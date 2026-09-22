using System.Diagnostics;
using System.Text.RegularExpressions;
using Factory.Cli.Models;
using Factory.Cli.Services;

namespace Factory.Cli.Commands;

/// <summary>
/// VALIDATE — "generated does not mean trusted" (slide 09). Objective gates, in order:
/// APPROVAL → TODO → CONTRACT → SECURITY → COMPILE → BEHAVIOR → SANDBOX (opt-in).
/// Every gate reports; the exit code is non-zero if any gate fails. No auto-fix — the human decides (mow2 P7f).
/// </summary>
public static class ValidateCommand
{
    private sealed record Gate(string Name, bool Passed, List<string> Findings);

    public static async Task<int> RunAsync(Workspace ws, string name, bool smoke, bool skipBuild)
    {
        var m = ws.LoadManifest(name);
        ConsoleUi.Step("VALIDATE", $"connector '{name}'");
        var gates = new List<Gate>();
        var outRoot = ws.Resolve(m.Generation.OutputRoot);
        var testRoot = ws.Resolve(m.Generation.TestRoot);
        var authored = AuthoredFiles(outRoot).Concat(AuthoredFiles(testRoot)).ToList();

        // APPROVAL — low-confidence semantic decisions were made by a human.
        {
            var findings = new List<string>();
            foreach (var op in m.Operations)
            {
                var p = ws.MappingPath(name, op.Name);
                if (!File.Exists(p)) { findings.Add($"{op.Name}: no mapping file"); continue; }
                findings.AddRange(OperationMapping.Load(p).PolicyViolations(m.Policy.MinAutoApproveConfidence));
            }
            gates.Add(new("APPROVAL", findings.Count == 0, findings));
        }

        // TODO — no unresolved TODO(ai) markers in authored code.
        {
            var findings = new List<string>();
            foreach (var f in authored)
            {
                var lines = File.ReadAllLines(f);
                for (var i = 0; i < lines.Length; i++)
                    if (lines[i].Contains("TODO(ai):")) findings.Add($"{ws.Relative(f)}:{i + 1} — {lines[i].Trim()}");
            }
            gates.Add(new("TODO", findings.Count == 0, findings));
        }

        // CONTRACT — only documented, manifest-allowed provider operations are called; every operation is implemented.
        {
            var findings = new List<string>();
            var allowed = new HashSet<string>(m.Operations.SelectMany(o => o.ProviderOperations).Select(id => SpecService.Pascal(id) + "Async"));
            var callRx = new Regex(@"_client\s*\.\s*([A-Za-z0-9_]+Async)\s*\(");
            var rawHttp = new Regex(@"\b(new\s+HttpClient|HttpRequestMessage|GetStringAsync|GetFromJsonAsync|PostAsJsonAsync)\b");
            foreach (var f in authored.Where(f => f.StartsWith(outRoot)))
            {
                var text = File.ReadAllText(f);
                foreach (Match mt in callRx.Matches(text))
                    if (!allowed.Contains(mt.Groups[1].Value))
                        findings.Add($"{ws.Relative(f)} calls undocumented/unallowed provider operation `{mt.Groups[1].Value}` (allowed: {string.Join(", ", allowed)})");
                foreach (Match mt in rawHttp.Matches(text))
                    findings.Add($"{ws.Relative(f)} bypasses the generated client (`{mt.Value}`) — all provider traffic goes through {SpecService.Pascal(name)}Client");
            }
            var connectorFile = Path.Combine(outRoot, "Connector", SpecService.Pascal(name) + "Connector.cs");
            if (File.Exists(connectorFile))
            {
                var text = File.ReadAllText(connectorFile);
                foreach (var op in m.Operations)
                    if (!text.Contains(SpecService.Pascal(op.Name) + "Async(")) findings.Add($"operation {op.Name} not present in {ws.Relative(connectorFile)}");
                    else if (Regex.IsMatch(text, @"NotImplementedException\(""TODO\(ai\): " + Regex.Escape(op.Name))) findings.Add($"operation {op.Name} still throws NotImplementedException");
            }
            else findings.Add($"connector not generated: {ws.Relative(connectorFile)}");
            gates.Add(new("CONTRACT", findings.Count == 0, findings));
        }

        // SECURITY — no secrets, no PII logging, no dependency additions outside the manifest.
        {
            var findings = new List<string>();
            var secretRx = new Regex(@"(?i)(api[_-]?key|secret|password|bearer\s+[a-z0-9\-_\.]{16,}|token)\s*[:=]\s*[""'][^""']{8,}[""']");
            var logRx = new Regex(@"Console\.Write(Line)?\(|\.LogInformation\(.*\b(source|response)\b");
            foreach (var f in authored)
            {
                var text = File.ReadAllText(f);
                foreach (Match mt in secretRx.Matches(text)) findings.Add($"{ws.Relative(f)} looks like a hard-coded credential: `{Truncate(mt.Value)}`");
                foreach (Match mt in logRx.Matches(text)) findings.Add($"{ws.Relative(f)} logs raw provider data: `{Truncate(mt.Value)}`");
            }
            foreach (var csproj in Directory.EnumerateFiles(outRoot, "*.csproj"))
            {
                var pkgs = Regex.Matches(File.ReadAllText(csproj), @"PackageReference\s+Include=""([^""]+)""").Select(x => x.Groups[1].Value);
                var baseline = new[] { "Microsoft.Extensions.Http", "Microsoft.Extensions.Http.Resilience", "Microsoft.Extensions.Options" };
                foreach (var p in pkgs.Except(baseline)) findings.Add($"{ws.Relative(csproj)} adds package `{p}` not in the approved baseline");
            }
            gates.Add(new("SECURITY", findings.Count == 0, findings));
        }

        Report(gates);

        // COMPILE + BEHAVIOR — objective, via dotnet. Skipped only for --skip-build (explaining the gates without a toolchain).
        if (!skipBuild)
        {
            var (buildOk, buildOut) = await Exec("dotnet", $"build \"{outRoot}\" --nologo -v q", ws.Root);
            var (testBuildOk, testBuildOut) = buildOk ? await Exec("dotnet", $"build \"{testRoot}\" --nologo -v q", ws.Root) : (false, "(skipped — connector build failed)");
            gates.Add(new("COMPILE", buildOk && testBuildOk, ExtractErrors(buildOut + "\n" + testBuildOut)));
            Report(gates.TakeLast(1));

            if (buildOk && testBuildOk)
            {
                var (testOk, testOut) = await Exec("dotnet", $"test \"{testRoot}\" --no-build --nologo -v q --filter \"Category!=Smoke\"", ws.Root);
                gates.Add(new("BEHAVIOR", testOk, ExtractTestFailures(testOut)));
                Report(gates.TakeLast(1));

                if (smoke)
                {
                    var (smokeOk, smokeOut) = await Exec("dotnet", $"test \"{testRoot}\" --no-build --nologo -v q --filter \"Category=Smoke\"", ws.Root);
                    gates.Add(new("SANDBOX", smokeOk, ExtractTestFailures(smokeOut)));
                    Report(gates.TakeLast(1));
                }
            }
        }
        else ConsoleUi.Warn("COMPILE / BEHAVIOR skipped (--skip-build)");

        Console.WriteLine();
        var failed = gates.Where(g => !g.Passed).ToList();
        if (failed.Count == 0)
        {
            ConsoleUi.Ok($"RELEASE POLICY SATISFIED — {gates.Count} gate(s) passed{(smoke ? "" : " (sandbox not run; add --smoke)")}");
            return 0;
        }
        ConsoleUi.Fail($"RELEASE BLOCKED — failing gates: {string.Join(", ", failed.Select(g => g.Name))}");
        ConsoleUi.Info("No auto-fix. Hand the findings above back to the implementing agent, or fix diff-scoped by hand (Remediation Ladder step 1).");
        return 3;
    }

    private static IEnumerable<string> AuthoredFiles(string root)
    {
        if (!Directory.Exists(root)) return Enumerable.Empty<string>();
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Generated{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
    }

    private static void Report(IEnumerable<Gate> gates)
    {
        foreach (var g in gates)
        {
            if (g.Passed) ConsoleUi.Ok($"{g.Name,-9} pass");
            else { ConsoleUi.Fail($"{g.Name,-9} FAIL ({g.Findings.Count})"); foreach (var f in g.Findings.Take(12)) ConsoleUi.Info("• " + f); if (g.Findings.Count > 12) ConsoleUi.Info($"• … {g.Findings.Count - 12} more"); }
        }
    }

    private static async Task<(bool ok, string output)> Exec(string file, string args, string cwd)
    {
        var psi = new ProcessStartInfo(file, args) { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (p.ExitCode == 0, await stdout + "\n" + await stderr);
    }

    private static List<string> ExtractErrors(string output) =>
        output.Split('\n').Where(l => l.Contains("error ", StringComparison.OrdinalIgnoreCase)).Select(l => l.Trim()).Distinct().Take(20).ToList();

    private static List<string> ExtractTestFailures(string output) =>
        output.Split('\n').Where(l => l.TrimStart().StartsWith("Failed ") || l.Contains("[FAIL]") || l.Contains("Error Message", StringComparison.OrdinalIgnoreCase) || l.TrimStart().StartsWith("Assert."))
              .Select(l => l.Trim()).Distinct().Take(20).ToList();

    private static string Truncate(string s) => s.Length > 60 ? s[..60] + "…" : s;
}

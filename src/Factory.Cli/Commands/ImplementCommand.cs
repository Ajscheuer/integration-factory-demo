using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Factory.Cli.Models;
using Factory.Cli.Services;

namespace Factory.Cli.Commands;

/// <summary>
/// AI IMPLEMENT — assemble the smallest complete package for one operation (slide 08) and hand it to the
/// implementing agent. The CLI decides *what the model sees*; the model only fills bounded TODO(ai) blocks.
/// </summary>
public static class ImplementCommand
{
    public static async Task<int> RunAsync(Workspace ws, string name, string operation)
    {
        var m = ws.LoadManifest(name);
        var op = m.Operations.FirstOrDefault(o => o.Name == operation);
        if (op is null) { ConsoleUi.Fail($"operation '{operation}' not in manifest"); return 1; }
        var mappingPath = ws.MappingPath(name, operation);
        if (!File.Exists(mappingPath)) { ConsoleUi.Fail("no mapping file — run map first"); return 1; }
        var mapping = OperationMapping.Load(mappingPath);
        var violations = mapping.PolicyViolations(m.Policy.MinAutoApproveConfidence).ToList();
        if (violations.Count > 0) { foreach (var v in violations) ConsoleUi.Fail(v); ConsoleUi.Info("Approve the mapping before implementing."); return 2; }

        ConsoleUi.Step("AI IMPLEMENT", $"{name}/{operation}");
        var provider = await SpecService.LoadAsync(ws.Resolve(m.Provider.Spec));
        var product = await SpecService.LoadAsync(ws.Resolve(m.Product.Spec));
        var model = GenerationModel.Build(ws, m, provider, product, new Dictionary<string, OperationMapping> { [operation] = mapping });
        var gop = model.Operations.First(o => o.Name == operation);
        var mapper = model.Mappers.First(x => x.Operation == operation || x.TargetSchema == op.CanonicalModel);

        var outRoot = m.Generation.OutputRoot;
        var testRoot = m.Generation.TestRoot;
        var connectorFile = $"{outRoot}/Connector/{model.ConnectorClass}.cs";
        var mapperFile = $"{outRoot}/Connector/{model.MapperClass}.cs";
        var testFile = $"{testRoot}/Connector/{model.Pascal}MapperTests.cs";
        var clientFile = $"{outRoot}/Generated/{model.ClientClass}.g.cs";

        var sb = new StringBuilder();
        sb.AppendLine($"# Implementation brief — {name} / {operation}");
        sb.AppendLine();
        sb.AppendLine("You are completing **bounded TODO(ai) blocks** in an already-generated connector. Read only the files named here.");
        sb.AppendLine("Do not restructure, add endpoints, add packages, or touch anything under `Generated/`.");
        sb.AppendLine();

        sb.AppendLine("## 1. Product contract (what we must return)");
        var prodOp = SpecService.FindOperation(product, op.ProductOperation);
        sb.AppendLine(SpecService.RenderOperation(product, prodOp));
        foreach (var s in SpecService.ReferencedSchemas(product, SpecService.FindSchema(product, op.CanonicalModel)))
            sb.AppendLine(SpecService.RenderSchema(product, s, SpecService.FindSchema(product, s)));

        sb.AppendLine("## 2. Provider contract — ONLY the endpoints this operation may call");
        var provSchemas = new HashSet<string>();
        foreach (var pid in op.ProviderOperations)
        {
            var po = SpecService.FindOperation(provider, pid);
            sb.AppendLine(SpecService.RenderOperation(provider, po));
            foreach (var s in SpecService.ReferencedSchemas(provider, SpecService.SuccessSchema(po))) provSchemas.Add(s);
        }
        foreach (var s in provSchemas) sb.AppendLine(SpecService.RenderSchema(provider, s, SpecService.FindSchema(provider, s)));
        sb.AppendLine("Generated client methods you may call (from `" + clientFile + "`):");
        foreach (var sig in ClientSignatures(ws.Resolve(clientFile), gop.ProviderCalls.Select(c => c.ClientMethod)))
            sb.AppendLine("- `" + sig + "`");
        sb.AppendLine();

        sb.AppendLine("## 3. Approved mapping (the semantic decisions — follow them literally)");
        sb.AppendLine("```json");
        sb.AppendLine(JsonSerializer.Serialize(mapping, ConnectorManifest.JsonOptions));
        sb.AppendLine("```");

        sb.AppendLine("## 4. Engineering policy");
        var policy = ws.ReadPolicy(m);
        sb.AppendLine(MapCommand.ExtractSection(policy, "Implementation"));
        sb.AppendLine(MapCommand.ExtractSection(policy, "Security"));

        sb.AppendLine("## 5. Target files and the exact blocks to fill");
        AppendTodoBlocks(ws, sb, connectorFile, gop.MethodName);
        AppendTodoBlocks(ws, sb, mapperFile, mapper.Name);
        AppendTodoBlocks(ws, sb, testFile, mapper.Name);
        sb.AppendLine($"Fixture for tests: `{testRoot}/Fixtures/{mapper.SourceSchema}.json`");
        sb.AppendLine();

        sb.AppendLine("## 6. Done when");
        sb.AppendLine($"- `integration-cli validate {name}` passes: build, tests, no `TODO(ai)` left in the blocks above, no client call outside `{string.Join(", ", gop.ProviderCalls.Select(c => c.ClientMethod))}`.");
        sb.AppendLine("- Shared helpers (parsing sentinels, id-from-url, etc.) are `private static` in the mapper — no new files, no new packages.");
        sb.AppendLine("- Return ≤150 words: outcome, files changed, anything you could not honour. No code in the reply, no narrative.");
        sb.AppendLine("- State your exact model as `model: <id>` on the first line.");

        Directory.CreateDirectory(ws.SessionDir);
        var briefPath = Path.Combine(ws.SessionDir, $"implement-{name}-{operation}.md");
        File.WriteAllText(briefPath, sb.ToString());

        var tokens = MapCommand.EstimateTokens(sb.ToString());
        ConsoleUi.Ok($"brief {ws.Relative(briefPath)} (~{tokens:N0} tokens)");
        var specTokens = MapCommand.EstimateTokens(File.ReadAllText(ws.Resolve(m.Provider.Spec)) + File.ReadAllText(ws.Resolve(m.Product.Spec)));
        var clientTokens = MapCommand.EstimateTokens(File.Exists(ws.Resolve(clientFile)) ? File.ReadAllText(ws.Resolve(clientFile)) : "");
        ConsoleUi.Info($"for comparison: both full specs ≈ {specTokens:N0} tokens, generated client ≈ {clientTokens:N0} tokens — none of it is sent");
        Console.WriteLine();
        ConsoleUi.Info("Hand it to the implementing agent (VS Code Copilot agent mode):");
        ConsoleUi.Info($"  @connector-agent Implement {name}/{operation} using {ws.Relative(briefPath)}");
        ConsoleUi.Info($"Then: integration-cli validate {name}");
        return 0;
    }

    private static IEnumerable<string> ClientSignatures(string clientPath, IEnumerable<string> methods)
    {
        if (!File.Exists(clientPath)) yield break;
        var text = File.ReadAllText(clientPath);
        foreach (var method in methods.Distinct())
        {
            // interface declarations: "System.Threading.Tasks.Task<Person> GetPersonAsync(int id, CancellationToken cancellationToken);"
            var rx = new Regex(@"Task<[^>]+>\s+" + Regex.Escape(method) + @"\((?:[^()]|\([^()]*\))*\);", RegexOptions.Multiline);
            var hits = rx.Matches(text).Select(x => Regex.Replace(x.Value, @"\s+", " ")).Distinct();
            foreach (var h in hits) yield return h;
        }
    }

    private static void AppendTodoBlocks(Workspace ws, StringBuilder sb, string relFile, string anchor)
    {
        var full = ws.Resolve(relFile);
        sb.AppendLine($"### `{relFile}` — block(s) mentioning `{anchor}`");
        if (!File.Exists(full)) { sb.AppendLine("(file not generated yet — run generate)"); sb.AppendLine(); return; }
        var lines = File.ReadAllLines(full);
        var decl = new Regex(@"\b" + Regex.Escape(anchor) + @"\s*\(");
        var any = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (!decl.IsMatch(lines[i]) || lines[i].TrimStart().StartsWith("//")) continue;
            // method body: from the declaration to the closing brace at the declaration's indentation
            var indent = lines[i].Length - lines[i].TrimStart().Length;
            var end = i;
            for (var j = i + 1; j < lines.Length; j++)
            {
                if (lines[j].Trim() == "}" && lines[j].Length - lines[j].TrimStart().Length == indent) { end = j; break; }
            }
            var body = lines.Skip(i).Take(end - i + 1).ToArray();
            if (!body.Any(l => l.Contains("TODO(ai)"))) { i = end; continue; }
            var start = Math.Max(0, i - 3);
            sb.AppendLine("```csharp");
            for (var j = start; j <= end; j++) sb.AppendLine(lines[j]);
            sb.AppendLine("```");
            any = true;
            i = end;
        }
        if (!any) sb.AppendLine("(no TODO(ai) block found — already implemented?)");
        sb.AppendLine();
    }

    private static IEnumerable<string> Window(string[] lines, int center, int radius)
    {
        for (var j = Math.Max(0, center - radius); j <= Math.Min(lines.Length - 1, center + radius); j++) yield return lines[j];
    }
}

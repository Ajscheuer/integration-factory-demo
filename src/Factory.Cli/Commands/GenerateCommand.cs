using System.Text.Json;
using Factory.Cli.Models;
using Factory.Cli.Services;
using Scriban;
using Scriban.Runtime;

namespace Factory.Cli.Commands;

/// <summary>
/// GENERATE — create interfaces, DI wiring, connector/mapper skeletons and test shells (journey step 4).
/// Two ownership zones (slide 05): Generated/ is overwritten every run; Connector/ and tests/Connector are
/// scaffolded once and never touched again. Refuses to run on unapproved low-confidence mappings.
/// </summary>
public static class GenerateCommand
{
    public sealed record Outcome(List<string> Created, List<string> Overwritten, List<string> Skipped, List<string> Warnings);

    public static async Task<int> RunAsync(Workspace ws, string name, bool allowPending)
    {
        var m = ws.LoadManifest(name);
        ConsoleUi.Step("GENERATE", $"connector '{name}'");
        var provider = await SpecService.LoadAsync(ws.Resolve(m.Provider.Spec));
        var product = await SpecService.LoadAsync(ws.Resolve(m.Product.Spec));

        // Approval gate: generation consumes only approved mappings.
        var mappings = new Dictionary<string, OperationMapping>();
        var violations = new List<string>();
        foreach (var op in m.Operations)
        {
            var p = ws.MappingPath(name, op.Name);
            if (!File.Exists(p)) { violations.Add($"{op.Name}: no mapping file (run `integration-cli map {name}`)"); continue; }
            var map = OperationMapping.Load(p);
            mappings[op.Name] = map;
            violations.AddRange(map.PolicyViolations(m.Policy.MinAutoApproveConfidence));
        }
        if (violations.Count > 0)
        {
            foreach (var v in violations) (allowPending ? (Action<string>)ConsoleUi.Warn : ConsoleUi.Fail)(v);
            if (!allowPending)
            {
                ConsoleUi.Info("Generation blocked by release policy: no unapproved low-confidence mappings (slide 09). Use --allow-pending only for scaffolding experiments.");
                return 2;
            }
        }
        else ConsoleUi.Ok($"{mappings.Count} approved mapping(s)");

        var model = GenerationModel.Build(ws, m, provider, product, mappings);
        var outcome = new Outcome(new(), new(), new(), new());
        var outRoot = ws.Resolve(m.Generation.OutputRoot);
        var testRoot = ws.Resolve(m.Generation.TestRoot);

        var ctxExtras = new Dictionary<string, object?>
        {
            ["CanonicalProjectRef"] = Path.GetRelativePath(outRoot, ws.Resolve(m.Generation.CanonicalProject) + $"/{m.Generation.CanonicalNamespace}.csproj").Replace('/', '\\'),
            ["ConnectorProjectRef"] = Path.GetRelativePath(testRoot, outRoot + $"/{m.Generation.Namespace}.csproj").Replace('/', '\\'),
        };

        // CLI-owned (always overwritten)
        Render(ws, model, ctxExtras, "Generated/IConnector.cs.scriban", $"{outRoot}/Generated/{model.ConnectorInterface}.g.cs", overwrite: true, outcome);
        Render(ws, model, ctxExtras, "Generated/ServiceCollectionExtensions.cs.scriban", $"{outRoot}/Generated/{model.Pascal}ServiceCollectionExtensions.g.cs", overwrite: true, outcome);
        Render(ws, model, ctxExtras, "Tests/ContractTests.cs.scriban", $"{testRoot}/Generated/{model.Pascal}ContractTests.g.cs", overwrite: true, outcome);
        Render(ws, model, ctxExtras, "Tests/SmokeTests.cs.scriban", $"{testRoot}/Generated/{model.Pascal}SmokeTests.g.cs", overwrite: true, outcome);

        // Scaffolded once (AI + human owned)
        Render(ws, model, ctxExtras, "Project/Connector.csproj.scriban", $"{outRoot}/{m.Generation.Namespace}.csproj", overwrite: false, outcome);
        Render(ws, model, ctxExtras, "Project/Tests.csproj.scriban", $"{testRoot}/{model.TestNamespace}.csproj", overwrite: false, outcome);
        Render(ws, model, ctxExtras, "Connector/Connector.cs.scriban", $"{outRoot}/Connector/{model.ConnectorClass}.cs", overwrite: false, outcome);
        Render(ws, model, ctxExtras, "Connector/Mapper.cs.scriban", $"{outRoot}/Connector/{model.MapperClass}.cs", overwrite: false, outcome);
        Render(ws, model, ctxExtras, "Tests/MapperTests.cs.scriban", $"{testRoot}/Connector/{model.Pascal}MapperTests.cs", overwrite: false, outcome);
        Directory.CreateDirectory($"{testRoot}/Fixtures");

        foreach (var mp in model.Mappers)
        {
            var fx = $"{testRoot}/Fixtures/{mp.SourceSchema}.json";
            if (!File.Exists(fx)) outcome.Warnings.Add($"fixture missing: {ws.Relative(fx)} — record a real {mp.SourceSchema} response there for the behavioral tests");
        }

        // Generation manifest (mirrors backend-codegen-manifest.json in mow2): what happened, for the next agent to read in one go.
        Directory.CreateDirectory(ws.SessionDir);
        var manifestPath = Path.Combine(ws.SessionDir, $"generate-{name}.manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new
        {
            connector = name,
            generatedAtUtc = model.GeneratedAtUtc,
            filesCreated = outcome.Created,
            filesOverwritten = outcome.Overwritten,
            filesSkipped = outcome.Skipped,
            warnings = outcome.Warnings,
            allowedClientMethods = model.AllowedClientMethods,
            todoMarkers = outcome.Created.Concat(outcome.Skipped).Where(f => f.EndsWith(".cs") && !f.Contains("/Generated/")).ToList(),
            operations = model.Operations.Select(o => new { o.Name, o.MethodName, o.ReturnType, mapper = o.MapperMethod, providerCalls = o.ProviderCalls.Select(c => c.ClientMethod) }),
        }, ConnectorManifest.JsonOptions));

        Console.WriteLine();
        foreach (var w in outcome.Warnings) ConsoleUi.Warn(w);
        ConsoleUi.Ok($"manifest {ws.Relative(manifestPath)}");
        ConsoleUi.Info($"created {outcome.Created.Count}, regenerated {outcome.Overwritten.Count}, left untouched {outcome.Skipped.Count}");
        ConsoleUi.Info($"Next: `integration-cli ai implement {name} --operation <op>` to build the bounded AI brief.");
        return 0;
    }

    private static void Render(Workspace ws, GenerationModel model, Dictionary<string, object?> extras, string template, string target, bool overwrite, Outcome outcome)
    {
        var rel = ws.Relative(target);
        var exists = File.Exists(target);
        if (exists && !overwrite) { outcome.Skipped.Add(rel); ConsoleUi.Info($"skip  {rel} (exists — human/AI owned)"); return; }

        var templatePath = Path.Combine(ws.TemplatesDir, template.Replace('/', Path.DirectorySeparatorChar));
        var tpl = Template.Parse(File.ReadAllText(templatePath), templatePath);
        if (tpl.HasErrors) throw new InvalidOperationException($"template {template}: {string.Join("; ", tpl.Messages)}");

        var so = new ScriptObject();
        so.Import(model, renamer: member => member.Name);
        foreach (var (k, v) in extras) so[k] = v;
        var ctx = new TemplateContext { MemberRenamer = member => member.Name, EnableRelaxedMemberAccess = true };
        ctx.PushGlobal(so);
        var text = tpl.Render(ctx);

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, text);
        if (exists) { outcome.Overwritten.Add(rel); ConsoleUi.Ok($"regen {rel}"); }
        else { outcome.Created.Add(rel); ConsoleUi.Ok($"new   {rel}"); }
    }
}

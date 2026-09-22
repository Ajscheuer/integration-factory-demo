using System.Text;
using Factory.Cli.Models;
using Factory.Cli.Services;
using NSwag;

namespace Factory.Cli.Commands;

/// <summary>
/// MAP — assemble the smallest complete context for the AI to *propose* operation and field mappings
/// (journey step 3). The CLI never guesses semantics; it writes the brief and a pending mapping skeleton.
/// The mapping-agent fills the skeleton; `map --check` enforces the approval policy on what came back.
/// </summary>
public static class MapCommand
{
    public static async Task<int> RunAsync(Workspace ws, string name, string? operation, bool check)
    {
        var m = ws.LoadManifest(name);
        var ops = operation is null ? m.Operations : m.Operations.Where(o => o.Name == operation).ToList();
        if (ops.Count == 0) { ConsoleUi.Fail($"operation '{operation}' not in manifest"); return 1; }

        if (check) return Check(ws, m, ops);

        ConsoleUi.Step("MAP", $"connector '{name}' — {ops.Count} operation(s)");
        var provider = await SpecService.LoadAsync(ws.Resolve(m.Provider.Spec));
        var product = await SpecService.LoadAsync(ws.Resolve(m.Product.Spec));
        Directory.CreateDirectory(ws.SessionDir);
        Directory.CreateDirectory(ws.MappingDir(name));

        foreach (var op in ops)
        {
            var brief = BuildBrief(ws, m, op, provider, product);
            var briefPath = Path.Combine(ws.SessionDir, $"map-{name}-{op.Name}.md");
            File.WriteAllText(briefPath, brief);
            ConsoleUi.Ok($"brief  {ws.Relative(briefPath)} (~{EstimateTokens(brief):N0} tokens)");

            var mappingPath = ws.MappingPath(name, op.Name);
            if (!File.Exists(mappingPath))
            {
                var prodOp = SpecService.FindOperation(product, op.ProductOperation);
                var provOp = SpecService.FindOperation(provider, op.ProviderOperations[0]);
                new OperationMapping
                {
                    Operation = op.Name,
                    ProductOperation = op.ProductOperation,
                    ProviderOperation = $"{provOp.Method.ToString().ToUpperInvariant()} {provOp.Path}",
                    CanonicalModel = op.CanonicalModel,
                    Confidence = 0,
                    Approval = new() { Status = "pending" },
                }.Save(mappingPath);
                ConsoleUi.Ok($"skeleton {ws.Relative(mappingPath)} (pending — to be filled by the mapping-agent)");
            }
            else ConsoleUi.Info($"mapping exists: {ws.Relative(mappingPath)} (left untouched)");
        }

        Console.WriteLine();
        ConsoleUi.Info("Hand the brief(s) to the AI assistant, e.g. in VS Code Copilot agent mode:");
        foreach (var op in ops)
            ConsoleUi.Info($"  @mapping-agent Propose the mapping for {name}/{op.Name} using .factory/session/map-{name}-{op.Name}.md");
        ConsoleUi.Info($"Then: integration-cli map {name} --check");
        return 0;
    }

    private static int Check(Workspace ws, ConnectorManifest m, List<ConnectorManifest.OperationSpec> ops)
    {
        ConsoleUi.Step("MAP --check", $"connector '{m.Connector}' — policy threshold {m.Policy.MinAutoApproveConfidence:0.00}");
        var failures = 0;
        foreach (var op in ops)
        {
            var path = ws.MappingPath(m.Connector, op.Name);
            if (!File.Exists(path)) { ConsoleUi.Fail($"{op.Name}: no mapping file"); failures++; continue; }
            var map = OperationMapping.Load(path);
            var low = map.LowConfidence(m.Policy.MinAutoApproveConfidence).ToList();
            var violations = map.PolicyViolations(m.Policy.MinAutoApproveConfidence).ToList();
            if (map.FieldMappings.Count == 0) { ConsoleUi.Fail($"{op.Name}: mapping has no fieldMappings (agent has not run)"); failures++; continue; }
            Console.WriteLine($"  {op.Name}: {map.ProviderOperation}  confidence {map.Confidence:0.00}  fields {map.FieldMappings.Count}  low-confidence {low.Count}  status {map.Approval.Status}");
            foreach (var f in low)
                ConsoleUi.Info($"  • {f.Source} -> {f.Target} ({f.Confidence:0.00}){(map.Approval.Decisions.TryGetValue(f.Target, out var d) ? "  decision: " + d : "  HUMAN APPROVAL REQUIRED")}");
            foreach (var q in map.UnresolvedQuestions) ConsoleUi.Info($"  ? {q}");
            if (violations.Count == 0) ConsoleUi.Ok($"{op.Name}: approved, policy satisfied");
            else { foreach (var v in violations) ConsoleUi.Fail(v); failures++; }
        }
        if (failures > 0)
        {
            Console.WriteLine();
            ConsoleUi.Info($"Resolve with: integration-cli approve {m.Connector} --operation <op> --by \"<name>\" --decision <target>=\"<decision>\"");
        }
        return failures == 0 ? 0 : 2;
    }

    public static string BuildBrief(Workspace ws, ConnectorManifest m, ConnectorManifest.OperationSpec op, OpenApiDocument provider, OpenApiDocument product)
    {
        var sb = new StringBuilder();
        var prodOp = SpecService.FindOperation(product, op.ProductOperation);
        var canonical = SpecService.FindSchema(product, op.CanonicalModel);

        sb.AppendLine($"# Mapping brief — {m.Connector} / {op.Name}");
        sb.AppendLine();
        sb.AppendLine("You are proposing a **semantic mapping**, not writing code. Output ONLY the JSON described in *Deliverable*.");
        sb.AppendLine("Everything you need is in this file. Do not read the repository or the full specs.");
        sb.AppendLine();
        sb.AppendLine("## 1. Product contract (ours)");
        sb.AppendLine(SpecService.RenderOperation(product, prodOp));
        foreach (var s in SpecService.ReferencedSchemas(product, canonical))
            sb.AppendLine(SpecService.RenderSchema(product, s, SpecService.FindSchema(product, s)));

        sb.AppendLine("## 2. Provider contract (theirs) — only the endpoints this operation is allowed to call");
        sb.AppendLine($"Base URL: `{m.Provider.BaseUrl}`  Auth: `{m.Provider.Auth.Type}`  Pagination: `{m.Capabilities.Pagination}`");
        sb.AppendLine();
        var provSchemas = new HashSet<string>();
        foreach (var pid in op.ProviderOperations)
        {
            var po = SpecService.FindOperation(provider, pid);
            sb.AppendLine(SpecService.RenderOperation(provider, po));
            foreach (var s in SpecService.ReferencedSchemas(provider, SpecService.SuccessSchema(po))) provSchemas.Add(s);
        }
        foreach (var s in provSchemas) sb.AppendLine(SpecService.RenderSchema(provider, s, SpecService.FindSchema(provider, s)));

        sb.AppendLine("## 3. Engineering policy (mapping rules)");
        sb.AppendLine(ExtractSection(ws.ReadPolicy(m), "Mapping"));

        sb.AppendLine("## 4. Deliverable");
        sb.AppendLine($"Write `{ws.Relative(ws.MappingPath(m.Connector, op.Name))}` with exactly this shape:");
        sb.AppendLine("```json");
        sb.AppendLine("""
{
  "operation": "<op.Name>",
  "productOperation": "<product operationId>",
  "providerOperation": "<METHOD /path>",
  "canonicalModel": "<schema>",
  "confidence": 0.0,
  "fieldMappings": [
    { "source": "<provider field or expression>", "target": "<canonical field>", "confidence": 0.0,
      "transform": "<exact rule the implementation must follow>", "requiresHumanApproval": false, "notes": "<why>" }
  ],
  "unresolvedQuestions": ["<business ambiguity the human must settle>"],
  "approval": { "status": "pending", "decisions": {} },
  "proposedBy": "mapping-agent",
  "proposedAtUtc": "<ISO 8601>"
}
""");
        sb.AppendLine("```");
        sb.AppendLine("Rules:");
        sb.AppendLine("- Every **required** canonical field must appear as a target. Optional fields you cannot source get `\"source\": null`-style entries with `transform: \"leave null\"`.");
        sb.AppendLine($"- Confidence < {m.Policy.MinAutoApproveConfidence:0.00} or any business-meaning ambiguity ⇒ `requiresHumanApproval: true` **and** an entry in `unresolvedQuestions`.");
        sb.AppendLine("- Never invent provider fields. Never widen scope beyond the endpoints in section 2.");
        sb.AppendLine("- `transform` must be precise enough that two engineers would write the same code from it.");
        sb.AppendLine();
        sb.AppendLine("Return: the JSON only, then one line `model: <your model id>`.");
        return sb.ToString();
    }

    public static string ExtractSection(string markdown, string heading)
    {
        var lines = markdown.Split('\n');
        var sb = new StringBuilder();
        var inside = false;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("## "))
            {
                if (inside) break;
                inside = line[3..].Trim().StartsWith(heading, StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (inside) sb.AppendLine(line);
        }
        return sb.Length == 0 ? "(no section '" + heading + "' in policy)\n" : sb.ToString();
    }

    public static int EstimateTokens(string text) => (int)Math.Ceiling(text.Length / 4.0);
}

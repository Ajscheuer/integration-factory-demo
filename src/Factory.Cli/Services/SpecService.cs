using System.Text;
using NJsonSchema;
using NSwag;

namespace Factory.Cli.Services;

/// <summary>
/// Reads OpenAPI contracts (yaml or json) and exposes just enough structure for templates and briefs.
/// This is deterministic "spec-aware" behaviour (slide 05): the CLI understands contracts, the AI never has to.
/// </summary>
public static class SpecService
{
    public static async Task<OpenApiDocument> LoadAsync(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) throw new FileNotFoundException($"Spec not found: {full}");
        var ext = Path.GetExtension(full).ToLowerInvariant();
        return ext is ".yaml" or ".yml"
            ? await OpenApiYamlDocument.FromFileAsync(full)
            : await OpenApiDocument.FromFileAsync(full);
    }

    public static OpenApiOperationDescription FindOperation(OpenApiDocument doc, string operationId) =>
        doc.Operations.FirstOrDefault(o => string.Equals(o.Operation.OperationId, operationId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"operationId '{operationId}' not found in {doc.Info?.Title}.");

    public static JsonSchema FindSchema(OpenApiDocument doc, string name) =>
        doc.Components.Schemas.TryGetValue(name, out var s) ? s
        : throw new InvalidOperationException($"schema '{name}' not found in {doc.Info?.Title}.");

    /// <summary>Success (2xx) response schema of an operation, dereferenced. Null for void.</summary>
    public static JsonSchema? SuccessSchema(OpenApiOperationDescription op)
    {
        var resp = op.Operation.Responses.FirstOrDefault(r => r.Key.StartsWith("2")).Value;
        return resp?.Content?.Values.FirstOrDefault()?.Schema?.ActualSchema ?? resp?.Schema?.ActualSchema;
    }

    public static string SchemaName(OpenApiDocument doc, JsonSchema? schema)
    {
        if (schema is null) return "void";
        var actual = schema.ActualSchema;
        var hit = doc.Components.Schemas.FirstOrDefault(kv => ReferenceEquals(kv.Value.ActualSchema, actual));
        return hit.Key ?? "object";
    }

    // ---- C# type mapping (mirrors what NSwag emits for DTOs, so generated signatures line up) ----

    public static string CSharpType(OpenApiDocument doc, JsonSchema? schema, bool nullableOverride = false)
    {
        if (schema is null) return "void";
        var s = schema.ActualSchema;
        var nullable = nullableOverride || schema.IsNullable(SchemaType.OpenApi3);
        string core;
        if (s.Reference is not null || (s.Type == JsonObjectType.Object && doc.Components.Schemas.Values.Any(v => ReferenceEquals(v.ActualSchema, s))))
            core = SchemaName(doc, s);
        else if (s.IsEnumeration)
            core = SchemaName(doc, s);
        else
            core = s.Type switch
            {
                JsonObjectType.String when s.Format == "date-time" => "System.DateTimeOffset",
                JsonObjectType.String when s.Format == "date" => "System.DateTimeOffset",
                JsonObjectType.String => "string",
                JsonObjectType.Integer when s.Format == "int64" => "long",
                JsonObjectType.Integer => "int",
                JsonObjectType.Number => "double",
                JsonObjectType.Boolean => "bool",
                JsonObjectType.Array => $"System.Collections.Generic.ICollection<{CSharpType(doc, s.Item)}>",
                _ => "object",
            };
        // NSwag treats strings/objects as reference types that are nullable by declaration; value types need the '?'
        return nullable && !core.EndsWith('?') ? core + "?" : core;
    }

    public static string Pascal(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var parts = name.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var p in parts) sb.Append(char.ToUpperInvariant(p[0])).Append(p.AsSpan(1));
        return sb.ToString();
    }

    public static string Camel(string name)
    {
        var p = Pascal(name);
        return p.Length == 0 ? p : char.ToLowerInvariant(p[0]) + p[1..];
    }

    // ---- Compact markdown renderers for AI briefs (slide 08: smallest complete package) ----

    public static string RenderOperation(OpenApiDocument doc, OpenApiOperationDescription op)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"### `{op.Method.ToString().ToUpperInvariant()} {op.Path}`  (operationId: `{op.Operation.OperationId}`)");
        if (!string.IsNullOrWhiteSpace(op.Operation.Summary)) sb.AppendLine(op.Operation.Summary);
        if (op.Operation.Parameters.Count > 0)
        {
            sb.AppendLine("Parameters:");
            foreach (var p in op.Operation.Parameters)
                sb.AppendLine($"- `{p.Name}` ({p.Kind.ToString().ToLowerInvariant()}, {(p.IsRequired ? "required" : "optional")}, {CSharpType(doc, p.Schema ?? p)}){(string.IsNullOrWhiteSpace(p.Description) ? "" : " — " + p.Description.Trim())}");
        }
        foreach (var r in op.Operation.Responses)
        {
            var schema = r.Value.Content?.Values.FirstOrDefault()?.Schema ?? r.Value.Schema;
            sb.AppendLine($"Response `{r.Key}`: {(schema is null ? "(no body)" : "`" + SchemaName(doc, schema) + "`")}{(string.IsNullOrWhiteSpace(r.Value.Description) ? "" : " — " + r.Value.Description.Trim())}");
        }
        return sb.ToString();
    }

    public static string RenderSchema(OpenApiDocument doc, string name, JsonSchema schema)
    {
        var s = schema.ActualSchema;
        var sb = new StringBuilder();
        sb.AppendLine($"### `{name}`{(string.IsNullOrWhiteSpace(s.Description) ? "" : " — " + s.Description.Trim().Replace("\n", " "))}");
        if (s.IsEnumeration)
        {
            sb.AppendLine("enum: " + string.Join(", ", s.Enumeration.Select(e => $"`{e}`")));
            return sb.ToString();
        }
        sb.AppendLine("| field | type | required | notes |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var (pname, prop) in s.ActualProperties)
        {
            var req = s.RequiredProperties.Contains(pname) ? "yes" : "no";
            var notes = (prop.Description ?? "").Trim().Replace("\n", " ");
            if (prop.Example is not null) notes += $" (e.g. `{prop.Example}`)";
            sb.AppendLine($"| `{pname}` | `{CSharpType(doc, prop)}` | {req} | {notes} |");
        }
        return sb.ToString();
    }

    /// <summary>Names of component schemas reachable from a schema (for "only referenced models").</summary>
    public static IEnumerable<string> ReferencedSchemas(OpenApiDocument doc, JsonSchema? root)
    {
        var seen = new HashSet<string>();
        void Walk(JsonSchema? s)
        {
            if (s is null) return;
            var a = s.ActualSchema;
            var name = doc.Components.Schemas.FirstOrDefault(kv => ReferenceEquals(kv.Value.ActualSchema, a)).Key;
            if (name is not null && !seen.Add(name)) return;
            foreach (var p in a.ActualProperties.Values) Walk(p);
            Walk(a.Item);
        }
        Walk(root);
        return seen;
    }
}

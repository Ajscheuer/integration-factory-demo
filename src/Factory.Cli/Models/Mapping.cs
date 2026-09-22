using System.Text.Json;

namespace Factory.Cli.Models;

/// <summary>
/// The intermediate artifact (slide 07): the AI explains the mapping before it writes the implementation.
/// One file per operation under connectors/{name}/mapping/{operation}.mapping.json. Versioned, reviewable,
/// and the only thing generation reads about semantics.
/// </summary>
public sealed class OperationMapping
{
    public string Operation { get; set; } = "";
    public string ProductOperation { get; set; } = "";
    public string ProviderOperation { get; set; } = "";
    public string CanonicalModel { get; set; } = "";
    /// <summary>Overall confidence that the provider operation satisfies the product operation.</summary>
    public double Confidence { get; set; }
    public List<FieldMapping> FieldMappings { get; set; } = new();
    public List<string> UnresolvedQuestions { get; set; } = new();
    public ApprovalSection Approval { get; set; } = new();
    public string? ProposedBy { get; set; }
    public DateTime? ProposedAtUtc { get; set; }

    public sealed class FieldMapping
    {
        /// <summary>Provider field (JSON name) or expression, e.g. "birth_year" or "url (last path segment)".</summary>
        public string Source { get; set; } = "";
        /// <summary>Canonical field name on the product model.</summary>
        public string Target { get; set; } = "";
        public double Confidence { get; set; }
        /// <summary>Human-readable transformation rule the implementation must follow.</summary>
        public string? Transform { get; set; }
        public bool RequiresHumanApproval { get; set; }
        public string? Notes { get; set; }
    }

    public sealed class ApprovalSection
    {
        /// <summary>pending | approved | rejected</summary>
        public string Status { get; set; } = "pending";
        public string? ApprovedBy { get; set; }
        public DateTime? ApprovedAtUtc { get; set; }
        /// <summary>Human decisions keyed by target field. A low-confidence mapping is only usable with a decision here.</summary>
        public Dictionary<string, string> Decisions { get; set; } = new();
    }

    public static OperationMapping Load(string path) =>
        JsonSerializer.Deserialize<OperationMapping>(File.ReadAllText(path), ConnectorManifest.JsonOptions)
        ?? throw new InvalidOperationException($"Mapping {path} is empty or invalid.");

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, ConnectorManifest.JsonOptions) + Environment.NewLine);

    /// <summary>Mappings that the release policy says a human must decide on.</summary>
    public IEnumerable<FieldMapping> LowConfidence(double threshold) =>
        FieldMappings.Where(f => f.RequiresHumanApproval || f.Confidence < threshold);

    public IEnumerable<string> PolicyViolations(double threshold)
    {
        if (!string.Equals(Approval.Status, "approved", StringComparison.OrdinalIgnoreCase))
            yield return $"{Operation}: approval.status is '{Approval.Status}' (must be 'approved').";

        foreach (var f in LowConfidence(threshold))
            if (!Approval.Decisions.ContainsKey(f.Target))
                yield return $"{Operation}: '{f.Source}' -> '{f.Target}' (confidence {f.Confidence:0.00}) has no human decision in approval.decisions.";

        if (UnresolvedQuestions.Count > 0 && Approval.Decisions.Count == 0)
            yield return $"{Operation}: {UnresolvedQuestions.Count} unresolved question(s) and no recorded decisions.";
    }
}

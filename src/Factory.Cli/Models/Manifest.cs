using System.Text.Json;
using System.Text.Json.Serialization;

namespace Factory.Cli.Models;

/// <summary>
/// The connector manifest: the single input the CLI owns. Everything deterministic
/// (layout, interfaces, client, test shells) is derived from it. See slide 05 "Manifest-driven".
/// </summary>
public sealed class ConnectorManifest
{
    public string Connector { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public ProviderSection Provider { get; set; } = new();
    public ProductSection Product { get; set; } = new();
    public List<OperationSpec> Operations { get; set; } = new();
    public CapabilitiesSection Capabilities { get; set; } = new();
    public GenerationSection Generation { get; set; } = new();
    public PolicySection Policy { get; set; } = new();

    public sealed class ProviderSection
    {
        public string Spec { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        public AuthSection Auth { get; set; } = new();
    }

    public sealed class AuthSection
    {
        /// <summary>none | apiKey | oauth2-client-credentials | bearer</summary>
        public string Type { get; set; } = "none";
        /// <summary>Name of the configuration key / secret that holds the credential. Never the value.</summary>
        public string? SecretName { get; set; }
    }

    public sealed class ProductSection
    {
        public string Spec { get; set; } = "";
    }

    public sealed class OperationSpec
    {
        /// <summary>Connector method name (camelCase). Usually equals the product operationId.</summary>
        public string Name { get; set; } = "";
        /// <summary>operationId in the product spec.</summary>
        public string ProductOperation { get; set; } = "";
        /// <summary>operationIds in the provider spec this operation is allowed to call. Anything else is a CONTRACT gate failure.</summary>
        public List<string> ProviderOperations { get; set; } = new();
        /// <summary>Canonical model (schema name in the product spec) returned by this operation.</summary>
        public string CanonicalModel { get; set; } = "";
        public string? Description { get; set; }
        /// <summary>C# literal arguments for the sandbox smoke test, e.g. ["\"1\""]. Empty = no smoke test for this operation.</summary>
        public List<string> SmokeArgs { get; set; } = new();
    }

    public sealed class CapabilitiesSection
    {
        public string Pagination { get; set; } = "none";
        public string? RateLimit { get; set; }
        public bool Retries { get; set; } = true;
        public bool Webhooks { get; set; } = false;
    }

    public sealed class GenerationSection
    {
        public string Namespace { get; set; } = "";
        public string OutputRoot { get; set; } = "";
        public string TestRoot { get; set; } = "";
        public string CanonicalProject { get; set; } = "src/Product.Canonical";
        public string CanonicalNamespace { get; set; } = "Product.Canonical";
    }

    public sealed class PolicySection
    {
        /// <summary>Field mappings below this confidence require an explicit human decision before generation.</summary>
        public double MinAutoApproveConfidence { get; set; } = 0.8;
        public string PolicyFile { get; set; } = "policy/engineering-policy.md";
    }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static ConnectorManifest Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ConnectorManifest>(json, JsonOptions)
               ?? throw new InvalidOperationException($"Manifest {path} is empty or invalid.");
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions) + Environment.NewLine);
}

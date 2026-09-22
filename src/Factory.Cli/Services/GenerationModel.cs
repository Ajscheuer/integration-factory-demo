using Factory.Cli.Models;
using NSwag;

namespace Factory.Cli.Services;

/// <summary>Everything the Scriban templates see. Built once per generate run from manifest + specs + approved mappings.</summary>
public sealed class GenerationModel
{
    public string Connector { get; init; } = "";
    public string Pascal { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Namespace { get; init; } = "";
    public string GeneratedNamespace => Namespace + ".Generated";
    public string CanonicalNamespace { get; init; } = "";
    public string ClientInterface => "I" + Pascal + "Client";
    public string ClientClass => Pascal + "Client";
    public string ConnectorInterface => "I" + Pascal + "Connector";
    public string ConnectorClass => Pascal + "Connector";
    public string MapperClass => Pascal + "Mapper";
    public string OptionsClass => Pascal + "ConnectorOptions";
    public string BaseUrl { get; init; } = "";
    public string AuthType { get; init; } = "none";
    public string? SecretName { get; init; }
    public string Pagination { get; init; } = "none";
    public bool Retries { get; init; }
    public string TestNamespace { get; init; } = "";
    public double MinConfidence { get; init; }
    public string GeneratedAtUtc { get; init; } = DateTime.UtcNow.ToString("O");
    public List<Operation> Operations { get; init; } = new();
    public List<MapperMethod> Mappers { get; init; } = new();
    public List<string> AllowedClientMethods => Operations.SelectMany(o => o.ProviderCalls.Select(p => p.ClientMethod)).Distinct().ToList();

    public sealed class Operation
    {
        public string Name { get; init; } = "";
        public string MethodName { get; init; } = "";
        public string Description { get; init; } = "";
        public string ReturnType { get; init; } = "";
        public string CanonicalModel { get; init; } = "";
        public List<Param> Params { get; init; } = new();
        public string ParamList => string.Join(", ", Params.Select(p => $"{p.Type} {p.Name}"));
        public string ArgList => string.Join(", ", Params.Select(p => p.Name));
        public List<ProviderCall> ProviderCalls { get; init; } = new();
        public OperationMapping? Mapping { get; init; }
        public string MapperMethod { get; init; } = "";
        public List<string> SmokeArgs { get; init; } = new();
        public string SmokeArgList => string.Join(", ", SmokeArgs);
    }

    public sealed class Param
    {
        public string Name { get; init; } = "";
        public string Type { get; init; } = "";
        public bool Required { get; init; }
    }

    public sealed class ProviderCall
    {
        public string OperationId { get; init; } = "";
        public string Method { get; init; } = "";
        public string Path { get; init; } = "";
        public string ClientMethod { get; init; } = "";
        public string ResponseType { get; init; } = "";
        public string ResponseSchema { get; init; } = "";
    }

    public sealed class MapperMethod
    {
        public string Name { get; init; } = "";
        public string SourceType { get; init; } = "";
        public string SourceSchema { get; init; } = "";
        public string TargetType { get; init; } = "";
        public string TargetSchema { get; init; } = "";
        public string Operation { get; init; } = "";
        public List<OperationMapping.FieldMapping> Fields { get; init; } = new();
        public Dictionary<string, string> Decisions { get; init; } = new();
    }

    public static GenerationModel Build(Workspace ws, ConnectorManifest m, OpenApiDocument provider, OpenApiDocument product, IReadOnlyDictionary<string, OperationMapping> mappings)
    {
        var pascal = SpecService.Pascal(m.Connector);
        var model = new GenerationModel
        {
            Connector = m.Connector,
            Pascal = pascal,
            DisplayName = m.DisplayName,
            Namespace = m.Generation.Namespace,
            CanonicalNamespace = m.Generation.CanonicalNamespace,
            BaseUrl = m.Provider.BaseUrl,
            AuthType = m.Provider.Auth.Type,
            SecretName = m.Provider.Auth.SecretName,
            Pagination = m.Capabilities.Pagination,
            Retries = m.Capabilities.Retries,
            TestNamespace = m.Generation.Namespace + ".Tests",
            MinConfidence = m.Policy.MinAutoApproveConfidence,
        };

        var mapperByModel = new Dictionary<string, MapperMethod>();
        foreach (var op in m.Operations)
        {
            var prodOp = SpecService.FindOperation(product, op.ProductOperation);
            var canonicalType = $"{m.Generation.CanonicalNamespace}.{op.CanonicalModel}";
            var calls = op.ProviderOperations.Select(pid =>
            {
                var po = SpecService.FindOperation(provider, pid);
                var schema = SpecService.SuccessSchema(po);
                var schemaName = SpecService.SchemaName(provider, schema);
                return new ProviderCall
                {
                    OperationId = pid,
                    Method = po.Method.ToString().ToUpperInvariant(),
                    Path = po.Path,
                    ClientMethod = SpecService.Pascal(pid) + "Async",
                    ResponseSchema = schemaName,
                    ResponseType = $"{model.GeneratedNamespace}.{schemaName}",
                };
            }).ToList();

            mappings.TryGetValue(op.Name, out var mapping);
            var mapperName = "To" + op.CanonicalModel;
            if (!mapperByModel.ContainsKey(op.CanonicalModel))
            {
                mapperByModel[op.CanonicalModel] = new MapperMethod
                {
                    Name = mapperName,
                    SourceType = calls[0].ResponseType,
                    SourceSchema = calls[0].ResponseSchema,
                    TargetType = canonicalType,
                    TargetSchema = op.CanonicalModel,
                    Operation = op.Name,
                    Fields = mapping?.FieldMappings ?? new(),
                    Decisions = mapping?.Approval.Decisions ?? new(),
                };
            }

            model.Operations.Add(new Operation
            {
                Name = op.Name,
                MethodName = SpecService.Pascal(op.Name) + "Async",
                Description = op.Description ?? prodOp.Operation.Summary ?? "",
                ReturnType = canonicalType,
                CanonicalModel = op.CanonicalModel,
                Params = prodOp.Operation.Parameters.Select(p => new Param
                {
                    Name = SpecService.Camel(p.Name),
                    Type = SpecService.CSharpType(product, p.Schema ?? p, nullableOverride: !p.IsRequired),
                    Required = p.IsRequired,
                }).ToList(),
                ProviderCalls = calls,
                Mapping = mapping,
                MapperMethod = mapperName,
                SmokeArgs = op.SmokeArgs,
            });
        }
        model.Mappers.AddRange(mapperByModel.Values);
        return model;
    }
}

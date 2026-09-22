using Factory.Cli.Models;
using Factory.Cli.Services;

namespace Factory.Cli.Commands;

/// <summary>INIT — create the connector manifest (journey step 1).</summary>
public static class InitCommand
{
    public static int Run(Workspace ws, string name, string providerSpec, string productSpec, string baseUrl, bool force)
    {
        var path = ws.ManifestPath(name);
        if (File.Exists(path) && !force)
        {
            ConsoleUi.Fail($"Manifest already exists: {ws.Relative(path)} (use --force to overwrite)");
            return 1;
        }

        var manifest = new ConnectorManifest
        {
            Connector = name,
            DisplayName = SpecService.Pascal(name),
            Provider = new() { Spec = providerSpec, BaseUrl = baseUrl, Auth = new() { Type = "none" } },
            Product = new() { Spec = productSpec },
            Operations = new(),
            Capabilities = new() { Pagination = "none", Retries = true },
            Generation = new()
            {
                Namespace = $"Connectors.{SpecService.Pascal(name)}",
                OutputRoot = $"src/Connectors.{SpecService.Pascal(name)}",
                TestRoot = $"tests/Connectors.{SpecService.Pascal(name)}.Tests",
            },
            Policy = new(),
        };

        Directory.CreateDirectory(ws.ConnectorDir(name));
        Directory.CreateDirectory(ws.MappingDir(name));
        manifest.Save(path);

        ConsoleUi.Step("INIT", $"connector '{name}'");
        ConsoleUi.Ok($"wrote {ws.Relative(path)}");
        ConsoleUi.Info("Next: add operations[] to the manifest (product operationId -> allowed provider operationIds), then run `integration-cli import " + name + "`.");
        return 0;
    }
}

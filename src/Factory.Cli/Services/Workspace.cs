using Factory.Cli.Models;

namespace Factory.Cli.Services;

/// <summary>Repo-relative path conventions. Everything the CLI writes is addressed from the repo root.</summary>
public sealed class Workspace
{
    public string Root { get; }
    public Workspace(string root) => Root = root;

    public static Workspace Discover(string? start = null)
    {
        var dir = new DirectoryInfo(start ?? Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (Directory.Exists(System.IO.Path.Combine(dir.FullName, "connectors")) || Directory.Exists(System.IO.Path.Combine(dir.FullName, ".git")))
                return new Workspace(dir.FullName);
            dir = dir.Parent;
        }
        return new Workspace(Directory.GetCurrentDirectory());
    }

    public string Resolve(string relative) => System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, relative));
    public string Relative(string full) => System.IO.Path.GetRelativePath(Root, full).Replace('\\', '/');

    public string ConnectorDir(string name) => Resolve($"connectors/{name}");
    public string ManifestPath(string name) => Resolve($"connectors/{name}/connector.manifest.json");
    public string MappingDir(string name) => Resolve($"connectors/{name}/mapping");
    public string MappingPath(string name, string operation) => Resolve($"connectors/{name}/mapping/{operation}.mapping.json");
    public string SessionDir => Resolve(".factory/session");

    public ConnectorManifest LoadManifest(string name)
    {
        var p = ManifestPath(name);
        if (!File.Exists(p)) throw new FileNotFoundException($"No manifest for connector '{name}'. Run: integration-cli init {name}", p);
        return ConnectorManifest.Load(p);
    }

    public string TemplatesDir => System.IO.Path.Combine(AppContext.BaseDirectory, "Templates");

    public string ReadPolicy(ConnectorManifest m)
    {
        var p = Resolve(m.Policy.PolicyFile);
        return File.Exists(p) ? File.ReadAllText(p) : "(no engineering policy file found)";
    }
}

public static class ConsoleUi
{
    public static void Step(string tag, string message) => Console.WriteLine($"[{tag}] {message}");
    public static void Ok(string message) { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine("  ✓ " + message); Console.ResetColor(); }
    public static void Warn(string message) { Console.ForegroundColor = ConsoleColor.Yellow; Console.WriteLine("  ! " + message); Console.ResetColor(); }
    public static void Fail(string message) { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine("  ✗ " + message); Console.ResetColor(); }
    public static void Info(string message) => Console.WriteLine("    " + message);
}

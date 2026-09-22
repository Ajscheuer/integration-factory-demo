using Factory.Cli.Models;
using Factory.Cli.Services;

namespace Factory.Cli.Commands;

/// <summary>
/// HUMAN APPROVAL — record the engineer's decision on a proposed mapping. This is the gate on slide 06:
/// "Required when mappings are ambiguous or confidence is below policy." Decisions are versioned with the mapping.
/// </summary>
public static class ApproveCommand
{
    public static int Run(Workspace ws, string name, string operation, string by, string[] decisions, bool reject)
    {
        var m = ws.LoadManifest(name);
        var path = ws.MappingPath(name, operation);
        if (!File.Exists(path)) { ConsoleUi.Fail($"no mapping for {operation}: {ws.Relative(path)}"); return 1; }
        var map = OperationMapping.Load(path);

        foreach (var d in decisions)
        {
            var idx = d.IndexOf('=');
            if (idx <= 0) { ConsoleUi.Fail($"decision must be <target>=<text>: '{d}'"); return 1; }
            var target = d[..idx].Trim();
            var text = d[(idx + 1)..].Trim();
            if (!map.FieldMappings.Any(f => f.Target == target))
                ConsoleUi.Warn($"'{target}' is not a mapping target in {operation}; recording anyway");
            map.Approval.Decisions[target] = text;
            ConsoleUi.Ok($"decision {target}: {text}");
        }

        map.Approval.ApprovedBy = by;
        map.Approval.ApprovedAtUtc = DateTime.UtcNow;
        map.Approval.Status = reject ? "rejected" : "approved";
        map.Save(path);

        ConsoleUi.Step("APPROVE", $"{operation} → {map.Approval.Status} by {by}");
        var remaining = map.PolicyViolations(m.Policy.MinAutoApproveConfidence).ToList();
        if (remaining.Count > 0)
        {
            foreach (var v in remaining) ConsoleUi.Warn(v);
            return 2;
        }
        ConsoleUi.Ok("policy satisfied — generation may proceed");
        return 0;
    }
}

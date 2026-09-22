using System.CommandLine;
using Factory.Cli.Commands;
using Factory.Cli.Services;

// integration-cli — the paved road (slide 06):
//   init → import → map → (human approval) → generate → ai implement → validate
// Deterministic where structure matters. AI-assisted where variability exists. Engineers stay accountable.

var root = new RootCommand("integration-cli — AI-assisted integration factory (demo)");
var rootOpt = new Option<string?>("--root") { Description = "Repository root (default: discovered from the current directory)" };
root.Options.Add(rootOpt);
Workspace Ws(ParseResult pr) => pr.GetValue(rootOpt) is { } r ? new Workspace(Path.GetFullPath(r)) : Workspace.Discover();

var nameArg = new Argument<string>("connector") { Description = "Connector key, e.g. swapi" };

// init
var init = new Command("init", "INIT — create the connector manifest");
var providerSpec = new Option<string>("--provider-spec") { Description = "Path to the third-party OpenAPI spec", Required = true };
var productSpec = new Option<string>("--product-spec") { Description = "Path to the product (canonical) OpenAPI spec", Required = true };
var baseUrl = new Option<string>("--base-url") { Description = "Provider base URL", Required = true };
var force = new Option<bool>("--force") { Description = "Overwrite an existing manifest" };
init.Arguments.Add(nameArg); init.Options.Add(providerSpec); init.Options.Add(productSpec); init.Options.Add(baseUrl); init.Options.Add(force);
init.SetAction(pr => InitCommand.Run(Ws(pr), pr.GetValue(nameArg)!, pr.GetValue(providerSpec)!, pr.GetValue(productSpec)!, pr.GetValue(baseUrl)!, pr.GetValue(force)));
root.Subcommands.Add(init);

// import
var import = new Command("import", "IMPORT — load specs; generate provider client + canonical models (CLI-owned)");
import.Arguments.Add(nameArg);
import.SetAction((pr, ct) => ImportCommand.RunAsync(Ws(pr), pr.GetValue(nameArg)!));
root.Subcommands.Add(import);

// map
var map = new Command("map", "MAP — build the mapping brief for the AI (or --check the returned mappings against policy)");
var opOpt = new Option<string?>("--operation") { Description = "Single operation (default: all)" };
var check = new Option<bool>("--check") { Description = "Validate mapping files against the approval policy" };
map.Arguments.Add(nameArg); map.Options.Add(opOpt); map.Options.Add(check);
map.SetAction((pr, ct) => MapCommand.RunAsync(Ws(pr), pr.GetValue(nameArg)!, pr.GetValue(opOpt), pr.GetValue(check)));
root.Subcommands.Add(map);

// approve
var approve = new Command("approve", "HUMAN APPROVAL — record decisions on low-confidence mappings");
var opReq = new Option<string>("--operation") { Description = "Operation name", Required = true };
var by = new Option<string>("--by") { Description = "Approver", Required = true };
var decision = new Option<string[]>("--decision") { Description = "<targetField>=<decision text> (repeatable)", AllowMultipleArgumentsPerToken = false };
var reject = new Option<bool>("--reject") { Description = "Reject instead of approve" };
approve.Arguments.Add(nameArg); approve.Options.Add(opReq); approve.Options.Add(by); approve.Options.Add(decision); approve.Options.Add(reject);
approve.SetAction(pr => ApproveCommand.Run(Ws(pr), pr.GetValue(nameArg)!, pr.GetValue(opReq)!, pr.GetValue(by)!, pr.GetValue(decision) ?? Array.Empty<string>(), pr.GetValue(reject)));
root.Subcommands.Add(approve);

// generate
var generate = new Command("generate", "GENERATE — interfaces, DI, connector/mapper skeletons, test shells (regeneration-safe)");
var allowPending = new Option<bool>("--allow-pending") { Description = "Scaffold even with unapproved mappings (experiments only)" };
generate.Arguments.Add(nameArg); generate.Options.Add(allowPending);
generate.SetAction((pr, ct) => GenerateCommand.RunAsync(Ws(pr), pr.GetValue(nameArg)!, pr.GetValue(allowPending)));
root.Subcommands.Add(generate);

// ai implement
var ai = new Command("ai", "AI-assisted steps");
var implement = new Command("implement", "AI IMPLEMENT — assemble the bounded brief for one operation");
implement.Arguments.Add(nameArg); implement.Options.Add(opReq);
implement.SetAction((pr, ct) => ImplementCommand.RunAsync(Ws(pr), pr.GetValue(nameArg)!, pr.GetValue(opReq)!));
ai.Subcommands.Add(implement);
root.Subcommands.Add(ai);

// validate
var validate = new Command("validate", "VALIDATE — approval, TODO, contract, security, compile, behavior (+ --smoke sandbox) gates");
var smoke = new Option<bool>("--smoke") { Description = "Also run the real-provider smoke tests" };
var skipBuild = new Option<bool>("--skip-build") { Description = "Static gates only" };
validate.Arguments.Add(nameArg); validate.Options.Add(smoke); validate.Options.Add(skipBuild);
validate.SetAction((pr, ct) => ValidateCommand.RunAsync(Ws(pr), pr.GetValue(nameArg)!, pr.GetValue(smoke), pr.GetValue(skipBuild)));
root.Subcommands.Add(validate);

try
{
    return await root.Parse(args).InvokeAsync();
}
catch (Exception ex)
{
    ConsoleUi.Fail(ex.Message);
    return 1;
}

# Copilot instructions — integration-factory-demo

This repository is a working model of the "AI-Assisted Integration Factory": deterministic code generation creates the
paved road; AI completes provider-specific logic inside guardrails; engineers stay accountable.

## Ownership zones (respect them in every edit)

| Path | Owner | Rule |
|---|---|---|
| `specs/*.openapi.yaml` | humans | contracts; change here, then `integration-cli import` |
| `connectors/*/connector.manifest.json` | humans | the only place operations and allowed endpoints are declared |
| `connectors/*/mapping/*.mapping.json` | mapping-agent proposes, humans approve | never edited by implementing agents |
| `src/*/Generated/**`, `tests/*/Generated/**` | `integration-cli` | regenerated; never hand-edited |
| `src/Connectors.*/Connector/**`, `tests/*/Connector/**` | connector-agent + humans | only inside `TODO(ai)` blocks until the marker is removed |
| `policy/engineering-policy.md` | humans | extracted into every AI brief by section |
| `.github/agents/*.agent.md` | humans | the agent roles; same shape as the mow2 repo |

## Commands

```
dotnet run --project src/Factory.Cli -- init <name> --provider-spec … --product-spec … --base-url …
dotnet run --project src/Factory.Cli -- import <name>
dotnet run --project src/Factory.Cli -- map <name> [--operation <op>] [--check]
dotnet run --project src/Factory.Cli -- approve <name> --operation <op> --by "<who>" --decision <field>="<text>"
dotnet run --project src/Factory.Cli -- generate <name>
dotnet run --project src/Factory.Cli -- ai implement <name> --operation <op>
dotnet run --project src/Factory.Cli -- validate <name> [--smoke]
```

## Non-negotiables for any agent

- Provider traffic goes through the generated client only. No `new HttpClient()`.
- Allowed endpoints are the manifest's `providerOperations`. Wanting another one is a manifest change + a new mapping.
- Low-confidence mappings are decided by a human via `integration-cli approve`. Never edit `approval` by hand.
- `TODO(ai):` markers, undocumented endpoints and unapproved mappings block release (`validate` enforces it).
- No secrets, no PII logging, no packages beyond the generated project baseline.

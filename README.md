# AI-Assisted Integration Factory — demo

> **Deterministic code generation creates the paved road. AI completes provider-specific logic inside guardrails.
> Engineers remain accountable for production.**

A working, end-to-end model of the *AI-Assisted Integration Factory* deck, built the way we already run agents on client work
(Copilot custom agents in `.github/agents`, a deterministic codegen CLI, coverage contracts, diff-scoped review, no
auto-fix). The third-party API is [SWAPI](https://swapi.dev) — open, no auth, and full of exactly the "similar fields,
different meaning" problems the deck is about (`"height": "172"`, `"mass": "1,358"`, `"birth_year": "19BBY"`,
`"gender": "n/a"`, relations as URLs).

```
specs/                         product + provider OpenAPI contracts (human-owned)
connectors/swapi/
  connector.manifest.json      INIT — operations, allowed provider endpoints, policy   (human-owned)
  mapping/*.mapping.json       MAP  — AI-proposed, human-approved semantic mappings   (versioned, reviewable)
policy/engineering-policy.md   extracted into every AI brief by section
src/Factory.Cli/               integration-cli: init | import | map | approve | generate | ai implement | validate
src/Product.Canonical/Generated/   canonical models      ← import   (CLI-owned, regenerated)
src/Connectors.Swapi/
  Generated/                   provider client, connector contract, DI  ← import/generate (CLI-owned, regenerated)
  Connector/                   SwapiConnector.cs, SwapiMapper.cs        ← generate once, AI fills TODO(ai) (AI + human owned)
tests/Connectors.Swapi.Tests/
  Generated/                   contract + smoke test shells             (CLI-owned)
  Connector/                   behavioral tests                         (shared ownership)
  Fixtures/                    recorded public SWAPI responses
.github/agents/                orchestrator, mapping-agent, connector-agent, connector-reviewer
.github/workflows/validate.yml CI = the release policy
demo/                          WALKTHROUGH.md, reset.ps1 / plant-failure.ps1 and the golden checkpoints
```

## Prerequisites

.NET 10 SDK, VS Code with GitHub Copilot (agent mode) for the AI steps, PowerShell for the demo scripts.
`alias integration-cli="dotnet run --project src/Factory.Cli --"` (or `dotnet build src/Factory.Cli` once and use
`src/Factory.Cli/bin/Debug/net10.0/integration-cli`).

## The paved road

| Step | Command | Who | What you see |
|---|---|---|---|
| INIT | `integration-cli init swapi --provider-spec specs/swapi.openapi.yaml --product-spec specs/product.openapi.yaml --base-url https://swapi.dev/api` then edit `operations[]` | human | the manifest: 3 operations, each with the *only* provider endpoints it may call |
| IMPORT | `integration-cli import swapi` | CLI | NSwag client (`SwapiClient.g.cs`) + canonical models; manifest validated against both specs |
| MAP | `integration-cli map swapi` → `@mapping-agent …` → `integration-cli map swapi --check` | AI proposes | ~1.4k-token briefs; `*.mapping.json` with per-field confidence; `--check` prints the low-confidence rows and the business questions |
| APPROVE | `integration-cli approve swapi --operation getCharacter --by "Andrew" --decision gender="…" --decision birthYear="…"` | human | decisions recorded *in* the mapping file; `--check` turns green |
| GENERATE | `integration-cli generate swapi` | CLI | interface, DI, csproj, connector + mapper skeletons with `TODO(ai)` blocks that carry the approved mappings and decisions as comments; test shells; refuses to run on unapproved mappings |
| IMPLEMENT | `integration-cli ai implement swapi --operation getCharacter` → `@connector-agent …` | AI in guardrails | a ~3.5k-token brief (vs ~16k for specs + client) listing the exact blocks to fill |
| VALIDATE | `integration-cli validate swapi --smoke` | CI + review | APPROVAL · TODO · CONTRACT · SECURITY · COMPILE · BEHAVIOR · SANDBOX — release blocked if any fails; `@connector-reviewer` reviews the diff against the mapping |

## Run it yourself

The deck (`AI_Assisted_API_Integration_Factory.pptx`, slides 01–11) explains the approach. The hands-on part is in
**[demo/WALKTHROUGH.md](demo/WALKTHROUGH.md)**: one prompt to `@orchestrator` takes `getCharacter` from spec to seven
green gates, stopping once to ask you a business question; a second prompt plants a failure so you can watch the
contract gate catch it and the agent escalate instead of fixing it. About six minutes, two prompts, one answer.

## How it maps to our existing agent loop

| Deck | This repo | Existing backend codegen loop |
|---|---|---|
| Manifest-driven CLI | `connector.manifest.json` → `integration-cli` (Scriban) | `BackendGenerationContract.json` → `tools/BackendCodegen.Cli` |
| Reviewable intermediate artifact | `*.mapping.json` with confidence + `approval.decisions` | `implementation-coverage-contract.json` + Boundary Scorecard |
| Smallest complete AI package | `.factory/session/*.md` briefs | pasted task briefs, Token Discipline |
| AI fills bounded TODOs | `TODO(ai)` blocks in `Connector/` | backend-agent "business logic in service methods only" |
| Quality gates, no auto-fix | `validate` + connector-reviewer + Remediation Ladder | P7d–P7f, backend-reviewer, Remediation Ladder |
| Regeneration-safe | `Generated/` overwritten, `Connector/` scaffolded once | `overwriteExisting: false`, existing-first mode |

## Adding a real provider

1. Drop its OpenAPI spec in `specs/`, `integration-cli init <name> …`, declare operations and allowed endpoints.
2. `import` → `map` → approve → `generate` → `ai implement` per operation → `validate`.
3. Set `provider.auth` in the manifest (`apiKey`, `oauth2-client-credentials`, `bearer` + `secretName`) — the DI
   extension is where the credential handler goes; it is generated, so it is the same for every connector.

# AI-Assisted Integration Factory — demo

> **Deterministic code generation creates the paved road. AI completes provider-specific logic inside guardrails.
> Engineers remain accountable for production.**

A working, end-to-end model of the *AI-Assisted Integration Factory* deck, built the way we run agents on VNA MOW 2.0
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
demo/                          reset.ps1 / plant-failure.ps1 and the golden checkpoints
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

## Run of show (≈14 min, engineering audience) — the AI drives, the room decides

The committed state is **implemented** (everything green). Before presenting: `./demo/reset.ps1 -To demo`. That leaves
`searchCharacters` and `getPlanet` finished and strips `getCharacter` back to "no mapping, TODO(ai) blocks", so one
prompt to the orchestrator runs the whole journey for one operation in about six minutes with two sub-agent calls.

**Setup:** repo open in VS Code, Copilot agent mode, integrated PowerShell terminal visible, `dotnet build src/Factory.Cli`
run once, a browser tab on `https://swapi.dev/api/people/2/` (C-3PO, `"gender": "n/a"`).

1. **Slides 01–09 (≈7 min).** Thesis, the repo layout, the journey, the mapping artifact, what the model receives, the gates.
2. **One prompt (≈6 min).** In Copilot chat:
   `@orchestrator Run the integration factory for connector swapi, operation getCharacter. Run validate with --smoke at the end.`
   The orchestrator then, announcing each phase in the terminal:
   - runs `map --check` and reports that two operations are done and `getCharacter` has no mapping;
   - runs `import` (regenerates the client and canonical models);
   - runs `map --operation getCharacter`, hands the ~1.4k-token brief to **mapping-agent**, which writes `getCharacter.mapping.json`;
   - runs `map --check`, prints the two low-confidence rows (`birth_year` 0.62, `gender` 0.55) and the droid question, and **stops**;
   - **you ask the room**: is `n/a` (a droid) `Other` or `Unknown`? Type the answer back to the orchestrator in plain words;
   - it runs `approve … --by "the room" --decision gender="…" --decision birthYear="…"`, shows `--check` green;
   - runs `generate` ("regenerated 4, left untouched 5" — the ownership rule);
   - runs `ai implement --operation getCharacter` (read the token line aloud) and hands the brief to **connector-agent**, which fills the three TODO(ai) blocks and runs `validate`;
   - runs `validate swapi --smoke` (7 green, the last against the live API) and asks **connector-reviewer** to check the diff against the mapping;
   - reports: gate matrix, decisions recorded, files changed.
3. **The trap (≈1 min).** Second prompt:
   `@orchestrator Someone added film-title enrichment to GetCharacterAsync — run .\demo\plant-failure.ps1, then validate, and tell me what you would do.`
   The orchestrator runs it, reports `CONTRACT FAIL — GetFilmAsync not an allowed provider operation`, and — per policy — does **not** fix it: it presents options (remove the call; or add `getFilm` to the manifest and map it) and asks you to choose. Say "remove it"; it runs `plant-failure.ps1 -Undo` and re-validates.
4. **Close (≈1 min).** Slide 13.

*Fallbacks:* if a sub-agent stalls, `./demo/reset.ps1 -To implemented` and `validate --smoke` shows the end state; if the network blocks
swapi.dev, drop `--smoke`. After the session `./demo/reset.ps1 -To implemented` returns the repo to the committed state.

## How it maps to what we run on mow2

| Deck | This repo | mow2 |
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

# Walkthrough — the orchestrator drives, you answer one question

This is the live demo that goes with the deck (`AI_Assisted_API_Integration_Factory.pptx`, slides 01–11). It takes
about six minutes and you type two prompts and one answer. Everything else is done by the agents in `.github/agents`
and by `integration-cli`.

## What you need

- .NET 10 SDK, PowerShell, and VS Code with GitHub Copilot in **agent mode** (the `.github/agents/*.agent.md` files
  show up as `@orchestrator`, `@mapping-agent`, `@connector-agent`, `@connector-reviewer`).
- Network access to `https://swapi.dev` for the final gate (drop `--smoke` from the prompt if you don't have it).
- Build the CLI once: `dotnet build src/Factory.Cli`.

## Step 1 — put the repo at the demo checkpoint

```powershell
.\demo\reset.ps1 -To demo
```

**What it does.** Wipes the generated code and the connector, restores two operations (`searchCharacters`, `getPlanet`)
fully mapped, implemented and green, and strips `getCharacter` back to "no mapping file, `TODO(ai)` blocks in the
connector, mapper and tests". One operation now has to go through the whole journey, which needs two agent calls and
fits in a few minutes.

**Check (optional, private).** `dotnet run --project src/Factory.Cli -- map swapi --check` should print two approved
rows and `getCharacter: no mapping file`. Don't show this to an audience — the orchestrator's first move is to run the
same check, and that should come from the agent.

Have a browser tab on `https://swapi.dev/api/people/2/` — that is C-3PO, `"gender": "n/a"`, the record that becomes
the question.

## Step 2 — one prompt to the orchestrator

Paste into Copilot chat, then take your hands off the keyboard:

```
@orchestrator Run the integration factory for connector swapi, operation getCharacter. Run validate with --smoke at the end.
```

**What you will see in the terminal, in order.** The orchestrator announces each phase.

| Phase | What happens | What to say |
|---|---|---|
| `map --check` | Reports two operations approved, `getCharacter` missing. | "It reads state from the tool; it doesn't assume." |
| `import` | Regenerates `SwapiClient.g.cs` (~1,000 lines) and `CanonicalModels.g.cs` from the two specs. | "A thousand lines of plumbing rebuilt in a second." |
| `map --operation getCharacter` | Writes a ~1,400-token brief and hands it to **mapping-agent**, which writes `connectors/swapi/mapping/getCharacter.mapping.json`. Takes 30–60 s. | Show the C-3PO tab: "this record is about to become a question." |
| `map --check` | Prints `birth_year -> birthYear (0.62)` and `gender -> gender (0.55)` with the question *does "n/a" (droids) map to Other or Unknown?* — and **ends its turn asking you**. | Read the question aloud. |

If the mapping agent stalls for more than two minutes: `.\demo\reset.ps1 -To mapped` and re-send the same prompt.

## Step 3 — the gate: a person decides

The orchestrator asks two questions and waits. Ask the room (or decide), then paste the answer in plain words — edit
this to match what was decided:

```
n/a is Other - droids are a real category, not missing data. Unparsable birth years become null.
```

**What it does next, on its own.**

| Phase | What happens | What to watch for |
|---|---|---|
| `approve` | Records your words in the mapping file as `approval.decisions`, with `approvedBy` and a timestamp, and re-runs `--check` — green. | "The decision is in the JSON with who and when — not in this chat." |
| `generate` | Rewrites `Generated/`, leaves `Connector/` alone. | The line `regenerated 4, left untouched 5` — that is the ownership rule. |
| `ai implement --operation getCharacter` | Builds a ~3k-token brief (the specs plus the client would be ~16k, and are not sent) and hands it to **connector-agent**, which fills the three `TODO(ai)` blocks and runs `validate` itself. This is the longest wait: 1–3 min. | Don't type. |
| `validate swapi --smoke` | Seven gates: APPROVAL, TODO, CONTRACT, SECURITY, COMPILE, BEHAVIOR, SANDBOX. The last one is a real call to swapi.dev. Then **connector-reviewer** checks the diff against the mapping file. | Seven green lines. |
| report | Gate matrix, the two decisions recorded, files changed. | |

## Step 4 — the trap

Second prompt:

```
@orchestrator Someone added film-title enrichment to GetCharacterAsync - run .\demo\plant-failure.ps1, then validate, and tell me what you would do.
```

**What it does.** `plant-failure.ps1` adds a "helpful" enrichment to `GetCharacterAsync`: fetch the film title for the
UI via `GetFilmAsync`. It compiles. The tests pass. It calls a documented endpoint that works. `validate` still blocks it:

```
✗ CONTRACT  FAIL (1)
  • src/Connectors.Swapi/Connector/SwapiConnector.cs calls undocumented/unallowed provider operation `GetFilmAsync`
    (allowed: GetPersonAsync, ListPeopleAsync, GetPlanetAsync)
```

Per its rules the orchestrator does **not** fix it. It lays out the options — remove the call, or add `getFilm` to the
manifest and map it (a new mapping and a new approval) — and asks you. Paste:

```
Remove it.
```

It runs `plant-failure.ps1 -Undo` and re-validates. The point: the manifest, not the code, decides what a connector may
depend on, and the agent escalated instead of quietly patching.

## Afterwards

`.\demo\reset.ps1 -To implemented` returns the repo to the committed, all-green state. Other checkpoints if you want to
show a specific moment: `init` (nothing generated), `mapped` (approved mappings, nothing generated), `scaffolded`
(`TODO(ai)` blocks everywhere — `validate` fails on TODO, CONTRACT and BEHAVIOR at once).

## If you can't run the agents

Everything the agents do can be replayed by hand with the CLI, in the same order; the golden outputs the agents are
expected to produce are checked in under `demo/golden/`:

```powershell
.\demo\reset.ps1 -To demo
dotnet run --project src/Factory.Cli -- import swapi
dotnet run --project src/Factory.Cli -- map swapi --operation getCharacter          # writes the brief + a pending skeleton
Copy-Item demo\golden\connectors\swapi\mapping\getCharacter.mapping.json connectors\swapi\mapping\   # what mapping-agent writes
dotnet run --project src/Factory.Cli -- map swapi --check
dotnet run --project src/Factory.Cli -- approve swapi --operation getCharacter --by "me" --decision gender="n/a -> Other" --decision birthYear="unparsable -> null"
dotnet run --project src/Factory.Cli -- generate swapi
dotnet run --project src/Factory.Cli -- ai implement swapi --operation getCharacter   # writes the brief
Copy-Item demo\golden\src\Connectors.Swapi\Connector\* src\Connectors.Swapi\Connector\           # what connector-agent writes
Copy-Item demo\golden\tests\Connectors.Swapi.Tests\Connector\* tests\Connectors.Swapi.Tests\Connector\
dotnet run --project src/Factory.Cli -- validate swapi --smoke
```

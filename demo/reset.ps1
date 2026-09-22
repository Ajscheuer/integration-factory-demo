<#
.SYNOPSIS
  Puts the repo into one of the demo checkpoints so the walkthrough can be replayed safely.

.PARAMETER To
  init        - before IMPORT: no generated code, no mappings, no connector. (start here for the live demo)
  mapped      - after MAP + human approval: golden mapping files in place, nothing generated yet.
  scaffolded  - after GENERATE: Connector/ and tests contain TODO(ai) markers; validate FAILS on purpose.
  implemented - after AI IMPLEMENT: golden implementation; validate PASSES (this is the committed state).
  demo        - LIVE DEMO start: searchCharacters and getPlanet fully done; getCharacter has NO mapping and its
                connector/mapper/test blocks are TODO(ai). One prompt to @orchestrator takes it from here.

.EXAMPLE
  ./demo/reset.ps1 -To init
#>
param(
  [Parameter(Mandatory)][ValidateSet('init','mapped','scaffolded','implemented','demo')] [string] $To
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $root

function Remove-IfExists($p) { if (Test-Path $p) { Remove-Item $p -Recurse -Force } }
function Copy-Tree($from, $to) { New-Item -ItemType Directory -Force -Path $to | Out-Null; Copy-Item (Join-Path $from '*') $to -Recurse -Force }

# 1) wipe everything the journey produces
Remove-IfExists 'src/Connectors.Swapi/Generated'
Remove-IfExists 'src/Connectors.Swapi/Connector'
Remove-IfExists 'src/Connectors.Swapi/Connectors.Swapi.csproj'
Remove-IfExists 'src/Connectors.Swapi/bin'; Remove-IfExists 'src/Connectors.Swapi/obj'
Remove-IfExists 'src/Product.Canonical/Generated'
Remove-IfExists 'tests/Connectors.Swapi.Tests/Generated'
Remove-IfExists 'tests/Connectors.Swapi.Tests/Connector'
Remove-IfExists 'tests/Connectors.Swapi.Tests/Connectors.Swapi.Tests.csproj'
Remove-IfExists 'tests/Connectors.Swapi.Tests/bin'; Remove-IfExists 'tests/Connectors.Swapi.Tests/obj'
Remove-IfExists 'connectors/swapi/mapping'
Remove-IfExists '.factory/session'
New-Item -ItemType Directory -Force -Path 'connectors/swapi/mapping' | Out-Null
Write-Host "[reset] cleared generated code, mappings, connector and session files" -ForegroundColor Yellow

if ($To -eq 'init') { Write-Host "[reset] at INIT. Next: integration-cli import swapi" -ForegroundColor Green; exit 0 }

# 2) mapped: golden (approved) mapping files
Copy-Tree 'demo/golden/connectors/swapi/mapping' 'connectors/swapi/mapping'
Write-Host "[reset] restored approved mappings" -ForegroundColor Yellow
if ($To -eq 'mapped') { Write-Host "[reset] at MAPPED. Next: integration-cli import swapi; integration-cli generate swapi" -ForegroundColor Green; exit 0 }

# 3) scaffolded: run the real CLI so Generated/ is fresh, then keep the TODO skeletons
dotnet run --project src/Factory.Cli -- import swapi | Out-Host
dotnet run --project src/Factory.Cli -- generate swapi | Out-Host
if ($To -eq 'scaffolded') { Write-Host "[reset] at SCAFFOLDED. validate will FAIL on TODO/CONTRACT/BEHAVIOR (that is the point)." -ForegroundColor Green; exit 0 }

# 4) implemented: overlay the golden implementation and tests
Copy-Tree 'demo/golden/src/Connectors.Swapi/Connector' 'src/Connectors.Swapi/Connector'
Copy-Tree 'demo/golden/tests/Connectors.Swapi.Tests/Connector' 'tests/Connectors.Swapi.Tests/Connector'
if ($To -eq 'implemented') { Write-Host "[reset] at IMPLEMENTED. Next: integration-cli validate swapi --smoke" -ForegroundColor Green; exit 0 }

# 5) demo: strip getCharacter back to "not yet mapped, not yet implemented"
Remove-IfExists 'connectors/swapi/mapping/getCharacter.mapping.json'
Copy-Item 'demo/partial/src/SwapiConnector.cs'   'src/Connectors.Swapi/Connector/SwapiConnector.cs' -Force
Copy-Item 'demo/partial/src/SwapiMapper.cs'      'src/Connectors.Swapi/Connector/SwapiMapper.cs' -Force
Copy-Item 'demo/partial/tests/SwapiMapperTests.cs' 'tests/Connectors.Swapi.Tests/Connector/SwapiMapperTests.cs' -Force
Remove-IfExists '.factory/session'
Write-Host "[reset] at DEMO. In Copilot chat: @orchestrator Run the integration factory for connector swapi, operation getCharacter." -ForegroundColor Green

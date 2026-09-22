#!/usr/bin/env bash
# Bash twin of reset.ps1 — see that file for the checkpoint descriptions.
#   ./demo/reset.sh init|mapped|scaffolded|implemented
set -euo pipefail
to="${1:-}"
case "$to" in init|mapped|scaffolded|implemented|demo) ;; *) echo "usage: $0 init|mapped|scaffolded|implemented|demo"; exit 1;; esac
cd "$(dirname "$0")/.."

rm -rf src/Connectors.Swapi/Generated src/Connectors.Swapi/Connector src/Connectors.Swapi/Connectors.Swapi.csproj src/Connectors.Swapi/bin src/Connectors.Swapi/obj \
       src/Product.Canonical/Generated \
       tests/Connectors.Swapi.Tests/Generated tests/Connectors.Swapi.Tests/Connector tests/Connectors.Swapi.Tests/Connectors.Swapi.Tests.csproj tests/Connectors.Swapi.Tests/bin tests/Connectors.Swapi.Tests/obj \
       connectors/swapi/mapping .factory/session
mkdir -p connectors/swapi/mapping
echo "[reset] cleared generated code, mappings, connector and session files"
[[ "$to" == init ]] && { echo "[reset] at INIT. Next: integration-cli import swapi"; exit 0; }

cp demo/golden/connectors/swapi/mapping/*.json connectors/swapi/mapping/
echo "[reset] restored approved mappings"
[[ "$to" == mapped ]] && { echo "[reset] at MAPPED. Next: import, generate"; exit 0; }

dotnet run --project src/Factory.Cli -- import swapi
dotnet run --project src/Factory.Cli -- generate swapi
[[ "$to" == scaffolded ]] && { echo "[reset] at SCAFFOLDED. validate will FAIL on TODO/CONTRACT/BEHAVIOR (that is the point)."; exit 0; }

cp demo/golden/src/Connectors.Swapi/Connector/* src/Connectors.Swapi/Connector/
mkdir -p tests/Connectors.Swapi.Tests/Connector
cp demo/golden/tests/Connectors.Swapi.Tests/Connector/* tests/Connectors.Swapi.Tests/Connector/
[[ "$to" == implemented ]] && { echo "[reset] at IMPLEMENTED. Next: integration-cli validate swapi --smoke"; exit 0; }

rm -f connectors/swapi/mapping/getCharacter.mapping.json
cp demo/partial/src/SwapiConnector.cs src/Connectors.Swapi/Connector/
cp demo/partial/src/SwapiMapper.cs src/Connectors.Swapi/Connector/
cp demo/partial/tests/SwapiMapperTests.cs tests/Connectors.Swapi.Tests/Connector/
rm -rf .factory/session
echo "[reset] at DEMO. In Copilot chat: @orchestrator Run the integration factory for connector swapi, operation getCharacter."

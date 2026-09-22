<#
.SYNOPSIS
  Plants a realistic "helpful AI" mistake so the audience sees the CONTRACT gate catch it.

  The connector starts calling GetFilmAsync (a documented provider endpoint that is NOT in the manifest for any
  operation) to "enrich" the character with film titles. Compiles fine. Tests pass. validate must still block it.

.EXAMPLE
  ./demo/plant-failure.ps1        # plant
  ./demo/plant-failure.ps1 -Undo  # restore the golden connector
#>
param([switch] $Undo)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..'); Set-Location $root
$target = 'src/Connectors.Swapi/Connector/SwapiConnector.cs'

if ($Undo) {
  Copy-Item 'demo/golden/src/Connectors.Swapi/Connector/SwapiConnector.cs' $target -Force
  Write-Host "[plant] restored golden connector" -ForegroundColor Green
  exit 0
}

$code = Get-Content $target -Raw
$needle = '            return SwapiMapper.ToCharacter(person);'
$patch = @'
            // "Enrichment": pull the first film title so the UI can show it. Looks harmless — but GET /films/{id}/
            // is not an allowed provider operation for getCharacter, so this is an undocumented dependency.
            if (person.Films is { Count: > 0 })
            {
                var firstFilmId = int.Parse(person.Films.First().AbsolutePath.TrimEnd('/').Split('/').Last());
                var film = await _client.GetFilmAsync(firstFilmId, cancellationToken).ConfigureAwait(false);
                person.Name = $"{person.Name} ({film.Title})";
            }
            return SwapiMapper.ToCharacter(person);
'@
if ($code -notlike "*$needle*") { throw "anchor not found; is the golden connector in place?" }
$code = $code.Replace($needle, $patch)
if ($code -notlike "*using System.Linq;*") { $code = $code.Replace('using System.Net;', "using System.Linq;`r`nusing System.Net;") }
Set-Content $target $code -NoNewline
Write-Host "[plant] SwapiConnector.GetCharacterAsync now calls GetFilmAsync — run: integration-cli validate swapi" -ForegroundColor Yellow

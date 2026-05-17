$ErrorActionPreference = "Stop"
$root    = $PSScriptRoot
$out     = "$root\coverage-results"
$report  = "$root\coverage-report"

# Limpa resultados anteriores
if (Test-Path $out)    { Remove-Item $out    -Recurse -Force }
if (Test-Path $report) { Remove-Item $report -Recurse -Force }

# Roda os testes coletando cobertura em formato Cobertura (XML)
dotnet test "$root\src\Api.Tests\Api.Tests.csproj" `
    --configuration Release `
    --collect:"XPlat Code Coverage" `
    --results-directory $out `
    -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Localiza o XML gerado (fica numa subpasta com GUID)
$xml = Get-ChildItem -Path $out -Filter "coverage.cobertura.xml" -Recurse | Select-Object -First 1

if (-not $xml) {
    Write-Error "Arquivo coverage.cobertura.xml não encontrado em $out"
    exit 1
}

# Gera relatório HTML
reportgenerator `
    "-reports:$($xml.FullName)" `
    "-targetdir:$report" `
    "-reporttypes:Html;TextSummary" `
    "-assemblyfilters:+Api"

# Exibe resumo no terminal
$summary = "$report\Summary.txt"
if (Test-Path $summary) {
    Write-Host ""
    Get-Content $summary
}

# Abre o relatório HTML no browser
Start-Process "$report\index.html"

Write-Host ""
Write-Host "Relatório completo em: $report\index.html"

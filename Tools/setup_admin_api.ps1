# Creates (or rotates) the read-only App Insights API key the admin dashboard's
# managed function queries with, and stores it in the SWA's app settings.
# The key value is never printed — rerun this any time to rotate it.
param(
    [string]$Rg = "photon-arena-rg",
    [string]$AppInsights = "jah-metrics",
    [string]$Swa = "jah-web",
    [string]$KeyName = "jah-admin-query"
)
$ErrorActionPreference = "Stop"

$appId = az monitor app-insights component show --app $AppInsights -g $Rg --query appId -o tsv
if (-not $appId) { throw "no appId for $AppInsights" }

# Rotation: delete-then-create; the delete is allowed to fail on first run.
# PS 5.1 turns native stderr into a terminating NativeCommandError under
# ErrorActionPreference=Stop even when redirected — drop the preference first.
$eap = $ErrorActionPreference
$ErrorActionPreference = "Continue"
az monitor app-insights api-key delete --api-key $KeyName --app $AppInsights -g $Rg 2>&1 | Out-Null
$ErrorActionPreference = $eap
$created = az monitor app-insights api-key create --api-key $KeyName --app $AppInsights -g $Rg `
    --read-properties ReadTelemetry | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $created.apiKey) { throw "api-key create failed" }

az staticwebapp appsettings set -n $Swa -g $Rg `
    --setting-names "APPINSIGHTS_APPID=$appId" "APPINSIGHTS_APIKEY=$($created.apiKey)" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "appsettings set failed" }

Write-Host "ADMIN API OK  key '$KeyName' rotated into $Swa app settings (value not shown)"

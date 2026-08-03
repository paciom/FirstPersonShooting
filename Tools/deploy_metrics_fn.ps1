# Zip-deploys Tools/metrics_fn/ to the ingest Function app (ANALYTICS_PLAN.md Phase 2).
param(
    [string]$Rg = "photon-arena-rg",
    [string]$FuncApp = "jah-metrics-fn"
)
$ErrorActionPreference = "Stop"
$src = Join-Path $PSScriptRoot "metrics_fn"
$zip = Join-Path $env:TEMP "metrics_fn.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $src '*') -DestinationPath $zip -Force
az functionapp deployment source config-zip -g $Rg -n $FuncApp --src $zip --only-show-errors | Out-Null
Write-Host "DEPLOY OK  https://$FuncApp.azurewebsites.net/api/e"

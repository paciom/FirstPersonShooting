# Provisions the first-party analytics stack (ANALYTICS_PLAN.md Phase 2).
# Idempotent — every az call is create-or-update. Run from repo root:
#   powershell -ExecutionPolicy Bypass -File Tools/provision_metrics.ps1
param(
    [string]$Rg = "photon-arena-rg",
    [string]$Loc = "eastus",
    [string]$Workspace = "jah-metrics-logs",
    [string]$AppInsights = "jah-metrics",
    [string]$Storage = "jahmetricsfn",
    [string]$FuncApp = "jah-metrics-fn"
)
$ErrorActionPreference = "Stop"

az config set extension.use_dynamic_install=yes_without_prompt --only-show-errors | Out-Null

Write-Host "[1/6] Log Analytics workspace $Workspace (90-day retention)..."
az monitor log-analytics workspace create -g $Rg -n $Workspace -l $Loc `
    --retention-time 90 --only-show-errors | Out-Null

# 0.2 GB/day cap = the circuit breaker that keeps the whole pipeline $0.
Write-Host "[2/6] Daily ingestion cap 0.2 GB..."
az monitor log-analytics workspace update -g $Rg -n $Workspace --quota 0.2 --only-show-errors | Out-Null
$wsid = az monitor log-analytics workspace show -g $Rg -n $Workspace --query id -o tsv

Write-Host "[3/6] Application Insights $AppInsights (workspace-based)..."
az monitor app-insights component create --app $AppInsights -g $Rg -l $Loc `
    --workspace $wsid --application-type web --only-show-errors | Out-Null
$conn = az monitor app-insights component show --app $AppInsights -g $Rg `
    --query connectionString -o tsv

Write-Host "[4/6] Storage account $Storage..."
az storage account create -n $Storage -g $Rg -l $Loc --sku Standard_LRS `
    --min-tls-version TLS1_2 --only-show-errors | Out-Null

Write-Host "[5/6] Function app $FuncApp (PowerShell, consumption)..."
az functionapp create -g $Rg -n $FuncApp --storage-account $Storage `
    --consumption-plan-location $Loc --runtime powershell --runtime-version 7.4 `
    --functions-version 4 --os-type Windows --app-insights $AppInsights `
    --only-show-errors | Out-Null

# METRICS_CONN is what run.ps1 forwards events with (its own telemetry uses the
# APPLICATIONINSIGHTS_CONNECTION_STRING that --app-insights already linked).
az functionapp config appsettings set -g $Rg -n $FuncApp `
    --settings "METRICS_CONN=$conn" --only-show-errors | Out-Null

Write-Host "[6/6] CORS origins..."
$origins = @("https://jah.cc", "https://www.jah.cc", "https://play.jah.cc",
    "http://localhost:8000", "http://localhost:8080")
foreach ($o in $origins) {
    az functionapp cors add -g $Rg -n $FuncApp --allowed-origins $o --only-show-errors | Out-Null
}

Write-Host "PROVISION OK  https://$FuncApp.azurewebsites.net/api/e"

# Publishes Tools/metrics_workbook.json as the "JAH Metrics" workbook on the
# jah-metrics Application Insights resource. Idempotent: the fixed GUID name
# means re-running updates the same workbook. Edit the .json, re-run this.
param(
    [string]$Rg = "photon-arena-rg",
    [string]$AppInsights = "jah-metrics",
    [string]$Loc = "eastus",
    # Fixed so the workbook is one stable resource, not a pile of copies.
    [string]$Name = "7a11ce5e-9a1e-4a2b-8b6f-2026080401aa"
)
$ErrorActionPreference = "Stop"

$componentId = az monitor app-insights component show --app $AppInsights -g $Rg --query id -o tsv
# NOT Get-Content: PS 5.1 attaches provider note-properties to the string, so
# ConvertTo-Json turns it into {"value":...} instead of a string — and it reads
# UTF-8-without-BOM as ANSI, mojibaking every non-ASCII char on the way through.
$serialized = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot "metrics_workbook.json"))

$payload = @{
    location   = $Loc
    kind       = "shared"
    properties = @{
        displayName    = "JAH Metrics"
        serializedData = $serialized
        version        = "Notebook/1.0"
        category       = "workbook"
        sourceId       = $componentId
        description    = "DAU, mode/robot popularity, funnel, match length, boot time, site, errors"
    }
}
# PUT straight to ARM. az's @file body handling dropped the payload entirely
# ("A payload is required"), and routing 8 KB of JSON through cmd's argument
# layer is asking for quoting damage — a PS-native web call has neither problem.
$json = $payload | ConvertTo-Json -Depth 6 -Compress
$sub = az account show --query id -o tsv
$token = az account get-access-token --query accessToken -o tsv
$url = "https://management.azure.com/subscriptions/$sub/resourceGroups/$Rg" +
    "/providers/Microsoft.Insights/workbooks/$Name" + "?api-version=2022-04-01"
try {
    Invoke-RestMethod -Method Put -Uri $url -ContentType "application/json" `
        -Headers @{ Authorization = "Bearer $token" } `
        -Body ([System.Text.Encoding]::UTF8.GetBytes($json)) | Out-Null
}
catch {
    $detail = ""
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $detail = $reader.ReadToEnd()
    }
    throw "workbook PUT failed: $($_.Exception.Message) $detail"
}
Write-Host "WORKBOOK OK  'JAH Metrics' on $AppInsights"

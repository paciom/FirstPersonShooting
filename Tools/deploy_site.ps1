<#
.SYNOPSIS
    Publishes the Web/ marketing site to Azure Static Web Apps (free tier).

.DESCRIPTION
    This is the OTHER deployment. Tools/deploy_webgl.ps1 pushes the 257 MB Unity
    player to blob storage; this pushes the ~4 MB home page that links to it. They
    are separate on purpose — see WEB_PLAN.md.

    Static Web Apps is the right host for the marketing side because the free tier
    includes managed TLS on a custom domain and leaves DNS at the registrar. The
    game does NOT fit here: the free tier caps an app at 250 MB.

    Requires: az CLI (logged in), Node (for the SWA CLI, fetched via npx).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Tools/deploy_site.ps1
    powershell -ExecutionPolicy Bypass -File Tools/deploy_site.ps1 -BuildAssets
    powershell -ExecutionPolicy Bypass -File Tools/deploy_site.ps1 -BindDomain
#>
param(
    [string]$Name     = 'jah-web',
    [string]$Group    = 'photon-arena-rg',
    [string]$Location = 'eastus2',           # SWA free tier is not in every region
    [string]$Domain   = 'www.jah.cc',
    [string]$SiteDir  = (Join-Path $PSScriptRoot '..\Web'),
    [switch]$BuildAssets,                    # re-derive Web/assets from the game art
    [switch]$BindDomain                      # attach $Domain after DNS is in place
)

$ErrorActionPreference = 'Stop'
$SiteDir = (Resolve-Path $SiteDir).Path

function Step($m) { Write-Host "==> $m" -ForegroundColor Cyan }

if ($BuildAssets) {
    Step 'Rebuilding Web/assets from the game art'
    python (Join-Path $PSScriptRoot 'build_site_assets.py')
    if ($LASTEXITCODE -ne 0) { throw 'asset build failed' }
}

Step 'Checking the Azure login'
$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) { throw 'Not logged in. Run: az login' }
Write-Host "    subscription: $($account.name)"

Step "Looking for the static web app '$Name'"
$app = az staticwebapp show --name $Name --resource-group $Group 2>$null | ConvertFrom-Json
if (-not $app) {
    Step "Creating it in $Group / $Location (Free tier)"
    $app = az staticwebapp create --name $Name --resource-group $Group `
        --location $Location --sku Free | ConvertFrom-Json
}
Write-Host "    default host: https://$($app.defaultHostname)/"

Step 'Fetching the deployment token'
$secrets = az staticwebapp secrets list --name $Name --resource-group $Group | ConvertFrom-Json
$token = $secrets.properties.apiKey
if (-not $token) { throw 'no deployment token returned' }

Step "Uploading $SiteDir"
# --env production is what puts it on the real hostname rather than a preview one.
npx -y @azure/static-web-apps-cli deploy $SiteDir --deployment-token $token --env production
if ($LASTEXITCODE -ne 0) { throw 'swa deploy failed' }

if ($BindDomain) {
    Step "Binding $Domain"
    az staticwebapp hostname set --name $Name --resource-group $Group --hostname $Domain
} else {
    $apex = ($Domain -replace '^www\.', '')
    Write-Host ''
    Write-Host 'DNS, at the registrar for the domain:' -ForegroundColor Yellow
    Write-Host "  CNAME  www   ->  $($app.defaultHostname)"
    Write-Host "  ALIAS/ANAME or registrar redirect: $apex -> $Domain"
    Write-Host ''
    Write-Host "Then re-run with -BindDomain. Azure validates the CNAME and issues"
    Write-Host "the certificate itself; there is nothing to buy or upload."
}

Step 'Done'
Write-Host "    live: https://$($app.defaultHostname)/"

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
    [string[]]$Domains = @('www.jah.cc', 'jah.cc'),
    [string]$SiteDir  = (Join-Path $PSScriptRoot '..\Web'),
    [string]$ApiDir   = (Join-Path $PSScriptRoot '..\WebApi'),  # SWA managed functions (admin dashboard)
    [switch]$BuildAssets,                    # re-derive Web/assets from the game art
    [switch]$BindDomain                      # attach $Domains after DNS is in place
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
# `az staticwebapp show` on a missing app writes to stderr, which PowerShell
# turns into a terminating NativeCommandError under $ErrorActionPreference stop.
# Listing and filtering asks the same question without the exception.
$app = az staticwebapp list --resource-group $Group `
    --query "[?name=='$Name'] | [0]" | ConvertFrom-Json
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

Step "Uploading $SiteDir (+ managed functions from $ApiDir)"
# --env production is what puts it on the real hostname rather than a preview one.
$ApiDir = (Resolve-Path $ApiDir).Path
# Without explicit flags the CLI assumes node 16 (EOL) for the API and the
# deployment binary dies with a blank exit 1 — keep these in step with
# platform.apiRuntime in Web/staticwebapp.config.json.
npx -y @azure/static-web-apps-cli deploy $SiteDir --api-location $ApiDir `
    --api-language node --api-version 20 `
    --deployment-token $token --env production
if ($LASTEXITCODE -ne 0) { throw 'swa deploy failed' }

if ($BindDomain) {
    foreach ($d in $Domains) {
        # An apex has no label to hang a CNAME validation off, so Azure hands
        # back a token to publish as TXT at @ instead. Subdomains validate off
        # the CNAME itself and need no token.
        $isApex = ($d.Split('.').Count -le 2)
        Step "Binding $d ($(if ($isApex) {'TXT token'} else {'CNAME'}) validation)"
        if ($isApex) {
            az staticwebapp hostname set --name $Name --resource-group $Group `
                --hostname $d --validation-method dns-txt-token --no-wait
        } else {
            az staticwebapp hostname set --name $Name --resource-group $Group `
                --hostname $d --no-wait
        }
    }
    Step 'Status (Ready means the certificate is issued)'
    az staticwebapp hostname list --name $Name --resource-group $Group `
        --query "[].{domain:domainName,status:status,token:validationToken}" -o table
} else {
    Write-Host ''
    Write-Host 'DNS to add in the jah.cc zone at Cloudflare:' -ForegroundColor Yellow
    Write-Host "  CNAME  @     ->  $($app.defaultHostname)      DNS only (grey cloud)"
    Write-Host "  CNAME  www   ->  $($app.defaultHostname)      DNS only (grey cloud)"
    Write-Host "  TXT    @     ->  <token from -BindDomain, apex ownership proof>"
    Write-Host ''
    Write-Host 'The grey cloud matters: a proxied record resolves to Cloudflare,'
    Write-Host 'so Azure cannot validate it and never issues the certificate.'
    Write-Host 'Cloudflare flattens the apex CNAME to A records by itself.'
    Write-Host ''
    Write-Host 'Then re-run with -BindDomain.'
}

Step 'Done'
Write-Host "    live: https://$($app.defaultHostname)/"

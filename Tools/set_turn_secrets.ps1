<#
.SYNOPSIS
    Gives the signaling server its Cloudflare TURN credentials.

.DESCRIPTION
    Without TURN the server hands out STUN only. Direct peer-to-peer still
    works on most home networks, so this looks fine in testing and then fails
    for the players who need it most: two kids behind carrier-grade NAT pair up
    in the lobby, exchange offers, and never connect — with nothing on screen
    to say why.

    The API token is stored as a Container App *secret* and referenced by the
    environment variable, so it never shows up in `az containerapp show` output
    or in the portal's environment listing. The key id is not sensitive.

    Create the credentials at Cloudflare dashboard -> Realtime -> TURN, then
    put them in (both gitignored):
        .secrets/cf_turn_key_id.txt
        .secrets/cf_turn_api_token.txt

    Updating env vars restarts the container. The handshake is stateless and
    rooms live in memory, so anyone mid-signalling reconnects; matches already
    running are peer-to-peer and unaffected.

.EXAMPLE
    powershell -File Tools/set_turn_secrets.ps1
#>
param(
    [string]$App   = 'photon-arena-signaling',
    [string]$Group = 'photon-arena-rg'
)

$ErrorActionPreference = 'Stop'

function Read-Secret([string]$Name) {
    $path = Join-Path $PSScriptRoot "..\.secrets\$Name"
    if (-not (Test-Path $path)) { throw "Missing $path" }
    $value = (Get-Content $path -Raw).Trim()
    if ([string]::IsNullOrWhiteSpace($value)) { throw "$path is empty" }
    return $value
}

$keyId = Read-Secret 'cf_turn_key_id.txt'
$token = Read-Secret 'cf_turn_api_token.txt'

Write-Host "Storing the TURN API token as a container app secret..."
az containerapp secret set --name $App --resource-group $Group `
    --secrets "cf-turn-api-token=$token" --output none
if ($LASTEXITCODE -ne 0) { throw 'Could not set the secret.' }

Write-Host "Pointing the environment at it..."
az containerapp update --name $App --resource-group $Group `
    --set-env-vars "CF_TURN_KEY_ID=$keyId" "CF_TURN_API_TOKEN=secretref:cf-turn-api-token" `
    --output none
if ($LASTEXITCODE -ne 0) { throw 'Could not update the environment.' }

# Confirm without printing the token: secretref shows as a reference, not a value.
Write-Host ''
Write-Host 'Environment now:'
az containerapp show --name $App --resource-group $Group `
    --query "properties.template.containers[0].env[?starts_with(name,'CF_TURN')].{name:name,value:value,secret:secretRef}" `
    -o table

Write-Host ''
Write-Host 'Done. New connections will receive TURN relay candidates alongside STUN.'

# Generates a role invitation for the www.jah.cc admin dashboard.
# The printed link IS the grant: whoever opens it and signs in with the given
# provider receives the role — send it only to the person it is for.
# Invitations expire (max 168 h); rerun to issue a fresh one.
param(
    [Parameter(Mandatory = $true)][string]$Email,
    [string]$Role = "admin",
    [ValidateSet("AAD", "GitHub")][string]$Provider = "AAD",
    [int]$Hours = 168,
    [string]$Swa = "jah-web",
    [string]$Rg = "photon-arena-rg",
    [string]$Domain = "www.jah.cc"
)
$ErrorActionPreference = "Stop"

$reply = az staticwebapp users invite -n $Swa -g $Rg `
    --authentication-provider $Provider --user-details $Email `
    --role $Role --invitation-expiration-in-hours $Hours --domain $Domain | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw "invite failed" }

$url = $reply.invitationUrl
if (-not $url -and $reply.properties) { $url = $reply.properties.invitationUrl }
if (-not $url) { throw "no invitation url in the reply" }

Write-Host "Invitation for $Email ($Provider, role '$Role', expires in $Hours h):"
Write-Host $url

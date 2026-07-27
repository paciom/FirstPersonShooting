<#
.SYNOPSIS
    Uploads the Unity WebGL player in Build/WebGL to Azure static website hosting.

.DESCRIPTION
    Unity's Brotli build has the decompression fallback OFF, so the browser is the
    thing that decompresses — which only works if each .br blob is served with
    Content-Encoding: br AND the content type of the file *inside* the archive.
    'az storage blob upload-batch' cannot set those per file, so this walks the
    build tree and uploads one blob at a time.

    Symptom of getting this wrong: the loading bar never appears and the console
    says the file is not a valid Unity web build.

.EXAMPLE
    pwsh Tools/deploy_webgl.ps1
    pwsh Tools/deploy_webgl.ps1 -Clean      # drop stale blobs from a previous build first
#>
param(
    [string]$BuildDir = (Join-Path $PSScriptRoot '..\Build\WebGL'),
    [string]$Account  = 'photonarenaweb',
    [string]$Group    = 'photon-arena-rg',
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'

# Content type of the payload, keyed by the extension left after stripping .br.
$ContentTypes = @{
    '.html'    = 'text/html'
    '.js'      = 'text/javascript'
    '.wasm'    = 'application/wasm'
    '.data'    = 'application/octet-stream'
    '.symbols' = 'application/octet-stream'
    '.json'    = 'application/json'
    '.css'     = 'text/css'
    '.png'     = 'image/png'
    '.jpg'     = 'image/jpeg'
    '.jpeg'    = 'image/jpeg'
    '.svg'     = 'image/svg+xml'
    '.ico'     = 'image/x-icon'
}

if (-not (Test-Path $BuildDir)) {
    throw "No build at $BuildDir. Run 'Photon Arena -> Build WebGL Player' in Unity first."
}
$BuildDir = (Resolve-Path $BuildDir).Path

Write-Host "Fetching storage key for $Account..."
$key = az storage account keys list -g $Group -n $Account --query '[0].value' -o tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($key)) {
    throw "Could not read the storage account key. Is 'az login' still valid?"
}

if ($Clean) {
    Write-Host 'Removing existing blobs...'
    az storage blob delete-batch --account-name $Account --account-key $key --source '$web' --output none
}

# Unity drops a Burst symbol folder next to the player that is explicitly not for
# shipping — it is dead weight on the host and leaks build paths.
$files = Get-ChildItem -Path $BuildDir -Recurse -File |
    Where-Object { $_.FullName -notmatch '_BurstDebugInformation_DoNotShip' }

$totalMb = [Math]::Round((($files | Measure-Object -Property Length -Sum).Sum / 1MB), 1)
Write-Host "Uploading $($files.Count) file(s), $totalMb MB, from $BuildDir"

$uploaded = 0
foreach ($file in $files) {
    $rel = $file.FullName.Substring($BuildDir.Length).TrimStart('\', '/').Replace('\', '/')

    # A .br blob keeps the inner file's content type and adds the encoding header.
    $inner    = $file.Name
    $encoding = $null
    if ($inner.EndsWith('.br')) {
        $encoding = 'br'
        $inner = $inner.Substring(0, $inner.Length - 3)
    } elseif ($inner.EndsWith('.gz')) {
        $encoding = 'gzip'
        $inner = $inner.Substring(0, $inner.Length - 3)
    }

    $ext = [System.IO.Path]::GetExtension($inner).ToLowerInvariant()
    $contentType = $ContentTypes[$ext]
    if (-not $contentType) { $contentType = 'application/octet-stream' }

    # index.html must never be cached or players keep loading the previous build.
    $cache = if ($rel -eq 'index.html') { 'no-cache' } else { 'public, max-age=86400' }

    $args = @(
        'storage', 'blob', 'upload',
        '--account-name', $Account, '--account-key', $key,
        '--container-name', '$web',
        '--name', $rel, '--file', $file.FullName,
        '--content-type', $contentType,
        '--content-cache-control', $cache,
        '--overwrite', '--output', 'none'
    )
    if ($encoding) { $args += @('--content-encoding', $encoding) }

    az @args
    if ($LASTEXITCODE -ne 0) { throw "Upload failed for $rel" }

    $note = if ($encoding) { "$contentType, $encoding" } else { $contentType }
    Write-Host ("  {0,-40} {1}" -f $rel, $note)
    $uploaded++
}

$url = az storage account show -g $Group -n $Account --query 'primaryEndpoints.web' -o tsv
Write-Host ''
Write-Host "Uploaded $uploaded file(s)."
Write-Host "Live at: $url"

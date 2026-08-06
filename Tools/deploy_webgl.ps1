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

    Cloudflare fronts this storage account at play.jah.cc and caches the build
    files for a day, so a deploy that does not purge leaves visitors on a new
    index.html fetching the previous build's data file — a mismatch that fails
    to boot rather than merely being stale. The purge runs last, once every
    blob is up, because purging mid-upload just re-caches the gap.

.EXAMPLE
    pwsh Tools/deploy_webgl.ps1
    pwsh Tools/deploy_webgl.ps1 -Clean      # drop stale blobs from a previous build first
    pwsh Tools/deploy_webgl.ps1 -NoPurge    # leave the Cloudflare cache alone
#>
param(
    [string]$BuildDir = (Join-Path $PSScriptRoot '..\Build\WebGL'),
    [string]$Account  = 'photonarenaweb',
    [string]$Group    = 'photon-arena-rg',
    [switch]$Clean,
    [switch]$NoPurge
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
    # StreamingAssets video, which WebGL streams by URL rather than embedding.
    '.mp4'     = 'video/mp4'
    '.webm'    = 'video/webm'
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

    # index.html is the only mutable file and must never be cached: it is what
    # tells a browser which Build-<hash> folder is current.
    #
    # Everything under Build-<hash>/ is immutable by construction -- change the
    # payload and the hash changes with it -- so it can be cached for a year.
    # That is the point of the hashed folder: returning players reuse their copy
    # with no network at all, and can never pair it with a newer index.html.
    #
    # StreamingAssets keeps a modest TTL. Those names are stable across builds,
    # so a long cache there would strand players on last build's video clips.
    if ($rel -eq 'index.html') {
        $cache = 'no-cache'
    } elseif ($rel -like 'Build-*') {
        $cache = 'public, max-age=31536000, immutable'
    } else {
        $cache = 'public, max-age=86400'
    }

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

# --- Cloudflare cache ------------------------------------------------------
# Every build reuses the same blob names, so the edge would keep serving the
# previous deploy until its 24h TTL expired. Purge only after the last upload.

function Read-Secret([string]$Name) {
    $path = Join-Path $PSScriptRoot "..\.secrets\$Name"
    if (-not (Test-Path $path)) { return $null }
    $value = (Get-Content $path -Raw).Trim()
    if ([string]::IsNullOrWhiteSpace($value)) { return $null }
    return $value
}

if ($NoPurge) {
    Write-Host 'Skipping Cloudflare purge (-NoPurge).'
    return
}

$zone  = Read-Secret 'cloudflare_zone_id.txt'
$token = Read-Secret 'cloudflare_token.txt'

if (-not $zone -or -not $token) {
    Write-Host ''
    Write-Host 'Cloudflare purge SKIPPED - credentials not found.' -ForegroundColor Yellow
    Write-Host '  Expected .secrets/cloudflare_zone_id.txt and .secrets/cloudflare_token.txt'
    Write-Host '  Until then play.jah.cc keeps serving the previous build for up to 24h.'
    return
}

Write-Host ''
Write-Host 'Purging the Cloudflare cache...'
try {
    $response = Invoke-RestMethod -Method Post `
        -Uri "https://api.cloudflare.com/client/v4/zones/$zone/purge_cache" `
        -Headers @{ Authorization = "Bearer $token" } `
        -ContentType 'application/json' `
        -Body '{"purge_everything":true}'

    if ($response.success) {
        Write-Host 'Cloudflare cache purged.'
    }
    else {
        # Don't throw: the blobs are already up, so the deploy itself succeeded.
        $why = ($response.errors | ForEach-Object { $_.message }) -join '; '
        Write-Host "Cloudflare purge FAILED: $why" -ForegroundColor Red
        Write-Host '  Purge by hand, or play.jah.cc will serve a mismatched build.'
    }
}
catch {
    Write-Host "Cloudflare purge FAILED: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host '  Purge by hand, or play.jah.cc will serve a mismatched build.'
}

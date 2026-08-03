using namespace System.Net
param($Request, $TriggerMetadata)

# Public ingest endpoint for the first-party analytics pipeline (ANALYTICS_PLAN.md).
# Always answers 204 — a public endpoint should teach probing clients nothing.
# Validation is the gate: bad envelopes are dropped silently, never described.
# Exception: the x-jah-debug header answers 200 with the gate trace instead,
# because a silent pipeline is undebuggable (it reveals gate names, no data).

$script:DebugMode = "$($Request.Headers['x-jah-debug'])" -eq '1'
$script:Trace = New-Object System.Collections.ArrayList

function Note([string]$m) { [void]$script:Trace.Add($m) }

function Done {
    if ($script:DebugMode) {
        Push-OutputBinding -Name Response -Value ([HttpResponseContext]@{
            StatusCode = [HttpStatusCode]::OK
            Body       = ($script:Trace -join "`n")
        })
    }
    else {
        Push-OutputBinding -Name Response -Value ([HttpResponseContext]@{
            StatusCode = [HttpStatusCode]::NoContent
        })
    }
}

try {
    $raw = $Request.RawBody
    # sendBeacon posts an untyped Blob; without a content type the worker may
    # hand the body over as bytes rather than a string.
    if ($raw -is [byte[]]) { $raw = [System.Text.Encoding]::UTF8.GetString($raw) }
    Note "raw: $($raw.GetType().Name) len=$($raw.Length)"
    if (-not $raw -or $raw.Length -gt 65536) { Note 'drop: size'; Done; return }

    try { $envelope = $raw | ConvertFrom-Json -ErrorAction Stop } catch { Note 'drop: json'; Done; return }

    if ($envelope.v -ne 1) { Note 'drop: v'; Done; return }
    $app = "$($envelope.app)"
    if ($app -notin @('jah', 'site')) { Note 'drop: app'; Done; return }
    $envName = "$($envelope.env)"
    if ($envName -notin @('dev', 'prod')) { $envName = 'dev' }
    $iid = "$($envelope.iid)"
    if ($iid -notmatch '^[0-9a-f]{32}$') { Note 'drop: iid'; Done; return }
    $sid = "$($envelope.sid)"
    if ($sid -notmatch '^[0-9a-f]{8,32}$') { Note 'drop: sid'; Done; return }
    # Account link: opaque server-minted userId ONLY — names/emails are rejected
    # by shape and must never appear in analytics (COPPA posture).
    $uid = "$($envelope.uid)"
    if ($uid -and $uid -notmatch '^[A-Za-z0-9_\-]{1,64}$') { $uid = '' }
    $events = @($envelope.events)
    if ($events.Count -eq 0 -or $events.Count -gt 50) { Note 'drop: events'; Done; return }
    Note "gates: ok, events=$($events.Count)"

    $conn = $env:METRICS_CONN
    $ikey = ''; $ingest = ''
    foreach ($part in $conn -split ';') {
        if ($part -like 'InstrumentationKey=*') { $ikey = $part.Substring(19) }
        elseif ($part -like 'IngestionEndpoint=*') { $ingest = $part.Substring(18).TrimEnd('/') }
    }
    Note "conn: ikey=$([bool]$ikey) ingest=$ingest"
    if (-not $ikey -or -not $ingest) { Note 'drop: conn'; Done; return }

    # Server time is authoritative; the client's clock only orders events via 'n'.
    $now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'")
    $tags = @{
        'ai.user.id'     = $iid   # -> user_Id: retention/DAU key
        'ai.session.id'  = $sid   # -> session_Id: funnels/session length
        'ai.cloud.role'  = $app   # 'jah' (game) vs 'site'
        'ai.internal.sdkVersion' = 'jah-fn:1'
    }
    if ($uid) { $tags['ai.user.authUserId'] = $uid }  # -> user_AuthenticatedId

    $batch = New-Object System.Collections.ArrayList
    foreach ($ev in $events) {
        $name = "$($ev.e)"
        if ($name -notmatch '^[a-z][a-z0-9_]{2,31}$') { continue }
        $props = @{ env = $envName }
        $meas = @{}
        if ($ev.p) {
            $count = 0
            foreach ($kv in $ev.p.PSObject.Properties) {
                if ($count -ge 12) { break }
                $k = $kv.Name
                if ($k -notmatch '^[a-z][a-z0-9_]{0,23}$') { continue }
                $val = $kv.Value
                if ($val -is [double] -or $val -is [long] -or $val -is [int] -or $val -is [decimal]) {
                    $meas[$k] = [double]$val
                }
                elseif ($val -is [bool]) { $props[$k] = "$val".ToLower() }
                else {
                    $s = "$val"
                    if ($s.Length -gt 64) { $s = $s.Substring(0, 64) }
                    $props[$k] = $s
                }
                $count++
            }
        }
        if ($null -ne $ev.n) { $meas['n'] = [double]$ev.n }

        [void]$batch.Add(@{
            name = 'Microsoft.ApplicationInsights.Event'
            time = $now
            iKey = $ikey
            tags = $tags
            data = @{
                baseType = 'EventData'
                baseData = @{ ver = 2; name = $name; properties = $props; measurements = $meas }
            }
        })
    }

    if ($batch.Count -gt 0) {
        $json = ConvertTo-Json -InputObject $batch -Depth 8 -Compress
        Note "fwd: $($batch.Count) items, $($json.Length) bytes"
        $resp = Invoke-RestMethod -Method Post -Uri "$ingest/v2/track" -Body $json `
            -ContentType 'application/json' -TimeoutSec 8
        Note "fwd: accepted=$($resp.itemsAccepted)/$($resp.itemsReceived) errors=$(ConvertTo-Json -InputObject @($resp.errors) -Compress -Depth 4)"
    }
    else { Note 'drop: no valid events' }
}
catch { Note "threw: $($_.Exception.Message)" }
Done

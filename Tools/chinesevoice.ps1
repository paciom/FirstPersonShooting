<#
.SYNOPSIS
    Bake a pronunciation clip for every word in ChineseLexicon.cs.

.DESCRIPTION
    Chinese Quest reads each word out loud when the player gets it right, and
    again when it reveals the answer they missed — hearing the word is half of
    learning it. Unity has no text-to-speech, and the browser's SpeechSynthesis
    API only exists in WebGL builds, so the clips are baked here instead:
    offline, once, with Windows' own zh-CN voice, into ordinary WAVs that ship
    as AudioClips and work identically on every platform.

    Clips land in Assets/Resources/Chinese/Voice/<slug>.wav, where <slug> is
    the word's tone-numbered pinyin ("xióng māo" -> "xiong2_mao1"). That rule
    is ChineseLexicon.ToneSlug in C#, reimplemented below; the two must agree
    or every clip is orphaned.

    Needs a Chinese voice installed:
      Settings > Time & language > Language & region > Chinese (Simplified)
      > Language options > Speech. "Microsoft Huihui" is the usual one.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Tools/chinesevoice.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Tools/chinesevoice.ps1 -Force
    Re-bake every clip instead of only the missing ones.
#>
param(
    [switch]$Force,
    # Learner pace: SAPI's 0 is conversational, which clips short words to a
    # blur. -2 is slow enough to copy without sounding like a tape drag.
    [int]$Rate = -2
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech

$root = Split-Path -Parent $PSScriptRoot
$lexicon = Join-Path $root 'Assets/Scripts/Chinese/ChineseLexicon.cs'
$outDir = Join-Path $root 'Assets/Resources/Chinese/Voice'

# --- the same tone-slug rule as ChineseLexicon.ToneSlug -----------------------

$toneMap = @{
    'ā' = 'a1'; 'á' = 'a2'; 'ǎ' = 'a3'; 'à' = 'a4'
    'ō' = 'o1'; 'ó' = 'o2'; 'ǒ' = 'o3'; 'ò' = 'o4'
    'ē' = 'e1'; 'é' = 'e2'; 'ě' = 'e3'; 'è' = 'e4'
    'ī' = 'i1'; 'í' = 'i2'; 'ǐ' = 'i3'; 'ì' = 'i4'
    'ū' = 'u1'; 'ú' = 'u2'; 'ǔ' = 'u3'; 'ù' = 'u4'
    'ǖ' = 'v1'; 'ǘ' = 'v2'; 'ǚ' = 'v3'; 'ǜ' = 'v4'
    'ü' = 'v0'
}

function Get-ToneSlug([string]$pinyin) {
    $parts = @()
    foreach ($syllable in $pinyin.Split(' ')) {
        if ($syllable.Length -eq 0) { continue }
        $letters = ''
        $tone = '0'
        foreach ($c in $syllable.ToCharArray()) {
            $mapped = $toneMap["$c"]
            if ($mapped) {
                $letters += $mapped[0]
                if ($mapped[1] -ne '0') { $tone = $mapped[1] }
            }
            else {
                $letters += [char]::ToLowerInvariant($c)
            }
        }
        $parts += "$letters$tone"
    }
    return $parts -join '_'
}

# --- read the lexicon --------------------------------------------------------

if (-not (Test-Path $lexicon)) { throw "Lexicon not found: $lexicon" }
$text = [System.IO.File]::ReadAllText($lexicon, [System.Text.Encoding]::UTF8)
$entries = [regex]::Matches($text, 'W\("([^"]+)", "([^"]+)", "([^"]+)"\)')
if ($entries.Count -eq 0) { throw "No W(...) entries found in $lexicon" }
Write-Host "$($entries.Count) words in the lexicon"

# --- pick the voice ----------------------------------------------------------

$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$voice = $synth.GetInstalledVoices() |
    Where-Object { $_.VoiceInfo.Culture.Name -like 'zh*' } |
    Select-Object -First 1
if (-not $voice) {
    $synth.Dispose()
    throw "No zh-* voice installed. Add Chinese (Simplified) speech in Windows Settings."
}
$synth.SelectVoice($voice.VoiceInfo.Name)
$synth.Rate = $Rate
Write-Host "voice: $($voice.VoiceInfo.Name) [$($voice.VoiceInfo.Culture.Name)] rate $Rate"

# 16 kHz mono is plenty for a spoken word and a quarter the bytes of the
# 44.1 kHz default — this is ~150 files shipping inside a WebGL build.
$format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(
    16000,
    [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen,
    [System.Speech.AudioFormat.AudioChannel]::Mono)

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$made = 0
$kept = 0
$seen = @{}
foreach ($m in $entries) {
    $hanzi = $m.Groups[1].Value
    $slug = Get-ToneSlug $m.Groups[2].Value

    # Two words can share a pronunciation but never a file; the first one
    # baked wins, and the clip is correct for both.
    if ($seen.ContainsKey($slug)) { continue }
    $seen[$slug] = $true

    $path = Join-Path $outDir "$slug.wav"
    if ((Test-Path $path) -and -not $Force) { $kept++; continue }

    $synth.SetOutputToWaveFile($path, $format)
    $synth.Speak($hanzi)
    $synth.SetOutputToNull()
    $made++
}

$synth.Dispose()
Write-Host "baked $made clip(s), kept $kept, $($seen.Count) unique pronunciations"
Write-Host "-> $outDir"

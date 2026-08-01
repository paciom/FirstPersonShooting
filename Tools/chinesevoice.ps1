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

# --- clip polish -------------------------------------------------------------
#
# SAPI writes each word with ~0.2 s of lead-in and up to a second of trailing
# room, at whatever level the synthesiser felt like: measured across the first
# bake, peaks ran from 3,908 to 28,973 — a 17 dB spread. Quiet words were
# inaudible under the victory sting, which reads as "the pronunciation is
# broken" rather than "the pronunciation is quiet".
#
# So every clip is trimmed to its speech and peak-normalised before it lands.
# Trimming also makes ChineseVoice.LengthOf honest: the reveal beat waits on
# that number, and a second of silence inside it is a second of dead screen.
Add-Type @"
using System;
using System.IO;

public static class WavPolish
{
    // Returns "trimmed <seconds> gain <x>" for the log, or an error string.
    public static string Run(string path, double targetPeak, double floorFraction,
                             double padSeconds)
    {
        byte[] raw = File.ReadAllBytes(path);
        int rate = 16000, channels = 1, bits = 16;
        int dataAt = -1, dataLen = 0;

        // Walk the RIFF chunks rather than assuming a 44-byte header: SAPI
        // emits a 'fact' chunk on some voices, which shifts 'data' along.
        int at = 12;
        while (at + 8 <= raw.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(raw, at, 4);
            int size = BitConverter.ToInt32(raw, at + 4);
            if (id == "fmt ")
            {
                channels = BitConverter.ToInt16(raw, at + 10);
                rate = BitConverter.ToInt32(raw, at + 12);
                bits = BitConverter.ToInt16(raw, at + 22);
            }
            else if (id == "data")
            {
                dataAt = at + 8;
                dataLen = Math.Min(size, raw.Length - dataAt);
                break;
            }
            at += 8 + size + (size & 1);
        }
        if (dataAt < 0 || bits != 16) return "skipped (unexpected format)";

        int count = dataLen / 2;
        short[] samples = new short[count];
        Buffer.BlockCopy(raw, dataAt, samples, 0, count * 2);

        int peak = 0;
        for (int i = 0; i < count; i++)
        {
            int v = Math.Abs((int)samples[i]);
            if (v > peak) peak = v;
        }
        if (peak == 0) return "skipped (silent)";

        int floor = (int)(peak * floorFraction);
        int first = 0, last = count - 1;
        while (first < count && Math.Abs((int)samples[first]) <= floor) first++;
        while (last > first && Math.Abs((int)samples[last]) <= floor) last--;

        int pad = (int)(padSeconds * rate) * channels;
        first = Math.Max(0, first - pad);
        last = Math.Min(count - 1, last + pad);
        int kept = last - first + 1;

        double gain = (targetPeak * 32767.0) / peak;
        short[] outSamples = new short[kept];
        for (int i = 0; i < kept; i++)
        {
            double v = samples[first + i] * gain;
            if (v > 32767.0) v = 32767.0;
            if (v < -32768.0) v = -32768.0;
            outSamples[i] = (short)v;
        }

        // A few ms of ramp at each end, so a trim that landed mid-waveform
        // does not click.
        int ramp = Math.Min(kept / 2, rate / 500);
        for (int i = 0; i < ramp; i++)
        {
            double k = i / (double)ramp;
            outSamples[i] = (short)(outSamples[i] * k);
            outSamples[kept - 1 - i] = (short)(outSamples[kept - 1 - i] * k);
        }

        using (var w = new BinaryWriter(File.Create(path)))
        {
            int bytes = kept * 2;
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + bytes);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16);
            w.Write((short)1);
            w.Write((short)channels);
            w.Write(rate);
            w.Write(rate * channels * 2);
            w.Write((short)(channels * 2));
            w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            w.Write(bytes);
            byte[] outBytes = new byte[bytes];
            Buffer.BlockCopy(outSamples, 0, outBytes, 0, bytes);
            w.Write(outBytes);
        }
        return String.Format("{0:0.00}s gain x{1:0.0}", kept / (double)rate / channels, gain);
    }
}
"@

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

# hanzi/pinyin pairs to bake, from both sources.
$words = New-Object System.Collections.ArrayList
foreach ($m in $entries) {
    [void]$words.Add(@($m.Groups[1].Value, $m.Groups[2].Value))
}
Write-Host "$($entries.Count) words in the lexicon"

# The INFINITE deck: 2000 more, generated by Tools/buildfrequency.py. Optional
# — a checkout without it still gets the themed decks' voices.
$frequency = Join-Path $root 'Assets/Resources/Chinese/frequency.txt'
if (Test-Path $frequency) {
    $rows = 0
    foreach ($line in [System.IO.File]::ReadAllLines($frequency, [System.Text.Encoding]::UTF8)) {
        if ($line.StartsWith('#') -or $line.Length -eq 0) { continue }
        $f = $line.Split("`t")
        if ($f.Length -lt 2) { continue }
        [void]$words.Add(@($f[0], $f[1]))
        $rows++
    }
    Write-Host "$rows words in the frequency deck"
}
else {
    Write-Host "no frequency.txt - run Tools/buildfrequency.py for the INFINITE deck"
}

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
foreach ($pair in $words) {
    $hanzi = $pair[0]
    $slug = Get-ToneSlug $pair[1]

    # Two words can share a pronunciation but never a file; the first one
    # baked wins, and the clip is correct for both.
    if ($seen.ContainsKey($slug)) { continue }
    $seen[$slug] = $true

    $path = Join-Path $outDir "$slug.wav"
    if ((Test-Path $path) -and -not $Force) { $kept++; continue }

    $synth.SetOutputToWaveFile($path, $format)
    $synth.Speak($hanzi)
    # Released before the polish reopens the file for writing.
    $synth.SetOutputToNull()
    # Peak to -1 dBFS, and trim anything under 6% of the peak with 40 ms of
    # room left either side.
    [WavPolish]::Run($path, 0.89, 0.06, 0.04) | Out-Null
    $made++
}

$synth.Dispose()
Write-Host "baked $made clip(s), kept $kept, $($seen.Count) unique pronunciations"
Write-Host "-> $outDir"

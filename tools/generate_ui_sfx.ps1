param(
    [string]$OutputDirectory = "assets/audio/ui"
)

$ErrorActionPreference = "Stop"
$sampleRate = 48000
$resolvedOutput = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot "..\$OutputDirectory"))
[System.IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

function New-SampleBuffer {
    param([double]$DurationSeconds)

    return [double[]]::new([Math]::Ceiling($DurationSeconds * $sampleRate))
}

function Add-Tone {
    param(
        [double[]]$Samples,
        [double]$Frequency,
        [double]$Amplitude,
        [double]$StartSeconds,
        [double]$DurationSeconds,
        [double]$AttackSeconds = 0.008,
        [double]$ReleaseSeconds = 0.05
    )

    $start = [Math]::Floor($StartSeconds * $sampleRate)
    $count = [Math]::Floor($DurationSeconds * $sampleRate)
    for ($offset = 0; $offset -lt $count; $offset++) {
        $index = $start + $offset
        if ($index -ge $Samples.Length) {
            break
        }

        $time = $offset / [double]$sampleRate
        $attack = [Math]::Min(1.0, $time / [Math]::Max(0.001, $AttackSeconds))
        $releaseTime = $DurationSeconds - $time
        $release = [Math]::Min(
            1.0,
            $releaseTime / [Math]::Max(0.001, $ReleaseSeconds))
        $envelope = [Math]::Sin([Math]::PI * 0.5 * $attack) *
            [Math]::Sin([Math]::PI * 0.5 * $release)
        $Samples[$index] += [Math]::Sin(2.0 * [Math]::PI * $Frequency * $time) *
            $Amplitude * $envelope
    }
}

function Add-Sweep {
    param(
        [double[]]$Samples,
        [double]$StartFrequency,
        [double]$EndFrequency,
        [double]$Amplitude,
        [double]$StartSeconds,
        [double]$DurationSeconds
    )

    $start = [Math]::Floor($StartSeconds * $sampleRate)
    $count = [Math]::Floor($DurationSeconds * $sampleRate)
    $phase = 0.0
    for ($offset = 0; $offset -lt $count; $offset++) {
        $index = $start + $offset
        if ($index -ge $Samples.Length) {
            break
        }

        $progress = $offset / [double][Math]::Max(1, $count - 1)
        $frequency = $StartFrequency + ($EndFrequency - $StartFrequency) * $progress
        $phase += 2.0 * [Math]::PI * $frequency / $sampleRate
        $envelope = [Math]::Sin([Math]::PI * $progress)
        $Samples[$index] += [Math]::Sin($phase) * $Amplitude * $envelope
    }
}

function Write-Wave {
    param(
        [string]$FileName,
        [double[]]$Samples
    )

    $path = Join-Path $resolvedOutput $FileName
    $stream = [System.IO.File]::Open(
        $path,
        [System.IO.FileMode]::Create,
        [System.IO.FileAccess]::Write)
    try {
        $writer = [System.IO.BinaryWriter]::new($stream)
        $dataLength = $Samples.Length * 2
        $writer.Write([System.Text.Encoding]::ASCII.GetBytes("RIFF"))
        $writer.Write(36 + $dataLength)
        $writer.Write([System.Text.Encoding]::ASCII.GetBytes("WAVEfmt "))
        $writer.Write(16)
        $writer.Write([int16]1)
        $writer.Write([int16]1)
        $writer.Write($sampleRate)
        $writer.Write($sampleRate * 2)
        $writer.Write([int16]2)
        $writer.Write([int16]16)
        $writer.Write([System.Text.Encoding]::ASCII.GetBytes("data"))
        $writer.Write($dataLength)

        foreach ($sample in $Samples) {
            $limited = [Math]::Max(-0.95, [Math]::Min(0.95, $sample))
            $writer.Write([int16][Math]::Round($limited * 32767.0))
        }
    }
    finally {
        $stream.Dispose()
    }
}

$hover = New-SampleBuffer 0.09
Add-Sweep $hover 880 1420 0.17 0.0 0.085
Write-Wave "hover.wav" $hover

$select = New-SampleBuffer 0.16
Add-Tone $select 520 0.18 0.0 0.10
Add-Tone $select 780 0.16 0.045 0.11
Write-Wave "select.wav" $select

$submit = New-SampleBuffer 0.24
Add-Tone $submit 330 0.16 0.0 0.12
Add-Tone $submit 660 0.18 0.07 0.16
Add-Tone $submit 990 0.10 0.09 0.13
Write-Wave "submit.wav" $submit

$success = New-SampleBuffer 0.64
Add-Tone $success 440 0.14 0.0 0.38
Add-Tone $success 660 0.15 0.10 0.44
Add-Tone $success 880 0.14 0.20 0.42
Add-Tone $success 1320 0.08 0.28 0.34
Write-Wave "success.wav" $success

$failure = New-SampleBuffer 0.62
Add-Sweep $failure 310 105 0.25 0.0 0.58
Add-Tone $failure 155 0.12 0.16 0.42 0.01 0.16
Write-Wave "failure.wav" $failure

$draw = New-SampleBuffer 0.48
Add-Tone $draw 330 0.15 0.0 0.42
Add-Tone $draw 440 0.14 0.0 0.42
Write-Wave "draw.wav" $draw

$transition = New-SampleBuffer 0.40
Add-Sweep $transition 120 720 0.10 0.0 0.38
Add-Tone $transition 960 0.035 0.18 0.18 0.01 0.08
Write-Wave "transition.wav" $transition

Write-Host "Generated UI sound assets in $resolvedOutput"

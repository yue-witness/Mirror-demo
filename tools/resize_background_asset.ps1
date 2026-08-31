param(
    [Parameter(Mandatory = $true)]
    [string]$InputPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [int]$Width = 1920,
    [int]$Height = 1080
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$resolvedInput = [System.IO.Path]::GetFullPath($InputPath)
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
$temporaryOutput = "$resolvedOutput.resize.tmp.png"

$source = [System.Drawing.Image]::FromFile($resolvedInput)
try {
    $target = [System.Drawing.Bitmap]::new(
        $Width,
        $Height,
        [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($target)
        try {
            $graphics.CompositingMode = `
                [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.CompositingQuality = `
                [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = `
                [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = `
                [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.SmoothingMode = `
                [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.DrawImage($source, 0, 0, $Width, $Height)
        }
        finally {
            $graphics.Dispose()
        }

        $target.Save(
            $temporaryOutput,
            [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $target.Dispose()
    }
}
finally {
    $source.Dispose()
}

[System.IO.File]::Move($temporaryOutput, $resolvedOutput, $true)
Write-Output "Saved ${Width}x${Height} background: $resolvedOutput"

param(
    [string]$BaseBackground = "assets/backgrounds/command_chamber_powered_down.png",

    [Parameter(Mandatory = $true)]
    [string]$TutorAtlas,

    [Parameter(Mandatory = $true)]
    [string]$S17Atlas,

    [Parameter(Mandatory = $true)]
    [string]$ContainerGlow,

    [Parameter(Mandatory = $true)]
    [string]$ScannerGlow,

    [string]$OutputDirectory = "_qa/art-preview"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

function New-Canvas {
    param(
        [int]$Width,
        [int]$Height,
        [System.Drawing.Color]$Color
    )

    $canvas = [System.Drawing.Bitmap]::new($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($canvas)
    $graphics.Clear($Color)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    return @($canvas, $graphics)
}

function Draw-CoverImage {
    param(
        [System.Drawing.Graphics]$Graphics,
        [System.Drawing.Image]$Image,
        [System.Drawing.Rectangle]$Destination
    )

    $sourceRatio = $Image.Width / [double]$Image.Height
    $targetRatio = $Destination.Width / [double]$Destination.Height

    if ($sourceRatio -gt $targetRatio) {
        $sourceHeight = $Image.Height
        $sourceWidth = [int]($sourceHeight * $targetRatio)
        $sourceX = [int](($Image.Width - $sourceWidth) / 2)
        $sourceY = 0
    }
    else {
        $sourceWidth = $Image.Width
        $sourceHeight = [int]($sourceWidth / $targetRatio)
        $sourceX = 0
        $sourceY = [int](($Image.Height - $sourceHeight) / 2)
    }

    $source = [System.Drawing.Rectangle]::new(
        $sourceX,
        $sourceY,
        $sourceWidth,
        $sourceHeight)
    $Graphics.DrawImage(
        $Image,
        $Destination,
        $source,
        [System.Drawing.GraphicsUnit]::Pixel)
}

function Draw-ContainImage {
    param(
        [System.Drawing.Graphics]$Graphics,
        [System.Drawing.Image]$Image,
        [System.Drawing.Rectangle]$Destination
    )

    $scale = [Math]::Min(
        $Destination.Width / [double]$Image.Width,
        $Destination.Height / [double]$Image.Height)
    $drawWidth = [int]($Image.Width * $scale)
    $drawHeight = [int]($Image.Height * $scale)
    $drawX = $Destination.X + [int](($Destination.Width - $drawWidth) / 2)
    $drawY = $Destination.Y + [int](($Destination.Height - $drawHeight) / 2)

    $Graphics.DrawImage(
        $Image,
        [System.Drawing.Rectangle]::new($drawX, $drawY, $drawWidth, $drawHeight),
        0,
        0,
        $Image.Width,
        $Image.Height,
        [System.Drawing.GraphicsUnit]::Pixel)
}

function Draw-ImageWithOpacity {
    param(
        [System.Drawing.Graphics]$Graphics,
        [System.Drawing.Image]$Image,
        [System.Drawing.Rectangle]$Destination,
        [float]$Opacity
    )

    $matrix = [System.Drawing.Imaging.ColorMatrix]::new()
    $matrix.Matrix33 = $Opacity
    $attributes = [System.Drawing.Imaging.ImageAttributes]::new()
    $attributes.SetColorMatrix($matrix)
    $Graphics.DrawImage(
        $Image,
        $Destination,
        0,
        0,
        $Image.Width,
        $Image.Height,
        [System.Drawing.GraphicsUnit]::Pixel,
        $attributes)
    $attributes.Dispose()
}

function Draw-Checkerboard {
    param(
        [System.Drawing.Graphics]$Graphics,
        [System.Drawing.Rectangle]$Area,
        [int]$CellSize = 24
    )

    $darkBrush = [System.Drawing.SolidBrush]::new(
        [System.Drawing.Color]::FromArgb(255, 18, 24, 25))
    $lightBrush = [System.Drawing.SolidBrush]::new(
        [System.Drawing.Color]::FromArgb(255, 30, 39, 40))

    for ($y = $Area.Top; $y -lt $Area.Bottom; $y += $CellSize) {
        for ($x = $Area.Left; $x -lt $Area.Right; $x += $CellSize) {
            $index = (($x - $Area.Left) / $CellSize) + (($y - $Area.Top) / $CellSize)
            $brush = if (($index % 2) -eq 0) { $darkBrush } else { $lightBrush }
            $Graphics.FillRectangle($brush, $x, $y, $CellSize, $CellSize)
        }
    }

    $darkBrush.Dispose()
    $lightBrush.Dispose()
}

$resolvedOutput = Join-Path (Get-Location) $OutputDirectory
[System.IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

$background = [System.Drawing.Image]::FromFile((Resolve-Path $BaseBackground))
$tutor = [System.Drawing.Image]::FromFile((Resolve-Path $TutorAtlas))
$s17 = [System.Drawing.Image]::FromFile((Resolve-Path $S17Atlas))
$containerGlowImage = [System.Drawing.Image]::FromFile(
    (Resolve-Path $ContainerGlow))
$scannerGlowImage = [System.Drawing.Image]::FromFile(
    (Resolve-Path $ScannerGlow))

try {
    $sceneParts = New-Canvas 1920 1080 ([System.Drawing.Color]::Black)
    $scene = $sceneParts[0]
    $sceneGraphics = $sceneParts[1]

    try {
        $sceneGraphics.DrawImage($background, 0, 0, 1920, 1080)

        $darkWash = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(30, 0, 8, 10))
        $sceneGraphics.FillRectangle($darkWash, 0, 0, 1920, 1080)
        $darkWash.Dispose()

        Draw-ImageWithOpacity `
            -Graphics $sceneGraphics `
            -Image $containerGlowImage `
            -Destination ([System.Drawing.Rectangle]::new(1510, 112, 380, 450)) `
            -Opacity 0.68
        Draw-ImageWithOpacity `
            -Graphics $sceneGraphics `
            -Image $scannerGlowImage `
            -Destination ([System.Drawing.Rectangle]::new(1260, 615, 650, 245)) `
            -Opacity 0.62

        $borderPen = [System.Drawing.Pen]::new(
            [System.Drawing.Color]::FromArgb(210, 56, 255, 59),
            2.0)
        $sceneGraphics.DrawRoundedRectangle(
            $borderPen,
            [System.Drawing.Rectangle]::new(96, 112, 1084, 840),
            [System.Drawing.Size]::new(34, 34))

        $dotBrush = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(255, 175, 255, 184))
        $glowBrush = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(90, 56, 255, 59))
        foreach ($dot in @(
            @(170, 109), @(198, 109), @(226, 109), @(254, 109),
            @(1177, 212), @(1177, 240), @(1177, 268), @(1177, 296),
            @(1080, 949), @(1052, 949), @(1024, 949))) {
            $sceneGraphics.FillEllipse($glowBrush, $dot[0] - 8, $dot[1] - 8, 18, 18)
            $sceneGraphics.FillEllipse($dotBrush, $dot[0] - 3, $dot[1] - 3, 8, 8)
        }
        $dotBrush.Dispose()
        $glowBrush.Dispose()
        $borderPen.Dispose()

        $scenePath = Join-Path $resolvedOutput "background-vfx-preview.png"
        $scene.Save($scenePath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $sceneGraphics.Dispose()
        $scene.Dispose()
    }

    $characterParts = New-Canvas 1920 1080 ([System.Drawing.Color]::Black)
    $characterCanvas = $characterParts[0]
    $characterGraphics = $characterParts[1]

    try {
        Draw-Checkerboard `
            -Graphics $characterGraphics `
            -Area ([System.Drawing.Rectangle]::new(0, 0, 1920, 1080)) `
            -CellSize 32

        $headingFont = [System.Drawing.Font]::new(
            "Arial",
            30,
            [System.Drawing.FontStyle]::Bold)
        $labelFont = [System.Drawing.Font]::new("Arial", 20)
        $greenBrush = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(255, 56, 255, 59))
        $whiteBrush = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(255, 224, 242, 244))

        $characterGraphics.DrawString(
            "TUTOR / 3 STATES / 12 FRAMES",
            $headingFont,
            $greenBrush,
            40,
            24)
        Draw-ContainImage `
            -Graphics $characterGraphics `
            -Image $tutor `
            -Destination ([System.Drawing.Rectangle]::new(40, 78, 1210, 930))

        $characterGraphics.DrawString(
            "S-17 / IDLE / 4 FRAMES",
            $headingFont,
            $greenBrush,
            1280,
            24)
        Draw-ContainImage `
            -Graphics $characterGraphics `
            -Image $s17 `
            -Destination ([System.Drawing.Rectangle]::new(1280, 86, 600, 730))

        $characterGraphics.DrawString(
            "Transparent alpha checked · preview scale only",
            $labelFont,
            $whiteBrush,
            1280,
            1006)

        $headingFont.Dispose()
        $labelFont.Dispose()
        $greenBrush.Dispose()
        $whiteBrush.Dispose()

        $characterPath = Join-Path $resolvedOutput "character-atlas-preview.png"
        $characterCanvas.Save(
            $characterPath,
            [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $characterGraphics.Dispose()
        $characterCanvas.Dispose()
    }
}
finally {
    $background.Dispose()
    $tutor.Dispose()
    $s17.Dispose()
    $containerGlowImage.Dispose()
    $scannerGlowImage.Dispose()
}

Write-Output (Join-Path $resolvedOutput "background-vfx-preview.png")
Write-Output (Join-Path $resolvedOutput "character-atlas-preview.png")

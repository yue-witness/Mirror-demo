param(
    [string]$OutputPath = (
        Join-Path $PSScriptRoot '..\assets\vfx\container_glow_120f.png')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$frameWidth = 256
$frameHeight = 512
$columns = 15
$rows = 8
$frameCount = 120
$supersample = 4
$nodeCount = 6

# The cage profile follows the tall hourglass hologram in the chamber artwork.
# Every ring is a horizontal 3D ellipse projected into the final 2D frame.
$rings = @(
    [pscustomobject]@{ Y = 68.0; Radius = 64.0 },
    [pscustomobject]@{ Y = 142.0; Radius = 82.0 },
    [pscustomobject]@{ Y = 222.0; Radius = 88.0 },
    [pscustomobject]@{ Y = 304.0; Radius = 88.0 },
    [pscustomobject]@{ Y = 384.0; Radius = 82.0 },
    [pscustomobject]@{ Y = 452.0; Radius = 64.0 }
)

function Get-ProjectedNode {
    param(
        [int]$RingIndex,
        [int]$NodeIndex,
        [int]$FrameIndex
    )

    $ring = $rings[$RingIndex]
    $rotation = 2.0 * [Math]::PI * $FrameIndex / $frameCount
    $ringTwist = $RingIndex * 0.39
    $angle = 2.0 * [Math]::PI * $NodeIndex / $nodeCount
    $angle += $rotation + $ringTwist
    $depth = [Math]::Sin($angle)

    # Depth changes both the ellipse projection and visibility, producing a
    # rotating 3D cage without using a 3D runtime node.
    $perspectiveWidth = 0.88 + 0.12 * (($depth + 1.0) * 0.5)
    $x = 128.0 + $ring.Radius * [Math]::Cos($angle) * $perspectiveWidth
    $y = $ring.Y + $ring.Radius * 0.19 * $depth
    $depthVisibility = 0.34 + 0.66 * (($depth + 1.0) * 0.5)

    # Each stable node has its own phase and frequency. This creates obvious
    # independent flicker while the geometry advances in a continuous loop.
    $stableId = $RingIndex * $nodeCount + $NodeIndex
    $phase = (($stableId * 37) % 71) / 71.0 * 2.0 * [Math]::PI
    # Integer cycle counts preserve independent flicker while guaranteeing a
    # seamless transition from the final frame back to the first frame.
    $frequency = 1.0 + (($stableId * 13) % 4)
    $wave = 0.5 + 0.5 * [Math]::Sin(
        2.0 * [Math]::PI * $FrameIndex / $frameCount * $frequency + $phase)
    $flicker = 0.22 + 0.78 * [Math]::Pow($wave, 1.75)

    return [pscustomobject]@{
        X = $x
        Y = $y
        Depth = $depth
        Visibility = $depthVisibility
        Flicker = $flicker
    }
}

function Get-SpineNode {
    param(
        [int]$RingIndex,
        [int]$FrameIndex
    )

    $phase = (($RingIndex * 29 + 11) % 61) / 61.0 * 2.0 * [Math]::PI
    $wave = 0.5 + 0.5 * [Math]::Sin(
        2.0 * [Math]::PI * $FrameIndex / $frameCount * 2.0 + $phase)
    return [pscustomobject]@{
        X = 128.0
        Y = $rings[$RingIndex].Y
        Depth = 0.65
        Visibility = 0.88
        Flicker = 0.30 + 0.70 * [Math]::Pow($wave, 1.65)
    }
}

function New-Color {
    param(
        [double]$Alpha,
        [int]$Red,
        [int]$Green,
        [int]$Blue
    )

    $boundedAlpha = [Math]::Max(0, [Math]::Min(255, [int][Math]::Round($Alpha)))
    return [System.Drawing.Color]::FromArgb($boundedAlpha, $Red, $Green, $Blue)
}

function Draw-CageLine {
    param(
        [System.Drawing.Graphics]$Graphics,
        [object]$From,
        [object]$To,
        [double]$Strength = 1.0
    )

    $visibility = ($From.Visibility + $To.Visibility) * 0.5
    $flicker = 0.72 + 0.28 * (($From.Flicker + $To.Flicker) * 0.5)
    $alpha = 162.0 * $visibility * $flicker * $Strength
    $width = $supersample * (0.68 + 0.34 * $visibility)
    $pen = [System.Drawing.Pen]::new(
        (New-Color $alpha 68 228 240),
        [single]$width)
    try {
        $Graphics.DrawLine(
            $pen,
            [single]($From.X * $supersample),
            [single]($From.Y * $supersample),
            [single]($To.X * $supersample),
            [single]($To.Y * $supersample))
    }
    finally {
        $pen.Dispose()
    }
}

function Draw-Node {
    param(
        [System.Drawing.Graphics]$Graphics,
        [object]$Node
    )

    $strength = $Node.Visibility * $Node.Flicker
    $cx = $Node.X * $supersample
    $cy = $Node.Y * $supersample

    # Concentric circles form a compact soft glow. There are deliberately no
    # rays or diamonds, so a node can never become an LLM-style starburst.
    $layers = @(
        [pscustomobject]@{ Radius = 5.4; Alpha = 28.0 },
        [pscustomobject]@{ Radius = 3.7; Alpha = 76.0 },
        [pscustomobject]@{ Radius = 1.9; Alpha = 255.0 }
    )
    foreach ($layer in $layers) {
        $radius = $layer.Radius * $supersample
        $brush = [System.Drawing.SolidBrush]::new(
            (New-Color ($layer.Alpha * $strength) 132 250 255))
        try {
            $Graphics.FillEllipse(
                $brush,
                [single]($cx - $radius),
                [single]($cy - $radius),
                [single]($radius * 2.0),
                [single]($radius * 2.0))
        }
        finally {
            $brush.Dispose()
        }
    }
}

function Draw-Frame {
    param([int]$FrameIndex)

    $largeWidth = $frameWidth * $supersample
    $largeHeight = $frameHeight * $supersample
    $large = [System.Drawing.Bitmap]::new(
        $largeWidth,
        $largeHeight,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($large)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
    $graphics.Clear([System.Drawing.Color]::Transparent)

    try {
        $nodes = @()
        for ($ringIndex = 0; $ringIndex -lt $rings.Count; $ringIndex++) {
            $ringNodes = @()
            for ($nodeIndex = 0; $nodeIndex -lt $nodeCount; $nodeIndex++) {
                $ringNodes += Get-ProjectedNode $ringIndex $nodeIndex $FrameIndex
            }
            $nodes += ,$ringNodes
        }

        # Ring edges provide the cylindrical depth cues.
        for ($ringIndex = 0; $ringIndex -lt $rings.Count; $ringIndex++) {
            for ($nodeIndex = 0; $nodeIndex -lt $nodeCount; $nodeIndex++) {
                $next = ($nodeIndex + 1) % $nodeCount
                Draw-CageLine $graphics $nodes[$ringIndex][$nodeIndex] `
                    $nodes[$ringIndex][$next] 0.92
            }
        }

        # Continuous vertical struts preserve the cylinder silhouette while
        # the diagonal cage rotates. Rear struts remain faint so the shape
        # reads as transparent rather than as a flat wall of lines.
        for ($ringIndex = 0; $ringIndex -lt $rings.Count - 1; $ringIndex++) {
            for ($nodeIndex = 0; $nodeIndex -lt $nodeCount; $nodeIndex++) {
                $verticalStrength = if (
                    $nodes[$ringIndex][$nodeIndex].Depth -ge 0.0
                ) { 0.58 } else { 0.34 }
                Draw-CageLine $graphics $nodes[$ringIndex][$nodeIndex] `
                    $nodes[$ringIndex + 1][$nodeIndex] $verticalStrength

                $direction = if ($ringIndex % 2 -eq 0) { 1 } else { -1 }
                $diagonal = ($nodeIndex + $direction + $nodeCount) % $nodeCount
                Draw-CageLine $graphics $nodes[$ringIndex][$nodeIndex] `
                    $nodes[$ringIndex + 1][$diagonal] 0.68
            }
        }

        # A few longer braces reproduce the irregular large triangles visible
        # through the original cylinder instead of repeating a uniform mesh.
        for ($ringIndex = 0; $ringIndex -lt $rings.Count - 2; $ringIndex++) {
            for ($nodeIndex = 0; $nodeIndex -lt $nodeCount; $nodeIndex += 2) {
                $longDiagonal = ($nodeIndex + 2 + $ringIndex) % $nodeCount
                Draw-CageLine $graphics $nodes[$ringIndex][$nodeIndex] `
                    $nodes[$ringIndex + 2][$longDiagonal] 0.38
            }
        }

        # The original hologram has a luminous central spine with triangular
        # braces reaching toward selected front-facing vertices.
        $spine = @()
        for ($ringIndex = 0; $ringIndex -lt $rings.Count; $ringIndex++) {
            $spine += Get-SpineNode $ringIndex $FrameIndex
        }
        for ($ringIndex = 0; $ringIndex -lt $spine.Count - 1; $ringIndex++) {
            Draw-CageLine $graphics $spine[$ringIndex] `
                $spine[$ringIndex + 1] 0.72
        }
        for ($ringIndex = 1; $ringIndex -lt $rings.Count - 1; $ringIndex++) {
            $leftBrace = ($ringIndex + 1) % $nodeCount
            $rightBrace = ($leftBrace + 3) % $nodeCount
            Draw-CageLine $graphics $spine[$ringIndex] `
                $nodes[$ringIndex][$leftBrace] 0.46
            Draw-CageLine $graphics $spine[$ringIndex] `
                $nodes[$ringIndex][$rightBrace] 0.46
        }

        # Draw the nodes last so their independent brightness remains legible.
        for ($ringIndex = 0; $ringIndex -lt $rings.Count; $ringIndex++) {
            for ($nodeIndex = 0; $nodeIndex -lt $nodeCount; $nodeIndex++) {
                Draw-Node $graphics $nodes[$ringIndex][$nodeIndex]
            }
        }
        foreach ($spineNode in $spine) {
            Draw-Node $graphics $spineNode
        }

        $frame = [System.Drawing.Bitmap]::new(
            $frameWidth,
            $frameHeight,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $downsample = [System.Drawing.Graphics]::FromImage($frame)
        try {
            $downsample.CompositingMode = `
                [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $downsample.InterpolationMode = `
                [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $downsample.DrawImage($large, 0, 0, $frameWidth, $frameHeight)
        }
        finally {
            $downsample.Dispose()
        }
        return $frame
    }
    finally {
        $graphics.Dispose()
        $large.Dispose()
    }
}

$atlas = [System.Drawing.Bitmap]::new(
    $frameWidth * $columns,
    $frameHeight * $rows,
    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$atlasGraphics = [System.Drawing.Graphics]::FromImage($atlas)
$atlasGraphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
$atlasGraphics.Clear([System.Drawing.Color]::Transparent)

try {
    for ($frameIndex = 0; $frameIndex -lt $frameCount; $frameIndex++) {
        $frame = Draw-Frame $frameIndex
        try {
            $column = $frameIndex % $columns
            $row = [Math]::Floor($frameIndex / $columns)
            $atlasGraphics.DrawImageUnscaled(
                $frame,
                $column * $frameWidth,
                $row * $frameHeight)
        }
        finally {
            $frame.Dispose()
        }
    }

    $resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
    $outputDirectory = [System.IO.Path]::GetDirectoryName($resolvedOutput)
    [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
    $atlas.Save($resolvedOutput, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output "Generated 120-frame transparent atlas: $resolvedOutput"
}
finally {
    $atlasGraphics.Dispose()
    $atlas.Dispose()
}

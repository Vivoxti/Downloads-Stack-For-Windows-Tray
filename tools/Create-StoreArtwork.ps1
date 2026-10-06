param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\store-assets')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$sourceRoot = Join-Path $outputRoot 'source'
$iconPath = Join-Path $projectRoot 'src\DownloadsStack\Assets\app.png'

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

function New-Canvas {
    param([int]$Width, [int]$Height)

    return [System.Drawing.Bitmap]::new(
        $Width,
        $Height,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
}

function Set-HighQualityRendering {
    param([System.Drawing.Graphics]$Graphics)

    $Graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
    $Graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $Graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $Graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $Graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
}

function Draw-CoverImage {
    param(
        [System.Drawing.Graphics]$Graphics,
        [System.Drawing.Image]$Image,
        [int]$Width,
        [int]$Height
    )

    $scale = [Math]::Max($Width / $Image.Width, $Height / $Image.Height)
    $scaledWidth = [int][Math]::Ceiling($Image.Width * $scale)
    $scaledHeight = [int][Math]::Ceiling($Image.Height * $scale)
    $x = [int](($Width - $scaledWidth) / 2)
    $y = [int](($Height - $scaledHeight) / 2)
    $Graphics.DrawImage($Image, $x, $y, $scaledWidth, $scaledHeight)
}

function Draw-Glow {
    param(
        [System.Drawing.Graphics]$Graphics,
        [int]$CenterX,
        [int]$CenterY,
        [int]$Diameter
    )

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddEllipse($CenterX - $Diameter / 2, $CenterY - $Diameter / 2, $Diameter, $Diameter)
    $brush = [System.Drawing.Drawing2D.PathGradientBrush]::new($path)
    $brush.CenterColor = [System.Drawing.Color]::FromArgb(115, 130, 225, 255)
    $brush.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 20, 110, 255))
    $Graphics.FillPath($brush, $path)
    $brush.Dispose()
    $path.Dispose()
}

function New-MarketingArtwork {
    param(
        [string]$BackgroundPath,
        [string]$OutputPath,
        [int]$Width,
        [int]$Height,
        [int]$IconSize,
        [int]$IconCenterX,
        [int]$IconCenterY
    )

    $background = [System.Drawing.Image]::FromFile($BackgroundPath)
    $icon = [System.Drawing.Image]::FromFile($iconPath)
    $canvas = New-Canvas -Width $Width -Height $Height
    $graphics = [System.Drawing.Graphics]::FromImage($canvas)
    Set-HighQualityRendering -Graphics $graphics

    try {
        Draw-CoverImage -Graphics $graphics -Image $background -Width $Width -Height $Height
        Draw-Glow -Graphics $graphics -CenterX $IconCenterX -CenterY $IconCenterY -Diameter ([int]($IconSize * 1.34))
        $iconX = [int]($IconCenterX - $IconSize / 2)
        $iconY = [int]($IconCenterY - $IconSize / 2)
        $graphics.DrawImage($icon, $iconX, $iconY, $IconSize, $IconSize)
        $canvas.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $canvas.Dispose()
        $icon.Dispose()
        $background.Dispose()
    }
}

function New-StoreIcon {
    param(
        [string]$OutputPath,
        [int]$Size
    )

    $icon = [System.Drawing.Image]::FromFile($iconPath)
    $canvas = New-Canvas -Width $Size -Height $Size
    $graphics = [System.Drawing.Graphics]::FromImage($canvas)
    Set-HighQualityRendering -Graphics $graphics
    $graphics.Clear([System.Drawing.Color]::Transparent)

    try {
        $drawSize = [int][Math]::Round($Size * 0.90)
        $offset = [int](($Size - $drawSize) / 2)
        $graphics.DrawImage($icon, $offset, $offset, $drawSize, $drawSize)
        $canvas.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $canvas.Dispose()
        $icon.Dispose()
    }
}

$portraitBackground = Join-Path $sourceRoot 'background-portrait.png'
$squareBackground = Join-Path $sourceRoot 'background-square.png'
$landscapeBackground = Join-Path $sourceRoot 'background-landscape.png'

New-MarketingArtwork -BackgroundPath $portraitBackground -OutputPath (Join-Path $outputRoot 'store-logo-poster-1440x2160.png') -Width 1440 -Height 2160 -IconSize 820 -IconCenterX 720 -IconCenterY 570
New-MarketingArtwork -BackgroundPath $portraitBackground -OutputPath (Join-Path $outputRoot 'store-logo-poster-720x1080.png') -Width 720 -Height 1080 -IconSize 410 -IconCenterX 360 -IconCenterY 285

New-MarketingArtwork -BackgroundPath $squareBackground -OutputPath (Join-Path $outputRoot 'store-logo-square-2160x2160.png') -Width 2160 -Height 2160 -IconSize 1120 -IconCenterX 1080 -IconCenterY 900
New-MarketingArtwork -BackgroundPath $squareBackground -OutputPath (Join-Path $outputRoot 'store-logo-square-1080x1080.png') -Width 1080 -Height 1080 -IconSize 560 -IconCenterX 540 -IconCenterY 450

New-MarketingArtwork -BackgroundPath $landscapeBackground -OutputPath (Join-Path $outputRoot 'store-super-banner-3840x2160.png') -Width 3840 -Height 2160 -IconSize 1050 -IconCenterX 1220 -IconCenterY 1040
New-MarketingArtwork -BackgroundPath $landscapeBackground -OutputPath (Join-Path $outputRoot 'store-super-banner-1920x1080.png') -Width 1920 -Height 1080 -IconSize 525 -IconCenterX 610 -IconCenterY 520

New-StoreIcon -OutputPath (Join-Path $outputRoot 'store-app-tile-300x300.png') -Size 300
New-StoreIcon -OutputPath (Join-Path $outputRoot 'store-icon-150x150.png') -Size 150
New-StoreIcon -OutputPath (Join-Path $outputRoot 'store-icon-71x71.png') -Size 71

Get-ChildItem $outputRoot -File -Filter '*.png' |
    Sort-Object Name |
    Select-Object Name, Length

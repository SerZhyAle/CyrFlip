<#
    Generates a Microsoft Store listing screenshot (1366x768 PNG, the Store minimum) by
    composing the existing brand assets — banner + the layout-aware cursor preview + a
    transliteration demo line — on a dark canvas. Output: assets/store/screenshot-1366x768.png
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$Root    = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent  # repo root
$Assets  = Join-Path $Root 'assets'
$OutDir  = Join-Path $Assets 'store'
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$OutFile = Join-Path $OutDir 'screenshot-1366x768.png'

$W = 1366; $H = 768
$bmp = New-Object System.Drawing.Bitmap($W, $H)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

try {
    # Background — vertical gradient, matching the dark site/tray palette.
    $top = [System.Drawing.Color]::FromArgb(23, 27, 38)   # #171b26
    $bot = [System.Drawing.Color]::FromArgb(13, 16, 23)   # #0d1017
    $rect = New-Object System.Drawing.Rectangle(0, 0, $W, $H)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $top, $bot, 90)
    $g.FillRectangle($brush, $rect)
    $brush.Dispose()

    function Draw-CenteredImage($path, $targetW, $y) {
        $img = [System.Drawing.Image]::FromFile($path)
        try {
            $h = [int]($targetW * $img.Height / $img.Width)
            $x = [int](($W - $targetW) / 2)
            $g.DrawImage($img, $x, $y, $targetW, $h)
            return $y + $h
        } finally { $img.Dispose() }
    }

    function Draw-CenteredText($text, $font, $color, $y) {
        $size = $g.MeasureString($text, $font)
        $x = ($W - $size.Width) / 2
        $sb = New-Object System.Drawing.SolidBrush($color)
        $g.DrawString($text, $font, $sb, $x, $y)
        $sb.Dispose()
        return $y + $size.Height
    }

    # Banner (1280x360) near the top.
    $afterBanner = Draw-CenteredImage (Join-Path $Assets 'banner.png') 900 40

    # Transliteration demo line.
    $white = [System.Drawing.Color]::White
    $mono  = New-Object System.Drawing.Font('Consolas', 30, [System.Drawing.FontStyle]::Bold)
    $demoY = $afterBanner + 24
    $afterDemo = Draw-CenteredText "ghbdtn   ->   Ctrl+Shift+F12   ->   привет" $mono $white $demoY

    # Cursor preview (560x200) — the headline feature: the EN/RU/UK marker on the text cursor.
    $afterPreview = Draw-CenteredImage (Join-Path $Assets 'cursor-preview.png') 700 ($afterDemo + 30)

    # Caption.
    $cap = New-Object System.Drawing.Font('Segoe UI', 16, [System.Drawing.FontStyle]::Regular)
    $grey = [System.Drawing.Color]::FromArgb(170, 178, 190)
    [void](Draw-CenteredText "Live EN / RU / UK marker on your text cursor - plus one-key transliteration" $cap $grey ($afterPreview + 18))
    $mono.Dispose(); $cap.Dispose()

    $bmp.Save($OutFile, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "Wrote $OutFile ($W x $H)"
}
finally {
    $g.Dispose(); $bmp.Dispose()
}

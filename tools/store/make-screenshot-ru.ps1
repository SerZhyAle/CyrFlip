<#
    Generates a Russian-language Microsoft Store screenshot (1366x768 PNG).
    Output: assets/store/screenshot-1366x768-ru.png
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$Root    = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$Assets  = Join-Path $Root 'assets'
$OutFile = Join-Path $Assets 'store\screenshot-1366x768-ru.png'

$W = 1366; $H = 768
$bmp = New-Object System.Drawing.Bitmap($W, $H)
$g   = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

# ── helpers ────────────────────────────────────────────────────────────────────
function RoundRect($gfx, $brush, $x, $y, $w, $h, $r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x,          $y,          $r*2, $r*2, 180, 90)
    $path.AddArc($x+$w-$r*2, $y,          $r*2, $r*2, 270, 90)
    $path.AddArc($x+$w-$r*2, $y+$h-$r*2, $r*2, $r*2,   0, 90)
    $path.AddArc($x,          $y+$h-$r*2, $r*2, $r*2,  90, 90)
    $path.CloseAllFigures()
    $gfx.FillPath($brush, $path)
    $path.Dispose()
}

function MeasW($gfx, $text, $font) { return $gfx.MeasureString($text, $font).Width }
function MeasH($gfx, $text, $font) { return $gfx.MeasureString($text, $font).Height }

try {
    # ── background ─────────────────────────────────────────────────────────────
    $bg1  = [System.Drawing.Color]::FromArgb(18,  22, 34)
    $bg2  = [System.Drawing.Color]::FromArgb(10,  13, 20)
    $rect = New-Object System.Drawing.Rectangle(0, 0, $W, $H)
    $bgBr = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $bg1, $bg2, 90)
    $g.FillRectangle($bgBr, $rect)
    $bgBr.Dispose()

    # ── header card ────────────────────────────────────────────────────────────
    $cardBr = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(28, 34, 52))
    RoundRect $g $cardBr 40 28 ($W - 80) 140 18
    $cardBr.Dispose()

    # App icon (cyrflip.ico → first frame as bitmap)
    $icoPath = Join-Path $Assets 'cyrflip.ico'
    if (Test-Path $icoPath) {
        $ico = [System.Drawing.Icon]::ExtractAssociatedIcon($icoPath)
        $icoBmp = $ico.ToBitmap()
        $iconSz = 80
        $g.DrawImage($icoBmp, 80, 44, $iconSz, $iconSz)
        $icoBmp.Dispose(); $ico.Dispose()
    }

    # Title
    $fTitle = New-Object System.Drawing.Font('Segoe UI', 52, [System.Drawing.FontStyle]::Bold)
    $brWhite = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.DrawString('CyrFlip', $fTitle, $brWhite, 174, 36)
    $fTitle.Dispose()

    # Tagline
    $fTag = New-Object System.Drawing.Font('Segoe UI', 20, [System.Drawing.FontStyle]::Regular)
    $brGrey = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(160, 170, 190))
    $g.DrawString('Живой индикатор раскладки у каретки  ·  исправление одной клавишей', $fTag, $brGrey, 174, 108)
    $fTag.Dispose()

    # ── left column: feature list ───────────────────────────────────────────────
    $fFeat = New-Object System.Drawing.Font('Segoe UI', 18, [System.Drawing.FontStyle]::Regular)
    $fFeatB = New-Object System.Drawing.Font('Segoe UI', 18, [System.Drawing.FontStyle]::Bold)
    $brAccent = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(99, 179, 237))   # light-blue bullet

    $features = @(
        @{ head='Метка EN/RU/UK';    body=' рядом с мигающей кареткой' },
        @{ head='Индикатор курсора'; body=' — замена I-beam (опционально)' },
        @{ head='Транслитерация';    body=' одной клавишей, в любом приложении' },
        @{ head='Смена клавиши';     body=' из меню трея, без перезапуска' },
        @{ head='Точечный стиль';    body=' или текстовая метка — на выбор' },
        @{ head='< 20 МБ';          body=' · без установки · без интернета' }
    )

    $dotColors = @(
        [System.Drawing.Color]::FromArgb( 56, 189, 248),   # blue  - EN
        [System.Drawing.Color]::FromArgb(248,  79,  79),   # red   - RU
        [System.Drawing.Color]::FromArgb( 74, 222, 128),   # green - UK
        [System.Drawing.Color]::FromArgb(251, 191,  36),   # amber
        [System.Drawing.Color]::FromArgb(167, 139, 250),   # purple
        [System.Drawing.Color]::FromArgb(156, 163, 175)    # grey
    )

    $featX = 68; $featY = 198; $lineH = 64
    $clipR = New-Object System.Drawing.RectangleF($featX, $featY, 650, ($lineH * $features.Count + 20))
    $g.SetClip($clipR)
    for ($i = 0; $i -lt $features.Count; $i++) {
        $feat = $features[$i]
        $cy   = $featY + $i * $lineH + 12

        # Coloured dot
        $dotBr = New-Object System.Drawing.SolidBrush($dotColors[$i])
        $g.FillEllipse($dotBr, $featX, $cy, 16, 16)
        $dotBr.Dispose()

        # Bold heading + normal body
        $headW = MeasW $g $feat.head $fFeatB
        $brH   = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
        $g.DrawString($feat.head, $fFeatB, $brH, $featX + 26, $cy - 3)
        $brH.Dispose()
        $brB = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(160, 170, 190))
        $g.DrawString($feat.body, $fFeat, $brB, $featX + 26 + $headW - 6, $cy - 3)
        $brB.Dispose()
    }
    $g.ResetClip()
    $fFeat.Dispose(); $fFeatB.Dispose(); $brAccent.Dispose()

    # ── right column: cursor-preview image ────────────────────────────────────
    $prevPath = Join-Path $Assets 'cursor-preview.png'
    if (Test-Path $prevPath) {
        $prev = [System.Drawing.Image]::FromFile($prevPath)
        $prevW = 560; $prevH = [int]($prevW * $prev.Height / $prev.Width)
        $prevX = $W - $prevW - 36
        $prevY = 198
        # Subtle card behind it
        $prevCardBr = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(22, 28, 44))
        RoundRect $g $prevCardBr ($prevX - 16) ($prevY - 12) ($prevW + 32) ($prevH + 24) 14
        $prevCardBr.Dispose()
        $g.DrawImage($prev, $prevX, $prevY, $prevW, $prevH)
        $prev.Dispose()
    }

    # ── transliteration demo line ─────────────────────────────────────────────
    $demoY  = 590
    $demoBg = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(24, 30, 48))
    RoundRect $g $demoBg 40 ($demoY - 14) ($W - 80) 82 14
    $demoBg.Dispose()

    $fMono  = New-Object System.Drawing.Font('Consolas', 30, [System.Drawing.FontStyle]::Bold)
    $brErr  = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(248, 113, 113))  # red - wrong
    $brArrow= New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(148, 163, 184))
    $brKey  = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb( 99, 179, 237))  # blue - hotkey
    $brOk   = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb( 74, 222, 128))  # green - correct

    # Measure each segment and draw left-to-right centred
    $seg1 = 'ghbdtn'; $sep1 = '   →   '; $seg2 = 'Ctrl+Shift+F12'; $sep2 = '   →   '; $seg3 = 'привет'
    $w1 = MeasW $g $seg1 $fMono; $ws1 = MeasW $g $sep1 $fMono
    $w2 = MeasW $g $seg2 $fMono; $ws2 = MeasW $g $sep2 $fMono
    $w3 = MeasW $g $seg3 $fMono
    $totalW = $w1 + $ws1 + $w2 + $ws2 + $w3
    $cx = ($W - $totalW) / 2

    $g.DrawString($seg1, $fMono, $brErr,   $cx,                              $demoY)
    $g.DrawString($sep1, $fMono, $brArrow, $cx + $w1,                        $demoY)
    $g.DrawString($seg2, $fMono, $brKey,   $cx + $w1 + $ws1,                 $demoY)
    $g.DrawString($sep2, $fMono, $brArrow, $cx + $w1 + $ws1 + $w2,           $demoY)
    $g.DrawString($seg3, $fMono, $brOk,    $cx + $w1 + $ws1 + $w2 + $ws2,   $demoY)
    $fMono.Dispose(); $brErr.Dispose(); $brArrow.Dispose(); $brKey.Dispose(); $brOk.Dispose()

    # ── footer ────────────────────────────────────────────────────────────────
    $fFoot = New-Object System.Drawing.Font('Segoe UI', 15, [System.Drawing.FontStyle]::Regular)
    $brFoot = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(100, 110, 130))
    $footTxt = 'Windows 10 / 11  ·  .NET Framework 4.8 уже встроен  ·  бесплатно  ·  открытый код: github.com/SerZhyAle/CyrFlip'
    $footW = MeasW $g $footTxt $fFoot
    $g.DrawString($footTxt, $fFoot, $brFoot, ($W - $footW) / 2, 706)
    $fFoot.Dispose(); $brFoot.Dispose()

    # ── save ──────────────────────────────────────────────────────────────────
    $bmp.Save($OutFile, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "Saved: $OutFile"
}
finally {
    $brWhite.Dispose()
    $brGrey.Dispose()
    $g.Dispose()
    $bmp.Dispose()
}

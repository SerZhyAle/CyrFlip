# Store screenshots: the settings window in each of the 13 interface languages, composed onto the
# 1366x768 canvas Partner Center wants.
#
#   .\tools\uitest\Save-StoreScreenshots.ps1                  # all 13 -> msix\screenshots
#   .\tools\uitest\Save-StoreScreenshots.ps1 -Language de,ar  # just these
#
# One language per pass, and each pass restarts the app: the UI language is read at construction,
# so a running instance would keep painting the previous one. The app's own value in
# HKCU\Software\CyrFlip is put back at the end, whatever happens - this walks over the developer's
# real settings, and leaving the app in Bengali after a crash is not a fair trade for a screenshot.
#
# The window is captured with PrintWindow (not a desktop grab), so nothing that happens to float
# above it - a tooltip, the tray flyout, another app - can land in the shot.
#
# Settings is opened with the app's own `/launcher-settings` argument rather than by double-clicking
# the tray icon. Killing instances leaves dead icons in the notification area until something hovers
# them, and MSAA cannot tell a dead "CyrFlip - RU" from the live one - a walk that restarts the app
# thirteen times would be clicking ghosts. The argument is handled whether or not the scenario
# launcher is switched on (Program.cs passes openSettingsOnStart to the context).
[CmdletBinding()]
param(
    [string[]]$Language,
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [int]$Width = 1366,
    [int]$Height = 768,
    [int]$Tab = 0,
    [string]$OutDir = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'msix\screenshots')
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.UiTest.psm1') -Force
Add-Type -AssemblyName System.Drawing

# Arabic and Urdu forms carry WS_EX_LAYOUTRTL, and PrintWindow hands back a horizontally mirrored
# bitmap for a mirrored window: the glyphs come out reversed. Flipping the capture restores it -
# verified against a desktop grab of the live Arabic window on 2026-07-26, which has the caption
# buttons on the left, the title on the right, the tab strip still on the left and the text
# right-aligned, exactly as the flipped capture does. Do not "fix" this by flipping unconditionally.
if (-not ('StoreShot' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class StoreShot {
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int index);
    public static bool IsMirrored(IntPtr h) { return (GetWindowLong(h, -20) & 0x00400000) != 0; }  // GWL_EXSTYLE / WS_EX_LAYOUTRTL
}
'@
}

# Endonym as AppConfig stores it, code as the file is named. The file names use the Partner Center
# listing codes (pt-br, zh-hans, en-us), so it is obvious which listing each shot belongs to.
$languages = @(
    @{ File = 'en-us';   Name = 'English' }
    @{ File = 'ru';      Name = 'Русский' }
    @{ File = 'uk';      Name = 'Українська' }
    @{ File = 'de';      Name = 'Deutsch' }
    @{ File = 'it';      Name = 'Italiano' }
    @{ File = 'es';      Name = 'Español' }
    @{ File = 'fr';      Name = 'Français' }
    @{ File = 'pt-br';   Name = 'Português' }
    @{ File = 'zh-hans'; Name = '中文' }
    @{ File = 'hi';      Name = 'हिन्दी' }
    @{ File = 'bn';      Name = 'বাংলা' }
    @{ File = 'ar';      Name = 'العربية' }
    @{ File = 'ur';      Name = 'اردو' }
)
if ($Language) {
    $languages = $languages | Where-Object { $Language -contains $_.File -or $Language -contains $_.Name }
    if (-not $languages) { throw "No language matched: $($Language -join ', ')" }
}

$key = 'HKCU:\Software\CyrFlip'
function Get-Value([string] $name) { (Get-ItemProperty $key -Name $name -ErrorAction SilentlyContinue).$name }
function Set-Value([string] $name, $value, [string] $type) {
    if ($null -eq $value) { Remove-ItemProperty $key -Name $name -ErrorAction SilentlyContinue; return }
    New-ItemProperty $key -Name $name -Value $value -PropertyType $type -Force | Out-Null
}

# Compose the captured window onto the canvas: centred, scaled down only if it does not fit, over a
# dark gradient so a light window reads as the subject rather than as a hole in the page.
function Write-Canvas([string] $shot, [string] $path, [bool] $mirrored) {
    $src = [System.Drawing.Image]::FromFile($shot)
    try {
        if ($mirrored) { $src.RotateFlip([System.Drawing.RotateFlipType]::RotateNoneFlipX) }
        $canvas = New-Object System.Drawing.Bitmap($Width, $Height)
        try {
            $g = [System.Drawing.Graphics]::FromImage($canvas)
            try {
                $g.SmoothingMode     = 'AntiAlias'
                $g.InterpolationMode = 'HighQualityBicubic'
                $g.PixelOffsetMode   = 'HighQuality'

                $rect  = New-Object System.Drawing.Rectangle(0, 0, $Width, $Height)
                $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
                    $rect,
                    [System.Drawing.Color]::FromArgb(16, 22, 30),
                    [System.Drawing.Color]::FromArgb(38, 50, 66),
                    [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
                $g.FillRectangle($brush, $rect)
                $brush.Dispose()

                $margin = 48
                $scale  = [Math]::Min(1.0, [Math]::Min(($Width - 2 * $margin) / $src.Width, ($Height - 2 * $margin) / $src.Height))
                $w = [int]($src.Width * $scale)
                $h = [int]($src.Height * $scale)
                $x = [int](($Width - $w) / 2)
                $y = [int](($Height - $h) / 2)

                # No drop shadow: translucent slabs stack their alpha and come out as a hard black
                # slab around the window. A hairline is enough to seat a light window on the dark
                # background without pretending to be a 3D effect.
                $g.DrawImage($src, $x, $y, $w, $h)
                $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(60, 255, 255, 255))
                $g.DrawRectangle($pen, $x - 1, $y - 1, $w + 1, $h + 1)
                $pen.Dispose()
            }
            finally { $g.Dispose() }
            $canvas.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $canvas.Dispose() }
    }
    finally { $src.Dispose() }
}

Enable-UiTestDpi | Out-Null
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$temp = Join-Path $env:TEMP 'cyrflip-storeshot.png'

$originalLanguage = Get-Value 'UiLanguage'
$originalTab      = Get-Value 'SettingsTab'
$results = @()

try {
    foreach ($lang in $languages) {
        Stop-CyrFlipApp
        Set-Value 'UiLanguage' $lang.Name 'String'
        Set-Value 'SettingsTab' $Tab 'DWord'

        Start-Process (Get-CyrFlipExe -Configuration $Configuration) -ArgumentList '/launcher-settings' | Out-Null

        $window = Wait-AppWindow -TimeoutSeconds 20
        if (-not $window) { throw "Settings window did not open for $($lang.Name)." }

        Start-Sleep -Milliseconds 700                      # let the tab strip and fonts settle
        $shot     = Save-WindowShot -Handle $window.Handle -Path $temp
        $mirrored = [StoreShot]::IsMirrored($window.Handle)
        $path     = Join-Path $OutDir ("settings-{0}.png" -f $lang.File)
        Write-Canvas $temp $path $mirrored

        $results += [pscustomobject]@{
            Language = $lang.Name
            File     = Split-Path $path -Leaf
            Window   = "$($shot.Width)x$($shot.Height)"
            Canvas   = "${Width}x${Height}"
            Mirrored = $mirrored
        }
        "  {0,-12} {1,-10} window {2}{3}" -f $lang.File, $lang.Name, "$($shot.Width)x$($shot.Height)", $(if ($mirrored) { '  (RTL - capture un-mirrored)' } else { '' }) | Write-Host
    }
}
finally {
    Stop-CyrFlipApp
    Set-Value 'UiLanguage' $originalLanguage 'String'
    Set-Value 'SettingsTab' $originalTab 'DWord'
    if (Test-Path $temp) { Remove-Item $temp -Force }
    Start-CyrFlipApp -Configuration $Configuration | Out-Null   # leave the tray app as we found it
}

$results | Format-Table -AutoSize
"done: $OutDir"

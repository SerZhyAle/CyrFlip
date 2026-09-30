# Builds the CyrFlip settings window inside this process and audits what a user would see: a PNG
# of every page - every screenful of it, scrolled - per UI language, and a report of what the user
# gets cut off or cannot read: captions larger than their control, combo-box entries wider than the
# box or its drop-down, list column headers wider than their column, page names wider than the page
# list, glyph-only buttons with no accessible name, the contrast of every text/background pair, and
# how much of each page's area is actually used.
#
#   .\tools\uitest\Audit-SettingsWindow.ps1                                   # 5 representative languages
#   .\tools\uitest\Audit-SettingsWindow.ps1 -AllLanguages -RealConfig -AllModules
#   .\tools\uitest\Audit-SettingsWindow.ps1 -Language en -Monitor 1           # beside a monitor with another DPI
#
# Unlike Save-SettingsShots.ps1 it never drives the running app, the mouse or the keyboard: the
# window is built from the built exe by reflection - the way SettingsLocalizationTests builds it -
# shown outside every monitor and captured with DrawToBitmap, so it can run while you work. It needs
# Windows PowerShell 5.1 (the exe is .NET Framework 4.8); started from pwsh 7 it re-launches itself.
#
# A window outside every monitor always gets the primary monitor's DPI (measured 2026-09-24 on a
# 175/225/100 % setup), so the DPI a user reaches by dragging the window to another monitor needs
# -OnScreen: the window opens inside monitor -Monitor N (index into Screen.AllScreens) and is pushed
# to the bottom of the z-order at once. It is visible only where no other window covers it.
#
# Writes nothing of the user's except the SettingsTab registry value, which the window saves on every
# page change; it is read first and put back at the end.
[CmdletBinding()]
param(
    [string[]]$Language = @('ru', 'en', 'de', 'ar', 'hi'),     # Localization.Codes
    [switch]$AllLanguages,
    [switch]$RealConfig,
    [switch]$AllModules,
    [int]$Monitor = -1,
    [switch]$OnScreen,         # inside -Monitor's working area, pushed to the bottom of the z-order
    [int]$Page = -1,           # one page only (index); -1 = all
    [ValidateSet('', 'system', 'light', 'dark')][string]$Theme = '',   # the app theme (S0020); default: none, as before the theme existed
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [string]$OutDir = ''       # default: artifacts\uitest\settings (Windows PowerShell has no $PSScriptRoot in param defaults)
)
if (-not $OutDir) {
    $OutDir = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'artifacts\uitest\settings'
    if ($Theme) { $OutDir += '-' + $Theme }
}

if ($PSVersionTable.PSEdition -eq 'Core') {
    $argList = @('-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
    foreach ($l in $Language) { $argList += @('-Language', $l) }
    if ($AllLanguages) { $argList += '-AllLanguages' }
    if ($RealConfig) { $argList += '-RealConfig' }
    if ($AllModules) { $argList += '-AllModules' }
    if ($OnScreen) { $argList += '-OnScreen' }
    $argList += @('-Monitor', $Monitor, '-Page', $Page, '-Configuration', $Configuration)
    if ($Theme) { $argList += @('-Theme', $Theme) }
    if ($OutDir) { $argList += @('-OutDir', $OutDir) }
    & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" @argList
    exit $LASTEXITCODE
}

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.UiTest.psm1') -Force
Enable-UiTestDpi                                   # PerMonitorV2 before the first window exists
[System.Windows.Forms.Application]::EnableVisualStyles()
try { [System.Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false) } catch { }

$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$exe = Join-Path $repo "src\CyrFlip\bin\$Configuration\net48\CyrFlip.exe"
if (-not (Test-Path $exe)) { throw "Build first: $exe not found (dotnet build CyrFlip.sln -c $Configuration)." }
$asm = [System.Reflection.Assembly]::LoadFrom($exe)
$T = @{
    Form   = $asm.GetType('CyrFlip.SettingsForm', $true)
    Config = $asm.GetType('CyrFlip.AppConfig', $true)
    Store  = $asm.GetType('CyrFlip.LauncherScenarioStore', $true)
    Loc    = $asm.GetType('CyrFlip.Localization', $true)
}
$names = [string[]]$T.Loc.GetField('Names').GetValue($null)
$codes = [string[]]$T.Loc.GetField('Codes').GetValue($null)
$Language = @($Language | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })   # -File passes "ru,en" as one string
if ($AllLanguages) { $Language = $codes }
foreach ($l in $Language) { if ($codes -notcontains $l) { throw "Unknown UI language code '$l'. Known: $($codes -join ', ')" } }

# ---------------------------------------------------------------- measuring helpers
Add-Type -TypeDefinition @'
using System;
using System.Drawing;
public static class AuditColor {
    [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
    static double Lin(int c) { double s = c / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
    public static double Luminance(Color c) { return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B); }
    public static double Contrast(Color a, Color b) {
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }
}
'@ -ReferencedAssemblies System.Drawing

# powershell.exe declares itself system-DPI-aware in its manifest, so the process-wide PerMonitorV2
# the module asks for is refused; the thread can still opt in, and every window here is created on
# this thread - which is what makes a window on another monitor take that monitor's DPI, as CyrFlip's
# own (PerMonitorV2 by manifest) windows do.
[void][AuditColor]::SetThreadDpiAwarenessContext([IntPtr]-4)
$TR = [System.Windows.Forms.TextRenderer]
function Get-Excerpt([string]$s) { $s = ($s -replace '\s+', ' ').Trim(); if ($s.Length -gt 60) { $s.Substring(0, 57) + '..' } else { $s } }
function Get-Descendants($root) {
    foreach ($c in $root.Controls) { $c; Get-Descendants $c }
}
function Get-EffectiveBack($c) {
    $p = $c
    while ($p -ne $null) {
        if ($p.BackColor.A -eq 255 -and $p.BackColor -ne [System.Drawing.Color]::Transparent) { return $p.BackColor }
        $p = $p.Parent
    }
    [System.Drawing.SystemColors]::Control
}
function Get-Viewport($page) {
    foreach ($c in $page.Controls) { if ($c -is [System.Windows.Forms.ScrollableControl] -and $c.AutoScroll) { return $c } }
    $page
}

$findings = New-Object System.Collections.Generic.List[object]
$pageStats = New-Object System.Collections.Generic.List[object]
$contrast = @{}
function Add-Finding($lang, $page, $kind, $control, $text, $detail) {
    $findings.Add([pscustomobject]@{ Language = $lang; Page = $page; Kind = $kind; Control = $control; Text = (Get-Excerpt $text); Detail = $detail })
}

function Measure-Page($lang, $pageName, $page, $form) {
    $view = Get-Viewport $page
    $viewW = $view.ClientSize.Width; $viewH = $view.ClientSize.Height
    $maxRight = 0; $maxBottom = 0
    foreach ($c in (Get-Descendants $page)) {
        if (-not $c.Visible) { continue }
        $type = $c.GetType().Name
        $text = [string]$c.Text

        # position inside the scrollable viewport (scroll-independent)
        $pt = $view.PointToClient($c.PointToScreen([System.Drawing.Point]::Empty))
        $right = $pt.X + $c.Width - $view.AutoScrollPosition.X
        $bottom = $pt.Y + $c.Height - $view.AutoScrollPosition.Y
        if ($c.Controls.Count -eq 0 -or $c -is [System.Windows.Forms.ComboBox]) {
            $maxRight = [Math]::Max($maxRight, $right); $maxBottom = [Math]::Max($maxBottom, $bottom)
        }

        if ($c -is [System.Windows.Forms.ComboBox]) {
            $widest = 0; $widestText = ''
            foreach ($item in $c.Items) {
                $s = $c.GetItemText($item); $w = $TR::MeasureText($s, $c.Font).Width
                if ($w -gt $widest) { $widest = $w; $widestText = $s }
            }
            $arrow = [System.Windows.Forms.SystemInformation]::VerticalScrollBarWidth
            if ($widest + $arrow + 8 -gt $c.Width) {
                Add-Finding $lang $pageName 'combo value clipped' $type $widestText "needs $($widest + $arrow + 8) px, box is $($c.Width)"
            }
            $scroll = 0; if ($c.Items.Count -gt $c.MaxDropDownItems) { $scroll = $arrow }
            if ($widest + 6 + $scroll -gt $c.DropDownWidth) {
                Add-Finding $lang $pageName 'combo list clipped' $type $widestText "needs $($widest + 6 + $scroll) px, drop-down is $($c.DropDownWidth)"
            }
            continue
        }
        if ($c -is [System.Windows.Forms.ListView]) {
            foreach ($col in $c.Columns) {
                $w = $TR::MeasureText([string]$col.Text, $c.Font).Width + 14
                if ($col.Width -gt 0 -and $w -gt $col.Width) { Add-Finding $lang $pageName 'column header clipped' 'ColumnHeader' $col.Text "needs $w px, column is $($col.Width)" }
            }
            continue
        }
        $isText = ($c -is [System.Windows.Forms.Label]) -or ($c -is [System.Windows.Forms.ButtonBase])
        if (-not $isText -or [string]::IsNullOrEmpty($text)) { continue }

        if (-not $c.AutoSize) {
            $pref = $c.GetPreferredSize((New-Object System.Drawing.Size $c.Width, 0))
            if ($pref.Width -gt $c.Width + 2 -or $pref.Height -gt $c.Height + 2) {
                Add-Finding $lang $pageName 'caption clipped' $type $text "needs $($pref.Width)x$($pref.Height), has $($c.Width)x$($c.Height)"
            }
        }
        if ($right -gt $viewW + 1 -and $c.Parent -eq $view -or ($right -gt $viewW + 1 -and $c.Controls.Count -eq 0)) {
            Add-Finding $lang $pageName 'past the right edge' $type $text "ends at $right px, page is $viewW px wide"
        }
        if ($c -is [System.Windows.Forms.ButtonBase] -and $text -notmatch '[\p{L}\p{N}]' -and [string]::IsNullOrEmpty($c.AccessibleName)) {
            Add-Finding $lang $pageName 'glyph without accessible name' $type $text 'Narrator reads the glyph or nothing'
        }
        $key = '{0} on {1}' -f $c.ForeColor.Name, (Get-EffectiveBack $c).Name
        if (-not $contrast.ContainsKey($key)) {
            $ratio = [AuditColor]::Contrast($c.ForeColor, (Get-EffectiveBack $c))
            $contrast[$key] = [pscustomobject]@{ Pair = $key; Ratio = [Math]::Round($ratio, 2); Example = (Get-Excerpt $text); Page = $pageName }
        }
    }
    $pageStats.Add([pscustomobject]@{
        Language = $lang; Page = $pageName
        ViewportW = $viewW; ViewportH = $viewH
        ContentRight = $maxRight; ContentBottom = $maxBottom
        WidthUsedPct = [int](100 * $maxRight / [Math]::Max(1, $viewW))
        HeightUsedPct = [int](100 * $maxBottom / [Math]::Max(1, $viewH))
        Screens = [Math]::Round($maxBottom / [Math]::Max(1, $viewH), 2)
    })
}

function Measure-TabStrip($lang, $tabs) {
    $icon = 0; if ($tabs.ImageList) { $icon = $tabs.ImageList.ImageSize.Width + 9 }
    $avail = $tabs.ItemSize.Height - 12 - $icon - 8
    $bold = New-Object System.Drawing.Font $tabs.Font, ([System.Drawing.FontStyle]::Bold)
    foreach ($p in $tabs.TabPages) {
        $w = $TR::MeasureText($p.Text, $bold).Width
        if ($w -gt $avail) { Add-Finding $lang $p.Text 'page name ellipsized' 'TabPage' $p.Text "needs $w px, strip leaves $avail px (ItemSize $($tabs.ItemSize))" }
    }
    $bold.Dispose()
}

# PrintWindow is not usable here: DWM does not recompose a window that sits outside every monitor, so
# PrintWindow(PW_RENDERFULLCONTENT) hands back stale pixels - measured 2026-09-24, one page's hint
# painted under another page's checkbox. DrawToBitmap goes through WM_PRINT and is exact for the
# client area; its title bar is drawn in the classic style, so the image is cropped to the client.
function Save-FormShot($form, [string]$path) {
    $full = New-Object System.Drawing.Bitmap $form.Width, $form.Height
    $form.DrawToBitmap($full, (New-Object System.Drawing.Rectangle 0, 0, $form.Width, $form.Height))
    # From the frame widths, not PointToScreen: a mirrored (RightToLeftLayout) form mirrors that too.
    $side = [int](($form.Width - $form.ClientSize.Width) / 2)
    $top = $form.Height - $form.ClientSize.Height - $side
    $client = New-Object System.Drawing.Rectangle $side, $top, $form.ClientSize.Width, $form.ClientSize.Height
    $crop = $full.Clone($client, $full.PixelFormat)
    $crop.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $crop.Dispose(); $full.Dispose()
}

# A caption that measures as fitting can still be missing on screen: in Hindi and Bengali only the
# selected page name was drawn (found 2026-09-24, also visible in the July Store screenshots). So the
# page list is also checked in pixels - every tab's caption area must hold some ink.
function Test-TabInk($lang, $tabs, $form, [string]$shotPath) {
    $bmp = New-Object System.Drawing.Bitmap $shotPath
    try {
        $iconW = 0; if ($tabs.ImageList) { $iconW = $tabs.ImageList.ImageSize.Width + 9 }
        for ($i = 0; $i -lt $tabs.TabPages.Count; $i++) {
            $r = $tabs.GetTabRect($i)
            $pt = $form.PointToClient($tabs.PointToScreen($r.Location))
            $x0 = $pt.X + 12 + $iconW; $x1 = $pt.X + $r.Width - 8
            if ($form.RightToLeftLayout) { $x0 = $pt.X - $r.Width + 8; $x1 = $pt.X - 12 - $iconW }   # mirrored client
            $ink = 0
            for ($y = [Math]::Max(0, $pt.Y + 3); $y -lt [Math]::Min($bmp.Height, $pt.Y + $r.Height - 3); $y++) {
                for ($x = [Math]::Max(0, $x0); $x -lt [Math]::Min($bmp.Width, $x1); $x++) {
                    $brightness = $bmp.GetPixel($x, $y).GetBrightness()
                    if (($inkIsLight -and $brightness -gt 0.55) -or (-not $inkIsLight -and $brightness -lt 0.45)) { $ink++ }
                }
            }
            if ($ink -lt 25) { Add-Finding $lang $tabs.TabPages[$i].Text 'page name not drawn' 'TabPage' $tabs.TabPages[$i].Text "$ink dark pixels in the caption area" }
        }
    }
    finally { $bmp.Dispose() }
}

function Get-OffscreenPoint([System.Drawing.Size]$size) {
    $screens = [System.Windows.Forms.Screen]::AllScreens
    if ($Monitor -lt 0) { $target = [System.Windows.Forms.Screen]::PrimaryScreen } else { $target = $screens[$Monitor] }
    $b = $target.Bounds; $gap = 40
    $candidates = @(
        (New-Object System.Drawing.Point ($b.Right + $gap), $b.Top),
        (New-Object System.Drawing.Point ($b.Left - $size.Width - $gap), $b.Top),
        (New-Object System.Drawing.Point $b.Left, ($b.Bottom + $gap)),
        (New-Object System.Drawing.Point $b.Left, ($b.Top - $size.Height - $gap)))
    foreach ($pt in $candidates) {
        $rect = New-Object System.Drawing.Rectangle $pt, $size
        $hit = $false; foreach ($s in $screens) { if ($s.Bounds.IntersectsWith($rect)) { $hit = $true } }
        if ($hit) { continue }
        # the window must be nearest to the target monitor, or it takes another monitor's DPI
        $best = $null; $bestD = [double]::MaxValue
        foreach ($s in $screens) {
            $dx = [Math]::Max(0, [Math]::Max($s.Bounds.Left - $rect.Right, $rect.Left - $s.Bounds.Right))
            $dy = [Math]::Max(0, [Math]::Max($s.Bounds.Top - $rect.Bottom, $rect.Top - $s.Bounds.Bottom))
            $d = [Math]::Sqrt($dx * $dx + $dy * $dy)
            if ($d -lt $bestD) { $bestD = $d; $best = $s }
        }
        if ($best.DeviceName -eq $target.DeviceName) { return $pt }
    }
    throw "No off-screen spot is nearest to monitor $($target.DeviceName); pick another -Monitor."
}

# ---------------------------------------------------------------- run
$regPath = 'HKCU:\Software\CyrFlip'
$savedTab = (Get-ItemProperty $regPath -Name SettingsTab -ErrorAction SilentlyContinue).SettingsTab
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$noB = [Action[bool]] { }; $noI = [Action[int]] { }; $noS = [Action[string]] { }; $no = [Action] { }

# The app theme (ticket S0020): started in this process exactly as the tray context starts it, so the
# window paints itself dark (or light) when its handle is created - the same path, not a re-colouring
# done by this script. "system" follows this machine's own Windows setting.
if ($Theme) {
    $modes = $asm.GetType('CyrFlip.ThemeModes', $true)
    $manager = $asm.GetType('CyrFlip.ThemeManager', $true)
    $mode = $modes.GetMethod('Parse').Invoke($null, @($Theme))
    [void]$manager.GetMethod('Initialize').Invoke($null, @($mode))
    "theme: $Theme -> $($manager.GetProperty('Kind').GetValue($null))"
}
$inkIsLight = $Theme -and "$($asm.GetType('CyrFlip.ThemeManager', $true).GetProperty('Kind').GetValue($null))" -eq 'Dark'
$export = [Func[string, bool, string]] { param($a, $b) '' }
$started = Get-Date

try {
    foreach ($code in $Language) {
        $lang = $names[[Array]::IndexOf($codes, $code)]
        $dir = Join-Path $OutDir $code
        New-Item -ItemType Directory -Force -Path $dir | Out-Null

        if ($RealConfig) { $cfg = $T.Config.GetMethod('Load').Invoke($null, @()) } else { $cfg = [Activator]::CreateInstance($T.Config) }
        $cfg.UiLanguage = $lang
        $cfg.SettingsTab = 0
        if ($AllModules) {
            $cfg.EnableClipboardHistory = $true; $cfg.EnableContextMenu = $true; $cfg.EnableScenarioLauncher = $true
            $cfg.EnableTranslate = $true; $cfg.EnableQuickNotes = $true
        }
        if ($RealConfig) { $storePath = Join-Path $env:APPDATA 'CyrFlip\Scenarios' }
        else { $storePath = Join-Path ([IO.Path]::GetTempPath()) ('CyrFlipAudit\' + [guid]::NewGuid().ToString('N')) }
        $store = [Activator]::CreateInstance($T.Store, [object[]]@([string]$storePath, $false))   # [string]: a PSObject-wrapped path matches no constructor

        $form = [Activator]::CreateInstance($T.Form, [object[]]@(
                $cfg, $noB, $noB, $noB, $noB, $noB, $noB, $noB, $noB, $noB, $noI, $noS, $no, $no, $no, $no, $no,
                $noB, $noB, $noB, $noB, $noB, $noB, $store, $noB, $no, $no, $no, $export))
        try {
            $form.ShowInTaskbar = $false
            $form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
            if ($OnScreen) {
                $screen = [System.Windows.Forms.Screen]::PrimaryScreen
                if ($Monitor -ge 0) { $screen = [System.Windows.Forms.Screen]::AllScreens[$Monitor] }
                $form.Location = New-Object System.Drawing.Point ($screen.WorkingArea.Left + 20), ($screen.WorkingArea.Top + 20)
            }
            else { $form.Location = Get-OffscreenPoint $form.Size }
            $form.Show()
            if ($OnScreen) { [void][AuditColor]::SetWindowPos($form.Handle, [IntPtr]1, 0, 0, 0, 0, 0x13) }   # HWND_BOTTOM; NOSIZE|NOMOVE|NOACTIVATE
            for ($k = 0; $k -lt 5; $k++) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 60 }
            $tabs = $T.Form.GetField('_tabs', [Reflection.BindingFlags]'NonPublic,Instance').GetValue($form)
            $dpi = [AuditColor]::GetDpiForWindow($form.Handle)
            "[$code] window $($form.Width)x$($form.Height) at $($form.Location), window DPI $dpi, font $($form.Font.Name) $($form.Font.SizeInPoints)pt, $($tabs.TabPages.Count) pages"
            Measure-TabStrip $lang $tabs

            for ($i = 0; $i -lt $tabs.TabPages.Count; $i++) {
                if ($Page -ge 0 -and $i -ne $Page) { continue }
                $tabs.SelectedIndex = $i
                # A full repaint before every capture: PrintWindow reads the DWM surface, and without
                # it the previous page's scrolled content survives in the new page's image.
                $form.Refresh()
                for ($k = 0; $k -lt 4; $k++) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 40 }
                $tabPage = $tabs.TabPages[$i]          # not $page: PowerShell names ignore case, and $Page is the [int] parameter
                Measure-Page $lang $tabPage.Text $tabPage $form
                $view = Get-Viewport $tabPage
                $total = $view.DisplayRectangle.Height; $step = [Math]::Max(100, [int]($view.ClientSize.Height * 0.9))
                $part = 0
                for ($y = 0; $part -eq 0 -or $y -lt $total - $view.ClientSize.Height + $step; $y += $step) {
                    $view.AutoScrollPosition = New-Object System.Drawing.Point 0, $y
                    $form.Refresh()
                    for ($k = 0; $k -lt 3; $k++) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 30 }
                    $name = if ($part -eq 0) { 'tab{0:d2}.png' -f $i } else { 'tab{0:d2}-{1}.png' -f $i, $part }
                    Save-FormShot $form (Join-Path $dir $name)
                    if ($i -eq 0 -and $part -eq 0) { Test-TabInk $lang $tabs $form (Join-Path $dir $name) }
                    $part++
                    if ($total -le $view.ClientSize.Height -or $part -ge 8) { break }
                }
                $view.AutoScrollPosition = New-Object System.Drawing.Point 0, 0
            }
        }
        finally { $form.Hide(); $form.Dispose() }
    }
}
finally {
    if ($null -ne $savedTab) { Set-ItemProperty $regPath -Name SettingsTab -Value $savedTab -Type DWord }
}

# ---------------------------------------------------------------- report
$report = Join-Path $OutDir 'report.txt'
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("Settings window audit - $($started.ToString('yyyy-MM-dd HH:mm')) - $exe")
$lines.Add("Languages: $($Language -join ', ')  RealConfig=$RealConfig AllModules=$AllModules Monitor=$Monitor Theme=$Theme")
$lines.Add('')
$lines.Add("== Findings ($($findings.Count))")
foreach ($g in ($findings | Group-Object Kind | Sort-Object Count -Descending)) {
    $lines.Add("-- $($g.Name): $($g.Count)")
    foreach ($f in $g.Group) { $lines.Add("   [$($f.Language)] $($f.Page) | $($f.Control) '$($f.Text)' | $($f.Detail)") }
}
$lines.Add('')
$lines.Add('== Page use (content extent vs the visible page; Screens > 1 means the page scrolls)')
foreach ($p in $pageStats) { $lines.Add(('   [{0}] {1} | width used {2}% | height used {3}% | {4} screens | viewport {5}x{6}' -f $p.Language, $p.Page, $p.WidthUsedPct, $p.HeightUsedPct, $p.Screens, $p.ViewportW, $p.ViewportH)) }
$lines.Add('')
$lines.Add('== Text contrast (WCAG ratio; 4.5 is the AA floor for body text)')
foreach ($c in ($contrast.Values | Sort-Object Ratio)) { $lines.Add(('   {0,5} {1} (e.g. "{2}" on {3})' -f $c.Ratio, $c.Pair, $c.Example, $c.Page)) }
$lines | Set-Content -Path $report -Encoding UTF8
$findings | Export-Csv -Path (Join-Path $OutDir 'findings.csv') -NoTypeInformation -Encoding UTF8
$pageStats | Export-Csv -Path (Join-Path $OutDir 'pages.csv') -NoTypeInformation -Encoding UTF8
"report: $report  ($($findings.Count) findings)"

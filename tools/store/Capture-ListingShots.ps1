# Store listing shots: one picture per pillar of POSITIONING.md, in the 13 interface languages, composed onto the
# 1366x768 canvas Partner Center wants (ticket S0058, P5 - the shots outside the settings window).
#
#   .\tools\store\Capture-ListingShots.ps1 -Plan                        # the matrix and the data check, writes nothing (about 1 s)
#   .\tools\store\Capture-ListingShots.ps1                              # all five shots, all 13 languages -> msix\screenshots\listing
#   .\tools\store\Capture-ListingShots.ps1 -Language de,ar -Shot menu   # just these
#
# The five shots (the file number is the listing order):
#   01-indicator          pillar 1  the layout marker beside the text caret of a real text box   (live app, ONE file for every language)
#   02-menu-<lang>        pillar 2  CyrFlip's own menu over a selection: link, conversion, case, translate, launcher, ..
#   03-translate-<lang>   pillar 2  the translation popup while the answer streams in
#   04-notes-<lang>       pillar 3  quick notes with a note open
#   05-launcher-<lang>    pillar 4  the quick-launch menu the taskbar button drops down
# The sixth kind of the listing - the settings window in the dark theme - stays with tools\uitest\Save-StoreScreenshots.ps1
# and the guide shots with tools\site\capture-guide-locales.ps1; both wait for the release and for S0060, S0062 and S0076.
#
# How each one is made, and what that costs the person running it:
#   * 02-05 are the app's OWN windows and menus, built in this process from the built exe by reflection (the way
#     Test-ClipboardFlip.ps1 reaches ClipboardHandler) over neutral sample data, and drawn with DrawToBitmap. Windows are
#     hosted in a panel and never shown on the desktop; the two menus are shown for an instant at the top left of the
#     screen, because a drop-down cannot be placed anywhere else. Nothing here reads or writes the real settings, the
#     clipboard or the real notes: the notes journal is a temporary file, deleted at the end.
#   * 01 needs the live app: the caret badge is excluded from every screen capture on purpose (WDA_EXCLUDEFROMCAPTURE), so
#     the shot is composed from PrintWindow of a plain text box and of the badge's own window, the badge blended in at
#     LayoutStyle.MarkerOpacity as on screen. It restarts the tray app, writes EnableCaretOverlay / CaretDotMode for the
#     run and puts every value back, and the tray app is started again at the end. Needs the Russian keyboard installed.
#   * Why not drive the live app for 02-05 too: the chords are injected input, which the hooks ignore by design, and the
#     pipe has no command that opens these windows.
#
# Every picture is checked for being drawn (not blank) before it is written, and a shot that fails is reported and does not
# stop the others. Sample text is neutral (no personal data); still look at every PNG before it is committed.
#
# This is an operator-run, desktop-bound check: it runs at the release, or when the owner names it (CLAUDE.md "Build, run,
# test"). Nothing here is wired into build.ps1, dotnet test or CI.
#
# Exit codes (CHECK-VERDICT): 0 every capture written, 1 a capture failed, 2 could not run (no built exe). The last line
# is "listing-shots: PASS|FAIL (n)|NOT VERIFIED (n)".
[CmdletBinding()]
param(
    [string[]]$Language,
    [string[]]$Shot,
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [int]$Width = 1366,
    [int]$Height = 768,
    [string]$OutDir = '',
    [switch]$Plan
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
# Not a parameter default: Windows PowerShell 5.1 leaves $PSScriptRoot empty there.
if (-not $OutDir) { $OutDir = Join-Path $repo 'msix\screenshots\listing' }

# Windows PowerShell, STA: the windows are WinForms, built in this process. A -File argument arrives as one string.
$Language = @($Language | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$Shot = @($Shot | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
foreach ($name in $Shot) {
    if ($name -notin 'indicator', 'menu', 'translate', 'notes', 'launcher') { throw "Unknown -Shot '$name'. Known: indicator, menu, translate, notes, launcher." }
}
# -Plan builds no window, so it runs in whatever shell it was typed in.
if (-not $Plan -and ($PSVersionTable.PSEdition -eq 'Core' -or [Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA')) {
    $again = @('-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath, '-Configuration', $Configuration,
        '-Width', $Width, '-Height', $Height, '-OutDir', $OutDir)
    if ($Language.Count) { $again += @('-Language', ($Language -join ',')) }
    if ($Shot.Count) { $again += @('-Shot', ($Shot -join ',')) }
    if ($Plan) { $again += '-Plan' }
    & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" @again
    exit $LASTEXITCODE
}

# ---- The data: the 13 languages and the neutral sample text each shot needs ---------------------------------------------
# Name is the endonym as AppConfig stores it; File is the Partner Center listing code the files are named by. Source is
# what the user "selected" (Russian for everyone but the Russian UI, which gets English), Result the same sentence
# translated into the UI language; both are one short sentence about meeting at a station.
$languages = @(
    @{ File = 'en-us';   Name = 'English';    Rtl = $false; Source = 'Встретимся на вокзале в шесть часов.'; Result = "Let's meet at the station at six o'clock." }
    @{ File = 'ru';      Name = 'Русский';    Rtl = $false; Source = "Let's meet at the station at six o'clock."; Result = 'Давайте встретимся на вокзале в шесть часов.' }
    @{ File = 'uk';      Name = 'Українська'; Rtl = $false; Source = 'Встретимся на вокзале в шесть часов.'; Result = 'Зустрінемося на вокзалі о шостій годині.' }
    @{ File = 'de';      Name = 'Deutsch';    Rtl = $false; Source = 'Встретимся на вокзале в шесть часов.'; Result = 'Treffen wir uns um sechs Uhr am Bahnhof.' }
    @{ File = 'it';      Name = 'Italiano';   Rtl = $false; Source = 'Встретимся на вокзале в шесть часов.'; Result = 'Ci vediamo alla stazione alle sei.' }
    @{ File = 'es';      Name = 'Español';    Rtl = $false; Source = 'Встретимся на вокзале в шесть часов.'; Result = 'Nos vemos en la estación a las seis.' }
    @{ File = 'fr';      Name = 'Français';   Rtl = $false; Source = 'Встретимся на вокзале в шесть часов.'; Result = 'Retrouvons-nous à la gare à six heures.' }
    @{ File = 'pt-br';   Name = 'Português';  Rtl = $false; Source = 'Встретимся на вокзале в шесть часов.'; Result = 'Vamos nos encontrar na estação às seis horas.' }
    @{ File = 'zh-hans'; Name = '中文';        Rtl = $false; Source = 'Встретимся на вокзале в шесть часов.'; Result = '我们六点在车站见面吧。' }
    @{ File = 'hi';      Name = 'हिन्दी';       Rtl = $false; Source = 'Встретимся на вокзале в шесть часов.'; Result = 'चलो छह बजे स्टेशन पर मिलते हैं।' }
    @{ File = 'bn';      Name = 'বাংলা';       Rtl = $false; Source = 'Встретимся на вокзале в шесть часов.'; Result = 'চলো ছয়টায় স্টেশনে দেখা করি।' }
    @{ File = 'ar';      Name = 'العربية';     Rtl = $true;  Source = 'Встретимся на вокзале в шесть часов.'; Result = 'لنلتقِ في المحطة في الساعة السادسة.' }
    @{ File = 'ur';      Name = 'اردو';        Rtl = $true;  Source = 'Встретимся на вокзале в шесть часов.'; Result = 'آئیں چھ بجے اسٹیشن پر ملتے ہیں۔' }
)
if ($Language.Count) {
    $languages = @($languages | Where-Object { $Language -contains $_.File -or $Language -contains $_.Name })
    if (-not $languages.Count) { throw "No language matched: $($Language -join ', ')" }
}

# The selection of the menu shot is a link, so the menu shows its "open" item beside the conversions and the translations.
$menuSelection = 'https://example.org/menu'
$sampleLauncher = @('Calculator', 'Notepad', 'Paint', 'Video download (yt-dlp)')
$sampleNotes = @(
    @{ Title = 'ssh';    Body = 'ssh deploy@demo.example.test -p 2222' }
    @{ Title = 'Commit'; Body = 'git commit -m "Fix layout switch"' }
)

# The shot table. Driver is filled below; PerLanguage false means one file for every language.
$shots = @(
    @{ Id = 'indicator'; Number = '01'; PerLanguage = $false; MaxScale = 2.0; Driver = $null }
    @{ Id = 'menu';      Number = '02'; PerLanguage = $true;  MaxScale = 2.0; Driver = $null }
    @{ Id = 'translate'; Number = '03'; PerLanguage = $true;  MaxScale = 1.5; Driver = $null }
    @{ Id = 'notes';     Number = '04'; PerLanguage = $true;  MaxScale = 1.2; Driver = $null }
    @{ Id = 'launcher';  Number = '05'; PerLanguage = $true;  MaxScale = 2.0; Driver = $null }
)
if ($Shot.Count) { $shots = @($shots | Where-Object { $Shot -contains $_.Id }) }

function Get-ShotPath($shotRow, $lang) {
    $name = if ($shotRow.PerLanguage) { '{0}-{1}-{2}.png' -f $shotRow.Number, $shotRow.Id, $lang.File } else { '{0}-{1}.png' -f $shotRow.Number, $shotRow.Id }
    Join-Path $OutDir $name
}

function Get-Matrix {
    foreach ($row in $shots) {
        $targets = if ($row.PerLanguage) { $languages } else { @($languages | Select-Object -First 1) }
        foreach ($lang in $targets) { [pscustomobject]@{ Shot = $row.Id; Language = $lang.File; File = (Get-ShotPath $row $lang) } }
    }
}

# -Plan: the data is complete and every shot has what it needs. This is the part that costs a second, so it is the part
# that can run between releases.
if ($Plan) {
    $problems = New-Object System.Collections.Generic.List[string]
    foreach ($lang in $languages) {
        foreach ($key in 'File', 'Name', 'Source', 'Result') { if ([string]::IsNullOrWhiteSpace($lang[$key])) { $problems.Add("$($lang.File): $key is empty") } }
        if ($lang.Source -eq $lang.Result) { $problems.Add("$($lang.File): the sample result equals its source") }
        if (($lang.File -in 'ar', 'ur') -ne $lang.Rtl) { $problems.Add("$($lang.File): Rtl is wrong") }
    }
    if ((@($languages | ForEach-Object { $_.File } | Select-Object -Unique)).Count -ne $languages.Count) { $problems.Add('a file code is used twice') }
    if (-not $Language.Count -and $languages.Count -ne 13) { $problems.Add("expected 13 languages, found $($languages.Count)") }
    $matrix = @(Get-Matrix)
    if ((@($matrix | Select-Object -ExpandProperty File -Unique)).Count -ne $matrix.Count) { $problems.Add('two captures write one file') }
    $matrix | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
    foreach ($p in $problems) { Write-Host "  PROBLEM: $p" }
    if ($problems.Count) { Write-Output "listing-shots plan: FAIL ($($problems.Count))"; exit 1 }
    Write-Output "listing-shots plan: PASS ($($matrix.Count) captures planned)"
    exit 0
}

# ---- Everything below needs the built exe and a desktop ----------------------------------------------------------------
Import-Module (Join-Path $repo 'tools\uitest\CyrFlip.UiTest.psm1') -Force
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Enable-UiTestDpi | Out-Null
[System.Windows.Forms.Application]::EnableVisualStyles()

try { $liveExe = Get-CyrFlipExe -Configuration $Configuration }
catch { Write-Host $_.Exception.Message; Write-Output 'listing-shots: NOT VERIFIED (1)'; exit 2 }

# A copy of the build output, so loading it never locks the folder the next build writes to. Copies of earlier runs go
# first: a loaded assembly cannot be deleted by the process that holds it, so each run clears the runs before it.
Get-ChildItem (Join-Path $repo 'artifacts\listing-shots-staging') -Directory -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
$staging = Join-Path $repo ('artifacts\listing-shots-staging\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $staging | Out-Null
Copy-Item -Path (Join-Path (Split-Path $liveExe -Parent) '*') -Destination $staging -Recurse
$script:App = [Reflection.Assembly]::LoadFrom((Join-Path $staging 'CyrFlip.exe'))
$script:Flags = [Reflection.BindingFlags]'Instance,Static,Public,NonPublic'

# ---- Reflection helpers: the app's types are internal, so none of them can be named in a type literal -------------------
function Get-AppType([string] $name) {
    $t = $script:App.GetType('CyrFlip.' + $name, $false)
    if (-not $t) { throw "CyrFlip.$name is not in the built exe." }
    $t
}
# Instance constructors only. With Static among the flags a type that has a static constructor (AppConfig does) answers a
# call with no arguments with AmbiguousMatchException: the class initializer is a second parameterless candidate.
function New-AppObject([string] $typeName, [object[]] $arguments = @()) {
    $constructors = [Reflection.BindingFlags]'Instance,Public,NonPublic'
    [Activator]::CreateInstance((Get-AppType $typeName), $constructors, $null, $arguments, $null)
}
function Select-Method([Type] $type, [string] $name, [int] $count) {
    $m = @($type.GetMethods($script:Flags) | Where-Object { $_.Name -eq $name -and $_.GetParameters().Count -eq $count }) | Select-Object -First 1
    if (-not $m) { throw "$($type.Name).$name with $count parameters was not found." }
    $m
}
function Invoke-AppMember($instance, [string] $name, [object[]] $arguments = @()) {
    (Select-Method $instance.GetType() $name $arguments.Count).Invoke($instance, $arguments)
}
function Invoke-AppStatic([string] $typeName, [string] $name, [object[]] $arguments = @()) {
    (Select-Method (Get-AppType $typeName) $name $arguments.Count).Invoke($null, $arguments)
}
function Set-AppProperty($instance, [string] $name, $value) {
    $p = $instance.GetType().GetProperty($name, $script:Flags)
    if (-not $p) { throw "$($instance.GetType().Name).$name was not found." }
    $p.SetValue($instance, $value)
}
function ConvertTo-AppDelegate([scriptblock] $body, [Type] $delegateType) {
    [System.Management.Automation.LanguagePrimitives]::ConvertTo($body, $delegateType)
}
# The type goes into a variable first: in Windows PowerShell 5.1 ([type]$definition).MakeGenericType($arguments), called on the
# cast expression itself, reaches Type's own method and fails with "Derived classes must provide an implementation".
function Get-GenericType([string] $definition, [Type[]] $arguments) {
    $generic = [type]$definition
    $generic.MakeGenericType($arguments)
}

function Get-UiCode([string] $languageName) {
    $localization = Get-AppType 'Localization'
    $names = @($localization.GetMember('Names', $script:Flags)[0].GetValue($null))
    $codes = @($localization.GetMember('Codes', $script:Flags)[0].GetValue($null))
    $i = [Array]::IndexOf($names, $languageName)
    if ($i -lt 0) { throw "'$languageName' is not one of Localization.Names." }
    $codes[$i]
}
function Get-Translated([string] $languageName, [string] $key) { Invoke-AppStatic 'Localization' 'Translate' @($languageName, $key) }

function New-AppConfig([string] $languageName) {
    $config = New-AppObject 'AppConfig'
    Set-AppProperty $config 'UiLanguage' $languageName
    $config
}

# ---- Drawing helpers ---------------------------------------------------------------------------------------------------
# Same hosting as tools\help: the form is a child of a panel, shown, and drawn into a bitmap - it never appears on the screen.
# Prepare runs once the form is shown, for what only a shown window can do.
function Save-HostedForm($form, [int] $formWidth, [int] $formHeight, [scriptblock] $prepare = $null) {
    $hostPanel = New-Object System.Windows.Forms.Panel
    $hostPanel.Size = New-Object System.Drawing.Size(2600, 2600)
    [void]$hostPanel.Handle
    try {
        $form.TopLevel = $false
        $form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
        if ($formWidth -gt 0) { $form.Size = New-Object System.Drawing.Size($formWidth, $formHeight) }
        $hostPanel.Controls.Add($form)
        $form.Show()
        $form.Location = New-Object System.Drawing.Point(0, 0)
        if ($prepare) { & $prepare $form }
        [System.Windows.Forms.Application]::DoEvents()
        [System.Windows.Forms.Application]::DoEvents()
        $bitmap = New-Object System.Drawing.Bitmap($form.Width, $form.Height)
        $form.DrawToBitmap($bitmap, (New-Object System.Drawing.Rectangle(0, 0, $form.Width, $form.Height)))
        $bitmap
    }
    finally { $form.Dispose(); $hostPanel.Dispose() }
}

# A drop-down is a window of its own and the shell keeps it on a screen, so it is shown at the top left for an instant.
function Save-DropDown($menu) {
    $menu.Show((New-Object System.Drawing.Point(40, 40)))
    try {
        [System.Windows.Forms.Application]::DoEvents()
        [System.Threading.Thread]::Sleep(250)
        [System.Windows.Forms.Application]::DoEvents()
        $bitmap = New-Object System.Drawing.Bitmap($menu.Width, $menu.Height)
        $menu.DrawToBitmap($bitmap, (New-Object System.Drawing.Rectangle(0, 0, $menu.Width, $menu.Height)))
        $bitmap
    }
    finally { $menu.Close(); $menu.Dispose() }
}

# A picture that is one flat colour, or two, is a failed capture (PrintWindow and DrawToBitmap answer blank, not an error).
function Assert-Drawn([System.Drawing.Bitmap] $bitmap, [string] $what) {
    $seen = New-Object 'System.Collections.Generic.HashSet[int]'
    $stepX = [Math]::Max(1, [int]($bitmap.Width / 40))
    $stepY = [Math]::Max(1, [int]($bitmap.Height / 40))
    for ($y = 0; $y -lt $bitmap.Height; $y += $stepY) {
        for ($x = 0; $x -lt $bitmap.Width; $x += $stepX) {
            $c = $bitmap.GetPixel($x, $y)
            [void]$seen.Add((($c.R -shr 3) -shl 10) -bor (($c.G -shr 3) -shl 5) -bor ($c.B -shr 3))
        }
    }
    if ($seen.Count -lt 6) { throw "$what came back blank ($($seen.Count) distinct colours in a $($bitmap.Width)x$($bitmap.Height) picture)." }
}

# The subject centred on a dark gradient, scaled up at most MaxScale and down as far as it must. No shadow (translucent
# slabs stack into a black one), only a hairline to seat a light window on the dark ground.
function Write-Canvas([System.Drawing.Bitmap] $subject, [string] $path, [double] $maxScale) {
    $canvas = New-Object System.Drawing.Bitmap($Width, $Height)
    try {
        $g = [System.Drawing.Graphics]::FromImage($canvas)
        try {
            $g.SmoothingMode = 'AntiAlias'
            $g.InterpolationMode = 'HighQualityBicubic'
            $g.PixelOffsetMode = 'HighQuality'
            $rect = New-Object System.Drawing.Rectangle(0, 0, $Width, $Height)
            $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect,
                [System.Drawing.Color]::FromArgb(16, 22, 30), [System.Drawing.Color]::FromArgb(38, 50, 66),
                [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
            $g.FillRectangle($brush, $rect)
            $brush.Dispose()
            $margin = 48
            $scale = [Math]::Min($maxScale, [Math]::Min(($Width - 2 * $margin) / $subject.Width, ($Height - 2 * $margin) / $subject.Height))
            $w = [int]($subject.Width * $scale)
            $h = [int]($subject.Height * $scale)
            $x = [int](($Width - $w) / 2)
            $y = [int](($Height - $h) / 2)
            $g.DrawImage($subject, $x, $y, $w, $h)
            $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(60, 255, 255, 255))
            $g.DrawRectangle($pen, $x - 1, $y - 1, $w + 1, $h + 1)
            $pen.Dispose()
        }
        finally { $g.Dispose() }
        $canvas.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $canvas.Dispose() }
}

# ---- 02: CyrFlip's own menu over a selection --------------------------------------------------------------------------
$noOp = { param($a) }
function Add-SampleLauncher($parent, $menuSurface, [string] $languageName) {
    # One list for both surfaces: the submenu of the text menu and the taskbar button's menu are built by the same code.
    $scenarioType = Get-AppType 'LauncherScenario'
    $list = [Activator]::CreateInstance((Get-GenericType 'System.Collections.Generic.List`1' @($scenarioType)))
    foreach ($name in $sampleLauncher) {
        $scenario = [Activator]::CreateInstance($scenarioType)
        $scenario.Name = $name
        $list.Add($scenario)
    }
    $run = ConvertTo-AppDelegate $noOp (Get-GenericType 'System.Action`1' @($scenarioType))
    $manage = [Action]{ }
    $translate = [Func[string, string]]{ param($key) Get-Translated $script:CurrentLanguage $key }
    $rebuild = @(([type](Get-AppType 'LauncherTrayMenu')).GetMethods($script:Flags) | Where-Object {
            $_.Name -eq 'Rebuild' -and $_.GetParameters()[0].ParameterType -eq $(if ($parent) { [System.Windows.Forms.ToolStripMenuItem] } else { [System.Windows.Forms.ContextMenuStrip] }) }) |
        Select-Object -First 1
    if (-not $rebuild) { throw 'LauncherTrayMenu.Rebuild for this surface was not found.' }
    $target = if ($parent) { $parent } else { $menuSurface }
    [void]$rebuild.Invoke($null, @($target, $list, $run, $manage, $null, $translate))
}

$script:CurrentLanguage = 'English'
$shots | Where-Object { $_.Id -eq 'menu' } | ForEach-Object {
    $_.Driver = {
        param($lang)
        $script:CurrentLanguage = $lang.Name
        $state = New-AppObject 'TextContextMenuState'
        Set-AppProperty $state 'Selection' ([Enum]::Parse((Get-AppType 'SelectionState'), 'Present'))
        Set-AppProperty $state 'ClipboardHasText' $true
        Set-AppProperty $state 'SelectionLines' 1
        Set-AppProperty $state 'SelectionChars' $menuSelection.Length
        Set-AppProperty $state 'CaseShortcut' 'Ctrl+Shift+F11'
        foreach ($flag in 'ShowLauncher', 'ShowHistory', 'ShowQuickNotes', 'ShowSymbols') { Set-AppProperty $state $flag $true }

        # The "open" item exists only when the selection parses as something openable; a refusal is a failed shot, not a
        # menu without it, because the shot is meant to show it.
        $parse = @((Get-AppType 'LaunchTarget').GetMethods($script:Flags) | Where-Object {
                $_.Name -eq 'TryParse' -and $_.GetParameters().Count -eq 3 -and $_.GetParameters()[2].ParameterType -eq [Func[string, bool]] }) | Select-Object -First 1
        if (-not $parse) { throw 'LaunchTarget.TryParse(string, out LaunchTarget, Func<string,bool>) was not found.' }
        $parsed = [object[]]@($menuSelection, $null, $null)
        if (-not $parse.Invoke($null, $parsed)) { throw "LaunchTarget refused the sample selection $menuSelection." }
        Set-AppProperty $state 'Launch' $parsed[1]

        $rowType = Get-AppType 'TextMenuRow'
        $conversions = $state.GetType().GetProperty('Conversions').GetValue($state)
        $translations = $state.GetType().GetProperty('Translations').GetValue($state)
        $conversions.Add([Activator]::CreateInstance($rowType, [object[]]@('EN ⇄ RU', 'Ctrl+Shift+F12', 'conversion-1')))
        $conversions.Add([Activator]::CreateInstance($rowType, [object[]]@('EN ⇄ UK', 'Alt+Shift+F12', 'conversion-2')))
        foreach ($code in 'en', 'ru') {
            $label = Invoke-AppStatic 'TranslationLanguages' 'Label' @($code, $lang.Name)
            $translations.Add([Activator]::CreateInstance($rowType, [object[]]@($label, '', "translation-$code")))
        }

        $menu = New-Object System.Windows.Forms.ContextMenuStrip
        if ($lang.Rtl) { $menu.RightToLeft = [System.Windows.Forms.RightToLeft]::Yes }
        $translate = [Func[string, string]]{ param($key) Get-Translated $script:CurrentLanguage $key }
        $editCommand = ConvertTo-AppDelegate $noOp (Get-GenericType 'System.Action`1' @((Get-AppType 'ClipboardHandler+EditCommand')))
        $launchTarget = ConvertTo-AppDelegate $noOp (Get-GenericType 'System.Action`1' @((Get-AppType 'LaunchTarget')))
        $fillLauncher = [Action[System.Windows.Forms.ToolStripMenuItem]]{ param($parent) Add-SampleLauncher $parent $null $script:CurrentLanguage }
        $rebuild = Select-Method (Get-AppType 'TextContextMenu') 'Rebuild' 13
        [void]$rebuild.Invoke($null, @($menu, $state, $translate, $editCommand, $launchTarget, [Action[string]]{ param($id) }, [Action]{ },
                [Action[string]]{ param($id) }, [Action]{ }, $fillLauncher, [Action]{ }, [Action]{ }, [Action]{ }))
        Save-DropDown $menu
    }
}

# ---- 03: the translation popup while the answer streams ---------------------------------------------------------------
$shots | Where-Object { $_.Id -eq 'translate' } | ForEach-Object {
    $_.Driver = {
        param($lang)
        $config = New-AppConfig $lang.Name
        $window = New-AppObject 'TranslationResultWindow' @($config, $lang.Name)
        $status = Get-Translated $lang.Name 'Translating... (Esc to cancel)'
        Invoke-AppMember $window 'ShowTranslating' @((Get-UiCode $lang.Name), $lang.Source, $status, [IntPtr]::Zero) | Out-Null
        Invoke-AppMember $window 'ShowSourcePanel' @($true) | Out-Null
        # Streaming means part of the sentence is there: the first 60 per cent, the way a model's chunks arrive.
        $part = $lang.Result.Substring(0, [Math]::Max(1, [int]($lang.Result.Length * 0.6)))
        Invoke-AppMember $window 'AppendChunk' @($part) | Out-Null
        Save-HostedForm $window 0 0
    }
}

# ---- 04: quick notes with a note open -----------------------------------------------------------------------------------
$shots | Where-Object { $_.Id -eq 'notes' } | ForEach-Object {
    $_.Driver = {
        param($lang)
        $journal = Join-Path $env:TEMP ('cyrflip-listing-notes-' + [guid]::NewGuid().ToString('N') + '.log')
        $service = $null
        try {
            $cipher = (Get-AppType 'QuickNotesCipher').GetField('Dpapi', $script:Flags).GetValue($null)
            $store = New-AppObject 'QuickNotesStore' @($journal, $cipher)
            $service = New-AppObject 'QuickNotesService' @($store, $true)
            $kind = [Enum]::Parse((Get-AppType 'QuickNoteKind'), 'Text')
            foreach ($sample in $sampleNotes) {
                $note = Invoke-AppMember $service 'CreateDraft' @($kind)
                Set-AppProperty $note 'Title' $sample.Title
                Set-AppProperty $note 'RawText' $sample.Body
                if (-not (Invoke-AppMember $service 'Commit' @($note))) { throw "The sample note '$($sample.Title)' was not written." }
                [System.Threading.Thread]::Sleep(30)
            }
            $config = New-AppConfig $lang.Name
            $window = New-AppObject 'QuickNotesWindow' @($service, $config, [Action]{ }, [Action]{ }, $null)
            # The open note is the sentence of the language and the link - a fresh note, as the context menu's "save the
            # selection" opens it. Hosted, not shown on the desktop, so the window's own foreground handling stays out of it.
            $text = $lang.Result + "`r`n" + $menuSelection
            Save-HostedForm $window 900 620 { param($shown) Invoke-AppMember $shown 'StartNoteWithText' @($text) | Out-Null }
        }
        finally {
            if ($service) { $service.Dispose() }
            foreach ($file in $journal, ($journal + '.bak'), ($journal + '.tmp')) { Remove-Item $file -Force -ErrorAction SilentlyContinue }
        }
    }
}
# ---- 05: the quick-launch menu of the taskbar button ------------------------------------------------------------------
$shots | Where-Object { $_.Id -eq 'launcher' } | ForEach-Object {
    $_.Driver = {
        param($lang)
        $script:CurrentLanguage = $lang.Name
        $menu = New-Object System.Windows.Forms.ContextMenuStrip
        if ($lang.Rtl) { $menu.RightToLeft = [System.Windows.Forms.RightToLeft]::Yes }
        Add-SampleLauncher $null $menu $lang.Name
        Save-DropDown $menu
    }
}

# ---- 01: the layout marker beside the caret, on the live app --------------------------------------------------------------
$registryKey = 'HKCU:\Software\CyrFlip'
function Get-Value([string] $name) { (Get-ItemProperty $registryKey -Name $name -ErrorAction SilentlyContinue).$name }
function Set-Value([string] $name, $value, [string] $type) {
    if ($null -eq $value) { Remove-ItemProperty $registryKey -Name $name -ErrorAction SilentlyContinue; return }
    New-ItemProperty $registryKey -Name $name -Value $value -PropertyType $type -Force | Out-Null
}

$shots | Where-Object { $_.Id -eq 'indicator' } | ForEach-Object {
    $_.Driver = {
        param($lang)
        $saved = @{ EnableCaretOverlay = (Get-Value 'EnableCaretOverlay'); CaretDotMode = (Get-Value 'CaretDotMode') }
        $target = $null
        $temp = Join-Path $env:TEMP ('cyrflip-indicator-' + [guid]::NewGuid().ToString('N'))
        try {
            Stop-CyrFlipApp
            Set-Value 'EnableCaretOverlay' 1 'DWord'
            Set-Value 'CaretDotMode' 0 'DWord'
            Start-CyrFlipApp -Configuration $Configuration -Fresh | Out-Null

            $russian = @(Get-InstalledLayouts | Where-Object { ($_.Hkl.ToInt64() -band 0xFFFF) -eq 0x0419 }) | Select-Object -First 1
            if (-not $russian) { throw 'The Russian keyboard is not installed: the marker would have no second layout to show.' }
            $target = Start-TargetWindow -Title 'Notes'
            [System.Windows.Forms.SendKeys]::SendWait('Meet me at the station at six')
            [void][CyrFlipUi]::SwitchLayout($target.Handle, $russian.Hkl)
            Start-Sleep -Milliseconds 2500                      # the caret tracker's first sighting after a layout change

            $badge = @(Get-AppWindows | Where-Object { $_.Width -ge 6 -and $_.Width -le 160 -and $_.Height -ge 6 -and $_.Height -le 100 }) | Select-Object -First 1
            if (-not $badge) { throw 'The caret badge window was not found (the overlay is off, or the caret of the text box was not located).' }

            $window = Save-WindowShot -Handle $target.Handle -Path ($temp + '-window.png')
            $mark = Save-WindowShot -Handle $badge.Handle -Path ($temp + '-badge.png')
            $windowRect = New-Object CyrFlipUi+RECT
            $badgeRect = New-Object CyrFlipUi+RECT
            [void][CyrFlipUi]::GetWindowRect($target.Handle, [ref]$windowRect)
            [void][CyrFlipUi]::GetWindowRect($badge.Handle, [ref]$badgeRect)

            $opacity = 0.6
            try {
                $field = (Get-AppType 'LayoutStyle').GetField('MarkerOpacity', $script:Flags)
                $opacity = if ($field.IsLiteral) { [double]$field.GetRawConstantValue() } else { [double]$field.GetValue($null) }
            }
            catch { }

            $windowImage = [System.Drawing.Image]::FromFile($window.Path)
            $badgeImage = [System.Drawing.Image]::FromFile($mark.Path)
            try {
                $composed = New-Object System.Drawing.Bitmap($windowImage.Width, $windowImage.Height)
                $g = [System.Drawing.Graphics]::FromImage($composed)
                try {
                    $g.DrawImage($windowImage, 0, 0, $windowImage.Width, $windowImage.Height)
                    $matrix = New-Object System.Drawing.Imaging.ColorMatrix
                    $matrix.Matrix33 = [single]$opacity
                    $attributes = New-Object System.Drawing.Imaging.ImageAttributes
                    $attributes.SetColorMatrix($matrix)
                    $destination = New-Object System.Drawing.Rectangle(($badgeRect.Left - $windowRect.Left), ($badgeRect.Top - $windowRect.Top), $badgeImage.Width, $badgeImage.Height)
                    $g.DrawImage($badgeImage, $destination, 0, 0, $badgeImage.Width, $badgeImage.Height, [System.Drawing.GraphicsUnit]::Pixel, $attributes)
                }
                finally { $g.Dispose() }
                $composed
            }
            finally { $windowImage.Dispose(); $badgeImage.Dispose() }
        }
        finally {
            if ($target) { Stop-Process -Id $target.Process.Id -Force -ErrorAction SilentlyContinue }
            Stop-CyrFlipApp
            Set-Value 'EnableCaretOverlay' $saved.EnableCaretOverlay 'DWord'
            Set-Value 'CaretDotMode' $saved.CaretDotMode 'DWord'
            foreach ($file in ($temp + '-window.png'), ($temp + '-badge.png')) { Remove-Item $file -Force -ErrorAction SilentlyContinue }
            Start-CyrFlipApp -Configuration $Configuration | Out-Null    # leave the tray app as we found it
        }
    }
}

# ---- Run ---------------------------------------------------------------------------------------------------------------
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$results = New-Object System.Collections.Generic.List[object]
try {
    foreach ($row in $shots) {
        $targets = if ($row.PerLanguage) { $languages } else { @($languages | Select-Object -First 1) }
        foreach ($lang in $targets) {
            $path = Get-ShotPath $row $lang
            $subject = $null
            try {
                $subject = & $row.Driver $lang
                if ($subject -is [array]) { $subject = $subject | Where-Object { $_ -is [System.Drawing.Bitmap] } | Select-Object -Last 1 }
                if ($subject -isnot [System.Drawing.Bitmap]) { throw 'The driver returned no picture.' }
                Assert-Drawn $subject ("$($row.Id)/$($lang.File)")
                Write-Canvas $subject $path $row.MaxScale
                $results.Add([pscustomobject]@{ Shot = $row.Id; Language = $lang.File; Subject = "$($subject.Width)x$($subject.Height)"; Result = 'ok'; File = (Split-Path $path -Leaf) })
                Write-Host ('  {0,-10} {1,-8} ok  {2}x{3}' -f $row.Id, $lang.File, $subject.Width, $subject.Height)
            }
            catch {
                $results.Add([pscustomobject]@{ Shot = $row.Id; Language = $lang.File; Subject = ''; Result = 'FAILED: ' + $_.Exception.Message; File = '' })
                Write-Host ('  {0,-10} {1,-8} FAILED  {2}' -f $row.Id, $lang.File, $_.Exception.Message)
            }
            finally { if ($subject -is [System.Drawing.Bitmap]) { $subject.Dispose() } }
        }
    }
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}

$results | Format-Table -AutoSize | Out-String -Width 220 | Write-Host
$failed = @($results | Where-Object { $_.Result -ne 'ok' }).Count
Write-Host "Written to $OutDir - look at every PNG for personal data before it is committed."
if ($failed) { Write-Output "listing-shots: FAIL ($failed)"; exit 1 }
Write-Output "listing-shots: PASS ($($results.Count))"
exit 0

<#
.SYNOPSIS
CU-3 of the runtime quality audit (ticket S0061, spec 5.5 CU item 3): what UI Automation reads from every
interactive control of every CyrFlip window - a name, a role and an automation id that mean something -
walked on a live, STAGED copy, page by page and window by window, in one or more UI languages.

.DESCRIPTION
SettingsAccessibilityTests holds the settings window's names in-process (WinForms properties). This script
reads what a screen reader actually gets, through UI Automation, and extends the walk to every window the
command line can open: every settings page (`show --page <key>`), the quick notes (`note show`), the
clipboard history strip (`clipboard-history show`), the symbols palette (`symbols show`) and the traffic
light board (`trafficlight show`). Each is a `safe` verb, so EnableExternalCommands stays off.

The copy runs in a throwaway profile (CyrFlip.AuditRig.psm1): the user's HKCU\Software\CyrFlip, Run value
and running instance are saved first and put back in finally; the user's data folder is never opened.
Restore-AuditProfile is the rescue for a run that died.

A control is a FINDING when it is interactive (Button, CheckBox, ComboBox, Edit, Hyperlink, ListItem,
MenuItem, RadioButton, Slider, Spinner, SplitButton, TabItem, DataItem, TreeItem, or any keyboard-focusable
element) and its UIA Name is
  - empty or whitespace                           -> reason 'no-name'
  - a designer identifier (button1, checkBox3..)  -> reason 'designer-name'
  - a vocabulary glyph id (action.add, nav.close) -> reason 'glyph-id'
  - its own control type or window class name     -> reason 'type-name'
Not covered here (spec 5.5 CU item 3, still open): focus order against reading order, live-region
announcement, and the 200 % text scale - those stay the by-hand half.

Output: one CSV of every inspected control and one JSON summary under -OutDir (default
temp\S0061\cu3-<stamp>). Exit codes (CHECK-VERDICT): 0 every control named, 1 at least one finding,
2 could not verify (not built, did not start, a window did not open, UIA unavailable).

Desktop-bound: it brings windows forward (focus moves). Above the 5 s line - an owner-named or release run.

.EXAMPLE
pwsh -File tools\uitest\Measure-AccessibleNames.ps1
pwsh -File tools\uitest\Measure-AccessibleNames.ps1 -Language English, Русский, العربية
pwsh -File tools\uitest\Measure-AccessibleNames.ps1 -AllLanguages
#>
[CmdletBinding()]
param(
    # UI language endonyms (the values of Localization.Names). Default: English only.
    [string[]]$Language = @('English'),
    # Every language the build ships (read from the exe by reflection).
    [switch]$AllLanguages,
    # Settings page keys of `show --page`; default every page the CLI lists.
    [string[]]$Page = @('general', 'indicators', 'hotkeys', 'conversions', 'windows-languages', 'clipboard',
        'quick-notes', 'screenshots', 'translate', 'launcher', 'about', 'symbols', 'selection-menu',
        'traffic-light', 'exchange-server'),
    # Skip the four module windows (notes, history, symbols, traffic light).
    [switch]$SettingsOnly,
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [string]$ExePath,
    [string]$OutDir,
    [int]$WindowTimeoutMs = 10000
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.AuditRig.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.UiTest.psm1') -Force
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repo 'tools\checks\CheckVerdict.psm1') -Force
Set-CheckSubject -Subject 'S0061 CU-3 accessible names'

$stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
if (-not $OutDir) { $OutDir = Join-Path $repo "temp\S0061\cu3-$stamp" }
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

try {
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
}
catch {
    Add-CheckFinding -Severity notverified -Name 'uia' -Reason "UI Automation assemblies did not load: $($_.Exception.Message)"
    Complete-Check
}
Enable-UiTestDpi

$exeSource = if ($ExePath) { (Resolve-Path $ExePath).Path } else { Join-Path $repo "src\CyrFlip\bin\$Configuration\net48\CyrFlip.exe" }
if (-not (Test-Path $exeSource)) {
    Add-CheckFinding -Severity notverified -Name 'not-built' -Reason "no build at $exeSource"
    Complete-Check
}

if ($AllLanguages) {
    # Read the shipped list rather than restating it: Localization.Names is the source.
    $asm = [Reflection.Assembly]::LoadFrom($exeSource)
    $field = $asm.GetType('CyrFlip.Localization', $true).GetField('Names', [Reflection.BindingFlags]'Static,Public,NonPublic')
    $Language = @($field.GetValue($null))
}

$ct = [System.Windows.Automation.ControlType]
$interactive = @($ct::Button, $ct::CheckBox, $ct::ComboBox, $ct::Edit, $ct::Hyperlink, $ct::ListItem, $ct::MenuItem,
    $ct::RadioButton, $ct::Slider, $ct::Spinner, $ct::SplitButton, $ct::TabItem, $ct::DataItem, $ct::TreeItem)
$designer = '^(button|checkbox|combobox|textbox|label|panel|radiobutton|listbox|listview|numericupdown|trackbar|linklabel|tabpage|groupbox)\d*$'
$glyph = '^(action|nav|app|feature|status|source|system)\.[a-z0-9.-]+$'

function Test-AccessibleName($Element) {
    $c = $Element.Current
    if (-not ($interactive -contains $c.ControlType) -and -not $c.IsKeyboardFocusable) { return $null }
    $name = [string]$c.Name
    $typeName = ($c.ControlType.ProgrammaticName -replace '^ControlType\.', '')
    if ([string]::IsNullOrWhiteSpace($name)) { return 'no-name' }
    if ($name.Trim() -match $designer) { return 'designer-name' }
    if ($name.Trim() -match $glyph) { return 'glyph-id' }
    if ($name.Trim() -ieq $typeName -or ($c.ClassName -and $name.Trim() -ieq $c.ClassName)) { return 'type-name' }
    ''
}

function Get-ElementPath($Element, $Root) {
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $parts = New-Object System.Collections.Generic.List[string]
    $e = $Element
    $guard = 0
    while ($e -and -not [System.Windows.Automation.Automation]::Compare($e, $Root) -and $guard -lt 40) {
        $label = if ($e.Current.AutomationId) { $e.Current.AutomationId } else { ($e.Current.ControlType.ProgrammaticName -replace '^ControlType\.', '') }
        $parts.Insert(0, $label)
        $e = $walker.GetParent($e)
        $guard++
    }
    $parts -join '/'
}

function Read-Window($Handle, [string]$Lang, [string]$Surface, $Rows) {
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($Handle)
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $inspected = 0
    foreach ($el in $all) {
        try {
            if ($el.Current.IsOffscreen) { continue }
            $verdict = Test-AccessibleName $el
            if ($null -eq $verdict) { continue }
            $inspected++
            $row = [pscustomobject]@{
                language     = $Lang
                surface      = $Surface
                path         = Get-ElementPath $el $root
                controlType  = ($el.Current.ControlType.ProgrammaticName -replace '^ControlType\.', '')
                automationId = $el.Current.AutomationId
                name         = $el.Current.Name
                finding      = $verdict
            }
            $Rows.Add($row)
            if ($verdict) {
                Add-CheckFinding -Severity fail -Name "$Lang/$Surface" -Reason ("{0} {1} '{2}' at {3}" -f $verdict, $row.controlType, $row.name, $row.path)
            }
        }
        catch [System.Windows.Automation.ElementNotAvailableException] { }
    }
    $inspected
}

function Wait-NewWindow($ProcId, $Before, [int]$TimeoutMs) {
    $known = @($Before | ForEach-Object { $_.Handle })
    $found = $null
    [void](Wait-AuditCondition -TimeoutMs $TimeoutMs -PollMs 50 -Condition {
            $script:candidate = @(Get-AppWindows | Where-Object { $_.ProcessId -eq $ProcId -and $_.Width -ge 120 -and $known -notcontains $_.Handle }) | Select-Object -First 1
            [bool]$script:candidate
        })
    if ($script:candidate) { $found = $script:candidate }
    $script:candidate = $null
    $found
}

function Invoke-Cli($Profile, [string[]]$CliArgs) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $Profile.Exe
    $psi.Arguments = ($CliArgs -join ' ')
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.EnvironmentVariables['LOCALAPPDATA'] = $Profile.LocalAppData
    $p = [System.Diagnostics.Process]::Start($psi)
    if (-not $p.WaitForExit(15000)) { try { $p.Kill() } catch { }; return -1 }
    $p.ExitCode
}

$rows = New-Object System.Collections.Generic.List[object]
$surfaces = New-Object System.Collections.Generic.List[object]
$machine = Get-AuditMachine -ExePath $exeSource

foreach ($lang in $Language) {
    Write-Host "== $lang" -ForegroundColor Cyan
    $values = @{
        UiLanguage = $lang; EnableQuickNotes = 1; QuickNotesNoticeShown = 1; EnableSymbols = 1
        EnableTrafficLight = 1; TrafficLightShowAtStart = 0; EnableClipboardHistory = 1
        ClipboardHistoryNoticeShown = 1; ShowClipboardHistoryOnStartup = 0
        EnableScenarioLauncher = 1; LauncherFirstEnableDone = 1
    }
    $profile = $null
    try {
        $profile = Enter-AuditProfile -Values $values -Configuration $Configuration -ExePath $ExePath
        $inst = Start-AuditInstance -Profile $profile
        $layout = Join-Path $profile.DataFolder 'layout.txt'
        if ((Wait-AuditCondition -TimeoutMs 20000 -PollMs 50 -Condition { Test-Path $layout }) -lt 0) {
            Add-CheckFinding -Severity notverified -Name "$lang/start" -Reason 'the staged copy did not publish its layout within 20 s'
            continue
        }

        $targets = New-Object System.Collections.Generic.List[object]
        foreach ($key in $Page) { $targets.Add([pscustomobject]@{ Surface = "settings:$key"; Args = @('show', '--page', $key); Settings = $true }) }
        if (-not $SettingsOnly) {
            $targets.Add([pscustomobject]@{ Surface = 'quick-notes'; Args = @('note', 'show'); Settings = $false })
            $targets.Add([pscustomobject]@{ Surface = 'clipboard-history'; Args = @('clipboard-history', 'show'); Settings = $false })
            $targets.Add([pscustomobject]@{ Surface = 'symbols'; Args = @('symbols', 'show'); Settings = $false })
            $targets.Add([pscustomobject]@{ Surface = 'traffic-light'; Args = @('trafficlight', 'show'); Settings = $false })
        }

        $settingsHandle = $null
        foreach ($t in $targets) {
            $before = @(Get-AppWindows | Where-Object { $_.ProcessId -eq $inst.Id })
            $code = Invoke-Cli $profile $t.Args
            if ($code -ne 0) {
                Add-CheckFinding -Severity notverified -Name "$lang/$($t.Surface)" -Reason "``$($t.Args -join ' ')`` answered exit $code"
                continue
            }
            $handle = $null
            if ($t.Settings -and $settingsHandle) {
                $handle = $settingsHandle
                Start-Sleep -Milliseconds 400   # the page switch is posted; let the new page lay out
            }
            else {
                $w = Wait-NewWindow $inst.Id $before $WindowTimeoutMs
                if (-not $w) {
                    Add-CheckFinding -Severity notverified -Name "$lang/$($t.Surface)" -Reason "no new window within $WindowTimeoutMs ms"
                    continue
                }
                $handle = $w.Handle
                if ($t.Settings) { $settingsHandle = $handle }
                Start-Sleep -Milliseconds 400
            }
            $n = Read-Window $handle $lang $t.Surface $rows
            $bad = @($rows | Where-Object { $_.language -eq $lang -and $_.surface -eq $t.Surface -and $_.finding }).Count
            $surfaces.Add([pscustomobject]@{ language = $lang; surface = $t.Surface; inspected = $n; findings = $bad })
            Write-Host ("  {0,-28} {1,4} controls, {2} unnamed" -f $t.Surface, $n, $bad)
            if ($n -eq 0) { Add-CheckFinding -Severity notverified -Name "$lang/$($t.Surface)" -Reason 'the walk inspected no interactive control' }
        }
    }
    finally {
        Exit-AuditProfile -Profile $profile
    }
}

$csv = Join-Path $OutDir 'cu3-controls.csv'
$rows | Export-Csv -Path $csv -NoTypeInformation -Encoding UTF8
[ordered]@{
    ticket = 'S0061'; track = 'CU-3'; stamp = $stamp; machine = $machine; languages = @($Language)
    surfaces = @($surfaces); controls = $rows.Count; findings = @($rows | Where-Object { $_.finding }).Count
} | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $OutDir 'cu3-summary.json') -Encoding UTF8
Write-Host "  -> $csv" -ForegroundColor DarkGray
Complete-Check

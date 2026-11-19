<#
.SYNOPSIS
Counts the controls and window handles of the built settings window, by page and by type (ticket S0135, phase 0).
.DESCRIPTION
Builds SettingsForm from the built CyrFlip.exe through the same constructor call as Audit-SettingsWindow.ps1, in an isolated profile (LOCALAPPDATA and APPDATA point at a
temporary folder, no registry value is written), and prints:
  - the number of controls right after construction (the budget of S0135 is at most -MaxControls built controls at open; 0 turns the check off),
  - the controls of every page and the controls by type,
  - the window handles after the window is shown off-screen (the opening page only) and after every page has been visited.
With -DumpPages <dir> it also writes, per page, <language>.<PageKey>.txt with one sorted line per built control of that page,
type|text|accessible name|enabled|tab index - the fixtures that the page-equality tests of S0135 phase 2 compare against.
Exit 0 = counted, and under the budget when one is set; exit 1 = over the budget or the build is missing.
.EXAMPLE
./Measure-SettingsControls.ps1 -Language ru
./Measure-SettingsControls.ps1 -Language en -MaxControls 0 -DumpPages artifacts/s0135/pages
.NOTES
Needs a built src/CyrFlip/bin/<Configuration>/net48/CyrFlip.exe (the owner's build). It runs in Windows PowerShell 5.1 with -STA (WinForms on the .NET Framework the app targets) and
re-launches itself there when started from PowerShell 7. The window is shown far off-screen and never takes the focus. The first S0135 count was 1030 controls on 15 pages
(handles: 96 after opening, 981 after visiting every page). The budget applies once phase 3 (lazy pages) has run; before it the check is expected to report over budget.
#>
[CmdletBinding()]
param(
    [string]$Language = 'en',
    [int]$MaxControls = 150,
    [string]$DumpPages = '',
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release'
)
if ($PSVersionTable.PSEdition -eq 'Core') {
    $argList = @('-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath, '-Language', $Language, '-MaxControls', $MaxControls, '-Configuration', $Configuration)
    if ($DumpPages) { $argList += @('-DumpPages', [IO.Path]::GetFullPath($DumpPages)) }
    & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" @argList
    exit $LASTEXITCODE
}

$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$exe = Join-Path $repo "src\CyrFlip\bin\$Configuration\net48\CyrFlip.exe"
if (-not (Test-Path -LiteralPath $exe)) { Write-Error "Build first: $exe not found (dotnet build CyrFlip.sln -c $Configuration)."; exit 1 }

$iso = Join-Path ([IO.Path]::GetTempPath()) ('CyrFlipControls\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $iso | Out-Null
$env:LOCALAPPDATA = $iso; $env:APPDATA = $iso

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()
try { [System.Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false) } catch { }

$asm = [System.Reflection.Assembly]::LoadFrom($exe)
$flags = [Reflection.BindingFlags]'NonPublic,Public,Instance'
$TForm = $asm.GetType('CyrFlip.SettingsForm', $true)
$TConfig = $asm.GetType('CyrFlip.AppConfig', $true)
$TStore = $asm.GetType('CyrFlip.LauncherScenarioStore', $true)
$TLoc = $asm.GetType('CyrFlip.Localization', $true)
$names = [string[]]$TLoc.GetField('Names').GetValue($null)
$codes = [string[]]$TLoc.GetField('Codes').GetValue($null)
$index = [Array]::IndexOf($codes, $Language)
if ($index -lt 0) { Write-Error "Unknown UI language code '$Language'. Known: $($codes -join ', ')"; exit 1 }

function Count-Tree($root) {
    $types = @{}; $total = 0; $handles = 0
    $stack = New-Object System.Collections.Generic.Stack[object]
    foreach ($c in $root.Controls) { $stack.Push($c) }
    while ($stack.Count -gt 0) {
        $c = $stack.Pop()
        $total++
        if ($c.IsHandleCreated) { $handles++ }
        $n = $c.GetType().Name
        if ($types.ContainsKey($n)) { $types[$n]++ } else { $types[$n] = 1 }
        foreach ($k in $c.Controls) { $stack.Push($k) }
    }
    [pscustomobject]@{ Total = $total; Handles = $handles; Types = $types }
}

function Get-PageLines($root) {
    $lines = New-Object System.Collections.Generic.List[string]
    $stack = New-Object System.Collections.Generic.Stack[object]
    foreach ($c in $root.Controls) { $stack.Push($c) }
    while ($stack.Count -gt 0) {
        $c = $stack.Pop()
        $text = ([string]$c.Text) -replace '[\r\n|]+', ' '
        $accessible = ([string]$c.AccessibleName) -replace '[\r\n|]+', ' '
        $lines.Add(('{0}|{1}|{2}|{3}|{4}' -f $c.GetType().Name, $text, $accessible, $c.Enabled, $c.TabIndex))
        foreach ($k in $c.Controls) { $stack.Push($k) }
    }
    $arr = $lines.ToArray(); [Array]::Sort($arr, [StringComparer]::Ordinal); $arr
}

$noB = [Action[bool]] { }; $noI = [Action[int]] { }; $noS = [Action[string]] { }; $no = [Action] { }
$export = [Func[string, bool, string]] { param($a, $b) '' }
$exchange = [Action[System.Windows.Forms.IWin32Window, bool]] { param($owner, $import) }
$cfg = [Activator]::CreateInstance($TConfig); $cfg.UiLanguage = $names[$index]; $cfg.SettingsTab = 0
$store = [Activator]::CreateInstance($TStore, [object[]]@([string](Join-Path $iso 'Scenarios'), $false))

$sw = [Diagnostics.Stopwatch]::StartNew()
$form = [Activator]::CreateInstance($TForm, [object[]]@(
        $cfg, $noB, $noB, $noB, $noB, $noB, $noB, $noB, $noB, $noB, $noI, $noS, $no, $no, $no, $no, $no,
        $noB, $noB, $noB, $noB, $noB, $noB, $store, $noB, $no, $no, $no, $export, $exchange, $null, $null))
$sw.Stop()
$exit = 0
try {
    $tabs = $TForm.GetField('_tabs', $flags).GetValue($form)
    $all = Count-Tree $form
    "language: $Language    constructor: $($sw.ElapsedMilliseconds) ms (a cold process; indicative only)"
    "built controls after construction: $($all.Total)    pages: $($tabs.TabPages.Count)"
    ""
    "per page (controls):"
    foreach ($p in $tabs.TabPages) { "{0,-34} {1,5}" -f $p.Text, (Count-Tree $p).Total }
    ""
    "by type (largest 14):"
    $all.Types.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 14 | ForEach-Object { "{0,-26} {1,5}" -f $_.Key, $_.Value }

    if ($DumpPages) {
        New-Item -ItemType Directory -Force -Path $DumpPages | Out-Null
        foreach ($p in $tabs.TabPages) {
            $key = if ($p.Tag) { [string]$p.Tag } else { $p.Text }
            $file = Join-Path $DumpPages ('{0}.{1}.txt' -f $Language, $key)
            [IO.File]::WriteAllLines($file, [string[]](Get-PageLines $p), (New-Object System.Text.UTF8Encoding($false)))
        }
        ""
        "page dumps written to $DumpPages"
    }

    $form.ShowInTaskbar = $false
    $form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $form.Location = New-Object System.Drawing.Point -32000, -32000
    $form.Show()
    for ($k = 0; $k -lt 5; $k++) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 60 }
    $afterShow = Count-Tree $form
    ""
    "handles after Show (opening page only): $($afterShow.Handles) of $($afterShow.Total)"
    for ($i = 0; $i -lt $tabs.TabPages.Count; $i++) {
        $tabs.SelectedIndex = $i
        for ($k = 0; $k -lt 3; $k++) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 30 }
    }
    $afterAll = Count-Tree $form
    "handles after visiting every page:     $($afterAll.Handles) of $($afterAll.Total)"

    if ($MaxControls -gt 0) {
        ""
        if ($all.Total -gt $MaxControls) {
            "OVER BUDGET: $($all.Total) built controls at open, the budget is $MaxControls (S0135 section 2; it applies from phase 3 on)."
            $exit = 1
        } else {
            "within budget: $($all.Total) built controls at open, the budget is $MaxControls."
        }
    }
}
finally {
    $form.Close(); $form.Dispose()
    Remove-Item -Recurse -Force $iso -ErrorAction SilentlyContinue
}
exit $exit

# What a screen reader - or any UI Automation client, including our own run-and-observe checks -
# can see of the LIVE settings window: every element with its control type and name, then the
# interactive ones that have no name, and whether the page list exposes its pages at all.
# Read-only: nothing is clicked, focused or moved.
#
#   .\tools\uitest\Get-SettingsUiaReport.ps1            # the settings window must be open
#   .\tools\uitest\Get-SettingsUiaReport.ps1 -Open      # opens it (launcher IPC) and closes it after
#   .\tools\uitest\Get-SettingsUiaReport.ps1 -All       # also print every named element
#
# -Open sends /launcher-settings to the running CyrFlip over its own pipe (needs the scenario
# launcher enabled); the window comes to the front, and is closed (hidden) again at the end.
[CmdletBinding()]
param([switch]$All, [switch]$Open)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.UiTest.psm1') -Force
Enable-UiTestDpi
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -Namespace UiaReport -Name Native -MemberDefinition '[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);'

# The largest titled window of the process, not the first: a drop-down menu CyrFlip keeps around is
# also a visible top-level window, and it can come first.
function Get-SettingsWindow {
    Get-AppWindows | Where-Object { $_.Width -ge 300 -and $_.Title } | Sort-Object { $_.Width * $_.Height } -Descending | Select-Object -First 1
}
$win = Get-SettingsWindow
$openedHere = $false
if (-not $win -and $Open) {
    Start-Process -FilePath (Get-Process CyrFlip -ErrorAction Stop | Select-Object -First 1).Path -ArgumentList '/launcher-settings'
    for ($k = 0; $k -lt 26 -and -not $win; $k++) { Start-Sleep -Milliseconds 300; $win = Get-SettingsWindow }
    $openedHere = [bool]$win
    Start-Sleep -Milliseconds 800      # let the pages build before the tree is read
}
if (-not $win) { throw 'The settings window is not open (open it, or pass -Open with the launcher enabled).' }
$root = [System.Windows.Automation.AutomationElement]::FromHandle($win.Handle)
$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
$interactive = 'Button', 'CheckBox', 'RadioButton', 'ComboBox', 'Edit', 'Slider', 'Spinner', 'TabItem', 'ListItem', 'Hyperlink', 'List', 'DataGrid', 'Tab'
$rows = New-Object System.Collections.Generic.List[object]

function Walk($el, [int]$depth) {
    if ($depth -gt 40) { return }
    $c = $walker.GetFirstChild($el)
    while ($c) {
        $type = $c.Current.ControlType.ProgrammaticName -replace '^ControlType\.', ''
        $rows.Add([pscustomobject]@{ Depth = $depth; Type = $type; Name = $c.Current.Name; Offscreen = $c.Current.IsOffscreen })
        Walk $c ($depth + 1)
        $c = $walker.GetNextSibling($c)
    }
}
Walk $root 0

"window '$($win.Title)': $($rows.Count) elements in the control view"
$rows | Group-Object Type | Sort-Object Count -Descending | ForEach-Object { '  {0,-12} {1}' -f $_.Name, $_.Count }
$tabItems = @($rows | Where-Object Type -eq 'TabItem')
"page list: $($tabItems.Count) TabItem element(s)" + $(if ($tabItems.Count -eq 0) { ' - a screen reader cannot list or name the pages' } else { '' })
$unnamed = @($rows | Where-Object { $interactive -contains $_.Type -and [string]::IsNullOrWhiteSpace($_.Name) })
"interactive elements without a name: $($unnamed.Count)"
$unnamed | ForEach-Object { '  {0}{1}' -f ('  ' * [Math]::Min($_.Depth, 6)), $_.Type }
$glyph = @($rows | Where-Object { $_.Type -eq 'Button' -and $_.Name -and $_.Name -notmatch '[\p{L}\p{N}]' })
"buttons named only by a glyph: $($glyph.Count)" + $(if ($glyph.Count) { ' (' + (($glyph | ForEach-Object Name | Select-Object -Unique) -join ' ') + ')' } else { '' })
if ($All) { $rows | Where-Object Name | ForEach-Object { '  {0}{1}: {2}' -f ('  ' * [Math]::Min($_.Depth, 6)), $_.Type, $_.Name } }
if ($openedHere) { [void][UiaReport.Native]::PostMessage($win.Handle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) }   # WM_CLOSE = hide, as the close box does

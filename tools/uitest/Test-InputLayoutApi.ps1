<#
.SYNOPSIS
Ticket S0007 WL-1 phase B - the "Windows languages" edits through the documented input-profile API,
on the real Windows. The part xUnit cannot reach (InputLayoutApiTests fake input.dll).

.DESCRIPTION
Default run (read-only): the built exe's own Win32InputLayoutApi enumerates the enabled input list;
every entry must parse, one must carry LOT_DEFAULT, and every KLID in the legacy Preload must be a
keyboard the API lists or the stand-in of a TIP of its language.

-Apply (writes Windows state, then puts it back): InputLayoutApi.Add of -Klid (default Ukrainian,
00000422), which must not be enabled already - the phase-B path alone, so a fallback shows as a FAIL;
checks the API and Preload both list it and nothing else changed; then InputLayouts.Remove (the
settings tab's own call) and checks the list is exactly what it was, InputMethodOverride
included. A failure stops before the next write and prints what to restore by hand.

What only a sign-out/in can show is printed as a checklist at the end.

.EXAMPLE
pwsh -NoProfile -File tools\uitest\Test-InputLayoutApi.ps1
pwsh -NoProfile -File tools\uitest\Test-InputLayoutApi.ps1 -Apply
#>
[CmdletBinding()]
param(
    [switch]$Apply,
    [ValidatePattern('^[0-9A-Fa-f]{8}$')][string]$Klid = '00000422',
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CyrFlip.UiTest.psm1') -Force

$failures = @()
function Check([string]$name, [bool]$ok, [string]$detail) {
    $mark = if ($ok) { 'PASS' } else { 'FAIL' }
    $colour = if ($ok) { 'Green' } else { 'Red' }
    Write-Host ("  [{0}] {1}{2}" -f $mark, $name, $(if ($detail) { " - $detail" } else { '' })) -ForegroundColor $colour
    if (-not $ok) { $script:failures += $name }
}

$exe = Get-CyrFlipExe -Configuration $Configuration
$asm = [Reflection.Assembly]::LoadFrom($exe)
$bf = [Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
$ApiType = $asm.GetType('CyrFlip.Win32InputLayoutApi', $true)
$Layouts = $asm.GetType('CyrFlip.InputLayouts', $true)
$api = [Activator]::CreateInstance($ApiType, $true)

function Enumerate {
    $a = [object[]]@($null)
    $ok = $ApiType.GetMethod('TryEnumerate').Invoke($api, $a)
    if (-not $ok) { return $null }
    , @($a[0] | ForEach-Object { [pscustomobject]@{ Id = $_.Id; Tip = $_.IsTip; Klid = $_.Klid; Lang = $_.LangId; Default = $_.IsDefault } })
}
function Preload { , @($Layouts.GetMethod('EffectiveKlids', $bf).Invoke($null, @())) }
function ProfileValue([string]$name) {
    try { Get-ItemPropertyValue 'HKCU:\Control Panel\International\User Profile' -Name $name -ErrorAction Stop } catch { $null }
}
function Override { ProfileValue 'InputMethodOverride' }
function Languages { (ProfileValue 'Languages') -join ',' }
function Ids($entries) { ($entries | ForEach-Object { $_.Id }) -join ';' }

Write-Host "Read-only: the enabled input list through input.dll" -ForegroundColor Cyan
$before = Enumerate
Check 'EnumEnabledLayoutOrTip answers' ($null -ne $before) ''
if ($null -eq $before) { exit 1 }
$before | ForEach-Object { Write-Host ("    {0}{1}" -f $_.Id, $(if ($_.Default) { '  (default)' } else { '' })) }
Check 'exactly one entry is the default' (@($before | Where-Object Default).Count -eq 1) ''
$preload = Preload
foreach ($k in $preload) {
    $lang = [Convert]::ToUInt16($k.Substring(4), 16)
    $known = @($before | Where-Object { (-not $_.Tip -and $_.Klid -eq $k) -or ($_.Tip -and $_.Lang -eq $lang -and $k.StartsWith('0000')) }).Count -gt 0
    Check "Preload $k is an entry the API lists" $known ''
}

if ($Apply) {
    $Klid = $Klid.ToLowerInvariant()
    Write-Host "`n-Apply: add $Klid through InputLayoutApi, remove it through InputLayouts (the settings tab's own call)" -ForegroundColor Cyan
    if (@($before | Where-Object { -not $_.Tip -and $_.Klid -eq $Klid }).Count -gt 0) {
        Write-Host "  $Klid is already enabled - pick a -Klid that is not, so the run can put everything back." -ForegroundColor Yellow
        exit 1
    }
    $overrideBefore = Override
    $languagesBefore = Languages
    $idsBefore = Ids $before

    # InputLayoutApi.Add, not InputLayouts.Add: a null here is the fallback to the registry path, which
    # InputLayouts.Add would hide behind the same Ok. Preload is then checked without CyrFlip's reconcile.
    $r = $asm.GetType('CyrFlip.InputLayoutApi', $true).GetMethod('Add', $bf).Invoke($null, @($api, $Klid))
    $mid = Enumerate
    Check 'InputLayoutApi.Add returns Ok (no fallback)' ("$r" -eq 'Ok') "$r"
    Check 'the API lists it under its own language' (@($mid | Where-Object { $_.Id -eq ('{0}:{1}' -f $Klid.Substring(4).ToUpperInvariant(), $Klid.ToUpperInvariant()) }).Count -eq 1) (Ids $mid)
    Check 'Preload lists it' ((Preload) -contains $Klid) ((Preload) -join ',')
    Check 'every other entry is untouched' ((Ids ($mid | Where-Object { $_.Klid -ne $Klid })) -eq $idsBefore) ''
    if ($failures) { Write-Host "  Stopped. Remove $Klid in Windows 'Language & region' if it is listed." -ForegroundColor Red; exit 1 }

    $r = $Layouts.GetMethod('Remove', $bf).Invoke($null, @($Klid))
    $after = Enumerate
    Check 'Remove returns Ok' ("$r" -eq 'Ok') "$r"
    Check 'the list is exactly what it was' ((Ids $after) -eq $idsBefore) (Ids $after)
    Check 'Preload is what it was' (((Preload) -join ',') -eq ($preload -join ',')) ((Preload) -join ',')
    Check 'InputMethodOverride is what it was' ("$(Override)" -eq "$overrideBefore") "$(Override)"
    Check 'Languages is what it was' ((Languages) -eq $languagesBefore) (Languages)
}

Write-Host "`nNeeds a sign-out/in (a human):" -ForegroundColor Cyan
Write-Host "  WL-1  add a Japanese IME in Windows, press 'Default' or up/down on any layout in CyrFlip, sign out/in: the IME is still listed"
Write-Host "  WL-2  Russian with a US keyboard: add any layout in CyrFlip, sign out/in: US is still under Russian, the language order is unchanged"
Write-Host "  WL-8  move a layout within its language, sign out/in: the order held"
Write-Host "  WL-2  'Default' on a layout: Windows' own taskbar switcher and Settings name it the default at once and after sign-in"
Write-Host "  WL-9  the Pinyin row (00000804) refuses removal with the 'managed by Windows' message"
Write-Host "  WL-10 rename one Preload value to a non-KLID: any edit says 'could not be read' and changes nothing"

if ($failures) { Write-Host "`nFAILED: $($failures -join '; ')" -ForegroundColor Red; exit 1 }
Write-Host "`nAll checks passed." -ForegroundColor Green

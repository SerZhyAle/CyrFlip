<#
    Fills the gaps in the Partner Center listing export and writes an import-ready CSV.

      msix/store-listing-export.csv   the file Partner Center produced (Store listings -> Export).
                                      Its columns, row order and asset URLs are the contract - this
                                      script never reorders or renames anything.
      msix/listing/<lang>.txt         our copy per language, as "@@Field / value" blocks.
      msix/store-listing-import-13-languages.csv   the result, ready for Store listings -> Import.

    Only EMPTY cells are filled. Anything Partner Center already holds - including the listing-asset
    URLs, which are tied to the current submission id - is copied through untouched, so re-running
    this after a fresh export can never overwrite the live listing with stale text.

    Two rules worth knowing:
      * A language must already exist on the submission. Partner Center exports one column per
        listing language, and a column it does not know is ignored on import - which is exactly how
        the Urdu copy went missing on the first attempt: `ur` had not been added yet, so that column
        was dropped without a word while the other twelve went in.
      * "What's new" (ReleaseNotes) is deliberately en-us/ru/uk only. The other language files carry
        no @@ReleaseNotes block, so those cells stay empty by design, not by omission.

    Fidelity is testable: run with -FillNothing and the output must be byte-identical to the export.
#>
[CmdletBinding()]
param(
    # Emit a pure round trip of the export. Any difference from the source file is a bug in the
    # writer below, not in the copy - which is the point of having the switch. Implies -Faithful,
    # since byte-identity is only meaningful against the export's own quoting.
    [switch] $FillNothing,

    # Quote a field only where CSV requires it, exactly as the export does. Produces a readable
    # diff - and a file Partner Center has so far refused with "We couldn't process this .csv file".
    # The default instead quotes EVERY field, which is the shape of the import that actually went
    # through (PowerShell's Export-Csv wrote it that way). Not a theory worth defending, just the
    # one difference between the attempt that worked and the one that did not.
    [switch] $Faithful,

    # Write the byte-order mark the export itself carries. Off by default: Partner Center exports
    # UTF-8 *with* BOM but its importer is widely reported to choke on it, and re-saving the same
    # file without the BOM is the fix people land on. -FillNothing turns it back on, since a byte
    # comparison against the export is meaningless without it.
    [switch] $KeepBom,

    # Leave these Field rows empty even when we have copy for them, and write the result somewhere
    # else. For bisecting an import Partner Center swallows without applying: it accepts the file
    # and simply drops what it dislikes, so the only way to find the offending field is to feed it
    # smaller sets and diff the next export.
    [string[]] $SkipFields,
    [string]   $OutFile,

    # Also stage msix/store-listing-import/ - the CSV next to the screenshots, with each language's
    # DesktopScreenshot pointing at its own file by relative path. Partner Center takes that through
    # *Import listings -> Import folder*, which is the only way to upload new images with the copy.
    # It matters: a listing counts as complete only once it has a description AND one screenshot, so
    # the ten languages that arrived with text alone are still shown as Incomplete.
    [switch] $ImportFolder
)
$ErrorActionPreference = 'Stop'

$MsixDir = $PSScriptRoot
$Export  = Join-Path $MsixDir 'store-listing-export.csv'
$LangDir = Join-Path $MsixDir 'listing'
$Dest    = if ($OutFile) { if ([System.IO.Path]::IsPathRooted($OutFile)) { $OutFile } else { Join-Path $MsixDir $OutFile } }
           else { Join-Path $MsixDir 'store-listing-import-13-languages.csv' }

# A Partner Center language code may differ from the file that holds its copy.
$FileForCode = @{ 'pt-br' = 'pt'; 'pt-pt' = 'pt'; 'zh-hans' = 'zh'; 'zh-hant' = 'zh'; 'bn-bd' = 'bn'; 'bn-in' = 'bn' }

# Rows that are not prose: the same value serves every language.
$SharedRows = @('Title', 'CopyrightTrademarkInformation')

function Read-LangFile([string] $path) {
    $map = @{}
    $key = $null
    $buf = New-Object System.Collections.Generic.List[string]
    foreach ($line in [System.IO.File]::ReadAllText($path).Replace("`r`n", "`n").Split("`n")) {
        if ($line.StartsWith('@@')) {
            if ($key) { $map[$key] = ($buf -join "`n").Trim() }
            $key = $line.Substring(2).Trim()
            $buf.Clear()
        }
        else { $buf.Add($line) }
    }
    if ($key) { $map[$key] = ($buf -join "`n").Trim() }
    $map
}

$quoteEverything = -not ($Faithful -or $FillNothing)

function Quote([string] $value) {
    if ($null -eq $value) { $value = '' }
    if ($quoteEverything) { return '"' + $value.Replace('"', '""') + '"' }
    if ($value.IndexOfAny([char[]] @(',', '"', "`r", "`n")) -ge 0) { return '"' + $value.Replace('"', '""') + '"' }
    $value
}

$rows    = Import-Csv $Export
$columns = $rows[0].PSObject.Properties.Name
$langs   = $columns | Select-Object -Skip 4          # Field, ID, Type (Type), default, then languages

$copy = @{}
if (-not $FillNothing) {
    foreach ($code in $langs) {
        $name = if ($FileForCode.ContainsKey($code)) { $FileForCode[$code] } else { $code }
        $path = Join-Path $LangDir "$name.txt"
        if (Test-Path $path) { $copy[$code] = Read-LangFile $path }
    }
}

$filled = @{}
foreach ($row in $rows) {
    foreach ($code in $langs) {
        if ($row.$code -ne '') { continue }                       # never overwrite Partner Center
        if (-not $copy.ContainsKey($code)) { continue }
        if ($SkipFields -contains $row.Field) { continue }
        $value = if ($SharedRows -contains $row.Field) { $row.'en-us' }
                 elseif ($copy[$code].ContainsKey($row.Field)) { $copy[$code][$row.Field] }
                 else { '' }
        if ($value -eq '') { continue }
        $row.$code = $value
        if (-not $filled.ContainsKey($code)) { $filled[$code] = 0 }
        $filled[$code]++
    }
}

# --- screenshots -------------------------------------------------------------------------------
# One shot per language, produced by tools/uitest/Save-StoreScreenshots.ps1. A language that already
# has a screenshot in Partner Center keeps it and gets ours in the next free slot; the rest get slot
# 1, which is what turns them from Incomplete into Complete.
$shotDir   = Join-Path $MsixDir 'screenshots'
$stageDir  = Join-Path $MsixDir 'store-listing-import'
$stageName = Split-Path $stageDir -Leaf
$staged    = @()
if ($ImportFolder) {
    if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
    New-Item -ItemType Directory -Force $stageDir | Out-Null
    foreach ($code in $langs) {
        $png = Join-Path $shotDir "settings-$code.png"
        if (-not (Test-Path $png)) { Write-Warning "No screenshot for '$code' - that listing stays Incomplete."; continue }
        Copy-Item $png $stageDir
        $slot = 1..10 | Where-Object { ($rows | Where-Object Field -eq "DesktopScreenshot$_").$code -eq '' } | Select-Object -First 1
        if (-not $slot) { continue }
        ($rows | Where-Object Field -eq "DesktopScreenshot$slot").$code = "$stageName/settings-$code.png"
        $staged += [pscustomobject]@{ Language = $code; Slot = "DesktopScreenshot$slot"; File = "settings-$code.png" }
    }
}

$out = New-Object System.Collections.Generic.List[string]
$out.Add(($columns | ForEach-Object { Quote $_ }) -join ',')
foreach ($row in $rows) {
    $out.Add((($columns | ForEach-Object { Quote $row.$_ }) -join ','))
}
# The export ends without a trailing newline; keep it that way.
$bom = [bool] ($KeepBom -or $FillNothing)
$text = $out -join "`r`n"
[System.IO.File]::WriteAllText($Dest, $text, (New-Object System.Text.UTF8Encoding($bom)))
if ($ImportFolder) {
    # Exactly one .csv in the folder - Partner Center refuses a folder holding more than one.
    [System.IO.File]::WriteAllText((Join-Path $stageDir 'listings.csv'), $text, (New-Object System.Text.UTF8Encoding($bom)))
}

if ($FillNothing) {
    $same = [System.Linq.Enumerable]::SequenceEqual([byte[]] [System.IO.File]::ReadAllBytes($Export), [byte[]] [System.IO.File]::ReadAllBytes($Dest))
    Write-Host ("Round trip byte-identical to the export: {0}" -f $same) -ForegroundColor $(if ($same) { 'Green' } else { 'Red' })
    return
}

$required = @('Description', 'ShortDescription', 'Title') + (1..17 | ForEach-Object { "Feature$_" }) + (1..7 | ForEach-Object { "SearchTerm$_" })
foreach ($code in $langs) {
    $missing = @($required | Where-Object { ($rows | Where-Object Field -eq $_).$code -eq '' })
    $notes   = ($rows | Where-Object Field -eq 'ReleaseNotes').$code
    $state   = if ($missing.Count -eq 0) { 'complete' } else { "MISSING: $($missing -join ', ')" }
    $added   = if ($filled.ContainsKey($code)) { "+$($filled[$code]) filled" } else { 'unchanged' }
    Write-Host ("{0,-9} {1,-10} {2,-12} what's new: {3}" -f $code, $state, $added, $(if ($notes) { "$($notes.Length) chars" } else { '-' }))
}
if ($ImportFolder) {
    Write-Host ''
    $staged | Format-Table -AutoSize | Out-String | Write-Host
    Write-Host "Import folder ready: $stageDir" -ForegroundColor Green
    Write-Host "Partner Center -> Store listings -> Import listings -> Import folder -> select '$stageName'" -ForegroundColor DarkGray
}
Write-Host "Wrote $Dest" -ForegroundColor Green

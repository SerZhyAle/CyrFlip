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
    # writer below, not in the copy - which is the point of having the switch.
    [switch] $FillNothing
)
$ErrorActionPreference = 'Stop'

$MsixDir = $PSScriptRoot
$Export  = Join-Path $MsixDir 'store-listing-export.csv'
$LangDir = Join-Path $MsixDir 'listing'
$Dest    = Join-Path $MsixDir 'store-listing-import-13-languages.csv'

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

# Partner Center quotes a field only when it has to; match that or the diff is unreadable.
function Quote([string] $value) {
    if ($null -eq $value) { return '' }
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
        $value = if ($SharedRows -contains $row.Field) { $row.'en-us' }
                 elseif ($copy[$code].ContainsKey($row.Field)) { $copy[$code][$row.Field] }
                 else { '' }
        if ($value -eq '') { continue }
        $row.$code = $value
        if (-not $filled.ContainsKey($code)) { $filled[$code] = 0 }
        $filled[$code]++
    }
}

$out = New-Object System.Collections.Generic.List[string]
$out.Add(($columns | ForEach-Object { Quote $_ }) -join ',')
foreach ($row in $rows) {
    $out.Add((($columns | ForEach-Object { Quote $row.$_ }) -join ','))
}
# The export ends without a trailing newline; keep it that way.
[System.IO.File]::WriteAllText($Dest, ($out -join "`r`n"), (New-Object System.Text.UTF8Encoding($true)))

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
Write-Host "Wrote $Dest" -ForegroundColor Green

<#
    Builds msix/store-listing-import-13-languages.csv - one Partner Center import file carrying the
    Store listing in all 13 languages the app's interface speaks.

    Where the copy comes from:
      en-us, ru   msix/store-listing-export.csv (the Partner Center export; copied through verbatim,
                  so a regenerated import file can never drift from what the Store already has)
      the rest    msix/listing/<lang>.txt - a plain "@@Field / value" file per language

    "What's new" (ReleaseNotes) is deliberately EN/RU/UK only: the release notes are rewritten every
    release and translating them 13 times each time is not worth it. A language file simply has no
    @@ReleaseNotes section and the column comes out empty, which Partner Center accepts.

    Image rows (screenshots, logos) are left EMPTY for the new languages on purpose: their values are
    Partner Center asset URLs tied to a listing id that a language which has never been submitted does
    not have yet. Upload those per language in the UI, or let the default listing's images stand.

    The -Codes parameter exists because the column header must be the language code Partner Center
    itself uses. If an export ever comes back with different headers, pass the corrected list rather
    than editing the copy: nothing here depends on the spelling of a code.
#>
[CmdletBinding()]
param(
    # Column order = the order the languages are offered in the app's own picker.
    [string[]] $Codes = @('en-us', 'ru', 'uk', 'de', 'it', 'es', 'fr', 'pt-br', 'zh-hans', 'hi', 'bn', 'ar', 'ur')
)
$ErrorActionPreference = 'Stop'

$MsixDir  = $PSScriptRoot
$Export   = Join-Path $MsixDir 'store-listing-export.csv'
$LangDir  = Join-Path $MsixDir 'listing'
$Dest     = Join-Path $MsixDir 'store-listing-import-13-languages.csv'

# A code may differ from the file that holds its copy (pt-br is written in pt.txt, zh-hans in zh.txt).
$FileForCode = @{ 'pt-br' = 'pt'; 'zh-hant' = 'zh'; 'zh-hans' = 'zh'; 'bn-bd' = 'bn'; 'bn-in' = 'bn' }

# Rows that are not prose: the same value serves every language.
$SharedRows = @('Title', 'CopyrightTrademarkInformation', 'OverrideLogosForWin10')

function Read-LangFile([string] $path) {
    $map = @{}
    $key = $null
    $buf = New-Object System.Collections.Generic.List[string]
    foreach ($line in [System.IO.File]::ReadAllLines($path)) {
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

$fromExport = @('en-us', 'ru')
$copy = @{}
foreach ($code in $Codes) {
    if ($fromExport -contains $code) { continue }
    $name = if ($FileForCode.ContainsKey($code)) { $FileForCode[$code] } else { $code }
    $path = Join-Path $LangDir "$name.txt"
    if (-not (Test-Path $path)) { throw "No listing copy for '$code' (expected $path)." }
    $copy[$code] = Read-LangFile $path
}

$rows = Import-Csv $Export
$out = New-Object System.Collections.Generic.List[object]
foreach ($row in $rows) {
    $o = [ordered]@{
        'Field'       = $row.Field
        'ID'          = $row.ID
        'Type (Type)' = $row.'Type (Type)'
        'default'     = $row.default
    }
    foreach ($code in $Codes) {
        if ($fromExport -contains $code)   { $o[$code] = $row.$code;   continue }
        if ($SharedRows -contains $row.Field) { $o[$code] = $row.'en-us'; continue }
        $map = $copy[$code]
        $o[$code] = if ($map.ContainsKey($row.Field)) { $map[$row.Field] } else { '' }
    }
    $out.Add([pscustomobject]$o)
}

$out | Export-Csv $Dest -NoTypeInformation -Encoding utf8BOM

# Report, so a missing translation is visible instead of silently shipping an empty column.
$required = @('Description', 'ShortDescription', 'Title') + (1..17 | ForEach-Object { "Feature$_" }) + (1..7 | ForEach-Object { "SearchTerm$_" })
$check = Import-Csv $Dest
foreach ($code in $Codes) {
    $missing = @($required | Where-Object { ($check | Where-Object Field -eq $_).$code -eq '' })
    $notes = ($check | Where-Object Field -eq 'ReleaseNotes').$code
    $state = if ($missing.Count -eq 0) { 'complete' } else { "MISSING: $($missing -join ', ')" }
    Write-Host ("{0,-9} {1,-10} what's new: {2}" -f $code, $state, $(if ($notes) { "$($notes.Length) chars" } else { '-' }))
}
Write-Host "Wrote $Dest" -ForegroundColor Green

<#
    Fills the gaps in the Partner Center listing export and writes an import-ready CSV.

      msix/store-listing-export.csv   the file Partner Center produced (Store listings -> Export).
                                      Its columns, row order and asset URLs are the contract - this
                                      script never reorders or renames anything.
      msix/listing/<lang>.txt         our copy per language, as "@@Field / value" blocks.
      msix/store-listing-export.sample.csv   the committed emptied fixture (WINDOWS-STORE rule 13):
                                      the export with every copy cell emptied and every language
                                      column present, marked -text so git never touches its bytes.
      msix/store-listing-import-13-languages.csv   the result, ready for Store listings -> Import.

    Only EMPTY cells are filled. Anything Partner Center already holds - including the listing-asset
    URLs, which are tied to the current submission id - is copied through untouched, so re-running
    this after a fresh export can never overwrite the live listing with stale text.

    Three rules worth knowing:
      * A language must already exist on the submission. Partner Center exports one column per
        listing language, and a column it does not know is ignored on import - which is exactly how
        the Urdu copy went missing on the first attempt: `ur` had not been added yet, so that column
        was dropped without a word while the other twelve went in. The script now refuses to run
        when a shipped language has no column, naming it.
      * "What's new" (ReleaseNotes) is authored in the languages CyrFlip proofreads (en-us, ru, uk);
        every machine-translated column carries the English text (WINDOWS-STORE rule 16), folded to
        the 1500-character cap before the file is written.
      * Every cell is checked against its Partner Center cap before anything is written - Feature
        200, ShortDescription 1000, Description 10000, ReleaseNotes 1500 after CRLF, at most 7 search
        terms with no duplicate in a language's set (WINDOWS-STORE rule 9). One cell over its cap
        loses the whole language, so the builder names language, field, length and source and exits
        non-zero without writing.

    The output is UTF-8 without a BOM, every line break is CRLF - inside quoted cells as much as
    between records - with no trailing newline, and the writer refuses to produce anything else
    (WINDOWS-STORE rule 8).

    Fidelity is testable:
      * -FillNothing renders the committed sample back and fails unless the result is byte-identical
        to it (rule 13).
      * -Check renders export + copy and compares BYTES with the committed import CSV - line
        endings included, never normalised away.
      * -WriteSample regenerates the sample fixture from the current export, copy cells emptied.
#>
[CmdletBinding()]
param(
    # Do not write: render in memory, compare BYTES with the committed import CSV, and fail if they
    # differ - line endings included.
    [switch] $Check,

    # Prove the writer on the committed sample export (WINDOWS-STORE rule 13): render it with
    # nothing filled and fail unless the result is byte-identical to the file on disk.
    [switch] $FillNothing,

    # Regenerate msix/store-listing-export.sample.csv from the current export: every copy cell
    # emptied, every language column present, written in the builder's canonical shape.
    [switch] $WriteSample,

    # Quote a field only where CSV requires it, exactly as the export does. Produces a readable
    # diff - and a file Partner Center has so far refused with "We couldn't process this .csv file".
    # The default instead quotes EVERY field, which is the shape of the import that actually went
    # through (PowerShell's Export-Csv wrote it that way). Not a theory worth defending, just the
    # one difference between the attempt that worked and the one that did not.
    [switch] $Faithful,

    # Legacy diagnostic selection. Accepted WINDOWS-STORE 0.2 bisection uses complete
    # files from a fresh draft export, never blanked cells. This option cannot produce an import.
    [string[]] $SkipFields,
    [string]   $OutFile,

    # Also stage msix/store-listing-import/ - the CSV next to the screenshots, with each language's
    # DesktopScreenshot pointing at its own file by relative path. Partner Center takes that through
    # *Import listings -> Import folder*, which is the only way to upload new images with the copy.
    # It matters: a listing counts as complete only once it has a description AND one screenshot, so
    # the ten languages that arrived with text alone are still shown as Incomplete.
    [switch] $ImportFolder,

    # Stage a screenshot only for a language that has none at all. Without it every re-import adds
    # our settings shot again, in the next free slot, beside the copy the previous import already
    # uploaded - a live listing then shows the same picture twice. Use it whenever the export was
    # taken after a successful import; the plain -ImportFolder is for the first one, or for a
    # language deliberately getting a second shot.
    [switch] $ScreenshotOnlyMissing,

    # Fill nothing, correct only OverrideLogosForWin10, and write to store-listing-fix-logo-flag.csv.
    # The smallest possible import: it cannot disturb copy that was entered by hand since the last
    # export, which a full fill would happily overwrite.
    [switch] $LogoFlagOnly
)
$ErrorActionPreference = 'Stop'
if ($SkipFields.Count -gt 0) {
    Write-Host 'SkipFields cannot produce an import: bisect complete files based on a fresh draft export. Nothing was written.' -ForegroundColor Red
    exit 1
}

$MsixDir = $PSScriptRoot
$Export  = Join-Path $MsixDir 'store-listing-export.csv'
$Sample  = Join-Path $MsixDir 'store-listing-export.sample.csv'
$LangDir = Join-Path $MsixDir 'listing'
if ($LogoFlagOnly -and -not $OutFile) { $OutFile = 'store-listing-fix-logo-flag.csv' }
$Dest    = if ($OutFile) { if ([System.IO.Path]::IsPathRooted($OutFile)) { $OutFile } else { Join-Path $MsixDir $OutFile } }
           else { Join-Path $MsixDir 'store-listing-import-13-languages.csv' }

# A Partner Center language code may differ from the file that holds its copy.
$FileForCode = @{ 'pt-br' = 'pt'; 'pt-pt' = 'pt'; 'zh-hans' = 'zh'; 'zh-hant' = 'zh'; 'bn-bd' = 'bn'; 'bn-in' = 'bn' }

# Rows that are not prose: the same value serves every language.
$SharedRows = @('Title', 'CopyrightTrademarkInformation')

# The thirteen UI languages CyrFlip ships (pt-br is the Store's Portuguese column; an extra `pt`
# column, when the submission holds one, is left exactly as exported).
$ExpectedLanguages = @('en-us','ru','ar','bn','zh-hans','fr','de','hi','it','pt-br','es','uk','ur')

# WINDOWS-STORE rule 9: the caps Partner Center enforces per field. The numbers are those observed;
# Partner Center's own message is the authority. ReleaseNotes is counted after CRLF normalisation -
# the unit of the line breaks the file will actually carry.
$CapByField       = @{ ShortDescription = 1000; Description = 10000; ReleaseNotes = 1500 }
$FeatureCap       = 200
$MaxSearchTerms   = 7
# Observed 2026-10-11 on Partner Center (CyrFlip submission 14): the words of all search terms of a language
# count together - 21 words passed (pt-br), 23 failed the whole file with "We couldn't process this .csv
# file" and an empty error list. A "word" is a run of non-space characters.
$MaxSearchTermWords = 21

# The copy rows this builder owns - what -WriteSample empties.
$ProseExact = @('Description', 'ReleaseNotes', 'ShortDescription', 'Title', 'CopyrightTrademarkInformation')

$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

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

# WINDOWS-STORE rule 8: CRLF is the only line break the file may carry - inside a quoted cell as
# much as between records. The decks are LF files, and a git checkout may have normalised the
# export; the writer canonicalises every cell to CRLF and refuses to emit anything else.
function Normalize-CellBreaks([string] $value) {
    if ($null -eq $value) { return '' }
    $value.Replace("`r`n", "`n").Replace("`n", "`r`n")
}

# WINDOWS-STORE rule 16: the machine-translated "What's new" columns carry the English text, folded
# to the rule 9 cap. Whole sentences first, then the last word, then a hard cut.
function Fold-ToCap([string] $text, [int] $cap) {
    $t = Normalize-CellBreaks $text
    if ($t.Length -le $cap) { return $t }
    $room = $t.Substring(0, $cap)
    $breaks = [regex]::Matches($room, '(?:\.\s|!\s|\?\s|\r\n)')
    if ($breaks.Count -gt 0) { return $t.Substring(0, $breaks[$breaks.Count - 1].Index + $breaks[$breaks.Count - 1].Length).TrimEnd() }
    $space = $room.LastIndexOf(' ')
    if ($space -gt 0) { return $t.Substring(0, $space).TrimEnd() }
    $room
}

# The writer's gate: refuse an output that carries a BOM (never emitted - UTF8Encoding($false) is the
# only encoding this script writes with) or a bare LF anywhere, cells included.
function Assert-Writable([string] $content, [string] $what) {
    if ([regex]::Matches($content, '(?<!\r)\n').Count -gt 0) {
        throw "Refusing to write $what with a bare LF in it (WINDOWS-STORE rule 8): the writer emits CRLF only, inside quoted cells as much as between records."
    }
}

$quoteEverything = -not $Faithful

function Quote([string] $value) {
    if ($null -eq $value) { $value = '' }
    $value = Normalize-CellBreaks $value
    if ($quoteEverything) { return '"' + $value.Replace('"', '""') + '"' }
    if ($value.IndexOfAny([char[]] @(',', '"', "`r", "`n")) -ge 0) { return '"' + $value.Replace('"', '""') + '"' }
    $value
}

$InputPath = if ($FillNothing) { $Sample } else { $Export }
if (-not (Test-Path $InputPath)) {
    Write-Host "Input file not found: $InputPath" -ForegroundColor Red
    exit 2
}

$rows    = Import-Csv $InputPath
$columns = $rows[0].PSObject.Properties.Name
$langs   = $columns | Select-Object -Skip 4          # Field, ID, Type (Type), default, then languages

# WINDOWS-STORE rule 7: every language the product ships exists as a column before the import. A
# column an import cannot find is dropped silently - the Urdu lesson - so its absence fails the run.
$absentLanguages = @($ExpectedLanguages | Where-Object { $langs -notcontains $_ })
if ($absentLanguages.Count -gt 0) {
    Write-Host "The export has no column for: $($absentLanguages -join ', ')." -ForegroundColor Red
    Write-Host 'Add each language in Partner Center (Store listings -> Manage additional languages), re-export and run again - an import cannot create a column, and a language with no column has its copy dropped without an error.' -ForegroundColor Red
    exit 2
}

$capFailures = New-Object System.Collections.Generic.List[string]

$copy = @{}
if (-not ($FillNothing -or $LogoFlagOnly -or $WriteSample)) {
    foreach ($code in $langs) {
        $name = if ($FileForCode.ContainsKey($code)) { $FileForCode[$code] } else { $code }
        $path = Join-Path $LangDir "$name.txt"
        if (-not (Test-Path $path)) { continue }
        $deck = Read-LangFile $path
        $copy[$code] = $deck
        # WINDOWS-STORE rule 9: check the deck itself, not only what lands in the table. A fresh
        # export (the only one an import may be built from) has empty cells, so every deck value is
        # a cell about to be written. One cell over its cap loses the whole language, and the deck
        # file is where the fix belongs.
        foreach ($field in @($deck.Keys)) {
            $limit = if ($field -match '^Feature\d+$') { $FeatureCap }
                     elseif ($CapByField.ContainsKey($field)) { $CapByField[$field] }
                     else { 0 }
            if ($limit -gt 0) {
                $length = (Normalize-CellBreaks $deck[$field]).Length
                if ($length -gt $limit) {
                    $capFailures.Add("$code ${field}: $length characters; Partner Center allows $limit (source: msix/listing/$name.txt)")
                }
            }
        }
        $deckTerms = @($deck.Keys | Where-Object { $_ -match '^SearchTerm\d+$' } | ForEach-Object { [string] $deck[$_] } | Where-Object { $_ -ne '' })
        if ($deckTerms.Count -gt $MaxSearchTerms) {
            $capFailures.Add("$code search terms: $($deckTerms.Count) in msix/listing/$name.txt; Partner Center accepts at most $MaxSearchTerms")
        }
        $deckWords = ($deckTerms | ForEach-Object { @(($_ -split '\s+') | Where-Object { $_ }).Count } | Measure-Object -Sum).Sum
        if ($deckWords -gt $MaxSearchTermWords) {
            $capFailures.Add("$code search terms: $deckWords words in msix/listing/$name.txt; Partner Center accepts at most $MaxSearchTermWords in total")
        }
        $deckDuplicates = @($deckTerms | Group-Object { $_.ToLowerInvariant() } | Where-Object Count -gt 1 | ForEach-Object { $_.Name })
        if ($deckDuplicates.Count -gt 0) {
            $capFailures.Add("$code search terms repeat in msix/listing/$name.txt: $($deckDuplicates -join ', ')")
        }
    }
}

# The English "What's new" every machine-translated column carries (rule 16). It comes from the
# export's own en-us cell, so a fresh export is always the accumulated text the Store has not seen.
$releaseRow = $rows | Where-Object Field -eq 'ReleaseNotes'
$englishReleaseNotes = if ($releaseRow) { $releaseRow.'en-us' } else { '' }

$filled = @{}
$sourceOf = @{}     # "<lang>|<Field>" -> where the value came from, for the cap-check message
foreach ($row in $rows) {
    foreach ($code in $langs) {
        if ($row.$code -ne '') { continue }                       # never overwrite Partner Center
        if (-not $copy.ContainsKey($code)) { continue }
        if ($SkipFields -contains $row.Field) { continue }
        $value = if ($SharedRows -contains $row.Field) { $row.'en-us' }
                 elseif ($copy[$code].ContainsKey($row.Field)) { $copy[$code][$row.Field] }
                 elseif ($row.Field -eq 'ReleaseNotes' -and $englishReleaseNotes) { Fold-ToCap $englishReleaseNotes $CapByField['ReleaseNotes'] }
                 else { '' }
        if ($value -eq '') { continue }
        $row.$code = $value
        $sourceOf["$code|$($row.Field)"] = if ($copy[$code].ContainsKey($row.Field)) { "msix/listing/$(if ($FileForCode.ContainsKey($code)) { $FileForCode[$code] } else { $code }).txt" }
                                            elseif ($SharedRows -contains $row.Field) { "the export's en-us column" }
                                            else { "the English release notes (rule 16)" }
        if (-not $filled.ContainsKey($code)) { $filled[$code] = 0 }
        $filled[$code]++
    }
}

# --- correction: OverrideLogosForWin10 --------------------------------------------------------
# The flag promises Windows-10 override logos for that listing, and Partner Center then holds the
# listing Incomplete until the logo files exist. An early version of this script copied the flag
# from en-us into every language along with Title and the copyright, so ten listings ended up
# claiming logos they do not have - which is exactly why they read Incomplete while Urdu, entered by
# hand and left at False, read Complete. Clear it wherever the language has no logo of its own.
$logoRows = @('StoreLogo720x1080', 'StoreLogo1080x1080', 'StoreLogo300x300',
              'StoreLogoOverride150x150', 'StoreLogoOverride71x71')
$flagRow = $rows | Where-Object Field -eq 'OverrideLogosForWin10'
$cleared = @()
foreach ($code in $langs) {
    if ($flagRow.$code -ne 'True') { continue }
    $hasLogo = $logoRows | Where-Object { ($rows | Where-Object Field -eq $_).$code -ne '' }
    if ($hasLogo) { continue }
    $flagRow.$code = 'False'
    $cleared += $code
}

function Format-Csv {
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add(($columns | ForEach-Object { Quote $_ }) -join ',')
    foreach ($row in $rows) { $lines.Add((($columns | ForEach-Object { Quote $row.$_ }) -join ',')) }
    # The export ends without a trailing newline; keep it that way.
    $lines -join "`r`n"
}

# --- -WriteSample: the rule 13 fixture ---------------------------------------------------------
if ($WriteSample) {
    foreach ($row in $rows) {
        if ($ProseExact -contains $row.Field -or $row.Field -match '^(Feature|SearchTerm)\d+$') {
            foreach ($code in $langs) { $row.$code = '' }
        }
    }
    $content = Format-Csv
    Assert-Writable $content 'the sample export'
    [System.IO.File]::WriteAllText($Sample, $content, $Utf8NoBom)
    Write-Host "Wrote the emptied sample export: $Sample ($($rows.Count) rows, $($langs.Count) language columns, copy cells emptied)." -ForegroundColor Green
    Write-Host "Prove it:  .\msix\build-store-listing-csv.ps1 -FillNothing" -ForegroundColor DarkGray
    exit 0
}

# --- WINDOWS-STORE rule 9: cap every cell before anything is written ---------------------------
# The deck was checked at load; this pass covers every cell that reaches the file - copy the
# export already holds, the shared rows and the rule-16 fallback included.
foreach ($row in $rows) {
    $field = [string] $row.Field
    $limit = if ($field -match '^Feature\d+$') { $FeatureCap }
             elseif ($CapByField.ContainsKey($field)) { $CapByField[$field] }
             else { 0 }
    if ($limit -le 0) { continue }
    foreach ($code in $langs) {
        $cell = [string] $row.$code
        if ($cell -eq '') { continue }
        $length = (Normalize-CellBreaks $cell).Length
        if ($length -gt $limit) {
            $source = if ($sourceOf.ContainsKey("$code|$field")) { $sourceOf["$code|$field"] } else { 'the export' }
            $capFailures.Add("$code ${field}: $length characters; Partner Center allows $limit (source: $source)")
        }
    }
}
# Rule 15: at most 7 search terms per language and no duplicate within a language's set - Store
# search matches literally, and a repeated term is a wasted slot at best.
$searchRows = @($rows | Where-Object { $_.Field -match '^SearchTerm\d+$' })
foreach ($code in $langs) {
    $terms = @($searchRows | ForEach-Object { [string] $_.$code } | Where-Object { $_ -ne '' })
    $deckName = "msix/listing/$(if ($FileForCode.ContainsKey($code)) { $FileForCode[$code] } else { $code }).txt"
    if ($terms.Count -gt $MaxSearchTerms) {
        $capFailures.Add("$code search terms: $($terms.Count); Partner Center accepts at most $MaxSearchTerms (source: $deckName)")
    }
    $words = ($terms | ForEach-Object { @(($_ -split '\s+') | Where-Object { $_ }).Count } | Measure-Object -Sum).Sum
    if ($words -gt $MaxSearchTermWords) {
        $capFailures.Add("$code search terms: $words words; Partner Center accepts at most $MaxSearchTermWords in total (source: $deckName)")
    }
    $duplicates = @($terms | Group-Object { $_.ToLowerInvariant() } | Where-Object Count -gt 1 | ForEach-Object { $_.Name })
    if ($duplicates.Count -gt 0) {
        $capFailures.Add("$code search terms repeat: $($duplicates -join ', ') (source: $deckName)")
    }
}
if ($capFailures.Count -gt 0) {
    Write-Host ''
    $capFailures | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    Write-Host ''
    Write-Host "The listing cannot be built: $($capFailures.Count) cell(s) over their Partner Center cap. Nothing was written." -ForegroundColor Red
    exit 1
}

if ($Check) {
    if (-not (Test-Path $Dest)) {
        Write-Host "Target import CSV not found: $Dest" -ForegroundColor Red
        exit 2
    }
    $content = Format-Csv
    Assert-Writable $content 'the import CSV'
    $rendered = [System.Text.Encoding]::UTF8.GetBytes($content)
    $existing = [System.IO.File]::ReadAllBytes($Dest)
    if (-not [System.Linq.Enumerable]::SequenceEqual([byte[]] $rendered, [byte[]] $existing)) {
        Write-Host "DRIFT: $Dest does not match re-rendered export + language copy (byte comparison - line endings included)." -ForegroundColor Red
        Write-Host 'Run .\msix\build-store-listing-csv.ps1 to regenerate it.' -ForegroundColor Yellow
        exit 1
    }
    Write-Host 'Store listing import CSV matches export and language sources (byte-identical).' -ForegroundColor Green
    exit 0
}

if ($FillNothing) {
    $content = Format-Csv
    Assert-Writable $content 'the round trip'
    $rendered = [System.Text.Encoding]::UTF8.GetBytes($content)
    $expected = [System.IO.File]::ReadAllBytes($Sample)
    # Preserve the input dialect for a no-change proof; actual import writers remain no-BOM.
    if ($expected.Length -ge 3 -and $expected[0] -eq 0xEF -and $expected[1] -eq 0xBB -and $expected[2] -eq 0xBF) {
        $rendered = [byte[]](@(0xEF, 0xBB, 0xBF) + $rendered)
    }
    if ([System.Linq.Enumerable]::SequenceEqual([byte[]] $rendered, [byte[]] $expected)) {
        Write-Host ("Round trip is byte-identical: {0} bytes, {1} rows, nothing filled." -f $expected.Length, $rows.Count) -ForegroundColor Green
        exit 0
    }
    Write-Host ("Round trip differs: the sample is {0} bytes, the render is {1} bytes - a bug in the writer, not in the copy." -f $expected.Length, $rendered.Length) -ForegroundColor Red
    exit 1
}

# The plain CSV carries text only. A relative image path in it would be rejected outright - Partner
# Center resolves such a path only against a folder upload, and "Import .csv" has nowhere to look.
$content = Format-Csv
Assert-Writable $content 'the import CSV'
[System.IO.File]::WriteAllText($Dest, $content, $Utf8NoBom)

# --- screenshots -------------------------------------------------------------------------------
# One shot per language, produced by tools/uitest/Save-StoreScreenshots.ps1, staged into its own
# folder with a second copy of the CSV that does reference them. A language that already has a
# screenshot keeps it and gets ours in the next free slot; the rest get slot 1, which is what turns
# them from Incomplete into Complete.
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
        $slot = 1..10 | Where-Object { ($rows | Where-Object Field -eq "DesktopScreenshot$_").$code -eq '' } | Select-Object -First 1
        if (-not $slot) { continue }
        if ($ScreenshotOnlyMissing -and $slot -ne 1) { continue }   # already carries a screenshot
        Copy-Item $png $stageDir
        ($rows | Where-Object Field -eq "DesktopScreenshot$slot").$code = "$stageName/settings-$code.png"
        $staged += [pscustomobject]@{ Language = $code; Slot = "DesktopScreenshot$slot"; File = "settings-$code.png" }
    }
    # Exactly one .csv in the folder - Partner Center refuses a folder holding more than one.
    $folderContent = Format-Csv
    Assert-Writable $folderContent 'the import folder CSV'
    [System.IO.File]::WriteAllText((Join-Path $stageDir 'listings.csv'), $folderContent, $Utf8NoBom)
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
if ($cleared.Count -gt 0) {
    Write-Host ''
    Write-Host ("OverrideLogosForWin10 cleared (claimed logos it does not have): {0}" -f ($cleared -join ', '))
    Write-Host ''
}
Write-Host "Wrote $Dest" -ForegroundColor Green

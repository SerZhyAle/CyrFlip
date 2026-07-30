<#
    Renders the two human-readable mirrors of the Store listing copy from the one source of truth.

      source of truth   msix/store-listing-export.csv   what Partner Center exports and imports
      mirror            msix/store-listings.md          EN / RU / UK blocks, ready to paste
      mirror            store/listing-<locale>.txt      one plain-text file per locale

    Why this script exists: both mirrors used to be hand-kept, and they drifted - the day this was
    written they were a release behind on the translator paragraph and on four feature lines, in
    three languages. A stale mirror is worse than no mirror, because the mirror is exactly what
    somebody pastes into Partner Center when the CSV import is unavailable.

      .\msix\render-listing-mirrors.ps1            # rewrite both mirrors from the CSV
      .\msix\render-listing-mirrors.ps1 -Check     # render in memory and compare; exit 1 on drift

    Only the copy blocks are touched. Everything else in those files - the field-limit notes, the
    system requirements (which the CSV has no row for), the runFullTrust justification and the
    submission notes in the Markdown - is hand-written prose and is preserved byte for byte.

    The ten other listing languages have no mirror at all: the CSV and msix/listing/<lang>.txt are
    their only home, which is why this script says nothing about them.
#>
[CmdletBinding()]
param(
    # Do not write: render, compare with what is on disk, and fail if they differ.
    [switch] $Check
)
$ErrorActionPreference = 'Stop'

$MsixDir  = $PSScriptRoot
$RepoRoot = Split-Path $MsixDir -Parent
$Export   = Join-Path $MsixDir 'store-listing-export.csv'
$Markdown = Join-Path $MsixDir 'store-listings.md'

$rows = Import-Csv $Export

function Field([string] $name, [string] $locale) {
    $row = $rows | Where-Object { $_.Field -eq $name }
    if (-not $row) { return '' }
    return ($row.$locale -replace "`r`n", "`n")
}

function Features([string] $locale) {
    $out = New-Object System.Collections.Generic.List[string]
    foreach ($i in 1..20) {
        $value = Field "Feature$i" $locale
        if ($value -ne '') { $out.Add($value) }
    }
    return $out
}

function SearchTerms([string] $locale) {
    $out = New-Object System.Collections.Generic.List[string]
    foreach ($i in 1..7) {
        $value = Field "SearchTerm$i" $locale
        if ($value -ne '') { $out.Add($value) }
    }
    return $out
}

# Write with the newline and BOM the file already uses: a mirror that changes shape on every run
# would drown the one line that actually changed.
function Save([string] $path, [string[]] $lines, [bool] $write) {
    $bytes    = [IO.File]::ReadAllBytes($path)
    $bom      = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    $existing = [IO.File]::ReadAllText($path)
    $newline  = if ($existing.Contains("`r`n")) { "`r`n" } else { "`n" }
    $trailing = if ($existing.EndsWith("`n")) { $newline } else { '' }
    $rendered = ($lines -join $newline) + $trailing
    if ($rendered -eq $existing) { return $false }
    if ($write) { [IO.File]::WriteAllText($path, $rendered, (New-Object Text.UTF8Encoding($bom))) }
    return $true
}

# --- Markdown: "**Label**" then either a "> " quote line or a fenced block -----------------------

$MdSections = @(
    @{ locale = 'en-us'; quote = '**Short description**'; blocks = [ordered]@{
        '**Description**'                        = 'Description'
        '**Product features (one per line)**'    = 'Features'
        "**What's new in this version**"         = 'ReleaseNotes' } },
    @{ locale = 'ru'; quote = '**Краткое описание**'; blocks = [ordered]@{
        '**Описание**'                              = 'Description'
        '**Функции продукта (по одной в строке)**'   = 'Features'
        '**Что нового в этой версии**'              = 'ReleaseNotes' } },
    @{ locale = 'uk'; quote = '**Короткий опис**'; blocks = [ordered]@{
        '**Опис**'                                  = 'Description'
        '**Функції продукту (по одній у рядку)**'    = 'Features'
        '**Що нового в цій версії**'                = 'ReleaseNotes' } }
)

function Render-Markdown {
    $lines = [Collections.ArrayList]::new()
    foreach ($line in ([IO.File]::ReadAllText($Markdown) -replace "`r`n", "`n").TrimEnd("`n").Split("`n")) { $lines.Add($line) | Out-Null }

    foreach ($section in $MdSections) {
        $locale = $section.locale

        # The short description is a single-line blockquote right under its label.
        $at = $lines.IndexOf($section.quote)
        if ($at -lt 0) { throw "store-listings.md: label not found - $($section.quote)" }
        if (-not $lines[$at + 1].StartsWith('> ')) { throw "store-listings.md: no blockquote under $($section.quote)" }
        $lines[$at + 1] = '> ' + (Field 'ShortDescription' $locale)

        foreach ($label in $section.blocks.Keys) {
            $at = $lines.IndexOf($label)
            if ($at -lt 0) { throw "store-listings.md: label not found - $label" }
            if ($lines[$at + 1] -ne '```') { throw "store-listings.md: no fenced block under $label" }
            $close = $at + 2
            while ($close -lt $lines.Count -and $lines[$close] -ne '```') { $close++ }
            if ($close -ge $lines.Count) { throw "store-listings.md: unterminated block under $label" }

            $content = switch ($section.blocks[$label]) {
                'Features' { @(Features $locale) }
                default    { (Field $section.blocks[$label] $locale).Split("`n") }
            }
            $lines.RemoveRange($at + 2, $close - ($at + 2))
            $lines.InsertRange($at + 2, [string[]] $content)
        }
    }
    return $lines.ToArray()
}

# --- Plain text: "HEADER" then a dashes line, content up to the next header ----------------------

$TxtFiles = @(
    @{ locale = 'en-us'; path = 'store\listing-en-US.txt'; sections = [ordered]@{
        'SHORT DESCRIPTION (max 1000 chars)'    = 'ShortDescription'
        'DESCRIPTION'                           = 'Description'
        'PRODUCT FEATURES (max 200 chars each)' = 'Features'
        "WHAT'S NEW IN THIS VERSION"            = 'ReleaseNotes'
        'SEARCH TERMS'                          = 'SearchTerms' } },
    @{ locale = 'ru'; path = 'store\listing-ru-RU.txt'; sections = [ordered]@{
        'КРАТКОЕ ОПИСАНИЕ (до 1000 знаков)'        = 'ShortDescription'
        'ОПИСАНИЕ'                                 = 'Description'
        'ФУНКЦИИ ПРОДУКТА (до 200 знаков каждая)'  = 'Features'
        'ЧТО НОВОГО В ЭТОЙ ВЕРСИИ'                 = 'ReleaseNotes'
        'ПОИСКОВЫЕ ЗАПРОСЫ'                        = 'SearchTerms' } },
    @{ locale = 'uk'; path = 'store\listing-uk-UA.txt'; sections = [ordered]@{
        'КОРОТКИЙ ОПИС (до 1000 знаків)'           = 'ShortDescription'
        'ОПИС'                                     = 'Description'
        'ФУНКЦІЇ ПРОДУКТУ (до 200 знаків кожна)'   = 'Features'
        'ЩО НОВОГО В ЦІЙ ВЕРСІЇ'                   = 'ReleaseNotes'
        'ПОШУКОВІ ЗАПИТИ'                          = 'SearchTerms' } }
)

function Render-Text($file) {
    $path  = Join-Path $RepoRoot $file.path
    $lines = [Collections.ArrayList]::new()
    foreach ($line in ([IO.File]::ReadAllText($path) -replace "`r`n", "`n").TrimEnd("`n").Split("`n")) { $lines.Add($line) | Out-Null }

    # Header lines, found by the row of dashes underneath. Walked back to front so an insertion
    # never moves a section we have not reached yet.
    $headers = @()
    for ($i = 0; $i -lt $lines.Count - 1; $i++) {
        if ($lines[$i + 1] -match '^-{3,}$' -and $lines[$i].Trim() -ne '') { $headers += $i }
    }

    foreach ($index in ($headers | Sort-Object -Descending)) {
        $header = $lines[$index]
        if (-not $file.sections.Contains($header)) { continue }      # e.g. SYSTEM REQUIREMENTS

        $start = $index + 2
        $end   = $start                                              # first line past the content
        while ($end -lt $lines.Count) {
            if ($end + 1 -lt $lines.Count -and $lines[$end + 1] -match '^-{3,}$' -and $lines[$end].Trim() -ne '') { break }
            $end++
        }
        while ($end -gt $start -and $lines[$end - 1].Trim() -eq '') { $end-- }   # keep the blank run

        $kind = $file.sections[$header]
        $content = switch ($kind) {
            'Features'    { @(Features $file.locale | ForEach-Object { '• ' + $_ }) }
            'SearchTerms' { @((SearchTerms $file.locale) -join ', ') }
            default       { (Field $kind $file.locale).Split("`n") }
        }
        if ($content.Count -eq 0 -or ($content.Count -eq 1 -and $content[0] -eq '')) { continue }

        $lines.RemoveRange($start, $end - $start)
        $lines.InsertRange($start, [string[]] $content)
    }
    return @{ path = $path; lines = $lines.ToArray() }
}

# --- Run ----------------------------------------------------------------------------------------

$write   = -not $Check
$changed = @()

if (Save $Markdown (Render-Markdown) $write) { $changed += 'msix/store-listings.md' }
foreach ($file in $TxtFiles) {
    $rendered = Render-Text $file
    if (Save $rendered.path $rendered.lines $write) { $changed += ($file.path -replace '\\', '/') }
}

if ($Check) {
    if ($changed.Count -gt 0) {
        foreach ($name in $changed) { Write-Host "DRIFT: $name is not what the CSV renders" -ForegroundColor Red }
        Write-Host 'Run .\msix\render-listing-mirrors.ps1 to regenerate them.' -ForegroundColor Yellow
        exit 1
    }
    Write-Host 'Listing mirrors match the export.' -ForegroundColor Green
    exit 0
}

if ($changed.Count -gt 0) {
    foreach ($name in $changed) { Write-Host "rewrote $name" -ForegroundColor Cyan }
}
else {
    Write-Host 'Nothing to do - both mirrors already match the export.' -ForegroundColor Green
}
exit 0

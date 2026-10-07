<#
    Test-Positioning.ps1 - holds every public surface to POSITIONING.md (ticket S0058, SITE-REPRESENTATION rules 1-2).

    Reads the facts block of POSITIONING.md and checks three things:
      1. Frozen anchors - each { file, regex, value } still reads back as its value: the exe and product
         name, the winget identifier / package name / moniker, the MSIX identity defaults and display
         names, the startup task id, the VS Code extension id. A rename here orphans installed users.
      2. Retired phrases - none of the old "this is a layout fixer" leads survives on a public surface.
      3. The descriptor's mark - the per-language phrase of the descriptor appears where a reader meets
         the product first: the landing pages (title or description), the Store listing (short
         description and the first paragraph of the description, all 13 columns), the language decks,
         the winget locales, the MSIX manifest, the three READMEs, the About text, the extension.

    Verdict per CHECK-VERDICT: exit 0 PASS, 1 FAIL, 2 NOT VERIFIED (the source or a surface cannot be
    read); the last stdout line is "positioning: PASS|FAIL (n)|NOT VERIFIED (n)".

      pwsh -File tools/site/Test-Positioning.ps1
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Import-Module (Join-Path $RepoRoot 'tools\checks\CheckVerdict.psm1') -Force
Set-CheckSubject 'positioning'

function Read-Text([string]$relative) {
    $path = Join-Path $RepoRoot $relative
    if (-not (Test-Path -LiteralPath $path)) {
        Add-CheckFinding -Severity notverified -Name "missing-$relative" -Reason "$relative does not exist"
        return $null
    }
    return [System.Net.WebUtility]::HtmlDecode([IO.File]::ReadAllText($path))
}

function Test-Contains([string]$text, [string]$phrase) {
    return $null -ne $text -and $text.IndexOf($phrase, [StringComparison]::OrdinalIgnoreCase) -ge 0
}

# --- the source -----------------------------------------------------------------------------------
$source = Read-Text 'POSITIONING.md'
if ($null -eq $source) { Complete-Check }
$block = [regex]::Match($source, '(?s)## Facts\s+```json\s*(\{.*?\})\s*```')
if (-not $block.Success) {
    Add-CheckFinding -Severity notverified -Name 'facts-block' -Reason 'POSITIONING.md has no ```json facts block under "## Facts"'
    Complete-Check
}
$facts = $block.Groups[1].Value | ConvertFrom-Json
$locales = @($facts.descriptor.PSObject.Properties.Name)
foreach ($code in $locales) {
    $mark = $facts.mark.$code
    if (-not $mark) { Add-CheckFinding -Severity fail -Name "mark-$code" -Reason "POSITIONING.md has no mark for '$code'" }
    elseif (-not (Test-Contains $facts.descriptor.$code $mark)) {
        Add-CheckFinding -Severity fail -Name "mark-in-descriptor-$code" -Reason "the mark '$mark' is not part of the '$code' descriptor"
    }
}

# --- 1. frozen anchors ----------------------------------------------------------------------------
foreach ($anchor in $facts.frozen) {
    $text = Read-Text $anchor.file
    if ($null -eq $text) { continue }
    $found = [regex]::Matches($text, $anchor.regex)
    if ($found.Count -eq 0) {
        Add-CheckFinding -Severity fail -Name "frozen-$($anchor.file)" -Reason "pattern $($anchor.regex) no longer matches in $($anchor.file)"
        continue
    }
    foreach ($m in $found) {
        if ($m.Groups[1].Value.Trim() -cne $anchor.value) {
            Add-CheckFinding -Severity fail -Name "frozen-$($anchor.file)" -Reason "$($anchor.file) reads '$($m.Groups[1].Value.Trim())' where the frozen value is '$($anchor.value)'"
        }
    }
}

# --- the surfaces ---------------------------------------------------------------------------------
$standalone = @($locales | Where-Object { $_ -notin @('en', 'ru', 'uk') })
$surfaces = [ordered]@{}
foreach ($rel in @('docs/index.html', 'docs/guide.html', 'docs/ru/guide.html', 'docs/uk/guide.html',
                   'README.md', 'README_RU.md', 'README_UK.md', 'msix/AppxManifest.xml',
                   'msix/store-listing-export.csv', 'msix/store-listings.md',
                   'store/listing-en-US.txt', 'store/listing-ru-RU.txt', 'store/listing-uk-UA.txt',
                   'winget/SerZhyAle.CyrFlip.locale.en-US.yaml', 'winget/SerZhyAle.CyrFlip.locale.ru-RU.yaml',
                   'winget/SerZhyAle.CyrFlip.locale.uk-UA.yaml', 'vscode-extension/package.json',
                   'vscode-extension/README.md', 'src/CyrFlip/Localization.General.cs')) {
    $surfaces[$rel] = Read-Text $rel
}
foreach ($code in $standalone) { $surfaces["docs/$code/index.html"] = Read-Text "docs/$code/index.html" }
foreach ($file in @(Get-ChildItem (Join-Path $RepoRoot 'msix/listing') -Filter '*.txt' -File)) {
    $surfaces["msix/listing/$($file.Name)"] = Read-Text "msix/listing/$($file.Name)"
}

# --- 2. retired phrases ---------------------------------------------------------------------------
foreach ($entry in $surfaces.GetEnumerator()) {
    foreach ($phrase in $facts.retired) {
        if (Test-Contains $entry.Value $phrase) {
            Add-CheckFinding -Severity fail -Name "retired-$($entry.Key)" -Reason "$($entry.Key) still says '$phrase'"
        }
    }
}

# --- 3. the mark where the product is met first -----------------------------------------------------
function Require-Mark([string]$where, [string]$text, [string]$code) {
    if ($null -eq $text) { return }
    $mark = $facts.mark.$code
    if (-not (Test-Contains $text $mark)) {
        Add-CheckFinding -Severity fail -Name "mark-$where" -Reason "$where lacks the '$code' descriptor mark '$mark'"
    }
}
function Head-Of([string]$html) {
    if ($null -eq $html) { return $null }
    $title = [regex]::Match($html, '(?s)<title>(.*?)</title>').Groups[1].Value
    $desc = [regex]::Match($html, '<meta name="description" content="([^"]*)"').Groups[1].Value
    return "$title`n$desc"
}

# Landing: the trilingual page carries all three of its languages, each other page its own.
foreach ($code in @('en', 'ru', 'uk')) { Require-Mark "docs/index.html ($code)" $surfaces['docs/index.html'] $code }
foreach ($code in $standalone) { Require-Mark "docs/$code/index.html title or description" (Head-Of $surfaces["docs/$code/index.html"]) $code }
Require-Mark 'docs/guide.html description' (Head-Of $surfaces['docs/guide.html']) 'en'
Require-Mark 'docs/ru/guide.html description' (Head-Of $surfaces['docs/ru/guide.html']) 'ru'
Require-Mark 'docs/uk/guide.html description' (Head-Of $surfaces['docs/uk/guide.html']) 'uk'

# READMEs: the lead, i.e. the first 15 lines.
foreach ($pair in @(@('README.md', 'en'), @('README_RU.md', 'ru'), @('README_UK.md', 'uk'))) {
    $text = $surfaces[$pair[0]]
    if ($null -ne $text) { Require-Mark "$($pair[0]) lead" ((($text -split "`n") | Select-Object -First 15) -join "`n") $pair[1] }
}

# Store listing: every column's short description and the first paragraph of its description.
$columnLocale = @{ 'en-us' = 'en'; 'pt-br' = 'pt'; 'pt' = 'pt'; 'zh-hans' = 'zh' }
$csvPath = Join-Path $RepoRoot 'msix/store-listing-export.csv'
if (Test-Path $csvPath) {
    $rows = Import-Csv $csvPath
    $columns = @($rows[0].PSObject.Properties.Name | Select-Object -Skip 4)
    $short = $rows | Where-Object Field -eq 'ShortDescription'
    $long = $rows | Where-Object Field -eq 'Description'
    foreach ($column in $columns) {
        $code = if ($columnLocale.ContainsKey($column)) { $columnLocale[$column] } else { $column }
        if ($code -notin $locales) { continue }
        Require-Mark "Store $column ShortDescription" $short.$column $code
        $first = (($long.$column -replace "`r`n", "`n") -split "`n`n")[0]
        Require-Mark "Store $column Description, first paragraph" $first $code
    }
}

# Language decks upstream of the CSV.
foreach ($key in @($surfaces.Keys | Where-Object { $_ -like 'msix/listing/*.txt' })) {
    $code = [IO.Path]::GetFileNameWithoutExtension($key)
    $deck = $surfaces[$key]
    if ($null -eq $deck -or $code -notin $locales) { continue }
    $shortDeck = [regex]::Match($deck, '(?s)@@ShortDescription\s*\n(.*?)\n@@').Groups[1].Value
    Require-Mark "$key ShortDescription" $shortDeck $code
}

# winget, MSIX, the About text, the extension.
foreach ($pair in @(@('en-US', 'en'), @('ru-RU', 'ru'), @('uk-UA', 'uk'))) {
    $rel = "winget/SerZhyAle.CyrFlip.locale.$($pair[0]).yaml"
    $line = [regex]::Match([string]$surfaces[$rel], '(?m)^ShortDescription: (.*)$').Groups[1].Value
    Require-Mark "$rel ShortDescription" $line $pair[1]
}
$appx = [regex]::Match([string]$surfaces['msix/AppxManifest.xml'], 'uap:VisualElements[^>]*?Description="([^"]*)"').Groups[1].Value
Require-Mark 'msix/AppxManifest.xml Description' $appx 'en'
foreach ($code in $locales) { Require-Mark 'src/CyrFlip/Localization.General.cs (About)' $surfaces['src/CyrFlip/Localization.General.cs'] $code }
$extension = [regex]::Match([string]$surfaces['vscode-extension/package.json'], '"description":\s*"([^"]*)"').Groups[1].Value
Require-Mark 'vscode-extension/package.json description' $extension 'en'

Complete-Check

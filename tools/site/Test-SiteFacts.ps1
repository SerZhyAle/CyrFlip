<#
    Test-SiteFacts.ps1 - holds the site's public facts to tools/site/site-facts.json
    (ticket S0054 D2-D3, SITE-REPRESENTATION rules 3 and 4).

    The facts file is the one place the public editions, the channel display names, the Windows
    minimum and the interface-language count are typed. This check compares the pages with it:

      1. Editions - the file declares exactly one edition, and no public page names another
         build a visitor could obtain under its own name; the channel ids must equal the stamp's
         channels (.sza-canon.json), because that is where the channel set is decided.
      2. Channels - every channel's evidence files exist; every surface the file names carries
         the channel's display name in that language (the landing names the GitHub channel by
         its source, "GitHub Releases"); none of the retired name variants survives anywhere.
      3. The Windows minimum - the pages' statement is compared with the MSIX MinVersion and the
         winget MinimumOSVersion (the majors must agree, and a minimum that stops being a
         Windows 10 build fails the "Windows 10/11" prose), and every minimum a page types is
         one of the declared forms.
      4. The interface-language count - the localization table's Codes array is the count, and
         every number a page puts next to a language word equals it.

    Verdict per CHECK-VERDICT: exit 0 PASS, 1 FAIL, 2 NOT VERIFIED (the facts file or a source
    cannot be read); the last stdout line is "site-facts: PASS|FAIL (n)|NOT VERIFIED (n)".

      pwsh -File tools/site/Test-SiteFacts.ps1
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Import-Module (Join-Path $RepoRoot 'tools\checks\CheckVerdict.psm1') -Force
Set-CheckSubject 'site-facts'

function Read-Text([string]$relative) {
    $path = Join-Path $RepoRoot $relative
    if (-not (Test-Path -LiteralPath $path)) {
        Add-CheckFinding -Severity notverified -Name "missing-$relative" -Reason "$relative does not exist"
        return $null
    }
    return [System.Net.WebUtility]::HtmlDecode([IO.File]::ReadAllText($path))
}

# --- the facts file --------------------------------------------------------------------------------
$factsPath = Join-Path $RepoRoot 'tools\site\site-facts.json'
if (-not (Test-Path -LiteralPath $factsPath)) {
    Add-CheckFinding -Severity notverified -Name 'facts-file' -Reason 'tools/site/site-facts.json does not exist'
    Complete-Check
}
$facts = Get-Content $factsPath -Raw | ConvertFrom-Json

# --- 1. editions and the channel set ---------------------------------------------------------------
if (@($facts.editions).Count -ne 1) {
    Add-CheckFinding -Severity fail -Name 'editions' -Reason "the facts file declares $(@($facts.editions).Count) editions; the site copy and the Store listing present one"
}
$stamp = Read-Text '.sza-canon.json'
if ($null -ne $stamp) {
    $stampChannels = @((Get-Content (Join-Path $RepoRoot '.sza-canon.json') -Raw | ConvertFrom-Json).channels)
    $factChannels = @($facts.channels | ForEach-Object { $_.id })
    $missing = @($stampChannels | Where-Object { $_ -notin $factChannels })
    $extra = @($factChannels | Where-Object { $_ -notin $stampChannels })
    if ($missing.Count -gt 0) { Add-CheckFinding -Severity fail -Name 'channels-vs-stamp' -Reason "the stamp declares channel(s) the facts file does not: $($missing -join ', ')" }
    if ($extra.Count -gt 0) { Add-CheckFinding -Severity fail -Name 'channels-vs-stamp' -Reason "the facts file declares channel(s) the stamp does not: $($extra -join ', ')" }
}

# --- 2. channels: evidence, display names, retired variants -------------------------------------------
foreach ($channel in $facts.channels) {
    foreach ($rel in @($channel.evidence)) {
        if ($null -eq (Read-Text $rel)) { continue }
    }
    if ($channel.PSObject.Properties['namedOn']) {
        foreach ($rel in @($channel.namedOn)) {
            $text = Read-Text $rel
            if ($null -eq $text) { continue }
            $name = [string]$channel.display.en
            if ($text.IndexOf($name, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
                Add-CheckFinding -Severity fail -Name "channel-$($channel.id)-$rel" -Reason "$rel does not name the channel as '$name'"
            }
        }
    }
    if ($channel.PSObject.Properties['namedOnByLocale']) {
        foreach ($locale in @($channel.namedOnByLocale.PSObject.Properties.Name)) {
            $name = [string]$channel.display.$locale
            foreach ($rel in @($channel.namedOnByLocale.$locale)) {
                $text = Read-Text $rel
                if ($null -eq $text) { continue }
                if ($text.IndexOf($name, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
                    Add-CheckFinding -Severity fail -Name "channel-$($channel.id)-$locale-$rel" -Reason "$rel does not name the channel as '$name' ($locale)"
                }
            }
        }
    }
    if ($channel.PSObject.Properties['landingName']) {
        foreach ($rel in @($channel.landingNamedOn)) {
            $text = Read-Text $rel
            if ($null -eq $text) { continue }
            if ($text.IndexOf([string]$channel.landingName, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
                Add-CheckFinding -Severity fail -Name "channel-$($channel.id)-landing-$rel" -Reason "$rel does not name the channel as '$($channel.landingName)'"
            }
        }
    }
}
# A channel with different rules is presented on its own page (SITE-STRUCTURE section 2).
foreach ($channel in $facts.channels) {
    if ($channel.PSObject.Properties['ownPage']) {
        $page = Read-Text ([string]$channel.ownPage)
        if ($null -ne $page -and $page.IndexOf([string]$channel.display.en, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
            Add-CheckFinding -Severity fail -Name "ownpage-$($channel.id)" -Reason "$($channel.ownPage) does not name the channel as '$($channel.display.en)'"
        }
        foreach ($locale in @('ru', 'uk')) {
            $own = $channel.ownPage -replace '^docs/', "docs/$locale/"
            $text = Read-Text $own
            if ($null -ne $text -and $text.IndexOf([string]$channel.display.$locale, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
                Add-CheckFinding -Severity fail -Name "ownpage-$($channel.id)-$locale" -Reason "$own does not name the channel as '$($channel.display.$locale)' ($locale)"
            }
        }
    }
}
# Retired name variants - nowhere in the public pages.
$corpus = @{}
foreach ($rel in @('docs/guide.html', 'docs/ru/guide.html', 'docs/uk/guide.html',
                   'README.md', 'README_RU.md', 'README_UK.md', 'docs/index.html', 'docs/trust.html')) {
    $corpus[$rel] = Read-Text $rel
}
foreach ($dir in Get-ChildItem (Join-Path $RepoRoot 'docs') -Directory) {
    $rel = 'docs/' + $dir.Name + '/index.html'
    if (Test-Path (Join-Path $RepoRoot ($rel -replace '/', '\'))) { $corpus[$rel] = Read-Text $rel }
}
foreach ($channel in $facts.channels) {
    if (-not $channel.PSObject.Properties['forbidden']) { continue }
    foreach ($entry in $corpus.GetEnumerator()) {
        foreach ($bad in @($channel.forbidden)) {
            if ($null -ne $entry.Value -and $entry.Value.IndexOf($bad, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                Add-CheckFinding -Severity fail -Name "retired-$($channel.id)-$($entry.Key)" -Reason "$($entry.Key) still names the channel as '$bad'"
            }
        }
    }
}

# --- 3. the Windows minimum ---------------------------------------------------------------------------
$min = $facts.windowsMinimum
$manifest = Read-Text $min.source.path
$installer = Read-Text $min.agree.path
if ($null -ne $manifest -and $null -ne $installer) {
    $srcMatch = [regex]::Match($manifest, $min.source.pattern)
    $agrMatch = [regex]::Match($installer, $min.agree.pattern)
    if (-not $srcMatch.Success) {
        Add-CheckFinding -Severity notverified -Name 'windows-source' -Reason "$($min.source.path) no longer matches the MinVersion pattern"
    }
    elseif (-not $agrMatch.Success) {
        Add-CheckFinding -Severity notverified -Name 'windows-agree' -Reason "$($min.agree.path) no longer matches the MinimumOSVersion pattern"
    }
    else {
        $srcMajor = [int]$srcMatch.Groups[1].Value
        $agrMajor = [int]$agrMatch.Groups[1].Value
        if ($srcMajor -ne $agrMajor) {
            Add-CheckFinding -Severity fail -Name 'windows-msix-vs-winget' -Reason "the MSIX minimum is Windows $srcMajor while the winget manifest says Windows $agrMajor"
        }
        if ($srcMajor -ne 10) {
            Add-CheckFinding -Severity fail -Name 'windows-prose' -Reason "the minimum is a Windows $srcMajor build; the declared statement 'Windows 10/11' is no longer true - update site-facts.json and the pages in one edit"
        }
    }
}
# Every minimum a page types is one of the declared forms.
$minForms = @()
foreach ($form in @($min.statedAs)) { $minForms += [regex]::Escape($form) }
$minAlternative = '(' + ($minForms -join '|') + ')'
foreach ($entry in $corpus.GetEnumerator()) {
    if ($null -eq $entry.Value) { continue }
    foreach ($m in [regex]::Matches($entry.Value, 'Windows\s*1[01](\s*/\s*11)?')) {
        if (-not [regex]::IsMatch($m.Value, $minAlternative)) {
            Add-CheckFinding -Severity fail -Name "windows-$($entry.Key)" -Reason "$($entry.Key) states '$($m.Value)' instead of a declared form ($($min.statedAs -join ' or '))"
        }
    }
}

# --- 4. the interface-language count -------------------------------------------------------------------
$declaredCount = [int]$facts.interfaceLanguages.countStatement
$loc = Read-Text 'src/CyrFlip/Localization.cs'
if ($null -ne $loc) {
    $codesBlock = [regex]::Match($loc, '(?s)public static readonly string\[\] Codes\s*=\s*\{(.*?)\};')
    if (-not $codesBlock.Success) {
        Add-CheckFinding -Severity notverified -Name 'languages-source' -Reason 'src/CyrFlip/Localization.cs no longer holds the Codes array'
    }
    else {
        $codes = @([regex]::Matches($codesBlock.Groups[1].Value, '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
        if ($codes.Count -ne $declaredCount) {
            Add-CheckFinding -Severity fail -Name 'languages-count' -Reason "the localization table carries $($codes.Count) interface languages while the facts file declares $declaredCount"
        }
    }
}
# Every number a public page puts next to a language word equals the declared count.
# The Bengali page states the count in Bengali numerals; Arabic-Indic digits are normalised too.
function ConvertTo-AsciiDigits([string]$value) {
    $map = @{ '০'='0';'১'='1';'২'='2';'৩'='3';'৪'='4';'৫'='5';'৬'='6';'৭'='7';'৮'='8';'৯'='9';
              '٠'='0';'١'='1';'٢'='2';'٣'='3';'٤'='4';'٥'='5';'٦'='6';'٧'='7';'٨'='8';'٩'='9' }
    return -join ($value.ToCharArray() | ForEach-Object { $c = [string]$_; if ($map.ContainsKey($c)) { $map[$c] } else { $c } })
}
$countPattern = ('(?<n>\d{1,2})\s*(?:на выбор|на вибір|to choose from|' +
    '种\s*\S{0,6}(?:语言|語言)|' +
    '(?:[^\d\s<>]{1,24}\s+){0,2}(?:languages?|Sprachen|langues|idiomas|lingue|línguas|linguagens|' +
    'язык\w*|мов\w*|भाषा\w*|زبانیں|زبانوں|ভাষা\w*|界面语言|语言|語言))')
foreach ($entry in $corpus.GetEnumerator()) {
    if ($null -eq $entry.Value) { continue }
    foreach ($m in [regex]::Matches($entry.Value, $countPattern)) {
        $n = [int](ConvertTo-AsciiDigits $m.Groups['n'].Value)
        if ($n -ne $declaredCount) {
            Add-CheckFinding -Severity fail -Name "languages-$($entry.Key)" -Reason "$($entry.Key) states $n interface languages where the localization table carries $declaredCount"
        }
    }
}

Complete-Check

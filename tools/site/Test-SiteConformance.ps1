<#
.SYNOPSIS
    Verifies site conformance with PAGE-CONTENT 1.1, PAGE-STYLE 1.0, and SITE-FAMILY-MAP 1.1 contracts.
.DESCRIPTION
    Checks all acceptance criteria A1-A15 from PLAN/S0017_contract-product-web-pages-sync.md across
    rendered HTML pages, stylesheets, assets, READMEs, and pointer files.
.PARAMETER Online
    When specified, queries served resources via HTTP to verify published assets.
.PARAMETER Manual
    When specified, prints reminders for manual visual and browser checks.
#>
[CmdletBinding()]
param(
    [switch]$Online,
    [switch]$Manual
)

Import-Module "$PSScriptRoot/../checks/CheckVerdict.psm1" -Force
Set-CheckSubject "site-conformance"

$ErrorActionPreference = 'Stop'
$script:failures = 0
$script:passes = 0

function Assert-Check {
    param(
        [string]$Name,
        [bool]$Condition,
        [string]$Message = ''
    )
    if ($Condition) {
        Write-Host "  [PASS] $Name" -ForegroundColor Green
        $script:passes++
    } else {
        Write-Host "  [FAIL] ${Name}: $Message" -ForegroundColor Red
        Add-CheckFinding -Severity Fail -Name $Name -Reason $Message
        $script:failures++
    }
}

$repoRoot = (Resolve-Path "$PSScriptRoot/../..").Path
Push-Location $repoRoot

try {
    Write-Host "Running CyrFlip Site Conformance Checks (S0017)...`n" -ForegroundColor Cyan

    # A1. Vendored sza-kit.css
    Write-Host "=== A1. sza-kit.css Vendoring & .gitattributes ===" -ForegroundColor Yellow
    $gitAttrContent = Get-Content -Raw ".gitattributes"
    $hasKitNoText = $gitAttrContent -match 'docs/assets/sza-kit\.css\s+-text'
    Assert-Check "sza-kit.css has -text in .gitattributes" $hasKitNoText "Missing '-text' attribute in .gitattributes"

    $kitPath = "docs/assets/sza-kit.css"
    $kitBytes = [System.IO.File]::ReadAllBytes((Join-Path $repoRoot $kitPath))
    $md5 = [System.Security.Cryptography.MD5]::Create()
    $hash = [System.BitConverter]::ToString($md5.ComputeHash($kitBytes)).Replace("-", "").ToLower()
    $expectedHash = "33bb9ad27200b26b158ab935ad7aafbc"
    Assert-Check "sza-kit.css md5 is $expectedHash" ($hash -eq $expectedHash) "Found hash $hash"

    if ($Online) {
        try {
            $onlineUrl = "https://serzhyale.github.io/CyrFlip/assets/sza-kit.css"
            $onlineBytes = (Invoke-WebRequest -Uri $onlineUrl -UseBasicParsing).Content
            if ($onlineBytes -is [string]) {
                $onlineBytes = [System.Text.Encoding]::UTF8.GetBytes($onlineBytes)
            }
            $onlineHash = [System.BitConverter]::ToString($md5.ComputeHash($onlineBytes)).Replace("-", "").ToLower()
            Assert-Check "Served sza-kit.css matches reference md5" ($onlineHash -eq $expectedHash) "Online hash $onlineHash"
        } catch {
            Assert-Check "Served sza-kit.css reachable" $false $_.Exception.Message
        }
    }

    # A2. sza-lang Normalization
    Write-Host "`n=== A2. sza-lang Forward-Tolerant Normalization ===" -ForegroundColor Yellow
    $indexPath = "docs/index.html"
    $indexContent = Get-Content -Raw $indexPath
    $hasNormalizingScript = ($indexContent -match "l==='uk'") -and ($indexContent -match "l='ua'") -and ($indexContent -match "l!=='ru'&&l!=='en'&&l!=='ua'")
    Assert-Check "index.html normalizes 'uk' -> 'ua' and validates ru|en|ua" $hasNormalizingScript "Normalization logic missing or altered"

    # A3 & A4. Family Grid across 11 Landing Pages
    Write-Host "`n=== A3 & A4. Family Grid (8 Sibling Tools) across 11 Landing Pages ===" -ForegroundColor Yellow
    $landingPages = @(
        "docs/index.html",
        "docs/ar/index.html",
        "docs/bn/index.html",
        "docs/de/index.html",
        "docs/es/index.html",
        "docs/fr/index.html",
        "docs/hi/index.html",
        "docs/it/index.html",
        "docs/pt/index.html",
        "docs/ur/index.html",
        "docs/zh/index.html"
    )
    $expectedSiblings = @(
        "https://serzhyale.github.io/FastMediaSorter_mob_v2/",
        "https://serzhyale.github.io/FastMediaSorter_Lite/",
        "https://serzhyale.github.io/doc-html-translate/",
        "https://serzhyale.github.io/FileDO/",
        "https://serzhyale.github.io/universal-agent-kit/",
        "https://serzhyale.github.io/OneClickRunner/",
        "https://serzhyale.github.io/StreamsPlayer/",
        "https://sza.od.ua"
    )
    foreach ($lp in $landingPages) {
        $c = Get-Content -Raw $lp
        $hasGrid = $c -match 'class="tools-grid"'
        $gridLinks = [regex]::Matches($c, 'href="([^"]+)"') | ForEach-Object { $_.Groups[1].Value } | Where-Object {
            $expectedSiblings -contains $_
        }
        $count = ($gridLinks | Select-Object -Unique).Count
        Assert-Check "$lp has tools-grid with 8 family links" ($hasGrid -and $count -eq 8) "Found $count family links in $lp"
    }

    # A5. Pre-paint Script Resolver Order (40 Rendered HTML Pages)
    Write-Host "`n=== A5. Pre-paint Script Preceding Stylesheet in All 40 Rendered Pages ===" -ForegroundColor Yellow
    $allHtml = Get-ChildItem -Path "docs" -Filter "*.html" -Recurse | Where-Object {
        $_.FullName -notmatch '[\\/]en[\\/]index\.html' -and
        $_.FullName -notmatch '[\\/]ru[\\/]index\.html' -and
        $_.FullName -notmatch '[\\/]uk[\\/]index\.html'
    }
    Assert-Check "Found 40 rendered HTML pages" ($allHtml.Count -eq 40) "Found $($allHtml.Count) rendered pages"
    foreach ($h in $allHtml) {
        $c = Get-Content -Raw $h.FullName
        $scriptPos = $c.IndexOf('<script')
        $linkPos = $c.IndexOf('<link rel="stylesheet"')
        $ok = ($scriptPos -ge 0) -and ($linkPos -ge 0) -and ($scriptPos -lt $linkPos)
        Assert-Check "$($h.FullName.Substring($repoRoot.Length + 1)) pre-paint script before stylesheet" $ok "scriptPos=$scriptPos, linkPos=$linkPos"
    }

    # A6. Copy Button Fallback & Localization
    Write-Host "`n=== A6. Copy Button Parity across 11 Landing Pages ===" -ForegroundColor Yellow
    foreach ($lp in $landingPages) {
        $c = Get-Content -Raw $lp
        $hasExecCommand = $c -match 'execCommand'
        $hasDoneClass = $c -match '\.classList\.add\(''done''\)'
        Assert-Check "$lp has fallback copy and .done class" ($hasExecCommand -and $hasDoneClass) "Missing fallback or done class"
    }

    # A7. Standalone EN Redirect Stub
    Write-Host "`n=== A7. docs/en/index.html Redirect Stub ===" -ForegroundColor Yellow
    $enStub = "docs/en/index.html"
    $hasEnStub = Test-Path $enStub
    if ($hasEnStub) {
        $stubContent = Get-Content -Raw $enStub
        $setsEn = $stubContent -match "localStorage\.setItem\('sza-lang',\s*'en'\)"
        $redirects = $stubContent -match 'location\.replace\('
        Assert-Check "docs/en/index.html sets sza-lang=en and redirects" ($setsEn -and $redirects) "Stub logic incorrect"
    } else {
        Assert-Check "docs/en/index.html exists" $false "File not found"
    }

    # A8. No Unversioned Releases Href in docs
    Write-Host "`n=== A8. Release Links Point to /latest ===" -ForegroundColor Yellow
    $allDocsHtml = Get-ChildItem -Path "docs" -Filter "*.html" -Recurse
    $badReleases = @()
    foreach ($h in $allDocsHtml) {
        $c = Get-Content -Raw $h.FullName
        $matches = [regex]::Matches($c, 'href="https://github\.com/SerZhyAle/CyrFlip/releases(?![/a-zA-Z0-9_\-\.]*latest)[^"]*"')
        if ($matches.Count -gt 0) {
            $badReleases += $h.FullName
        }
    }
    Assert-Check "No unpinned /releases href in docs" ($badReleases.Count -eq 0) "Found bad links in: $($badReleases -join ', ')"

    # A9. Monochrome Brand Header (No brand-icon)
    Write-Host "`n=== A9. Monochrome Brand Header (No brand-icon) ===" -ForegroundColor Yellow
    $hasBrandIcon = @()
    foreach ($h in $allDocsHtml) {
        if ((Get-Content -Raw $h.FullName) -match 'brand-icon') {
            $hasBrandIcon += $h.FullName
        }
    }
    Assert-Check "No brand-icon class in docs/*.html" ($hasBrandIcon.Count -eq 0) "Found brand-icon in: $($hasBrandIcon -join ', ')"

    # A10. Expand All / Collapse All on docs/index.html
    Write-Host "`n=== A10. Expand / Collapse All Controls in index.html ===" -ForegroundColor Yellow
    $hasExpand = ($indexContent -match 'id="expandAll"') -and ($indexContent -match 'id="collapseAll"')
    $hasDetails00 = $indexContent -match '<span class="secnum">00</span>'
    $hasDetails03 = $indexContent -match '<span class="secnum">03</span>'
    Assert-Check "index.html has expand/collapse controls and 00-03 details" ($hasExpand -and $hasDetails00 -and $hasDetails03) "Missing controls or details 00-03"

    # A11. Landing Copy: Audience, Outcome Cards <= 6, Caveats
    Write-Host "`n=== A11. Landing Copy Outcome Regrouping (<= 6 Cards) & Caveat ===" -ForegroundColor Yellow
    foreach ($lp in $landingPages) {
        $c = Get-Content -Raw $lp
        $cardMatches = [regex]::Matches($c, 'class="card"')
        $cardCount = $cardMatches.Count
        $hasCaveat = ($c -match 'class="whatfor"') -and ($c -match 'trust\.html')
        Assert-Check "$lp has <= 6 cards (found $cardCount) and unsigned caveat" ($cardCount -le 6 -and $hasCaveat) "Cards=$cardCount, Caveat=$hasCaveat"
    }

    # A12. Localized OG/Twitter Metadata in setLang
    Write-Host "`n=== A12. Localized OG/Twitter Metadata in index.html ===" -ForegroundColor Yellow
    $hasMetaMap = ($indexContent -match 'META\s*=\s*\{') -and ($indexContent -match 'og:title') -and ($indexContent -match 'og:description')
    Assert-Check "index.html updates og:title and og:description dynamically" $hasMetaMap "Missing META map or dynamic updater"

    # A13. Touch Targets (44px) & Prefers-Reduced-Motion
    Write-Host "`n=== A13. Touch Targets (44px) & Reduced Motion ===" -ForegroundColor Yellow
    $cssContent = Get-Content -Raw "docs/style.css"
    $hasCoarseMedia = ($cssContent -match 'pointer:\s*coarse') -and ($cssContent -match 'min-height:\s*44px')
    $hasReducedMotion = $indexContent -match 'prefers-reduced-motion'
    Assert-Check "docs/style.css has pointer:coarse 44px rule and index.html respects reduced motion" ($hasCoarseMedia -and $hasReducedMotion) "CSS or JS rule missing"

    # A14. Privacy Pages Language Links (13 Hreflangs & 13-Locale Langbar)
    Write-Host "`n=== A14. 13 Hreflangs and 13-Locale Langbar on All 13 Privacy Pages ===" -ForegroundColor Yellow
    $privacyPages = Get-ChildItem -Path "docs" -Filter "privacy.html" -Recurse
    Assert-Check "Found 13 privacy pages" ($privacyPages.Count -eq 13) "Found $($privacyPages.Count) pages"
    foreach ($pp in $privacyPages) {
        $c = Get-Content -Raw $pp.FullName
        $hreflangCount = ([regex]::Matches($c, '<link rel="alternate" hreflang="')).Count
        $hasLangbar = $c -match '<nav class="langbar"'
        Assert-Check "$($pp.FullName.Substring($repoRoot.Length + 1)) has 13 hreflangs and langbar" ($hreflangCount -eq 13 -and $hasLangbar) "Hreflangs=$hreflangCount, Langbar=$hasLangbar"
    }

    # A15. READMEs: No 'Related project' & Author section
    Write-Host "`n=== A15. READMEs Integrity (No Related project heading) ===" -ForegroundColor Yellow
    $readmes = @("README.md", "README_RU.md", "README_UK.md")
    foreach ($r in $readmes) {
        $c = Get-Content -Raw $r
        $hasRelated = $c -match '(?m)^##\s*(Related project|Связанный проект|Пов''язаний проєкт)'
        $hasAuthorHub = $c -match 'sza\.od\.ua'
        Assert-Check "$r has no Related project heading and references sza.od.ua" (-not $hasRelated -and $hasAuthorHub) "Related heading found or sza.od.ua missing"
    }

    # Summary
    Write-Host "`n========================================================" -ForegroundColor Cyan
    Write-Host "Site Conformance Results: $($script:passes) passed, $($script:failures) failed." -ForegroundColor $(if ($script:failures -eq 0) { 'Green' } else { 'Red' })
    Write-Host "========================================================`n" -ForegroundColor Cyan

    if ($Manual) {
        Write-Host "Manual Verification Checklist:" -ForegroundColor Magenta
        Write-Host "  1. View index.html in browser with sza-lang=uk, de, xx in localStorage."
        Write-Host "  2. Test theme switcher persistence across all 40 pages."
        Write-Host "  3. Verify touch target sizing (44px min) on 360px viewport."
        Write-Host "  4. Test keyboard focus outlines (:focus-visible) on all navigation controls.`n"
    }

    Complete-Check
} finally {
    Pop-Location
}

[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Import-Module (Join-Path $repo 'tools/checks/CheckVerdict.psm1') -Force
Set-CheckSubject 'context-help'
try {
    $map = Get-Content (Join-Path $repo 'help-map.json') -Raw | ConvertFrom-Json
    $root = Join-Path $repo $map.helpRoot
    $localization = Get-Content (Join-Path $repo 'src/CyrFlip/Localization.cs') -Raw
    $localeBlock = [regex]::Match($localization, 'Codes\s*=\s*\{([^}]+)').Groups[1].Value
    $locales = @([regex]::Matches($localeBlock, '"([a-z]{2})"') | ForEach-Object { $_.Groups[1].Value })
    if ($locales.Count -eq 0) { throw 'No UI locales discovered.' }
    $declared = @($map.surfaces | Where-Object type | ForEach-Object type)
    $forms = @()
    foreach ($file in Get-ChildItem (Join-Path $repo 'src/CyrFlip') -Filter '*.cs') {
        foreach ($match in [regex]::Matches([IO.File]::ReadAllText($file.FullName), 'class\s+(\w+)\s*:\s*(?:ThemedForm|Form)\b')) {
            $name = $match.Groups[1].Value
            if ($name -eq 'ThemedForm') { continue }
            if ($name -in @('MonitorWindow','OverlayForm','SettingsSearchDialog')) { $name = $file.BaseName + '+' + $name }
            $forms += 'CyrFlip.' + $name
        }
    }
    foreach ($difference in Compare-Object @($forms | Sort-Object) @($declared | Sort-Object)) {
        Add-CheckFinding -Severity fail -Name 'surface-map' -Reason "Form inventory differs: $($difference.InputObject) $($difference.SideIndicator)"
    }
    $pageSource = Get-Content (Join-Path $repo 'src/CyrFlip/SettingsPageKey.cs') -Raw
    $pageBlock = [regex]::Match($pageSource, 'DisplayOrder\s*=\s*\{([^}]+)').Groups[1].Value
    $pages = @([regex]::Matches($pageBlock, 'SettingsPageKey\.(\w+)') | ForEach-Object { 'settings.' + $_.Groups[1].Value })
    foreach ($difference in Compare-Object @($pages | Sort-Object) @($map.surfaces.id | Where-Object { $_ -like 'settings.*' } | Sort-Object)) {
        Add-CheckFinding -Severity fail -Name 'settings-map' -Reason "Settings inventory differs: $($difference.InputObject)"
    }
    $tourSource = Get-Content (Join-Path $repo 'src/CyrFlip/WelcomeDialog.cs') -Raw
    $steps = [int][regex]::Match($tourSource, 'const int PageCount = (\d+)').Groups[1].Value
    foreach ($step in 0..($steps-1)) {
        if (@($map.surfaces | Where-Object id -eq "welcome.$step").Count -ne 1) { Add-CheckFinding -Severity fail -Name 'tour-map' -Reason "Missing/duplicate welcome.$step" }
    }
    foreach ($duplicate in $map.surfaces | Group-Object id | Where-Object Count -gt 1) { Add-CheckFinding -Severity fail -Name 'duplicate-id' -Reason $duplicate.Name }
    foreach ($surface in $map.surfaces) {
        if ($surface.form -in @('none','inherit') -and -not $surface.reason) { Add-CheckFinding -Severity fail -Name 'missing-reason' -Reason $surface.id }
        if ($surface.form -eq 'inherit' -and $surface.parent -notin $map.surfaces.id) { Add-CheckFinding -Severity fail -Name 'missing-parent' -Reason $surface.id }
        if ($surface.form -notin @('none','inherit','layer','page','both')) { Add-CheckFinding -Severity fail -Name 'unknown-form' -Reason $surface.id }
        if ($surface.form -notin @('page','both')) { continue }
        foreach ($locale in $locales) {
            $path = Join-Path $root "$locale/$($surface.help).html"
            if (-not (Test-Path -LiteralPath $path)) { Add-CheckFinding -Severity fail -Name 'missing-page' -Reason "$locale/$($surface.help)"; continue }
            $html = [IO.File]::ReadAllText($path)
            $direction = if ($locale -in @('ar','ur')) { 'rtl' } else { 'ltr' }
            if ($html -notmatch "<html lang=`"$locale`" dir=`"$direction`"") { Add-CheckFinding -Severity fail -Name 'page-language' -Reason $path }
            if ($html -match '<script\b|\bon\w+\s*=' -or $html -notmatch 'name="referrer" content="no-referrer"') { Add-CheckFinding -Severity fail -Name 'page-privacy' -Reason $path }
            foreach ($link in [regex]::Matches($html, '(href|src)="([^"]+)"')) {
                $attribute = $link.Groups[1].Value
                $target = [System.Net.WebUtility]::HtmlDecode($link.Groups[2].Value)
                if ($target -match '^https?://') {
                    if ($attribute -ne 'href') { Add-CheckFinding -Severity fail -Name 'remote-request' -Reason $path }
                    continue
                }
                if ($target -match '^mailto:') { continue }
                $parts = $target -split '#',2
                $resolved = if ($parts[0]) { [IO.Path]::GetFullPath((Join-Path (Split-Path $path) $parts[0])) } else { $path }
                if (-not (Test-Path -LiteralPath $resolved)) { Add-CheckFinding -Severity fail -Name 'broken-link' -Reason "$path -> $target" }
                elseif ($parts.Count -gt 1 -and [IO.File]::ReadAllText($resolved) -notmatch ('id="' + [regex]::Escape($parts[1]) + '"')) { Add-CheckFinding -Severity fail -Name 'broken-anchor' -Reason "$path -> $target" }
            }
            # EXCHANGE-MARK rules 10 and 11 (S0079). A page about the exchange server opens with the full form: the first image
            # of <main>, the vendored file, 128 CSS px or more, once, with the full name as its alt (rule 14). The page of a
            # surface that merely shows the mark carries the flat drawing inline beside its title, 24 to 32 px, its alt the
            # term of DEVICE-EXCHANGE item CW in the page's language - read off this locale's own page of the server's settings.
            # Every other page shows nothing, the open exchange file's included (a different thing that shares a word).
            $mainAt = $html.IndexOf('<main>')
            $main = if ($mainAt -ge 0) { $html.Substring($mainAt) } else { $html }
            $markImages = @([regex]::Matches($main, '<img\b[^>]*exchange-mark/[^>]*>') | ForEach-Object { $_.Value })
            if ($surface.id -in @('settings.ExchangeServer', 'exchange-account-dialog')) {
                $first = [regex]::Match($main, '<img\b[^>]*>').Value
                $okHead = $markImages.Count -eq 1 -and $first -eq $markImages[0] -and $main.StartsWith('<main><img') `
                    -and $first -match 'src="\.\./exchange-mark/exchange-mark-full\.svg"' `
                    -and $first -match 'width="(\d+)"' -and [int]$Matches[1] -ge 128 `
                    -and $first -match 'alt="Media Exchange Server"'
                if (-not $okHead) { Add-CheckFinding -Severity fail -Name 'exchange-mark-head' -Reason "$path : the full form must be the first and only mark of <main>, 128 px or more, alt = the full name" }
            } elseif ($surface.id -eq 'settings.About') {
                $serverPage = [IO.File]::ReadAllText((Join-Path $root "$locale/settings-exchangeserver.html"))
                $term = [System.Net.WebUtility]::HtmlDecode([regex]::Match($serverPage, '<h1>(.*?)</h1>').Groups[1].Value)
                $inline = if ($markImages.Count -eq 1) { $markImages[0] } else { '' }
                $okInline = $inline -ne '' -and $inline -match 'src="\.\./exchange-mark/exchange-mark-small\.svg"' `
                    -and $inline -match 'width="(\d+)"' -and [int]$Matches[1] -ge 24 -and [int]$Matches[1] -le 32 `
                    -and $inline -match 'alt="([^"]*)"' -and [System.Net.WebUtility]::HtmlDecode($Matches[1]) -eq $term -and $term -ne ''
                if (-not $okInline) { Add-CheckFinding -Severity fail -Name 'exchange-mark-inline' -Reason "$path : one flat mark, 24 to 32 px, alt = '$term'" }
            } elseif ($markImages.Count -gt 0) {
                Add-CheckFinding -Severity fail -Name 'exchange-mark-stray' -Reason "$path : a page that is not about the server and does not show the mark carries it"
            }
            if ($surface.id -ne 'index') {
                foreach ($section in @('purpose','order','elements','related')) { if ($html -notmatch "id=`"$section`"") { Add-CheckFinding -Severity fail -Name 'page-anatomy' -Reason "$path : $section" } }
                foreach ($theme in @('light','dark')) { if (-not (Test-Path (Join-Path $root "$locale/$($surface.help)-$theme.png"))) { Add-CheckFinding -Severity fail -Name 'missing-capture' -Reason "$locale/$($surface.help)/$theme" } }
            }
        }
    }
    foreach ($css in Get-ChildItem $root -Filter '*.css') {
        $text = [regex]::Replace([IO.File]::ReadAllText($css.FullName), '/\*[\s\S]*?\*/', '')
        if ($text -match '@import|url\(\s*["'']?https?://') { Add-CheckFinding -Severity fail -Name 'remote-css' -Reason $css.Name }
    }
} catch {
    Add-CheckFinding -Severity notverified -Name 'help-input' -Reason $_.Exception.Message
}
Complete-Check

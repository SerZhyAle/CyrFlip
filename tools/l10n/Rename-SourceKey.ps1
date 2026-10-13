<#
.SYNOPSIS
    Renames one English source key of the localization table, at its entry and at every use (ticket S0070 D5).

.DESCRIPTION
    The key of Localization is the English text, so rewording a caption renames its key. Doing that by hand
    misses a use and leaves the old text untranslated in twelve languages; this does it by value, over
    src/CyrFlip and tests/CyrFlip.Tests, and refuses when the old key is not exactly one table entry.

    It changes the key and every literal equal to it - nothing else: the twelve translations stay as they
    are, so it is for a rename (a typo, a clearer name), not for a wording change that needs the other
    languages re-read. A key with a context in parentheses keeps that form; rename both texts of the pair
    in two calls.

    Afterwards build and run the localization gates (LocalizationSourceGateTests,
    SettingsLocalizationTests) - they fail for a use this did not reach.

.PARAMETER Old
    The key as it stands, exactly (case, punctuation and placeholders included).

.PARAMETER New
    The new English text. It must not hold Cyrillic.

.EXAMPLE
    .\tools\l10n\Rename-SourceKey.ps1 -Old 'Save selection to quick notes' -New 'Save the selection as a quick note'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Old,
    [Parameter(Mandatory)] [string] $New
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = Join-Path $PSScriptRoot 'L10nKeys.csproj'
$exe = Join-Path $PSScriptRoot 'bin\Release\net48\L10nKeys.exe'

if (-not (Test-Path $exe) -or (Get-ChildItem $PSScriptRoot -Filter '*.cs' | Where-Object LastWriteTime -gt (Get-Item $exe).LastWriteTime)) {
    & dotnet build $project -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'the localization-key tool did not build' }
}

Push-Location $RepoRoot
try {
    & $exe rename $Old $New --root $RepoRoot
    exit $LASTEXITCODE
}
finally { Pop-Location }

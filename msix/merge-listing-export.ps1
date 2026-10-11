<#
.SYNOPSIS
Overlays the product's listing copy onto a FRESH Partner Center export, so an import can start from the
submission's own asset URLs (WINDOWS-STORE rule 6) and be bisected by language or field.

.DESCRIPTION
The base is the export Partner Center just produced for the draft submission (Field, ID, Type and every
asset row are kept untouched). The copy comes from the product's generated import file
(msix/store-listing-import-13-languages.csv): only rows of type Text, only the languages and fields asked
for, and a cell the product leaves empty never blanks a cell the draft holds.

The result is written in Partner Center's own dialect - a BOM, minimal quoting, CRLF, no trailing newline -
which is what the export itself looks like, so an unmodified export round-trips byte for byte.

Bisecting a refused import (the refusal names no language and no cell):
  1. -Languages <half of them>            -> import, export the draft again, compare bytes with the file;
  2. repeat on the other half / narrow with -Fields (Feature3..20, SearchTerm1..7, ...);
  3. byte-identical re-export == every cell of that file landed.
Always build the next file from the LAST ACCEPTED state (-Export takes the re-export), not from the first one.

.EXAMPLE
.\msix\merge-listing-export.ps1 -Export $env:USERPROFILE\Downloads\listingData-<id>-<submission>.csv -Out merged.csv
.\msix\merge-listing-export.ps1 -Export fresh.csv -Out step1.csv -Languages it,de -Fields Description,ReleaseNotes
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]   $Export,
    [Parameter(Mandatory)] [string]   $Out,
    [string]   $Import = (Join-Path $PSScriptRoot 'store-listing-import-13-languages.csv'),
    [string[]] $Languages,
    [string[]] $Fields
)
$ErrorActionPreference = 'Stop'
foreach ($path in $Export, $Import) { if (-not (Test-Path $path)) { Write-Host "Not found: $path" -ForegroundColor Red; exit 2 } }

function Read-Table([string] $path) {
    [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8).TrimStart([char] 0xFEFF) | ConvertFrom-Csv
}
$base    = @(Read-Table $Export)
$overlay = @(Read-Table $Import)
if ($base.Count -ne $overlay.Count) { Write-Host "Row counts differ: export $($base.Count), import $($overlay.Count)" -ForegroundColor Red; exit 2 }

$columns = @($base[0].PSObject.Properties.Name | Where-Object { $_ -notin 'Field', 'ID', 'Type (Type)' })
if ($Languages) {
    $unknown = @($Languages | Where-Object { $_ -notin $columns })
    if ($unknown.Count -gt 0) { Write-Host "No such language column in the export: $($unknown -join ', ')" -ForegroundColor Red; exit 2 }
    $columns = @($columns | Where-Object { $_ -in $Languages })
}

$replaced = 0
for ($i = 0; $i -lt $base.Count; $i++) {
    $b = $base[$i]; $o = $overlay[$i]
    if ($b.ID -ne $o.ID -or $b.Field -ne $o.Field) { Write-Host "Row $i differs in identity: $($b.Field)/$($b.ID) vs $($o.Field)/$($o.ID)" -ForegroundColor Red; exit 2 }
    if ($b.'Type (Type)' -ne 'Text') { continue }
    if ($Fields -and $b.Field -notin $Fields) { continue }
    foreach ($c in $columns) {
        $new = [string] $o.$c
        if ($new -eq '' -or $new -eq [string] $b.$c) { continue }
        $b.$c = $new
        $replaced++
    }
}

$temporary = "$Out.tmp"
$base | Export-Csv -Path $temporary -NoTypeInformation -UseQuotes AsNeeded -Encoding utf8BOM
$bytes = [IO.File]::ReadAllBytes($temporary)
$length = $bytes.Length
if ($length -ge 2 -and $bytes[$length - 2] -eq 13 -and $bytes[$length - 1] -eq 10) { $length -= 2 }   # no trailing newline, like the export
[IO.File]::WriteAllBytes($Out, $bytes[0..($length - 1)])
Remove-Item $temporary
"{0}: {1} copy cells replaced (languages: {2}; fields: {3}); {4} bytes" -f $Out, $replaced,
    $(if ($Languages) { $Languages -join ',' } else { 'all' }), $(if ($Fields) { $Fields -join ',' } else { 'all' }), $length

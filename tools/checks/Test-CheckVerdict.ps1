<#
    Self-test / exit-code vector for CheckVerdict.psm1 (ladder rung 1).

    Usage:
      pwsh -File tools/checks/Test-CheckVerdict.ps1 -Outcome pass|fail|notverified|advisory
#>
[CmdletBinding()]
param(
    [ValidateSet('pass', 'fail', 'notverified', 'advisory', 'notapplicable')]
    [string]$Outcome = 'pass',
    [Nullable[int]]$RunCount,
    [int]$SkippedCount = 0
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CheckVerdict.psm1') -Force

Set-CheckSubject 'Test-CheckVerdict'

switch ($Outcome) {
    'pass' {
        # No findings added
    }
    'fail' {
        Add-CheckFinding -Severity fail -Name 'SyntheticDefect' -Reason 'Simulated defect finding'
    }
    'notverified' {
        Add-CheckFinding -Severity notverified -Name 'SyntheticUnverified' -Reason 'Simulated unverified condition'
    }
    'advisory' {
        Add-CheckFinding -Severity advisory -Name 'SyntheticAdvisory' -Reason 'Simulated advisory finding'
    }
}

$arguments = @{}
if ($null -ne $RunCount) { $arguments.RunCount = $RunCount; $arguments.SkippedCount = $SkippedCount }
if ($Outcome -eq 'notapplicable') { $arguments.NotApplicableReason = 'caller deselected synthetic check' }
Complete-Check @arguments

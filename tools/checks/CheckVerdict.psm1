<#
    CheckVerdict - standardized exit codes and verdict line helper (CHECK-VERDICT 0.13).

    Exit codes:
      0 = PASS
      1 = DEFECT / FAIL
      2 = COULD-NOT-VERIFY / NOT VERIFIED
      3 = ADVISORY / PASS WITH ADVISORIES

    Verdict line grammar (last stdout line):
      <subject>: PASS
      <subject>: PASS WITH ADVISORIES (<n>)
      <subject>: FAIL (<n>)
      <subject>: COULD NOT VERIFY (<n>)
      <subject>: NOT APPLICABLE (<reason>)
      Test-count suffix when present: run=N skipped=M
#>

$script:Subject = 'check'
$script:Findings = [System.Collections.Generic.List[hashtable]]::new()

function Set-CheckSubject {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Subject)
    $script:Subject = $Subject
}

function Write-CheckSubject {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Subject)
    $script:Subject = $Subject
    Write-Host "Subject: $Subject" -ForegroundColor Cyan
}

function Add-CheckFinding {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('fail', 'defect', 'notverified', 'not-verified', 'advisory', 'warning')]
        [string]$Severity,
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$Reason
    )

    $normalizedSeverity = switch ($Severity.ToLowerInvariant()) {
        'defect'        { 'fail' }
        'not-verified'  { 'notverified' }
        'warning'       { 'advisory' }
        default         { $Severity.ToLowerInvariant() }
    }

    $script:Findings.Add(@{
        Severity = $normalizedSeverity
        Name     = $Name
        Reason   = $Reason
    })
}

function Assert-Tool {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [string]$Hint = ''
    )
    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    if (-not $cmd) {
        $reason = if ($Hint) { "tool '$Name' not found. $Hint" } else { "tool '$Name' not found." }
        Add-CheckFinding -Severity notverified -Name $Name -Reason $reason
        return $false
    }
    return $true
}

function Complete-Check {
    [CmdletBinding()]
    param(
        [string]$Subject,
        [switch]$NoExit,
        [ValidateRange(0, [int]::MaxValue)][int]$RunCount,
        [ValidateRange(0, [int]::MaxValue)][int]$SkippedCount = 0,
        [string]$NotApplicableReason
    )

    if ($Subject) { $script:Subject = $Subject }

    $hasCounts = $PSBoundParameters.ContainsKey('RunCount')
    if ($hasCounts -and -not $NotApplicableReason) {
        if ($RunCount -eq 0) { Add-CheckFinding -Severity notverified -Name 'empty-test-run' -Reason 'No tests executed.' }
        if ($SkippedCount -gt 0) { Add-CheckFinding -Severity notverified -Name 'skipped-tests' -Reason "$SkippedCount requested test(s) did not execute." }
    }
    $countSuffix = if ($hasCounts) { " (run=$RunCount skipped=$SkippedCount)" } else { '' }

    $fails       = @($script:Findings | Where-Object { $_.Severity -eq 'fail' })
    $notVerified = @($script:Findings | Where-Object { $_.Severity -eq 'notverified' })
    $advisories  = @($script:Findings | Where-Object { $_.Severity -eq 'advisory' })

    # Print reasons to stdout (never through Write-Error per CHECK-VERDICT rule 3)
    if ($fails.Count -gt 0) {
        Write-Host "FAILURES ($($fails.Count)):" -ForegroundColor Red
        foreach ($f in $fails) {
            Write-Host "  - [$($f.Name)] $($f.Reason)" -ForegroundColor Red
        }
    }
    if ($notVerified.Count -gt 0) {
        Write-Host "COULD NOT VERIFY ($($notVerified.Count)):" -ForegroundColor Yellow
        foreach ($nv in $notVerified) {
            Write-Host "  - [$($nv.Name)] $($nv.Reason)" -ForegroundColor Yellow
        }
    }
    if ($advisories.Count -gt 0) {
        Write-Host "ADVISORIES ($($advisories.Count)):" -ForegroundColor DarkYellow
        foreach ($adv in $advisories) {
            Write-Host "  - [$($adv.Name)] $($adv.Reason)" -ForegroundColor DarkYellow
        }
    }

    $code = 0
    $verdictLine = ""

    if ($fails.Count -gt 0) {
        $code = 1
        $verdictLine = "$($script:Subject): FAIL ($($fails.Count))$countSuffix"
        Write-Host $verdictLine -ForegroundColor Red
    }
    elseif ($notVerified.Count -gt 0) {
        $code = 2
        $verdictLine = "$($script:Subject): COULD NOT VERIFY ($($notVerified.Count))$countSuffix"
        Write-Host $verdictLine -ForegroundColor Yellow
    }
    elseif ($advisories.Count -gt 0) {
        $code = 3
        $verdictLine = "$($script:Subject): PASS WITH ADVISORIES ($($advisories.Count))$countSuffix"
        Write-Host $verdictLine -ForegroundColor Yellow
    }
    elseif ($NotApplicableReason) {
        $verdictLine = "$($script:Subject): NOT APPLICABLE ($NotApplicableReason)$countSuffix"
        Write-Host $verdictLine -ForegroundColor Cyan
    }
    else {
        $code = 0
        $verdictLine = "$($script:Subject): PASS$countSuffix"
        Write-Host $verdictLine -ForegroundColor Green
    }

    if (-not $NoExit) {
        $global:LASTEXITCODE = $code
        exit $code
    }

    return [pscustomobject]@{
        ExitCode    = $code
        VerdictLine = $verdictLine
        Fails       = $fails
        NotVerified = $notVerified
        Advisories  = $advisories
    }
}

Export-ModuleMember -Function Set-CheckSubject, Write-CheckSubject, Add-CheckFinding, Assert-Tool, Complete-Check

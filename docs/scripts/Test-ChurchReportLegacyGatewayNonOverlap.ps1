<#
.SYNOPSIS
Offline, no-secret validator for the ChurchReport legacy Gateway cutover boundary.

.DESCRIPTION
The validator accepts only six server-selected evidence switches. It performs
no network, upstream, database, feature-flag, or filesystem mutation. Missing evidence is
reported as one fixed de-identified classification and returns exit code 2.
#>
[CmdletBinding()]
param(
    [switch] $Durable,
    [switch] $CanonicalBinding,
    [switch] $LegacyDrained,
    [switch] $LegacyCoverageProven,
    [switch] $GatewayReady,
    [switch] $RollbackOwner,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $Unexpected
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

function Write-FixedResult {
    param(
        [string] $Outcome,
        [string] $Classification,
        [int] $ExitCode
    )

    $result = [ordered]@{
        schemaVersion = 1
        outcome = $Outcome
        classification = $Classification
    }

    '{0}: {1}' -f $Outcome.ToUpperInvariant(), $Classification

    exit $ExitCode
}

try {
    if ($null -ne $Unexpected -and $Unexpected.Count -gt 0) {
        Write-FixedResult -Outcome 'no-go' -Classification 'invalid-input' -ExitCode 2
    }

    # Precedence is deterministic: topology, drain, coverage, binding, readiness, owner.
    if (-not $Durable) {
        Write-FixedResult -Outcome 'no-go' -Classification 'non-durable-topology' -ExitCode 2
    }
    if (-not $LegacyDrained) {
        Write-FixedResult -Outcome 'no-go' -Classification 'sync-overrun' -ExitCode 2
    }
    if (-not $LegacyCoverageProven) {
        Write-FixedResult -Outcome 'no-go' -Classification 'legacy-coverage-unproven' -ExitCode 2
    }
    if (-not $CanonicalBinding) {
        Write-FixedResult -Outcome 'no-go' -Classification 'canonical-binding-unproven' -ExitCode 2
    }
    if (-not $GatewayReady) {
        Write-FixedResult -Outcome 'no-go' -Classification 'gateway-not-ready' -ExitCode 2
    }
    if (-not $RollbackOwner) {
        Write-FixedResult -Outcome 'no-go' -Classification 'rollback-owner-unassigned' -ExitCode 2
    }

    Write-FixedResult -Outcome 'go' -Classification 'all-required-evidence-proven' -ExitCode 0
}
catch {
    Write-FixedResult -Outcome 'no-go' -Classification 'invalid-input' -ExitCode 2
}

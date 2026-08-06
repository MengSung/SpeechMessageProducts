<#
.SYNOPSIS
Verifies that the local Official Worker deployment-readiness probe remains
fail-closed and never serializes sensitive deployment inputs.

.DESCRIPTION
Creates a process-owned temporary manifest and profile-input fixture. The
fixture deliberately contains endpoint and credential-reference markers which
must never appear in probe output. The asserted no-go case uses unresolvable
generic Credential Manager targets, so this test never reads a credential
secret, starts a Worker, creates an overlay, or sends a network request.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

$scriptPath = Join-Path $PSScriptRoot 'Test-DynamicsOfficialWorkerDeploymentReadiness.ps1'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'speechmessage-dynamics-worker-readiness-' +
    [Guid]::NewGuid().ToString('N'))

function Assert-True {
    param(
        [bool] $Condition,
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function ConvertTo-CrlfText {
    param([string] $Value)

    return ($Value -replace '(?<!\r)\n', "`r`n").TrimEnd("`r", "`n") + "`r`n"
}

function Write-StrictUtf8Text {
    param(
        [string] $Path,
        [string] $Value
    )

    [IO.File]::WriteAllText(
        $Path,
        (ConvertTo-CrlfText -Value $Value),
        [Text.UTF8Encoding]::new($false))
}

function Write-StrictJson {
    param(
        [string] $Path,
        [object] $Value
    )

    Write-StrictUtf8Text -Path $Path -Value ($Value | ConvertTo-Json -Depth 12)
}

function Assert-StrictTextFile {
    param([string] $Path)

    $bytes = $null
    try {
        $bytes = [IO.File]::ReadAllBytes($Path)
        Assert-True -Condition ($bytes.Length -gt 0) `
            -Message 'A checked text file is empty.'
        Assert-True -Condition (-not (
            $bytes.Length -ge 3 -and
            $bytes[0] -eq 0xEF -and
            $bytes[1] -eq 0xBB -and
            $bytes[2] -eq 0xBF
        )) -Message 'A checked text file contains a UTF-8 BOM.'
        $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        Assert-True -Condition (-not [Regex]::IsMatch($text, '(?<!\r)\n')) `
            -Message 'A checked text file contains an LF-only line ending.'
        Assert-True -Condition ($text.EndsWith("`r`n", [StringComparison]::Ordinal)) `
            -Message 'A checked text file lacks a final CRLF.'
    }
    finally {
        if ($null -ne $bytes) {
            [Array]::Clear($bytes, 0, $bytes.Length)
        }
    }
}

function New-ReadinessFixture {
    $publishedRoot = Join-Path $fixtureRoot 'published'
    $crm82Directory = Join-Path $publishedRoot 'crm82'
    $crm91Directory = Join-Path $publishedRoot 'crm91'
    foreach ($directory in @($crm82Directory, $crm91Directory)) {
        [void](New-Item -ItemType Directory -Path $directory -Force)
    }

    $crm82Executable = Join-Path $crm82Directory 'SpeechMessage.Dynamics.Crm82Worker.exe'
    $crm91Executable = Join-Path $crm91Directory 'SpeechMessage.Dynamics.Crm91Worker.exe'
    [IO.File]::WriteAllBytes(
        $crm82Executable,
        [Text.Encoding]::ASCII.GetBytes('test-owned-crm82-worker'))
    [IO.File]::WriteAllBytes(
        $crm91Executable,
        [Text.Encoding]::ASCII.GetBytes('test-owned-crm91-worker'))

    $crm82Hash = (Get-FileHash -LiteralPath $crm82Executable -Algorithm SHA256).Hash
    $crm91Hash = (Get-FileHash -LiteralPath $crm91Executable -Algorithm SHA256).Hash
    $crm82Lock = 'crm82-xrmtooling-8.2.0.5-core-8.2.0.2'
    $crm91Lock = 'crm91-xrmtooling-9.1.1.65-core-9.0.2.60'
    $manifestPath = Join-Path $publishedRoot 'official-worker-manifest.json'
    Write-StrictJson -Path $manifestPath -Value ([ordered]@{
        schemaVersion = 1
        featureGateMustRemainDisabled = $true
        workers = @(
            [ordered]@{
                workerKind = 'OfficialCrm82Worker'
                ceVersion = '8.2'
                packageLockId = $crm82Lock
                relativeExecutablePath = 'crm82/SpeechMessage.Dynamics.Crm82Worker.exe'
                sha256 = $crm82Hash
            },
            [ordered]@{
                workerKind = 'OfficialCrm91Worker'
                ceVersion = '9.1'
                packageLockId = $crm91Lock
                relativeExecutablePath = 'crm91/SpeechMessage.Dynamics.Crm91Worker.exe'
                sha256 = $crm91Hash
            }
        )
    })

    $endpointMarker = 'https://endpoint-must-not-appear.fixture.invalid/'
    $credentialMarker = 'credential-reference-must-not-appear-fixture'
    $profileInputPath = Join-Path $fixtureRoot 'approved-profile-input.json'
    Write-StrictJson -Path $profileInputPath -Value ([ordered]@{
        profiles = @(
            [ordered]@{
                profileAlias = 'crm82'
                workerKind = 'OfficialCrm82Worker'
                packageLockId = $crm82Lock
                profileGenerationId = 'crm82-test-generation-0001'
                organizationBaseUri = $endpointMarker
                organizationName = 'jesus'
                expectedOrganizationId = '4d701c24-2102-eb11-80da-00155d006913'
                authentication = 'ActiveDirectory'
                identity = [ordered]@{
                    mode = 'WindowsCredentialReference'
                    reference = $credentialMarker
                }
            },
            [ordered]@{
                profileAlias = 'crm91'
                workerKind = 'OfficialCrm91Worker'
                packageLockId = $crm91Lock
                profileGenerationId = 'crm91-test-generation-0001'
                organizationBaseUri = 'https://ce91-endpoint-must-not-appear.fixture.invalid/'
                organizationName = 'sunnyvalechback'
                expectedOrganizationId = 'bfb92ead-3705-f011-8143-00155d006608'
                authentication = 'Ifd'
                identity = [ordered]@{
                    mode = 'WindowsCredentialReference'
                    reference = 'credential-reference-ce91-must-not-appear-fixture'
                    homeRealm = 'https://home-realm-must-not-appear.fixture.invalid/'
                }
            }
        )
    })

    return [pscustomobject]@{
        PublishedRoot = $publishedRoot
        ManifestPath = $manifestPath
        ProfileInputPath = $profileInputPath
        EndpointMarker = $endpointMarker
        CredentialMarker = $credentialMarker
        CurrentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
        Crm82Directory = $crm82Directory
        Crm91Directory = $crm91Directory
    }
}

function Invoke-ReadinessProbe {
    param([object] $Fixture)

    $arguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', $scriptPath,
        '-ManifestPath', $Fixture.ManifestPath,
        '-ProfileInputPath', $Fixture.ProfileInputPath,
        '-ExpectedExecutionIdentity', $Fixture.CurrentIdentity,
        '-Json'
    )
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& powershell.exe @arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Text = $output -join [Environment]::NewLine
    }
}

function Invoke-ReadinessInventory {
    param([object] $Fixture)

    $arguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', $scriptPath,
        '-ManifestPath', $Fixture.ManifestPath,
        '-ExpectedExecutionIdentity', $Fixture.CurrentIdentity,
        '-InventoryOnly',
        '-Json'
    )
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& powershell.exe @arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Text = $output -join [Environment]::NewLine
    }
}

try {
    Assert-True -Condition (Test-Path -LiteralPath $scriptPath -PathType Leaf) `
        -Message 'The deployment readiness probe is missing.'
    Assert-StrictTextFile -Path $PSCommandPath
    Assert-StrictTextFile -Path $scriptPath

    $fixture = New-ReadinessFixture
    $beforeCrm82Files = @(Get-ChildItem -LiteralPath $fixture.Crm82Directory -File).Count
    $beforeCrm91Files = @(Get-ChildItem -LiteralPath $fixture.Crm91Directory -File).Count
    $result = Invoke-ReadinessProbe -Fixture $fixture

    Assert-True -Condition ($result.ExitCode -eq 2) `
        -Message 'An unresolved credential reference must return a no-go exit code.'
    $evidence = $result.Text | ConvertFrom-Json -ErrorAction Stop
    Assert-True -Condition ($evidence.outcome -eq 'no-go') `
        -Message 'An unresolved credential reference must report no-go.'
    Assert-True -Condition (@($evidence.profiles).Count -eq 2) `
        -Message 'Both CE profiles must appear in sanitized readiness evidence.'
    Assert-True -Condition (@($evidence.profiles | Where-Object {
        $_.reasons -contains 'credential-reference-unresolvable'
    }).Count -eq 2) `
        -Message 'Each unresolved credential reference requires a sanitized reason.'
    foreach ($sensitiveMarker in @(
        $fixture.EndpointMarker,
        $fixture.CredentialMarker,
        $fixture.CurrentIdentity,
        'home-realm-must-not-appear.fixture.invalid'
    )) {
        Assert-True -Condition (-not $result.Text.Contains($sensitiveMarker)) `
            -Message 'Readiness evidence exposed a sensitive fixture marker.'
    }
    foreach ($forbiddenProperty in @(
        'organizationBaseUri',
        'expectedOrganizationId',
        'credentialReference',
        'homeRealm',
        'password',
        'token',
        'cookie',
        'executionIdentity'
    )) {
        Assert-True -Condition ($evidence.PSObject.Properties.Name -notcontains $forbiddenProperty) `
            -Message 'Readiness evidence contains a forbidden root property.'
    }
    Assert-True -Condition (@(Get-ChildItem -LiteralPath $fixture.Crm82Directory -File).Count -eq $beforeCrm82Files) `
        -Message 'The readiness probe created a file beside the CE 8.2 Worker.'
    Assert-True -Condition (@(Get-ChildItem -LiteralPath $fixture.Crm91Directory -File).Count -eq $beforeCrm91Files) `
        -Message 'The readiness probe created a file beside the CE 9.1 Worker.'

    $inventoryResult = Invoke-ReadinessInventory -Fixture $fixture
    Assert-True -Condition ($inventoryResult.ExitCode -eq 2) `
        -Message 'Inventory-only mode must remain no-go until approved profile input exists.'
    $inventoryEvidence = $inventoryResult.Text | ConvertFrom-Json -ErrorAction Stop
    Assert-True -Condition ($inventoryEvidence.outcome -eq 'no-go') `
        -Message 'Inventory-only mode must report no-go without profile input.'
    Assert-True -Condition (@($inventoryEvidence.profiles | Where-Object {
        $_.reasons -contains 'profile-input-required'
    }).Count -eq 2) `
        -Message 'Inventory-only mode must report the missing profile-input requirement.'
    Assert-True -Condition (@($inventoryEvidence.profiles | Where-Object {
        @($_.reasons).Count -ne 1
    }).Count -eq 0) `
        -Message 'Inventory-only mode must not report unrelated manifest or credential failures.'
    foreach ($sensitiveMarker in @($fixture.EndpointMarker, $fixture.CredentialMarker)) {
        Assert-True -Condition (-not $inventoryResult.Text.Contains($sensitiveMarker)) `
            -Message 'Inventory-only evidence exposed a sensitive fixture marker.'
    }

    'All official Worker deployment readiness probe tests passed.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        $resolvedFixtureRoot = [IO.Path]::GetFullPath($fixtureRoot)
        $resolvedTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (-not $resolvedFixtureRoot.StartsWith(
                $resolvedTempRoot,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to remove a readiness fixture outside the temporary directory.'
        }

        Remove-Item -LiteralPath $resolvedFixtureRoot -Recurse -Force
    }
}

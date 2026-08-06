<#
.SYNOPSIS
Verifies the P6.2 local Official Worker profile-input generator.

.DESCRIPTION
Creates only process-owned temporary manifests and redirects LOCALAPPDATA into
the temporary fixture. The test proves that the generator derives Worker kind
and package-lock values from the manifest, serializes the versioned profile
contract with strict UTF-8/CRLF text, rejects unsafe input and unknown secret
parameters, and never overwrites an existing local profile input. No Credential
Manager secret, Worker process, Gateway overlay, D365 request, or repository
artifact is read or created by this test.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

$scriptPath = Join-Path $PSScriptRoot 'New-DynamicsOfficialWorkerProfileInput.ps1'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'speechmessage-dynamics-profile-input-' + [Guid]::NewGuid().ToString('N'))

function Assert-True {
    param(
        [bool] $Condition,
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-ExactProperties {
    param(
        [object] $Object,
        [string[]] $Expected,
        [string] $Message
    )

    $actual = @($Object.PSObject.Properties | ForEach-Object { $_.Name })
    if ($actual.Count -ne $Expected.Count -or
        @($actual | Where-Object { $_ -cnotin $Expected }).Count -ne 0 -or
        @($Expected | Where-Object { $_ -cnotin $actual }).Count -ne 0) {
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

function New-ProfileInputFixture {
    param([string] $Name)

    $root = Join-Path $fixtureRoot $Name
    $publishedRoot = Join-Path $root 'published'
    $localAppData = Join-Path $root 'local-app-data'
    [void](New-Item -ItemType Directory -Path $publishedRoot -Force)

    $manifest = [ordered]@{
        schemaVersion = 1
        featureGateMustRemainDisabled = $true
        workers = @(
            [ordered]@{
                workerKind = 'OfficialCrm82Worker'
                ceVersion = '8.2'
                packageLockId = 'crm82-xrmtooling-8.2.0.5-core-8.2.0.2'
            },
            [ordered]@{
                workerKind = 'OfficialCrm91Worker'
                ceVersion = '9.1'
                packageLockId = 'crm91-xrmtooling-9.1.1.65-core-9.0.2.60'
            }
        )
    }
    $manifestPath = Join-Path $publishedRoot 'official-worker-manifest.json'
    Write-StrictJson -Path $manifestPath -Value $manifest

    return [pscustomobject]@{
        Manifest = $manifest
        ManifestPath = $manifestPath
        LocalAppData = $localAppData
        OutputPath = Join-Path $localAppData 'SpeechMessage\Dynamics\P6.2\official-worker-profile-input.json'
        Crm82OrganizationBaseUri = 'https://crm82-profile-input.fixture.invalid/'
        Crm82OrganizationName = 'crm82fixture'
        Crm82ExpectedOrganizationId = '4d701c24-2102-eb11-80da-00155d006913'
        Crm82HomeRealm = 'https://crm82-home-realm.fixture.invalid/'
        Crm82CredentialTarget = 'speechmessage.crm82.fixture'
        Crm82ProfileGenerationId = 'crm82-p6-2-fixture-0001'
        Crm91OrganizationBaseUri = 'https://crm91-profile-input.fixture.invalid/'
        Crm91OrganizationName = 'crm91fixture'
        Crm91ExpectedOrganizationId = 'bfb92ead-3705-f011-8143-00155d006608'
        Crm91HomeRealm = 'https://crm91-home-realm.fixture.invalid/'
        Crm91CredentialTarget = 'speechmessage.crm91.fixture'
        Crm91ProfileGenerationId = 'crm91-p6-2-fixture-0001'
    }
}

function Invoke-ProfileInputGenerator {
    param(
        [object] $Fixture,
        [hashtable] $Overrides = @{},
        [string[]] $AdditionalArguments = @()
    )

    $values = [ordered]@{
        Crm82OrganizationBaseUri = $Fixture.Crm82OrganizationBaseUri
        Crm82OrganizationName = $Fixture.Crm82OrganizationName
        Crm82ExpectedOrganizationId = $Fixture.Crm82ExpectedOrganizationId
        Crm82HomeRealm = $Fixture.Crm82HomeRealm
        Crm82CredentialTarget = $Fixture.Crm82CredentialTarget
        Crm82ProfileGenerationId = $Fixture.Crm82ProfileGenerationId
        Crm91OrganizationBaseUri = $Fixture.Crm91OrganizationBaseUri
        Crm91OrganizationName = $Fixture.Crm91OrganizationName
        Crm91ExpectedOrganizationId = $Fixture.Crm91ExpectedOrganizationId
        Crm91HomeRealm = $Fixture.Crm91HomeRealm
        Crm91CredentialTarget = $Fixture.Crm91CredentialTarget
        Crm91ProfileGenerationId = $Fixture.Crm91ProfileGenerationId
    }
    foreach ($name in $Overrides.Keys) {
        $values[$name] = $Overrides[$name]
    }

    $arguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', $scriptPath,
        '-ManifestPath', $Fixture.ManifestPath
    )
    foreach ($name in $values.Keys) {
        $arguments += "-$name"
        $arguments += [string]$values[$name]
    }
    $arguments += $AdditionalArguments
    $arguments += '-Json'

    $previousLocalAppData = $env:LOCALAPPDATA
    try {
        $env:LOCALAPPDATA = $Fixture.LocalAppData
        $previousErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            $output = @(& powershell.exe @arguments 2>&1)
            $exitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }
    }
    finally {
        if ($null -eq $previousLocalAppData) {
            Remove-Item Env:LOCALAPPDATA -ErrorAction SilentlyContinue
        }
        else {
            $env:LOCALAPPDATA = $previousLocalAppData
        }
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Text = $output -join [Environment]::NewLine
    }
}

function Assert-NoProfileInputOutput {
    param([object] $Fixture)

    Assert-True -Condition (-not (Test-Path -LiteralPath $Fixture.OutputPath -PathType Leaf)) `
        -Message 'Invalid profile input parameters created the local profile file.'
}

try {
    Assert-True -Condition (Test-Path -LiteralPath $scriptPath -PathType Leaf) `
        -Message 'The local Official Worker profile-input generator is missing.'
    Assert-StrictTextFile -Path $PSCommandPath
    Assert-StrictTextFile -Path $scriptPath

    $fixture = New-ProfileInputFixture -Name 'valid'
    $result = Invoke-ProfileInputGenerator -Fixture $fixture
    Assert-True -Condition ($result.ExitCode -eq 0) `
        -Message 'Valid non-secret profile metadata must generate a local profile input.'
    $resultDocument = $result.Text | ConvertFrom-Json -ErrorAction Stop
    Assert-True -Condition ($resultDocument.outcome -eq 'written' -and $resultDocument.profileCount -eq 2) `
        -Message 'Generator output must contain only a sanitized written outcome.'
    foreach ($marker in @(
        $fixture.Crm82OrganizationBaseUri,
        $fixture.Crm82ExpectedOrganizationId,
        $fixture.Crm82HomeRealm,
        $fixture.Crm82CredentialTarget,
        $fixture.Crm91OrganizationBaseUri,
        $fixture.Crm91ExpectedOrganizationId,
        $fixture.Crm91HomeRealm,
        $fixture.Crm91CredentialTarget
    )) {
        Assert-True -Condition (-not $result.Text.Contains($marker)) `
            -Message 'Generator console output exposed profile metadata.'
    }
    Assert-True -Condition (Test-Path -LiteralPath $fixture.OutputPath -PathType Leaf) `
        -Message 'Generator did not create the fixed LOCALAPPDATA profile-input path.'
    Assert-StrictTextFile -Path $fixture.OutputPath
    $profileDocument = [Text.UTF8Encoding]::new($false, $true).GetString(
        [IO.File]::ReadAllBytes($fixture.OutputPath)) | ConvertFrom-Json -ErrorAction Stop
    Assert-ExactProperties -Object $profileDocument -Expected @('schemaVersion', 'profiles') `
        -Message 'Profile input must use the versioned top-level schema.'
    Assert-True -Condition ($profileDocument.schemaVersion -eq 1 -and @($profileDocument.profiles).Count -eq 2) `
        -Message 'Profile input must contain schema version 1 and both fixed profiles.'
    $crm82 = @($profileDocument.profiles | Where-Object { $_.profileAlias -ceq 'crm82' })
    $crm91 = @($profileDocument.profiles | Where-Object { $_.profileAlias -ceq 'crm91' })
    Assert-True -Condition ($crm82.Count -eq 1 -and $crm91.Count -eq 1) `
        -Message 'Profile input must contain exactly crm82 and crm91 profiles.'
    Assert-True -Condition (
        $crm82[0].workerKind -ceq 'OfficialCrm82Worker' -and
        $crm82[0].packageLockId -ceq $fixture.Manifest.workers[0].packageLockId -and
        $crm82[0].authentication -ceq 'Ifd' -and
        $crm82[0].identity.mode -ceq 'WindowsCredentialReference' -and
        $crm82[0].identity.reference -ceq $fixture.Crm82CredentialTarget -and
        $crm82[0].identity.homeRealm -ceq $fixture.Crm82HomeRealm
    ) -Message 'CE 8.2 profile must be manifest-derived and IFD-bound.'
    Assert-True -Condition (
        $crm91[0].workerKind -ceq 'OfficialCrm91Worker' -and
        $crm91[0].packageLockId -ceq $fixture.Manifest.workers[1].packageLockId -and
        $crm91[0].authentication -ceq 'Ifd' -and
        $crm91[0].identity.mode -ceq 'WindowsCredentialReference' -and
        $crm91[0].identity.reference -ceq $fixture.Crm91CredentialTarget -and
        $crm91[0].identity.homeRealm -ceq $fixture.Crm91HomeRealm
    ) -Message 'CE 9.1 profile must be manifest-derived and IFD-bound.'

    $outputHash = (Get-FileHash -LiteralPath $fixture.OutputPath -Algorithm SHA256).Hash
    $overwriteResult = Invoke-ProfileInputGenerator -Fixture $fixture
    Assert-True -Condition ($overwriteResult.ExitCode -ne 0) `
        -Message 'Generator must refuse to overwrite an existing profile input.'
    Assert-True -Condition ((Get-FileHash -LiteralPath $fixture.OutputPath -Algorithm SHA256).Hash -eq $outputHash) `
        -Message 'Refused overwrite must leave the existing profile input byte-identical.'

    $unsafeUriFixture = New-ProfileInputFixture -Name 'unsafe-uri'
    $unsafeUriResult = Invoke-ProfileInputGenerator -Fixture $unsafeUriFixture -Overrides @{
        Crm82OrganizationBaseUri = 'http://crm82-profile-input.fixture.invalid/'
    }
    Assert-True -Condition ($unsafeUriResult.ExitCode -ne 0) `
        -Message 'A non-HTTPS organization URI must be rejected.'
    Assert-NoProfileInputOutput -Fixture $unsafeUriFixture

    # 部署器會把 OrganizationName 另行傳給官方 SDK，因此 base URI 必須只代表
    # IFD HTTPS host root。若在此接受組織路徑，後續部署器才拒絕會造成 profile
    # 已建立卻無法部署的永久 No-Go；此案例必須在 create-new 寫入前 fail closed。
    $nonCanonicalOrganizationFixture = New-ProfileInputFixture -Name 'non-canonical-organization-uri'
    $nonCanonicalOrganizationResult = Invoke-ProfileInputGenerator `
        -Fixture $nonCanonicalOrganizationFixture `
        -Overrides @{
            Crm82OrganizationBaseUri = 'https://crm82-profile-input.fixture.invalid/organization/'
        }
    Assert-True -Condition ($nonCanonicalOrganizationResult.ExitCode -ne 0) `
        -Message 'An organization URI containing an organization path must be rejected before writing a profile.'
    Assert-NoProfileInputOutput -Fixture $nonCanonicalOrganizationFixture

    $missingWorkerFixture = New-ProfileInputFixture -Name 'missing-worker'
    $missingWorkerFixture.Manifest.workers = @($missingWorkerFixture.Manifest.workers[0])
    Write-StrictJson -Path $missingWorkerFixture.ManifestPath -Value $missingWorkerFixture.Manifest
    $missingWorkerResult = Invoke-ProfileInputGenerator -Fixture $missingWorkerFixture
    Assert-True -Condition ($missingWorkerResult.ExitCode -ne 0) `
        -Message 'A manifest missing either approved Worker must be rejected.'
    Assert-NoProfileInputOutput -Fixture $missingWorkerFixture

    $unknownSecretFixture = New-ProfileInputFixture -Name 'unknown-secret'
    $unknownSecretResult = Invoke-ProfileInputGenerator -Fixture $unknownSecretFixture `
        -AdditionalArguments @('-Password', 'not-accepted')
    Assert-True -Condition ($unknownSecretResult.ExitCode -ne 0) `
        -Message 'The generator must not accept a password parameter.'
    Assert-NoProfileInputOutput -Fixture $unknownSecretFixture

    'All Official Worker profile-input generator tests passed.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        $resolvedFixtureRoot = [IO.Path]::GetFullPath($fixtureRoot)
        $resolvedTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (-not $resolvedFixtureRoot.StartsWith(
                $resolvedTempRoot,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to remove a profile-input fixture outside the temporary directory.'
        }

        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}

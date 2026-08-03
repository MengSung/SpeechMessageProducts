<#
.SYNOPSIS
在 Windows PowerShell 5.1 中驗證官方 Dynamics Worker 部署工具的 fail-closed 與輸出所有權。

.DESCRIPTION
測試只建立位於系統暫存目錄下、本次執行唯一擁有的 fixture，並以真正的 powershell.exe
子程序執行部署工具。非法案例會直接竄改原始 JSON 或 Worker artifact，要求非零結束、兩個
worker-profile.xml 與 Gateway overlay 都不存在，且無關 marker 保持原樣；成功案例則驗證
實際 XML/JSON 消費契約、SHA-256、credential reference、UTF-8 no BOM、CRLF 與 final CRLF。

測試不建立網路連線、不讀取秘密、不寫入來源 appsettings，並在 finally 中只遞迴移除經過
系統暫存根目錄邊界驗證的本次 fixture。子程序完成後不保留 Session、Job、Stream、Timer、
環境變數或背景工作。

.OUTPUTS
成功時輸出固定的通過訊息；任何 assertion 或 cleanup 失敗時以非零結束。

.NOTES
此檔刻意透過破壞真實輸入來驗證拒絕路徑，不以 production helper 計算預期拒絕結果。
#>
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$scriptPath = Join-Path $PSScriptRoot 'New-DynamicsOfficialWorkerDeployment.ps1'
$sourceGatewaySettings = Join-Path (
    $repositoryRoot
) 'SpeechMessage.Dynamics.Gateway\appsettings.json'
$fixtureRoot = Join-Path (
    [IO.Path]::GetTempPath()
) (
    'speechmessage-dynamics-official-worker-deployment-' +
    [Guid]::NewGuid().ToString('N')
)

function Assert-True {
    param(
        [bool] $Condition,
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Write-Utf8Json {
    param(
        [string] $Path,
        [object] $Value
    )

    $json = $Value | ConvertTo-Json -Depth 12
    $json = $json -replace '(?<!\r)\n', "`r`n"
    [IO.File]::WriteAllText(
        $Path,
        $json + "`r`n",
        [Text.UTF8Encoding]::new($false))
}

function Assert-StrictTextFile {
    param([string] $Path)

    $bytes = [IO.File]::ReadAllBytes($Path)
    Assert-True -Condition ($bytes.Length -gt 0) `
        -Message "Generated text file is empty: $Path"
    Assert-True -Condition (-not (
        $bytes.Length -ge 3 -and
        $bytes[0] -eq 0xEF -and
        $bytes[1] -eq 0xBB -and
        $bytes[2] -eq 0xBF
    )) -Message "Generated text file contains a UTF-8 BOM: $Path"

    $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
    Assert-True -Condition (-not [Regex]::IsMatch($text, '(?<!\r)\n')) `
        -Message "Generated text file contains an LF-only line: $Path"
    Assert-True -Condition ($text.EndsWith("`r`n", [StringComparison]::Ordinal)) `
        -Message "Generated text file lacks a final CRLF: $Path"
}

function New-DeploymentFixture {
    param([string] $Name)

    $root = Join-Path $fixtureRoot $Name
    $publishRoot = Join-Path $root 'published'
    $crm82Directory = Join-Path $publishRoot 'crm82'
    $crm91Directory = Join-Path $publishRoot 'crm91'
    $inputDirectory = Join-Path $root 'input'
    $outputDirectory = Join-Path $root 'deployment-config'
    foreach ($directory in @(
        $crm82Directory,
        $crm91Directory,
        $inputDirectory,
        $outputDirectory
    )) {
        [void](New-Item -ItemType Directory -Path $directory -Force)
    }

    $crm82Executable = Join-Path (
        $crm82Directory
    ) 'SpeechMessage.Dynamics.Crm82Worker.exe'
    $crm91Executable = Join-Path (
        $crm91Directory
    ) 'SpeechMessage.Dynamics.Crm91Worker.exe'
    [IO.File]::WriteAllBytes(
        $crm82Executable,
        [Text.Encoding]::ASCII.GetBytes('test-owned-crm82-worker-artifact'))
    [IO.File]::WriteAllBytes(
        $crm91Executable,
        [Text.Encoding]::ASCII.GetBytes('test-owned-crm91-worker-artifact'))

    $protectedMarker = Join-Path $outputDirectory 'protected.test-owned.marker'
    [IO.File]::WriteAllText(
        $protectedMarker,
        'must remain',
        [Text.UTF8Encoding]::new($false))

    $manifest = [ordered]@{
        schemaVersion = 1
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        configuration = 'Release'
        targetFramework = 'net48'
        protocolVersion = 1
        featureGateMustRemainDisabled = $true
        outputRoot = $publishRoot
        workers = @(
            [ordered]@{
                workerKind = 'OfficialCrm82Worker'
                ceVersion = '8.2'
                packageLockId = 'crm82-xrmtooling-8.2.0.5-core-8.2.0.2'
                packageLockSha256 = '4F49F64D7AD1075DE08DDF29C57317843A5BAD3CD0E6203CBC4AA3FF9BCCD58D'
                relativeExecutablePath = 'crm82/SpeechMessage.Dynamics.Crm82Worker.exe'
                sha256 = (
                    Get-FileHash -LiteralPath $crm82Executable -Algorithm SHA256
                ).Hash
                executableBytes = (Get-Item -LiteralPath $crm82Executable).Length
                artifactFileCount = 1
                artifactTotalBytes = (Get-Item -LiteralPath $crm82Executable).Length
            },
            [ordered]@{
                workerKind = 'OfficialCrm91Worker'
                ceVersion = '9.1'
                packageLockId = 'crm91-xrmtooling-9.1.1.65-core-9.0.2.60'
                packageLockSha256 = 'C2FF98918A505AB260676447B719F1EA52A7516028DBACAEF2B438C68F8383EC'
                relativeExecutablePath = 'crm91/SpeechMessage.Dynamics.Crm91Worker.exe'
                sha256 = (
                    Get-FileHash -LiteralPath $crm91Executable -Algorithm SHA256
                ).Hash
                executableBytes = (Get-Item -LiteralPath $crm91Executable).Length
                artifactFileCount = 1
                artifactTotalBytes = (Get-Item -LiteralPath $crm91Executable).Length
            }
        )
    }
    $manifestPath = Join-Path $publishRoot 'official-worker-manifest.json'
    Write-Utf8Json -Path $manifestPath -Value $manifest

    $profiles = [ordered]@{
        schemaVersion = 1
        profiles = @(
            [ordered]@{
                profileAlias = 'crm82'
                workerKind = 'OfficialCrm82Worker'
                packageLockId = 'crm82-xrmtooling-8.2.0.5-core-8.2.0.2'
                profileGenerationId = 'crm82-phase4c-20260802'
                organizationBaseUri = 'https://crm82.lab.test:444/'
                organizationName = 'crm82lab'
                expectedOrganizationId = '7cda29bc-a18b-4c9b-8fb0-4c0fb547e5b1'
                authentication = 'ActiveDirectory'
                identity = [ordered]@{
                    mode = 'HostIdentity'
                }
            },
            [ordered]@{
                profileAlias = 'crm91'
                workerKind = 'OfficialCrm91Worker'
                packageLockId = 'crm91-xrmtooling-9.1.1.65-core-9.0.2.60'
                profileGenerationId = 'crm91-phase4c-20260802'
                organizationBaseUri = 'https://crm91.lab.test/'
                organizationName = 'crm91lab'
                expectedOrganizationId = '30964fc3-53de-4d22-9be7-d3ce8ec2b9c1'
                authentication = 'Ifd'
                identity = [ordered]@{
                    mode = 'WindowsCredentialReference'
                    reference = 'dynamics-crm91-service'
                    homeRealm = 'https://adfs.lab.test/adfs/services/trust/mex'
                }
            }
        )
    }
    $profileInputPath = Join-Path $inputDirectory 'profiles.json'
    Write-Utf8Json -Path $profileInputPath -Value $profiles

    return [pscustomobject]@{
        Root = $root
        PublishRoot = $publishRoot
        Manifest = $manifest
        ManifestPath = $manifestPath
        Profiles = $profiles
        ProfileInputPath = $profileInputPath
        OutputDirectory = $outputDirectory
        OverlayPath = Join-Path $outputDirectory 'dynamics-official-workers.gateway.json'
        Crm82Executable = $crm82Executable
        Crm91Executable = $crm91Executable
        Crm82ProfilePath = Join-Path $crm82Directory 'worker-profile.xml'
        Crm91ProfilePath = Join-Path $crm91Directory 'worker-profile.xml'
        ProtectedMarker = $protectedMarker
    }
}

function Invoke-ProvisioningScript {
    param([object] $Fixture)

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& powershell.exe `
            -NoProfile `
            -ExecutionPolicy Bypass `
            -File $scriptPath `
            -ManifestPath $Fixture.ManifestPath `
            -ProfileInputPath $Fixture.ProfileInputPath `
            -OutputDirectory $Fixture.OutputDirectory `
            -Json 2>&1)
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

function Assert-NoDeploymentWrites {
    param([object] $Fixture)

    Assert-True -Condition (-not (Test-Path -LiteralPath $Fixture.Crm82ProfilePath)) `
        -Message 'Invalid input wrote the CE 8.2 worker profile.'
    Assert-True -Condition (-not (Test-Path -LiteralPath $Fixture.Crm91ProfilePath)) `
        -Message 'Invalid input wrote the CE 9.1 worker profile.'
    Assert-True -Condition (-not (Test-Path -LiteralPath $Fixture.OverlayPath)) `
        -Message 'Invalid input wrote the Gateway overlay.'
    Assert-True -Condition (Test-Path -LiteralPath $Fixture.ProtectedMarker) `
        -Message 'The provisioning script removed an unrelated test-owned marker.'
}

function Assert-FailureBeforeWrites {
    param(
        [string] $Name,
        [scriptblock] $Mutate
    )

    $fixture = New-DeploymentFixture -Name $Name
    & $Mutate $fixture
    $result = Invoke-ProvisioningScript -Fixture $fixture
    Assert-True -Condition ($result.ExitCode -ne 0) `
        -Message "Invalid deployment input was accepted: $Name"
    Assert-NoDeploymentWrites -Fixture $fixture
}

try {
    Assert-True -Condition (Test-Path -LiteralPath $scriptPath -PathType Leaf) `
        -Message 'The official Dynamics worker deployment provisioning script is missing.'

    $sourceHashBefore = (
        Get-FileHash -LiteralPath $sourceGatewaySettings -Algorithm SHA256
    ).Hash

    Assert-FailureBeforeWrites -Name 'manifest-hash-mismatch' -Mutate {
        param($fixture)
        $fixture.Manifest.workers[0].sha256 = ('A' * 64)
        Write-Utf8Json -Path $fixture.ManifestPath -Value $fixture.Manifest
    }

    Assert-FailureBeforeWrites -Name 'published-executable-tamper' -Mutate {
        param($fixture)
        [IO.File]::WriteAllBytes(
            $fixture.Crm82Executable,
            [Text.Encoding]::ASCII.GetBytes('tampered-crm82-worker-artifact'))
    }

    Assert-FailureBeforeWrites -Name 'manifest-package-lock-drift' -Mutate {
        param($fixture)
        $fixture.Manifest.workers[1].packageLockId = 'crm91-unapproved-lock'
        Write-Utf8Json -Path $fixture.ManifestPath -Value $fixture.Manifest
    }

    Assert-FailureBeforeWrites -Name 'manifest-worker-kind-drift' -Mutate {
        param($fixture)
        $fixture.Manifest.workers[0].workerKind = 'OfficialCrm91Worker'
        Write-Utf8Json -Path $fixture.ManifestPath -Value $fixture.Manifest
    }

    Assert-FailureBeforeWrites -Name 'manifest-feature-gate-enabled' -Mutate {
        param($fixture)
        $fixture.Manifest.featureGateMustRemainDisabled = $false
        Write-Utf8Json -Path $fixture.ManifestPath -Value $fixture.Manifest
    }

    Assert-FailureBeforeWrites -Name 'manifest-path-traversal' -Mutate {
        param($fixture)
        $fixture.Manifest.workers[0].relativeExecutablePath =
            '../SpeechMessage.Dynamics.Crm82Worker.exe'
        Write-Utf8Json -Path $fixture.ManifestPath -Value $fixture.Manifest
    }

    Assert-FailureBeforeWrites -Name 'manifest-byte-count-drift' -Mutate {
        param($fixture)
        $fixture.Manifest.workers[1].executableBytes++
        Write-Utf8Json -Path $fixture.ManifestPath -Value $fixture.Manifest
    }

    Assert-FailureBeforeWrites -Name 'manifest-artifact-count-drift' -Mutate {
        param($fixture)
        $fixture.Manifest.workers[0].artifactFileCount++
        Write-Utf8Json -Path $fixture.ManifestPath -Value $fixture.Manifest
    }

    Assert-FailureBeforeWrites -Name 'manifest-artifact-byte-drift' -Mutate {
        param($fixture)
        $fixture.Manifest.workers[0].artifactTotalBytes++
        Write-Utf8Json -Path $fixture.ManifestPath -Value $fixture.Manifest
    }

    Assert-FailureBeforeWrites -Name 'manifest-placeholder-package-hash' -Mutate {
        param($fixture)
        $fixture.Manifest.workers[0].packageLockSha256 = ('A' * 64)
        Write-Utf8Json -Path $fixture.ManifestPath -Value $fixture.Manifest
    }

    Assert-FailureBeforeWrites -Name 'manifest-unapproved-package-hash' -Mutate {
        param($fixture)
        $fixture.Manifest.workers[0].packageLockSha256 = (
            '0123456789ABCDEF' * 4
        )
        Write-Utf8Json -Path $fixture.ManifestPath -Value $fixture.Manifest
    }

    Assert-FailureBeforeWrites -Name 'profile-placeholder-guid' -Mutate {
        param($fixture)
        $fixture.Profiles.profiles[0].expectedOrganizationId =
            '11111111-1111-1111-1111-111111111111'
        Write-Utf8Json -Path $fixture.ProfileInputPath -Value $fixture.Profiles
    }

    Assert-FailureBeforeWrites -Name 'profile-secret-field' -Mutate {
        param($fixture)
        $fixture.Profiles.profiles[1]['password'] = 'must-not-be-accepted'
        Write-Utf8Json -Path $fixture.ProfileInputPath -Value $fixture.Profiles
    }

    Assert-FailureBeforeWrites -Name 'profile-route-shaped-reference' -Mutate {
        param($fixture)
        $fixture.Profiles.profiles[1].identity.reference =
            'https://credential.invalid/reference'
        Write-Utf8Json -Path $fixture.ProfileInputPath -Value $fixture.Profiles
    }

    Assert-FailureBeforeWrites -Name 'profile-noncanonical-organization-uri' -Mutate {
        param($fixture)
        $fixture.Profiles.profiles[0].organizationBaseUri =
            'https://crm82.lab.test:444/?probe=true'
        Write-Utf8Json -Path $fixture.ProfileInputPath -Value $fixture.Profiles
    }

    Assert-FailureBeforeWrites -Name 'profile-ifd-host-identity' -Mutate {
        param($fixture)
        $fixture.Profiles.profiles[1].identity = [ordered]@{
            mode = 'HostIdentity'
        }
        Write-Utf8Json -Path $fixture.ProfileInputPath -Value $fixture.Profiles
    }

    Assert-FailureBeforeWrites -Name 'profile-duplicate-alias' -Mutate {
        param($fixture)
        $fixture.Profiles.profiles[1].profileAlias = 'CRM82'
        Write-Utf8Json -Path $fixture.ProfileInputPath -Value $fixture.Profiles
    }

    Assert-FailureBeforeWrites -Name 'profile-unexpected-field' -Mutate {
        param($fixture)
        $fixture.Profiles.profiles[0]['unexpectedField'] = 'unexpected-value'
        Write-Utf8Json -Path $fixture.ProfileInputPath -Value $fixture.Profiles
    }

    Assert-FailureBeforeWrites -Name 'profile-exact-duplicate-property' -Mutate {
        param($fixture)
        $rawJson = [IO.File]::ReadAllText($fixture.ProfileInputPath)
        $duplicateJson = [Regex]::Replace(
            $rawJson,
            '"profileAlias"\s*:\s*"crm82"',
            '"profileAlias": "crm91", "profileAlias": "crm82"',
            1)
        Assert-True -Condition (-not [string]::Equals(
            $rawJson,
            $duplicateJson,
            [StringComparison]::Ordinal)) `
            -Message 'The duplicate-property test did not alter the raw JSON fixture.'
        [IO.File]::WriteAllText(
            $fixture.ProfileInputPath,
            $duplicateJson,
            [Text.UTF8Encoding]::new($false))
    }

    # profile 可獨立驗證不代表輸入集合可為空或無界；空集合會失去任何可驗證的
    # Worker owner，超過兩個則可能嘗試重複寫入同一 executable 旁的 XML。兩者都必須
    # 在建立 temporary output、overlay 或接觸既有 artifact 前 fail closed。
    Assert-FailureBeforeWrites -Name 'profile-empty-list' -Mutate {
        param($fixture)
        $fixture.Profiles.profiles = @()
        Write-Utf8Json -Path $fixture.ProfileInputPath -Value $fixture.Profiles
    }

    Assert-FailureBeforeWrites -Name 'profile-excess-count' -Mutate {
        param($fixture)
        $fixture.Profiles.profiles = @(
            $fixture.Profiles.profiles[0],
            $fixture.Profiles.profiles[1],
            $fixture.Profiles.profiles[0]
        )
        Write-Utf8Json -Path $fixture.ProfileInputPath -Value $fixture.Profiles
    }

    $existingOutputFixture = New-DeploymentFixture -Name 'existing-output-refused'
    [IO.File]::WriteAllText(
        $existingOutputFixture.OverlayPath,
        'test-owned-existing-overlay',
        [Text.UTF8Encoding]::new($false))
    $existingOutputResult = Invoke-ProvisioningScript -Fixture $existingOutputFixture
    Assert-True -Condition ($existingOutputResult.ExitCode -ne 0) `
        -Message 'Provisioning overwrote an existing Gateway overlay.'
    Assert-True -Condition (-not (
        Test-Path -LiteralPath $existingOutputFixture.Crm82ProfilePath
    )) -Message 'Existing-output rejection wrote the CE 8.2 worker profile.'
    Assert-True -Condition (-not (
        Test-Path -LiteralPath $existingOutputFixture.Crm91ProfilePath
    )) -Message 'Existing-output rejection wrote the CE 9.1 worker profile.'
    Assert-True -Condition (
        (Get-Content -LiteralPath $existingOutputFixture.OverlayPath -Raw) -eq
        'test-owned-existing-overlay'
    ) -Message 'Provisioning changed the existing Gateway overlay.'
    Assert-True -Condition (Test-Path -LiteralPath (
        $existingOutputFixture.ProtectedMarker
    )) -Message 'Existing-output rejection removed an unrelated marker.'

    $fixture = New-DeploymentFixture -Name 'valid-two-profile-deployment'
    Assert-NoDeploymentWrites -Fixture $fixture
    $result = Invoke-ProvisioningScript -Fixture $fixture
    Assert-True -Condition ($result.ExitCode -eq 0) `
        -Message (
            'Valid deployment provisioning failed. ExitCode=' +
            $result.ExitCode + ' Output=' + $result.Text
        )

    $resultObject = $result.Text | ConvertFrom-Json -ErrorAction Stop
    Assert-True -Condition ($resultObject.outcome -eq 'provisioned') `
        -Message 'The deployment result outcome is invalid.'
    Assert-True -Condition ($resultObject.featureGateMustRemainDisabled -eq $true) `
        -Message 'The deployment result must preserve the disabled feature gate.'

    foreach ($path in @(
        $fixture.Crm82ProfilePath,
        $fixture.Crm91ProfilePath,
        $fixture.OverlayPath
    )) {
        Assert-True -Condition (Test-Path -LiteralPath $path -PathType Leaf) `
            -Message "Expected deployment output is missing: $path"
        Assert-StrictTextFile -Path $path
    }

    $profileCases = @(
        [pscustomobject]@{
            Path = $fixture.Crm82ProfilePath
            Alias = 'crm82'
            Kind = 'OfficialCrm82Worker'
            PackageLockId = 'crm82-xrmtooling-8.2.0.5-core-8.2.0.2'
            GenerationId = 'crm82-phase4c-20260802'
            HostName = 'crm82.lab.test'
            Port = '444'
            OrganizationName = 'crm82lab'
            ExpectedOrganizationId = '7cda29bc-a18b-4c9b-8fb0-4c0fb547e5b1'
            Authentication = 'ActiveDirectory'
            IdentityMode = 'HostIdentity'
            CredentialReference = $null
            HomeRealm = $null
        },
        [pscustomobject]@{
            Path = $fixture.Crm91ProfilePath
            Alias = 'crm91'
            Kind = 'OfficialCrm91Worker'
            PackageLockId = 'crm91-xrmtooling-9.1.1.65-core-9.0.2.60'
            GenerationId = 'crm91-phase4c-20260802'
            HostName = 'crm91.lab.test'
            Port = '443'
            OrganizationName = 'crm91lab'
            ExpectedOrganizationId = '30964fc3-53de-4d22-9be7-d3ce8ec2b9c1'
            Authentication = 'Ifd'
            IdentityMode = 'WindowsCredentialReference'
            CredentialReference = 'dynamics-crm91-service'
            HomeRealm = 'https://adfs.lab.test/adfs/services/trust/mex'
        }
    )

    foreach ($case in $profileCases) {
        [xml]$document = Get-Content -LiteralPath $case.Path -Raw -Encoding utf8
        $root = $document.officialDynamicsWorkerProfiles
        Assert-True -Condition ($root.version -eq '1') `
            -Message "Worker profile root version is invalid: $($case.Kind)"
        $profiles = @($root.profile)
        Assert-True -Condition ($profiles.Count -eq 1) `
            -Message "Worker profile must contain exactly one profile: $($case.Kind)"
        $profile = $profiles[0]
        Assert-True -Condition ($profile.generationId -eq $case.GenerationId) `
            -Message "Worker profile generation mismatch: $($case.Kind)"
        Assert-True -Condition ($profile.workerKind -eq $case.Kind) `
            -Message "Worker profile kind mismatch: $($case.Kind)"
        Assert-True -Condition ($profile.packageLockId -eq $case.PackageLockId) `
            -Message "Worker profile package lock mismatch: $($case.Kind)"
        Assert-True -Condition ($profile.organization.hostName -eq $case.HostName) `
            -Message "Worker profile host mismatch: $($case.Kind)"
        Assert-True -Condition ($profile.organization.port -eq $case.Port) `
            -Message "Worker profile port mismatch: $($case.Kind)"
        Assert-True -Condition ($profile.organization.name -eq $case.OrganizationName) `
            -Message "Worker profile organization name mismatch: $($case.Kind)"
        Assert-True -Condition (
            $profile.organization.expectedOrganizationId -eq
            $case.ExpectedOrganizationId
        ) -Message "Worker profile organization ID mismatch: $($case.Kind)"
        Assert-True -Condition ($profile.organization.useSsl -eq 'true') `
            -Message "Worker profile must require TLS: $($case.Kind)"
        Assert-True -Condition (
            $profile.organization.authentication -eq $case.Authentication
        ) -Message "Worker profile authentication mismatch: $($case.Kind)"
        Assert-True -Condition ($profile.identity.mode -eq $case.IdentityMode) `
            -Message "Worker profile identity mode mismatch: $($case.Kind)"
        $actualCredentialReference = if ($profile.identity.HasAttribute('reference')) {
            $profile.identity.GetAttribute('reference')
        }
        else {
            $null
        }
        $actualHomeRealm = if ($profile.identity.HasAttribute('homeRealm')) {
            $profile.identity.GetAttribute('homeRealm')
        }
        else {
            $null
        }
        Assert-True -Condition (
            $actualCredentialReference -eq $case.CredentialReference
        ) `
            -Message "Worker profile credential reference mismatch: $($case.Kind)"
        Assert-True -Condition ($actualHomeRealm -eq $case.HomeRealm) `
            -Message "Worker profile home realm mismatch: $($case.Kind)"
    }

    $overlay = Get-Content -LiteralPath $fixture.OverlayPath -Raw -Encoding utf8 |
        ConvertFrom-Json -ErrorAction Stop
    $crm82Overlay = $overlay.DynamicsProfiles.Profiles.crm82
    $crm91Overlay = $overlay.DynamicsProfiles.Profiles.crm91
    foreach ($case in @(
        [pscustomobject]@{
            Overlay = $crm82Overlay
            Kind = 'OfficialCrm82Worker'
            Executable = $fixture.Crm82Executable
            Hash = $fixture.Manifest.workers[0].sha256
            PackageLockId = $fixture.Manifest.workers[0].packageLockId
            GenerationId = 'crm82-phase4c-20260802'
            OrganizationBaseUri = 'https://crm82.lab.test:444/'
            ExpectedOrganizationId = '7cda29bc-a18b-4c9b-8fb0-4c0fb547e5b1'
        },
        [pscustomobject]@{
            Overlay = $crm91Overlay
            Kind = 'OfficialCrm91Worker'
            Executable = $fixture.Crm91Executable
            Hash = $fixture.Manifest.workers[1].sha256
            PackageLockId = $fixture.Manifest.workers[1].packageLockId
            GenerationId = 'crm91-phase4c-20260802'
            OrganizationBaseUri = 'https://crm91.lab.test/'
            ExpectedOrganizationId = '30964fc3-53de-4d22-9be7-d3ce8ec2b9c1'
        }
    )) {
        Assert-True -Condition ($case.Overlay.WorkerKind -eq $case.Kind) `
            -Message "Gateway overlay worker kind mismatch: $($case.Kind)"
        Assert-True -Condition (
            $case.Overlay.WorkerExecutablePath -eq
            [IO.Path]::GetFullPath($case.Executable)
        ) -Message "Gateway overlay executable path mismatch: $($case.Kind)"
        Assert-True -Condition ($case.Overlay.WorkerExecutableSha256 -eq $case.Hash) `
            -Message "Gateway overlay executable hash mismatch: $($case.Kind)"
        Assert-True -Condition ($case.Overlay.PackageLockId -eq $case.PackageLockId) `
            -Message "Gateway overlay package lock mismatch: $($case.Kind)"
        Assert-True -Condition (
            $case.Overlay.WorkerProfileGenerationId -eq $case.GenerationId
        ) -Message "Gateway overlay generation mismatch: $($case.Kind)"
        Assert-True -Condition (
            $case.Overlay.OrganizationBaseUri -eq $case.OrganizationBaseUri
        ) -Message "Gateway overlay organization URI mismatch: $($case.Kind)"
        Assert-True -Condition (
            $case.Overlay.Admission.ExpectedOrganizationId -eq
            $case.ExpectedOrganizationId
        ) -Message "Gateway overlay organization ID mismatch: $($case.Kind)"
    }

    $generatedText = @(
        (Get-Content -LiteralPath $fixture.Crm82ProfilePath -Raw -Encoding utf8)
        (Get-Content -LiteralPath $fixture.Crm91ProfilePath -Raw -Encoding utf8)
        (Get-Content -LiteralPath $fixture.OverlayPath -Raw -Encoding utf8)
        $result.Text
    ) -join [Environment]::NewLine
    Assert-True -Condition (-not [Regex]::IsMatch(
        $generatedText,
        '(?i)(password|token|connection[ -]?string|cookie|client[ -]?secret|secret[ -]?value)'
    )) -Message 'Generated deployment output contains a secret-shaped field or value.'
    Assert-True -Condition (Test-Path -LiteralPath $fixture.ProtectedMarker) `
        -Message 'Provisioning removed an unrelated output marker.'

    # Phase 4C 允許 CE 8.2 或 CE 9.1 各自完成部署與相容性驗證；因此缺少另一版的權威資料時，
    # 不能為了湊成雙 profile 而猜測組織、認證或 credential reference。此案例只保留 CE 8.2 的
    # 已核准非機密輸入，驗證產生器只建立該 worker 的 XML 與同樣單 profile 的 Gateway overlay，
    # 既不觸碰未選取 CE 9.1 目錄，也不把兩版本的設定、Session 或資源所有權混在一起。
    $singleProfileFixture = New-DeploymentFixture -Name 'valid-single-ce82-profile-deployment'
    $singleProfileFixture.Profiles.profiles = @(
        $singleProfileFixture.Profiles.profiles[0]
    )
    Write-Utf8Json `
        -Path $singleProfileFixture.ProfileInputPath `
        -Value $singleProfileFixture.Profiles
    Assert-NoDeploymentWrites -Fixture $singleProfileFixture
    $singleProfileResult = Invoke-ProvisioningScript -Fixture $singleProfileFixture
    Assert-True -Condition ($singleProfileResult.ExitCode -eq 0) `
        -Message (
            'A valid single CE 8.2 deployment profile was rejected. ExitCode=' +
            $singleProfileResult.ExitCode + ' Output=' + $singleProfileResult.Text
        )

    $singleProfileResultObject = $singleProfileResult.Text |
        ConvertFrom-Json -ErrorAction Stop
    Assert-True -Condition (@($singleProfileResultObject.workers).Count -eq 1) `
        -Message 'A single-profile deployment result must expose exactly one worker.'
    Assert-True -Condition (
        $singleProfileResultObject.workers[0].workerKind -eq 'OfficialCrm82Worker'
    ) -Message 'A single CE 8.2 deployment result selected an unexpected worker.'
    Assert-True -Condition (Test-Path -LiteralPath $singleProfileFixture.Crm82ProfilePath) `
        -Message 'A valid single CE 8.2 deployment did not create its worker profile.'
    Assert-True -Condition (-not (
        Test-Path -LiteralPath $singleProfileFixture.Crm91ProfilePath
    )) -Message 'A single CE 8.2 deployment created an unselected CE 9.1 worker profile.'
    Assert-True -Condition (Test-Path -LiteralPath $singleProfileFixture.OverlayPath) `
        -Message 'A valid single CE 8.2 deployment did not create the Gateway overlay.'
    Assert-StrictTextFile -Path $singleProfileFixture.Crm82ProfilePath
    Assert-StrictTextFile -Path $singleProfileFixture.OverlayPath

    $singleProfileOverlay = Get-Content `
        -LiteralPath $singleProfileFixture.OverlayPath `
        -Raw `
        -Encoding utf8 |
        ConvertFrom-Json -ErrorAction Stop
    Assert-True -Condition ($null -ne $singleProfileOverlay.DynamicsProfiles.Profiles.crm82) `
        -Message 'A single CE 8.2 Gateway overlay omitted the selected profile.'
    Assert-True -Condition ($null -eq (
        $singleProfileOverlay.DynamicsProfiles.Profiles.PSObject.Properties['crm91']
    )) `
        -Message 'A single CE 8.2 Gateway overlay invented an unselected CE 9.1 profile.'
    Assert-True -Condition (Test-Path -LiteralPath $singleProfileFixture.ProtectedMarker) `
        -Message 'Single-profile provisioning removed an unrelated test-owned marker.'

    # CE 9.1 的單 profile 路徑另行保護 IFD／Windows credential reference 的嚴格聯集。
    # 這個 fixture 只驗證輸出的數量與選取範圍，不讀取或輸出 credential reference、home realm
    # 或 CRM 回應；每個 temporary fixture 仍由最外層 finally 唯一擁有並刪除，避免測試程序
    # 殘留 profile、檔案 handle、Session 或跨版本狀態。
    $singleCrm91Fixture = New-DeploymentFixture -Name 'valid-single-ce91-profile-deployment'
    $singleCrm91Fixture.Profiles.profiles = @(
        $singleCrm91Fixture.Profiles.profiles[1]
    )
    Write-Utf8Json `
        -Path $singleCrm91Fixture.ProfileInputPath `
        -Value $singleCrm91Fixture.Profiles
    Assert-NoDeploymentWrites -Fixture $singleCrm91Fixture
    $singleCrm91Result = Invoke-ProvisioningScript -Fixture $singleCrm91Fixture
    Assert-True -Condition ($singleCrm91Result.ExitCode -eq 0) `
        -Message 'A valid single CE 9.1 deployment profile was rejected.'
    $singleCrm91ResultObject = $singleCrm91Result.Text |
        ConvertFrom-Json -ErrorAction Stop
    Assert-True -Condition (@($singleCrm91ResultObject.workers).Count -eq 1) `
        -Message 'A single CE 9.1 deployment result must expose exactly one worker.'
    Assert-True -Condition (
        $singleCrm91ResultObject.workers[0].workerKind -eq 'OfficialCrm91Worker'
    ) -Message 'A single CE 9.1 deployment result selected an unexpected worker.'
    Assert-True -Condition (-not (
        Test-Path -LiteralPath $singleCrm91Fixture.Crm82ProfilePath
    )) -Message 'A single CE 9.1 deployment created an unselected CE 8.2 worker profile.'
    Assert-True -Condition (Test-Path -LiteralPath $singleCrm91Fixture.Crm91ProfilePath) `
        -Message 'A valid single CE 9.1 deployment did not create its worker profile.'
    Assert-StrictTextFile -Path $singleCrm91Fixture.Crm91ProfilePath
    $singleCrm91Overlay = Get-Content `
        -LiteralPath $singleCrm91Fixture.OverlayPath `
        -Raw `
        -Encoding utf8 |
        ConvertFrom-Json -ErrorAction Stop
    Assert-True -Condition ($null -eq (
        $singleCrm91Overlay.DynamicsProfiles.Profiles.PSObject.Properties['crm82']
    )) `
        -Message 'A single CE 9.1 Gateway overlay invented an unselected CE 8.2 profile.'
    Assert-True -Condition ($null -ne (
        $singleCrm91Overlay.DynamicsProfiles.Profiles.PSObject.Properties['crm91']
    )) `
        -Message 'A single CE 9.1 Gateway overlay omitted the selected profile.'
    Assert-True -Condition (Test-Path -LiteralPath $singleCrm91Fixture.ProtectedMarker) `
        -Message 'Single CE 9.1 provisioning removed an unrelated test-owned marker.'

    $sourceHashAfter = (
        Get-FileHash -LiteralPath $sourceGatewaySettings -Algorithm SHA256
    ).Hash
    Assert-True -Condition ($sourceHashAfter -eq $sourceHashBefore) `
        -Message 'Provisioning modified the source Gateway appsettings.json.'

    'All official Dynamics worker deployment provisioning tests passed.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
        $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (-not $resolvedFixture.StartsWith(
                $resolvedTemp,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to remove a deployment fixture outside the temporary directory.'
        }

        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
    }
}

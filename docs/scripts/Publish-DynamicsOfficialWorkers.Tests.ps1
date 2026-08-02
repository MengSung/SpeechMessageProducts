[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$scriptPath = Join-Path $PSScriptRoot 'Publish-DynamicsOfficialWorkers.ps1'
$fixtureRoot = Join-Path (
    [IO.Path]::GetTempPath()
) (
    'speechmessage-dynamics-official-worker-publish-' +
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

try {
    Assert-True -Condition (Test-Path -LiteralPath $scriptPath -PathType Leaf) `
        -Message 'The official Dynamics worker publish script is missing.'

    $output = @(& powershell.exe `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $scriptPath `
        -RepositoryPath $repositoryRoot `
        -OutputRoot $fixtureRoot `
        -Json 2>&1)
    $exitCode = $LASTEXITCODE
    $text = $output -join [Environment]::NewLine

    Assert-True -Condition ($exitCode -eq 0) `
        -Message "Publish script failed. ExitCode=$exitCode Output=$text"

    $result = $text | ConvertFrom-Json -ErrorAction Stop
    Assert-True -Condition ($result.schemaVersion -eq 1) `
        -Message 'The publish result schema version is invalid.'
    Assert-True -Condition (@($result.workers).Count -eq 2) `
        -Message 'The publish result must contain exactly two worker artifacts.'

    $manifestPath = Join-Path $fixtureRoot 'official-worker-manifest.json'
    Assert-True -Condition (Test-Path -LiteralPath $manifestPath -PathType Leaf) `
        -Message 'The official worker manifest was not created.'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8 |
        ConvertFrom-Json -ErrorAction Stop

    foreach ($expected in @(
        [pscustomobject]@{
            Kind = 'OfficialCrm82Worker'
            Directory = 'crm82'
            Executable = 'SpeechMessage.Dynamics.Crm82Worker.exe'
            PackageLockId = 'crm82-xrmtooling-8.2.0.5-core-8.2.0.2'
            CeVersion = '8.2'
        },
        [pscustomobject]@{
            Kind = 'OfficialCrm91Worker'
            Directory = 'crm91'
            Executable = 'SpeechMessage.Dynamics.Crm91Worker.exe'
            PackageLockId = 'crm91-xrmtooling-9.1.1.65-core-9.0.2.60'
            CeVersion = '9.1'
        }
    )) {
        $worker = @($manifest.workers) |
            Where-Object { $_.workerKind -eq $expected.Kind } |
            Select-Object -First 1
        Assert-True -Condition ($null -ne $worker) `
            -Message "Manifest worker is missing: $($expected.Kind)"

        $executablePath = Join-Path (
            Join-Path $fixtureRoot $expected.Directory
        ) $expected.Executable
        Assert-True -Condition (Test-Path -LiteralPath $executablePath -PathType Leaf) `
            -Message "Published executable is missing: $($expected.Executable)"
        Assert-True -Condition (-not (Test-Path -LiteralPath (
            Join-Path (Split-Path -Parent $executablePath) 'worker-profile.xml'
        ))) -Message 'Publish must not create or copy worker-profile.xml.'

        $actualHash = (Get-FileHash -LiteralPath $executablePath -Algorithm SHA256).Hash
        Assert-True -Condition ($worker.sha256 -eq $actualHash) `
            -Message "Manifest hash mismatch: $($expected.Kind)"
        Assert-True -Condition ($worker.packageLockId -eq $expected.PackageLockId) `
            -Message "Manifest package lock mismatch: $($expected.Kind)"
        Assert-True -Condition ($worker.ceVersion -eq $expected.CeVersion) `
            -Message "Manifest CE version mismatch: $($expected.Kind)"
        Assert-True -Condition ($worker.relativeExecutablePath -eq (
            "$($expected.Directory)/$($expected.Executable)"
        )) -Message "Manifest relative path mismatch: $($expected.Kind)"
    }

    Assert-True -Condition ($manifest.featureGateMustRemainDisabled -eq $true) `
        -Message 'Manifest must preserve the disabled Package01 feature gate.'

    'All official Dynamics worker publish tests passed.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
        $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (-not $resolvedFixture.StartsWith(
                $resolvedTemp,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to remove a publish fixture outside the temporary directory.'
        }

        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
    }
}

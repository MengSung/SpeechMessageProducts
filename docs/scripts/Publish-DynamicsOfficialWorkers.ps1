[CmdletBinding()]
param(
    [string] $RepositoryPath = '',

    [string] $OutputRoot = '',

    [switch] $Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

if ([string]::IsNullOrWhiteSpace($RepositoryPath)) {
    $RepositoryPath = Join-Path $PSScriptRoot '..\..'
}

$repositoryRoot = [IO.Path]::GetFullPath($RepositoryPath)
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot 'artifacts\dynamics-workers'
}

$resolvedOutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$directorySeparator = [IO.Path]::DirectorySeparatorChar
$repositoryPrefix = $repositoryRoot.TrimEnd('\', '/') + $directorySeparator
$outputPrefix = $resolvedOutputRoot.TrimEnd('\', '/') + $directorySeparator

if (-not (Test-Path -LiteralPath (
            Join-Path $repositoryRoot 'SpeechMessageProducts.sln'
        ) -PathType Leaf)) {
    throw 'RepositoryPath must contain SpeechMessageProducts.sln.'
}

if ([string]::Equals(
        $resolvedOutputRoot,
        $repositoryRoot,
        [StringComparison]::OrdinalIgnoreCase) -or
    $repositoryPrefix.StartsWith(
        $outputPrefix,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputRoot must not be the repository or one of its ancestors.'
}

if (Test-Path -LiteralPath $resolvedOutputRoot -PathType Leaf) {
    throw 'OutputRoot must be a directory path.'
}

$dotnet = Get-Command 'dotnet.exe' -CommandType Application -ErrorAction Stop |
    Select-Object -First 1

$workerDefinitions = @(
    [pscustomobject]@{
        WorkerKind = 'OfficialCrm82Worker'
        CeVersion = '8.2'
        DirectoryName = 'crm82'
        ProjectRelativePath = 'SpeechMessage.Dynamics.Crm82Worker\SpeechMessage.Dynamics.Crm82Worker.csproj'
        ExecutableName = 'SpeechMessage.Dynamics.Crm82Worker.exe'
        PackageLockId = 'crm82-xrmtooling-8.2.0.5-core-8.2.0.2'
    },
    [pscustomobject]@{
        WorkerKind = 'OfficialCrm91Worker'
        CeVersion = '9.1'
        DirectoryName = 'crm91'
        ProjectRelativePath = 'SpeechMessage.Dynamics.Crm91Worker\SpeechMessage.Dynamics.Crm91Worker.csproj'
        ExecutableName = 'SpeechMessage.Dynamics.Crm91Worker.exe'
        PackageLockId = 'crm91-xrmtooling-9.1.1.65-core-9.0.2.60'
    }
)

function Get-BoundedFailureDetail {
    param([object[]] $Output)

    $detail = (($Output | ForEach-Object { [string] $_ }) -join [Environment]::NewLine).Trim()
    if ([string]::IsNullOrWhiteSpace($detail)) {
        return '(no diagnostic output)'
    }

    if ($detail.Length -gt 4096) {
        return $detail.Substring(0, 4096) + [Environment]::NewLine +
            '(diagnostic output truncated)'
    }

    return $detail
}

function Remove-VerifiedOutputDirectory {
    param(
        [string] $DirectoryPath,
        [string] $ExpectedParent
    )

    if (-not (Test-Path -LiteralPath $DirectoryPath)) {
        return
    }

    $resolvedDirectory = [IO.Path]::GetFullPath($DirectoryPath)
    $resolvedParent = [IO.Path]::GetFullPath($ExpectedParent)
    $parentPrefix = $resolvedParent.TrimEnd('\', '/') +
        [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedDirectory.StartsWith(
            $parentPrefix,
            [StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals(
            $resolvedDirectory,
            $resolvedParent,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to remove an output directory outside OutputRoot.'
    }

    Remove-Item -LiteralPath $resolvedDirectory -Recurse -Force
}

[void](New-Item -ItemType Directory -Path $resolvedOutputRoot -Force)
$publishedWorkers = [Collections.Generic.List[object]]::new(2)

foreach ($definition in $workerDefinitions) {
    $projectPath = [IO.Path]::GetFullPath((
        Join-Path $repositoryRoot $definition.ProjectRelativePath
    ))
    $lockPath = Join-Path (Split-Path -Parent $projectPath) 'packages.lock.json'
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $lockPath -PathType Leaf)) {
        throw "The pinned worker project or package lock is missing: $($definition.WorkerKind)"
    }

    $workerOutput = Join-Path $resolvedOutputRoot $definition.DirectoryName
    Remove-VerifiedOutputDirectory `
        -DirectoryPath $workerOutput `
        -ExpectedParent $resolvedOutputRoot
    [void](New-Item -ItemType Directory -Path $workerOutput)

    $publishOutput = @(& $dotnet.Source `
        publish $projectPath `
        --configuration Release `
        --no-restore `
        --output $workerOutput 2>&1)
    $publishExitCode = $LASTEXITCODE
    if ($publishExitCode -ne 0) {
        $detail = Get-BoundedFailureDetail -Output $publishOutput
        throw "Official worker publish failed: $($definition.WorkerKind) " +
            "(exit code $publishExitCode).$([Environment]::NewLine)$detail"
    }

    $executablePath = Join-Path $workerOutput $definition.ExecutableName
    if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
        throw "Published worker executable is missing: $($definition.WorkerKind)"
    }

    $profilePath = Join-Path $workerOutput 'worker-profile.xml'
    if (Test-Path -LiteralPath $profilePath) {
        throw 'Publish output must not contain worker-profile.xml.'
    }

    $files = @(Get-ChildItem -LiteralPath $workerOutput -File)
    $totalBytes = [long](($files | Measure-Object -Property Length -Sum).Sum)
    $publishedWorkers.Add([ordered]@{
        workerKind = $definition.WorkerKind
        ceVersion = $definition.CeVersion
        packageLockId = $definition.PackageLockId
        packageLockSha256 = (
            Get-FileHash -LiteralPath $lockPath -Algorithm SHA256
        ).Hash
        relativeExecutablePath = (
            $definition.DirectoryName + '/' + $definition.ExecutableName
        )
        sha256 = (
            Get-FileHash -LiteralPath $executablePath -Algorithm SHA256
        ).Hash
        executableBytes = (Get-Item -LiteralPath $executablePath).Length
        artifactFileCount = $files.Count
        artifactTotalBytes = $totalBytes
    })
}

$manifest = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    configuration = 'Release'
    targetFramework = 'net48'
    protocolVersion = 1
    featureGateMustRemainDisabled = $true
    outputRoot = $resolvedOutputRoot
    workers = @($publishedWorkers)
}
$manifestPath = Join-Path $resolvedOutputRoot 'official-worker-manifest.json'
$manifestJson = $manifest | ConvertTo-Json -Depth 6
[IO.File]::WriteAllText(
    $manifestPath,
    $manifestJson + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))

$result = [ordered]@{
    schemaVersion = 1
    outcome = 'published'
    manifestPath = $manifestPath
    featureGateMustRemainDisabled = $true
    workers = @($publishedWorkers)
}

if ($Json) {
    $result | ConvertTo-Json -Depth 6
}
else {
    [pscustomobject]$result
}

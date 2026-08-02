[CmdletBinding()]
param(
    [string]$RepositoryPath,
    [string]$SolutionPath = "SpeechMessageProducts.sln",
    [switch]$Json,
    [switch]$SummaryOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$trimCharacters = [char[]]@(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar
)

function Get-FullPath {
    param(
        [string]$BasePath,
        [string]$Path
    )

    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }

    return [IO.Path]::GetFullPath((Join-Path $BasePath $Path))
}

function Get-RelativePath {
    param(
        [string]$BasePath,
        [string]$Path
    )

    $baseFullPath = [IO.Path]::GetFullPath($BasePath).TrimEnd($trimCharacters)
    $pathFullPath = [IO.Path]::GetFullPath($Path)
    $prefix = $baseFullPath + [IO.Path]::DirectorySeparatorChar

    if ([string]::Equals(
        $pathFullPath,
        $baseFullPath,
        [StringComparison]::OrdinalIgnoreCase
    )) {
        return "."
    }

    if ($pathFullPath.StartsWith(
        $prefix,
        [StringComparison]::OrdinalIgnoreCase
    )) {
        return $pathFullPath.Substring($prefix.Length)
    }

    return $pathFullPath
}

function Test-IsExcludedPath {
    param(
        [string]$RepositoryRoot,
        [string]$Path
    )

    $relativePath = Get-RelativePath -BasePath $RepositoryRoot -Path $Path
    $segments = $relativePath.Replace("\", "/").Split("/")
    $excludedSegments = @(
        ".git",
        ".ccg",
        ".trellis",
        ".agents",
        ".codex",
        "bin",
        "obj",
        "docs",
        "scratch",
        "tools"
    )

    foreach ($segment in $segments) {
        if ($excludedSegments -contains $segment) {
            return $true
        }
    }

    return $false
}

function Get-ProjectProperty {
    param(
        [xml]$Project,
        [string]$Name
    )

    $nodes = @($Project.SelectNodes(
        "//*[local-name()='$Name']"
    ))
    if ($nodes.Count -eq 0) {
        return $null
    }

    return [string]$nodes[0].InnerText
}

function Get-ProjectReferences {
    param([xml]$Project)

    return @(
        $Project.SelectNodes(
            "//*[local-name()='ProjectReference']"
        ) |
            ForEach-Object {
                [pscustomobject]@{
                    Include = [string]$_.GetAttribute("Include")
                    Name = [IO.Path]::GetFileNameWithoutExtension(
                        [string]$_.GetAttribute("Include")
                    )
                }
            }
    )
}

function Get-PackageReferences {
    param([xml]$Project)

    return @(
        $Project.SelectNodes(
            "//*[local-name()='PackageReference']"
        ) |
            ForEach-Object {
                $version = [string]$_.GetAttribute("Version")
                if ([string]::IsNullOrWhiteSpace($version)) {
                    $versionNode = $_.SelectSingleNode(
                        "*[local-name()='Version']"
                    )
                    if ($null -ne $versionNode) {
                        $version = [string]$versionNode.InnerText
                    }
                }

                [pscustomobject]@{
                    Name = [string]$_.GetAttribute("Include")
                    Version = $version
                }
            }
    )
}

function Test-IsCrmSdkPackage {
    param([string]$Name)

    return $Name -match (
        "(?i)^Microsoft\." +
        "(?:CrmSdk|Xrm|PowerPlatform\.Dataverse)"
    )
}

if ([string]::IsNullOrWhiteSpace($RepositoryPath)) {
    $RepositoryPath = Join-Path $PSScriptRoot ".."
}

$repositoryRoot = [IO.Path]::GetFullPath($RepositoryPath)
$solutionFullPath = Get-FullPath `
    -BasePath $repositoryRoot `
    -Path $SolutionPath
$findings = New-Object System.Collections.Generic.List[object]

function Add-Finding {
    param(
        [string]$RuleId,
        [string]$Path,
        [string]$Message
    )

    [void]$findings.Add([pscustomobject]@{
        ruleId = $RuleId
        path = $Path
        message = $Message
    })
}

$legacyPaths = @(
    "SpeechMessage.Dynamics.WebApi",
    "SpeechMessage.Dynamics.SmokeTests",
    "docs\scripts\Invoke-DynamicsLiveSmoke.ps1",
    "docs\scripts\Invoke-DynamicsLiveSmoke.Tests.ps1",
    "docs\scripts\Get-DynamicsCrmWebIfdDiagnostics.ps1",
    "docs\scripts\Get-DynamicsCrmWebIfdDiagnostics.Tests.ps1"
)

$workerProjectNames = @(
    "SpeechMessage.Dynamics.Crm82Worker",
    "SpeechMessage.Dynamics.Crm91Worker"
)
$workerTestProjectBindings = [ordered]@{
    "SpeechMessage.Dynamics.Crm82Worker.Tests" =
        "SpeechMessage.Dynamics.Crm82Worker"
    "SpeechMessage.Dynamics.Crm91Worker.Tests" =
        "SpeechMessage.Dynamics.Crm91Worker"
}
$workerTestProjectNames = @($workerTestProjectBindings.Keys)
$approvedSdkProjectNames = @(
    $workerProjectNames + $workerTestProjectNames
)

foreach ($relativePath in $legacyPaths) {
    $fullPath = Get-FullPath `
        -BasePath $repositoryRoot `
        -Path $relativePath
    if (Test-Path -LiteralPath $fullPath) {
        Add-Finding `
            -RuleId "DYNBOUNDARY001" `
            -Path $relativePath `
            -Message "Retired direct-WebApi executable surface still exists."
    }
}

$requiredProjectNames = @(
    "SpeechMessage.Dynamics.Abstractions",
    "SpeechMessage.Dynamics.ControlPlane",
    "SpeechMessage.Dynamics.Crm82Worker",
    "SpeechMessage.Dynamics.Crm82Worker.Tests",
    "SpeechMessage.Dynamics.Crm91Worker",
    "SpeechMessage.Dynamics.Crm91Worker.Tests",
    "SpeechMessage.Dynamics.Embedded",
    "SpeechMessage.Dynamics.Gateway",
    "SpeechMessage.Dynamics.ProductClient",
    "SpeechMessage.Dynamics.SqlCoordinatorTestWorker",
    "SpeechMessage.Dynamics.Tests",
    "SpeechMessage.Dynamics.WorkerHost",
    "SpeechMessage.Dynamics.WorkerProtocol",
    "SpeechMessage.Dynamics.WorkerSupervisor",
    "SpeechMessage.Dynamics.WorkerTestHost"
)

$solutionProjects = @{}
if (-not (Test-Path -LiteralPath $solutionFullPath)) {
    Add-Finding `
        -RuleId "DYNBOUNDARY002" `
        -Path (Get-RelativePath `
            -BasePath $repositoryRoot `
            -Path $solutionFullPath) `
        -Message "Required solution file is missing."
}
else {
    foreach ($line in [IO.File]::ReadLines($solutionFullPath)) {
        if ($line -match (
            '^Project\("[^"]+"\)\s*=\s*' +
            '"(?<name>[^"]+)",\s*' +
            '"(?<path>[^"]+)",'
        )) {
            $solutionProjects[$Matches["name"]] = $Matches["path"]
        }
    }

    foreach ($projectName in $requiredProjectNames) {
        if (-not $solutionProjects.ContainsKey($projectName)) {
            Add-Finding `
                -RuleId "DYNBOUNDARY002" `
                -Path (Get-RelativePath `
                    -BasePath $repositoryRoot `
                    -Path $solutionFullPath) `
                -Message "Required Dynamics project is absent from the solution: $projectName."
            continue
        }

        $expectedRelativeProjectPath = (
            Join-Path $projectName ($projectName + ".csproj")
        )
        $actualRelativeProjectPath = [string]$solutionProjects[$projectName]
        if (-not [string]::Equals(
            $actualRelativeProjectPath.Replace("/", "\"),
            $expectedRelativeProjectPath.Replace("/", "\"),
            [StringComparison]::OrdinalIgnoreCase
        )) {
            Add-Finding `
                -RuleId "DYNBOUNDARY002" `
                -Path (Get-RelativePath `
                    -BasePath $repositoryRoot `
                    -Path $solutionFullPath) `
                -Message "Dynamics solution entry has an unexpected project path: $projectName."
        }
    }

    foreach ($projectName in @($solutionProjects.Keys)) {
        if (
            $projectName.StartsWith(
                "SpeechMessage.Dynamics.",
                [StringComparison]::OrdinalIgnoreCase
            ) -and
            $requiredProjectNames -notcontains $projectName
        ) {
            Add-Finding `
                -RuleId "DYNBOUNDARY003" `
                -Path (Get-RelativePath `
                    -BasePath $repositoryRoot `
                    -Path $solutionFullPath) `
                -Message "Unexpected Dynamics project remains in the solution: $projectName."
        }
    }
}

$projectFiles = @(
    Get-ChildItem `
        -LiteralPath $repositoryRoot `
        -Filter "SpeechMessage.Dynamics*.csproj" `
        -Recurse `
        -File |
        Where-Object {
            -not (Test-IsExcludedPath `
                -RepositoryRoot $repositoryRoot `
                -Path $_.FullName)
        }
)

$projects = @{}
foreach ($projectFile in $projectFiles) {
    [xml]$projectXml = Get-Content `
        -LiteralPath $projectFile.FullName `
        -Raw
    $projectName = $projectFile.BaseName
    $projects[$projectName] = [pscustomobject]@{
        Name = $projectName
        Path = $projectFile.FullName
        RelativePath = Get-RelativePath `
            -BasePath $repositoryRoot `
            -Path $projectFile.FullName
        Xml = $projectXml
        References = @(Get-ProjectReferences -Project $projectXml)
        Packages = @(Get-PackageReferences -Project $projectXml)
    }
}

foreach ($projectName in $requiredProjectNames) {
    if (-not $projects.ContainsKey($projectName)) {
        Add-Finding `
            -RuleId "DYNBOUNDARY002" `
            -Path (Join-Path $projectName ($projectName + ".csproj")) `
            -Message "Required Dynamics project file is missing."
    }
}

foreach ($project in @($projects.Values)) {
    foreach ($package in @($project.Packages)) {
        if (
            (Test-IsCrmSdkPackage -Name $package.Name) -and
            $approvedSdkProjectNames -notcontains $project.Name
        ) {
            Add-Finding `
                -RuleId "DYNBOUNDARY004" `
                -Path $project.RelativePath `
                -Message "CRM SDK package is outside the approved worker or matching worker-only test projects: $($package.Name)."
        }
    }

    $directReferences = @(
        $project.Xml.SelectNodes(
            "//*[local-name()='Reference' or local-name()='HintPath']"
        )
    )
    foreach ($reference in $directReferences) {
        $referenceText = [string]$reference.InnerText
        if ($reference.Name.LocalName -eq "Reference") {
            $referenceText += " " + [string]$reference.GetAttribute("Include")
        }

        if ($referenceText -match (
            "(?i)Microsoft\.(?:Xrm|Crm)(?:\.|,)|" +
            "Microsoft\.PowerPlatform\.Dataverse|" +
            "Dynamics 365 SDK DLL"
        )) {
            Add-Finding `
                -RuleId "DYNBOUNDARY005" `
                -Path $project.RelativePath `
                -Message "Direct CRM SDK assembly or HintPath reference is forbidden."
        }
    }
}

foreach ($project in @($projects.Values)) {
    foreach ($reference in @($project.References)) {
        $projectDirectory = Split-Path -Parent $project.Path
        $resolvedReference = [IO.Path]::GetFullPath(
            (Join-Path $projectDirectory $reference.Include)
        )
        $repositoryPrefix = (
            $repositoryRoot.TrimEnd($trimCharacters) +
            [IO.Path]::DirectorySeparatorChar
        )

        if (-not $resolvedReference.StartsWith(
            $repositoryPrefix,
            [StringComparison]::OrdinalIgnoreCase
        )) {
            Add-Finding `
                -RuleId "DYNBOUNDARY008" `
                -Path $project.RelativePath `
                -Message "ProjectReference resolves outside the repository."
        }
        elseif (-not (Test-Path -LiteralPath $resolvedReference)) {
            Add-Finding `
                -RuleId "DYNBOUNDARY008" `
                -Path $project.RelativePath `
                -Message "ProjectReference target is missing: $($reference.Name)."
        }

        $expectedWorkerReference = $null
        if ($workerTestProjectBindings.Contains($project.Name)) {
            $expectedWorkerReference = [string]$workerTestProjectBindings[
                $project.Name
            ]
        }

        if (
            $workerProjectNames -contains $reference.Name -and
            -not [string]::Equals(
                $reference.Name,
                $expectedWorkerReference,
                [StringComparison]::OrdinalIgnoreCase
            )
        ) {
            Add-Finding `
                -RuleId "DYNBOUNDARY006" `
                -Path $project.RelativePath `
                -Message "Only the matching worker-only test may reference a version-specific worker executable."
        }
    }
}

$expectedGraph = @{
    "SpeechMessage.Dynamics.Abstractions" = @()
    "SpeechMessage.Dynamics.ControlPlane" = @(
        "SpeechMessage.Dynamics.Abstractions",
        "SpeechMessage.Dynamics.WorkerSupervisor"
    )
    "SpeechMessage.Dynamics.Crm82Worker" = @(
        "SpeechMessage.Dynamics.WorkerHost",
        "SpeechMessage.Dynamics.WorkerProtocol"
    )
    "SpeechMessage.Dynamics.Crm82Worker.Tests" = @(
        "SpeechMessage.Dynamics.Crm82Worker"
    )
    "SpeechMessage.Dynamics.Crm91Worker" = @(
        "SpeechMessage.Dynamics.WorkerHost",
        "SpeechMessage.Dynamics.WorkerProtocol"
    )
    "SpeechMessage.Dynamics.Crm91Worker.Tests" = @(
        "SpeechMessage.Dynamics.Crm91Worker"
    )
    "SpeechMessage.Dynamics.Embedded" = @(
        "SpeechMessage.Dynamics.Abstractions"
    )
    "SpeechMessage.Dynamics.Gateway" = @(
        "SpeechMessage.Dynamics.Abstractions",
        "SpeechMessage.Dynamics.ControlPlane",
        "SpeechMessage.Dynamics.WorkerSupervisor"
    )
    "SpeechMessage.Dynamics.ProductClient" = @(
        "SpeechMessage.Dynamics.Abstractions"
    )
    "SpeechMessage.Dynamics.SqlCoordinatorTestWorker" = @(
        "SpeechMessage.Dynamics.ControlPlane"
    )
    "SpeechMessage.Dynamics.WorkerHost" = @(
        "SpeechMessage.Dynamics.WorkerProtocol"
    )
    "SpeechMessage.Dynamics.WorkerProtocol" = @()
    "SpeechMessage.Dynamics.WorkerSupervisor" = @(
        "SpeechMessage.Dynamics.Abstractions",
        "SpeechMessage.Dynamics.WorkerHost",
        "SpeechMessage.Dynamics.WorkerProtocol"
    )
    "SpeechMessage.Dynamics.WorkerTestHost" = @(
        "SpeechMessage.Dynamics.WorkerHost",
        "SpeechMessage.Dynamics.WorkerProtocol"
    )
}

foreach ($projectName in @($expectedGraph.Keys)) {
    if (-not $projects.ContainsKey($projectName)) {
        continue
    }

    $project = $projects[$projectName]
    $actualReferences = @(
        $project.References |
            ForEach-Object { $_.Name } |
            Sort-Object -Unique
    )
    $expectedReferences = @(
        $expectedGraph[$projectName] |
            Sort-Object -Unique
    )
    $missingReferences = @(
        $expectedReferences |
            Where-Object { $actualReferences -notcontains $_ }
    )
    $unexpectedReferences = @(
        $actualReferences |
            Where-Object { $expectedReferences -notcontains $_ }
    )

    if (
        $missingReferences.Count -gt 0 -or
        $unexpectedReferences.Count -gt 0
    ) {
        Add-Finding `
            -RuleId "DYNBOUNDARY007" `
            -Path $project.RelativePath `
            -Message (
                "ProjectReference graph drift. Missing=[" +
                ($missingReferences -join ",") +
                "] Unexpected=[" +
                ($unexpectedReferences -join ",") +
                "]."
            )
    }
}

$workerContracts = @{
    "SpeechMessage.Dynamics.Crm82Worker" = [ordered]@{
        "Microsoft.CrmSdk.XrmTooling.CoreAssembly" = "8.2.0.5"
        "Microsoft.CrmSdk.CoreAssemblies" = "8.2.0.2"
        "Microsoft.CrmSdk.Deployment" = "8.2.0.2"
        "Microsoft.CrmSdk.Workflow" = "8.2.0.2"
    }
    "SpeechMessage.Dynamics.Crm91Worker" = [ordered]@{
        "Microsoft.CrmSdk.XrmTooling.CoreAssembly" = "9.1.1.65"
        "Microsoft.CrmSdk.CoreAssemblies" = "9.0.2.60"
    }
}

foreach ($workerName in @($workerContracts.Keys)) {
    if (-not $projects.ContainsKey($workerName)) {
        continue
    }

    $worker = $projects[$workerName]
    $targetFramework = Get-ProjectProperty `
        -Project $worker.Xml `
        -Name "TargetFramework"
    $outputType = Get-ProjectProperty `
        -Project $worker.Xml `
        -Name "OutputType"
    $restoreWithLock = Get-ProjectProperty `
        -Project $worker.Xml `
        -Name "RestorePackagesWithLockFile"
    $restoreLockedMode = Get-ProjectProperty `
        -Project $worker.Xml `
        -Name "RestoreLockedMode"

    if (
        $targetFramework -ne "net48" -or
        $outputType -ne "Exe" -or
        $restoreWithLock -ne "true" -or
        $restoreLockedMode -ne "true"
    ) {
        Add-Finding `
            -RuleId "DYNBOUNDARY009" `
            -Path $worker.RelativePath `
            -Message "Worker must be an Exe targeting net48 with locked restore enabled."
    }

    $expectedPackages = $workerContracts[$workerName]
    foreach ($expectedPackage in $expectedPackages.GetEnumerator()) {
        $matches = @(
            $worker.Packages |
                Where-Object {
                    $_.Name -eq [string]$expectedPackage.Key
                }
        )
        if (
            $matches.Count -ne 1 -or
            $matches[0].Version -ne [string]$expectedPackage.Value
        ) {
            Add-Finding `
                -RuleId "DYNBOUNDARY009" `
                -Path $worker.RelativePath `
                -Message "Worker CRM package version is missing or drifted: $($expectedPackage.Key)."
        }
    }

    foreach ($package in @($worker.Packages)) {
        if (
            (Test-IsCrmSdkPackage -Name $package.Name) -and
            -not $expectedPackages.Contains($package.Name)
        ) {
            Add-Finding `
                -RuleId "DYNBOUNDARY009" `
                -Path $worker.RelativePath `
                -Message "Worker has an unapproved CRM SDK package: $($package.Name)."
        }
    }

    $workerDirectory = Split-Path -Parent $worker.Path
    $lockPath = Join-Path $workerDirectory "packages.lock.json"
    if (-not (Test-Path -LiteralPath $lockPath)) {
        Add-Finding `
            -RuleId "DYNBOUNDARY010" `
            -Path (Get-RelativePath `
                -BasePath $repositoryRoot `
                -Path $lockPath) `
            -Message "Worker packages.lock.json is missing."
        continue
    }

    $lock = Get-Content -LiteralPath $lockPath -Raw |
        ConvertFrom-Json
    $dependenciesProperty = $lock.PSObject.Properties["dependencies"]
    $frameworkProperty = $null
    if ($null -ne $dependenciesProperty) {
        $frameworkProperty = $dependenciesProperty.Value.PSObject.Properties[
            ".NETFramework,Version=v4.8"
        ]
    }

    if ($null -eq $frameworkProperty) {
        Add-Finding `
            -RuleId "DYNBOUNDARY010" `
            -Path (Get-RelativePath `
                -BasePath $repositoryRoot `
                -Path $lockPath) `
            -Message "Worker lock file lacks the net48 dependency graph."
        continue
    }

    foreach ($expectedPackage in $expectedPackages.GetEnumerator()) {
        $packageProperty = $frameworkProperty.Value.PSObject.Properties[
            [string]$expectedPackage.Key
        ]
        $resolvedVersion = $null
        if ($null -ne $packageProperty) {
            $resolvedProperty = $packageProperty.Value.PSObject.Properties[
                "resolved"
            ]
            if ($null -ne $resolvedProperty) {
                $resolvedVersion = [string]$resolvedProperty.Value
            }
        }

        if ($resolvedVersion -ne [string]$expectedPackage.Value) {
            Add-Finding `
                -RuleId "DYNBOUNDARY010" `
                -Path (Get-RelativePath `
                    -BasePath $repositoryRoot `
                    -Path $lockPath) `
                -Message "Worker lock file version drift: $($expectedPackage.Key)."
        }
    }
}

$workerTestContracts = @{
    "SpeechMessage.Dynamics.Crm82Worker.Tests" = [ordered]@{
        "Microsoft.CrmSdk.XrmTooling.CoreAssembly" = "8.2.0.5"
        "Microsoft.CrmSdk.CoreAssemblies" = "8.2.0.2"
    }
    "SpeechMessage.Dynamics.Crm91Worker.Tests" = [ordered]@{
        "Microsoft.CrmSdk.XrmTooling.CoreAssembly" = "9.1.1.65"
        "Microsoft.CrmSdk.CoreAssemblies" = "9.0.2.60"
    }
}

foreach ($testProjectName in @($workerTestContracts.Keys)) {
    if (-not $projects.ContainsKey($testProjectName)) {
        continue
    }

    $testProject = $projects[$testProjectName]
    $targetFramework = Get-ProjectProperty `
        -Project $testProject.Xml `
        -Name "TargetFramework"
    $isTestProject = Get-ProjectProperty `
        -Project $testProject.Xml `
        -Name "IsTestProject"
    $restoreWithLock = Get-ProjectProperty `
        -Project $testProject.Xml `
        -Name "RestorePackagesWithLockFile"
    $restoreLockedMode = Get-ProjectProperty `
        -Project $testProject.Xml `
        -Name "RestoreLockedMode"

    if (
        $targetFramework -ne "net48" -or
        $isTestProject -ne "true" -or
        $restoreWithLock -ne "true" -or
        $restoreLockedMode -ne "true"
    ) {
        Add-Finding `
            -RuleId "DYNBOUNDARY014" `
            -Path $testProject.RelativePath `
            -Message "Worker-only tests must target net48 with locked restore enabled."
    }

    $expectedPackages = $workerTestContracts[$testProjectName]
    foreach ($expectedPackage in $expectedPackages.GetEnumerator()) {
        $matches = @(
            $testProject.Packages |
                Where-Object {
                    $_.Name -eq [string]$expectedPackage.Key
                }
        )
        if (
            $matches.Count -ne 1 -or
            $matches[0].Version -ne [string]$expectedPackage.Value
        ) {
            Add-Finding `
                -RuleId "DYNBOUNDARY014" `
                -Path $testProject.RelativePath `
                -Message "Worker-only test CRM package version is missing or drifted: $($expectedPackage.Key)."
        }
    }

    foreach ($package in @($testProject.Packages)) {
        if (
            (Test-IsCrmSdkPackage -Name $package.Name) -and
            -not $expectedPackages.Contains($package.Name)
        ) {
            Add-Finding `
                -RuleId "DYNBOUNDARY014" `
                -Path $testProject.RelativePath `
                -Message "Worker-only test has an unapproved CRM SDK package: $($package.Name)."
        }
    }

    $testDirectory = Split-Path -Parent $testProject.Path
    $lockPath = Join-Path $testDirectory "packages.lock.json"
    if (-not (Test-Path -LiteralPath $lockPath)) {
        Add-Finding `
            -RuleId "DYNBOUNDARY014" `
            -Path (Get-RelativePath `
                -BasePath $repositoryRoot `
                -Path $lockPath) `
            -Message "Worker-only test packages.lock.json is missing."
        continue
    }

    $lock = Get-Content -LiteralPath $lockPath -Raw |
        ConvertFrom-Json
    $frameworkProperty = $lock.dependencies.PSObject.Properties[
        ".NETFramework,Version=v4.8"
    ]
    if ($null -eq $frameworkProperty) {
        Add-Finding `
            -RuleId "DYNBOUNDARY014" `
            -Path (Get-RelativePath `
                -BasePath $repositoryRoot `
                -Path $lockPath) `
            -Message "Worker-only test lock file lacks the net48 dependency graph."
        continue
    }

    foreach ($expectedPackage in $expectedPackages.GetEnumerator()) {
        $packageProperty = $frameworkProperty.Value.PSObject.Properties[
            [string]$expectedPackage.Key
        ]
        $resolvedVersion = $null
        if ($null -ne $packageProperty) {
            $resolvedProperty = $packageProperty.Value.PSObject.Properties[
                "resolved"
            ]
            if ($null -ne $resolvedProperty) {
                $resolvedVersion = [string]$resolvedProperty.Value
            }
        }

        if ($resolvedVersion -ne [string]$expectedPackage.Value) {
            Add-Finding `
                -RuleId "DYNBOUNDARY014" `
                -Path (Get-RelativePath `
                    -BasePath $repositoryRoot `
                    -Path $lockPath) `
                -Message "Worker-only test lock file version drift: $($expectedPackage.Key)."
        }
    }
}

$neutralProjectNames = @(
    "SpeechMessage.Dynamics.Abstractions",
    "SpeechMessage.Dynamics.ControlPlane",
    "SpeechMessage.Dynamics.Embedded",
    "SpeechMessage.Dynamics.Gateway",
    "SpeechMessage.Dynamics.ProductClient",
    "SpeechMessage.Dynamics.SqlCoordinatorTestWorker",
    "SpeechMessage.Dynamics.WorkerHost",
    "SpeechMessage.Dynamics.WorkerProtocol",
    "SpeechMessage.Dynamics.WorkerSupervisor",
    "SpeechMessage.Dynamics.WorkerTestHost"
)
$sdkSourcePattern = (
    "(?im)^\s*(?:global\s+)?using\s+" +
    "(?:global::)?Microsoft\.(?:Xrm|Crm)(?:\.|;)|" +
    "Microsoft\.PowerPlatform\.Dataverse|" +
    "Microsoft\.CrmSdk"
)
$transportPatterns = @(
    "OrganizationWebApiBaseUri",
    "AddSpeechMessageDynamicsWebApi",
    "AddSpeechMessageDynamicsProfiles",
    "SpeechMessage.Dynamics.WebApi",
    "DynamicsWebApiOptions",
    "AdfsOAuth"
)
$approvedApiDataRejections = @(
    "SpeechMessage.Dynamics.ProductClient\Configuration\GatewayProductDynamicsOptionsValidator.cs",
    "SpeechMessage.Dynamics.ControlPlane\Capacity\CapacityKeys.cs",
    "SpeechMessage.Dynamics.ControlPlane\Runtime\DynamicsProfileDefinition.cs"
)

foreach ($projectName in $neutralProjectNames) {
    $projectDirectory = Join-Path $repositoryRoot $projectName
    if (-not (Test-Path -LiteralPath $projectDirectory)) {
        continue
    }

    $sourceFiles = @(
        Get-ChildItem `
            -LiteralPath $projectDirectory `
            -Recurse `
            -File |
            Where-Object {
                -not (Test-IsExcludedPath `
                    -RepositoryRoot $repositoryRoot `
                    -Path $_.FullName) -and
                $_.Extension -in @(".cs", ".json", ".csproj")
            }
    )

    foreach ($sourceFile in $sourceFiles) {
        $relativePath = Get-RelativePath `
            -BasePath $repositoryRoot `
            -Path $sourceFile.FullName
        $text = Get-Content -LiteralPath $sourceFile.FullName -Raw

        if (
            $sourceFile.Extension -eq ".cs" -and
            $text -match $sdkSourcePattern
        ) {
            Add-Finding `
                -RuleId "DYNBOUNDARY011" `
                -Path $relativePath `
                -Message "CRM SDK namespace or package leaked into an SDK-free project."
        }

        foreach ($pattern in $transportPatterns) {
            if ($text.IndexOf(
                $pattern,
                [StringComparison]::OrdinalIgnoreCase
            ) -ge 0) {
                Add-Finding `
                    -RuleId "DYNBOUNDARY012" `
                    -Path $relativePath `
                    -Message "Retired direct-WebApi contract remains in the official route."
            }
        }

        if (
            $text.IndexOf(
                "/api/data/",
                [StringComparison]::OrdinalIgnoreCase
            ) -ge 0 -and
            $approvedApiDataRejections -notcontains $relativePath
        ) {
            Add-Finding `
                -RuleId "DYNBOUNDARY012" `
                -Path $relativePath `
                -Message "Direct Web API route text is outside the exact rejection allowlist."
        }
    }
}

foreach ($workerName in $workerProjectNames) {
    $workerDirectory = Join-Path $repositoryRoot $workerName
    if (-not (Test-Path -LiteralPath $workerDirectory)) {
        continue
    }

    foreach ($sourceFile in @(
        Get-ChildItem `
            -LiteralPath $workerDirectory `
            -Recurse `
            -File |
            Where-Object {
                -not (Test-IsExcludedPath `
                    -RepositoryRoot $repositoryRoot `
                    -Path $_.FullName) -and
                $_.Extension -in @(".cs", ".json", ".csproj")
            }
    )) {
        $text = Get-Content -LiteralPath $sourceFile.FullName -Raw
        if ($text -match "(?i)/api/data/|DynamicsWebApi|AdfsOAuth|HttpClient") {
            Add-Finding `
                -RuleId "DYNBOUNDARY013" `
                -Path (Get-RelativePath `
                    -BasePath $repositoryRoot `
                    -Path $sourceFile.FullName) `
                -Message "Official worker contains a direct Web API transport surface."
        }
    }
}

$result = [pscustomobject]@{
    schemaVersion = "2026-08-02.dynamics-worker-boundary-result.v1"
    generatedAt = (Get-Date).ToString("o")
    scope = "SpeechMessage.Dynamics worker subgraph"
    repositoryRoot = $repositoryRoot
    solutionPath = Get-RelativePath `
        -BasePath $repositoryRoot `
        -Path $solutionFullPath
    findingCount = $findings.Count
    findings = $findings.ToArray()
}

if ($Json) {
    $result | ConvertTo-Json -Depth 8
}
else {
    Write-Host "Dynamics official-worker boundary"
    Write-Host "Scope: $($result.scope)"
    Write-Host "Findings: $($result.findingCount)"

    foreach ($group in (
        $findings |
            Group-Object -Property ruleId |
            Sort-Object -Property Name
    )) {
        Write-Host ("{0}: {1}" -f $group.Name, $group.Count)
    }

    if (-not $SummaryOnly) {
        foreach ($finding in $findings) {
            Write-Host (
                "{0}: [{1}] {2}" -f
                $finding.path,
                $finding.ruleId,
                $finding.message
            )
        }
    }
}

if ($findings.Count -gt 0) {
    exit 1
}

exit 0

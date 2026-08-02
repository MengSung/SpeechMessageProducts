[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$gatePath = Join-Path $PSScriptRoot "Verify-DynamicsWorkerBoundary.ps1"

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

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Invoke-BoundaryGate {
    param([string]$FixtureRoot)

    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        $gatePath,
        "-RepositoryPath",
        $FixtureRoot,
        "-SolutionPath",
        "SpeechMessageProducts.sln",
        "-Json"
    )
    $output = @(& powershell.exe @arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $text = $output -join [Environment]::NewLine
    $result = $null

    try {
        $result = $text | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "Boundary gate did not return JSON. ExitCode=$exitCode Output=$text"
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Result = $result
    }
}

function New-CleanFixture {
    $fixtureRoot = Join-Path (
        [IO.Path]::GetTempPath()
    ) (
        "speechmessage-dynamics-worker-boundary-" +
        [Guid]::NewGuid().ToString("N")
    )
    [void](New-Item -ItemType Directory -Path $fixtureRoot)

    Copy-Item -LiteralPath (
        Join-Path $repositoryRoot "SpeechMessageProducts.sln"
    ) -Destination (
        Join-Path $fixtureRoot "SpeechMessageProducts.sln"
    )

    foreach ($projectName in $requiredProjectNames) {
        $sourceDirectory = Join-Path $repositoryRoot $projectName
        $targetDirectory = Join-Path $fixtureRoot $projectName
        [void](New-Item -ItemType Directory -Path $targetDirectory)

        Copy-Item -LiteralPath (
            Join-Path $sourceDirectory ($projectName + ".csproj")
        ) -Destination (
            Join-Path $targetDirectory ($projectName + ".csproj")
        )

        $lockPath = Join-Path $sourceDirectory "packages.lock.json"
        if (Test-Path -LiteralPath $lockPath) {
            Copy-Item -LiteralPath $lockPath -Destination (
                Join-Path $targetDirectory "packages.lock.json"
            )
        }
    }

    return $fixtureRoot
}

function Add-PackageReference {
    param(
        [string]$ProjectPath,
        [string]$Name,
        [string]$Version
    )

    [xml]$project = Get-Content -LiteralPath $ProjectPath -Raw
    $itemGroup = @($project.Project.ItemGroup)[0]
    $packageReference = $project.CreateElement("PackageReference")
    [void]$packageReference.SetAttribute("Include", $Name)
    [void]$packageReference.SetAttribute("Version", $Version)
    [void]$itemGroup.AppendChild($packageReference)
    $project.Save($ProjectPath)
}

function Add-ProjectReference {
    param(
        [string]$ProjectPath,
        [string]$Reference
    )

    [xml]$project = Get-Content -LiteralPath $ProjectPath -Raw
    $itemGroup = @($project.Project.ItemGroup)[-1]
    $projectReference = $project.CreateElement("ProjectReference")
    [void]$projectReference.SetAttribute("Include", $Reference)
    [void]$itemGroup.AppendChild($projectReference)
    $project.Save($ProjectPath)
}

function Invoke-FixtureCase {
    param(
        [string]$Name,
        [scriptblock]$Mutate,
        [string]$ExpectedRuleId
    )

    $fixtureRoot = New-CleanFixture
    try {
        & $Mutate $fixtureRoot
        $run = Invoke-BoundaryGate -FixtureRoot $fixtureRoot

        Assert-True -Condition (
            $run.ExitCode -ne 0
        ) -Message (
            "$Name should fail the boundary gate."
        )
        Assert-True -Condition (
            @($run.Result.findings.ruleId) -contains $ExpectedRuleId
        ) -Message (
            "$Name should report rule $ExpectedRuleId."
        )
    }
    finally {
        if (Test-Path -LiteralPath $fixtureRoot) {
            Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
        }
    }
}

$cleanFixture = New-CleanFixture
try {
    $cleanRun = Invoke-BoundaryGate -FixtureRoot $cleanFixture
    Assert-True -Condition (
        $cleanRun.ExitCode -eq 0
    ) -Message (
        "A clean worker-boundary fixture should pass."
    )
    Assert-True -Condition (
        [int]$cleanRun.Result.findingCount -eq 0
    ) -Message (
        "A clean worker-boundary fixture should have no findings."
    )
}
finally {
    if (Test-Path -LiteralPath $cleanFixture) {
        Remove-Item -LiteralPath $cleanFixture -Recurse -Force
    }
}

Invoke-FixtureCase -Name "SDK package in Gateway" -ExpectedRuleId "DYNBOUNDARY004" -Mutate {
    param($fixtureRoot)
    Add-PackageReference -ProjectPath (
        Join-Path $fixtureRoot "SpeechMessage.Dynamics.Gateway\SpeechMessage.Dynamics.Gateway.csproj"
    ) -Name "Microsoft.CrmSdk.CoreAssemblies" -Version "9.0.2.60"
}

Invoke-FixtureCase -Name "Gateway references a worker executable" -ExpectedRuleId "DYNBOUNDARY006" -Mutate {
    param($fixtureRoot)
    Add-ProjectReference -ProjectPath (
        Join-Path $fixtureRoot "SpeechMessage.Dynamics.Gateway\SpeechMessage.Dynamics.Gateway.csproj"
    ) -Reference "..\SpeechMessage.Dynamics.Crm91Worker\SpeechMessage.Dynamics.Crm91Worker.csproj"
}

Invoke-FixtureCase -Name "Ordinary tests reference an SDK package" -ExpectedRuleId "DYNBOUNDARY004" -Mutate {
    param($fixtureRoot)
    Add-PackageReference -ProjectPath (
        Join-Path $fixtureRoot "SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj"
    ) -Name "Microsoft.CrmSdk.CoreAssemblies" -Version "9.0.2.60"
}

Invoke-FixtureCase -Name "CE 8.2 worker tests reference the CE 9.1 worker" -ExpectedRuleId "DYNBOUNDARY006" -Mutate {
    param($fixtureRoot)
    $projectPath = Join-Path (
        $fixtureRoot
    ) "SpeechMessage.Dynamics.Crm82Worker.Tests\SpeechMessage.Dynamics.Crm82Worker.Tests.csproj"
    [xml]$project = Get-Content -LiteralPath $projectPath -Raw
    $reference = @($project.SelectNodes(
        "//*[local-name()='ProjectReference']"
    )) | Select-Object -First 1
    [void]$reference.SetAttribute(
        "Include",
        "..\SpeechMessage.Dynamics.Crm91Worker\SpeechMessage.Dynamics.Crm91Worker.csproj"
    )
    $project.Save($projectPath)
}

Invoke-FixtureCase -Name "CE 9.1 worker test package version drift" -ExpectedRuleId "DYNBOUNDARY014" -Mutate {
    param($fixtureRoot)
    $projectPath = Join-Path (
        $fixtureRoot
    ) "SpeechMessage.Dynamics.Crm91Worker.Tests\SpeechMessage.Dynamics.Crm91Worker.Tests.csproj"
    [xml]$project = Get-Content -LiteralPath $projectPath -Raw
    $package = @($project.SelectNodes(
        "//*[local-name()='PackageReference']"
    )) |
        Where-Object {
            $_.GetAttribute("Include") -eq
                "Microsoft.CrmSdk.XrmTooling.CoreAssembly"
        } |
        Select-Object -First 1
    [void]$package.SetAttribute("Version", "0.0.0")
    $project.Save($projectPath)
}

Invoke-FixtureCase -Name "Worker package version drift" -ExpectedRuleId "DYNBOUNDARY009" -Mutate {
    param($fixtureRoot)
    $projectPath = Join-Path (
        $fixtureRoot
    ) "SpeechMessage.Dynamics.Crm91Worker\SpeechMessage.Dynamics.Crm91Worker.csproj"
    [xml]$project = Get-Content -LiteralPath $projectPath -Raw
    $package = @($project.SelectNodes(
        "//*[local-name()='PackageReference']"
    )) |
        Where-Object {
            $_.GetAttribute("Include") -eq
                "Microsoft.CrmSdk.XrmTooling.CoreAssembly"
        } |
        Select-Object -First 1
    [void]$package.SetAttribute("Version", "0.0.0")
    $project.Save($projectPath)
}

Invoke-FixtureCase -Name "SDK namespace leaks into protocol" -ExpectedRuleId "DYNBOUNDARY011" -Mutate {
    param($fixtureRoot)
    [IO.File]::WriteAllText(
        (Join-Path $fixtureRoot "SpeechMessage.Dynamics.WorkerProtocol\Leak.cs"),
        "using Microsoft.Xrm.Sdk;" + [Environment]::NewLine
    )
}

Invoke-FixtureCase -Name "Legacy WebApi directory returns" -ExpectedRuleId "DYNBOUNDARY001" -Mutate {
    param($fixtureRoot)
    [void](New-Item -ItemType Directory -Path (
        Join-Path $fixtureRoot "SpeechMessage.Dynamics.WebApi"
    ))
}

Write-Host "All Dynamics worker boundary verifier tests passed."

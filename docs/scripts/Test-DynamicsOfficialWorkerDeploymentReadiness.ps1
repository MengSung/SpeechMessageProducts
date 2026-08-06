<#
.SYNOPSIS
Performs a local-only, sanitized readiness check before generating Official
Dynamics Worker deployment artifacts.

.DESCRIPTION
This probe validates a published Worker manifest and, when supplied, a
separately approved profile-input document for exactly CE 8.2 and CE 9.1. It
verifies the pinned Worker artifacts, validates the profile/authentication
shape, compares the current execution identity to the explicitly supplied
target identity, and checks generic Credential Manager target presence without
reading a credential blob. `-InventoryOnly` intentionally stops before profile
input and credential checks, so an operator can collect a safe local inventory
without first creating a profile file. It never starts Gateway or a Worker,
creates deployment files, invokes the deployment generator, or sends a network
request.

The emitted evidence intentionally excludes paths, endpoints, Organization
IDs, credential references, execution identities, passwords, tokens, cookies,
raw command output, and exception text. A no-go result is expected until every
deployment-owned input is available under the target service identity.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ManifestPath,

    [Parameter(Mandatory = $false)]
    [string] $ProfileInputPath,

    [Parameter(Mandatory = $true)]
    [string] $ExpectedExecutionIdentity,

    [switch] $InventoryOnly,

    [switch] $Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)
Add-Type -AssemblyName System.Runtime.Serialization -ErrorAction Stop

$maximumManifestBytes = 256 * 1024
$maximumProfileInputBytes = 128 * 1024
$utf8NoBom = [Text.UTF8Encoding]::new($false, $true)

function Add-Reason {
    param(
        [System.Collections.Generic.List[string]] $Reasons,
        [string] $Reason
    )

    if (-not $Reasons.Contains($Reason)) {
        [void]$Reasons.Add($Reason)
    }
}

function Test-SafeIdentifier {
    param(
        [string] $Value,
        [int] $MaximumLength,
        [switch] $AllowDot
    )

    if ([string]::IsNullOrEmpty($Value) -or $Value.Length -gt $MaximumLength) {
        return $false
    }

    if (-not (($Value[0] -ge 'a' -and $Value[0] -le 'z') -or
              ($Value[0] -ge 'A' -and $Value[0] -le 'Z') -or
              ($Value[0] -ge '0' -and $Value[0] -le '9'))) {
        return $false
    }

    if (-not (($Value[$Value.Length - 1] -ge 'a' -and $Value[$Value.Length - 1] -le 'z') -or
              ($Value[$Value.Length - 1] -ge 'A' -and $Value[$Value.Length - 1] -le 'Z') -or
              ($Value[$Value.Length - 1] -ge '0' -and $Value[$Value.Length - 1] -le '9'))) {
        return $false
    }

    foreach ($character in $Value.ToCharArray()) {
        if (($character -ge 'a' -and $character -le 'z') -or
            ($character -ge 'A' -and $character -le 'Z') -or
            ($character -ge '0' -and $character -le '9') -or
            $character -eq '-' -or
            $character -eq '_' -or
            ($AllowDot -and $character -eq '.')) {
            continue
        }

        return $false
    }

    return $true
}

function Test-NonPlaceholderGuid {
    param([string] $Value)

    $parsed = [Guid]::Empty
    if (-not [Guid]::TryParseExact($Value, 'D', [ref]$parsed)) {
        return $false
    }

    $bytes = $parsed.ToByteArray()
    try {
        if ($parsed -eq [Guid]::Empty) {
            return $false
        }

        return @($bytes | Select-Object -Unique).Count -gt 1
    }
    finally {
        [Array]::Clear($bytes, 0, $bytes.Length)
    }
}

function Test-SafeHttpsUri {
    param([string] $Value)

    $uri = $null
    if ([string]::IsNullOrWhiteSpace($Value) -or
        -not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$uri)) {
        return $false
    }

    return [string]::Equals($uri.Scheme, [Uri]::UriSchemeHttps, [StringComparison]::OrdinalIgnoreCase) -and
        $uri.HostNameType -eq [UriHostNameType]::Dns -and
        [string]::IsNullOrEmpty($uri.UserInfo) -and
        [string]::IsNullOrEmpty($uri.Query) -and
        [string]::IsNullOrEmpty($uri.Fragment)
}

function Assert-NoDuplicateJsonObjectProperties {
    param([Xml.XmlElement] $Element)

    $children = @($Element.ChildNodes | Where-Object { $_ -is [Xml.XmlElement] })
    if ([string]::Equals(
            $Element.GetAttribute('type'),
            'object',
            [StringComparison]::Ordinal)) {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($child in $children) {
            if (-not $names.Add($child.LocalName)) {
                throw 'duplicate-json-property'
            }
        }
    }

    foreach ($child in $children) {
        Assert-NoDuplicateJsonObjectProperties -Element $child
    }
}

function Read-StrictJsonDocument {
    param(
        [string] $Path,
        [int] $MaximumBytes
    )

    $bytes = $null
    $reader = $null
    $text = $null
    try {
        $resolvedPath = [IO.Path]::GetFullPath($Path)
        if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
            throw 'file-not-found'
        }

        $length = (Get-Item -LiteralPath $resolvedPath -Force).Length
        if ($length -lt 1 -or $length -gt $MaximumBytes) {
            throw 'file-size-invalid'
        }

        $bytes = [IO.File]::ReadAllBytes($resolvedPath)
        if ($bytes.Length -ne $length -or
            ($bytes.Length -ge 3 -and
             $bytes[0] -eq 0xEF -and
             $bytes[1] -eq 0xBB -and
             $bytes[2] -eq 0xBF)) {
            throw 'file-encoding-invalid'
        }

        $quotas = [Xml.XmlDictionaryReaderQuotas]::new()
        $quotas.MaxDepth = 32
        $quotas.MaxStringContentLength = $MaximumBytes
        $quotas.MaxArrayLength = $MaximumBytes
        $quotas.MaxBytesPerRead = [Math]::Min(4096, $MaximumBytes)
        $quotas.MaxNameTableCharCount = $MaximumBytes
        $reader = [Runtime.Serialization.Json.JsonReaderWriterFactory]::CreateJsonReader(
            $bytes,
            0,
            $bytes.Length,
            [Text.Encoding]::UTF8,
            $quotas,
            $null)
        $document = [Xml.XmlDocument]::new()
        $document.PreserveWhitespace = $false
        $document.Load($reader)
        if ($null -eq $document.DocumentElement -or
            -not [string]::Equals(
                $document.DocumentElement.GetAttribute('type'),
                'object',
                [StringComparison]::Ordinal)) {
            throw 'json-root-invalid'
        }

        Assert-NoDuplicateJsonObjectProperties -Element $document.DocumentElement
        $text = $utf8NoBom.GetString($bytes)
        return $text | ConvertFrom-Json -ErrorAction Stop
    }
    finally {
        if ($null -ne $reader) {
            $reader.Dispose()
        }
        if ($null -ne $bytes) {
            [Array]::Clear($bytes, 0, $bytes.Length)
        }
        $text = $null
    }
}

function Get-RequiredString {
    param(
        [object] $Object,
        [string] $PropertyName,
        [int] $MaximumLength
    )

    if ($null -eq $Object) {
        throw 'object-missing'
    }

    $property = @($Object.PSObject.Properties | Where-Object { $_.Name -ceq $PropertyName })
    if ($property.Count -ne 1 -or -not ($property[0].Value -is [string])) {
        throw 'required-string-missing'
    }

    $value = [string]$property[0].Value
    if ([string]::IsNullOrWhiteSpace($value) -or
        $value.Length -gt $MaximumLength -or
        -not [string]::Equals($value, $value.Trim(), [StringComparison]::Ordinal)) {
        throw 'required-string-invalid'
    }

    return $value
}

function Assert-ExactProperties {
    param(
        [object] $Object,
        [string[]] $Expected
    )

    if ($null -eq $Object) {
        throw 'object-missing'
    }

    $actual = @($Object.PSObject.Properties | ForEach-Object { $_.Name })
    if ($actual.Count -ne $Expected.Count) {
        throw 'object-shape-invalid'
    }

    foreach ($name in $Expected) {
        if ($actual -cnotcontains $name) {
            throw 'object-shape-invalid'
        }
    }
}

function Get-ProfileInputs {
    param([object] $Document)

    Assert-ExactProperties -Object $Document -Expected @('schemaVersion', 'profiles')
    if (($Document.schemaVersion -isnot [int] -and $Document.schemaVersion -isnot [long]) -or
        $Document.schemaVersion -ne 1) {
        throw 'profile-schema-version-invalid'
    }

    $profiles = @($Document.profiles)
    if ($profiles.Count -ne 2) {
        throw 'profile-count-invalid'
    }

    $expectedByAlias = @{
        crm82 = [pscustomobject]@{ CeVersion = '8.2'; WorkerKind = 'OfficialCrm82Worker' }
        crm91 = [pscustomobject]@{ CeVersion = '9.1'; WorkerKind = 'OfficialCrm91Worker' }
    }
    $seenAliases = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $result = [Collections.Generic.List[object]]::new(2)
    foreach ($profile in $profiles) {
        Assert-ExactProperties -Object $profile -Expected @(
            'profileAlias',
            'workerKind',
            'packageLockId',
            'profileGenerationId',
            'organizationBaseUri',
            'organizationName',
            'expectedOrganizationId',
            'authentication',
            'identity'
        )

        $alias = Get-RequiredString -Object $profile -PropertyName 'profileAlias' -MaximumLength 128
        $workerKind = Get-RequiredString -Object $profile -PropertyName 'workerKind' -MaximumLength 64
        $packageLockId = Get-RequiredString -Object $profile -PropertyName 'packageLockId' -MaximumLength 128
        $generationId = Get-RequiredString -Object $profile -PropertyName 'profileGenerationId' -MaximumLength 128
        $organizationBaseUri = Get-RequiredString -Object $profile -PropertyName 'organizationBaseUri' -MaximumLength 2048
        $organizationName = Get-RequiredString -Object $profile -PropertyName 'organizationName' -MaximumLength 100
        $organizationId = Get-RequiredString -Object $profile -PropertyName 'expectedOrganizationId' -MaximumLength 36
        $authentication = Get-RequiredString -Object $profile -PropertyName 'authentication' -MaximumLength 32

        if (-not $expectedByAlias.ContainsKey($alias) -or
            -not $seenAliases.Add($alias) -or
            -not (Test-SafeIdentifier -Value $alias -MaximumLength 128) -or
            -not (Test-SafeIdentifier -Value $workerKind -MaximumLength 64) -or
            -not (Test-SafeIdentifier -Value $packageLockId -MaximumLength 128 -AllowDot) -or
            -not (Test-SafeIdentifier -Value $generationId -MaximumLength 128 -AllowDot) -or
            -not (Test-SafeIdentifier -Value $organizationName -MaximumLength 100) -or
            -not (Test-SafeHttpsUri -Value $organizationBaseUri) -or
            -not (Test-NonPlaceholderGuid -Value $organizationId) -or
            -not [string]::Equals($workerKind, $expectedByAlias[$alias].WorkerKind, [StringComparison]::Ordinal) -or
            ($authentication -cne 'ActiveDirectory' -and $authentication -cne 'Ifd')) {
            throw 'profile-value-invalid'
        }

        $identity = $profile.identity
        $identityMode = Get-RequiredString -Object $identity -PropertyName 'mode' -MaximumLength 64
        $credentialReference = $null
        switch ($identityMode) {
            'HostIdentity' {
                Assert-ExactProperties -Object $identity -Expected @('mode')
                if ($authentication -cne 'ActiveDirectory') {
                    throw 'identity-shape-invalid'
                }
            }
            'WindowsCredentialReference' {
                if ($authentication -ceq 'Ifd') {
                    Assert-ExactProperties -Object $identity -Expected @('mode', 'reference', 'homeRealm')
                    $homeRealm = Get-RequiredString -Object $identity -PropertyName 'homeRealm' -MaximumLength 2048
                    if (-not (Test-SafeHttpsUri -Value $homeRealm)) {
                        throw 'identity-value-invalid'
                    }
                }
                else {
                    Assert-ExactProperties -Object $identity -Expected @('mode', 'reference')
                }

                $credentialReference = Get-RequiredString -Object $identity -PropertyName 'reference' -MaximumLength 256
                if (-not (Test-SafeIdentifier -Value $credentialReference -MaximumLength 256 -AllowDot)) {
                    throw 'identity-value-invalid'
                }
            }
            default {
                throw 'identity-mode-invalid'
            }
        }

        $result.Add([pscustomobject]@{
            ProfileAlias = $alias
            CeVersion = $expectedByAlias[$alias].CeVersion
            WorkerKind = $workerKind
            PackageLockId = $packageLockId
            CredentialReference = $credentialReference
        })
    }

    return @($result)
}

function Get-ArtifactStates {
    param([object] $Manifest)

    $workers = @($Manifest.workers)
    if ($workers.Count -ne 2 -or $Manifest.featureGateMustRemainDisabled -ne $true) {
        throw 'manifest-shape-invalid'
    }

    $manifestDirectory = Split-Path -Parent ([IO.Path]::GetFullPath($ManifestPath))
    $manifestPrefix = $manifestDirectory.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $states = @{}
    foreach ($expected in @(
        [pscustomobject]@{ WorkerKind = 'OfficialCrm82Worker'; CeVersion = '8.2'; ExecutableName = 'SpeechMessage.Dynamics.Crm82Worker.exe' },
        [pscustomobject]@{ WorkerKind = 'OfficialCrm91Worker'; CeVersion = '9.1'; ExecutableName = 'SpeechMessage.Dynamics.Crm91Worker.exe' }
    )) {
        $worker = @($workers | Where-Object {
            $_.workerKind -ceq $expected.WorkerKind -and $_.ceVersion -ceq $expected.CeVersion
        })
        if ($worker.Count -ne 1) {
            throw 'manifest-worker-invalid'
        }

        $packageLockId = Get-RequiredString -Object $worker[0] -PropertyName 'packageLockId' -MaximumLength 128
        $relativeExecutablePath = Get-RequiredString -Object $worker[0] -PropertyName 'relativeExecutablePath' -MaximumLength 512
        $expectedHash = Get-RequiredString -Object $worker[0] -PropertyName 'sha256' -MaximumLength 64
        $isRootedExecutablePath = [IO.Path]::IsPathRooted($relativeExecutablePath) -or
            $relativeExecutablePath -match '^[A-Za-z]:'
        if (-not (Test-SafeIdentifier -Value $packageLockId -MaximumLength 128 -AllowDot) -or
            $isRootedExecutablePath -or
            $relativeExecutablePath.Contains('..') -or
            -not [string]::Equals(
                [IO.Path]::GetFileName($relativeExecutablePath),
                $expected.ExecutableName,
                [StringComparison]::OrdinalIgnoreCase) -or
            $expectedHash -notmatch '^[0-9A-Fa-f]{64}$' -or
            $expectedHash -match '^0{64}$') {
            throw 'manifest-worker-invalid'
        }

        $executablePath = [IO.Path]::GetFullPath((Join-Path $manifestDirectory $relativeExecutablePath))
        if (-not $executablePath.StartsWith($manifestPrefix, [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
            throw 'worker-artifact-unavailable'
        }

        $actualHash = (Get-FileHash -LiteralPath $executablePath -Algorithm SHA256).Hash
        if (-not [string]::Equals($actualHash, $expectedHash, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'worker-artifact-unavailable'
        }

        $states[$expected.WorkerKind] = [pscustomobject]@{
            PackageLockId = $packageLockId
            Valid = $true
        }
    }

    return $states
}

function Get-ResolvableCredentialReferences {
    param([string[]] $CredentialReferences)

    $found = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    if ($CredentialReferences.Count -eq 0) {
        return $found
    }

    $command = Get-Command 'cmdkey.exe' -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -eq $command) {
        throw 'credential-store-unavailable'
    }

    $output = $null
    try {
        $output = @(& $command.Source /list 2>$null)
        if ($LASTEXITCODE -ne 0) {
            throw 'credential-store-unavailable'
        }

        foreach ($line in $output) {
            if ($line -notmatch '^\s*Target:\s*(?:LegacyGeneric:target=)?(?<target>.+?)\s*$') {
                continue
            }

            foreach ($reference in $CredentialReferences) {
                if ([string]::Equals(
                        $Matches['target'],
                        $reference,
                        [StringComparison]::Ordinal)) {
                    [void]$found.Add($reference)
                }
            }
        }
    }
    finally {
        $output = $null
    }

    return $found
}

function Emit-Result {
    param(
        [object] $Result,
        [int] $ExitCode
    )

    if ($Json) {
        [Console]::Out.WriteLine(($Result | ConvertTo-Json -Depth 8 -Compress))
    }
    else {
        $Result
    }

    exit $ExitCode
}

$globalReasons = [Collections.Generic.List[string]]::new()
$profileEvidence = [Collections.Generic.List[object]]::new(2)
$profileInputs = $null
$artifactStates = $null
$resolvableCredentialReferences = $null
$currentIdentity = $null
try {
    try {
        $manifest = Read-StrictJsonDocument -Path $ManifestPath -MaximumBytes $maximumManifestBytes
        $artifactStates = Get-ArtifactStates -Manifest $manifest
    }
    catch {
        Add-Reason -Reasons $globalReasons -Reason 'manifest-validation-failed'
    }

    if (-not $InventoryOnly) {
        if ([string]::IsNullOrWhiteSpace($ProfileInputPath)) {
            Add-Reason -Reasons $globalReasons -Reason 'profile-input-validation-failed'
        }
        else {
            try {
                $profileDocument = Read-StrictJsonDocument -Path $ProfileInputPath -MaximumBytes $maximumProfileInputBytes
                $profileInputs = Get-ProfileInputs -Document $profileDocument
            }
            catch {
                Add-Reason -Reasons $globalReasons -Reason 'profile-input-validation-failed'
            }
        }
    }

    try {
        $currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
        if (-not [string]::Equals(
                $currentIdentity,
                $ExpectedExecutionIdentity,
                [StringComparison]::OrdinalIgnoreCase)) {
            Add-Reason -Reasons $globalReasons -Reason 'execution-identity-mismatch'
        }
    }
    catch {
        Add-Reason -Reasons $globalReasons -Reason 'execution-identity-unverified'
    }

    if ($null -ne $profileInputs) {
        try {
            $references = @($profileInputs | Where-Object {
                $_.CredentialReference -is [string]
            } | ForEach-Object { $_.CredentialReference })
            $resolvableCredentialReferences = Get-ResolvableCredentialReferences -CredentialReferences $references
        }
        catch {
            Add-Reason -Reasons $globalReasons -Reason 'credential-store-unavailable'
        }
    }

    foreach ($expected in @(
        [pscustomobject]@{ ProfileAlias = 'crm82'; CeVersion = '8.2'; WorkerKind = 'OfficialCrm82Worker' },
        [pscustomobject]@{ ProfileAlias = 'crm91'; CeVersion = '9.1'; WorkerKind = 'OfficialCrm91Worker' }
    )) {
        $reasons = [Collections.Generic.List[string]]::new()
        foreach ($globalReason in $globalReasons) {
            Add-Reason -Reasons $reasons -Reason $globalReason
        }

        $credentialTargetState = 'not-applicable'
        $profile = if ($null -eq $profileInputs) {
            $null
        }
        else {
            @($profileInputs | Where-Object { $_.ProfileAlias -ceq $expected.ProfileAlias }) | Select-Object -First 1
        }
        if ($null -eq $profile) {
            if ($InventoryOnly) {
                Add-Reason -Reasons $reasons -Reason 'profile-input-required'
            }
            else {
                Add-Reason -Reasons $reasons -Reason 'profile-input-validation-failed'
            }
        }
        else {
            if ($null -eq $artifactStates -or
                -not $artifactStates.ContainsKey($profile.WorkerKind) -or
                -not [string]::Equals(
                    $artifactStates[$profile.WorkerKind].PackageLockId,
                    $profile.PackageLockId,
                    [StringComparison]::Ordinal)) {
                Add-Reason -Reasons $reasons -Reason 'worker-artifact-package-lock-mismatch'
            }

            if ($profile.CredentialReference -is [string]) {
                if ($null -ne $resolvableCredentialReferences -and
                    $resolvableCredentialReferences.Contains($profile.CredentialReference)) {
                    $credentialTargetState = 'present'
                }
                else {
                    $credentialTargetState = 'unresolvable'
                    Add-Reason -Reasons $reasons -Reason 'credential-reference-unresolvable'
                }
            }
        }

        $profileEvidence.Add([ordered]@{
            ceVersion = $expected.CeVersion
            profileAlias = $expected.ProfileAlias
            workerKind = $expected.WorkerKind
            credentialTargetState = $credentialTargetState
            reasons = @($reasons)
        })
    }

    $outcome = if ($globalReasons.Count -eq 0 -and
                   @($profileEvidence | Where-Object { @($_.reasons).Count -gt 0 }).Count -eq 0) {
        'go'
    }
    else {
        'no-go'
    }
    Emit-Result -Result ([ordered]@{
        schemaVersion = 1
        outcome = $outcome
        profiles = @($profileEvidence)
    }) -ExitCode $(if ($outcome -eq 'go') { 0 } else { 2 })
}
catch {
    Emit-Result -Result ([ordered]@{
        schemaVersion = 1
        outcome = 'no-go'
        profiles = @(
            [ordered]@{ ceVersion = '8.2'; profileAlias = 'crm82'; workerKind = 'OfficialCrm82Worker'; credentialTargetState = 'not-checked'; reasons = @('readiness-probe-failed') },
            [ordered]@{ ceVersion = '9.1'; profileAlias = 'crm91'; workerKind = 'OfficialCrm91Worker'; credentialTargetState = 'not-checked'; reasons = @('readiness-probe-failed') }
        )
    }) -ExitCode 2
}
finally {
    if ($null -ne $resolvableCredentialReferences) {
        $resolvableCredentialReferences.Clear()
    }
    $profileInputs = $null
    $artifactStates = $null
    $currentIdentity = $null
}

<#
.SYNOPSIS
Creates the local, non-secret P6.2 profile input for Official CRM Workers.

.DESCRIPTION
This script accepts only deployment metadata that is safe to store beneath the
current Windows user's LOCALAPPDATA directory. It derives each Worker kind and
package-lock identifier from the supplied immutable Worker manifest, fixes IFD
authentication to WindowsCredentialReference, and writes one versioned profile
document using an atomic create-new operation. Credential values are never
accepted, inspected, or serialized: the JSON carries only the Credential
Manager target name that the Gateway and Worker identity must resolve later.

The script fail-closes before writing when an input is malformed, the manifest
does not describe exactly the approved CRM 8.2 and 9.1 Workers, or the target
file already exists. Its result deliberately excludes endpoint, Organization ID,
home realm, credential-target, and local-path values so an operator can safely
paste the result into P6 evidence.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ManifestPath,

    [Parameter(Mandatory = $true)]
    [string] $Crm82OrganizationBaseUri,

    [Parameter(Mandatory = $true)]
    [string] $Crm82OrganizationName,

    [Parameter(Mandatory = $true)]
    [string] $Crm82ExpectedOrganizationId,

    [Parameter(Mandatory = $true)]
    [string] $Crm82HomeRealm,

    [Parameter(Mandatory = $true)]
    [string] $Crm82CredentialTarget,

    [Parameter(Mandatory = $true)]
    [string] $Crm82ProfileGenerationId,

    [Parameter(Mandatory = $true)]
    [string] $Crm91OrganizationBaseUri,

    [Parameter(Mandatory = $true)]
    [string] $Crm91OrganizationName,

    [Parameter(Mandatory = $true)]
    [string] $Crm91ExpectedOrganizationId,

    [Parameter(Mandatory = $true)]
    [string] $Crm91HomeRealm,

    [Parameter(Mandatory = $true)]
    [string] $Crm91CredentialTarget,

    [Parameter(Mandatory = $true)]
    [string] $Crm91ProfileGenerationId,

    [switch] $Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)
Add-Type -AssemblyName System.Runtime.Serialization -ErrorAction Stop

$maximumManifestBytes = 256 * 1024
$strictUtf8 = [Text.UTF8Encoding]::new($false, $true)
$writeUtf8 = [Text.UTF8Encoding]::new($false)

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
        return $parsed -ne [Guid]::Empty -and
            @($bytes | Select-Object -Unique).Count -gt 1
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

function Test-CanonicalHttpsRootUri {
    param([string] $Value)

    $uri = $null
    if (-not (Test-SafeHttpsUri -Value $Value) -or
        -not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$uri) -or
        -not [string]::Equals($uri.AbsolutePath, '/', [StringComparison]::Ordinal)) {
        return $false
    }

    $canonical = 'https://' + $uri.IdnHost.ToLowerInvariant()
    if ($uri.Port -ne 443) {
        $canonical += ':' + $uri.Port.ToString([Globalization.CultureInfo]::InvariantCulture)
    }

    $canonical += '/'
    return [string]::Equals($Value, $canonical, [StringComparison]::Ordinal)
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
            throw 'manifest-not-found'
        }

        $length = (Get-Item -LiteralPath $resolvedPath -Force).Length
        if ($length -lt 1 -or $length -gt $MaximumBytes) {
            throw 'manifest-size-invalid'
        }

        $bytes = [IO.File]::ReadAllBytes($resolvedPath)
        if ($bytes.Length -ne $length -or
            ($bytes.Length -ge 3 -and
             $bytes[0] -eq 0xEF -and
             $bytes[1] -eq 0xBB -and
             $bytes[2] -eq 0xBF)) {
            throw 'manifest-encoding-invalid'
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
            throw 'manifest-root-invalid'
        }

        Assert-NoDuplicateJsonObjectProperties -Element $document.DocumentElement
        $text = $strictUtf8.GetString($bytes)
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

function Get-ValidatedWorkers {
    param([object] $Manifest)

    if ($null -eq $Manifest -or
        (($Manifest.schemaVersion -isnot [int] -and $Manifest.schemaVersion -isnot [long]) -or
         $Manifest.schemaVersion -ne 1) -or
        $Manifest.featureGateMustRemainDisabled -ne $true) {
        throw 'manifest-shape-invalid'
    }

    $expectedWorkers = @{
        OfficialCrm82Worker = '8.2'
        OfficialCrm91Worker = '9.1'
    }
    $workers = @($Manifest.workers)
    if ($workers.Count -ne $expectedWorkers.Count) {
        throw 'manifest-worker-count-invalid'
    }

    $result = @{}
    foreach ($worker in $workers) {
        $workerKind = Get-RequiredString -Object $worker -PropertyName 'workerKind' -MaximumLength 64
        $ceVersion = Get-RequiredString -Object $worker -PropertyName 'ceVersion' -MaximumLength 16
        $packageLockId = Get-RequiredString -Object $worker -PropertyName 'packageLockId' -MaximumLength 128
        if (-not $expectedWorkers.ContainsKey($workerKind) -or
            -not [string]::Equals(
                $ceVersion,
                $expectedWorkers[$workerKind],
                [StringComparison]::Ordinal) -or
            -not (Test-SafeIdentifier -Value $workerKind -MaximumLength 64) -or
            -not (Test-SafeIdentifier -Value $packageLockId -MaximumLength 128 -AllowDot) -or
            $result.ContainsKey($workerKind)) {
            throw 'manifest-worker-invalid'
        }

        $result.Add($workerKind, [pscustomobject]@{
            WorkerKind = $workerKind
            PackageLockId = $packageLockId
        })
    }

    foreach ($requiredWorkerKind in $expectedWorkers.Keys) {
        if (-not $result.ContainsKey($requiredWorkerKind)) {
            throw 'manifest-worker-missing'
        }
    }

    return $result
}

function New-IfdProfile {
    param(
        [string] $ProfileAlias,
        [string] $WorkerKind,
        [hashtable] $Workers,
        [string] $OrganizationBaseUri,
        [string] $OrganizationName,
        [string] $ExpectedOrganizationId,
        [string] $HomeRealm,
        [string] $CredentialTarget,
        [string] $ProfileGenerationId
    )

    if (-not $Workers.ContainsKey($WorkerKind) -or
        -not (Test-SafeIdentifier -Value $ProfileAlias -MaximumLength 128) -or
        -not (Test-CanonicalHttpsRootUri -Value $OrganizationBaseUri) -or
        -not (Test-SafeIdentifier -Value $OrganizationName -MaximumLength 100) -or
        -not (Test-NonPlaceholderGuid -Value $ExpectedOrganizationId) -or
        -not (Test-SafeHttpsUri -Value $HomeRealm) -or
        -not (Test-SafeIdentifier -Value $CredentialTarget -MaximumLength 256 -AllowDot) -or
        -not (Test-SafeIdentifier -Value $ProfileGenerationId -MaximumLength 128 -AllowDot)) {
        throw 'profile-input-invalid'
    }

    return [ordered]@{
        profileAlias = $ProfileAlias
        workerKind = $WorkerKind
        packageLockId = $Workers[$WorkerKind].PackageLockId
        profileGenerationId = $ProfileGenerationId
        organizationBaseUri = $OrganizationBaseUri
        organizationName = $OrganizationName
        expectedOrganizationId = $ExpectedOrganizationId
        authentication = 'Ifd'
        identity = [ordered]@{
            mode = 'WindowsCredentialReference'
            reference = $CredentialTarget
            homeRealm = $HomeRealm
        }
    }
}

function ConvertTo-StrictCrlfText {
    param([string] $Value)

    return ($Value -replace '(?<!\r)\n', "`r`n").TrimEnd("`r", "`n") + "`r`n"
}

function Write-CreateNewUtf8Json {
    param(
        [string] $Path,
        [object] $Value
    )

    $stream = $null
    $bytes = $null
    try {
        $json = ConvertTo-StrictCrlfText -Value ($Value | ConvertTo-Json -Depth 8)
        $bytes = $writeUtf8.GetBytes($json)
        $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush($true)
    }
    finally {
        if ($null -ne $stream) {
            $stream.Dispose()
        }
        if ($null -ne $bytes) {
            [Array]::Clear($bytes, 0, $bytes.Length)
        }
    }
}

$exitCode = 0
$result = $null
try {
    $manifest = Read-StrictJsonDocument -Path $ManifestPath -MaximumBytes $maximumManifestBytes
    $workers = Get-ValidatedWorkers -Manifest $manifest
    $profileInput = [ordered]@{
        schemaVersion = 1
        profiles = @(
            (New-IfdProfile `
                -ProfileAlias 'crm82' `
                -WorkerKind 'OfficialCrm82Worker' `
                -Workers $workers `
                -OrganizationBaseUri $Crm82OrganizationBaseUri `
                -OrganizationName $Crm82OrganizationName `
                -ExpectedOrganizationId $Crm82ExpectedOrganizationId `
                -HomeRealm $Crm82HomeRealm `
                -CredentialTarget $Crm82CredentialTarget `
                -ProfileGenerationId $Crm82ProfileGenerationId),
            (New-IfdProfile `
                -ProfileAlias 'crm91' `
                -WorkerKind 'OfficialCrm91Worker' `
                -Workers $workers `
                -OrganizationBaseUri $Crm91OrganizationBaseUri `
                -OrganizationName $Crm91OrganizationName `
                -ExpectedOrganizationId $Crm91ExpectedOrganizationId `
                -HomeRealm $Crm91HomeRealm `
                -CredentialTarget $Crm91CredentialTarget `
                -ProfileGenerationId $Crm91ProfileGenerationId)
        )
    }

    if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        throw 'local-app-data-unavailable'
    }

    $outputDirectory = Join-Path ([IO.Path]::GetFullPath($env:LOCALAPPDATA)) 'SpeechMessage\Dynamics\P6.2'
    $outputPath = Join-Path $outputDirectory 'official-worker-profile-input.json'
    if (Test-Path -LiteralPath $outputPath) {
        throw 'profile-input-already-exists'
    }

    [void][IO.Directory]::CreateDirectory($outputDirectory)
    Write-CreateNewUtf8Json -Path $outputPath -Value $profileInput
    $result = [ordered]@{
        schemaVersion = 1
        outcome = 'written'
        profileCount = 2
    }
}
catch {
    $exitCode = 1
    $result = [ordered]@{
        schemaVersion = 1
        outcome = 'error'
        reason = 'profile-input-not-written'
    }
}
finally {
    $manifest = $null
    $workers = $null
    $profileInput = $null
}

if ($Json) {
    $result | ConvertTo-Json -Compress
}
elseif ($exitCode -eq 0) {
    'Official Worker profile input was written.'
}
else {
    'Official Worker profile input was not written.'
}

exit $exitCode

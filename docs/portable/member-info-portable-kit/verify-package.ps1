param(
    [string]$KitRoot = $PSScriptRoot,
    [switch]$GenerateManifest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# This verifier requires an offline, stable kit. It rejects mutations detected by repeated
# namespace, metadata, and content checks, but cannot provide cryptographic protection
# against an adversary that can race filesystem namespace operations at the OS level.
$resolvedRootWithSeparator = [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $KitRoot).Path)
$filesystemRoot = [System.IO.Path]::GetPathRoot($resolvedRootWithSeparator)
$normalizedRootCandidate = $resolvedRootWithSeparator.TrimEnd('\', '/')
$normalizedFilesystemRoot = $filesystemRoot.TrimEnd('\', '/')
if ([string]::IsNullOrEmpty($normalizedRootCandidate) -or
    $normalizedRootCandidate.Equals($normalizedFilesystemRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "KitRoot cannot be a filesystem root: $resolvedRootWithSeparator"
}
$resolvedRoot = $normalizedRootCandidate
$rootPrefix = $resolvedRoot + [System.IO.Path]::DirectorySeparatorChar
$manifestPath = Join-Path $resolvedRoot 'manifest.json'
$utf8NoBom = New-Object System.Text.UTF8Encoding($false, $true)
$textExtensions = @('.md', '.json', '.jsonl', '.cs', '.cshtml', '.csproj', '.patch', '.ps1')

function Test-IsTextFile {
    param([string]$Path)

    return $textExtensions -contains [System.IO.Path]::GetExtension($Path).ToLowerInvariant()
}

function Test-IsWithinKitRoot {
    param([string]$FullPath)

    return $FullPath.Equals($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
        $FullPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-RelativePosixPath {
    param([string]$FullPath)

    $resolved = [System.IO.Path]::GetFullPath($FullPath)
    if (-not $resolved.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "File is outside the kit root: $resolved"
    }
    return $resolved.Substring($rootPrefix.Length).Replace('\', '/')
}

function Assert-PathDoesNotTraverseReparsePoint {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Context
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not (Test-IsWithinKitRoot -FullPath $fullPath)) {
        throw "$Context escapes the kit root: $fullPath"
    }

    $rootItem = Get-Item -LiteralPath $resolvedRoot -Force
    if (($rootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Context traverses a reparse point at the kit root: $resolvedRoot"
    }
    if ($fullPath.Equals($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath
    }

    $relativePath = $fullPath.Substring($rootPrefix.Length)
    $currentPath = $resolvedRoot
    foreach ($segment in ($relativePath -split '[\\/]')) {
        if ([string]::IsNullOrEmpty($segment)) {
            continue
        }
        $currentPath = Join-Path $currentPath $segment
        if (-not (Test-Path -LiteralPath $currentPath)) {
            break
        }
        $item = Get-Item -LiteralPath $currentPath -Force
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Context traverses a reparse point: $(Get-RelativePosixPath -FullPath $currentPath)"
        }
    }
    return $fullPath
}

function Get-StableItemState {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Context
    )

    [void](Assert-PathDoesNotTraverseReparsePoint -Path $Path -Context $Context)
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Kit changed during verification: item disappeared during ${Context}: $Path"
    }
    $item = Get-Item -LiteralPath $Path -Force
    [void](Assert-PathDoesNotTraverseReparsePoint -Path $Path -Context $Context)
    return [pscustomobject]@{
        FullPath = [System.IO.Path]::GetFullPath($item.FullName)
        IsContainer = [bool]$item.PSIsContainer
        Length = if ($item.PSIsContainer) { [int64]0 } else { [int64]$item.Length }
        CreationTimeUtcTicks = [int64]$item.CreationTimeUtc.Ticks
        LastWriteTimeUtcTicks = [int64]$item.LastWriteTimeUtc.Ticks
        Attributes = [int64]$item.Attributes
    }
}

function Test-ItemStatesEqual {
    param(
        [object]$Expected,
        [object]$Actual
    )

    return $Expected.FullPath.Equals($Actual.FullPath, [System.StringComparison]::OrdinalIgnoreCase) -and
        $Expected.IsContainer -eq $Actual.IsContainer -and
        $Expected.Length -eq $Actual.Length -and
        $Expected.CreationTimeUtcTicks -eq $Actual.CreationTimeUtcTicks -and
        $Expected.LastWriteTimeUtcTicks -eq $Actual.LastWriteTimeUtcTicks -and
        $Expected.Attributes -eq $Actual.Attributes
}

function Assert-ItemStateUnchanged {
    param(
        [object]$Expected,
        [object]$Actual,
        [string]$Context
    )

    if (-not (Test-ItemStatesEqual -Expected $Expected -Actual $Actual)) {
        throw "Kit changed during verification: $Context"
    }
}

function Get-KitFilesSafely {
    param([string]$DirectoryPath = $resolvedRoot)

    $directoryBefore = Get-StableItemState -Path $DirectoryPath -Context 'kit directory enumeration'
    if (-not $directoryBefore.IsContainer) {
        throw "Kit directory enumeration reached a non-directory: $DirectoryPath"
    }
    [void](Assert-PathDoesNotTraverseReparsePoint -Path $DirectoryPath -Context 'kit directory enumeration')
    $items = @(Get-ChildItem -LiteralPath $DirectoryPath -Force)
    [void](Assert-PathDoesNotTraverseReparsePoint -Path $DirectoryPath -Context 'kit directory enumeration')
    $directoryAfterList = Get-StableItemState -Path $DirectoryPath -Context 'kit directory enumeration'
    Assert-ItemStateUnchanged -Expected $directoryBefore -Actual $directoryAfterList -Context "directory changed while enumerating $DirectoryPath"

    foreach ($item in $items) {
        [void](Assert-PathDoesNotTraverseReparsePoint -Path $item.FullName -Context 'kit directory enumeration')
        $currentState = Get-StableItemState -Path $item.FullName -Context 'kit directory enumeration'
        if ($currentState.IsContainer) {
            Get-KitFilesSafely -DirectoryPath $currentState.FullPath
        }
        else {
            Write-Output $currentState.FullPath
        }
    }
    $directoryAfterRecursion = Get-StableItemState -Path $DirectoryPath -Context 'kit directory enumeration'
    Assert-ItemStateUnchanged -Expected $directoryBefore -Actual $directoryAfterRecursion -Context "directory changed while enumerating $DirectoryPath"
}

function Get-ByteArraySha256 {
    param([byte[]]$Bytes)

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($sha256.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha256.Dispose()
    }
}

function Get-StableFileHashRecord {
    param(
        [string]$Path,
        [string]$Context
    )

    $before = Get-StableItemState -Path $Path -Context $Context
    if ($before.IsContainer) {
        throw "Kit changed during verification: expected a file during ${Context}: $Path"
    }
    [void](Assert-PathDoesNotTraverseReparsePoint -Path $Path -Context $Context)
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    [void](Assert-PathDoesNotTraverseReparsePoint -Path $Path -Context $Context)
    $after = Get-StableItemState -Path $Path -Context $Context
    Assert-ItemStateUnchanged -Expected $before -Actual $after -Context "file changed while hashing $Path"
    return [pscustomobject]@{
        Hash = $hash
        State = $after
    }
}

function Read-StableFileBytes {
    param(
        [string]$Path,
        [string]$Context
    )

    $before = Get-StableItemState -Path $Path -Context $Context
    if ($before.IsContainer) {
        throw "Kit changed during verification: expected a file during ${Context}: $Path"
    }
    [void](Assert-PathDoesNotTraverseReparsePoint -Path $Path -Context $Context)
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    [void](Assert-PathDoesNotTraverseReparsePoint -Path $Path -Context $Context)
    $after = Get-StableItemState -Path $Path -Context $Context
    Assert-ItemStateUnchanged -Expected $before -Actual $after -Context "file changed while reading $Path"
    if ([int64]$bytes.Length -ne $after.Length) {
        throw "Kit changed during verification: byte count changed while reading $Path"
    }
    return [pscustomobject]@{
        Bytes = $bytes
        Hash = Get-ByteArraySha256 -Bytes $bytes
        State = $after
    }
}

function Read-StrictUtf8File {
    param(
        [string]$Path,
        [string]$Context = 'strict UTF-8 read'
    )

    $readRecord = Read-StableFileBytes -Path $Path -Context $Context
    try {
        $text = $utf8NoBom.GetString($readRecord.Bytes)
    }
    catch {
        throw "Text file is not strict UTF-8: $(Get-RelativePosixPath -FullPath $Path)"
    }
    if ($text.IndexOf([char]0xFFFD) -ge 0) {
        throw "Text file contains U+FFFD: $(Get-RelativePosixPath -FullPath $Path)"
    }
    return [pscustomobject]@{
        Text = $text
        Hash = $readRecord.Hash
        State = $readRecord.State
    }
}

function Get-KitSnapshot {
    param([string[]]$ExcludedFullPaths = @())

    $excluded = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($excludedPath in $ExcludedFullPaths) {
        [void]$excluded.Add([System.IO.Path]::GetFullPath($excludedPath))
    }
    $snapshot = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([System.StringComparer]::Ordinal)
    foreach ($fullPath in @(Get-KitFilesSafely -DirectoryPath $resolvedRoot)) {
        if ($excluded.Contains([System.IO.Path]::GetFullPath($fullPath))) {
            continue
        }
        $relativePath = Get-RelativePosixPath -FullPath $fullPath
        if ($snapshot.ContainsKey($relativePath)) {
            throw "Two files resolve to the same manifest path: $relativePath"
        }
        $snapshot.Add($relativePath, (Get-StableFileHashRecord -Path $fullPath -Context 'kit snapshot'))
    }
    return ,$snapshot
}

function Assert-KitSnapshotsEqual {
    param(
        [object]$Expected,
        [object]$Actual,
        [string]$Context
    )

    if ($Expected.Count -ne $Actual.Count) {
        throw "Kit changed during verification: $Context (file count changed)."
    }
    foreach ($relativePath in $Expected.Keys) {
        if (-not $Actual.ContainsKey($relativePath)) {
            throw "Kit changed during verification: $Context (file set changed at $relativePath)."
        }
        $expectedRecord = $Expected[$relativePath]
        $actualRecord = $Actual[$relativePath]
        if ($expectedRecord.Hash -cne $actualRecord.Hash -or
            -not (Test-ItemStatesEqual -Expected $expectedRecord.State -Actual $actualRecord.State)) {
            throw "Kit changed during verification: $Context ($relativePath changed)."
        }
    }
}

function Get-MarkdownTextOutsideFences {
    param([string]$Text)

    $outsideFences = New-Object System.Text.StringBuilder
    $insideFence = $false
    $fenceMarker = $null
    $fenceLength = 0
    foreach ($line in ($Text -split "`r?`n")) {
        if (-not $insideFence -and $line -match '^ {0,3}(?<fence>`{3,}|~{3,})') {
            $insideFence = $true
            $fenceMarker = $Matches['fence'][0]
            $fenceLength = $Matches['fence'].Length
            continue
        }
        if ($insideFence) {
            $closingPattern = '^ {0,3}' + [regex]::Escape([string]$fenceMarker) + '{' + $fenceLength + ',}\s*$'
            if ($line -match $closingPattern) {
                $insideFence = $false
                $fenceMarker = $null
                $fenceLength = 0
            }
            continue
        }
        [void]$outsideFences.AppendLine($line)
    }
    return $outsideFences.ToString()
}

function Get-InlineMarkdownLinkTargets {
    param([string]$Text)

    $index = 0
    while ($index -lt $Text.Length) {
        if ($Text[$index] -eq '\') {
            $index += 2
            continue
        }
        if ($Text[$index] -ne '[') {
            $index++
            continue
        }

        $labelDepth = 1
        $cursor = $index + 1
        while ($cursor -lt $Text.Length -and $labelDepth -gt 0) {
            if ($Text[$cursor] -eq '\') {
                $cursor += 2
                continue
            }
            if ($Text[$cursor] -eq '[') {
                $labelDepth++
            }
            elseif ($Text[$cursor] -eq ']') {
                $labelDepth--
            }
            $cursor++
        }
        if ($labelDepth -ne 0 -or $cursor -ge $Text.Length -or $Text[$cursor] -ne '(') {
            $index++
            continue
        }

        $destinationStart = $cursor + 1
        while ($destinationStart -lt $Text.Length -and [char]::IsWhiteSpace($Text[$destinationStart])) {
            $destinationStart++
        }
        if ($destinationStart -ge $Text.Length) {
            break
        }

        if ($Text[$destinationStart] -eq '<') {
            $targetStart = $destinationStart + 1
            $destinationCursor = $targetStart
            while ($destinationCursor -lt $Text.Length) {
                if ($Text[$destinationCursor] -eq '\') {
                    $destinationCursor += 2
                    continue
                }
                if ($Text[$destinationCursor] -eq '>') {
                    Write-Output $Text.Substring($targetStart, $destinationCursor - $targetStart)
                    $index = $destinationCursor + 1
                    break
                }
                $destinationCursor++
            }
            if ($destinationCursor -ge $Text.Length) {
                $index++
            }
            continue
        }

        $targetStart = $destinationStart
        $destinationCursor = $destinationStart
        $nestedParentheses = 0
        $targetEnd = -1
        $linkEnd = -1
        while ($destinationCursor -lt $Text.Length) {
            if ($Text[$destinationCursor] -eq '\') {
                $destinationCursor += 2
                continue
            }
            if ($Text[$destinationCursor] -eq '(') {
                $nestedParentheses++
            }
            elseif ($Text[$destinationCursor] -eq ')') {
                if ($nestedParentheses -eq 0) {
                    if ($targetEnd -lt 0) {
                        $targetEnd = $destinationCursor
                    }
                    $linkEnd = $destinationCursor
                    break
                }
                $nestedParentheses--
            }
            elseif ([char]::IsWhiteSpace($Text[$destinationCursor]) -and $nestedParentheses -eq 0 -and $targetEnd -lt 0) {
                $targetEnd = $destinationCursor
            }
            $destinationCursor++
        }
        if ($linkEnd -ge 0 -and $targetEnd -ge $targetStart) {
            Write-Output $Text.Substring($targetStart, $targetEnd - $targetStart)
            $index = $linkEnd + 1
        }
        else {
            $index++
        }
    }
}

function ConvertFrom-MarkdownEscapes {
    param([string]$Target)

    $unescaped = New-Object System.Text.StringBuilder
    $index = 0
    while ($index -lt $Target.Length) {
        if ($Target[$index] -eq '\' -and $index + 1 -lt $Target.Length) {
            [void]$unescaped.Append($Target[$index + 1])
            $index += 2
            continue
        }
        [void]$unescaped.Append($Target[$index])
        $index++
    }
    return $unescaped.ToString()
}

function Assert-MarkdownLinks {
    param([string[]]$MarkdownFiles)

    $linkCount = 0
    foreach ($markdownFile in $MarkdownFiles) {
        $markdownRelativePath = Get-RelativePosixPath -FullPath $markdownFile
        $markdownRead = Read-StrictUtf8File -Path $markdownFile -Context "Markdown read for $markdownRelativePath"
        $outsideFences = Get-MarkdownTextOutsideFences -Text $markdownRead.Text
        foreach ($rawTarget in @(Get-InlineMarkdownLinkTargets -Text $outsideFences)) {
            $target = $rawTarget.Trim()
            if ([string]::IsNullOrWhiteSpace($target) -or
                $target.StartsWith('#') -or
                $target -match '^(?i:https?|mailto):') {
                continue
            }
            if ($target -match '^(?i:file):' -or
                [System.IO.Path]::IsPathRooted($target) -or
                $target -match '^[A-Za-z]:') {
                throw "Markdown link uses a forbidden absolute target in ${markdownRelativePath}: $target"
            }

            $pathPart = ($target -split '#', 2)[0]
            if ([string]::IsNullOrWhiteSpace($pathPart)) {
                continue
            }
            try {
                $decodedPath = [System.Uri]::UnescapeDataString((ConvertFrom-MarkdownEscapes -Target $pathPart))
            }
            catch {
                throw "Markdown link has invalid URL encoding in ${markdownRelativePath}: $target"
            }
            $decodedPath = $decodedPath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
            $candidate = [System.IO.Path]::GetFullPath((Join-Path ([System.IO.Path]::GetDirectoryName($markdownFile)) $decodedPath))
            if (-not (Test-IsWithinKitRoot -FullPath $candidate)) {
                throw "Markdown link escapes the kit root in ${markdownRelativePath}: $target"
            }
            [void](Assert-PathDoesNotTraverseReparsePoint -Path $candidate -Context "Markdown link target in ${markdownRelativePath}: $target")
            if (-not (Test-Path -LiteralPath $candidate)) {
                throw "Markdown link target is missing in ${markdownRelativePath}: $target"
            }
            $linkCount++
        }
    }
    return $linkCount
}

function Get-ArtifactRole {
    param([string]$RelativePath)

    switch -Regex ($RelativePath) {
        '^00-START-HERE\.md$' { return 'entry_point' }
        '^01-INTEGRATED-SPEC\.md$' { return 'integrated_spec' }
        '^02-DEPENDENCY-MATRIX\.md$' { return 'dependency_matrix' }
        '^03-PROMPT-HISTORY-VERBATIM\.md$' { return 'prompt_history' }
        '^04-PROMPT-PLAYBOOK\.md$' { return 'prompt_playbook' }
        '^05-MIGRATION-RUNBOOK\.md$' { return 'migration_runbook' }
        '^06-ACCEPTANCE-CHECKLIST\.md$' { return 'acceptance_checklist' }
        '^07-PRIVACY-REDACTIONS\.md$' { return 'privacy_redactions' }
        '^verify-package\.ps1$' { return 'verifier' }
        '^original-specs/' { return 'original_spec' }
        '^original-plans/' { return 'original_plan' }
        '^authoritative-context/' { return 'authoritative_context' }
        '^reference-implementation/feature-files/' { return 'reference_source' }
        '^reference-implementation/tests/' { return 'reference_test' }
        '^reference-implementation/host-integration/.*\.patch$' { return 'host_patch' }
        '^reference-implementation/' { return 'reference_documentation' }
        default { return 'package_artifact' }
    }
}

function Get-SourcePath {
    param([string]$RelativePath)

    if ($RelativePath.StartsWith('original-specs/')) {
        return 'docs/superpowers/specs/' + [System.IO.Path]::GetFileName($RelativePath)
    }
    if ($RelativePath.StartsWith('original-plans/')) {
        return 'docs/superpowers/plans/' + [System.IO.Path]::GetFileName($RelativePath)
    }
    if ($RelativePath -eq 'authoritative-context/requirements.md') {
        return '.ccg/tasks/archive/2026-07/implement-member-info-district-group-tree/requirements.md'
    }
    if ($RelativePath -eq 'authoritative-context/context.jsonl') {
        return '.ccg/tasks/archive/2026-07/implement-member-info-district-group-tree/context.jsonl'
    }
    if ($RelativePath.StartsWith('reference-implementation/feature-files/')) {
        return $RelativePath.Substring('reference-implementation/feature-files/'.Length)
    }
    if ($RelativePath.StartsWith('reference-implementation/tests/')) {
        return $RelativePath.Substring('reference-implementation/tests/'.Length)
    }
    return $null
}

function New-ManifestEntry {
    param(
        [string]$RelativePath,
        [object]$SnapshotRecord
    )

    $fullPath = $SnapshotRecord.State.FullPath
    $isText = Test-IsTextFile -Path $fullPath
    if ($isText) {
        $textRecord = Read-StrictUtf8File -Path $fullPath -Context "manifest generation read for $RelativePath"
        if ($textRecord.Hash -cne $SnapshotRecord.Hash -or
            -not (Test-ItemStatesEqual -Expected $SnapshotRecord.State -Actual $textRecord.State)) {
            throw "Kit changed during verification: $RelativePath changed while generating its manifest entry."
        }
    }
    $entry = [ordered]@{
        path = $RelativePath
        role = Get-ArtifactRole -RelativePath $RelativePath
    }
    $sourcePath = Get-SourcePath -RelativePath $RelativePath
    if ($null -ne $sourcePath) {
        $entry['sourcePath'] = $sourcePath
    }
    $entry['bytes'] = [int64]$SnapshotRecord.State.Length
    $entry['sha256'] = $SnapshotRecord.Hash
    $entry['utf8'] = $isText
    return [pscustomobject]$entry
}

function Assert-ExactJsonProperties {
    param(
        [object]$Object,
        [string[]]$ExpectedProperties,
        [string]$Context
    )

    if ($null -eq $Object -or $Object -isnot [pscustomobject]) {
        throw "Manifest schema invalid: $Context must be a JSON object."
    }
    $actualProperties = @($Object.PSObject.Properties.Name)
    $expectedSet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
    foreach ($propertyName in $ExpectedProperties) {
        [void]$expectedSet.Add($propertyName)
    }
    if ($actualProperties.Count -ne $ExpectedProperties.Count) {
        throw "Manifest schema invalid: $Context properties must be exactly: $($ExpectedProperties -join ', ')."
    }
    foreach ($propertyName in $actualProperties) {
        if (-not $expectedSet.Contains([string]$propertyName)) {
            throw "Manifest schema invalid: $Context properties must be exactly: $($ExpectedProperties -join ', ')."
        }
    }
}

function Test-IsJsonInteger {
    param([object]$Value)

    return $Value -is [int] -or $Value -is [long]
}

function Resolve-ManifestRelativePath {
    param([string]$RelativePath)

    if ([string]::IsNullOrWhiteSpace($RelativePath) -or
        $RelativePath.Contains('\') -or
        $RelativePath.StartsWith('/') -or
        $RelativePath.EndsWith('/') -or
        [System.IO.Path]::IsPathRooted($RelativePath) -or
        $RelativePath -match '^[A-Za-z]:') {
        throw "Manifest path is not a portable relative POSIX path: $RelativePath"
    }
    $segments = @($RelativePath -split '/')
    if ($segments -contains '..') {
        throw "Manifest path escapes the kit root: $RelativePath"
    }
    if ($segments -contains '.' -or $segments -contains '') {
        throw "Manifest path is not a portable relative POSIX path: $RelativePath"
    }
    try {
        $candidate = [System.IO.Path]::GetFullPath((Join-Path $resolvedRoot ($RelativePath.Replace('/', '\'))))
    }
    catch {
        throw "Manifest path is invalid: $RelativePath"
    }
    if (-not $candidate.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Manifest path escapes the kit root: $RelativePath"
    }
    return $candidate
}

function Write-ManifestAtomically {
    param(
        [string]$ManifestText,
        [object]$ExpectedKitSnapshot
    )

    $workFileId = [Guid]::NewGuid().ToString('N')
    $temporaryPath = Join-Path $resolvedRoot ('.manifest.' + $workFileId + '.tmp')
    $backupPath = Join-Path $resolvedRoot ('.manifest.' + $workFileId + '.bak')
    $temporaryCreated = $false
    $backupCreated = $false
    $manifestExisted = Test-Path -LiteralPath $manifestPath -PathType Leaf
    if (-not $manifestExisted -and (Test-Path -LiteralPath $manifestPath)) {
        throw "Manifest destination is not a regular file: $manifestPath"
    }
    $manifestBefore = $null
    if ($manifestExisted) {
        $manifestBefore = Get-StableFileHashRecord -Path $manifestPath -Context 'manifest destination before generation'
    }

    try {
        [void](Assert-PathDoesNotTraverseReparsePoint -Path $resolvedRoot -Context 'manifest generation root')
        [void](Assert-PathDoesNotTraverseReparsePoint -Path $manifestPath -Context 'manifest destination')
        [void](Assert-PathDoesNotTraverseReparsePoint -Path $temporaryPath -Context 'temporary manifest path')
        [void](Assert-PathDoesNotTraverseReparsePoint -Path $backupPath -Context 'manifest backup path')
        if ((Test-Path -LiteralPath $temporaryPath) -or (Test-Path -LiteralPath $backupPath)) {
            throw "Manifest work path already exists: $temporaryPath or $backupPath"
        }

        $beforeWriteSnapshot = Get-KitSnapshot -ExcludedFullPaths @($manifestPath, $temporaryPath, $backupPath)
        Assert-KitSnapshotsEqual -Expected $ExpectedKitSnapshot -Actual $beforeWriteSnapshot -Context 'kit changed before manifest write'

        $manifestBytes = $utf8NoBom.GetBytes($ManifestText)
        $expectedManifestHash = Get-ByteArraySha256 -Bytes $manifestBytes
        [void](Assert-PathDoesNotTraverseReparsePoint -Path $temporaryPath -Context 'temporary manifest write')
        $stream = [System.IO.File]::Open(
            $temporaryPath,
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None)
        $temporaryCreated = $true
        try {
            $stream.Write($manifestBytes, 0, $manifestBytes.Length)
            $stream.Flush($true)
        }
        finally {
            $stream.Dispose()
        }
        [void](Assert-PathDoesNotTraverseReparsePoint -Path $temporaryPath -Context 'temporary manifest write')
        $temporaryRecord = Get-StableFileHashRecord -Path $temporaryPath -Context 'temporary manifest verification'
        if ($temporaryRecord.Hash -cne $expectedManifestHash -or $temporaryRecord.State.Length -ne $manifestBytes.Length) {
            throw 'Kit changed during verification: temporary manifest content changed after write.'
        }

        $afterWriteSnapshot = Get-KitSnapshot -ExcludedFullPaths @($manifestPath, $temporaryPath, $backupPath)
        Assert-KitSnapshotsEqual -Expected $ExpectedKitSnapshot -Actual $afterWriteSnapshot -Context 'kit changed during manifest write'

        if ($manifestExisted) {
            if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
                throw 'Kit changed during verification: manifest destination disappeared before replacement.'
            }
            $manifestCurrent = Get-StableFileHashRecord -Path $manifestPath -Context 'manifest destination before replacement'
            if ($manifestCurrent.Hash -cne $manifestBefore.Hash -or
                -not (Test-ItemStatesEqual -Expected $manifestBefore.State -Actual $manifestCurrent.State)) {
                throw 'Kit changed during verification: manifest destination changed before replacement.'
            }
        }
        elseif (Test-Path -LiteralPath $manifestPath) {
            throw 'Kit changed during verification: manifest destination appeared before replacement.'
        }

        [void](Assert-PathDoesNotTraverseReparsePoint -Path $resolvedRoot -Context 'manifest replacement root')
        [void](Assert-PathDoesNotTraverseReparsePoint -Path $manifestPath -Context 'manifest destination before replacement')
        [void](Assert-PathDoesNotTraverseReparsePoint -Path $temporaryPath -Context 'temporary manifest before replacement')
        [void](Assert-PathDoesNotTraverseReparsePoint -Path $backupPath -Context 'manifest backup before replacement')
        if ($manifestExisted) {
            [System.IO.File]::Replace($temporaryPath, $manifestPath, $backupPath, $true)
            $backupCreated = $true
        }
        else {
            [System.IO.File]::Move($temporaryPath, $manifestPath)
        }
        $temporaryCreated = $false

        [void](Assert-PathDoesNotTraverseReparsePoint -Path $manifestPath -Context 'manifest destination after replacement')
        $manifestAfter = Get-StableFileHashRecord -Path $manifestPath -Context 'manifest destination after replacement'
        if ($manifestAfter.Hash -cne $expectedManifestHash -or $manifestAfter.State.Length -ne $manifestBytes.Length) {
            throw 'Kit changed during verification: manifest content changed after replacement.'
        }
        if ($backupCreated) {
            [void](Assert-PathDoesNotTraverseReparsePoint -Path $backupPath -Context 'manifest backup cleanup')
            $backupState = Get-StableItemState -Path $backupPath -Context 'manifest backup cleanup'
            if ($backupState.IsContainer) {
                throw "Refusing manifest backup cleanup of a directory: $backupPath"
            }
            [System.IO.File]::Delete($backupPath)
            [void](Assert-PathDoesNotTraverseReparsePoint -Path $backupPath -Context 'manifest backup cleanup')
            $backupCreated = $false
        }
        $afterReplaceSnapshot = Get-KitSnapshot -ExcludedFullPaths @($manifestPath, $temporaryPath, $backupPath)
        Assert-KitSnapshotsEqual -Expected $ExpectedKitSnapshot -Actual $afterReplaceSnapshot -Context 'kit changed during manifest replacement'
    }
    finally {
        if ($temporaryCreated -and (Test-Path -LiteralPath $temporaryPath)) {
            $expectedTemporaryPath = [System.IO.Path]::GetFullPath($temporaryPath)
            if (-not $expectedTemporaryPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing temporary manifest cleanup outside the kit root: $expectedTemporaryPath"
            }
            [void](Assert-PathDoesNotTraverseReparsePoint -Path $expectedTemporaryPath -Context 'temporary manifest cleanup')
            $temporaryState = Get-StableItemState -Path $expectedTemporaryPath -Context 'temporary manifest cleanup'
            if ($temporaryState.IsContainer) {
                throw "Refusing temporary manifest cleanup of a directory: $expectedTemporaryPath"
            }
            [System.IO.File]::Delete($expectedTemporaryPath)
            [void](Assert-PathDoesNotTraverseReparsePoint -Path $expectedTemporaryPath -Context 'temporary manifest cleanup')
        }
        if ($backupCreated -and (Test-Path -LiteralPath $backupPath)) {
            $expectedBackupPath = [System.IO.Path]::GetFullPath($backupPath)
            if (-not $expectedBackupPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing manifest backup cleanup outside the kit root: $expectedBackupPath"
            }
            [void](Assert-PathDoesNotTraverseReparsePoint -Path $expectedBackupPath -Context 'manifest backup cleanup')
            $backupState = Get-StableItemState -Path $expectedBackupPath -Context 'manifest backup cleanup'
            if ($backupState.IsContainer) {
                throw "Refusing manifest backup cleanup of a directory: $expectedBackupPath"
            }
            [System.IO.File]::Delete($expectedBackupPath)
            [void](Assert-PathDoesNotTraverseReparsePoint -Path $expectedBackupPath -Context 'manifest backup cleanup')
        }
    }
}

if ($GenerateManifest) {
    $generationSnapshot = Get-KitSnapshot -ExcludedFullPaths @($manifestPath)
    $sortedPaths = [string[]]@($generationSnapshot.Keys)
    [Array]::Sort($sortedPaths, [System.StringComparer]::Ordinal)
    $entries = @(foreach ($relativePath in $sortedPaths) {
        New-ManifestEntry -RelativePath $relativePath -SnapshotRecord $generationSnapshot[$relativePath]
    })

    $manifest = [ordered]@{
        formatVersion = 1
        kitId = 'member-info-portable-kit'
        source = [ordered]@{
            branch = 'Sunny_5.1.2.WorktreeTuneMemberView'
            commit = '2406b126e989cc980e8cada9da0e07a2ede1e08d'
            documentRangeStart = '2026-07-15'
        }
        files = $entries
    }

    $json = $manifest | ConvertTo-Json -Depth 8
    Write-ManifestAtomically -ManifestText ($json + [Environment]::NewLine) -ExpectedKitSnapshot $generationSnapshot
}

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Manifest does not exist: $manifestPath"
}
[void](Assert-PathDoesNotTraverseReparsePoint -Path $manifestPath -Context 'Manifest file')
$manifestReadAtStart = Read-StrictUtf8File -Path $manifestPath -Context 'manifest read'
$manifestData = $manifestReadAtStart.Text | ConvertFrom-Json

Assert-ExactJsonProperties -Object $manifestData -ExpectedProperties @('formatVersion', 'kitId', 'source', 'files') -Context 'top-level'
if (-not (Test-IsJsonInteger -Value $manifestData.formatVersion) -or [int64]$manifestData.formatVersion -ne 1) {
    throw 'Manifest schema invalid: formatVersion must be the integer 1.'
}
if ($manifestData.kitId -isnot [string] -or $manifestData.kitId -cne 'member-info-portable-kit') {
    throw 'Manifest schema invalid: kitId is invalid.'
}
Assert-ExactJsonProperties -Object $manifestData.source -ExpectedProperties @('branch', 'commit', 'documentRangeStart') -Context 'source'
if ($manifestData.source.branch -isnot [string] -or $manifestData.source.branch -cne 'Sunny_5.1.2.WorktreeTuneMemberView') {
    throw 'Manifest schema invalid: source.branch is invalid.'
}
if ($manifestData.source.commit -isnot [string] -or $manifestData.source.commit -cne '2406b126e989cc980e8cada9da0e07a2ede1e08d') {
    throw 'Manifest schema invalid: source.commit is invalid.'
}
if ($manifestData.source.documentRangeStart -isnot [string] -or $manifestData.source.documentRangeStart -cne '2026-07-15') {
    throw 'Manifest schema invalid: source.documentRangeStart is invalid.'
}
if ($manifestData.files -isnot [System.Array]) {
    throw 'Manifest schema invalid: files must be a JSON array.'
}

$manifestEntries = @($manifestData.files)
$manifestPaths = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
$resolvedManifestFiles = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([System.StringComparer]::Ordinal)
$previousPath = $null
foreach ($entry in $manifestEntries) {
    if ($null -eq $entry -or $entry -isnot [pscustomobject]) {
        throw 'Manifest schema invalid: every files entry must be a JSON object.'
    }
    if (-not ($entry.PSObject.Properties.Name -ccontains 'path') -or $entry.path -isnot [string]) {
        throw 'Manifest schema invalid: each entry path must be a string.'
    }
    $relativePath = [string]$entry.path
    $candidate = Resolve-ManifestRelativePath -RelativePath $relativePath
    $expectedSourcePath = Get-SourcePath -RelativePath $relativePath
    if ($null -eq $expectedSourcePath) {
        $expectedProperties = @('path', 'role', 'bytes', 'sha256', 'utf8')
    }
    else {
        $expectedProperties = @('path', 'role', 'sourcePath', 'bytes', 'sha256', 'utf8')
    }
    Assert-ExactJsonProperties -Object $entry -ExpectedProperties $expectedProperties -Context "entry properties for $relativePath"

    if (-not $manifestPaths.Add($relativePath)) {
        throw "Manifest contains a duplicate path: $relativePath"
    }
    if ($null -ne $previousPath -and [System.StringComparer]::Ordinal.Compare($previousPath, $relativePath) -ge 0) {
        throw "Manifest files must be strictly sorted by ordinal path: $previousPath then $relativePath"
    }
    $previousPath = $relativePath

    $expectedRole = Get-ArtifactRole -RelativePath $relativePath
    if ($entry.role -isnot [string] -or $entry.role -cne $expectedRole) {
        throw "Manifest entry role differs for ${relativePath}: expected $expectedRole."
    }
    if (-not (Test-IsJsonInteger -Value $entry.bytes) -or [int64]$entry.bytes -lt 0) {
        throw "Manifest entry bytes must be a non-negative integer: $relativePath"
    }
    if ($entry.sha256 -isnot [string] -or $entry.sha256 -cnotmatch '^[0-9a-f]{64}$') {
        throw "Manifest entry sha256 must be 64 lowercase hexadecimal characters: $relativePath"
    }
    if ($entry.utf8 -isnot [bool]) {
        throw "Manifest entry utf8 must be a boolean: $relativePath"
    }
    if ($null -ne $expectedSourcePath) {
        if ($entry.sourcePath -isnot [string] -or $entry.sourcePath -cne $expectedSourcePath) {
            throw "Manifest entry sourcePath differs for ${relativePath}: expected $expectedSourcePath."
        }
    }

    $isText = Test-IsTextFile -Path $relativePath
    if ($isText -and -not $entry.utf8) {
        throw "Manifest must mark text file as UTF-8: $relativePath"
    }
    if (-not $isText -and $entry.utf8) {
        throw "Manifest cannot mark a non-text file as strictly decoded UTF-8: $relativePath"
    }
    [void](Assert-PathDoesNotTraverseReparsePoint -Path $candidate -Context "Manifest path $relativePath")
    $resolvedManifestFiles.Add($relativePath, $candidate)
}

$verificationStartSnapshot = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([System.StringComparer]::Ordinal)
foreach ($entry in $manifestEntries) {
    $relativePath = [string]$entry.path
    $candidate = $resolvedManifestFiles[$relativePath]
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "Manifest file is missing: $relativePath"
    }
    if ($entry.utf8) {
        $actualRecord = Read-StrictUtf8File -Path $candidate -Context "manifest file verification for $relativePath"
    }
    else {
        $actualRecord = Get-StableFileHashRecord -Path $candidate -Context "manifest file verification for $relativePath"
    }
    if ([int64]$entry.bytes -ne [int64]$actualRecord.State.Length) {
        throw "Manifest byte length differs for ${relativePath}: expected $($entry.bytes), actual $($actualRecord.State.Length)."
    }
    if ($actualRecord.Hash -cne [string]$entry.sha256) {
        throw "Manifest SHA-256 differs for $relativePath."
    }
    $verificationStartSnapshot.Add($relativePath, $actualRecord)
}

$markdownFiles = @(foreach ($entry in $manifestEntries) {
    if ([System.IO.Path]::GetExtension([string]$entry.path).Equals('.md', [System.StringComparison]::OrdinalIgnoreCase)) {
        $resolvedManifestFiles[[string]$entry.path]
    }
})
$markdownLinkCount = Assert-MarkdownLinks -MarkdownFiles $markdownFiles

$verificationEndSnapshot = Get-KitSnapshot -ExcludedFullPaths @($manifestPath)
$actualPaths = @($verificationEndSnapshot.Keys)
foreach ($actualPath in $actualPaths) {
    if (-not $manifestPaths.Contains($actualPath)) {
        throw "Kit contains a file missing from the manifest: $actualPath"
    }
}
if ($actualPaths.Count -ne $manifestPaths.Count) {
    throw "Manifest file count differs: manifest $($manifestPaths.Count), kit $($actualPaths.Count)."
}
Assert-KitSnapshotsEqual -Expected $verificationStartSnapshot -Actual $verificationEndSnapshot -Context 'kit changed during verification'
$verificationFinalSnapshot = Get-KitSnapshot -ExcludedFullPaths @($manifestPath)
Assert-KitSnapshotsEqual -Expected $verificationEndSnapshot -Actual $verificationFinalSnapshot -Context 'kit changed during final verification checks'

$manifestReadAtEnd = Read-StrictUtf8File -Path $manifestPath -Context 'final manifest stability check'
if ($manifestReadAtEnd.Hash -cne $manifestReadAtStart.Hash -or
    -not (Test-ItemStatesEqual -Expected $manifestReadAtStart.State -Actual $manifestReadAtEnd.State)) {
    throw 'Kit changed during verification: manifest changed during verification.'
}

$manifestFileCount = $manifestEntries.Count
$strictUtf8Count = @($manifestEntries | Where-Object { $_.utf8 }).Count
Write-Output ("PASS: verified {0} files, {1} strict UTF-8 text files, {2} SHA-256 hashes, and {3} relative Markdown links." -f $manifestFileCount, $strictUtf8Count, $manifestFileCount, $markdownLinkCount)

<#
.SYNOPSIS
    從已完成 Slice A 的 task-owned contact fixture 建立 P7.2 B1/B2 descriptors。

.DESCRIPTION
    不要求操作者再次輸入 Contact GUID。腳本只接受目前 Windows identity 所擁有、
    固定指向 sunnyvalechback CE 9.1 Data8 的 Slice A descriptor，並以 UTF-8
    without BOM／CRLF 寫出 B1 LINE profile 與 B2 read-only aggregate descriptors。
    既有 descriptor 若內容不一致會 fail closed，不會覆寫。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string] $SourceDescriptorPath,

    [Parameter(Mandatory = $false)]
    [string] $DestinationDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

if ([string]::IsNullOrWhiteSpace($DestinationDirectory)) {
    $DestinationDirectory = Join-Path $env:LOCALAPPDATA 'SpeechMessage\Dynamics\P7.2'
}
if ([string]::IsNullOrWhiteSpace($SourceDescriptorPath)) {
    $SourceDescriptorPath = Join-Path $DestinationDirectory 'contact-basic-info-fixture.json'
}

function Read-StrictSourceDescriptor {
    param([string] $Path)
    $bytes = $null
    try {
        if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw 'source-descriptor-invalid' }
        $item = Get-Item -LiteralPath $Path -Force
        if ($item.Length -lt 1 -or $item.Length -gt 32768) { throw 'source-descriptor-invalid' }
        $bytes = [IO.File]::ReadAllBytes($Path)
        if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { throw 'source-descriptor-invalid' }
        $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        if ([Regex]::IsMatch($text, '(?<!\r)\n')) { throw 'source-descriptor-invalid' }
        return $text | ConvertFrom-Json -ErrorAction Stop
    }
    catch { throw 'source-descriptor-invalid' }
    finally { if ($null -ne $bytes) { [Array]::Clear($bytes, 0, $bytes.Length) } }
}

function Test-DescriptorValue {
    param([object] $Value, [object] $Expected)
    return [string]::Equals([string]$Value, [string]$Expected, [StringComparison]::Ordinal)
}

function Write-DescriptorIfAbsent {
    param([string] $Path, [object] $Value)
    $json = $Value | ConvertTo-Json -Depth 6
    $text = ($json -replace "`r?`n", "`r`n").TrimEnd("`r", "`n") + "`r`n"
    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        $existing = Read-StrictSourceDescriptor $Path
        $expected = $text | ConvertFrom-Json
        $expectedContact = $expected.PSObject.Properties['contactId']
        $existingContact = $existing.PSObject.Properties['contactId']
        if (-not (Test-DescriptorValue $existing.fixtureId $expected.fixtureId) -or
            -not (Test-DescriptorValue $existing.profileAlias $expected.profileAlias) -or
            -not (Test-DescriptorValue $existing.ceVersion $expected.ceVersion) -or
            -not (Test-DescriptorValue $existing.connector $expected.connector) -or
            -not (Test-DescriptorValue $existing.marker $expected.marker) -or
            -not (Test-DescriptorValue $existing.ownerIdentity $expected.ownerIdentity) -or
            ($null -ne $expectedContact -and ($null -eq $existingContact -or
                -not (Test-DescriptorValue $existingContact.Value $expectedContact.Value)))) {
            throw 'destination-descriptor-conflict'
        }
        return
    }
    [IO.File]::WriteAllText($Path, $text, [Text.UTF8Encoding]::new($false))
}

try {
    $source = Read-StrictSourceDescriptor $SourceDescriptorPath
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $contactId = [Guid]::Empty
    if ($source.schemaVersion -ne 1 -or $source.fixtureId -cne 'p7.2-contact-basic-info' -or
        $source.profileAlias -cne 'sunnyvalechback' -or $source.ceVersion -cne '9.1' -or
        $source.connector -cne 'Data8' -or $source.marker -cne 'p7.2-contact-basic-info' -or
        -not [Guid]::TryParseExact([string]$source.contactId, 'D', [ref]$contactId) -or $contactId -eq [Guid]::Empty -or
        -not [string]::Equals([string]$source.ownerIdentity, $identity, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'source-descriptor-invalid'
    }
    if (-not (Test-Path -LiteralPath $DestinationDirectory -PathType Container)) {
        [void][IO.Directory]::CreateDirectory($DestinationDirectory)
    }
    Write-DescriptorIfAbsent (Join-Path $DestinationDirectory 'contact-line-profile-fixture.json') ([ordered]@{
        schemaVersion = 1; fixtureId = 'contact-line-profile'; profileAlias = 'sunnyvalechback'; ceVersion = '9.1'; connector = 'Data8'; marker = 'p7.2-contact-line-profile'; contactId = $contactId.ToString('D'); ownerIdentity = $identity
    })
    Write-DescriptorIfAbsent (Join-Path $DestinationDirectory 'ungrouped-commitment-fixture.json') ([ordered]@{
        schemaVersion = 1; fixtureId = 'ungrouped-commitment'; profileAlias = 'sunnyvalechback'; ceVersion = '9.1'; connector = 'Data8'; marker = 'p7.2-ungrouped-commitment'; ownerIdentity = $identity
    })
    [ordered]@{ schemaVersion = 1; outcome = 'written'; descriptorCount = 2 } | ConvertTo-Json -Compress
    exit 0
}
catch {
    $reason = if ($_.Exception.Message -in @('source-descriptor-invalid', 'destination-descriptor-conflict')) { $_.Exception.Message } else { 'descriptor-initialization-failed' }
    [ordered]@{ schemaVersion = 1; outcome = 'error'; reason = $reason; descriptorCount = 0 } | ConvertTo-Json -Compress
    exit 1
}

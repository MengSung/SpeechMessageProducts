<#
.SYNOPSIS
預設只預覽的無損編碼轉換；來源文字與字串常值驗證成功後才允許明確套用。
.DESCRIPTION
辨認 UTF BOM、嚴格 UTF-8 或 CP950，產生無 BOM UTF-8 與 CRLF 候選。
manifest 只保存編碼、雜湊與常值一致性，不輸出任何實際常值，避免將憑證或業務資料複製到診斷。
逐字／raw 字串內的換行若會改變內容，該檔拒絕寫入。每檔以同目錄暫存檔原子替換，失敗保留原檔。
所有檔案與雜湊生命週期只限本次呼叫，暫存檔於 finally 確定清除；不保存跨使用者狀態。
.PARAMETER Path
明確指定要檢查的來源檔案，不遞迴尋找或批次改寫其他檔。
.PARAMETER ManifestPath
必要的 JSON 稽核輸出位置，不能與來源相同；DryRun 只寫此 manifest，不改來源。
.PARAMETER Apply
明確指定才寫入來源；不可與 DryRun 同用。
.PARAMETER DryRun
明確預覽開關；未給 Apply 時預設就是此模式。
#>
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string[]]$Path, [Parameter(Mandatory=$true)][string]$ManifestPath, [switch]$Apply, [switch]$DryRun)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TextEncoding.Common.ps1')
try {
    if ($Apply -and $DryRun) { throw 'Apply and DryRun cannot be combined.' }
    $manifestFile = [IO.Path]::GetFullPath($ManifestPath)
    $utf8 = New-Object Text.UTF8Encoding($false, $true)
    $records = New-Object 'Collections.Generic.List[object]'
    $failures = 0
    foreach ($sourcePath in $Path) {
        $fullPath = [IO.Path]::GetFullPath($sourcePath)
        if ($fullPath.Equals($manifestFile, [StringComparison]::OrdinalIgnoreCase)) { throw 'ManifestPath cannot overwrite a source file.' }
        $record = [ordered]@{ path = $fullPath; originalEncoding = $null; beforeSha256 = $null; afterSha256 = $null; chineseStringLiteralCount = 0; chineseStringLiteralsUnchanged = $false; stringLiteralsUnchanged = $false; status = 'failed'; error = $null }
        $temporary = $null
        try {
            $bytes = [IO.File]::ReadAllBytes($fullPath)
            $record.beforeSha256 = Get-TextSha256 -Bytes $bytes
            $skip = 0
            if ($bytes.Length -ge 4 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE -and $bytes[2] -eq 0 -and $bytes[3] -eq 0) {
                $encoding = New-Object Text.UTF32Encoding($false, $false, $true); $skip = 4; $record.originalEncoding = 'utf-32le-bom'
            }
            elseif ($bytes.Length -ge 4 -and $bytes[0] -eq 0 -and $bytes[1] -eq 0 -and $bytes[2] -eq 0xFE -and $bytes[3] -eq 0xFF) {
                $encoding = New-Object Text.UTF32Encoding($true, $false, $true); $skip = 4; $record.originalEncoding = 'utf-32be-bom'
            }
            elseif ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
                $encoding = $utf8; $skip = 3; $record.originalEncoding = 'utf-8-bom'
            }
            elseif ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) {
                $encoding = New-Object Text.UnicodeEncoding($false, $false, $true); $skip = 2; $record.originalEncoding = 'utf-16le-bom'
            }
            elseif ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF) {
                $encoding = New-Object Text.UnicodeEncoding($true, $false, $true); $skip = 2; $record.originalEncoding = 'utf-16be-bom'
            }
            else {
                try { [void]$utf8.GetString($bytes); $encoding = $utf8; $record.originalEncoding = 'utf-8' }
                catch [Text.DecoderFallbackException] {
                    $encoding = [Text.Encoding]::GetEncoding(950, [Text.EncoderFallback]::ExceptionFallback, [Text.DecoderFallback]::ExceptionFallback)
                    $record.originalEncoding = 'cp950'
                }
            }
            try { $source = $encoding.GetString($bytes, $skip, $bytes.Length - $skip) }
            catch [Text.DecoderFallbackException] { throw 'InvalidEncoding: source cannot be decoded losslessly.' }
            if ($source.Contains([string][char]0xFFFD)) { throw 'InvalidEncoding: source already contains a replacement character.' }
            $candidate = [regex]::Replace($source, '\r\n|\r|\n', "`r`n")
            if (-not $candidate.EndsWith("`r`n", [StringComparison]::Ordinal)) { $candidate += "`r`n" }
            $afterBytes = $utf8.GetBytes($candidate)
            $record.afterSha256 = Get-TextSha256 -Bytes $afterBytes
            $beforeLiterals = @(Get-TextLiteralSegments -Source $source)
            $afterLiterals = @(Get-TextLiteralSegments -Source $candidate)
            $beforeChinese = @($beforeLiterals | Where-Object { $_ -match '[\u3400-\u9FFF\uF900-\uFAFF]' })
            $afterChinese = @($afterLiterals | Where-Object { $_ -match '[\u3400-\u9FFF\uF900-\uFAFF]' })
            $record.chineseStringLiteralCount = $beforeChinese.Count
            $record.chineseStringLiteralsUnchanged = ($beforeChinese.Count -eq $afterChinese.Count)
            for ($i = 0; $record.chineseStringLiteralsUnchanged -and $i -lt $beforeChinese.Count; $i++) {
                if (-not $beforeChinese[$i].Equals($afterChinese[$i], [StringComparison]::Ordinal)) { $record.chineseStringLiteralsUnchanged = $false }
            }
            $record.stringLiteralsUnchanged = ($beforeLiterals.Count -eq $afterLiterals.Count)
            for ($i = 0; $record.stringLiteralsUnchanged -and $i -lt $beforeLiterals.Count; $i++) {
                if (-not $beforeLiterals[$i].Equals($afterLiterals[$i], [StringComparison]::Ordinal)) { $record.stringLiteralsUnchanged = $false }
            }
            if (-not $record.chineseStringLiteralsUnchanged -or -not $record.stringLiteralsUnchanged) { throw 'StringLiteralChanged: normalization would change literal content.' }
            $issues = @(Get-TextByteIssues -Bytes $afterBytes -Extension ([IO.Path]::GetExtension($fullPath).ToLowerInvariant()))
            if ($issues.Count -gt 0) { throw ('CandidateRejected: ' + ($issues -join ', ')) }
            if ($Apply) {
                $temporary = $fullPath + '.encoding-' + [guid]::NewGuid().ToString('N') + '.tmp'
                [IO.File]::WriteAllBytes($temporary, $afterBytes)
                # PS 5.1 會把一般空值轉成空字串路徑，須以 NullString 明確傳入沒有備份檔名的原子替換。
                [IO.File]::Replace($temporary, $fullPath, [NullString]::Value)
                $record.status = 'applied'
            }
            else { $record.status = 'dry-run' }
        }
        catch { $record.error = $_.Exception.Message; $failures++ }
        finally { if ($temporary -and [IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
        $records.Add([pscustomobject]$record)
        if ($record.error) { Write-Output ("FAIL $($record.path): " + $record.error) }
    }
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($manifestFile))
    $manifest = [ordered]@{ dryRun = (-not $Apply); failureCount = $failures; files = @($records.ToArray()) }
    [IO.File]::WriteAllText($manifestFile, (($manifest | ConvertTo-Json -Depth 8) -replace "`r?`n", "`r`n") + "`r`n", $utf8)
    Write-Output "Text conversion: files=$($records.Count) dryRun=$(-not $Apply) failures=$failures"
    if ($failures -gt 0) { exit 1 }
    exit 0
}
catch { Write-Output "FAIL Text conversion: $($_.Exception.Message)"; exit 1 }

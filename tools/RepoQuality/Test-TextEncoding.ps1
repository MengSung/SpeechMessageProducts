<#
檔案整體說明

目的與定位：檢查差異檔案的 UTF-8 無 BOM、CRLF 與 PowerShell 非 ASCII 邊界，或唯讀產生全量盤點。

主要功能與流程：
依下方原生宣告執行本檔所述工作；入口、條件及順序須與實際呼叫端一致，不能把示範或預覽視為已套用。

使用邊界與維護：修改前先核對引用此檔的專案、頁面、腳本或CI步驟，保留既有參數、輸出及失敗結束碼。
涉及寫入、外部命令或暫存資源時，依本檔實際實作的owner及清理路徑處理；說明不能取代執行前的範圍驗證。
#>
<#
.SYNOPSIS
檢查差異檔案的 UTF-8 無 BOM、CRLF 與 PowerShell 非 ASCII 邊界，或唯讀產生全量盤點。
.DESCRIPTION
Changed 只採 git diff 的新增／修改／重新命名路徑；未追蹤新檔須先暫存，避免把本機診斷封包混入發布差異。
All 只讀取此工作樹的 Git 追蹤檔，不沿 reparse point 讀取外部內容；只輸出舊問題報告。
報告只保存路徑與原因，不保存來源文字、個資或憑證；每檔讀取完成即釋放區域參照。
.PARAMETER Scope
Changed 作為發布守門，All 僅供盤點。
.PARAMETER Base
Changed 的 Git 比對起點，不可省略；與階段分支起點比較。
.PARAMETER RepositoryPath
目前工作樹根目錄，預設為本腳本上兩層，不能指向父工作樹作為後備來源。
.PARAMETER ReportPath
可選的 Markdown 報告路徑；All 必須明確指定。
#>
[CmdletBinding()]
param([ValidateSet('Changed','All')][string]$Scope = 'Changed', [string]$Base, [string]$RepositoryPath, [string]$ReportPath)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TextEncoding.Common.ps1')
try {
    if (-not $RepositoryPath) { $RepositoryPath = Join-Path $PSScriptRoot '..\..' }
    $root = (Resolve-Path -LiteralPath $RepositoryPath).Path
    $actualRoot = (Invoke-TextQualityGit -Root $root -Arguments @('rev-parse','--show-toplevel')).Trim()
    if (-not [IO.Path]::GetFullPath($actualRoot).Equals([IO.Path]::GetFullPath($root), [StringComparison]::OrdinalIgnoreCase)) { throw 'RepositoryPath must identify this Git worktree root.' }
    if ($Scope -eq 'Changed') {
        if ([string]::IsNullOrWhiteSpace($Base) -or $Base.StartsWith('-')) { throw 'Changed scope requires a valid Base revision.' }
        [string]$listing = Invoke-TextQualityGit -Root $root -Arguments @('diff','--name-only','--diff-filter=AMR','-z',$Base,'--')
    }
    else {
        if (-not $ReportPath) { throw 'All scope requires ReportPath.' }
        # All 只代表版控中的來源快照；未追蹤檔可能是外部工具輸出或機密，禁止被全量盤點吸入。
        [string]$listing = Invoke-TextQualityGit -Root $root -Arguments @('ls-files','--cached','-z')
    }
    [string[]]$paths = @($listing.Split([char]0) | Where-Object { $_ })
    [array]::Sort($paths, [StringComparer]::Ordinal)
    $extensions = @('.cs','.cshtml','.csproj','.props','.targets','.json','.md','.ps1','.resx','.xml','.yml')
    $violations = New-Object 'Collections.Generic.List[object]'
    $checked = 0
    $excluded = 0
    foreach ($relative in $paths) {
        $extension = [IO.Path]::GetExtension($relative).ToLowerInvariant()
        if ($extensions -notcontains $extension) { continue }
        # 即使誤被追蹤，也不讀取其他工作樹、參考舊方案或建置產物。
        if ($relative -match '(^|/)(\.worktrees|\.\u5176\u4ED6\u89E3\u6C7A\u65B9\u6848|bin|obj)(/|$)') { $excluded++; continue }
        # 僅排除具時間戳記的 CCG 原始執行目錄；手寫提示詞與審查包仍須接受編碼守門。
        # 機器產出的 prompt/stdout/stderr 保留原始位元組，不以修格式改寫診斷證據。
        if ($relative -match '^\.ccg/dual-model-runs/[0-9]{8}-[0-9]{6}-[^/]+/') { $excluded++; continue }
        $path = Join-Path $root $relative
        if (-not [IO.File]::Exists($path)) { continue }
        $checked++
        $rootPrefix = [IO.Path]::GetFullPath($root).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        $safe = [IO.Path]::GetFullPath($path).StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)
        $cursor = $root
        foreach ($segment in $relative.Split([char]'/')) {
            if (-not $safe) { break }
            $cursor = Join-Path $cursor $segment
            if (([IO.File]::GetAttributes($cursor) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { $safe = $false }
        }
        if (-not $safe) { $issues = @('UnsafePath') }
        else { $issues = @(Get-TextByteIssues -Bytes ([IO.File]::ReadAllBytes($path)) -Extension $extension) }
        if ($issues.Count -gt 0) { $violations.Add([pscustomobject]@{ Path = $relative; Issues = $issues }) }
    }
    if ($ReportPath) {
        $report = New-Object 'Collections.Generic.List[string]'
        $report.Add('# Text encoding inventory')
        $report.Add('')
        $report.Add("Scope: $Scope; checked=$checked; excluded=$excluded; filesWithIssues=$($violations.Count).")
        $report.Add('')
        $report.Add('| File | Issues |')
        $report.Add('| --- | --- |')
        foreach ($violation in $violations) { $report.Add('| ' + $violation.Path.Replace('|','\|') + ' | ' + ($violation.Issues -join ', ') + ' |') }
        $destination = [IO.Path]::GetFullPath($ReportPath)
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
        [IO.File]::WriteAllText($destination, ($report -join "`r`n") + "`r`n", (New-Object Text.UTF8Encoding($false)))
    }
    if ($Scope -eq 'Changed') { foreach ($violation in $violations) { Write-Output ("FAIL $($violation.Path): " + ($violation.Issues -join ', ')) } }
    Write-Output "Text encoding: scope=$Scope checked=$checked excluded=$excluded filesWithIssues=$($violations.Count)"
    if ($Scope -eq 'Changed' -and $violations.Count -gt 0) { exit 1 }
    exit 0
}
catch { Write-Output "FAIL Text encoding: $($_.Exception.Message)"; exit 1 }

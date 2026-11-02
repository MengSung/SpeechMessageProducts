<#
.SYNOPSIS
檢查實際專案的中央套件版本契約，拒絕內嵌版本與套件降版警告抑制。
.DESCRIPTION
以 Git 的已追蹤與未忽略新檔清單為界，不走訪父工作樹、建置產物或參考舊方案。
文件快照與 CCG 任務封存不在套件管理範圍；XML 註解也不是有效依賴。
僅唯讀解析專案，不載入或執行 MSBuild、不還原套件、不改寫舊編碼。
舊 Trace 專案尚有 CP950 註解，因此只在嚴格 UTF-8 解碼失敗後唯讀使用 CP950；
編碼合法性由專用編碼守門負責，本檢查不會以替代字元掩蓋解碼錯誤。
Git 程序與重新導向串流由單次呼叫擁有；三十秒內須完成，逾時終止並確定釋放。
.PARAMETER RepositoryPath
要檢查的工作樹根目錄；預設為本腳本上兩層，測試可指定隔離的暫存 Git 目錄。
#>
[CmdletBinding()]
param(
    [string]$RepositoryPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

try {
    if (-not $RepositoryPath) { $RepositoryPath = Join-Path $PSScriptRoot '..\..' }
    $root = (Resolve-Path -LiteralPath $RepositoryPath).Path
    $utf8 = New-Object System.Text.UTF8Encoding($false, $true)
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = 'git'
    $startInfo.Arguments = '-c core.longpaths=true -c core.quotepath=false ls-files --cached --others --exclude-standard -z -- "*.csproj"'
    $startInfo.WorkingDirectory = $root
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = $utf8
    $startInfo.StandardErrorEncoding = $utf8
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) {
            $process.Kill()
            $process.WaitForExit()
            throw 'Git project enumeration timed out.'
        }
        $fileList = $stdout.GetAwaiter().GetResult()
        $gitError = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "Git project enumeration failed: $gitError" }
    }
    finally {
        $process.Dispose()
    }

    $failures = New-Object 'System.Collections.Generic.List[string]'
    $checked = 0
    $references = 0
    $excluded = 0
    $files = @($fileList.Split([char]0) | Where-Object { $_ } | Sort-Object -Unique)
    foreach ($relativePath in $files) {
        if ($relativePath -match '^(docs/|\.ccg/tasks/)') { $excluded++; continue }
        $path = Join-Path $root $relativePath
        if (-not [System.IO.File]::Exists($path)) { continue }
        $bytes = [System.IO.File]::ReadAllBytes($path)
        try { $source = $utf8.GetString($bytes) }
        catch [System.Text.DecoderFallbackException] {
            $legacyEncoding = [System.Text.Encoding]::GetEncoding(
                950, [System.Text.EncoderFallback]::ExceptionFallback,
                [System.Text.DecoderFallback]::ExceptionFallback)
            $source = $legacyEncoding.GetString($bytes)
        }
        $document = New-Object System.Xml.XmlDocument
        $document.XmlResolver = $null
        $document.LoadXml($source.TrimStart([char]0xFEFF))
        $checked++
        foreach ($reference in $document.SelectNodes("//*[local-name()='PackageReference']")) {
            $references++
            if ($reference.HasAttribute('Version') -or $reference.SelectSingleNode("*[local-name()='Version']")) {
                $identity = $reference.GetAttribute('Include')
                if (-not $identity) { $identity = $reference.GetAttribute('Update') }
                $failures.Add("InlineVersion: $relativePath [$identity]")
            }
        }
        foreach ($suppression in $document.SelectNodes("//*[local-name()='NoWarn'] | //@NoWarn")) {
            if ($suppression.InnerText -match '(?i)(^|[;,\s])NU1605($|[;,\s])') {
                $failures.Add("SuppressedNU1605: $relativePath")
            }
        }
    }
    foreach ($failure in $failures) { Write-Output "FAIL $failure" }
    Write-Output "Package guard: projects=$checked references=$references excluded=$excluded failures=$($failures.Count)"
    if ($checked -eq 0) { throw 'No program projects found; refusing an empty package check.' }
    if ($failures.Count -gt 0) { exit 1 }
    exit 0
}
catch {
    Write-Output "FAIL Package guard: $($_.Exception.Message)"
    exit 1
}

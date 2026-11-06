<#
檔案整體說明

目的與定位：檢查實際專案的中央套件版本契約，拒絕內嵌版本與套件降版警告抑制。

主要功能與流程：
操作入口包含Get-SafePackagePath；呼叫參數與輸出形狀以各入口宣告為準。

使用邊界與維護：修改前先核對引用此檔的專案、頁面、腳本或CI步驟，保留既有參數、輸出及失敗結束碼。
涉及寫入、外部命令或暫存資源時，依本檔實際實作的owner及清理路徑處理；說明不能取代執行前的範圍驗證。
#>
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

function Get-SafePackagePath {
    <# 任何專案、中央表與固定依賴檢查都先逐段拒絕 reparse point，包含 NTFS junction。
       先驗證本工作樹絕對前綴，再檢查元件屬性；不讓 XML 讀取越過索引宣告的邊界。 #>
    param([string]$Relative)
    $full = [IO.Path]::GetFullPath((Join-Path $script:packageRoot $Relative))
    if (-not $full.StartsWith($script:packageRootPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'UnsafePath: package source is outside this worktree.' }
    $cursor = $script:packageRoot
    foreach ($segment in $full.Substring($script:packageRootPrefix.Length).Split([IO.Path]::DirectorySeparatorChar)) {
        $cursor = Join-Path $cursor $segment
        if (([IO.File]::Exists($cursor) -or [IO.Directory]::Exists($cursor)) -and
            (([IO.File]::GetAttributes($cursor) -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
            throw 'UnsafePath: package source contains a reparse point.'
        }
    }
    return $full
}

try {
    if (-not $RepositoryPath) { $RepositoryPath = Join-Path $PSScriptRoot '..\..' }
    $root = (Resolve-Path -LiteralPath $RepositoryPath).Path
    $script:packageRoot = $root
    $script:packageRootPrefix = [IO.Path]::GetFullPath($root).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
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
        # 與編碼／重複掃描共用已核定的排除邊界；實際孤兒專案仍在盤點範圍內。
        if ($relativePath -match '(^|/)(\.worktrees|\.\u5176\u4ED6\u89E3\u6C7A\u65B9\u6848|bin|obj)(/|$)') { $excluded++; continue }
        $path = Get-SafePackagePath $relativePath
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
    $centralPath = Get-SafePackagePath 'Directory.Packages.props'
    if (-not [System.IO.File]::Exists($centralPath)) { throw 'Central package version table is missing.' }
    $central = New-Object System.Xml.XmlDocument
    $central.XmlResolver = $null
    $central.Load($centralPath)
    foreach ($package in @('System.Security.Cryptography.Xml', 'System.Security.Cryptography.Pkcs')) {
        $version = $central.SelectSingleNode("//*[local-name()='PackageVersion' and @Include='$package']")
        if ($null -eq $version -or $version.GetAttribute('Version') -ne '10.0.14') {
            $failures.Add("CryptographySecurityFix: Directory.Packages.props [$package] must be 10.0.14")
        }
    }
    $connectionProjectPath = Get-SafePackagePath 'PowerPlatform.Dataverse.Client/PowerPlatform.Dataverse.Client.csproj'
    if (-not [System.IO.File]::Exists($connectionProjectPath)) { throw 'On-premises connection project is missing.' }
    $connectionProject = New-Object System.Xml.XmlDocument
    $connectionProject.XmlResolver = $null
    $connectionProject.Load($connectionProjectPath)
    foreach ($package in @('System.Security.Cryptography.Xml', 'System.Security.Cryptography.Pkcs')) {
        $reference = $connectionProject.SelectSingleNode("//*[local-name()='PackageReference' and @Include='$package']")
        if ($null -eq $reference -or $reference.HasAttribute('Version') -or
            $reference.SelectSingleNode("*[local-name()='Version']")) {
            $failures.Add("CryptographyDirectDependency: PowerPlatform.Dataverse.Client [$package] must directly reference its centrally managed version")
        }
    }
    if ($central.SelectSingleNode("//*[local-name()='PropertyGroup']/*[local-name()='CentralPackageTransitivePinningEnabled' and translate(text(),'TRUE','true')='true']")) {
        $failures.Add('CryptographyCentralPinning: CentralPackageTransitivePinningEnabled must not be enabled.')
    }
    $systemTextJsonVersion = $central.SelectSingleNode("//*[local-name()='PackageVersion' and @Include='System.Text.Json']")
    if ($null -eq $systemTextJsonVersion -or $systemTextJsonVersion.GetAttribute('Version') -ne '6.0.11') {
        $failures.Add('PluginJsonSecurityFix: Directory.Packages.props [System.Text.Json] must be 6.0.11')
    }
    foreach ($pluginProjectRelativePath in @(
        'SpeechMessage.Dynamics.Plugins/SpeechMessage.Dynamics.Plugins.csproj',
        'SpeechMessage.Dynamics.Plugins.Tests/SpeechMessage.Dynamics.Plugins.Tests.csproj'
    )) {
        $pluginProjectPath = Get-SafePackagePath $pluginProjectRelativePath
        if (-not [System.IO.File]::Exists($pluginProjectPath)) { throw "Dynamics plugin project is missing: $pluginProjectRelativePath" }
        $pluginProject = New-Object System.Xml.XmlDocument
        $pluginProject.XmlResolver = $null
        $pluginProject.Load($pluginProjectPath)
        $pluginJsonReference = $pluginProject.SelectSingleNode("//*[local-name()='PackageReference' and @Include='System.Text.Json']")
        if ($null -eq $pluginJsonReference -or $pluginJsonReference.HasAttribute('Version') -or
            $pluginJsonReference.SelectSingleNode("*[local-name()='Version']") -or
            $pluginJsonReference.GetAttribute('PrivateAssets') -ne 'all') {
            $failures.Add("PluginJsonPrivateReference: $pluginProjectRelativePath must directly reference centrally managed System.Text.Json with PrivateAssets=all")
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

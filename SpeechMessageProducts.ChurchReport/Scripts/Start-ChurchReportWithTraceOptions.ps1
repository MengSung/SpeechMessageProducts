<#
檔案整體說明

目的與定位：以可選診斷開關啟動ChurchReport，將Dataverse、應用效能及舊ToolUtility追蹤設定交給子程序，維持使用者可調整的單一設定區。

主要功能與流程：
操作入口包含Test-TcpPortReady、Stop-ExistingChurchReportForPort；呼叫參數與輸出形狀以各入口宣告為準。

使用邊界與維護：修改前先核對引用此檔的專案、頁面、腳本或CI步驟，保留既有參數、輸出及失敗結束碼。
涉及寫入、外部命令或暫存資源時，依本檔實際實作的owner及清理路徑處理；說明不能取代執行前的範圍驗證。
#>
# ChurchReport 可調整組態的啟動範例
#
# 使用方式：
# 1. 只修改下方「使用者設定區」的參數。
# 2. 使用 Windows PowerShell 執行本檔案；不需要先設定全域環境變數。
# 3. 本腳本只在目前 PowerShell 程序及其子程序中設定 DiagnosticsTrace，
#    結束後不會寫入 Windows 使用者或系統環境變數。
#
# 注意：$diagnosticTraceEnabled = $false 只會停止新增／追加 Trace，
# 不會刪除 D:\除錯追蹤 中既有的 dataverse-trace.jsonl、Trace.log 或
# CHURCH_REPORT_TRACE.TXT。

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ======================== 使用者設定區 ========================
# 可選：'Debug' 或 'Release'。
$configuration = 'Debug'

# 是否讓 Debug 組態建立三種診斷 Trace。
$diagnosticTraceEnabled = $true

# 三種 Trace 共用的輸出目錄；只由本機啟動設定提供，不來自 request 或使用者輸入。
$traceDirectory = ([regex]::Unescape('D:\\\u9664\u932f\u8ffd\u8e64'))

# 網站網址。
$url = 'http://localhost:5000/'

# $true：只編譯，不啟動網站；$false：編譯後啟動網站。
$buildOnly = $false

# $true：啟動網站後不開啟瀏覽器；$false：自動開啟瀏覽器。
$skipBrowser = $false

# $true：編譯／啟動前先停止本機既有的 ChurchReport；$false：保留既有網站程序。
$stopExistingWebsite = $true

# ====================== 使用者設定區結束 ======================

$utf8 = [System.Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = $utf8
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDirectory = (Resolve-Path (Join-Path $scriptDirectory '..')).Path
$projectPath = Join-Path $projectDirectory 'SpeechMessageProducts.ChurchReport.csproj'
$existingStarter = Join-Path $scriptDirectory 'Start-ChurchReportDevelopment.ps1'

if ($configuration -notin @('Debug', 'Release')) {
    throw "configuration $([regex]::Unescape('\u5fc5\u9808\u662f')) 'Debug' $([regex]::Unescape('\u6216')) 'Release'$([regex]::Unescape('\u3002\u76ee\u524d\u503c\uff1a'))$configuration"
}

if ($diagnosticTraceEnabled -isnot [bool]) {
    throw ([regex]::Unescape('diagnosticTraceEnabled \u5fc5\u9808\u662f $true \u6216 $false\u3002'))
}

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "$([regex]::Unescape('\u627e\u4e0d\u5230')) ChurchReport $([regex]::Unescape('\u5c08\u6848\u6a94\uff1a'))$projectPath"
}

if (-not (Test-Path -LiteralPath $existingStarter -PathType Leaf)) {
    throw "$([regex]::Unescape('\u627e\u4e0d\u5230\u65e2\u6709\u555f\u52d5\u8173\u672c\uff1a'))$existingStarter"
}

$traceDirectoryFull = [System.IO.Path]::GetFullPath($traceDirectory)
$dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction Stop
$traceEnabledText = $diagnosticTraceEnabled.ToString().ToLowerInvariant()
$targetUri = [System.Uri]::new($url)

if ($targetUri.Scheme -notin @('http', 'https') -or [string]::IsNullOrWhiteSpace($targetUri.Host)) {
    throw "$([regex]::Unescape('\u7db2\u5740\u5fc5\u9808\u662f\u6709\u6548\u7684')) HTTP $([regex]::Unescape('\u6216')) HTTPS URL$([regex]::Unescape('\uff1a'))$url"
}

function Test-TcpPortReady {
    param(
        [Parameter(Mandatory = $true)][string]$HostName,
        [Parameter(Mandatory = $true)][int]$Port
    )

    $tcpClient = [System.Net.Sockets.TcpClient]::new()
    try {
        $connectionTask = $tcpClient.ConnectAsync($HostName, $Port)
        return $connectionTask.Wait(1000) -and $tcpClient.Connected
    }
    catch {
        return $false
    }
    finally {
        $tcpClient.Dispose()
    }
}

function Stop-ExistingChurchReportForPort {
    param(
        [Parameter(Mandatory = $true)][int]$Port,
        [Parameter(Mandatory = $true)][string]$ProjectDirectory
    )

    $ownerProcessIds = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction Stop |
        Select-Object -ExpandProperty OwningProcess -Unique)
    if ($ownerProcessIds.Count -eq 0) {
        return
    }

    $normalizedProjectDirectory = [System.IO.Path]::GetFullPath($ProjectDirectory).TrimEnd('\')
    $churchReportProcessIds = New-Object System.Collections.Generic.List[int]
    $unknownProcessIds = New-Object System.Collections.Generic.List[int]

    foreach ($ownerProcessId in $ownerProcessIds) {
        $processInfo = Get-CimInstance Win32_Process `
            -Filter ("ProcessId = {0}" -f $ownerProcessId) `
            -ErrorAction SilentlyContinue
        $commandLine = if ($null -ne $processInfo) { [string]$processInfo.CommandLine } else { '' }
        $processName = if ($null -ne $processInfo) { [string]$processInfo.Name } else { '' }
        $belongsToChurchReport =
            (-not [string]::IsNullOrWhiteSpace($commandLine)) -and
            (($commandLine.IndexOf($normalizedProjectDirectory, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) -or
             ($commandLine.IndexOf('SpeechMessageProducts.ChurchReport', [System.StringComparison]::OrdinalIgnoreCase) -ge 0))

        if ($belongsToChurchReport) {
            [void]$churchReportProcessIds.Add([int]$ownerProcessId)
        }
        else {
            [void]$unknownProcessIds.Add([int]$ownerProcessId)
            Write-Host "$([regex]::Unescape('\u7121\u6cd5\u78ba\u8a8d\u7a0b\u5e8f')) $ownerProcessId$([regex]::Unescape('\uff08'))$processName$([regex]::Unescape('\uff09\u662f\u5426\u5c6c\u65bc')) ChurchReport$([regex]::Unescape('\u3002'))" -ForegroundColor Yellow
        }
    }

    if ($unknownProcessIds.Count -gt 0) {
        throw "$([regex]::Unescape('\u9023\u63a5\u57e0')) $Port $([regex]::Unescape('\u5df2\u88ab\u5176\u4ed6\u6216\u7121\u6cd5\u8fa8\u8b58\u7684\u7a0b\u5e8f\u4f7f\u7528\uff1b\u70ba\u907f\u514d\u8aa4\u6bba\uff0c\u8173\u672c\u4e0d\u6703\u81ea\u52d5\u7d42\u6b62\u3002'))PID$([regex]::Unescape('\uff1a'))$($unknownProcessIds -join ', ')"
    }

    foreach ($churchReportProcessId in $churchReportProcessIds) {
        Write-Host "$([regex]::Unescape('\u6b63\u5728\u505c\u6b62\u65e2\u6709')) ChurchReport $([regex]::Unescape('\u7a0b\u5e8f\uff08'))PID $churchReportProcessId$([regex]::Unescape('\uff09'))..." -ForegroundColor Yellow
        & taskkill.exe /PID $churchReportProcessId /T /F 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "$([regex]::Unescape('\u7121\u6cd5\u505c\u6b62')) ChurchReport $([regex]::Unescape('\u7a0b\u5e8f\uff08'))PID $churchReportProcessId$([regex]::Unescape('\uff09\uff0c\u7d50\u675f\u78bc\uff1a'))$LASTEXITCODE"
        }
    }

    $stopDeadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $stopDeadline) {
        $remainingProcessIds = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty OwningProcess -Unique)
        if ($remainingProcessIds.Count -eq 0) {
            Write-Host "$([regex]::Unescape('\u65e2\u6709')) ChurchReport $([regex]::Unescape('\u5df2\u505c\u6b62\uff0c\u9023\u63a5\u57e0')) $Port $([regex]::Unescape('\u5df2\u91cb\u653e\u3002'))" -ForegroundColor Green
            return
        }

        Start-Sleep -Milliseconds 250
    }

    throw "$([regex]::Unescape('\u65e2\u6709')) ChurchReport $([regex]::Unescape('\u5df2\u8981\u6c42\u505c\u6b62\uff0c\u4f46\u9023\u63a5\u57e0')) $Port $([regex]::Unescape('\u4ecd\u88ab\u5360\u7528\u3002'))"
}

$previousTraceEnabled = [Environment]::GetEnvironmentVariable('DiagnosticsTrace__Enabled', 'Process')
$previousTraceDirectory = [Environment]::GetEnvironmentVariable('DiagnosticsTrace__Directory', 'Process')

try {
    # 這些設定只供本腳本及其子程序使用；不是永久的 Windows 環境變數。
    $env:DiagnosticsTrace__Enabled = $traceEnabledText
    $env:DiagnosticsTrace__Directory = $traceDirectoryFull

    if ($stopExistingWebsite -and (Test-TcpPortReady -HostName $targetUri.DnsSafeHost -Port $targetUri.Port)) {
        if ($targetUri.Host -in @('localhost', '127.0.0.1', '::1')) {
            Stop-ExistingChurchReportForPort -Port $targetUri.Port -ProjectDirectory $projectDirectory
        }
        else {
            throw "$([regex]::Unescape('\u7db2\u5740\u76ee\u524d\u5df2\u88ab\u5176\u4ed6\u7a0b\u5e8f\u4f7f\u7528\uff1a'))$url$([regex]::Unescape('\uff1b\u975e\u672c\u6a5f\u7db2\u5740\u4e0d\u6703\u81ea\u52d5\u7d42\u6b62\u7a0b\u5e8f\u3002'))"
        }
    }

Write-Host '========================================' -ForegroundColor Magenta
Write-Host ([regex]::Unescape('ChurchReport \u7d44\u614b\u555f\u52d5\u7bc4\u4f8b')) -ForegroundColor Magenta
Write-Host '========================================' -ForegroundColor Magenta
Write-Host "$([regex]::Unescape('\u7de8\u8b6f\u7d44\u614b\uff1a'))$configuration" -ForegroundColor Gray
Write-Host "DiagnosticsTrace:Enabled$([regex]::Unescape('\uff1a'))$traceEnabledText" -ForegroundColor Gray
Write-Host "Trace $([regex]::Unescape('\u76ee\u9304\uff1a'))$traceDirectoryFull" -ForegroundColor Gray
Write-Host "$([regex]::Unescape('\u7db2\u5740\uff1a'))$url" -ForegroundColor Gray

if ($configuration -eq 'Release' -and $diagnosticTraceEnabled) {
    Write-Warning ([regex]::Unescape('Release \u7d44\u614b\u5177\u6709\u7de8\u8b6f\u671f\u505c\u7528\u9632\u7dda\uff1b\u5373\u4f7f\u8a2d\u5b9a true\uff0c\u6b63\u5f0f\u7d44\u614b\u4ecd\u4e0d\u6703\u5efa\u7acb\u4e09\u7a2e\u6a94\u6848 Trace\u3002'))
}

    if ($buildOnly) {
        Write-Host "$([regex]::Unescape('\u958b\u59cb\u7de8\u8b6f')) $configuration ..." -ForegroundColor Cyan
        & $dotnetCommand.Source build $projectPath --configuration $configuration --nologo --property:UseAppHost=false
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build $([regex]::Unescape('\u5931\u6557\uff0c\u7d50\u675f\u78bc\uff1a'))$LASTEXITCODE"
        }

        Write-Host ([regex]::Unescape('\u7de8\u8b6f\u6210\u529f\uff1b\u56e0\u70ba buildOnly=$true\uff0c\u672a\u555f\u52d5\u7db2\u7ad9\u3002')) -ForegroundColor Green
        return
    }

$starterArguments = @(
    '-NoProfile'
    '-ExecutionPolicy'
    'Bypass'
    '-File'
    $existingStarter
    '-Configuration'
    $configuration
    '-Url'
    $url
)

if ($skipBrowser) {
    $starterArguments += '-SkipBrowser'
}

Write-Host ([regex]::Unescape('\u4ea4\u7531\u65e2\u6709 Start-ChurchReportDevelopment.ps1 \u7de8\u8b6f\u3001\u555f\u52d5\u7db2\u7ad9\u4e26\u7ba1\u7406\u7a0b\u5e8f\u751f\u547d\u9031\u671f ...')) -ForegroundColor Cyan
    & powershell.exe @starterArguments
    if ($LASTEXITCODE -ne 0) {
        throw "$([regex]::Unescape('\u65e2\u6709')) ChurchReport $([regex]::Unescape('\u555f\u52d5\u8173\u672c\u5931\u6557\uff0c\u7d50\u675f\u78bc\uff1a'))$LASTEXITCODE"
    }
}
finally {
    [Environment]::SetEnvironmentVariable('DiagnosticsTrace__Enabled', $previousTraceEnabled, 'Process')
    [Environment]::SetEnvironmentVariable('DiagnosticsTrace__Directory', $previousTraceDirectory, 'Process')
}

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
$configuration = 'Release'

# 是否讓 Debug 組態建立三種診斷 Trace。
$diagnosticTraceEnabled = $false

# 三種 Trace 共用的輸出目錄；只由本機啟動設定提供，不來自 request 或使用者輸入。
$traceDirectory = 'D:\除錯追蹤'

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
    throw "configuration 必須是 'Debug' 或 'Release'。目前值：$configuration"
}

if ($diagnosticTraceEnabled -isnot [bool]) {
    throw 'diagnosticTraceEnabled 必須是 $true 或 $false。'
}

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "找不到 ChurchReport 專案檔：$projectPath"
}

if (-not (Test-Path -LiteralPath $existingStarter -PathType Leaf)) {
    throw "找不到既有啟動腳本：$existingStarter"
}

$traceDirectoryFull = [System.IO.Path]::GetFullPath($traceDirectory)
$dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction Stop
$traceEnabledText = $diagnosticTraceEnabled.ToString().ToLowerInvariant()
$targetUri = [System.Uri]::new($url)

if ($targetUri.Scheme -notin @('http', 'https') -or [string]::IsNullOrWhiteSpace($targetUri.Host)) {
    throw "網址必須是有效的 HTTP 或 HTTPS URL：$url"
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
            Write-Host "無法確認程序 $ownerProcessId（$processName）是否屬於 ChurchReport。" -ForegroundColor Yellow
        }
    }

    if ($unknownProcessIds.Count -gt 0) {
        throw "連接埠 $Port 已被其他或無法辨識的程序使用；為避免誤殺，腳本不會自動終止。PID：$($unknownProcessIds -join ', ')"
    }

    foreach ($churchReportProcessId in $churchReportProcessIds) {
        Write-Host "正在停止既有 ChurchReport 程序（PID $churchReportProcessId）..." -ForegroundColor Yellow
        & taskkill.exe /PID $churchReportProcessId /T /F 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "無法停止 ChurchReport 程序（PID $churchReportProcessId），結束碼：$LASTEXITCODE"
        }
    }

    $stopDeadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $stopDeadline) {
        $remainingProcessIds = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty OwningProcess -Unique)
        if ($remainingProcessIds.Count -eq 0) {
            Write-Host "既有 ChurchReport 已停止，連接埠 $Port 已釋放。" -ForegroundColor Green
            return
        }

        Start-Sleep -Milliseconds 250
    }

    throw "既有 ChurchReport 已要求停止，但連接埠 $Port 仍被占用。"
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
            throw "網址目前已被其他程序使用：$url；非本機網址不會自動終止程序。"
        }
    }

Write-Host '========================================' -ForegroundColor Magenta
Write-Host 'ChurchReport 組態啟動範例' -ForegroundColor Magenta
Write-Host '========================================' -ForegroundColor Magenta
Write-Host "編譯組態：$configuration" -ForegroundColor Gray
Write-Host "DiagnosticsTrace:Enabled：$traceEnabledText" -ForegroundColor Gray
Write-Host "Trace 目錄：$traceDirectoryFull" -ForegroundColor Gray
Write-Host "網址：$url" -ForegroundColor Gray

if ($configuration -eq 'Release' -and $diagnosticTraceEnabled) {
    Write-Warning 'Release 組態具有編譯期停用防線；即使設定 true，正式組態仍不會建立三種檔案 Trace。'
}

    if ($buildOnly) {
        Write-Host "開始編譯 $configuration ..." -ForegroundColor Cyan
        & $dotnetCommand.Source build $projectPath --configuration $configuration --nologo --property:UseAppHost=false
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build 失敗，結束碼：$LASTEXITCODE"
        }

        Write-Host '編譯成功；因為 buildOnly=$true，未啟動網站。' -ForegroundColor Green
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

Write-Host '交由既有 Start-ChurchReportDevelopment.ps1 編譯、啟動網站並管理程序生命週期 ...' -ForegroundColor Cyan
    & powershell.exe @starterArguments
    if ($LASTEXITCODE -ne 0) {
        throw "既有 ChurchReport 啟動腳本失敗，結束碼：$LASTEXITCODE"
    }
}
finally {
    [Environment]::SetEnvironmentVariable('DiagnosticsTrace__Enabled', $previousTraceEnabled, 'Process')
    [Environment]::SetEnvironmentVariable('DiagnosticsTrace__Directory', $previousTraceDirectory, 'Process')
}

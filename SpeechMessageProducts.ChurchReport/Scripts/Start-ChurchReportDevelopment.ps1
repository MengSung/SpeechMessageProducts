<#
檔案整體說明

目的與定位：以Development環境啟動ChurchReport，限制測試組織及必要開發設定，將設定限制在目前程序及其子程序，不改正式部署組態。

主要功能與流程：
操作入口包含Stop-ServerProcessTree、Test-TcpPortReady、Stop-ExistingChurchReportForPort；呼叫參數與輸出形狀以各入口宣告為準。

使用邊界與維護：修改前先核對引用此檔的專案、頁面、腳本或CI步驟，保留既有參數、輸出及失敗結束碼。
涉及寫入、外部命令或暫存資源時，依本檔實際實作的owner及清理路徑處理；說明不能取代執行前的範圍驗證。
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [ValidatePattern('^https?://')]
    [string]$Url = 'http://localhost:5000/',

    [ValidateRange(5, 600)]
    [int]$StartupTimeoutSeconds = 60,

    [switch]$SkipBrowser
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# 這個腳本可能由 Windows PowerShell 5.1 執行；明確設定三個輸出編碼來源，
# 避免 PowerShell、.NET 子程序與主控台各自使用不同編碼而產生中文亂碼。
$utf8 = [System.Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = $utf8
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDirectory = (Resolve-Path (Join-Path $scriptDirectory '..')).Path
$projectPath = Join-Path $projectDirectory 'SpeechMessageProducts.ChurchReport.csproj'
$serverProcess = $null

function Stop-ServerProcessTree {
    param(
        [System.Diagnostics.Process]$Process
    )

    if ($null -eq $Process -or $Process.HasExited) {
        return
    }

    # dotnet run 可能再啟動一個應用程式子程序；使用 /T 確保 Ctrl+C、
    # 啟動失敗與正常離開都不會留下孤兒網站程序或持續占用 5000 埠。
    & taskkill.exe /PID $Process.Id /T /F 2>$null | Out-Null
}

function Test-TcpPortReady {
    param(
        [string]$HostName,
        [int]$Port
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
        [int]$Port,
        [string]$ProjectDirectory
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

        # 只接受命令列能證明屬於本專案的程序。若命令列無法取得，
        # 不能以「它剛好占用 5000 埠」推論其身分，避免誤終止其他服務。
        $belongsToChurchReport =
            (-not [string]::IsNullOrWhiteSpace($commandLine)) -and
            (($commandLine.IndexOf($normalizedProjectDirectory, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) -or
             ($commandLine.IndexOf('SpeechMessageProducts.ChurchReport', [System.StringComparison]::OrdinalIgnoreCase) -ge 0))

        if ($belongsToChurchReport) {
            $churchReportProcessIds.Add([int]$ownerProcessId)
        }
        else {
            $unknownProcessIds.Add([int]$ownerProcessId)
            Write-Host "$([regex]::Unescape('\u7121\u6cd5\u78ba\u8a8d\u7a0b\u5e8f')) $ownerProcessId$([regex]::Unescape('\uff08'))$processName$([regex]::Unescape('\uff09\u662f\u5426\u5c6c\u65bc')) ChurchReport$([regex]::Unescape('\u3002'))" -ForegroundColor Yellow
        }
    }

    if ($unknownProcessIds.Count -gt 0) {
        throw "$([regex]::Unescape('\u9023\u63a5\u57e0')) $Port $([regex]::Unescape('\u5df2\u88ab\u5176\u4ed6\u6216\u7121\u6cd5\u8fa8\u8b58\u7684\u7a0b\u5e8f\u4f7f\u7528\uff1b\u70ba\u907f\u514d\u8aa4\u6bba\uff0c\u8173\u672c\u4e0d\u6703\u81ea\u52d5\u7d42\u6b62\u3002'))PID$([regex]::Unescape('\uff1a'))$($unknownProcessIds -join ', ')"
    }

    foreach ($churchReportProcessId in $churchReportProcessIds) {
        Write-Host "[0/3] $([regex]::Unescape('\u6b63\u5728\u505c\u6b62\u65e2\u6709')) ChurchReport $([regex]::Unescape('\u7a0b\u5e8f\uff08'))PID $churchReportProcessId$([regex]::Unescape('\uff09'))..." -ForegroundColor Yellow
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
            Write-Host "[0/3] $([regex]::Unescape('\u65e2\u6709')) ChurchReport $([regex]::Unescape('\u5df2\u505c\u6b62\uff0c\u9023\u63a5\u57e0')) $Port $([regex]::Unescape('\u5df2\u91cb\u653e\u3002'))" -ForegroundColor Green
            return
        }

        Start-Sleep -Milliseconds 250
    }

    throw "$([regex]::Unescape('\u65e2\u6709')) ChurchReport $([regex]::Unescape('\u5df2\u8981\u6c42\u505c\u6b62\uff0c\u4f46\u9023\u63a5\u57e0')) $Port $([regex]::Unescape('\u4ecd\u88ab\u5360\u7528\u3002'))"
}

try {
    $dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction Stop

    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "$([regex]::Unescape('\u627e\u4e0d\u5230')) ChurchReport $([regex]::Unescape('\u5c08\u6848\u6a94\uff1a'))$projectPath"
    }

    $targetUri = [System.Uri]::new($Url)
    if ($targetUri.Scheme -notin @('http', 'https') -or [string]::IsNullOrWhiteSpace($targetUri.Host)) {
        throw "$([regex]::Unescape('\u7db2\u7ad9\u7db2\u5740\u5fc5\u9808\u662f\u6709\u6548\u7684')) HTTP $([regex]::Unescape('\u6216')) HTTPS URL$([regex]::Unescape('\uff1a'))$Url"
    }

    $port = $targetUri.Port
    if ($port -le 0) {
        throw "$([regex]::Unescape('\u7121\u6cd5\u5f9e\u7db2\u7ad9\u7db2\u5740\u53d6\u5f97\u6709\u6548\u7684\u9023\u63a5\u57e0\uff1a'))$Url"
    }

    if (Test-TcpPortReady -HostName $targetUri.DnsSafeHost -Port $port) {
        if ($targetUri.Host -in @('localhost', '127.0.0.1', '::1')) {
            Stop-ExistingChurchReportForPort -Port $port -ProjectDirectory $projectDirectory
        }
        else {
            throw "$([regex]::Unescape('\u7db2\u5740\u76ee\u524d\u5df2\u88ab\u5176\u4ed6\u7a0b\u5e8f\u4f7f\u7528\uff1a'))$Url$([regex]::Unescape('\uff1b\u975e\u672c\u6a5f\u7db2\u5740\u4e0d\u6703\u81ea\u52d5\u7d42\u6b62\u7a0b\u5e8f\u3002'))"
        }
    }

    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = $Url

    Write-Host '========================================' -ForegroundColor Magenta
    Write-Host ([regex]::Unescape('ChurchReport \u958b\u767c\u7db2\u7ad9\u555f\u52d5\u5668')) -ForegroundColor Magenta
    Write-Host '========================================' -ForegroundColor Magenta
    Write-Host "$([regex]::Unescape('\u5c08\u6848\uff1a'))$projectPath" -ForegroundColor Gray
    Write-Host "$([regex]::Unescape('\u74b0\u5883\uff1a'))$env:ASPNETCORE_ENVIRONMENT" -ForegroundColor Gray
    Write-Host "$([regex]::Unescape('\u7db2\u5740\uff1a'))$Url" -ForegroundColor Gray
    Write-Host ''

    Write-Host "[1/3] $([regex]::Unescape('\u7de8\u8b6f')) $Configuration ..." -ForegroundColor Cyan
    # 不產生可執行檔 apphost，避免另一個開發程序只因鎖定 apphost.exe 而阻塞編譯。
    # 網站仍由 dotnet 啟動目標 DLL，行為與一般開發執行一致。
    & $dotnetCommand.Source build $projectPath --configuration $Configuration --nologo --property:UseAppHost=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build $([regex]::Unescape('\u5931\u6557\uff0c\u7d50\u675f\u78bc\uff1a'))$LASTEXITCODE"
    }
    Write-Host ([regex]::Unescape('[1/3] \u7de8\u8b6f\u6210\u529f')) -ForegroundColor Green

    Write-Host ([regex]::Unescape('[2/3] \u555f\u52d5\u7db2\u7ad9 ...')) -ForegroundColor Cyan
    $serverArguments = @(
        'run'
        '--no-launch-profile'
        '--no-build'
        '--configuration'
        $Configuration
        '--property:UseAppHost=false'
        '--project'
        $projectPath
    )
    $serverProcess = Start-Process `
        -FilePath $dotnetCommand.Source `
        -ArgumentList $serverArguments `
        -WorkingDirectory $projectDirectory `
        -PassThru `
        -NoNewWindow

    $deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)
    $ready = $false
    while ((Get-Date) -lt $deadline) {
        if ($serverProcess.HasExited) {
            throw "$([regex]::Unescape('\u7db2\u7ad9\u7a0b\u5e8f\u5728\u555f\u52d5\u5b8c\u6210\u524d\u7d50\u675f\uff0c\u7d50\u675f\u78bc\uff1a'))$($serverProcess.ExitCode)"
        }

        if (Test-TcpPortReady -HostName $targetUri.DnsSafeHost -Port $port) {
            $ready = $true
            break
        }

        Start-Sleep -Milliseconds 250
    }

    if (-not $ready) {
        throw "$([regex]::Unescape('\u7b49\u5f85\u7db2\u7ad9\u555f\u52d5\u903e\u6642\uff08'))$StartupTimeoutSeconds $([regex]::Unescape('\u79d2\uff09\uff1a'))$Url"
    }

    Write-Host ([regex]::Unescape('[2/3] \u7db2\u7ad9\u5df2\u958b\u59cb\u76e3\u807d')) -ForegroundColor Green

    if (-not $SkipBrowser) {
        Write-Host ([regex]::Unescape('[3/3] \u958b\u555f\u700f\u89bd\u5668 ...')) -ForegroundColor Cyan
        Start-Process -FilePath $Url | Out-Null
        Write-Host ([regex]::Unescape('[3/3] \u700f\u89bd\u5668\u5df2\u958b\u555f')) -ForegroundColor Green
    }
    else {
        Write-Host ([regex]::Unescape('[3/3] \u5df2\u7565\u904e\u958b\u555f\u700f\u89bd\u5668\uff08-SkipBrowser\uff09')) -ForegroundColor Yellow
    }

    Write-Host ''
    Write-Host ([regex]::Unescape('\u7db2\u7ad9\u6b63\u5728\u57f7\u884c\uff1b\u6309 Ctrl+C \u6703\u505c\u6b62\u7db2\u7ad9\u4e26\u6e05\u7406\u5b50\u7a0b\u5e8f\u3002')) -ForegroundColor Yellow
    Wait-Process -Id $serverProcess.Id
}
catch {
    Write-Error $_
    exit 1
}
finally {
    Stop-ServerProcessTree -Process $serverProcess
}

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
            Write-Host "無法確認程序 $ownerProcessId（$processName）是否屬於 ChurchReport。" -ForegroundColor Yellow
        }
    }

    if ($unknownProcessIds.Count -gt 0) {
        throw "連接埠 $Port 已被其他或無法辨識的程序使用；為避免誤殺，腳本不會自動終止。PID：$($unknownProcessIds -join ', ')"
    }

    foreach ($churchReportProcessId in $churchReportProcessIds) {
        Write-Host "[0/3] 正在停止既有 ChurchReport 程序（PID $churchReportProcessId）..." -ForegroundColor Yellow
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
            Write-Host "[0/3] 既有 ChurchReport 已停止，連接埠 $Port 已釋放。" -ForegroundColor Green
            return
        }

        Start-Sleep -Milliseconds 250
    }

    throw "既有 ChurchReport 已要求停止，但連接埠 $Port 仍被占用。"
}

try {
    $dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction Stop

    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "找不到 ChurchReport 專案檔：$projectPath"
    }

    $targetUri = [System.Uri]::new($Url)
    if ($targetUri.Scheme -notin @('http', 'https') -or [string]::IsNullOrWhiteSpace($targetUri.Host)) {
        throw "網站網址必須是有效的 HTTP 或 HTTPS URL：$Url"
    }

    $port = $targetUri.Port
    if ($port -le 0) {
        throw "無法從網站網址取得有效的連接埠：$Url"
    }

    if (Test-TcpPortReady -HostName $targetUri.DnsSafeHost -Port $port) {
        if ($targetUri.Host -in @('localhost', '127.0.0.1', '::1')) {
            Stop-ExistingChurchReportForPort -Port $port -ProjectDirectory $projectDirectory
        }
        else {
            throw "網址目前已被其他程序使用：$Url；非本機網址不會自動終止程序。"
        }
    }

    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = $Url

    Write-Host '========================================' -ForegroundColor Magenta
    Write-Host 'ChurchReport 開發網站啟動器' -ForegroundColor Magenta
    Write-Host '========================================' -ForegroundColor Magenta
    Write-Host "專案：$projectPath" -ForegroundColor Gray
    Write-Host "環境：$env:ASPNETCORE_ENVIRONMENT" -ForegroundColor Gray
    Write-Host "網址：$Url" -ForegroundColor Gray
    Write-Host ''

    Write-Host "[1/3] 編譯 $Configuration ..." -ForegroundColor Cyan
    # 不產生可執行檔 apphost，避免另一個開發程序只因鎖定 apphost.exe 而阻塞編譯。
    # 網站仍由 dotnet 啟動目標 DLL，行為與一般開發執行一致。
    & $dotnetCommand.Source build $projectPath --configuration $Configuration --nologo --property:UseAppHost=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build 失敗，結束碼：$LASTEXITCODE"
    }
    Write-Host '[1/3] 編譯成功' -ForegroundColor Green

    Write-Host '[2/3] 啟動網站 ...' -ForegroundColor Cyan
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
            throw "網站程序在啟動完成前結束，結束碼：$($serverProcess.ExitCode)"
        }

        if (Test-TcpPortReady -HostName $targetUri.DnsSafeHost -Port $port) {
            $ready = $true
            break
        }

        Start-Sleep -Milliseconds 250
    }

    if (-not $ready) {
        throw "等待網站啟動逾時（$StartupTimeoutSeconds 秒）：$Url"
    }

    Write-Host '[2/3] 網站已開始監聽' -ForegroundColor Green

    if (-not $SkipBrowser) {
        Write-Host '[3/3] 開啟瀏覽器 ...' -ForegroundColor Cyan
        Start-Process -FilePath $Url | Out-Null
        Write-Host '[3/3] 瀏覽器已開啟' -ForegroundColor Green
    }
    else {
        Write-Host '[3/3] 已略過開啟瀏覽器（-SkipBrowser）' -ForegroundColor Yellow
    }

    Write-Host ''
    Write-Host '網站正在執行；按 Ctrl+C 會停止網站並清理子程序。' -ForegroundColor Yellow
    Wait-Process -Id $serverProcess.Id
}
catch {
    Write-Error $_
    exit 1
}
finally {
    Stop-ServerProcessTree -Process $serverProcess
}

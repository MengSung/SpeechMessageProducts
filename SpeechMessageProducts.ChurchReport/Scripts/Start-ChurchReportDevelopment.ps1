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
$repositoryDirectory = (Resolve-Path (Join-Path $projectDirectory '..')).Path
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

    $env:ASPNETCORE_ENVIRONMENT = 'Development'

    Write-Host '========================================' -ForegroundColor Magenta
    Write-Host 'ChurchReport 開發網站啟動器' -ForegroundColor Magenta
    Write-Host '========================================' -ForegroundColor Magenta
    Write-Host "專案：$projectPath" -ForegroundColor Gray
    Write-Host "環境：$env:ASPNETCORE_ENVIRONMENT" -ForegroundColor Gray
    Write-Host "網址：$Url" -ForegroundColor Gray
    Write-Host ''

    Write-Host "[1/3] 編譯 $Configuration ..." -ForegroundColor Cyan
    & $dotnetCommand.Source build $projectPath --configuration $Configuration --nologo
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
        '--project'
        ('"{0}"' -f $projectPath)
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

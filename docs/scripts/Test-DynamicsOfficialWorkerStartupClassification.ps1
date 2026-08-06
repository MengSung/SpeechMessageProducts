<#
.SYNOPSIS
以受限、唯讀的方式分類實際 Official Worker 的啟動結果。

.DESCRIPTION
本腳本只建立一個程序專屬的本機命名管道，啟動已發布且具有相符名稱的
Official Worker，並觀察 Worker 是否連線及其已去識別化的結束分類。它不寫入
CRM、不傳送 Worker operation、不讀取或輸出任何祕密，也不變更 feature flag。

每次執行擁有一個 NamedPipeServerStream、一個直接建立的子程序與兩個只丟棄內容的
stdout/stderr reader task。finally 會依序終止仍存活的子程序、等待或釋放 reader、再釋放
管道，因此不能把程序、pipe handle、輸出 stream 或背景 reader 留給下一個 profile 或下一次執行。輸出只保留
profile alias、pipe 是否連上和固定分類，避免跨 profile 泄漏端點、身分或 session。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-z][a-z0-9]{1,63}$')]
    [string] $ProfileAlias,

    [Parameter(Mandatory = $true)]
    [string] $WorkerExecutablePath,

    [Parameter(Mandatory = $true)]
    [ValidateSet('OfficialCrm82Worker', 'OfficialCrm91Worker')]
    [string] $WorkerKind,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9._-]{1,128}$')]
    [string] $PackageLockId,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9._-]{1,128}$')]
    [string] $ProfileGenerationId,

    [ValidateRange(5, 55)]
    [int] $StartupTimeoutSeconds = 45,

    [switch] $Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

$process = $null
$pipe = $null
$stdoutDiscardTask = $null
$stderrDiscardTask = $null
$result = $null
$scriptExitCode = 1
$workerExitCode = $null

function Get-ExpectedWorkerFileName {
    param([string] $Kind)

    switch ($Kind) {
        'OfficialCrm82Worker' { return 'SpeechMessage.Dynamics.Crm82Worker.exe' }
        'OfficialCrm91Worker' { return 'SpeechMessage.Dynamics.Crm91Worker.exe' }
        default { throw 'worker-startup-input-invalid' }
    }
}

function Resolve-ApprovedWorkerExecutable {
    param(
        [string] $Path,
        [string] $Kind
    )

    try {
        $resolved = [IO.Path]::GetFullPath($Path)
    }
    catch {
        throw 'worker-startup-input-invalid'
    }

    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf) -or
        -not [string]::Equals(
            (Split-Path -Leaf $resolved),
            (Get-ExpectedWorkerFileName -Kind $Kind),
            [StringComparison]::Ordinal) -or
        -not (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $resolved) 'worker-profile.xml') -PathType Leaf)) {
        throw 'worker-startup-input-invalid'
    }

    return $resolved
}

function Get-StartupClassification {
    param([int] $ExitCode)

    switch ($ExitCode) {
        10 { return 'sdk-client-not-ready' }
        16 { return 'identity-probe-not-ready' }
        17 { return 'sdk-authentication-failure' }
        18 { return 'sdk-secure-channel-failure' }
        19 { return 'sdk-transport-failure' }
        20 { return 'sdk-unclassified-failure' }
        21 { return 'sdk-diagnostic-unavailable' }
        22 { return 'sdk-initialization-failure' }
        0 { return 'clean-drain-without-operation' }
        default { return 'other-worker-startup-failure' }
    }
}

try {
    $resolvedWorkerExecutable = Resolve-ApprovedWorkerExecutable `
        -Path $WorkerExecutablePath `
        -Kind $WorkerKind
    $workerDirectory = Split-Path -Parent $resolvedWorkerExecutable
    $pipeName = 'speechmessage-dynamics-' + [Guid]::NewGuid().ToString('N')
    $processNonce = [Guid]::NewGuid().ToString('N')
    $pipe = [IO.Pipes.NamedPipeServerStream]::new(
        $pipeName,
        [IO.Pipes.PipeDirection]::InOut,
        1,
        [IO.Pipes.PipeTransmissionMode]::Byte,
        [IO.Pipes.PipeOptions]::Asynchronous)
    $arguments = @(
        '--pipe', $pipeName,
        '--nonce', $processNonce,
        '--protocol', '1',
        '--worker-kind', $WorkerKind,
        '--package-lock', $PackageLockId,
        '--profile-generation', $ProfileGenerationId
    )
    # PowerShell 的檔案重導向 launcher 可能回傳中介程序，而非真正 Worker 的 exit code。診斷必須直接
    # 擁有 Process instance，才不會將成功啟動中介程序的 0 誤當成 Worker clean drain。所有 bootstrap
    # scalar 均已經本腳本或 manifest 驗證為無空白的安全識別字，因此 Arguments 不會加入 untrusted shell
    # quoting；stdout/stderr 直接複製到 Stream.Null，不解碼、不累積、不寫檔，也不讓 SDK 診斷留存。
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $resolvedWorkerExecutable
    $startInfo.WorkingDirectory = $workerDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.Arguments = [string]::Join(' ', [string[]]$arguments)
    $process = [Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw 'worker-startup-input-invalid'
    }
    $stdoutDiscardTask = $process.StandardOutput.BaseStream.CopyToAsync([IO.Stream]::Null)
    $stderrDiscardTask = $process.StandardError.BaseStream.CopyToAsync([IO.Stream]::Null)

    $pipeWait = $pipe.BeginWaitForConnection($null, $null)
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    while (-not $pipeWait.IsCompleted -and
           -not $process.HasExited -and
           [DateTimeOffset]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 200
        $process.Refresh()
    }

    $pipeConnected = $pipeWait.IsCompleted
    $readyFrameObserved = $false
    if ($pipeConnected) {
        $pipe.EndWaitForConnection($pipeWait)

        # Worker 在建立 SDK client 並通過 identity probe 後，唯一可能先寫入的資料是 READY frame。
        # 僅讀取固定四 bytes 的 length prefix，不解碼或輸出 frame body，避免任何 IPC 內容形成保留狀態。
        $readyPrefix = New-Object byte[] 4
        try {
            $readyRead = $pipe.BeginRead($readyPrefix, 0, $readyPrefix.Length, $null, $null)
            while (-not $readyRead.IsCompleted -and
                   -not $process.HasExited -and
                   [DateTimeOffset]::UtcNow -lt $deadline) {
                Start-Sleep -Milliseconds 200
                $process.Refresh()
            }

            if ($readyRead.IsCompleted -and $pipe.EndRead($readyRead) -eq $readyPrefix.Length) {
                $readyFrameObserved = $true
            }
        }
        finally {
            [Array]::Clear($readyPrefix, 0, $readyPrefix.Length)
        }

        if ($readyFrameObserved) {
            # 不傳送任何 operation 或 drain frame；關閉對端讓 Worker 自行結束其空白 session。
            # 診斷結果已在此之前固定為 READY，後續的 child exit 不得覆寫已確認的啟動事實。
            $pipe.Dispose()
            $pipe = $null
        }
    }

    while (-not $process.HasExited -and [DateTimeOffset]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 200
        $process.Refresh()
    }

    # Process 的 exit code 只能在 OS 已確認 child 終止後擷取一次；此 scalar 是安全的 protocol
    # 分類輸入，絕不保存 stdout/stderr、SDK exception、profile、endpoint 或 credential。明確快照可避免
    # cleanup 後重新查詢已 Dispose Process 而遺失真實 startup outcome。
    if ($process.HasExited) {
        $process.Refresh()
        $workerExitCode = [int]$process.ExitCode
    }

    $classification = if ($readyFrameObserved) {
        'worker-reported-ready'
    }
    elseif ($null -ne $workerExitCode) {
        Get-StartupClassification -ExitCode $workerExitCode
    }
    else {
        'worker-still-running-after-bounded-startup-window'
    }
    $result = [ordered]@{
        schemaVersion = 1
        outcome = if ($classification -eq 'worker-reported-ready') {
            'started'
        }
        elseif ($classification -eq 'worker-still-running-after-bounded-startup-window') {
            'inconclusive'
        }
        else {
            'no-go'
        }
        profileAlias = $ProfileAlias
        pipeConnected = $pipeConnected
        readyFrameObserved = $readyFrameObserved
        startupClassification = $classification
        workerExitCode = $workerExitCode
        operationExecuted = $false
        featureFlagChanged = $false
    }
    $scriptExitCode = switch ($result.outcome) {
        'started' { 0 }
        'no-go' { 2 }
        default { 3 }
    }
}
catch {
    $result = [ordered]@{
        schemaVersion = 1
        outcome = 'error'
        reason = 'worker-startup-input-invalid'
        profileAlias = $ProfileAlias
        pipeConnected = $false
        readyFrameObserved = $false
        workerExitCode = $null
        operationExecuted = $false
        featureFlagChanged = $false
    }
}
finally {
    if ($null -ne $process) {
        try {
            if (-not $process.HasExited) {
                try {
                    $process.Kill()
                }
                catch {
                }
                [void]$process.WaitForExit(10000)
            }

            if ($null -ne $stdoutDiscardTask) {
                try {
                    [void]$stdoutDiscardTask.Wait(10000)
                }
                catch {
                }
            }
            if ($null -ne $stderrDiscardTask) {
                try {
                    [void]$stderrDiscardTask.Wait(10000)
                }
                catch {
                }
            }
        }
        finally {
            try {
                $process.StandardOutput.Dispose()
            }
            catch {
            }
            finally {
                try {
                    $process.StandardError.Dispose()
                }
                catch {
                }
            }
            try {
                $process.Dispose()
            }
            catch {
            }
            $process = $null
        }
    }

    if ($null -ne $pipe) {
        $pipe.Dispose()
        $pipe = $null
    }

    $stdoutDiscardTask = $null
    $stderrDiscardTask = $null
}

if ($Json) {
    $result | ConvertTo-Json -Depth 4
}
else {
    [pscustomobject] $result
}

exit $scriptExitCode

<#
.SYNOPSIS
以固定本機設定執行 P6 Gateway startup smoke check，並只輸出去識別化結果。

.DESCRIPTION
這支 Windows PowerShell 5.1 bridge 只允許 published Gateway 以 Development／Local
binding 啟動，selector 固定為 crm82、crm91 與兩個 P6 read-only operation。它把 child
stdout/stderr 導向自身建立的 temporary directory，永不讀取或重播內容；只依 process
是否能活過 bounded startup window 判定 `started` 或
`gateway-startup-failed-before-ready`。結束時只停止本次自己建立的 process，確認 listener
釋放，並刪除位於系統 temporary root 下的專屬目錄。

此 bridge 不呼叫 CE、不送 operation、不切換 ChurchReport、不改變 feature flag，也不保存
任何 deployment metadata。若 startup 失敗，請只回傳最後 JSON；不得貼 child log。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $GatewayExecutablePath,

    [Parameter(Mandatory = $true)]
    [string] $GatewayContentRootPath,

    [Parameter(Mandatory = $true)]
    [string] $GatewayEndpoint,

    [ValidateRange(5, 120)]
    [int] $StartupTimeoutSeconds = 20,

    [switch] $Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

$process = $null
$temporaryRoot = $null
$stdoutPath = $null
$stderrPath = $null
$result = $null

function Get-ValidatedLocalHttpsUri {
    param([string] $Value)

    $uri = $null
    if (-not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$uri) -or
        $uri.Scheme -cne 'https' -or
        $uri.Host -cne 'localhost' -or
        $uri.AbsolutePath -cne '/' -or
        $uri.Query.Length -ne 0 -or
        $uri.Fragment.Length -ne 0 -or
        $uri.UserInfo.Length -ne 0) {
        throw 'startup-bridge-input-invalid'
    }

    return $uri
}

function Assert-LocalPath {
    param(
        [string] $Path,
        [bool] $RequireLeaf
    )

    try {
        $resolved = [IO.Path]::GetFullPath($Path)
    }
    catch {
        throw 'startup-bridge-input-invalid'
    }

    $exists = if ($RequireLeaf) {
        Test-Path -LiteralPath $resolved -PathType Leaf
    }
    else {
        Test-Path -LiteralPath $resolved -PathType Container
    }
    if (-not $exists) {
        throw 'startup-bridge-input-invalid'
    }

    return $resolved
}

function Get-ListenerObserved {
    param([int] $Port)

    try {
        $connections = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction Stop)
        return $connections.Count -gt 0
    }
    catch {
        return $false
    }
}

function ReadBoundedDiagnosticText {
    param([string] $Path)

    $stream = $null
    $buffer = $null
    try {
        if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
            return [string]::Empty
        }

        $buffer = New-Object byte[] 16384
        $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        $offset = 0
        while ($offset -lt $buffer.Length) {
            $read = $stream.Read($buffer, $offset, $buffer.Length - $offset)
            if ($read -eq 0) {
                break
            }

            $offset += $read
        }

        return [Text.UTF8Encoding]::new($false, $false).GetString($buffer, 0, $offset)
    }
    finally {
        if ($null -ne $stream) {
            $stream.Dispose()
        }
        if ($null -ne $buffer) {
            [Array]::Clear($buffer, 0, $buffer.Length)
        }
    }
}

function Get-GatewayStartupFailureClassification {
    param(
        [string] $StandardOutputPath,
        [string] $StandardErrorPath
    )

    # child log 僅在本程序記憶體內以固定、受限字串分類；不得回傳、寫檔或列印原始內容。
    $diagnosticText = (ReadBoundedDiagnosticText -Path $StandardOutputPath) +
        [Environment]::NewLine +
        (ReadBoundedDiagnosticText -Path $StandardErrorPath)
    try {
        if ($diagnosticText.IndexOf(
                'The worker frame ended before the declared length was read.',
                [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return 'worker-ready-frame-not-published'
        }
        if ($diagnosticText.IndexOf(
                'Dynamics control-plane schema verification failed.',
                [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $diagnosticText.IndexOf(
                'Cannot open database "SpeechMessageDynamicsControlPlane"',
                [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return 'control-plane-schema-or-connection-failed'
        }
        if ($diagnosticText.IndexOf(
                'The official Dynamics worker deployment overlay is invalid.',
                [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return 'official-worker-overlay-invalid'
        }
        if ($diagnosticText.IndexOf(
                'Unable to configure HTTPS endpoint',
                [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return 'local-https-binding-failed'
        }

        return 'gateway-startup-unclassified'
    }
    finally {
        $diagnosticText = $null
    }
}

try {
    $endpointUri = Get-ValidatedLocalHttpsUri -Value $GatewayEndpoint
    $resolvedExecutable = Assert-LocalPath -Path $GatewayExecutablePath -RequireLeaf $true
    $resolvedContentRoot = Assert-LocalPath -Path $GatewayContentRootPath -RequireLeaf $false
    if (-not [string]::Equals(
            (Split-Path -Parent $resolvedExecutable),
            $resolvedContentRoot,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'startup-bridge-input-invalid'
    }

    $temporaryRoot = Join-Path (
        [IO.Path]::GetTempPath()) (
            'speechmessage-p6-startup-' + [Guid]::NewGuid().ToString('N'))
    [void](New-Item -ItemType Directory -Path $temporaryRoot -Force)
    $stdoutPath = Join-Path $temporaryRoot 'stdout.log'
    $stderrPath = Join-Path $temporaryRoot 'stderr.log'

    $arguments = @(
        '--contentRoot', $resolvedContentRoot,
        '--environment', 'Development',
        '--urls', $endpointUri.AbsoluteUri,
        '--DynamicsGateway:ActiveWorkloadBindingSet', 'Local',
        '--DynamicsGateway:WorkloadBindingSets:Local:0:ProfileAliases:0', 'crm82',
        '--DynamicsGateway:WorkloadBindingSets:Local:0:ProfileAliases:1', 'crm91',
        '--DynamicsGateway:WorkloadBindingSets:Local:0:CapabilityOperationIds:0', 'runtime.health.whoami',
        '--DynamicsGateway:WorkloadBindingSets:Local:0:CapabilityOperationIds:1', 'runtime.pool.validate.connection'
    )

    $process = Start-Process `
        -FilePath $resolvedExecutable `
        -WorkingDirectory $resolvedContentRoot `
        -ArgumentList $arguments `
        -WindowStyle Hidden `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -PassThru

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    while (-not $process.HasExited -and [DateTimeOffset]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 200
        $process.Refresh()
    }

    $processExited = $process.HasExited
    $listenerObserved = Get-ListenerObserved -Port $endpointUri.Port
    $failureClassification = if ($processExited) {
        Get-GatewayStartupFailureClassification `
            -StandardOutputPath $stdoutPath `
            -StandardErrorPath $stderrPath
    }
    else {
        'not-applicable'
    }
    $result = [ordered]@{
        schemaVersion = 1
        outcome = if ($processExited) { 'no-go' } else { 'started' }
        reason = if ($processExited) {
            'gateway-startup-failed-before-ready'
        }
        else {
            'gateway-survived-startup-window'
        }
        processObserved = $true
        processExitedBeforeWindow = $processExited
        listenerObserved = $listenerObserved
        failureClassification = $failureClassification
        ceContacted = $false
        featureFlagChanged = $false
        operationExecuted = $false
    }
}
catch {
    $result = [ordered]@{
        schemaVersion = 1
        outcome = 'error'
        reason = 'startup-bridge-input-or-process-error'
        processObserved = $null -ne $process
        processExitedBeforeWindow = $null
        listenerObserved = $false
        failureClassification = 'startup-bridge-input-or-process-error'
        ceContacted = $false
        featureFlagChanged = $false
        operationExecuted = $false
    }
}
finally {
    if ($null -ne $process) {
        try {
            if (-not $process.HasExited) {
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
                [void]$process.WaitForExit(10000)
            }

            if ($null -ne $result) {
                $result.listenerReleased = -not (Get-ListenerObserved -Port $endpointUri.Port)
            }
        }
        catch {
            if ($null -ne $result) {
                $result.listenerReleased = $false
            }
        }
        finally {
            $process.Dispose()
            $process = $null
        }
    }

    if ($null -ne $temporaryRoot -and (Test-Path -LiteralPath $temporaryRoot)) {
        $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
        $resolvedSystemTemporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (-not $resolvedTemporaryRoot.StartsWith(
                $resolvedSystemTemporaryRoot,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw 'startup-bridge-cleanup-boundary-invalid'
        }

        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }

    $stdoutPath = $null
    $stderrPath = $null
    $temporaryRoot = $null
}

if ($Json) {
    $result | ConvertTo-Json -Depth 4
}
else {
    [pscustomobject]$result
}

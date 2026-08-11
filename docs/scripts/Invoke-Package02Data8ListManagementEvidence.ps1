<#
.SYNOPSIS
    執行 P7.2 Slice C 的 CE 9.1 Data8 list-management 實機證據預檢與明確 opt-in runner。

.DESCRIPTION
    此 runner 專供 Lenovo 本機的 sunnyvalechback 部署設定使用。預設只驗證 coverage matrix、
    ChurchReport feature flag、既有 P6.2 profile input、Credential Manager target、Slice A
    contact descriptor，以及 Slice C task-owned fixture graph descriptor；預設絕不連線 CE、
    不讀取 password、不中斷 browser session，也不變更 CRM 或 feature flag。

    只有明確傳入 -ExecuteFixture 後，runner 才會以 Credential Manager 的既有 crm91 target
    將 password 放入單一短生命週期 child process，啟動一次受限的 dotnet test。child test
    先讀取所有五段流程的基線，再依序執行 add、remove、small-group、owner、transfer，每段
    都由既有 P72ListManagementFixtureBridge 完成一次 dispatch、read-back、restore 與
    restore read-back。timeout 或模糊結果絕不重試。

    明確傳入 -ReconcileFixture 時，runner 使用同一個 Credential Manager reference，但只啟動
    獨立的 read-only child lane。該 lane 僅執行 WhoAmI、Retrieve 與 RetrieveMultiple projection，
    正常路徑輸出 baseline-unprovable；若 child 的 store、runtime 或 logger cleanup 無法證明完成，
    則輸出優先序更高的 cleanup-failure 並把 readOnlyProbeExecuted 固定為 false。parent-owned
    safeToRetry 固定為 false。-ExecuteFixture 與
    -ReconcileFixture 為互斥 parameter set；同時指定會在讀取 credential 或建立 child 前由 binder 拒絕。

    所有 stdout 僅為一行 sanitized JSON。child 在清理自身 runtime 後只可寫入 parent 已建立的
    唯一 OS temporary evidence file；parent 以固定 schema、operation ID 與分類重新投影，並在
    寫出最終 JSON 前刪除整個 nonce directory。若 cleanup 無法證明成功，結果一律為 No-Go；永遠
    不輸出 password、token、cookie、endpoint、GUID、owner identity、baseline、CRM payload、
    temporary path 或原始例外。Credential native pointer、child process、temporary directory 與
    環境變數在 finally 有唯一且可預期的釋放／回復路徑。
#>
[CmdletBinding(DefaultParameterSetName = 'Preflight')]
param(
    [Parameter(Mandatory = $true)]
    [string] $RepositoryPath,

    [Parameter(Mandatory = $false)]
    [string] $ProfileInputPath,

    [Parameter(Mandatory = $false)]
    [string] $SourceFixtureDescriptorPath,

    [Parameter(Mandatory = $false)]
    [string] $FixtureDescriptorPath,

    [switch] $Json,

    [Parameter(ParameterSetName = 'Execute')]
    [switch] $ExecuteFixture,

    [Parameter(ParameterSetName = 'Reconcile')]
    [switch] $ReconcileFixture,

    [Parameter(ParameterSetName = 'Repair')]
    [switch] $RepairFixture,

    [Parameter(ParameterSetName = 'RepairProbe')]
    [switch] $RepairProbe,

    [Parameter(ParameterSetName = 'FreshPreflightProbe')]
    [switch] $FreshPreflightProbe,

    [Parameter(ParameterSetName = 'ProvisionFresh')]
    [switch] $ProvisionFreshFixture,

    [Parameter(ParameterSetName = 'ProvisionFresh')]
    [switch] $ReplaceStaleDescriptor,

    [Parameter(ParameterSetName = 'CleanupFresh')]
    [switch] $CleanupFreshFixture,

    [Parameter(ParameterSetName = 'CleanupFresh')]
    [switch] $ConfirmFreshFixtureCleanup
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)

$scriptExitCode = 1
$resultAlreadyWritten = $false
$completedHandoffOutcome = $null
$temporaryDirectory = $null
$temporaryDirectoryCreated = $false
$process = $null
$childProcessStarted = $false
$credentialPassword = $null
$freshControlPlaneRoots = $null
$freshLedgerPath = $null
$freshNonce = $null
$freshLedgerBeforeCleanup = $null
$freshOriginalTargetLeaderContactId = $null
$isReconciliationMode = [bool]$ReconcileFixture
$isRepairMode = [bool]$RepairFixture
$isRepairProbeMode = [bool]$RepairProbe
$isFreshPreflightProbeMode = [bool]$FreshPreflightProbe
$isFreshProvisionMode = [bool]$ProvisionFreshFixture
$isFreshCleanupMode = [bool]$CleanupFreshFixture
$liveModeRequested = [bool]($ExecuteFixture -or $ReconcileFixture -or $RepairFixture -or $RepairProbe -or $FreshPreflightProbe -or $ProvisionFreshFixture -or $CleanupFreshFixture)
$operationMayHaveExecuted = -not ($isReconciliationMode -or $isRepairMode -or $isRepairProbeMode -or $isFreshPreflightProbeMode)
$previousEnvironment = @{}
# legacy inventory 是 fresh child 的唯一 inherited Slice C state denylist；它同時餵給 snapshot、
# fresh-mode clear 與 finally restore，避免 P7_2_ 或 SPEECHMESSAGE_ namespace 新增 key 時只更新其中
# 一條 lifecycle path 而跨 session 洩漏。最後三個 retired key 使用拆分 suffix 組合，僅用於
# scrub/restore，絕非 child protocol、evidence、owner 或 credential contract。
$legacySliceCEnvironmentPrefix = 'P7_2_SLICE_C_'
$legacySliceCEnvironmentPrefixes = @(
    $legacySliceCEnvironmentPrefix,
    'SPEECHMESSAGE_P7_2_SLICE_C_'
)
$legacySliceCEnvironmentNames = @(
    'SPEECHMESSAGE_P7_2_SLICE_C_LIVE',
    'SPEECHMESSAGE_P7_2_SLICE_C_RECONCILE',
    'SPEECHMESSAGE_P7_2_SLICE_C_REPAIR',
    'SPEECHMESSAGE_P7_2_SLICE_C_REPAIR_PROBE',
    'SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PREFLIGHT_PROBE',
    'P7_2_SLICE_C_FIXTURE_OWNER',
    'P7_2_SLICE_C_FIXTURE_MARKER',
    'P7_2_SLICE_C_CONTACT_ID',
    'P7_2_SLICE_C_ADD_LIST_ID',
    'P7_2_SLICE_C_REMOVE_LIST_ID',
    'P7_2_SLICE_C_SMALL_GROUP_LIST_ID',
    'P7_2_SLICE_C_SMALL_GROUP_TARGET_LEADER_CONTACT_ID',
    'P7_2_SLICE_C_SMALL_GROUP_EXPECTED_RELATIONSHIP_LIST_ID',
    'P7_2_SLICE_C_TRANSFER_SOURCE_LIST_ID',
    'P7_2_SLICE_C_TRANSFER_TARGET_LIST_ID',
    'P7_2_SLICE_C_TRANSFER_WEEK_START_UTC',
    'P7_2_SLICE_C_EVIDENCE_PATH',
    'P7_2_SLICE_C_RECONCILIATION_EVIDENCE_PATH',
    'P7_2_SLICE_C_REPAIR_EVIDENCE_PATH',
    'P7_2_SLICE_C_REPAIR_PROBE_EVIDENCE_PATH',
    ($legacySliceCEnvironmentPrefix + 'EVIDENCE_' + 'JSON'),
    ($legacySliceCEnvironmentPrefix + 'RETIRED_' + 'TRX_' + 'EVIDENCE'),
    ($legacySliceCEnvironmentPrefix + 'TARGET_' + 'OWNER_' + 'ID')
)
# 僅有這些由 parent 在本次 invocation 明確建立的 P7_2_SLICE_C_FRESH_* bindings 能傳給
# fresh child。此 allowlist 必須是大小寫不敏感的精確比對；未知名稱即使帶有 FRESH_ prefix，
# 仍是長壽命 shell 遺留的 mutable state，必須 snapshot、清空並在 finally 還原。
$freshSliceCControlPlaneEnvironmentNames = @(
    'SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PROVISION',
    'SPEECHMESSAGE_P7_2_SLICE_C_FRESH_CLEANUP',
    'P7_2_SLICE_C_FRESH_LEDGER_ROOT',
    'P7_2_SLICE_C_FRESH_LEDGER_PATH',
    'P7_2_SLICE_C_FRESH_EVIDENCE_PATH',
    'P7_2_SLICE_C_FRESH_DIAGNOSTIC_PATH',
    'P7_2_SLICE_C_FRESH_PREFLIGHT_EVIDENCE_PATH',
    'P7_2_SLICE_C_FRESH_DESCRIPTOR_CONFIRMATION',
    'P7_2_SLICE_C_FRESH_NONCE',
    'P7_2_SLICE_C_FRESH_OWNER',
    'P7_2_SLICE_C_FRESH_ADD_LIST_ID',
    'P7_2_SLICE_C_FRESH_REMOVE_LIST_ID',
    'P7_2_SLICE_C_FRESH_SMALL_GROUP_LIST_ID',
    'P7_2_SLICE_C_FRESH_EXISTING_TARGET_LEADER_ID',
    'P7_2_SLICE_C_FRESH_TRANSFER_SOURCE_LIST_ID',
    'P7_2_SLICE_C_FRESH_TRANSFER_TARGET_LIST_ID',
    'P7_2_SLICE_C_FRESH_TRANSFER_WEEK_START_UTC'
)
$freshSliceCControlPlaneEnvironmentNameSet = [System.Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
foreach ($freshSliceCControlPlaneEnvironmentName in $freshSliceCControlPlaneEnvironmentNames) {
    [void]$freshSliceCControlPlaneEnvironmentNameSet.Add($freshSliceCControlPlaneEnvironmentName)
}

# Take one process-local inventory before this runner changes any mode input. A
# long-lived shell may retain an unknown legacy P7.2 Slice C value introduced by
# an earlier runner version; fresh child processes must neither inherit it nor
# lose it after this invocation.
$discoveredLegacySliceCEnvironmentNames = @(
    [Environment]::GetEnvironmentVariables([EnvironmentVariableTarget]::Process).Keys |
        ForEach-Object { [string]$_ } |
        Where-Object {
            $candidateEnvironmentName = [string]$_
            $matchesLegacySliceCNamespace = $false
            foreach ($legacyNamespacePrefix in $legacySliceCEnvironmentPrefixes) {
                if ($candidateEnvironmentName.StartsWith($legacyNamespacePrefix, [StringComparison]::OrdinalIgnoreCase)) {
                    $matchesLegacySliceCNamespace = $true
                    break
                }
            }

            $matchesLegacySliceCNamespace -and
                -not $freshSliceCControlPlaneEnvironmentNameSet.Contains($candidateEnvironmentName)
        }
)
$legacySliceCEnvironmentNames = @(
    @($legacySliceCEnvironmentNames + $discoveredLegacySliceCEnvironmentNames) |
        Select-Object -Unique
)
$inputEnvironmentNames = @('CRM_PASSWORD') + $legacySliceCEnvironmentNames + $freshSliceCControlPlaneEnvironmentNames
$credentialTarget = 'speechmessage.crm91.p62'
$expectedProfileAlias = 'sunnyvalechback'
$expectedDeploymentProfileAlias = 'crm91'
$expectedFixtureMarker = 'p7.2-list-management'
$expectedOperationIds = @(
    'list.members.add.many',
    'list.members.remove.one',
    'listmanagement.smallgroup.update.fields',
    'contact.assign.owner',
    'newperson.contact.transfer.between.lists'
)

if ([string]::IsNullOrWhiteSpace($ProfileInputPath)) {
    $ProfileInputPath = Join-Path $env:LOCALAPPDATA 'SpeechMessage\Dynamics\P6.2\official-worker-profile-input.json'
}

if ([string]::IsNullOrWhiteSpace($SourceFixtureDescriptorPath)) {
    $SourceFixtureDescriptorPath = Join-Path $env:LOCALAPPDATA 'SpeechMessage\Dynamics\P7.2\contact-basic-info-fixture.json'
}

if ([string]::IsNullOrWhiteSpace($FixtureDescriptorPath)) {
    $FixtureDescriptorPath = Join-Path $env:LOCALAPPDATA 'SpeechMessage\Dynamics\P7.2\list-management-fixture.json'
}

function New-HandoffResult {
    <#
    .SYNOPSIS
        建立唯一允許輸出到 operator console 的去識別化結果。

    .DESCRIPTION
        回傳物只含固定 operation ID、部署 alias、CE/connector、布林旗標與 allowlisted
        分類。它不接受 path、descriptor、GUID、identity、credential 或 exception 作為欄位，
        因而即使呼叫端失敗也不會把敏感 input 放入最後的一行 JSON。
    #>
    param(
        [string] $Outcome,
        [string] $Reason,
        [bool] $PreflightOnly,
        [bool] $OperationExecuted = $false,
        [object[]] $Operations = @(),
        [object[]] $Checks = @(),
        [Nullable[bool]] $ReadOnlyProbeExecuted = $null,
        [Nullable[bool]] $ReadBackConfirmed = $null,
        [string] $OwnerBinding = $null,
        [string] $ProbeStage = $null,
        [object] $States = $null,
        [object] $Probe = $null,
        [string] $DiagnosticCategory = $null
    )

    $result = [ordered]@{
        schemaVersion = 1
        outcome = $Outcome
        reason = $Reason
        operationIds = $expectedOperationIds
        profileAlias = $expectedProfileAlias
        deploymentProfileAlias = $expectedDeploymentProfileAlias
        ceVersion = '9.1'
        connector = 'Data8'
        preflightOnly = $PreflightOnly
        operationExecuted = $OperationExecuted
        featureFlagChanged = $false
    }
    $modeVariable = Get-Variable -Name isReconciliationMode -Scope Script -ErrorAction SilentlyContinue
    $repairVariable = Get-Variable -Name isRepairMode -Scope Script -ErrorAction SilentlyContinue
    $repairProbeVariable = Get-Variable -Name isRepairProbeMode -Scope Script -ErrorAction SilentlyContinue
    $freshPreflightProbeVariable = Get-Variable -Name isFreshPreflightProbeMode -Scope Script -ErrorAction SilentlyContinue
    $freshProvisionVariable = Get-Variable -Name isFreshProvisionMode -Scope Script -ErrorAction SilentlyContinue
    $freshCleanupVariable = Get-Variable -Name isFreshCleanupMode -Scope Script -ErrorAction SilentlyContinue
    if (($null -ne $modeVariable -and [bool]$modeVariable.Value) -or
        ($null -ne $repairVariable -and [bool]$repairVariable.Value) -or
        ($null -ne $repairProbeVariable -and [bool]$repairProbeVariable.Value) -or
        ($null -ne $freshPreflightProbeVariable -and [bool]$freshPreflightProbeVariable.Value) -or
        ($null -ne $freshProvisionVariable -and [bool]$freshProvisionVariable.Value) -or
        ($null -ne $freshCleanupVariable -and [bool]$freshCleanupVariable.Value)) {
        # 這個欄位由 parent mode 自己產生；child evidence 若攜帶同名欄位會被 strict parser 拒絕。
        $result.safeToRetry = $false
    }
    if ($null -ne $ReadOnlyProbeExecuted) {
        $result.readOnlyProbeExecuted = [bool]$ReadOnlyProbeExecuted
    }
    if ($null -ne $ReadBackConfirmed) {
        $result.readBackConfirmed = [bool]$ReadBackConfirmed
    }
    if (-not [string]::IsNullOrWhiteSpace($OwnerBinding)) {
        $result.ownerBinding = $OwnerBinding
    }
    if (-not [string]::IsNullOrWhiteSpace($ProbeStage)) {
        $result.probeStage = $ProbeStage
    }
    if ($null -ne $States) {
        $result.states = $States
    }
    if ($null -ne $Probe) {
        $result.probe = $Probe
    }
    if (-not [string]::IsNullOrWhiteSpace($DiagnosticCategory)) {
        # 這個欄位只能說明非零 child exit 的固定分類；它絕不是 child evidence，不能用來
        # 發布 descriptor、推導 CRM 寫入結果、觸發 cleanup 或將 safeToRetry 改為 true。
        if ($DiagnosticCategory -cnotin @(
                'fixture-precondition-failed',
                'baseline-owner-unavailable',
                'fresh-source-readback-failed',
                'fresh-leader-readback-failed',
                'fresh-relationship-readback-failed',
                'remove-membership-readback-failed',
                'transfer-source-membership-readback-failed',
                'baseline-owner-readback-failed',
                'fresh-graph-unproven',
                'provisioning-ambiguous',
                'runtime-failure',
                'cleanup-failure')) {
            throw 'fresh-fixture-diagnostic-invalid'
        }

        $result.diagnosticCategory = $DiagnosticCategory
    }
    if ($Operations.Count -gt 0) {
        $result.operations = $Operations
    }
    if ($Checks.Count -gt 0) {
        $result.checks = $Checks
    }

    return $result
}

function Write-HandoffResult {
    <#
    .SYNOPSIS
        輸出剛好一行 JSON 並鎖定後續 catch 不得再輸出第二筆結果。
    #>
    param([object] $Result)

    $script:resultAlreadyWritten = $true
    $Result | ConvertTo-Json -Compress -Depth 8
}

function New-TemporaryCleanupFailureResult {
    <#
    .SYNOPSIS
        將已計算的 child 結果保守轉為 temporary cleanup 的固定 No-Go。

    .DESCRIPTION
        evidence directory 是 child 執行後唯一仍可能保留去識別化資料的資源。即使五段 operation
        都已成功，parent 只要無法證明該 directory 已刪除，就不得輸出 Green；否則下一次 handoff
        可能讀到前次殘留證據，並讓資源生命週期與 CE mutation 狀態失去可稽核的一致性。

        此投影只保留既有 allowlisted operation 結果與是否可能已 dispatch 的布林值，固定覆寫為
        ``temporary-cleanup-failed``。它不保留或輸出 directory path、exception、credential 或 child
        payload，因此 cleanup 失敗不會擴大為本機資訊洩漏。
    #>
    param([object] $Result)

    $preflightOnly = $false
    $operationExecuted = $false
    $operations = @()
    if ($null -ne $Result) {
        if ($null -ne $Result.PSObject.Properties['preflightOnly'] -and $Result.preflightOnly -is [bool]) {
            $preflightOnly = [bool]$Result.preflightOnly
        }
        if ($null -ne $Result.PSObject.Properties['operationExecuted'] -and $Result.operationExecuted -is [bool]) {
            $operationExecuted = [bool]$Result.operationExecuted
        }
        if ($null -ne $Result.PSObject.Properties['operations'] -and $null -ne $Result.operations) {
            $operations = @($Result.operations)
        }
    }

    return New-HandoffResult `
        -Outcome 'no-go' `
        -Reason 'temporary-cleanup-failed' `
        -PreflightOnly $preflightOnly `
        -OperationExecuted $operationExecuted `
        -Operations $operations
}

function Read-StrictJsonFile {
    <#
    .SYNOPSIS
        讀取 UTF-8 no-BOM、CRLF-only、有限大小的 descriptor 或 profile JSON。

    .DESCRIPTION
        這個邊界一次讀入受限 bytes，驗證檔案長度與實際 bytes 一致，再用 UTF-8 strict
        decoder 與 ConvertFrom-Json 解析。任何路徑、encoding、換行或 JSON 問題都轉為呼叫端
        指定的固定 failure reason，避免 raw exception 把本機資料夾或內容帶到 stdout。
    #>
    param(
        [string] $Path,
        [int] $MaximumBytes,
        [string] $FailureReason,
        [switch] $RequireFinalCrLf
    )

    # 此 reader 會被既有 contract suite 以單一 function AST import；duplicate-key guard 因此必須
    # 與 reader 同一 lexical scope，不依賴未被 import 的 script-level helper。兩個 local function
    # 僅在此次 bounded read 存活，避免 parser state 跨測試、profile 或 child handoff 留存。
    function Get-StrictJsonStringEndIndex {
        param(
            [string] $JsonText,
            [int] $StartIndex
        )

        if ($null -eq $JsonText -or
            $StartIndex -lt 0 -or
            $StartIndex -ge $JsonText.Length -or
            $JsonText[$StartIndex] -ne '"') {
            return -1
        }

        $index = $StartIndex + 1
        while ($index -lt $JsonText.Length) {
            $character = $JsonText[$index]
            if ($character -eq '"') {
                return $index
            }

            if ($character -eq '\') {
                $index++
                if ($index -ge $JsonText.Length) {
                    return -1
                }

                $escapedCharacter = $JsonText[$index]
                if ($escapedCharacter -eq 'u') {
                    if ($index + 4 -ge $JsonText.Length) {
                        return -1
                    }

                    for ($hexIndex = $index + 1; $hexIndex -le $index + 4; $hexIndex++) {
                        if ('0123456789abcdefABCDEF'.IndexOf($JsonText[$hexIndex]) -lt 0) {
                            return -1
                        }
                    }

                    $index += 5
                    continue
                }

                if (@('"', '\', '/', 'b', 'f', 'n', 'r', 't') -cnotcontains [string]$escapedCharacter) {
                    return -1
                }

                $index++
                continue
            }

            if ([int][char]$character -lt 0x20) {
                return -1
            }

            $index++
        }

        return -1
    }

    function Test-StrictJsonObjectPropertyNamesAreUnique {
        param([string] $JsonText)

        if ($null -eq $JsonText) {
            return $false
        }

        $contexts = [System.Collections.Generic.Stack[object]]::new()
        try {
            $index = 0
            while ($index -lt $JsonText.Length) {
                $character = $JsonText[$index]
                if ($character -eq ' ' -or
                    $character -eq [char]0x09 -or
                    $character -eq [char]0x0D -or
                    $character -eq [char]0x0A) {
                    $index++
                    continue
                }

                if ($character -eq '{') {
                    $contexts.Push([pscustomobject]@{
                            Kind = 'object'
                            PropertyNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                        })
                    $index++
                    continue
                }

                if ($character -eq '[') {
                    $contexts.Push([pscustomobject]@{
                            Kind = 'array'
                            PropertyNames = $null
                        })
                    $index++
                    continue
                }

                if ($character -eq '}') {
                    if ($contexts.Count -eq 0 -or $contexts.Peek().Kind -cne 'object') {
                        return $false
                    }

                    [void]$contexts.Pop()
                    $index++
                    continue
                }

                if ($character -eq ']') {
                    if ($contexts.Count -eq 0 -or $contexts.Peek().Kind -cne 'array') {
                        return $false
                    }

                    [void]$contexts.Pop()
                    $index++
                    continue
                }

                if ($character -eq '"') {
                    $stringEndIndex = Get-StrictJsonStringEndIndex -JsonText $JsonText -StartIndex $index
                    if ($stringEndIndex -lt 0) {
                        return $false
                    }

                    $nextTokenIndex = $stringEndIndex + 1
                    while ($nextTokenIndex -lt $JsonText.Length -and
                        ($JsonText[$nextTokenIndex] -eq ' ' -or
                         $JsonText[$nextTokenIndex] -eq [char]0x09 -or
                         $JsonText[$nextTokenIndex] -eq [char]0x0D -or
                         $JsonText[$nextTokenIndex] -eq [char]0x0A)) {
                        $nextTokenIndex++
                    }

                    if ($nextTokenIndex -lt $JsonText.Length -and $JsonText[$nextTokenIndex] -eq ':') {
                        if ($contexts.Count -eq 0 -or $contexts.Peek().Kind -cne 'object') {
                            return $false
                        }

                        try {
                            $propertyName = $JsonText.Substring($index, $stringEndIndex - $index + 1) |
                                ConvertFrom-Json -ErrorAction Stop
                        }
                        catch {
                            return $false
                        }

                        if ($propertyName -isnot [string] -or
                            -not $contexts.Peek().PropertyNames.Add([string]$propertyName)) {
                            return $false
                        }
                    }

                    $index = $stringEndIndex + 1
                    continue
                }

                $index++
            }

            return $contexts.Count -eq 0
        }
        finally {
            $contexts.Clear()
        }
    }

    $bytes = $null
    try {
        $resolved = [IO.Path]::GetFullPath($Path)
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
            throw $FailureReason
        }

        $item = Get-Item -LiteralPath $resolved -Force -ErrorAction Stop
        if ($item.Length -lt 1 -or $item.Length -gt $MaximumBytes) {
            throw $FailureReason
        }

        $bytes = [IO.File]::ReadAllBytes($resolved)
        if ($bytes.Length -ne $item.Length -or
            ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)) {
            throw $FailureReason
        }

        $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        # JSON 允許 standalone CR 作為 whitespace；因此必須同時拒絕沒有前置 CR 的 LF 與沒有
        # 後續 LF 的 CR。只檢查 bare LF 會讓看似 final-CRLF 的本機 descriptor 越過 CRLF-only
        # trust boundary，而它之後可能成為 child、cleanup 與 descriptor publication 的輸入。
        if ([Regex]::IsMatch($text, '(?<!\r)\n|\r(?!\n)')) {
            throw $FailureReason
        }
        if ($RequireFinalCrLf) {
            $requiredFinalCrLf = [string]([char]0x0D) + [string]([char]0x0A)
            if (-not $text.EndsWith($requiredFinalCrLf, [StringComparison]::Ordinal)) {
                throw $FailureReason
            }
        }

        if (-not (Test-StrictJsonObjectPropertyNamesAreUnique -JsonText $text)) {
            throw $FailureReason
        }

        return $text | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw $FailureReason
    }
    finally {
        if ($null -ne $bytes) {
            [Array]::Clear($bytes, 0, $bytes.Length)
        }
    }
}

function Read-StrictTextFile {
    <#
    .SYNOPSIS
        讀取 configuration text，但保留相同 encoding、大小與換行安全界線。
    #>
    param(
        [string] $Path,
        [int] $MaximumBytes,
        [string] $FailureReason
    )

    $bytes = $null
    try {
        $resolved = [IO.Path]::GetFullPath($Path)
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
            throw $FailureReason
        }

        $item = Get-Item -LiteralPath $resolved -Force -ErrorAction Stop
        if ($item.Length -lt 1 -or $item.Length -gt $MaximumBytes) {
            throw $FailureReason
        }

        $bytes = [IO.File]::ReadAllBytes($resolved)
        if ($bytes.Length -ne $item.Length -or
            ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)) {
            throw $FailureReason
        }

        $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        # 設定文字與 JSON descriptor 使用同一個 byte-level line-ending boundary；獨立 CR 也不能
        # 因為 .NET/PowerShell 將其視為 whitespace 而被接受，否則不同 reader 會對同一檔案產生
        # 不一致的 local control-plane 信任結果。
        if ([Regex]::IsMatch($text, '(?<!\r)\n|\r(?!\n)')) {
            throw $FailureReason
        }

        return $text
    }
    catch {
        throw $FailureReason
    }
    finally {
        if ($null -ne $bytes) {
            [Array]::Clear($bytes, 0, $bytes.Length)
        }
    }
}
function Test-NonEmptyGuid {
    <#
    .SYNOPSIS
        驗證 descriptor 的 GUID 為固定 D 格式且非空值。
    #>
    param([object] $Value)

    $parsed = [Guid]::Empty
    return $Value -is [string] -and
        [Guid]::TryParseExact($Value, 'D', [ref]$parsed) -and
        $parsed -ne [Guid]::Empty
}

function Test-SafeOwnerIdentity {
    <#
    .SYNOPSIS
        驗證 owner identity 可安全比對但不輸出。
    #>
    param([object] $Value)

    return $Value -is [string] -and
        -not [string]::IsNullOrWhiteSpace($Value) -and
        $Value.Length -le 256 -and
        $Value.IndexOfAny([char[]]@("`0", "`r", "`n")) -lt 0
}

function Test-CredentialTargetPresent {
    <#
    .SYNOPSIS
        只確認既有 Generic Credential target 存在，不取得其 secret。

    .DESCRIPTION
        Native credential pointer 由 C# helper 在 finally 呼叫 CredFree 釋放。預檢使用這個
        最小權限 probe，故不會在未傳入 -ExecuteFixture 時使 password 進入 managed memory。
    #>
    try {
        if ($null -eq ('SpeechMessage.P72SliceC.CredentialPresenceReader' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace SpeechMessage.P72SliceC
{
    /// <summary>只確認 Generic Credential 存在，並在 finally 釋放 native pointer。</summary>
    public static class CredentialPresenceReader
    {
        private const uint GenericCredentialType = 1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeCredential
        {
            public uint Flags;
            public uint Type;
            public IntPtr TargetName;
            public IntPtr Comment;
            public long LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public IntPtr TargetAlias;
            public IntPtr UserName;
        }

        [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

        [DllImport("Advapi32.dll", SetLastError = true)]
        private static extern void CredFree(IntPtr credential);

        /// <summary>驗證 target 後立即釋放 native credential，不複製 blob。</summary>
        public static bool Exists(string target)
        {
            if (string.IsNullOrWhiteSpace(target) || target.IndexOf('\0') >= 0)
            {
                return false;
            }

            IntPtr pointer = IntPtr.Zero;
            try
            {
                if (!CredRead(target, GenericCredentialType, 0, out pointer) || pointer == IntPtr.Zero)
                {
                    return false;
                }

                var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
                return credential.Type == GenericCredentialType;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (pointer != IntPtr.Zero)
                {
                    CredFree(pointer);
                }
            }
        }
    }
}
'@ -ErrorAction Stop
        }

        return [SpeechMessage.P72SliceC.CredentialPresenceReader]::Exists($credentialTarget)
    }
    catch {
        return $false
    }
}

function Get-P72CredentialPassword {
    <#
    .SYNOPSIS
        僅在 ExecuteFixture 後以既有 crm91 Credential Manager target 讀取短暫 password。

    .DESCRIPTION
        指標、char buffer 與 native credential 都有單一 finally owner。回傳的 string 只在本
        script process 中設定為 child environment，finally 清除 reference 與環境變數；stdout、
        exception、evidence parser 和 result object 都不接收此值。
    #>
    try {
        if ($null -eq ('SpeechMessage.P72SliceCLive.CredentialReader' -as [type])) {
            Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace SpeechMessage.P72SliceCLive
{
    /// <summary>以 bounded native read 取得 Generic Credential，且保證釋放 pointer 與 buffer。</summary>
    public static class CredentialReader
    {
        private const uint GenericCredentialType = 1;
        private const int MaximumBlobBytes = 8192;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeCredential
        {
            public uint Flags;
            public uint Type;
            public IntPtr TargetName;
            public IntPtr Comment;
            public long LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public IntPtr TargetAlias;
            public IntPtr UserName;
        }

        [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

        [DllImport("Advapi32.dll", SetLastError = true)]
        private static extern void CredFree(IntPtr credential);

        /// <summary>複製合法 bounded UTF-16 blob，並在 finally 清空 temporary char buffer。</summary>
        public static string ReadGenericSecret(string target)
        {
            if (string.IsNullOrWhiteSpace(target) || target.IndexOf('\0') >= 0)
            {
                return null;
            }

            IntPtr pointer = IntPtr.Zero;
            try
            {
                if (!CredRead(target, GenericCredentialType, 0, out pointer) || pointer == IntPtr.Zero)
                {
                    return null;
                }

                var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
                if (credential.Type != GenericCredentialType ||
                    credential.CredentialBlob == IntPtr.Zero ||
                    credential.CredentialBlobSize == 0 ||
                    credential.CredentialBlobSize > MaximumBlobBytes ||
                    (credential.CredentialBlobSize & 1) != 0)
                {
                    return null;
                }

                var characterCount = checked((int)credential.CredentialBlobSize / 2);
                var characters = new char[characterCount];
                try
                {
                    Marshal.Copy(credential.CredentialBlob, characters, 0, characterCount);
                    var length = characterCount;
                    while (length > 0 && characters[length - 1] == '\0')
                    {
                        length--;
                    }

                    for (var index = 0; index < length; index++)
                    {
                        if (characters[index] == '\0')
                        {
                            return null;
                        }
                    }

                    return length == 0 ? null : new string(characters, 0, length);
                }
                finally
                {
                    Array.Clear(characters, 0, characters.Length);
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                if (pointer != IntPtr.Zero)
                {
                    CredFree(pointer);
                }
            }
        }
    }
}
'@ -ErrorAction Stop
        }

        return [SpeechMessage.P72SliceCLive.CredentialReader]::ReadGenericSecret($credentialTarget)
    }
    catch {
        return $null
    }
}

function Test-Matrix {
    <#
    .SYNOPSIS
        驗證 Slice C coverage rows 仍是 CE 9.1/Data8 required-for-activation contract。
    #>
    param([object] $Matrix)

    if ($null -eq $Matrix -or
        $Matrix.schemaVersion -cne 'p7.2.fixture-activation.v1' -or
        $Matrix.defaultDispatch -cne 'fail-closed' -or
        $Matrix.allowedConnector -cne 'Data8' -or
        $Matrix.defaultCeSupport.ce82 -cne 'unsupported') {
        return $false
    }

    $expectations = @(
        @{ Id = 'list-membership-association'; Operations = @($expectedOperationIds[0], $expectedOperationIds[1]) },
        @{ Id = 'small-group-fixed-fields'; Operations = @($expectedOperationIds[2]) },
        @{ Id = 'contact-owner-assignment'; Operations = @($expectedOperationIds[3]) },
        @{ Id = 'contact-list-transfer-composite'; Operations = @($expectedOperationIds[4]) }
    )
    foreach ($expectation in $expectations) {
        $slice = @($Matrix.slices | Where-Object { $_.id -ceq $expectation.Id })
        if ($slice.Count -ne 1 -or
            $slice[0].status -cne 'required-for-activation' -or
            $slice[0].requiredCeVersion -cne '9.1' -or
            $slice[0].realCeEvidence.ce82 -cne 'unsupported' -or
            $slice[0].realCeEvidence.ce91 -cnotin @('pending', 'complete') -or
            @($slice[0].operationIds).Count -ne $expectation.Operations.Count) {
            return $false
        }

        for ($index = 0; $index -lt $expectation.Operations.Count; $index++) {
            if ($slice[0].operationIds[$index] -cne $expectation.Operations[$index]) {
                return $false
            }
        }
    }

    return $true
}

function Test-ProfileInput {
    <#
    .SYNOPSIS
        驗證既有 P6.2 profile 僅作為 crm91 Credential Manager reference 的來源。

    .DESCRIPTION
        profile 中的 Official Worker metadata 不會被啟動或傳入 child test；本 runner 固定使用
        ChurchReport Embedded + Data8。任何 profile identity/reference 不精確符合既有 target
        都 fail closed，避免以別的 deployment 或 credential 執行 CE operation。
    #>
    param([object] $Profile)

    if ($null -eq $Profile -or $Profile.schemaVersion -ne 1) {
        return $false
    }

    $profiles = @($Profile.profiles)
    $crm91 = @($profiles | Where-Object { $_.profileAlias -ceq 'crm91' })
    return $crm91.Count -eq 1 -and
        $crm91[0].workerKind -ceq 'OfficialCrm91Worker' -and
        $crm91[0].authentication -ceq 'Ifd' -and
        $crm91[0].identity.mode -ceq 'WindowsCredentialReference' -and
        $crm91[0].identity.reference -ceq $credentialTarget
}

function Test-ChurchReportData8Configuration {
    <#
    .SYNOPSIS
        確認 deployment-owned sunnyvalechback CE 9.1 與所有既有 Package02 consumer flags 均為 false。
    #>
    param([string] $RepositoryRoot)

    try {
        $production = Read-StrictTextFile (Join-Path $RepositoryRoot 'SpeechMessageProducts.ChurchReport\appsettings.json') 512KB 'churchreport-config-invalid'
        $development = Read-StrictTextFile (Join-Path $RepositoryRoot 'SpeechMessageProducts.ChurchReport\appsettings.Development.json') 128KB 'churchreport-config-invalid'
        $catalogPattern = '"sunnyvalechback"\s*:\s*\{[^\r\n\}]*"CeVersion"\s*:\s*"9\.1"[^\r\n\}]*"ServiceUri"\s*:\s*"https://sunnyvalechback\.speechmessage\.com\.tw/XRMServices/2011/Organization\.svc"'
        $profilePattern = '"ProfileAlias"\s*:\s*"sunnyvalechback"'
        $modePattern = '"ConnectionMode"\s*:\s*"Embedded"'
        $readFlagPattern = '"Package01FeeReadsEnabled"\s*:\s*false'
        $basicFlagPattern = '"Package02ContactBasicInfoUpdatesEnabled"\s*:\s*false'
        $profileFlagPattern = '"Package02ContactProfileOperationsEnabled"\s*:\s*false'
        return [Regex]::IsMatch($production, $catalogPattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase) -and
            [Regex]::IsMatch($development, $profilePattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase) -and
            [Regex]::IsMatch($development, $modePattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase) -and
            [Regex]::IsMatch($development, $readFlagPattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase) -and
            [Regex]::IsMatch($development, $basicFlagPattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase) -and
            [Regex]::IsMatch($development, $profileFlagPattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    }
    catch {
        return $false
    }
}

function Test-SourceFixtureDescriptor {
    <#
    .SYNOPSIS
        驗證並重用既有 Slice A contact descriptor，避免另行要求 operator 貼 contact GUID。
    #>
    param(
        [object] $Fixture,
        [string] $CurrentIdentity
    )

    return $null -ne $Fixture -and
        $Fixture.schemaVersion -eq 1 -and
        $Fixture.fixtureId -ceq 'p7.2-contact-basic-info' -and
        $Fixture.profileAlias -ceq $expectedProfileAlias -and
        $Fixture.ceVersion -ceq '9.1' -and
        $Fixture.connector -ceq 'Data8' -and
        $Fixture.marker -ceq 'p7.2-contact-basic-info' -and
        (Test-NonEmptyGuid $Fixture.contactId) -and
        (Test-SafeOwnerIdentity $Fixture.ownerIdentity) -and
        [string]::Equals($Fixture.ownerIdentity, $CurrentIdentity, [StringComparison]::OrdinalIgnoreCase)
}

function Test-SliceCFixtureDescriptor {
    <#
    .SYNOPSIS
        驗證 Slice C task-owned graph descriptor，並確認它只能搭配目前 operator 的 Slice A contact。

    .DESCRIPTION
        現有 bridge/store 沒有 generic discovery/provision API；因此 runner 不猜測 schema、不建立
        任意 CRM graph，也不要求 operator 在 command line 貼 GUID。smallGroupExpectedRelationshipListId
        必須是與所有 Slice C mutation list 都不同的專用 task-owned relationship list，避免 expected
        projection 與 add/remove/transfer rollback graph 共用 identity，或 bridge 退回以 leader 對全組織
        進行廣泛查詢。只有由已核准 provisioning 流程寫入的本機 descriptor 可通過，缺失或不完整時
        輸出 no-go 並且不啟動 child process。
    #>
    param(
        [object] $Fixture,
        [object] $SourceFixture,
        [string] $CurrentIdentity
    )

    if ($null -eq $Fixture -or
        $Fixture.schemaVersion -ne 1 -or
        $Fixture.fixtureId -cne 'p7.2-list-management' -or
        $Fixture.profileAlias -cne $expectedProfileAlias -or
        $Fixture.ceVersion -cne '9.1' -or
        $Fixture.connector -cne 'Data8' -or
        $Fixture.marker -cne $expectedFixtureMarker -or
        -not (Test-SafeOwnerIdentity $Fixture.ownerIdentity) -or
        -not [string]::Equals($Fixture.ownerIdentity, $CurrentIdentity, [StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    $expectedPropertyNames = @(
        'schemaVersion',
        'fixtureId',
        'profileAlias',
        'ceVersion',
        'connector',
        'marker',
        'ownerIdentity',
        'addListId',
        'removeListId',
        'smallGroupListId',
        'smallGroupTargetLeaderContactId',
        'smallGroupExpectedRelationshipListId',
        'transferSourceListId',
        'transferTargetListId',
        'transferWeekStartUtc'
    )
    $actualPropertyNames = @($Fixture.PSObject.Properties.Name)
    if ($actualPropertyNames.Count -ne $expectedPropertyNames.Count -or
        @($actualPropertyNames | Where-Object { $_ -cnotin $expectedPropertyNames }).Count -ne 0 -or
        @($expectedPropertyNames | Where-Object { $_ -cnotin $actualPropertyNames }).Count -ne 0) {
        return $false
    }

    $requiredGuidProperties = @(
        'addListId',
        'removeListId',
        'smallGroupListId',
        'smallGroupTargetLeaderContactId',
        'smallGroupExpectedRelationshipListId',
        'transferSourceListId',
        'transferTargetListId'
    )
    foreach ($name in $requiredGuidProperties) {
        if ($null -eq $Fixture.PSObject.Properties[$name] -or -not (Test-NonEmptyGuid $Fixture.$name)) {
            return $false
        }
    }

    $listIds = @(
        [string]$Fixture.addListId,
        [string]$Fixture.removeListId,
        [string]$Fixture.smallGroupListId,
        [string]$Fixture.smallGroupExpectedRelationshipListId,
        [string]$Fixture.transferSourceListId,
        [string]$Fixture.transferTargetListId
    )
    if (@($listIds | Select-Object -Unique).Count -ne $listIds.Count) {
        return $false
    }

    $weekStart = [DateTimeOffset]::MinValue
    if ($null -eq $Fixture.PSObject.Properties['transferWeekStartUtc'] -or
        -not [DateTimeOffset]::TryParseExact(
            [string]$Fixture.transferWeekStartUtc,
            'O',
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal,
            [ref]$weekStart) -or
        $weekStart.Offset -ne [TimeSpan]::Zero -or
        $weekStart.TimeOfDay -ne [TimeSpan]::Zero -or
        $weekStart.DayOfWeek -ne [DayOfWeek]::Sunday) {
        return $false
    }

    # source contact 不重複寫入 Slice C descriptor；它由已驗證 Slice A descriptor 帶入 child
    # process，減少 operator 需要處理的 identity 數量並避免兩份 descriptor 漂移。
    return $null -ne $SourceFixture -and (Test-NonEmptyGuid $SourceFixture.contactId)
}

function Test-StrictPropertyNames {
    <#
    .SYNOPSIS
        驗證 fresh-fixture ledger/evidence 的 top-level JSON 欄位完全符合固定 schema。

    .DESCRIPTION
        Parent 不接受額外欄位或缺欄位，避免 child 把 CRM ID、endpoint、credential、token、
        cookie、原始例外或其他跨 session 狀態偷偷帶出 evidence boundary。
    #>
    param(
        [object] $Value,
        [string[]] $ExpectedNames
    )

    if ($null -eq $Value -or $null -eq $ExpectedNames) {
        return $false
    }

    $actualNames = @($Value.PSObject.Properties.Name)
    return $actualNames.Count -eq $ExpectedNames.Count -and
        @($actualNames | Where-Object { $_ -cnotin $ExpectedNames }).Count -eq 0 -and
        @($ExpectedNames | Where-Object { $_ -cnotin $actualNames }).Count -eq 0
}

function Test-StrictFreshFixtureSchemaVersion {
    <#
    .SYNOPSIS
        驗證 fresh-fixture JSON schemaVersion 是唯一允許的 integral numeric version。

    .DESCRIPTION
        ConvertFrom-Json 對未帶 decimal/exponent 的 JSON 整數產生 Int32；quoted 值、decimal、
        exponent、Boolean、null 與集合則不是可接受的同值 token。不能使用 PowerShell 鬆散的 -eq/-ne
        比較，否則 child 可把不同 wire schema 偽裝成 evidence/diagnostic 的 version 1 或 ledger 的
        version 2，導致 parent 從未證明的資料跨越 process boundary。此函式沒有快取或可變 shared
        state；每次讀取都重新驗證原始 parser type，失敗一律交由既有 no-go 邊界處理。
    #>
    param(
        [object] $Value,
        [int] $ExpectedVersion
    )

    return $Value -is [int] -and $Value -eq $ExpectedVersion
}

function Read-StrictFinalCrLfJsonFile {
    <#
    .SYNOPSIS
        讀取要求 final CRLF 的 fresh-fixture ledger 或 evidence。

    .DESCRIPTION
        此 wrapper 將 final-CRLF requirement 交給唯一的 strict JSON reader，因此 UTF-8、BOM、
        bare-LF、raw duplicate-key、schema-parser 與 byte lifetime 都由同一個 bounded ownership path
        驗證，避免未來兩條 parser 邊界漂移。
    #>
    param(
        [string] $Path,
        [int] $MaximumBytes,
        [string] $FailureReason
    )

    return Read-StrictJsonFile -Path $Path -MaximumBytes $MaximumBytes -FailureReason $FailureReason -RequireFinalCrLf
}

function Get-StrictFreshFixtureEvidenceFile {
    <#
    .SYNOPSIS
        讀取並驗證 fresh provision/cleanup child 的唯一去識別化 evidence。

    .DESCRIPTION
        Evidence 只包含 lane、結果分類、operationExecuted、descriptor publication readiness
        與固定 feature-flag false；child exit code 仍由 parent 先驗證，JSON 不可取代 process
        lifecycle proof。
    #>
    param(
        [string] $EvidencePath,
        [ValidateSet('provision', 'cleanup')]
        [string] $ExpectedLane
    )

    $evidence = Read-StrictFinalCrLfJsonFile -Path $EvidencePath -MaximumBytes 32768 -FailureReason 'fresh-fixture-evidence-unavailable'
    $expectedNames = @(
        'schemaVersion',
        'lane',
        'outcome',
        'reason',
        'operationExecuted',
        'descriptorPublicationReady',
        'featureFlagChanged'
    )
    # reason 是 child-to-parent 的分類邊界，必須依 lane 使用完整但有限的 published vocabulary。
    # 這些值不含 CRM ID、identity、路徑、transport detail 或 exception；未知字串即使搭配 no-go 與
    # descriptorPublicationReady=false 也不得穿越 console handoff，避免未受控 child state 留存或洩漏。
    $allowedReasons = if ($ExpectedLane -ceq 'provision') {
        @(
            'fresh-fixture-provisioned',
            'fixture-precondition-failed',
            'baseline-owner-unavailable',
            'fresh-source-readback-failed',
            'fresh-leader-readback-failed',
            'fresh-relationship-readback-failed',
            'remove-membership-readback-failed',
            'transfer-source-membership-readback-failed',
            'baseline-owner-readback-failed',
            'fresh-graph-unproven',
            'provisioning-ambiguous',
            'runtime-failure',
            'cleanup-failure'
        )
    }
    else {
        @(
            'fresh-fixture-cleaned',
            'cleanup-precondition-failed',
            'cleanup-membership-readback-failed',
            'cleanup-relationship-readback-failed',
            'cleanup-source-readback-failed',
            'cleanup-leader-readback-failed',
            'cleanup-ambiguous',
            'runtime-failure',
            'cleanup-failure'
        )
    }
    if (-not (Test-StrictPropertyNames -Value $evidence -ExpectedNames $expectedNames) -or
        -not (Test-StrictFreshFixtureSchemaVersion -Value $evidence.schemaVersion -ExpectedVersion 1) -or
        $evidence.lane -cne $ExpectedLane -or
        $evidence.outcome -cnotin @('go', 'no-go') -or
        $evidence.reason -isnot [string] -or
        $evidence.reason -cnotin $allowedReasons -or
        $evidence.operationExecuted -isnot [bool] -or
        $evidence.descriptorPublicationReady -isnot [bool] -or
        $evidence.featureFlagChanged -ne $false) {
        throw 'fresh-fixture-evidence-unavailable'
    }

    $expectedGoReason = if ($ExpectedLane -eq 'provision') {
        'fresh-fixture-provisioned'
    }
    else {
        'fresh-fixture-cleaned'
    }
    $expectedDescriptorPublicationReady = $ExpectedLane -eq 'provision'
    if ($evidence.outcome -eq 'go' -and
        ($evidence.reason -cne $expectedGoReason -or
         $evidence.operationExecuted -ne $true -or
         $evidence.descriptorPublicationReady -ne $expectedDescriptorPublicationReady)) {
        throw 'fresh-fixture-evidence-unavailable'
    }

    if ($evidence.outcome -eq 'no-go' -and $evidence.descriptorPublicationReady -ne $false) {
        throw 'fresh-fixture-evidence-unavailable'
    }

    return $evidence
}

function Get-StrictFreshFixtureChildFailureDiagnosticCategory {
    <#
    .SYNOPSIS
        讀取 fresh provision 非零 child exit 的非授權診斷分類。

    .DESCRIPTION
        此函式只在 parent 已確認 child ExitCode 非零後使用。它不讀取或信任 child evidence，
        不會發布 descriptor、執行 cleanup、改變 feature flag 或允許重試；其唯一輸出是固定
        allowlist 的去識別化 category，協助下一次經明確授權的獨立診斷定位前置失敗邊界。
        診斷檔由 parent-owned temporary root 侷限，讀取後仍由 parent finally 刪除；任何路徑、
        編碼、schema 或內容問題都回傳 null，以保留既有 child-process-failed fail-closed 結果。
    #>
    param(
        [string] $DiagnosticPath,
        [string] $OwnedRoot
    )

    try {
        if ([string]::IsNullOrWhiteSpace($DiagnosticPath) -or
            [string]::IsNullOrWhiteSpace($OwnedRoot)) {
            return $null
        }

        $resolvedRoot = [IO.Path]::GetFullPath($OwnedRoot)
        $resolvedPath = [IO.Path]::GetFullPath($DiagnosticPath)
        if (-not (Test-Path -LiteralPath $resolvedRoot -PathType Container) -or
            -not (Test-Path -LiteralPath $resolvedPath -PathType Leaf) -or
            -not [string]::Equals([IO.Path]::GetDirectoryName($resolvedPath), $resolvedRoot, [StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::GetFileName($resolvedPath) -cne 'P72FreshSliceCFixtureDiagnostic.json') {
            return $null
        }

        $rootItem = Get-Item -LiteralPath $resolvedRoot -Force -ErrorAction Stop
        $fileItem = Get-Item -LiteralPath $resolvedPath -Force -ErrorAction Stop
        if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            ($fileItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            return $null
        }

        $diagnostic = Read-StrictFinalCrLfJsonFile `
            -Path $resolvedPath `
            -MaximumBytes 1024 `
            -FailureReason 'fresh-fixture-diagnostic-invalid'
        if (-not (Test-StrictPropertyNames -Value $diagnostic -ExpectedNames @('schemaVersion', 'category')) -or
            -not (Test-StrictFreshFixtureSchemaVersion -Value $diagnostic.schemaVersion -ExpectedVersion 1) -or
            $diagnostic.category -isnot [string] -or
            $diagnostic.category -cnotin @(
                'fixture-precondition-failed',
                'baseline-owner-unavailable',
                'fresh-source-readback-failed',
                'fresh-leader-readback-failed',
                'fresh-relationship-readback-failed',
                'remove-membership-readback-failed',
                'transfer-source-membership-readback-failed',
                'baseline-owner-readback-failed',
                'fresh-graph-unproven',
                'provisioning-ambiguous',
                'runtime-failure',
                'cleanup-failure')) {
            return $null
        }

        return [string]$diagnostic.category
    }
    catch {
        return $null
    }
}

function Get-StrictFreshFixtureLedger {
    <#
    .SYNOPSIS
        讀取 current-user fresh-fixture pending ledger，作為 descriptor publication 的唯一 ID 來源。

    .DESCRIPTION
        Ledger 必須位於 parent 指定的 local-app-data root、固定檔名、固定 profile/connector/CE
        binding，且只允許 final fresh-graph-proven stage。所有 ID 只留在本機控制面，不進 console、
        evidence、TRX 或產品 request。
    #>
    param(
        [string] $Path,
        [string] $OwnedRoot,
        [string] $CurrentIdentity,
        [ValidateSet('fresh-graph-proven', 'cleanup-leader-contact-deleted')]
        [string] $ExpectedStage = 'fresh-graph-proven'
    )

    $resolvedRoot = [IO.Path]::GetFullPath($OwnedRoot)
    $resolvedPath = [IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $resolvedRoot -PathType Container) -or
        -not (Test-Path -LiteralPath $resolvedPath -PathType Leaf) -or
        -not [string]::Equals([IO.Path]::GetDirectoryName($resolvedPath), $resolvedRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedPath) -cne 'fresh-slice-c-ledger.json') {
        throw 'fresh-fixture-ledger-unavailable'
    }

    $rootItem = Get-Item -LiteralPath $resolvedRoot -Force -ErrorAction Stop
    $fileItem = Get-Item -LiteralPath $resolvedPath -Force -ErrorAction Stop
    if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        ($fileItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'fresh-fixture-ledger-unavailable'
    }

    $ledger = Read-StrictFinalCrLfJsonFile -Path $resolvedPath -MaximumBytes 32768 -FailureReason 'fresh-fixture-ledger-unavailable'
    $expectedNames = @(
        'schemaVersion',
        'fixtureId',
        'profileAlias',
        'ceVersion',
        'connector',
        'ownerIdentity',
        'stage',
        'nonce',
        'sourceContactId',
        'leaderContactId',
        'relationshipListId',
        'originalTargetLeaderContactId'
    )
    if (-not (Test-StrictPropertyNames -Value $ledger -ExpectedNames $expectedNames) -or
        -not (Test-StrictFreshFixtureSchemaVersion -Value $ledger.schemaVersion -ExpectedVersion 2) -or
        $ledger.fixtureId -cne 'p7.2-slice-c-fresh-fixture' -or
        $ledger.profileAlias -cne 'crm91' -or
        $ledger.ceVersion -cne '9.1' -or
        $ledger.connector -cne 'Data8' -or
        $ledger.ownerIdentity -isnot [string] -or
        -not (Test-SafeOwnerIdentity $ledger.ownerIdentity) -or
        -not [string]::Equals($ledger.ownerIdentity, $CurrentIdentity, [StringComparison]::OrdinalIgnoreCase) -or
        $ledger.stage -cne $ExpectedStage -or
        -not (Test-NonEmptyGuid $ledger.nonce) -or
        -not (Test-NonEmptyGuid $ledger.sourceContactId) -or
        -not (Test-NonEmptyGuid $ledger.leaderContactId) -or
        -not (Test-NonEmptyGuid $ledger.relationshipListId) -or
        -not (Test-NonEmptyGuid $ledger.originalTargetLeaderContactId) -or
        $ledger.originalTargetLeaderContactId -eq $ledger.leaderContactId) {
        throw 'fresh-fixture-ledger-unavailable'
    }

    return $ledger
}

function Write-AtomicStrictJsonFile {
    <#
    .SYNOPSIS
        以 UTF-8 no-BOM、CRLF、create-new temporary file 寫出單一 local descriptor。

    .DESCRIPTION
        只寫入 parent 已驗證的 descriptor 目標；bytes、stream 與 temporary path 都由本次
        invocation 擁有，flush 失敗不會留下可被下一個 session 誤信任的 partial file。
    #>
    param(
        [string] $Path,
        [string] $JsonText
    )

    $directory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Path))
    if ([string]::IsNullOrWhiteSpace($directory) -or -not (Test-Path -LiteralPath $directory -PathType Container)) {
        throw 'fresh-descriptor-publication-failed'
    }

    $normalized = ($JsonText -replace "`r?`n", "`n").Replace("`r", "`n").Replace("`n", "`r`n")
    if (-not $normalized.EndsWith("`r`n", [StringComparison]::Ordinal)) {
        $normalized += "`r`n"
    }

    $bytes = [Text.UTF8Encoding]::new($false, $true).GetBytes($normalized)
    $temporaryPath = Join-Path $directory ('.p7-2-fresh-descriptor.tmp-' + [Guid]::NewGuid().ToString('N'))
    # Windows PowerShell/.NET Framework 的 File.Replace 不能將 $null 綁定為 backup path。使用同一目錄中、
    # 本次 invocation 唯一擁有的 random backup path 可保留原子取代語意；成功後立即精確刪除。若
    # 任一步驟失敗，將由上層 fresh transaction quarantine descriptors 並保留 ledger，不會用舊 bytes 進行 rollback。
    $backupPath = Join-Path $directory ('.p7-2-fresh-descriptor.backup-' + [Guid]::NewGuid().ToString('N'))
    try {
        $stream = [IO.FileStream]::new($temporaryPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None, 4096, [IO.FileOptions]::WriteThrough)
        try {
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush($true)
        }
        finally {
            $stream.Dispose()
        }

        if (Test-Path -LiteralPath $Path -PathType Leaf) {
            [IO.File]::Replace($temporaryPath, $Path, $backupPath, $true)
            if (-not (Test-Path -LiteralPath $backupPath -PathType Leaf)) {
                throw 'fresh-descriptor-publication-failed'
            }
            [IO.File]::Delete($backupPath)
            if (Test-Path -LiteralPath $backupPath -PathType Leaf) {
                throw 'fresh-descriptor-publication-failed'
            }
        }
        else {
            [IO.File]::Move($temporaryPath, $Path)
        }
    }
    catch {
        throw 'fresh-descriptor-publication-failed'
    }
    finally {
        [Array]::Clear($bytes, 0, $bytes.Length)
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
        }
        if (Test-Path -LiteralPath $backupPath -PathType Leaf) {
            Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue
        }
    }
}

function Get-CurrentUserFreshFixtureControlPlaneRoots {
    <#
    .SYNOPSIS
        建立並驗證只屬於目前 Windows 使用者的 P7.2 fresh-fixture control-plane 路徑。

    .DESCRIPTION
        此路徑僅保存本機 descriptor 與 recovery ledger；它不包含密碼、endpoint、token、
        browser cookie 或 CRM response。每一個既有或剛建立的 path segment 都拒絕 reparse
        point，避免其他 session 把 parent/child 的 ID-only recovery state 重新導向。這是一次
        explicit fresh invocation 的固定成本，不能以共用 static cache 或任意 caller path 取代。
    #>
    $localAppData = [Environment]::GetEnvironmentVariable('LOCALAPPDATA', 'Process')
    if ([string]::IsNullOrWhiteSpace($localAppData) -or $localAppData.IndexOfAny([char[]]@("`0", "`r", "`n")) -ge 0) {
        throw 'fresh-fixture-local-root-unavailable'
    }

    $root = [IO.Path]::GetFullPath($localAppData)
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        throw 'fresh-fixture-local-root-unavailable'
    }

    $rootItem = Get-Item -LiteralPath $root -Force -ErrorAction Stop
    if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'fresh-fixture-local-root-unavailable'
    }

    $descriptorRoot = $root
    foreach ($segment in @('SpeechMessage', 'Dynamics', 'P7.2')) {
        $descriptorRoot = Join-Path $descriptorRoot $segment
        if (-not (Test-Path -LiteralPath $descriptorRoot -PathType Container)) {
            if (Test-Path -LiteralPath $descriptorRoot) {
                throw 'fresh-fixture-local-root-unavailable'
            }
            [void][IO.Directory]::CreateDirectory($descriptorRoot)
        }

        $item = Get-Item -LiteralPath $descriptorRoot -Force -ErrorAction Stop
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'fresh-fixture-local-root-unavailable'
        }
    }

    $ledgerRoot = Join-Path $descriptorRoot 'FreshSliceC'
    if (-not (Test-Path -LiteralPath $ledgerRoot -PathType Container)) {
        if (Test-Path -LiteralPath $ledgerRoot) {
            throw 'fresh-fixture-local-root-unavailable'
        }
        [void][IO.Directory]::CreateDirectory($ledgerRoot)
    }

    $ledgerItem = Get-Item -LiteralPath $ledgerRoot -Force -ErrorAction Stop
    if (($ledgerItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'fresh-fixture-local-root-unavailable'
    }

    return [pscustomobject]@{
        descriptorRoot = $descriptorRoot
        ledgerRoot = $ledgerRoot
    }
}

function Assert-CurrentUserFreshFixtureDescriptorPath {
    <#
    .SYNOPSIS
        限定 fresh descriptor transaction 只能寫入預定的 current-user P7.2 檔案。

    .DESCRIPTION
        Provision child 沒有 descriptor path；只有 parent 在完整 graph evidence 與 ledger proof
        後能寫入這兩個固定檔案。拒絕任意 path、錯誤檔名、reparse point 或缺檔，避免成功的
        CRM fixture 被用來覆寫其他使用者／產品／profile 的本機設定。
    #>
    param(
        [string] $Path,
        [string] $DescriptorRoot,
        [string] $ExpectedFileName
    )

    $resolvedPath = [IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf) -or
        -not [string]::Equals([IO.Path]::GetDirectoryName($resolvedPath), $DescriptorRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedPath) -cne $ExpectedFileName) {
        throw 'fresh-fixture-descriptor-path-invalid'
    }

    $item = Get-Item -LiteralPath $resolvedPath -Force -ErrorAction Stop
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'fresh-fixture-descriptor-path-invalid'
    }
}

function Publish-FreshFixtureDescriptorPair {
    <#
    .SYNOPSIS
        將完全 proven fresh graph 的 source/leader/relationship IDs 發布為一組本機 descriptors。

    .DESCRIPTION
        兩個 descriptor 均以固定 schema 重新投影，絕不保留 child 原始 JSON 或 caller-provided
        extra field。先寫 source 再寫 Slice C，任一寫入或讀回驗證失敗便以 invocation 內的原始
        strict bytes 回復已寫的檔案；若回復也失敗則回傳 release-blocking no-go，且保留 pending
        ledger。此 transaction 從不觸及 stale CRM rows、feature flag、流量或 browser session。
    #>
    param(
        [string] $SourcePath,
        [string] $FixturePath,
        [string] $DescriptorRoot,
        [object] $SourceFixture,
        [object] $Fixture,
        [object] $Ledger,
        [string] $CurrentIdentity
    )

    Assert-CurrentUserFreshFixtureDescriptorPath -Path $SourcePath -DescriptorRoot $DescriptorRoot -ExpectedFileName 'contact-basic-info-fixture.json'
    Assert-CurrentUserFreshFixtureDescriptorPath -Path $FixturePath -DescriptorRoot $DescriptorRoot -ExpectedFileName 'list-management-fixture.json'
    if ([string]::Equals([IO.Path]::GetFullPath($SourcePath), [IO.Path]::GetFullPath($FixturePath), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'fresh-fixture-descriptor-path-invalid'
    }

    # 發佈前仍然要驗證舊檔案的編碼與大小，但不保留其 bytes 作為 rollback 來源。一旦 fresh
    # transaction 部分失敗，舊的 descriptor 不再能代表 fresh graph；重新啟用它們會使 execution/cleanup
    # lane 誤用未證明的 CRM IDs。唯一的 recovery 權威是仍保留的 strict pending ledger。
    [void](Read-StrictTextFile -Path $SourcePath -MaximumBytes 32768 -FailureReason 'fresh-descriptor-publication-failed')
    [void](Read-StrictTextFile -Path $FixturePath -MaximumBytes 32768 -FailureReason 'fresh-descriptor-publication-failed')
    $updatedSource = [ordered]@{
        schemaVersion = 1
        fixtureId = 'p7.2-contact-basic-info'
        profileAlias = $expectedProfileAlias
        ceVersion = '9.1'
        connector = 'Data8'
        marker = 'p7.2-contact-basic-info'
        contactId = [string]$Ledger.sourceContactId
        ownerIdentity = $CurrentIdentity
    }
    $updatedFixture = [ordered]@{
        schemaVersion = 1
        fixtureId = 'p7.2-list-management'
        profileAlias = $expectedProfileAlias
        ceVersion = '9.1'
        connector = 'Data8'
        marker = $expectedFixtureMarker
        ownerIdentity = $CurrentIdentity
        addListId = [string]$Fixture.addListId
        removeListId = [string]$Fixture.removeListId
        smallGroupListId = [string]$Fixture.smallGroupListId
        smallGroupTargetLeaderContactId = [string]$Ledger.leaderContactId
        smallGroupExpectedRelationshipListId = [string]$Ledger.relationshipListId
        transferSourceListId = [string]$Fixture.transferSourceListId
        transferTargetListId = [string]$Fixture.transferTargetListId
        transferWeekStartUtc = [string]$Fixture.transferWeekStartUtc
    }
    $sourceText = $updatedSource | ConvertTo-Json -Depth 4
    $fixtureText = $updatedFixture | ConvertTo-Json -Depth 4
    try {
        Write-AtomicStrictJsonFile -Path $SourcePath -JsonText $sourceText
        Write-AtomicStrictJsonFile -Path $FixturePath -JsonText $fixtureText

        $publishedSource = Read-StrictJsonFile -Path $SourcePath -MaximumBytes 32768 -FailureReason 'fresh-descriptor-publication-failed'
        $publishedFixture = Read-StrictJsonFile -Path $FixturePath -MaximumBytes 32768 -FailureReason 'fresh-descriptor-publication-failed'
        if (-not (Test-SourceFixtureDescriptor $publishedSource $CurrentIdentity) -or
            -not (Test-SliceCFixtureDescriptor $publishedFixture $publishedSource $CurrentIdentity) -or
            $publishedSource.contactId -cne $Ledger.sourceContactId -or
            $publishedFixture.smallGroupTargetLeaderContactId -cne $Ledger.leaderContactId -or
            $publishedFixture.smallGroupExpectedRelationshipListId -cne $Ledger.relationshipListId) {
            throw 'fresh-descriptor-publication-failed'
        }
    }
    catch {
        # 第一個 write 成功、第二個 write 失敗時，不可以將 stale bytes 寫回任一檔案。這會讓之後的
        # child 誤以為它得到了完整 fresh graph。只能以 exact path 刪除兩檔案來 quarantine；失敗時仍保留
        # ledger 供一條明確的 reconciliation/cleanup lane 處理，不做自動 remote compensation。
        $quarantineSucceeded = $true
        foreach ($descriptorPath in @($FixturePath, $SourcePath)) {
            try {
                $resolvedDescriptorPath = [IO.Path]::GetFullPath($descriptorPath)
                if (Test-Path -LiteralPath $resolvedDescriptorPath -PathType Leaf) {
                    [IO.File]::Delete($resolvedDescriptorPath)
                }
                if (Test-Path -LiteralPath $resolvedDescriptorPath -PathType Leaf) {
                    throw 'fresh-descriptor-publication-failed'
                }
            }
            catch {
                $quarantineSucceeded = $false
            }
        }

        if (-not $quarantineSucceeded) {
            throw 'fresh-descriptor-publication-failed'
        }
        throw 'fresh-descriptor-publication-failed'
    }
}

function Remove-FreshFixtureDescriptorPair {
    <#
    .SYNOPSIS
        僅在 remote cleanup 已有 exact absence proof 後移除仍指向同一 fresh graph 的 descriptors。

    .DESCRIPTION
        Parent 會先重讀並比對目前 descriptor 與 ledger，防止移除被另一個 profile/session 替換的
        檔案。檔案刪除使用 exact paths、無 wildcard、無 recursive delete；失敗時保留 ledger，
        讓後續人工 reconciliation 有一個 current-user ID-only recovery record。
    #>
    param(
        [string] $SourcePath,
        [string] $FixturePath,
        [string] $DescriptorRoot,
        [object] $Ledger,
        [string] $CurrentIdentity
    )

    Assert-CurrentUserFreshFixtureDescriptorPath -Path $SourcePath -DescriptorRoot $DescriptorRoot -ExpectedFileName 'contact-basic-info-fixture.json'
    Assert-CurrentUserFreshFixtureDescriptorPath -Path $FixturePath -DescriptorRoot $DescriptorRoot -ExpectedFileName 'list-management-fixture.json'
    $source = Read-StrictJsonFile -Path $SourcePath -MaximumBytes 32768 -FailureReason 'fresh-descriptor-cleanup-failed'
    $fixture = Read-StrictJsonFile -Path $FixturePath -MaximumBytes 32768 -FailureReason 'fresh-descriptor-cleanup-failed'
    if (-not (Test-SourceFixtureDescriptor $source $CurrentIdentity) -or
        -not (Test-SliceCFixtureDescriptor $fixture $source $CurrentIdentity) -or
        $source.contactId -cne $Ledger.sourceContactId -or
        $fixture.smallGroupTargetLeaderContactId -cne $Ledger.leaderContactId -or
        $fixture.smallGroupExpectedRelationshipListId -cne $Ledger.relationshipListId) {
        throw 'fresh-descriptor-cleanup-failed'
    }

    try {
        [IO.File]::Delete([IO.Path]::GetFullPath($FixturePath))
        if (Test-Path -LiteralPath $FixturePath -PathType Leaf) {
            throw 'fresh-descriptor-cleanup-failed'
        }
        [IO.File]::Delete([IO.Path]::GetFullPath($SourcePath))
        if (Test-Path -LiteralPath $SourcePath -PathType Leaf) {
            throw 'fresh-descriptor-cleanup-failed'
        }
    }
    catch {
        throw 'fresh-descriptor-cleanup-failed'
    }
}

function Remove-StrictFreshFixtureLedger {
    <#
    .SYNOPSIS
        刪除已證明完成 cleanup 的 current-user ledger。

    .DESCRIPTION
        只有 cleanup child 以最後一個 exact-ID absence read-back 寫入 final stage 後，parent 才會
        再讀一次 strict ledger 並比對 provision snapshot。任何 stage、owner、nonce 或 ID 不符皆
        保留 ledger 並 fail closed；不會根據名稱搜尋或刪除其他 profile 的 recovery data。
    #>
    param(
        [string] $Path,
        [string] $OwnedRoot,
        [string] $CurrentIdentity,
        [object] $ExpectedLedger
    )

    $ledger = Get-StrictFreshFixtureLedger -Path $Path -OwnedRoot $OwnedRoot -CurrentIdentity $CurrentIdentity -ExpectedStage 'cleanup-leader-contact-deleted'
    if ($ledger.nonce -cne $ExpectedLedger.nonce -or
        $ledger.sourceContactId -cne $ExpectedLedger.sourceContactId -or
        $ledger.leaderContactId -cne $ExpectedLedger.leaderContactId -or
        $ledger.relationshipListId -cne $ExpectedLedger.relationshipListId -or
        $ledger.originalTargetLeaderContactId -cne $ExpectedLedger.originalTargetLeaderContactId) {
        throw 'fresh-fixture-ledger-cleanup-failed'
    }

    $resolved = [IO.Path]::GetFullPath($Path)
    $item = Get-Item -LiteralPath $resolved -Force -ErrorAction Stop
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'fresh-fixture-ledger-cleanup-failed'
    }

    [IO.File]::Delete($resolved)
    if (Test-Path -LiteralPath $resolved -PathType Leaf) {
        throw 'fresh-fixture-ledger-cleanup-failed'
    }
}

function Get-StrictSliceCEvidenceFile {
    <#
    .SYNOPSIS
        讀取由 Slice C child 唯一寫入、parent 唯一擁有的去識別化 evidence 檔。

    .DESCRIPTION
        TRX stdout 會由 xUnit adapter 重複投影，不能作為單一 evidence record。此 parser 只接受
        parent 建立的 32 KiB UTF-8/CRLF temporary file，並以 exact top-level 與五筆 operation schema
        重新投影 allowlisted scalar，防止 child 夾帶 GUID、路徑、credential、exception 或額外 property
        到最終 console JSON。遺失、編碼錯誤、schema 不符或語意互相矛盾時固定丟出
        evidence-result-unavailable，caller 不得推論成功或重送 CRM mutation。
    #>
    param([string] $EvidencePath)

    $evidence = Read-StrictJsonFile -Path $EvidencePath -MaximumBytes 32768 -FailureReason 'evidence-result-unavailable'
    $topPropertyNames = @(
        'schemaVersion',
        'outcome',
        'reason',
        'profileAlias',
        'deploymentProfileAlias',
        'ceVersion',
        'connector',
        'preflightOnly',
        'operationExecuted',
        'featureFlagChanged',
        'operations'
    )
    $actualTopPropertyNames = @($evidence.PSObject.Properties.Name)
    if ($actualTopPropertyNames.Count -ne $topPropertyNames.Count -or
        @($actualTopPropertyNames | Where-Object { $_ -cnotin $topPropertyNames }).Count -ne 0 -or
        @($topPropertyNames | Where-Object { $_ -cnotin $actualTopPropertyNames }).Count -ne 0) {
        throw 'evidence-result-unavailable'
    }

    $topReasons = @('', 'runtime-failure', 'cleanup-failure', 'fixture-precondition-failed', 'live-evidence-incomplete')
    $operationReasons = @(
        '', 'write-not-required', 'write-response-state-mismatch', 'write-not-committed',
        'write-ambiguous', 'reconciliation-failed', 'cleanup-reconciliation-failed',
        'cleanup-failed', 'cleanup-ambiguous-reconciled', 'write-ambiguous-reconciled',
        'write-result-invalid', 'prior-operation-no-go'
    )
    $reconciliationStates = @('baseline', 'expected', 'expected-after-fault', 'expected-after-invalid-response', 'partial-or-unknown', 'unknown', 'not-started')
    $cleanupStates = @('not-required', 'restored', 'restored-after-fault', 'manual-reconciliation-required', 'not-started')
    $operations = @($evidence.operations)
    if ($evidence.schemaVersion -ne 1 -or
        $evidence.profileAlias -cne $expectedProfileAlias -or
        $evidence.deploymentProfileAlias -cne $expectedDeploymentProfileAlias -or
        $evidence.ceVersion -cne '9.1' -or
        $evidence.connector -cne 'Data8' -or
        $evidence.preflightOnly -ne $false -or
        $evidence.operationExecuted -isnot [bool] -or
        $evidence.featureFlagChanged -ne $false -or
        $evidence.outcome -cnotin @('go', 'no-go') -or
        $evidence.reason -cnotin $topReasons -or
        $operations.Count -ne $expectedOperationIds.Count) {
        throw 'evidence-result-unavailable'
    }

    $operationPropertyNames = @(
        'operationId',
        'outcome',
        'reason',
        'operationExecuted',
        'reconciliationState',
        'cleanupState'
    )
    $projectedOperations = @()
    for ($index = 0; $index -lt $expectedOperationIds.Count; $index++) {
        $operation = $operations[$index]
        $actualOperationPropertyNames = if ($null -eq $operation) { @() } else { @($operation.PSObject.Properties.Name) }
        if ($null -eq $operation -or
            $actualOperationPropertyNames.Count -ne $operationPropertyNames.Count -or
            @($actualOperationPropertyNames | Where-Object { $_ -cnotin $operationPropertyNames }).Count -ne 0 -or
            @($operationPropertyNames | Where-Object { $_ -cnotin $actualOperationPropertyNames }).Count -ne 0 -or
            $operation.operationId -cne $expectedOperationIds[$index] -or
            $operation.outcome -cnotin @('go', 'no-go', 'not-run') -or
            $operation.reason -cnotin $operationReasons -or
            $operation.operationExecuted -isnot [bool] -or
            $operation.reconciliationState -cnotin $reconciliationStates -or
            $operation.cleanupState -cnotin $cleanupStates) {
            throw 'evidence-result-unavailable'
        }

        if ($operation.outcome -ceq 'go' -and
            ($operation.reason -cne '' -or
             -not $operation.operationExecuted -or
             $operation.reconciliationState -cne 'expected' -or
             $operation.cleanupState -cne 'restored')) {
            throw 'evidence-result-unavailable'
        }

        if ($operation.outcome -ceq 'not-run' -and
            ($operation.reason -cne 'prior-operation-no-go' -or
             $operation.operationExecuted -or
             $operation.reconciliationState -cne 'not-started' -or
             $operation.cleanupState -cne 'not-started')) {
            throw 'evidence-result-unavailable'
        }

        $projectedOperations += [pscustomobject]@{
            operationId = [string]$operation.operationId
            outcome = [string]$operation.outcome
            reason = [string]$operation.reason
            operationExecuted = [bool]$operation.operationExecuted
            reconciliationState = [string]$operation.reconciliationState
            cleanupState = [string]$operation.cleanupState
        }
    }

    $allGo = @($projectedOperations | Where-Object { $_.outcome -cne 'go' }).Count -eq 0
    $anyOperationExecuted = @($projectedOperations | Where-Object { $_.operationExecuted }).Count -gt 0
    if ($evidence.operationExecuted -ne $anyOperationExecuted -or
        ($evidence.outcome -ceq 'go' -and
         ($evidence.reason -cne '' -or -not $evidence.operationExecuted -or -not $allGo)) -or
        ($evidence.outcome -ceq 'no-go' -and
         ($evidence.reason -ceq '' -or $allGo))) {
        throw 'evidence-result-unavailable'
    }

    return [pscustomobject]@{
        outcome = [string]$evidence.outcome
        reason = [string]$evidence.reason
        operationExecuted = [bool]$evidence.operationExecuted
        operations = $projectedOperations
    }
}

function Get-StrictSliceCReconciliationEvidenceFile {
    <#
    .SYNOPSIS
        讀取只讀 reconciliation child 的封閉 evidence schema。

    .DESCRIPTION
        reconciliation 與 mutation evidence 使用不同檔名及不同環境變數。此 parser 只接受
        baseline-unprovable 或 cleanup-failure、五筆 not-run operation、readOnlyProbeExecuted、ownerBinding 與固定
        state categories。cleanup-failure 必須連同 readOnlyProbeExecuted=false，否則拒絕 evidence；它刻意拒絕
        child 自行傳入 safeToRetry，因為 retry 權限只能由 parent
        handoff 決定。所有 raw path、GUID、baseline、credential 與例外都在此邊界外消失。
    #>
    param([string] $EvidencePath)

    $evidence = Read-StrictJsonFile -Path $EvidencePath -MaximumBytes 32768 -FailureReason 'evidence-result-unavailable'
    $topPropertyNames = @(
        'schemaVersion',
        'outcome',
        'reason',
        'profileAlias',
        'deploymentProfileAlias',
        'ceVersion',
        'connector',
        'preflightOnly',
        'readOnlyProbeExecuted',
        'operationExecuted',
        'featureFlagChanged',
        'ownerBinding',
        'probeStage',
        'operations'
    )
    $actualTopPropertyNames = @($evidence.PSObject.Properties.Name)
    if ($actualTopPropertyNames.Count -ne $topPropertyNames.Count -or
        @($actualTopPropertyNames | Where-Object { $_ -cnotin $topPropertyNames }).Count -ne 0 -or
        @($topPropertyNames | Where-Object { $_ -cnotin $actualTopPropertyNames }).Count -ne 0) {
        throw 'evidence-result-unavailable'
    }

    $stateNames = @('addMembership', 'removeMembership', 'smallGroup', 'contactOwner', 'transfer')
    $allowedStates = @{
        addMembership = @('baseline-absent', 'unexpected-present', 'unavailable')
        removeMembership = @('baseline-present', 'unexpected-absent', 'unavailable')
        smallGroup = @('not-expected-baseline-unproven', 'expected-baseline-unproven', 'unavailable')
        contactOwner = @('non-target-baseline-unproven', 'target-baseline-unproven', 'unavailable')
        transfer = @('baseline-shape-unproven', 'unexpected-shape-unproven', 'unavailable')
    }
    $operations = @($evidence.operations)
    # cleanup-failure 是唯一允許覆寫一般 baseline reason 的 release-blocking 類別；它只能
    # 與 readOnlyProbeExecuted=false 一起傳遞，避免 child 的未完成資源釋放被誤視為可用證據。
    $isCleanupFailure = $evidence.reason -ceq 'cleanup-failure'
    if ($evidence.schemaVersion -ne 1 -or
        $evidence.profileAlias -cne $expectedProfileAlias -or
        $evidence.deploymentProfileAlias -cne $expectedDeploymentProfileAlias -or
        $evidence.ceVersion -cne '9.1' -or
        $evidence.connector -cne 'Data8' -or
        $evidence.preflightOnly -ne $false -or
        $evidence.readOnlyProbeExecuted -isnot [bool] -or
        $evidence.operationExecuted -isnot [bool] -or
        $evidence.operationExecuted -ne $false -or
        $evidence.featureFlagChanged -ne $false -or
        $evidence.outcome -cne 'no-go' -or
        $evidence.reason -cnotin @('baseline-unprovable', 'cleanup-failure') -or
        ($isCleanupFailure -and $evidence.readOnlyProbeExecuted) -or
        $evidence.ownerBinding -cnotin @('matches-service-identity', 'unavailable') -or
        $evidence.probeStage -cnotin @(
            'not-started',
            'whoami-verified',
            'fixture-store-created',
            'add-membership-read',
            'remove-membership-read',
            'small-group-read',
            'small-group-expected-read',
            'contact-owner-read',
            'transfer-read',
            'classification-complete') -or
        $operations.Count -ne $expectedOperationIds.Count) {
        throw 'evidence-result-unavailable'
    }

    $operationPropertyNames = @(
        'operationId',
        'outcome',
        'reason',
        'operationExecuted',
        'reconciliationState',
        'cleanupState'
    )
    $projectedOperations = @()
    $projectedStates = [ordered]@{}
    for ($index = 0; $index -lt $expectedOperationIds.Count; $index++) {
        $operation = $operations[$index]
        $actualOperationPropertyNames = if ($null -eq $operation) { @() } else { @($operation.PSObject.Properties.Name) }
        $stateName = $stateNames[$index]
        if ($null -eq $operation -or
            $actualOperationPropertyNames.Count -ne $operationPropertyNames.Count -or
            @($actualOperationPropertyNames | Where-Object { $_ -cnotin $operationPropertyNames }).Count -ne 0 -or
            @($operationPropertyNames | Where-Object { $_ -cnotin $actualOperationPropertyNames }).Count -ne 0 -or
            $operation.operationId -cne $expectedOperationIds[$index] -or
            $operation.outcome -cne 'not-run' -or
            $operation.reason -cne 'baseline-unprovable' -or
            $operation.operationExecuted -isnot [bool] -or
            $operation.operationExecuted -ne $false -or
            $operation.reconciliationState -cnotin $allowedStates[$stateName] -or
            $operation.cleanupState -cne 'not-applicable') {
            throw 'evidence-result-unavailable'
        }

        $projectedOperations += [pscustomobject]@{
            operationId = [string]$operation.operationId
            outcome = 'not-run'
            reason = 'baseline-unprovable'
            operationExecuted = $false
            reconciliationState = [string]$operation.reconciliationState
            cleanupState = 'not-applicable'
        }
        $projectedStates[$stateName] = [string]$operation.reconciliationState
    }

    return [pscustomobject]@{
        outcome = 'no-go'
        reason = [string]$evidence.reason
        readOnlyProbeExecuted = [bool]$evidence.readOnlyProbeExecuted
        ownerBinding = [string]$evidence.ownerBinding
        probeStage = [string]$evidence.probeStage
        states = $projectedStates
        operations = $projectedOperations
    }
}

function Get-StrictSliceCRepairEvidenceFile {
    <#
    .SYNOPSIS
        解析 relationship-list repair child 的最小 sanitized evidence schema。

    .DESCRIPTION
        repair 是唯一一個允許對 task-owned relationship list 送出一次 Update 的 lane；
        parser 只接受固定 profile、CE、connector、operation/read-back scalar 與 allowlisted
        reason。任何缺欄位、額外欄位、未預期 outcome 或 operationExecuted/readBackConfirmed
        組合都 fail closed，並由 parent 保持 safeToRetry=false。
    #>
    param([string] $EvidencePath)

    $evidence = Read-StrictJsonFile -Path $EvidencePath -MaximumBytes 32768 -FailureReason 'evidence-result-unavailable'
    $topPropertyNames = @(
        'schemaVersion',
        'outcome',
        'reason',
        'profileAlias',
        'deploymentProfileAlias',
        'ceVersion',
        'connector',
        'preflightOnly',
        'operationExecuted',
        'readBackConfirmed',
        'featureFlagChanged'
    )
    $actualPropertyNames = @($evidence.PSObject.Properties.Name)
    if ($actualPropertyNames.Count -ne $topPropertyNames.Count -or
        @($actualPropertyNames | Where-Object { $_ -cnotin $topPropertyNames }).Count -ne 0 -or
        @($topPropertyNames | Where-Object { $_ -cnotin $actualPropertyNames }).Count -ne 0) {
        throw 'evidence-result-unavailable'
    }

    $allowedReasons = @(
        '',
        'already-repaired',
        'fixture-precondition-failed',
        'fixture-state-unexpected',
        'repair-readback-mismatch',
        'repair-ambiguous',
        'repair-precondition-failed',
        'cleanup-failure'
    )
    if ($evidence.schemaVersion -ne 1 -or
        $evidence.outcome -cnotin @('go', 'no-go') -or
        $evidence.reason -cnotin $allowedReasons -or
        $evidence.profileAlias -cne $expectedProfileAlias -or
        $evidence.deploymentProfileAlias -cne $expectedDeploymentProfileAlias -or
        $evidence.ceVersion -cne '9.1' -or
        $evidence.connector -cne 'Data8' -or
        $evidence.preflightOnly -ne $false -or
        $evidence.operationExecuted -isnot [bool] -or
        $evidence.readBackConfirmed -isnot [bool] -or
        $evidence.featureFlagChanged -ne $false) {
        throw 'evidence-result-unavailable'
    }

    if ($evidence.outcome -ceq 'go') {
        if ($evidence.reason -ceq 'already-repaired') {
            if ($evidence.operationExecuted -or -not $evidence.readBackConfirmed) {
                throw 'evidence-result-unavailable'
            }
        }
        elseif ($evidence.reason -cne '' -or -not $evidence.operationExecuted -or -not $evidence.readBackConfirmed) {
            throw 'evidence-result-unavailable'
        }
    }
    else {
        if ($evidence.reason -ceq '' -or
            ($evidence.reason -in @('fixture-precondition-failed', 'fixture-state-unexpected', 'repair-precondition-failed') -and $evidence.operationExecuted) -or
            ($evidence.reason -in @('repair-readback-mismatch', 'repair-ambiguous') -and -not $evidence.operationExecuted) -or
            $evidence.readBackConfirmed) {
            throw 'evidence-result-unavailable'
        }
    }

    return [pscustomobject]@{
        outcome = [string]$evidence.outcome
        reason = [string]$evidence.reason
        operationExecuted = [bool]$evidence.operationExecuted
        readBackConfirmed = [bool]$evidence.readBackConfirmed
    }
}

function Get-StrictSliceCRepairProbeEvidenceFile {
    <#
    .SYNOPSIS
        解析 relationship-list repair 的唯讀 precondition probe evidence。

    .DESCRIPTION
        Probe 永遠不是 repair 授權；即使所有遠端 proof 成立，child 仍回傳 no-go，
        parent 只投影固定的 precondition 狀態。任何額外欄位、mutation 標記、原始例外、
        GUID 或不在 allowlist 的狀態都 fail closed。
    #>
    param([string] $EvidencePath)

    $evidence = Read-StrictJsonFile -Path $EvidencePath -MaximumBytes 32768 -FailureReason 'evidence-result-unavailable'
    $topPropertyNames = @(
        'schemaVersion',
        'outcome',
        'reason',
        'profileAlias',
        'deploymentProfileAlias',
        'ceVersion',
        'connector',
        'preflightOnly',
        'operationExecuted',
        'readOnlyProbeExecuted',
        'featureFlagChanged',
        'probe'
    )
    $actualTopPropertyNames = @($evidence.PSObject.Properties.Name)
    if ($actualTopPropertyNames.Count -ne $topPropertyNames.Count -or
        @($actualTopPropertyNames | Where-Object { $_ -cnotin $topPropertyNames }).Count -ne 0 -or
        @($topPropertyNames | Where-Object { $_ -cnotin $actualTopPropertyNames }).Count -ne 0) {
        throw 'evidence-result-unavailable'
    }

    $allowedReasons = @('repair-preconditions-proven', 'probe-precondition-failed', 'cleanup-failure')
    if ($evidence.schemaVersion -ne 1 -or
        $evidence.outcome -cne 'no-go' -or
        $evidence.reason -cnotin $allowedReasons -or
        $evidence.profileAlias -cne $expectedProfileAlias -or
        $evidence.deploymentProfileAlias -cne $expectedDeploymentProfileAlias -or
        $evidence.ceVersion -cne '9.1' -or
        $evidence.connector -cne 'Data8' -or
        $evidence.preflightOnly -ne $false -or
        $evidence.operationExecuted -ne $false -or
        $evidence.readOnlyProbeExecuted -isnot [bool] -or
        $evidence.featureFlagChanged -ne $false) {
        throw 'evidence-result-unavailable'
    }

    $probePropertyNames = @(
        'sourceContactMarkerValid',
        'smallGroupListValid',
        'expectedRelationshipListValid',
        'targetLeaderMarkerValid',
        'expectedRelationshipRaceLeaderMatches',
        'expectedRelationshipFieldsState',
        'preconditionState'
    )
    if ($null -eq $evidence.probe) {
        throw 'evidence-result-unavailable'
    }
    $actualProbePropertyNames = @($evidence.probe.PSObject.Properties.Name)
    if ($actualProbePropertyNames.Count -ne $probePropertyNames.Count -or
        @($actualProbePropertyNames | Where-Object { $_ -cnotin $probePropertyNames }).Count -ne 0 -or
        @($probePropertyNames | Where-Object { $_ -cnotin $actualProbePropertyNames }).Count -ne 0) {
        throw 'evidence-result-unavailable'
    }

    $allowedFieldStates = @('blank', 'expected', 'partial', 'unexpected', 'unreadable')
    $allowedPreconditionStates = @('blank-repairable', 'already-repaired', 'partial-or-unexpected', 'provenance-invalid', 'fixture-precondition-failed', 'unavailable')
    if ($evidence.probe.sourceContactMarkerValid -isnot [bool] -or
        $evidence.probe.smallGroupListValid -isnot [bool] -or
        $evidence.probe.expectedRelationshipListValid -isnot [bool] -or
        $evidence.probe.targetLeaderMarkerValid -isnot [bool] -or
        $evidence.probe.expectedRelationshipRaceLeaderMatches -isnot [bool] -or
        $evidence.probe.expectedRelationshipFieldsState -cnotin $allowedFieldStates -or
        $evidence.probe.preconditionState -cnotin $allowedPreconditionStates) {
        throw 'evidence-result-unavailable'
    }

    if ($evidence.reason -ceq 'repair-preconditions-proven' -and -not $evidence.readOnlyProbeExecuted) {
        throw 'evidence-result-unavailable'
    }
    if ($evidence.reason -cne 'repair-preconditions-proven' -and $evidence.readOnlyProbeExecuted) {
        throw 'evidence-result-unavailable'
    }

    return [pscustomobject]@{
        outcome = 'no-go'
        reason = [string]$evidence.reason
        readOnlyProbeExecuted = [bool]$evidence.readOnlyProbeExecuted
        preconditionState = [string]$evidence.probe.preconditionState
        probe = [pscustomobject]@{
            sourceContactMarkerValid = [bool]$evidence.probe.sourceContactMarkerValid
            smallGroupListValid = [bool]$evidence.probe.smallGroupListValid
            expectedRelationshipListValid = [bool]$evidence.probe.expectedRelationshipListValid
            targetLeaderMarkerValid = [bool]$evidence.probe.targetLeaderMarkerValid
            expectedRelationshipRaceLeaderMatches = [bool]$evidence.probe.expectedRelationshipRaceLeaderMatches
            expectedRelationshipFieldsState = [string]$evidence.probe.expectedRelationshipFieldsState
            preconditionState = [string]$evidence.probe.preconditionState
        }
    }
}

function Get-StrictFreshPreflightProbeEvidenceFile {
    <#
    .SYNOPSIS
        驗證 Slice C fresh-fixture 唯讀前置診斷 child 所寫出的固定分類 evidence。

    .DESCRIPTION
        此 parser 是 child 與 parent 間的信任邊界。它只接受固定欄位、固定分類與零 mutation
        bit 的 JSON；任何 CRM ID、名稱、端點、帳密、原始回應、例外、額外欄位或不一致的
        completion state 都會在 descriptor 發布、ledger 寫入、fixture 建立與後續 CE 寫入前
        fail closed。它不保存 evidence、profile、credential 或 CRM 物件；temporary file 的唯一
        cleanup owner 是 parent runner 的 finally。
    #>
    param([string] $EvidencePath)

    $evidence = Read-StrictJsonFile -Path $EvidencePath -MaximumBytes 32768 -FailureReason 'evidence-result-unavailable'
    $topPropertyNames = @(
        'schemaVersion',
        'outcome',
        'reason',
        'profileAlias',
        'deploymentProfileAlias',
        'ceVersion',
        'connector',
        'preflightOnly',
        'operationExecuted',
        'readOnlyProbeExecuted',
        'featureFlagChanged',
        'probe'
    )
    $actualTopPropertyNames = @($evidence.PSObject.Properties.Name)
    if ($actualTopPropertyNames.Count -ne $topPropertyNames.Count -or
        @($actualTopPropertyNames | Where-Object { $_ -cnotin $topPropertyNames }).Count -ne 0 -or
        @($topPropertyNames | Where-Object { $_ -cnotin $actualTopPropertyNames }).Count -ne 0) {
        throw 'evidence-result-unavailable'
    }

    $allowedReasons = @(
        'fresh-preconditions-proven',
        'fresh-preconditions-not-proven',
        'probe-input-invalid',
        'probe-unavailable',
        'cleanup-failure'
    )
    if ($evidence.schemaVersion -ne 1 -or
        $evidence.outcome -cnotin @('go', 'no-go') -or
        $evidence.reason -cnotin $allowedReasons -or
        $evidence.profileAlias -cne $expectedProfileAlias -or
        $evidence.deploymentProfileAlias -cne $expectedDeploymentProfileAlias -or
        $evidence.ceVersion -cne '9.1' -or
        $evidence.connector -cne 'Data8' -or
        $evidence.preflightOnly -ne $false -or
        $evidence.operationExecuted -ne $false -or
        $evidence.readOnlyProbeExecuted -isnot [bool] -or
        $evidence.featureFlagChanged -ne $false -or
        $null -eq $evidence.probe) {
        throw 'evidence-result-unavailable'
    }

    $probePropertyNames = @(
        'requestShape',
        'operationalLists',
        'leaderMarker',
        'ownerKind',
        'ownerState',
        'ownerRelation',
        'weeklyReport'
    )
    $actualProbePropertyNames = @($evidence.probe.PSObject.Properties.Name)
    if ($actualProbePropertyNames.Count -ne $probePropertyNames.Count -or
        @($actualProbePropertyNames | Where-Object { $_ -cnotin $probePropertyNames }).Count -ne 0 -or
        @($probePropertyNames | Where-Object { $_ -cnotin $actualProbePropertyNames }).Count -ne 0) {
        throw 'evidence-result-unavailable'
    }

    $validCategories =
        $evidence.probe.requestShape -cin @('valid', 'invalid') -and
        $evidence.probe.operationalLists -cin @('valid', 'invalid', 'unavailable') -and
        $evidence.probe.leaderMarker -cin @('valid', 'invalid', 'unavailable') -and
        $evidence.probe.ownerKind -cin @('systemuser', 'other-or-missing', 'unavailable') -and
        $evidence.probe.ownerState -cin @('active', 'inactive-or-missing', 'unavailable') -and
        $evidence.probe.ownerRelation -cin @('different-from-data8', 'same-as-data8', 'unavailable') -and
        $evidence.probe.weeklyReport -cin @('exactly-one-active', 'zero-active', 'duplicate-active', 'unavailable')
    $allRemoteUnavailable =
        $evidence.probe.operationalLists -ceq 'unavailable' -and
        $evidence.probe.leaderMarker -ceq 'unavailable' -and
        $evidence.probe.ownerKind -ceq 'unavailable' -and
        $evidence.probe.ownerState -ceq 'unavailable' -and
        $evidence.probe.ownerRelation -ceq 'unavailable' -and
        $evidence.probe.weeklyReport -ceq 'unavailable'
    $allGreen =
        $evidence.probe.requestShape -ceq 'valid' -and
        $evidence.probe.operationalLists -ceq 'valid' -and
        $evidence.probe.leaderMarker -ceq 'valid' -and
        $evidence.probe.ownerKind -ceq 'systemuser' -and
        $evidence.probe.ownerState -ceq 'active' -and
        $evidence.probe.ownerRelation -ceq 'different-from-data8' -and
        # 使用者已確認 zero-active 是目標小組尚未建立本週週報的正常分支；它和唯一週報分支
        # 都只能證明 fresh fixture 可開始。duplicate/unavailable 仍落在 no-go，且 evidence 從不
        # 攜帶 report ID、名稱、日期、數量或任何可供挑選／修補週報的資料。
        $evidence.probe.weeklyReport -cin @('exactly-one-active', 'zero-active')
    $validCombination =
        ($evidence.outcome -ceq 'go' -and
            $evidence.reason -ceq 'fresh-preconditions-proven' -and
            $evidence.readOnlyProbeExecuted -and
            $allGreen) -or
        ($evidence.outcome -ceq 'no-go' -and
            $evidence.reason -ceq 'fresh-preconditions-not-proven' -and
            $evidence.readOnlyProbeExecuted -and
            $evidence.probe.requestShape -ceq 'valid' -and
            -not $allGreen) -or
        ($evidence.outcome -ceq 'no-go' -and
            $evidence.reason -ceq 'probe-input-invalid' -and
            -not $evidence.readOnlyProbeExecuted -and
            $evidence.probe.requestShape -ceq 'invalid' -and
            $allRemoteUnavailable) -or
        ($evidence.outcome -ceq 'no-go' -and
            $evidence.reason -cin @('probe-unavailable', 'cleanup-failure') -and
            -not $evidence.readOnlyProbeExecuted -and
            $evidence.probe.requestShape -ceq 'valid' -and
            $allRemoteUnavailable)
    if (-not $validCategories -or -not $validCombination) {
        throw 'evidence-result-unavailable'
    }

    return [pscustomobject]@{
        outcome = [string]$evidence.outcome
        reason = [string]$evidence.reason
        readOnlyProbeExecuted = [bool]$evidence.readOnlyProbeExecuted
        probe = [pscustomobject]@{
            requestShape = [string]$evidence.probe.requestShape
            operationalLists = [string]$evidence.probe.operationalLists
            leaderMarker = [string]$evidence.probe.leaderMarker
            ownerKind = [string]$evidence.probe.ownerKind
            ownerState = [string]$evidence.probe.ownerState
            ownerRelation = [string]$evidence.probe.ownerRelation
            weeklyReport = [string]$evidence.probe.weeklyReport
        }
    }
}

function Remove-OwnedSliceCTemporaryDirectory {
    <#
    .SYNOPSIS
        刪除本次 handoff 唯一擁有的 Slice C OS 暫存目錄。

    .DESCRIPTION
        temporary evidence 的唯一持有者是 parent runner；child 只獲得固定 evidence file path，不能
        指定或保留 parent directory。本 helper 因此只接受 OS temporary root 的直接子目錄，且名稱必須
        精確符合 ``speechmessage-p7-2-slice-c-`` 加上 32 位 GUID nonce。根目錄與所有已列舉的後代都
        不得是 reparse point，避免 cleanup 跟隨 junction／symlink 而刪除 owner 範圍以外的資料。

        回傳 ``$true`` 代表目錄已確實不存在；所有路徑、名稱、reparse、存取或刪除錯誤均回傳
        ``$false``。呼叫端必須把 ``$false`` 轉為 sanitized No-Go，而不是吞掉錯誤後報告 Green。
        此函式不輸出任何 path 或例外，因此不會把本機檔案系統資訊跨越 operator handoff 邊界。
    #>
    param([string] $TemporaryDirectory)

    if ([string]::IsNullOrWhiteSpace($TemporaryDirectory)) {
        return $false
    }

    try {
        $trimCharacters = [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
        $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd($trimCharacters)
        $fullTemporaryDirectory = [IO.Path]::GetFullPath($TemporaryDirectory).TrimEnd($trimCharacters)
        $directoryName = [IO.Path]::GetFileName($fullTemporaryDirectory)
        $parentDirectory = [IO.Path]::GetDirectoryName($fullTemporaryDirectory)
        if (-not [string]::Equals($parentDirectory, $temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
            -not [Regex]::IsMatch($directoryName, '^speechmessage-p7-2-slice-c-[0-9a-f]{32}$', [Text.RegularExpressions.RegexOptions]::CultureInvariant) -or
            -not (Test-Path -LiteralPath $fullTemporaryDirectory -PathType Container)) {
            return $false
        }

        $rootItem = Get-Item -LiteralPath $fullTemporaryDirectory -Force -ErrorAction Stop
        if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            return $false
        }

        # child 已結束後才進入 finally；仍逐一拒絕 descendant reparse point，確保遞迴刪除不會將
        # temporary evidence 的唯一 owner 擴張到連結目標。目錄很小（唯一 32 KiB evidence file），
        # 故完整列舉不會形成長生命週期記憶體或 I/O 成本。
        foreach ($item in @(Get-ChildItem -LiteralPath $fullTemporaryDirectory -Force -Recurse -ErrorAction Stop)) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                return $false
            }
        }

        Remove-Item -LiteralPath $fullTemporaryDirectory -Force -Recurse -ErrorAction Stop
        return -not (Test-Path -LiteralPath $fullTemporaryDirectory)
    }
    catch {
        return $false
    }
}

function Complete-HandoffResult {
    <#
    .SYNOPSIS
        在輸出唯一 JSON 前完成 Slice C temporary evidence 的確定性 cleanup。

    .DESCRIPTION
        console JSON 是 operator 可據以判斷是否可繼續的最終狀態，故不能先輸出 Green、再在 finally
        靜默發現 evidence directory 刪除失敗。本函式是 parent 的唯一完成點：若本次確實建立過
        directory，會先執行受限 cleanup；成功才清除 owner state，失敗則將原本的 normal、timeout
        或 child-failure 結果統一轉成去識別化 No-Go。finally 仍會作 best-effort second cleanup，但
        絕不反向把已報告的 No-Go 改回 Green。
    #>
    param([object] $Result)

    $completedResult = $Result
    if ($script:temporaryDirectoryCreated) {
        if (Remove-OwnedSliceCTemporaryDirectory $script:temporaryDirectory) {
            $script:temporaryDirectory = $null
            $script:temporaryDirectoryCreated = $false
        }
        else {
            $completedResult = New-TemporaryCleanupFailureResult $Result
        }
    }

    # 唯一 console output 與 process exit code 必須由同一個 cleanup 後 verdict 決定；不可因 child
    # 原本成功而讓已轉成 No-Go 的 handoff 仍以 exit code 0 結束。
    $script:completedHandoffOutcome = [string]$completedResult.outcome
    Write-HandoffResult $completedResult
}

function Quote-Argument {
    <#
    .SYNOPSIS
        將受控 local path 封裝為 ProcessStartInfo argument，拒絕控制字元。
    #>
    param([string] $Value)

    if ([string]::IsNullOrWhiteSpace($Value) -or $Value.IndexOfAny([char[]]@("`0", "`r", "`n")) -ge 0) {
        throw 'dotnet-start-failed'
    }

    return '"' + $Value.Replace('"', '\"') + '"'
}

function New-NotStartedOperations {
    <#
    .SYNOPSIS
        產生無 dispatch 的五段預設摘要，用於 timeout 或 parser failure 的 sanitized JSON。
    #>
    $operations = @()
    foreach ($operationId in $expectedOperationIds) {
        $operations += [pscustomobject]@{
            operationId = $operationId
            outcome = 'not-run'
            reason = 'prior-operation-no-go'
            operationExecuted = $false
            reconciliationState = 'not-started'
            cleanupState = 'not-started'
        }
    }

    return $operations
}

try {
    # 先 snapshot 環境，所有 early exit 都會在 finally 恢復；這避免 parent process 的既有
    # credential/session 變數被 child execution 汙染或在失敗路徑遺留。
    foreach ($name in $inputEnvironmentNames) {
        $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    }

    $resolvedRepositoryPath = [IO.Path]::GetFullPath($RepositoryPath)
    $matrixPath = Join-Path $resolvedRepositoryPath '.trellis\tasks\08-07-churchreport-write-action-function-migrations\p7.2-fixture-activation-matrix.json'
    $testProjectPath = Join-Path $resolvedRepositoryPath 'ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj'
    if (-not (Test-Path -LiteralPath $resolvedRepositoryPath -PathType Container) -or
        -not (Test-Path -LiteralPath $matrixPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $testProjectPath -PathType Leaf)) {
        Write-HandoffResult (New-HandoffResult -Outcome 'error' -Reason 'repository-invalid' -PreflightOnly (-not $liveModeRequested))
        $scriptExitCode = 1
        throw 'result-written'
    }

    $matrix = Read-StrictJsonFile $matrixPath 256KB 'matrix-invalid'
    if (-not (Test-Matrix $matrix)) {
        Write-HandoffResult (New-HandoffResult -Outcome 'error' -Reason 'matrix-invalid' -PreflightOnly (-not $liveModeRequested))
        $scriptExitCode = 1
        throw 'result-written'
    }

    if (-not (Test-ChurchReportData8Configuration $resolvedRepositoryPath)) {
        Write-HandoffResult (New-HandoffResult -Outcome 'no-go' -Reason 'churchreport-config-invalid' -PreflightOnly (-not $liveModeRequested))
        $scriptExitCode = 2
        throw 'result-written'
    }

    if (-not (Test-Path -LiteralPath $ProfileInputPath -PathType Leaf)) {
        Write-HandoffResult (New-HandoffResult -Outcome 'no-go' -Reason 'profile-input-required' -PreflightOnly (-not $liveModeRequested))
        $scriptExitCode = 2
        throw 'result-written'
    }

    $profile = Read-StrictJsonFile $ProfileInputPath 128KB 'profile-input-invalid'
    if (-not (Test-ProfileInput $profile)) {
        Write-HandoffResult (New-HandoffResult -Outcome 'no-go' -Reason 'profile-input-invalid' -PreflightOnly (-not $liveModeRequested))
        $scriptExitCode = 2
        throw 'result-written'
    }

    if (-not (Test-Path -LiteralPath $SourceFixtureDescriptorPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $FixtureDescriptorPath -PathType Leaf)) {
        Write-HandoffResult (New-HandoffResult -Outcome 'no-go' -Reason 'fixture-input-required' -PreflightOnly (-not $liveModeRequested))
        $scriptExitCode = 2
        throw 'result-written'
    }

    $sourceFixture = Read-StrictJsonFile $SourceFixtureDescriptorPath 32KB 'fixture-input-invalid'
    $fixture = Read-StrictJsonFile $FixtureDescriptorPath 32KB 'fixture-input-invalid'
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    if (-not (Test-SourceFixtureDescriptor $sourceFixture $identity) -or
        -not (Test-SliceCFixtureDescriptor $fixture $sourceFixture $identity)) {
        Write-HandoffResult (New-HandoffResult -Outcome 'no-go' -Reason 'fixture-input-invalid' -PreflightOnly (-not $liveModeRequested))
        $scriptExitCode = 2
        throw 'result-written'
    }

    # fresh-fixture provision 與 cleanup 是兩條獨立的遠端 mutation lane。先在所有 descriptor
    # shape/owner proof 都完成後檢查明確確認，卻刻意早於 Credential Manager、temporary directory
    # 與 dotnet child；這使缺少確認的 invocation 無法讀取密碼、保留 ledger 或碰觸 CE。
    if (($ReplaceStaleDescriptor -and -not $isFreshProvisionMode) -or
        ($ConfirmFreshFixtureCleanup -and -not $isFreshCleanupMode)) {
        Write-HandoffResult (New-HandoffResult -Outcome 'no-go' -Reason 'fresh-fixture-confirmation-misused' -PreflightOnly $false)
        $scriptExitCode = 2
        throw 'result-written'
    }

    if ($isFreshProvisionMode -and -not $ReplaceStaleDescriptor) {
        Write-HandoffResult (New-HandoffResult -Outcome 'no-go' -Reason 'fresh-fixture-confirmation-required' -PreflightOnly $false)
        $scriptExitCode = 2
        throw 'result-written'
    }

    if ($isFreshCleanupMode -and -not $ConfirmFreshFixtureCleanup) {
        Write-HandoffResult (New-HandoffResult -Outcome 'no-go' -Reason 'fresh-fixture-cleanup-confirmation-required' -PreflightOnly $false)
        $scriptExitCode = 2
        throw 'result-written'
    }

    # fresh control plane 只能使用目前 Windows 使用者的預定 local-app-data paths；即使呼叫端
    # 傳入其他 descriptor path，亦不允許藉由一個已 proven 的 CRM graph 覆寫其他產品或 profile
    # 的設定。這些 local proof 均早於 Credential Manager 與 child process，因此失敗時零 CE I/O。
    if ($isFreshPreflightProbeMode) {
        # 唯讀 probe 不得繼承上一個 shell 的 execute/reconcile/repair 或 fresh ledger state。
        # 先清空兩個受限 namespace，再只設定本次 descriptor-derived scalar 與唯一 evidence path；
        # child 沒有 nonce、ledger、descriptor publication 或 mutation flag，因此無法退化為寫入 lane。
        foreach ($legacyEnvironmentName in $legacySliceCEnvironmentNames) {
            [Environment]::SetEnvironmentVariable($legacyEnvironmentName, $null, 'Process')
        }
        foreach ($freshEnvironmentName in $freshSliceCControlPlaneEnvironmentNames) {
            [Environment]::SetEnvironmentVariable($freshEnvironmentName, $null, 'Process')
        }

        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PREFLIGHT_PROBE', '1', 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_OWNER', $identity, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_ADD_LIST_ID', [string]$fixture.addListId, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_REMOVE_LIST_ID', [string]$fixture.removeListId, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_SMALL_GROUP_LIST_ID', [string]$fixture.smallGroupListId, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_EXISTING_TARGET_LEADER_ID', [string]$fixture.smallGroupTargetLeaderContactId, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_TRANSFER_SOURCE_LIST_ID', [string]$fixture.transferSourceListId, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_TRANSFER_TARGET_LIST_ID', [string]$fixture.transferTargetListId, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_TRANSFER_WEEK_START_UTC', [string]$fixture.transferWeekStartUtc, 'Process')
    }
    elseif ($isFreshProvisionMode -or $isFreshCleanupMode) {
        $freshControlPlaneRoots = Get-CurrentUserFreshFixtureControlPlaneRoots
        Assert-CurrentUserFreshFixtureDescriptorPath `
            -Path $SourceFixtureDescriptorPath `
            -DescriptorRoot $freshControlPlaneRoots.descriptorRoot `
            -ExpectedFileName 'contact-basic-info-fixture.json'
        Assert-CurrentUserFreshFixtureDescriptorPath `
            -Path $FixtureDescriptorPath `
            -DescriptorRoot $freshControlPlaneRoots.descriptorRoot `
            -ExpectedFileName 'list-management-fixture.json'
        $freshLedgerPath = Join-Path $freshControlPlaneRoots.ledgerRoot 'fresh-slice-c-ledger.json'

        if ($isFreshProvisionMode) {
            # pending ledger 表示上一次 create/associate/assign 可能已送達但尚未完成 graph proof；
            # 不可用新的 nonce 覆寫它，也不可用名稱搜尋或自動刪除做猜測式補償。
            if (Test-Path -LiteralPath $freshLedgerPath -PathType Leaf) {
                Write-HandoffResult (New-HandoffResult -Outcome 'no-go' -Reason 'fresh-fixture-ledger-pending' -PreflightOnly $false)
                $scriptExitCode = 2
                throw 'result-written'
            }
            $freshNonce = [Guid]::NewGuid().ToString('D')
            # publication 前的 target leader 是 cleanup 唯一可接受的 immutable baseline。它必須保留在 parent
            # invocation 內，並在 child 寫出 ledger 後做 exact match；不可信任已發佈 descriptor 或 child environment。
            $freshOriginalTargetLeaderContactId = [string]$fixture.smallGroupTargetLeaderContactId
        }
        else {
            # cleanup 只可開始於之前 fully proven 的 exact-ID graph；解析或 owner binding 失敗時
            # 不讀 credential、不啟 child，並保留 ledger 給後續唯讀 reconciliation。
            try {
                $freshLedgerBeforeCleanup = Get-StrictFreshFixtureLedger `
                    -Path $freshLedgerPath `
                    -OwnedRoot $freshControlPlaneRoots.ledgerRoot `
                    -CurrentIdentity $identity `
                    -ExpectedStage 'fresh-graph-proven'
            }
            catch {
                # 嚴格 ledger 缺失或不合法是預先條件拒絕，不是 runner 內部錯誤。這個分支發生在 Credential Manager 與子行程序之前，所以不可投影為沒有來源的泛用 error。
                if ([string]$_.Exception.Message -eq 'fresh-fixture-ledger-unavailable') {
                    Write-HandoffResult (New-HandoffResult `
                        -Outcome 'no-go' `
                        -Reason 'fresh-fixture-ledger-unavailable' `
                        -PreflightOnly $false `
                        -OperationExecuted $false)
                    $scriptExitCode = 2
                    throw 'result-written'
                }

                throw
            }
            $freshNonce = [string]$freshLedgerBeforeCleanup.nonce
        }
    }

    if (-not (Test-CredentialTargetPresent)) {
        Write-HandoffResult (New-HandoffResult -Outcome 'no-go' -Reason 'credential-unavailable' -PreflightOnly (-not $liveModeRequested))
        $scriptExitCode = 2
        throw 'result-written'
    }

    if (-not $liveModeRequested) {
        Write-HandoffResult (New-HandoffResult `
            -Outcome 'go' `
            -Reason '' `
            -PreflightOnly $true `
            -Checks @(
                'matrix-approved',
                'profile-crm91-present',
                'credential-target-present',
                'slice-a-contact-reused',
                'slice-c-fixture-owner-matches-operator',
                'feature-flags-remain-false'))
        $scriptExitCode = 0
        throw 'result-written'
    }

    $credentialPassword = Get-P72CredentialPassword
    if ([string]::IsNullOrWhiteSpace($credentialPassword)) {
        Write-HandoffResult (New-HandoffResult -Outcome 'no-go' -Reason 'credential-unavailable' -PreflightOnly $false)
        $scriptExitCode = 2
        throw 'result-written'
    }

    $dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        Write-HandoffResult (New-HandoffResult -Outcome 'error' -Reason 'dotnet-unavailable' -PreflightOnly $false)
        $scriptExitCode = 1
        throw 'result-written'
    }

    [Environment]::SetEnvironmentVariable('CRM_PASSWORD', $credentialPassword, 'Process')
    # 兩個 child lane 共用 fixture scalar，但 mode flag 與 evidence path 永遠互斥；先清掉另一
    # lane 的 process environment，避免從 parent 或前一次測試繼承後誤觸 mutation。
    if ($isFreshProvisionMode -or $isFreshCleanupMode) {
        # Fresh control plane 和既有 Slice C 互斥：子行程序只能看見本次、已驗證的 fresh
        # allowlist。不可以使用 parent 或前一次執行殘留的 contact、list、leader 或 evidence 參數，否則
        # 會在不同 fixture、profile 或 Windows session 間誤用 mutable CRM state。
        foreach ($legacyEnvironmentName in $legacySliceCEnvironmentNames) {
            [Environment]::SetEnvironmentVariable($legacyEnvironmentName, $null, 'Process')
        }

        # fresh lanes 不可繼承舊 Slice C execute/reconcile/repair 的 environment；parent 唯一會
        # 傳遞 deployment-owned descriptor scalars、current-user ledger path 與 current invocation
        # nonce。password 只存在此 child process 的 CRM_PASSWORD 環境變數，finally 必定還原。
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_LIVE', $null, 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_RECONCILE', $null, 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR', $null, 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR_PROBE', $null, 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PROVISION', $(if ($isFreshProvisionMode) { '1' } else { $null }), 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_FRESH_CLEANUP', $(if ($isFreshCleanupMode) { '1' } else { $null }), 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_LEDGER_ROOT', [string]$freshControlPlaneRoots.ledgerRoot, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_LEDGER_PATH', [string]$freshLedgerPath, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_DIAGNOSTIC_PATH', $null, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_DESCRIPTOR_CONFIRMATION', $(if ($isFreshProvisionMode) { 'replace-stale-descriptor' } else { 'cleanup-fresh-fixture' }), 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_NONCE', [string]$freshNonce, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_OWNER', $identity, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_ADD_LIST_ID', [string]$fixture.addListId, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_REMOVE_LIST_ID', [string]$fixture.removeListId, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_SMALL_GROUP_LIST_ID', [string]$fixture.smallGroupListId, 'Process')
        # Provision 會使用 publication 前 descriptor leader 作為 immutable baseline。Cleanup 只能从 strict
        # ledger 取回原始 baseline，不可繼承 fresh 或 legacy target-leader environment variable。
        [Environment]::SetEnvironmentVariable(
            'P7_2_SLICE_C_FRESH_EXISTING_TARGET_LEADER_ID',
            $(if ($isFreshProvisionMode) { [string]$fixture.smallGroupTargetLeaderContactId } else { $null }),
            'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_TRANSFER_SOURCE_LIST_ID', [string]$fixture.transferSourceListId, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_TRANSFER_TARGET_LIST_ID', [string]$fixture.transferTargetListId, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_TRANSFER_WEEK_START_UTC', [string]$fixture.transferWeekStartUtc, 'Process')
    }
    elseif ($isReconciliationMode) {
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_LIVE', $null, 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_RECONCILE', '1', 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR', $null, 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR_PROBE', $null, 'Process')
    }
    elseif ($isRepairMode) {
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_LIVE', $null, 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_RECONCILE', $null, 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR', '1', 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR_PROBE', $null, 'Process')
    }
    elseif ($isRepairProbeMode) {
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_LIVE', $null, 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_RECONCILE', $null, 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR', $null, 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR_PROBE', '1', 'Process')
    }
    else {
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_LIVE', '1', 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_RECONCILE', $null, 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR', $null, 'Process')
        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR_PROBE', $null, 'Process')
    }
    if (-not ($isFreshProvisionMode -or $isFreshCleanupMode -or $isFreshPreflightProbeMode)) {
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FIXTURE_OWNER', [string]$fixture.ownerIdentity, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FIXTURE_MARKER', [string]$fixture.marker, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_CONTACT_ID', [string]$sourceFixture.contactId, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_ADD_LIST_ID', [string]$fixture.addListId, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REMOVE_LIST_ID', [string]$fixture.removeListId, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_SMALL_GROUP_LIST_ID', [string]$fixture.smallGroupListId, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_SMALL_GROUP_TARGET_LEADER_CONTACT_ID', [string]$fixture.smallGroupTargetLeaderContactId, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_SMALL_GROUP_EXPECTED_RELATIONSHIP_LIST_ID', [string]$fixture.smallGroupExpectedRelationshipListId, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_TRANSFER_SOURCE_LIST_ID', [string]$fixture.transferSourceListId, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_TRANSFER_TARGET_LIST_ID', [string]$fixture.transferTargetListId, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_TRANSFER_WEEK_START_UTC', [string]$fixture.transferWeekStartUtc, 'Process')
    }

    $temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ('speechmessage-p7-2-slice-c-' + [Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($temporaryDirectory)
    $temporaryDirectoryCreated = $true
    $freshDiagnosticPath = $null
    if ($isFreshPreflightProbeMode) {
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_EVIDENCE_PATH', $null, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_RECONCILIATION_EVIDENCE_PATH', $null, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_EVIDENCE_PATH', $null, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_PROBE_EVIDENCE_PATH', $null, 'Process')
        $evidencePath = Join-Path $temporaryDirectory 'P72FreshSliceCFixturePreflightProbeEvidence.json'
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_PREFLIGHT_EVIDENCE_PATH', $evidencePath, 'Process')
        $testFilter = 'FullyQualifiedName=ChurchReport.MemberInfo.Tests.LivePackage02Data8ListManagementFreshPreflightProbeTests.Probe_fresh_package02_data8_list_management_preconditions_emits_sanitized_evidence'
    }
    elseif ($isFreshProvisionMode -or $isFreshCleanupMode) {
        # Fresh child 只接受 fresh evidence path；不可以寫入或讀取既有 Slice C 的 generic、reconcile、repair
        # evidence 名稱。將互斥的 path 全部清空後才指定固定檔名，避免不同 session 的 child 取到前一次
        # 執行殘留的 evidence 來決定是否發佈或 cleanup。
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_EVIDENCE_PATH', $null, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_RECONCILIATION_EVIDENCE_PATH', $null, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_EVIDENCE_PATH', $null, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_PROBE_EVIDENCE_PATH', $null, 'Process')
        $evidencePath = Join-Path $temporaryDirectory 'P72FreshSliceCFixtureEvidence.json'
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_EVIDENCE_PATH', $evidencePath, 'Process')
        if ($isFreshProvisionMode) {
            $freshDiagnosticPath = Join-Path $temporaryDirectory 'P72FreshSliceCFixtureDiagnostic.json'
            [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_DIAGNOSTIC_PATH', $freshDiagnosticPath, 'Process')
        }
        $testFilter = if ($isFreshProvisionMode) {
            'FullyQualifiedName=ChurchReport.MemberInfo.Tests.LivePackage02Data8ListManagementFreshFixtureTests.Provision_fresh_package02_data8_list_management_fixture_emits_sanitized_evidence'
        }
        else {
            'FullyQualifiedName=ChurchReport.MemberInfo.Tests.LivePackage02Data8ListManagementFreshFixtureCleanupTests.Cleanup_fresh_package02_data8_list_management_fixture_emits_sanitized_evidence'
        }
    }
    elseif ($isReconciliationMode) {
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_EVIDENCE_PATH', $null, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_EVIDENCE_PATH', $null, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_PROBE_EVIDENCE_PATH', $null, 'Process')
        $evidencePath = Join-Path $temporaryDirectory 'P72Data8ListManagementReconciliationEvidence.json'
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_RECONCILIATION_EVIDENCE_PATH', $evidencePath, 'Process')
        $testFilter = 'FullyQualifiedName=ChurchReport.MemberInfo.Tests.LivePackage02Data8ListManagementEvidenceTests.Reconcile_package02_data8_list_management_emits_sanitized_reconciliation'
    }
    elseif ($isRepairMode) {
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_EVIDENCE_PATH', $null, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_RECONCILIATION_EVIDENCE_PATH', $null, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_PROBE_EVIDENCE_PATH', $null, 'Process')
        $evidencePath = Join-Path $temporaryDirectory 'P72Data8ListManagementRepairEvidence.json'
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_EVIDENCE_PATH', $evidencePath, 'Process')
        $testFilter = 'FullyQualifiedName=ChurchReport.MemberInfo.Tests.LivePackage02Data8ListManagementEvidenceTests.Repair_package02_data8_relationship_fixture_emits_sanitized_evidence'
    }
    elseif ($isRepairProbeMode) {
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_EVIDENCE_PATH', $null, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_RECONCILIATION_EVIDENCE_PATH', $null, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_EVIDENCE_PATH', $null, 'Process')
        $evidencePath = Join-Path $temporaryDirectory 'P72Data8ListManagementRepairProbeEvidence.json'
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_PROBE_EVIDENCE_PATH', $evidencePath, 'Process')
        $testFilter = 'FullyQualifiedName=ChurchReport.MemberInfo.Tests.LivePackage02Data8ListManagementEvidenceTests.Probe_package02_data8_relationship_fixture_emits_sanitized_evidence'
    }
    else {
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_RECONCILIATION_EVIDENCE_PATH', $null, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_EVIDENCE_PATH', $null, 'Process')
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_PROBE_EVIDENCE_PATH', $null, 'Process')
        $evidencePath = Join-Path $temporaryDirectory 'P72Data8ListManagementEvidence.json'
        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_EVIDENCE_PATH', $evidencePath, 'Process')
        $testFilter = 'FullyQualifiedName=ChurchReport.MemberInfo.Tests.LivePackage02Data8ListManagementEvidenceTests.Live_package02_data8_list_management_emits_sanitized_evidence'
    }
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $dotnetCommand.Source
    $startInfo.Arguments = 'test ' + (Quote-Argument $testProjectPath) +
        ' --no-restore --filter ' +
        (Quote-Argument $testFilter) +
        ' --blame-hang-timeout 150s --verbosity quiet'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw 'dotnet-start-failed'
    }

    $childProcessStarted = $true
    $standardOutputTask = $process.StandardOutput.ReadToEndAsync()
    $standardErrorTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(180000)) {
        # timeout 後只終止同一 child tree，不重試或重新送出可能已 commit 的 operation。
        try {
            & taskkill.exe /PID $process.Id /T /F *> $null
        }
        catch {
            try { $process.Kill() } catch { }
        }

        $timeoutOperations = @()
        if ($operationMayHaveExecuted) {
            $timeoutOperations = New-NotStartedOperations
        }
        Complete-HandoffResult (New-HandoffResult `
            -Outcome 'no-go' `
            -Reason 'test-timeout' `
            -PreflightOnly $false `
            -OperationExecuted $operationMayHaveExecuted `
            -Operations $timeoutOperations)
        $scriptExitCode = 2
        throw 'result-written'
    }

    $process.WaitForExit()
    [void]$standardOutputTask.GetAwaiter().GetResult()
    [void]$standardErrorTask.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) {
        # child 的 evidence 檔不是成功證明：非零結束可能代表 operation、runtime 或 cleanup
        # 在寫檔後仍失敗。先 drain 兩個 stream 才讀取穩定 ExitCode，接著完全拒絕 child
        # 留下的內容，避免格式正確但不可信的 JSON 越過 process lifecycle 邊界。Execute lane
        # 保守宣告可能已進入 operation 範圍，並以五筆 not-started projection 禁止任何重試；
        # reconciliation lane 則維持零 mutation 的空 operation 集合。
        $childFailureOperations = @()
        if ($operationMayHaveExecuted) {
            $childFailureOperations = New-NotStartedOperations
        }
        $diagnosticCategory = $null
        if ($isFreshProvisionMode) {
            $diagnosticCategory = Get-StrictFreshFixtureChildFailureDiagnosticCategory `
                -DiagnosticPath $freshDiagnosticPath `
                -OwnedRoot $temporaryDirectory
        }
        Complete-HandoffResult (New-HandoffResult `
            -Outcome 'no-go' `
            -Reason 'child-process-failed' `
            -PreflightOnly $false `
            -OperationExecuted $operationMayHaveExecuted `
            -Operations $childFailureOperations `
            -DiagnosticCategory $diagnosticCategory)
        $scriptExitCode = 2
        throw 'result-written'
    }
    if ($isFreshPreflightProbeMode) {
        $strictEvidence = Get-StrictFreshPreflightProbeEvidenceFile $evidencePath
        Complete-HandoffResult (New-HandoffResult `
            -Outcome $strictEvidence.outcome `
            -Reason $strictEvidence.reason `
            -PreflightOnly $false `
            -OperationExecuted $false `
            -ReadOnlyProbeExecuted $strictEvidence.readOnlyProbeExecuted `
            -ProbeStage 'fresh-fixture-provision-preconditions' `
            -Probe $strictEvidence.probe)
    }
    elseif ($isReconciliationMode) {
        $strictEvidence = Get-StrictSliceCReconciliationEvidenceFile $evidencePath
        Complete-HandoffResult (New-HandoffResult `
            -Outcome $strictEvidence.outcome `
            -Reason $strictEvidence.reason `
            -PreflightOnly $false `
            -OperationExecuted $false `
            -ReadOnlyProbeExecuted $strictEvidence.readOnlyProbeExecuted `
            -OwnerBinding $strictEvidence.ownerBinding `
            -ProbeStage $strictEvidence.probeStage `
            -States $strictEvidence.states)
    }
    elseif ($isRepairMode) {
        $strictEvidence = Get-StrictSliceCRepairEvidenceFile $evidencePath
        Complete-HandoffResult (New-HandoffResult `
            -Outcome $strictEvidence.outcome `
            -Reason $strictEvidence.reason `
            -PreflightOnly $false `
            -OperationExecuted $strictEvidence.operationExecuted `
            -ReadBackConfirmed $strictEvidence.readBackConfirmed)
    }
    elseif ($isRepairProbeMode) {
        $strictEvidence = Get-StrictSliceCRepairProbeEvidenceFile $evidencePath
        Complete-HandoffResult (New-HandoffResult `
            -Outcome $strictEvidence.outcome `
            -Reason $strictEvidence.reason `
            -PreflightOnly $false `
            -OperationExecuted $false `
            -ReadOnlyProbeExecuted $strictEvidence.readOnlyProbeExecuted `
            -ProbeStage 'relationship-list-repair-preconditions' `
            -Probe $strictEvidence.probe)
    }
    elseif ($isFreshProvisionMode -or $isFreshCleanupMode) {
        # Fresh lane 的 child schema 和舊 Slice C evidence 完全不同。一定要先以 fresh allowlist 解析它，再決定
        # 是否發佈 descriptor 或移除 recovery state。不允許 generic parser 把另一條 lane 的欄位當成可信任的操作證據。
        $freshExpectedLane = if ($isFreshProvisionMode) { 'provision' } else { 'cleanup' }
        try {
            $strictEvidence = Get-StrictFreshFixtureEvidenceFile `
                -EvidencePath $evidencePath `
                -ExpectedLane $freshExpectedLane

            if ($strictEvidence.outcome -eq 'go') {
                if ($isFreshProvisionMode) {
                    $freshLedgerAfterProvision = Get-StrictFreshFixtureLedger `
                        -Path $freshLedgerPath `
                        -OwnedRoot $freshControlPlaneRoots.ledgerRoot `
                        -CurrentIdentity $identity `
                        -ExpectedStage 'fresh-graph-proven'
                    if ($freshLedgerAfterProvision.originalTargetLeaderContactId -cne $freshOriginalTargetLeaderContactId) {
                        throw 'fresh-fixture-ledger-unavailable'
                    }

                    Publish-FreshFixtureDescriptorPair `
                        -SourcePath $SourceFixtureDescriptorPath `
                        -FixturePath $FixtureDescriptorPath `
                        -DescriptorRoot $freshControlPlaneRoots.descriptorRoot `
                        -SourceFixture $sourceFixture `
                        -Fixture $fixture `
                        -Ledger $freshLedgerAfterProvision `
                        -CurrentIdentity $identity
                }
                else {
                    $freshLedgerAfterCleanup = Get-StrictFreshFixtureLedger `
                        -Path $freshLedgerPath `
                        -OwnedRoot $freshControlPlaneRoots.ledgerRoot `
                        -CurrentIdentity $identity `
                        -ExpectedStage 'cleanup-leader-contact-deleted'
                    if ($freshLedgerAfterCleanup.originalTargetLeaderContactId -cne $freshLedgerBeforeCleanup.originalTargetLeaderContactId) {
                        throw 'fresh-fixture-ledger-cleanup-failed'
                    }

                    Remove-FreshFixtureDescriptorPair `
                        -SourcePath $SourceFixtureDescriptorPath `
                        -FixturePath $FixtureDescriptorPath `
                        -DescriptorRoot $freshControlPlaneRoots.descriptorRoot `
                        -Ledger $freshLedgerAfterCleanup `
                        -CurrentIdentity $identity
                    Remove-StrictFreshFixtureLedger `
                        -Path $freshLedgerPath `
                        -OwnedRoot $freshControlPlaneRoots.ledgerRoot `
                        -CurrentIdentity $identity `
                        -ExpectedLedger $freshLedgerAfterCleanup
                }
            }

            Complete-HandoffResult (New-HandoffResult `
                -Outcome $strictEvidence.outcome `
                -Reason $strictEvidence.reason `
                -PreflightOnly $false `
                -OperationExecuted $strictEvidence.operationExecuted)
        }
        catch {
            # Child 已結束且可能已發出 Create/Associate/Assign/Delete。因此 schema、ledger、publication、cleanup 任何
            # 不確定都要是 non-retryable no-go，保留 strict ledger 供以後 exact-ID reconciliation，而不可默認成未執行。
            $freshFailureReason = [string]$_.Exception.Message
            if ($freshFailureReason -cnotin @(
                    'fresh-fixture-evidence-unavailable',
                    'fresh-fixture-ledger-unavailable',
                    'fresh-fixture-ledger-cleanup-failed',
                    'fresh-descriptor-publication-failed',
                    'fresh-descriptor-cleanup-failed')) {
                $freshFailureReason = 'fresh-fixture-evidence-unavailable'
            }

            Complete-HandoffResult (New-HandoffResult `
                -Outcome 'no-go' `
                -Reason $freshFailureReason `
                -PreflightOnly $false `
                -OperationExecuted $operationMayHaveExecuted)
            $scriptExitCode = 2
            throw 'result-written'
        }
    }
    else {
        $strictEvidence = Get-StrictSliceCEvidenceFile $evidencePath
        Complete-HandoffResult (New-HandoffResult `
            -Outcome $strictEvidence.outcome `
            -Reason $strictEvidence.reason `
            -PreflightOnly $false `
            -OperationExecuted $strictEvidence.operationExecuted `
            -Operations $strictEvidence.operations)
    }
    $scriptExitCode = if ($completedHandoffOutcome -ceq 'go') { 0 } else { 2 }
}
catch {
    if (-not $resultAlreadyWritten) {
        if ($childProcessStarted) {
            $reason = if ([string]$_.Exception.Message -eq 'evidence-result-unavailable') { 'evidence-result-unavailable' } else { 'handoff-failed' }
            $childFailureOperations = @()
            if ($operationMayHaveExecuted) {
                $childFailureOperations = New-NotStartedOperations
            }
            Complete-HandoffResult (New-HandoffResult `
                -Outcome 'no-go' `
                -Reason $reason `
                -PreflightOnly $false `
                -OperationExecuted $operationMayHaveExecuted `
                -Operations $childFailureOperations)
            $scriptExitCode = 2
        }
        else {
            $reason = if ([string]$_.Exception.Message -eq 'dotnet-start-failed') { 'dotnet-start-failed' } else { 'preflight-failed' }
            Complete-HandoffResult (New-HandoffResult -Outcome 'error' -Reason $reason -PreflightOnly (-not $liveModeRequested))
            $scriptExitCode = if ($completedHandoffOutcome -ceq 'no-go') { 2 } else { 1 }
        }
    }
}
finally {
    if ($null -ne $process) {
        try {
            if (-not $process.HasExited) {
                & taskkill.exe /PID $process.Id /T /F *> $null
            }
        }
        catch {
            try { $process.Kill() } catch { }
        }
        finally {
            $process.Dispose()
        }
    }

    foreach ($name in $inputEnvironmentNames) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
    }

    if ($temporaryDirectoryCreated) {
        # Complete-HandoffResult 已在任何 JSON 輸出前處理第一次 cleanup 並於失敗時回報 No-Go。
        # 這裡只保留第二次 best-effort，避免錯誤後留下 evidence；無論結果如何都不得改寫已輸出的
        # sanitized verdict，也不得輸出 temporary path 或原始刪除例外。
        if (Remove-OwnedSliceCTemporaryDirectory $temporaryDirectory) {
            $temporaryDirectory = $null
            $temporaryDirectoryCreated = $false
        }
    }

    $credentialPassword = $null
}

exit $scriptExitCode

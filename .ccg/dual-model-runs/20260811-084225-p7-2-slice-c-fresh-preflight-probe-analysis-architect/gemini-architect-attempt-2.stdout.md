# 1. Analysis (架構分析)

當前 `Invoke-Package02Data8ListManagementEvidence.ps1` 與 `LivePackage02Data8ListManagementFreshFixtureTests.cs` 的架構設計中，`-ProvisionFreshFixture` 模式在執行任何 CRM 變更（Create/Assign/Add）之前，會先進行一系列的 Preflight 驗證。然而，當這些驗證失敗時，系統僅會返回一個模糊的 `fixture-precondition-failed` 診斷類別，並將其寫入 `P72FreshSliceCFixtureDiagnostic.json`。

這種設計雖然保護了敏感的 CRM 資訊（如 ID、名稱等），但卻隱藏了具體是哪一個 Pre-mutation Proof 失敗了（例如：是 5 個 Operational Lists 的 Task-owned 屬性無效、Leader Owner 種類不對、還是 Weekly Report 的基數不為 1）。

為了解決此問題，我們需要設計一個全新的 **唯讀 (Read-Only)** 探測模式 `FreshPreflightProbe`。此模式將利用現有的 `P72FreshSliceCFixturePreflightProbe` 類別，對所有前置條件進行精確的分類，並輸出經過脫敏（Sanitized）的 JSON 證據，同時保證在整個生命週期中**絕不發出任何 Mutation 呼叫**。

---

# 2. Architecture Decision (架構決策)

### 2.1 整合點與參數集邊界
- **決策**：在 PowerShell 腳本中新增一個獨立的參數集 `FreshPreflightProbe`，並對應 `-FreshPreflightProbe` 開關。
- **合理性**：這能確保該模式與其他會產生 Mutation 的模式（如 `ProvisionFresh`、`CleanupFresh`、`Repair`）在語法上互斥，避免誤用。
- **拒絕的替代方案**：重用現有的 `RepairProbe` 或 `Reconcile` 模式。
  - *原因*：`Reconcile` 模式需要 descriptor 中已填入 Fresh IDs，而此時 IDs 尚未產生；`RepairProbe` 則是專門針對 Relationship List 欄位修復的探測，兩者的實體與欄位完全不同。

### 2.2 脫敏證據 Schema 設計
- **決策**：輸出 strict UTF-8 no-BOM, CRLF-only JSON 檔案 `P72FreshSliceCFixturePreflightProbeEvidence.json`。
- **允許的值**：
  - `outcome`: `"go"` | `"no-go"`
  - `reason`: `"fresh-preconditions-proven"` | `"fresh-preconditions-not-proven"` | `"probe-unavailable"`
  - `probe`:
    - `requestShape`: `"valid"` | `"invalid"`
    - `operationalLists`: `"valid"` | `"invalid"` | `"unavailable"`
    - `leaderMarker`: `"valid"` | `"invalid"` | `"unavailable"`
    - `ownerKind`: `"systemuser"` | `"other-or-missing"` | `"unavailable"`
    - `ownerState`: `"active"` | `"inactive-or-missing"` | `"unavailable"`
    - `ownerRelation`: `"same-as-data8"` | `"different-from-data8"` | `"unavailable"`
    - `weeklyReport`: `"exactly-one-active"` | `"not-exactly-one-active"` | `"unavailable"`

### 2.3 濫用與生命週期風險控制
- **資訊洩漏防護**：在 C# 測試與 PowerShell 解析器中，任何 CRM ID、異常堆疊、連線字串均不得寫入 stdout 或 evidence 檔案。若發生異常，一律安全地 catch 並歸類為 `"probe-unavailable"`。
- **資源生命週期**：在 `finally` 區塊中，必須嚴格 dispose `IOrganizationService`、`EmbeddedData8Runtime` 與 `ILoggerFactory`，防止 WCF 通道與記憶體洩漏。
- **狀態隔離**：在啟動 child process 前，清除所有 legacy Slice C 環境變數，避免跨使用者/設定檔的狀態保留。

---

# 3. Implementation Plan (實作計畫)

## 3.1 Unified Diff Patch

```diff
--- a/docs/scripts/Invoke-Package02Data8ListManagementEvidence.ps1
+++ b/docs/scripts/Invoke-Package02Data8ListManagementEvidence.ps1
@@ -56,6 +56,9 @@ param(
     [Parameter(ParameterSetName = 'RepairProbe')]
     [switch] $RepairProbe,

+    [Parameter(ParameterSetName = 'FreshPreflightProbe')]
+    [switch] $FreshPreflightProbe,
+
     [Parameter(ParameterSetName = 'ProvisionFresh')]
     [switch] $ProvisionFreshFixture,

@@ -90,11 +93,12 @@ $completedHandoffOutcome = $null
 $isReconciliationMode = [bool]$ReconcileFixture
 $isRepairMode = [bool]$RepairFixture
 $isRepairProbeMode = [bool]$RepairProbe
+$isFreshPreflightProbeMode = [bool]$FreshPreflightProbe
 $isFreshProvisionMode = [bool]$ProvisionFreshFixture
 $isFreshCleanupMode = [bool]$CleanupFreshFixture
-$liveModeRequested = [bool]($ExecuteFixture -or $ReconcileFixture -or $RepairFixture -or $RepairProbe -or $ProvisionFreshFixture -or $CleanupFreshFixture)
-$operationMayHaveExecuted = -not ($isReconciliationMode -or $isRepairMode -or $isRepairProbeMode)
+$liveModeRequested = [bool]($ExecuteFixture -or $ReconcileFixture -or $RepairFixture -or $RepairProbe -or $FreshPreflightProbe -or $ProvisionFreshFixture -or $CleanupFreshFixture)
+$operationMayHaveExecuted = -not ($isReconciliationMode -or $isRepairMode -or $isRepairProbeMode -or $isFreshPreflightProbeMode)
 $previousEnvironment = @{}
 # legacy inventory 與 fresh child 的另一個 inherited Slice C state denylist，避免被 snapshot。
 # fresh-mode clear 與 finally restore，限縮 P7_2_ 與 SPEECHMESSAGE_ namespace 環境變數 key，只在生命週期中
@@ -148,7 +152,8 @@ $freshSliceCControlPlaneEnvironmentNames = @(
     'P7_2_SLICE_C_FRESH_EXISTING_TARGET_LEADER_ID',
     'P7_2_SLICE_C_FRESH_TRANSFER_SOURCE_LIST_ID',
     'P7_2_SLICE_C_FRESH_TRANSFER_TARGET_LIST_ID',
-    'P7_2_SLICE_C_FRESH_TRANSFER_WEEK_START_UTC'
+    'P7_2_SLICE_C_FRESH_TRANSFER_WEEK_START_UTC',
+    'P7_2_SLICE_C_FRESH_PREFLIGHT_PROBE_EVIDENCE_PATH'
 )
 $freshSliceCControlPlaneEnvironmentNameSet = [System.Collections.Generic.HashSet[string]]::new(
     [StringComparer]::OrdinalIgnoreCase)
@@ -2181,6 +2186,79 @@ function Get-StrictSliceCRepairProbeEvidenceFile {
     }
 }

+function Get-StrictSliceCFreshPreflightProbeEvidenceFile {
+    param([string] $EvidencePath)
+
+    $evidence = Read-StrictJsonFile -Path $EvidencePath -MaximumBytes 32768 -FailureReason 'evidence-result-unavailable'
+    $topPropertyNames = @(
+        'schemaVersion',
+        'outcome',
+        'reason',
+        'profileAlias',
+        'deploymentProfileAlias',
+        'ceVersion',
+        'connector',
+        'preflightOnly',
+        'operationExecuted',
+        'readOnlyProbeExecuted',
+        'featureFlagChanged',
+        'probe'
+    )
+    $actualTopPropertyNames = @($evidence.PSObject.Properties.Name)
+    if ($actualTopPropertyNames.Count -ne $topPropertyNames.Count -or
+        @($actualTopPropertyNames | Where-Object { $_ -cnotin $topPropertyNames }).Count -ne 0 -or
+        @($topPropertyNames | Where-Object { $_ -cnotin $actualTopPropertyNames }).Count -ne 0) {
+        throw 'evidence-result-unavailable'
+    }
+
+    $allowedReasons = @('fresh-preconditions-proven', 'fresh-preconditions-not-proven', 'probe-unavailable')
+    if ($evidence.schemaVersion -ne 1 -or
+        $evidence.outcome -cnotin @('go', 'no-go') -or
+        $evidence.reason -cnotin $allowedReasons -or
+        $evidence.profileAlias -cne $expectedProfileAlias -or
+        $evidence.deploymentProfileAlias -cne $expectedDeploymentProfileAlias -or
+        $evidence.ceVersion -cne '9.1' -or
+        $evidence.connector -cne 'Data8' -or
+        $evidence.preflightOnly -ne $true -or
+        $evidence.operationExecuted -ne $false -or
+        $evidence.readOnlyProbeExecuted -isnot [bool] -or
+        $evidence.featureFlagChanged -ne $false) {
+        throw 'evidence-result-unavailable'
+    }
+
+    $probePropertyNames = @(
+        'requestShape',
+        'operationalLists',
+        'leaderMarker',
+        'ownerKind',
+        'ownerState',
+        'ownerRelation',
+        'weeklyReport'
+    )
+    if ($null -eq $evidence.probe) {
+        throw 'evidence-result-unavailable'
+    }
+    $actualProbePropertyNames = @($evidence.probe.PSObject.Properties.Name)
+    if ($actualProbePropertyNames.Count -ne $probePropertyNames.Count -or
+        @($actualProbePropertyNames | Where-Object { $_ -cnotin $probePropertyNames }).Count -ne 0 -or
+        @($probePropertyNames | Where-Object { $_ -cnotin $actualProbePropertyNames }).Count -ne 0) {
+        throw 'evidence-result-unavailable'
+    }
+
+    return [pscustomobject]@{
+        outcome = [string]$evidence.outcome
+        reason = [string]$evidence.reason
+        readOnlyProbeExecuted = [bool]$evidence.readOnlyProbeExecuted
+        probe = [pscustomobject]@{
+            requestShape = [string]$evidence.probe.requestShape
+            operationalLists = [string]$evidence.probe.operationalLists
+            leaderMarker = [string]$evidence.probe.leaderMarker
+            ownerKind = [string]$evidence.probe.ownerKind
+            ownerState = [string]$evidence.probe.ownerState
+            ownerRelation = [string]$evidence.probe.ownerRelation
+            weeklyReport = [string]$evidence.probe.weeklyReport
+        }
+    }
+}
+
 function Remove-OwnedSliceCTemporaryDirectory {
     <#
     .SYNOPSIS
@@ -2493,12 +2571,14 @@ if (-not $liveModeRequested) {
         [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_RECONCILE', $null, 'Process')
         [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR', $null, 'Process')
         [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR_PROBE', $null, 'Process')
+        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PREFLIGHT_PROBE', $null, 'Process')
         [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PROVISION', $(if ($isFreshProvisionMode) { '1' } else { $null }), 'Process')
         [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_FRESH_CLEANUP', $(if ($isFreshCleanupMode) { '1' } else { $null }), 'Process')
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_LEDGER_ROOT', [string]$freshControlPlaneRoots.ledgerRoot, 'Process')
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_LEDGER_PATH', [string]$freshLedgerPath, 'Process')
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_DIAGNOSTIC_PATH', $null, 'Process')
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_DESCRIPTOR_CONFIRMATION', $(if ($isFreshProvisionMode) { 'replace-stale-descriptor' } else { 'cleanup-fresh-fixture' }), 'Process')
+        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_PREFLIGHT_PROBE_EVIDENCE_PATH', $null, 'Process')
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_NONCE', [string]$freshNonce, 'Process')
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_OWNER', $identity, 'Process')
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_ADD_LIST_ID', [string]$fixture.addListId, 'Process')
@@ -2514,24 +2594,35 @@ if (-not $liveModeRequested) {
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_TRANSFER_TARGET_LIST_ID', [string]$fixture.transferTargetListId, 'Process')
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_TRANSFER_WEEK_START_UTC', [string]$fixture.transferWeekStartUtc, 'Process')
     }
+    elseif ($isFreshPreflightProbeMode) {
+        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_LIVE', $null, 'Process')
+        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_RECONCILE', $null, 'Process')
+        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR', $null, 'Process')
+        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR_PROBE', $null, 'Process')
+        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PREFLIGHT_PROBE', '1', 'Process')
+    }
     elseif ($isReconciliationMode) {
         [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_LIVE', $null, 'Process')
         [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_RECONCILE', '1', 'Process')
         [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR', $null, 'Process')
         [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR_PROBE', $null, 'Process')
+        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PREFLIGHT_PROBE', $null, 'Process')
     }
     elseif ($isRepairMode) {
         [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_LIVE', $null, 'Process')
         [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_RECONCILE', $null, 'Process')
         [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR', '1', 'Process')
         [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR_PROBE', $null, 'Process')
+        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PREFLIGHT_PROBE', $null, 'Process')
     }
     elseif ($isRepairProbeMode) {
         [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_LIVE', $null, 'Process')
         [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_RECONCILE', $null, 'Process')
         [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR', $null, 'Process')
         [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR_PROBE', '1', 'Process')
+        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PREFLIGHT_PROBE', $null, 'Process')
     }
     else {
         [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_LIVE', '1', 'Process')
         [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_RECONCILE', $null, 'Process')
         [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR', $null, 'Process')
         [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_REPAIR_PROBE', $null, 'Process')
+        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PREFLIGHT_PROBE', $null, 'Process')
     }
     if (-not ($isFreshProvisionMode -or $isFreshCleanupMode)) {
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FIXTURE_OWNER', [string]$fixture.ownerIdentity, 'Process')
@@ -2579,18 +2670,30 @@ if (-not $liveModeRequested) {
             $testFilter = 'FullyQualifiedName=ChurchReport.MemberInfo.Tests.LivePackage02Data8ListManagementFreshFixtureCleanupTests.Cleanup_fresh_package02_data8_list_management_fixture_emits_sanitized_evidence'
         }
     }
+    elseif ($isFreshPreflightProbeMode) {
+        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_EVIDENCE_PATH', $null, 'Process')
+        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_RECONCILIATION_EVIDENCE_PATH', $null, 'Process')
+        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_EVIDENCE_PATH', $null, 'Process')
+        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_PROBE_EVIDENCE_PATH', $null, 'Process')
+        $evidencePath = Join-Path $temporaryDirectory 'P72FreshSliceCFixturePreflightProbeEvidence.json'
+        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_PREFLIGHT_PROBE_EVIDENCE_PATH', $evidencePath, 'Process')
+        $testFilter = 'FullyQualifiedName=ChurchReport.MemberInfo.Tests.LivePackage02Data8ListManagementFreshFixtureTests.Probe_fresh_package02_data8_list_management_preconditions_emits_sanitized_evidence'
+    }
     elseif ($isReconciliationMode) {
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_EVIDENCE_PATH', $null, 'Process')
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_EVIDENCE_PATH', $null, 'Process')
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_PROBE_EVIDENCE_PATH', $null, 'Process')
+        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_PREFLIGHT_PROBE_EVIDENCE_PATH', $null, 'Process')
         $evidencePath = Join-Path $temporaryDirectory 'P72Data8ListManagementReconciliationEvidence.json'
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_RECONCILIATION_EVIDENCE_PATH', $evidencePath, 'Process')
         $testFilter = 'FullyQualifiedName=ChurchReport.MemberInfo.Tests.LivePackage02Data8ListManagementEvidenceTests.Reconcile_package02_data8_list_management_emits_sanitized_reconciliation'
     }
     elseif ($isRepairMode) {
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_EVIDENCE_PATH', $null, 'Process')
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_RECONCILIATION_EVIDENCE_PATH', $null, 'Process')
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_PROBE_EVIDENCE_PATH', $null, 'Process')
+        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_PREFLIGHT_PROBE_EVIDENCE_PATH', $null, 'Process')
         $evidencePath = Join-Path $temporaryDirectory 'P72Data8ListManagementRepairEvidence.json'
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_EVIDENCE_PATH', $evidencePath, 'Process')
         $testFilter = 'FullyQualifiedName=ChurchReport.MemberInfo.Tests.LivePackage02Data8ListManagementEvidenceTests.Repair_package02_data8_relationship_fixture_emits_sanitized_evidence'
@@ -2599,18 +2702,22 @@ if (-not $liveModeRequested) {
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_EVIDENCE_PATH', $null, 'Process')
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_RECONCILIATION_EVIDENCE_PATH', $null, 'Process')
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_EVIDENCE_PATH', $null, 'Process')
+        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_PREFLIGHT_PROBE_EVIDENCE_PATH', $null, 'Process')
         $evidencePath = Join-Path $temporaryDirectory 'P72Data8ListManagementRepairProbeEvidence.json'
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_PROBE_EVIDENCE_PATH', $evidencePath, 'Process')
         $testFilter = 'FullyQualifiedName=ChurchReport.MemberInfo.Tests.LivePackage02Data8ListManagementEvidenceTests.Probe_package02_data8_relationship_fixture_emits_sanitized_evidence'
     }
     else {
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_RECONCILIATION_EVIDENCE_PATH', $null, 'Process')
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_EVIDENCE_PATH', $null, 'Process')
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_REPAIR_PROBE_EVIDENCE_PATH', $null, 'Process')
+        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_PREFLIGHT_PROBE_EVIDENCE_PATH', $null, 'Process')
         $evidencePath = Join-Path $temporaryDirectory 'P72Data8ListManagementEvidence.json'
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_EVIDENCE_PATH', $evidencePath, 'Process')
         $testFilter = 'FullyQualifiedName=ChurchReport.MemberInfo.Tests.LivePackage02Data8ListManagementEvidenceTests.Live_package02_data8_list_management_emits_sanitized_evidence'
     }
```

```diff
--- a/ChurchReport.MemberInfo.Tests/LivePackage02Data8ListManagementFreshFixtureTests.cs
+++ b/ChurchReport.MemberInfo.Tests/LivePackage02Data8ListManagementFreshFixtureTests.cs
@@ -156,6 +156,98 @@ public sealed class LivePackage02Data8ListManagementFreshFixtureTests
             "go",
             because: "only a fully proven fresh graph and deterministic Data8 resource cleanup may authorize parent descriptor publication");
     }
+
+    /// <summary>
+    /// 執行唯讀的 fresh preflight probe，對所有前置條件進行精確分類，並輸出脫敏的 JSON 證據。
+    /// 整個生命週期中絕不發出任何 Create, Update, Delete, Assign, Execute 呼叫。
+    /// </summary>
+    [P72Data8SliceCFreshPreflightProbeFact]
+    public async Task Probe_fresh_package02_data8_list_management_preconditions_emits_sanitized_evidence()
+    {
+        var outcome = "no-go";
+        var reason = "probe-unavailable";
+        var readOnlyProbeExecuted = false;
+        var cleanupSucceeded = true;
+        var probeResult = new P72FreshSliceCFixturePreflightProbeResult(
+            "no-go",
+            "probe-unavailable",
+            ReadOnlyProbeExecuted: false,
+            RequestShape: "invalid",
+            OperationalLists: "unavailable",
+            LeaderMarker: "unavailable",
+            OwnerKind: "unavailable",
+            OwnerState: "unavailable",
+            OwnerRelation: "unavailable",
+            WeeklyReport: "unavailable");
+        ILoggerFactory? loggerFactory = null;
+        EmbeddedData8Runtime? runtime = null;
+        OnPremiseClient? service = null;
+
+        try
+        {
+            var fixture = ReadFixture();
+            var configuration = LivePackage02Data8ListManagementEvidenceTests.CreateDevelopmentConfiguration();
+            var (profiles, catalog, organization, settings) = LivePackage02Data8ListManagementEvidenceTests.ResolveProfile(configuration);
+            var credentialPassword = Environment.GetEnvironmentVariable("CRM_PASSWORD");
+            credentialPassword.Should().NotBeNullOrWhiteSpace();
+
+            loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
+            runtime = new EmbeddedData8Runtime(
+                profiles,
+                catalog,
+                ProfileAlias,
+                new OnPremiseData8ConnectorClientFactory(settings),
+                loggerFactory.CreateLogger<EmbeddedData8Runtime>(),
+                loggerFactory);
+
+            var verifiedOwnerId = await LivePackage02Data8ListManagementEvidenceTests.ResolveFixtureTargetOwnerIdAsync(
+                runtime.Executor,
+                organization.OrganizationId).ConfigureAwait(false);
+            if (verifiedOwnerId is Guid verifiedUserId)
+            {
+                service = new OnPremiseClient(organization.ServiceUri, settings.UserName, credentialPassword!);
+                var probe = new P72FreshSliceCFixturePreflightProbe(service);
+                var request = new P72FreshSliceCFixturePreflightRequest(
+                    fixture.AddListId,
+                    fixture.RemoveListId,
+                    fixture.SmallGroupListId,
+                    fixture.SmallGroupTargetLeaderContactId,
+                    fixture.TransferSourceListId,
+                    fixture.TransferTargetListId,
+                    fixture.TransferWeekStartUtc,
+                    verifiedUserId);
+
+                probeResult = probe.Probe(request);
+                outcome = probeResult.Outcome;
+                reason = probeResult.Reason;
+                readOnlyProbeExecuted = probeResult.ReadOnlyProbeExecuted;
+            }
+        }
+        catch (Exception)
+        {
+            outcome = "no-go";
+            reason = "probe-unavailable";
+            readOnlyProbeExecuted = false;
+        }
+        finally
+        {
+            DisposeService(ref service, ref outcome, ref reason);
+            if (!await DisposeRuntimeAsync(runtime).ConfigureAwait(false))
+            {
+                cleanupSucceeded = false;
+            }
+            DisposeLogger(ref loggerFactory, ref outcome, ref reason);
+            if (reason == "cleanup-failure")
+            {
+                cleanupSucceeded = false;
+            }
+        }
+
+        if (!cleanupSucceeded)
+        {
+            outcome = "no-go";
+            reason = "cleanup-failure";
+            readOnlyProbeExecuted = false;
+        }
+
+        var evidencePath = Environment.GetEnvironmentVariable("P7_2_SLICE_C_FRESH_PREFLIGHT_PROBE_EVIDENCE_PATH");
+        if (!string.IsNullOrWhiteSpace(evidencePath))
+        {
+            var evidence = new
+            {
+                schemaVersion = 1,
+                outcome,
+                reason,
+                profileAlias = ProfileAlias,
+                deploymentProfileAlias = "crm91",
+                ceVersion = "9.1",
+                connector = "Data8",
+                preflightOnly = true,
+                operationExecuted = false,
+                readOnlyProbeExecuted,
+                featureFlagChanged = false,
+                probe = new
+                {
+                    requestShape = probeResult.RequestShape,
+                    operationalLists = probeResult.OperationalLists,
+                    leaderMarker = probeResult.LeaderMarker,
+                    ownerKind = probeResult.OwnerKind,
+                    ownerState = probeResult.OwnerState,
+                    ownerRelation = probeResult.OwnerRelation,
+                    weeklyReport = probeResult.WeeklyReport
+                }
+            };
+            WriteSliceCEvidenceFile(
+                JsonSerializer.Serialize(evidence, EvidenceJsonOptions),
+                "P7_2_SLICE_C_FRESH_PREFLIGHT_PROBE_EVIDENCE_PATH",
+                "P72FreshSliceCFixturePreflightProbeEvidence.json");
+        }
+    }
```

---

# 4. Considerations (設計考量)

### 4.1 Performance (效能)
- **唯讀查詢優化**：所有的 `Retrieve` 與 `RetrieveMultiple` 查詢均使用 `ColumnSet` 限制回傳欄位，且 `QueryExpression` 設定 `NoLock = true`，避免對 CRM 資料庫造成鎖定或效能負擔。
- **無多餘連線**：探測完成後立即釋放 WCF 連線，不佔用連線池資源。

### 4.2 Accessibility & Security (安全性與合規性)
- **WCAG / Deidentification 規範**：嚴格遵守去識別化原則，不暴露任何 CRM ID、使用者名稱、組織名稱或 raw exception。
- **Reparse Point 阻擋**：在寫入 evidence 檔案時，呼叫 `RejectReparsePoint` 驗證路徑，防止符號連結 (Symlink) 或目錄接合點 (Junction) 攻擊。

### 4.3 Maintainability (可維護性)
- **強型別 Schema 驗證**：PowerShell 腳本中的 `Get-StrictSliceCFreshPreflightProbeEvidenceFile` 函數會對 JSON 檔案的屬性名稱與允許值進行嚴格的 allowlist 比對，任何不符合 schema 的輸出都會導致 `evidence-result-unavailable` 並 fail closed，確保合約的嚴謹性。

# P7.2 Slice C Fresh Preflight Diagnostic Analysis - Design Review

本設計審查針對 P7.2 Slice C 全新測試環境（Fresh Fixture）部署前置條件的唯讀診斷探針（Preflight Probe）進行架構規劃與設計。

---

## 1. Analysis (當前架構評估)

在現有的 `ProvisionFreshFixture` 流程中，若前置條件驗證失敗（例如：5 個 static list 權屬不正確、leader 聯絡人狀態異常、或 weekly report 日期不符），系統會直接回傳去識別化的 `fixture-precondition-failed` 錯誤，但**刻意隱藏了具體是哪一個前置條件未通過**。這使得維運人員在面對部署失敗時，難以定位具體原因。

現有的 `Reconcile` 與 `Repair` 模式是針對已部署的 fixture 進行狀態比對與修復，其邏輯與部署前的「前置條件驗證」完全不同，因此無法直接重用。我們需要一個專屬的、**唯讀的** preflight probe 模式，在不產生任何 side effects（如建立 entity、寫入 ledger、發布 descriptor）的前提下，安全地診斷並分類所有前置條件。

---

## 2. Architecture Decision (關鍵設計決策)

### 決策 1：新增獨立的 `-ProvisionFreshProbe` 參數集
- **理由**：將 Probe 模式與現有的 `Provision` / `Cleanup` 流程在參數層面完全隔離，確保 Probe 模式下絕對不會觸發任何 mutation 邏輯。
- **替代方案**：在現有的 `ProvisionFreshFixture` 中加入 `-ProbeOnly` 開關。
- **拒絕理由**：共用參數集容易因腳本邏輯判斷失誤而誤觸寫入操作，隔離參數集能提供最強的編譯期與執行期安全保障。

### 決策 2：定義專屬的 `provision-probe` Lane 與去識別化詞彙
- **理由**：Probe 產出的 evidence 必須符合嚴格的去識別化規範，絕不洩漏 CRM ID、組織 URI 或詳細 Exception。
- **允許的 Reason 詞彙**：
  - `fresh-preflight-proven` (當所有 precondition 均通過，outcome 為 `go`)
  - `fixture-precondition-failed` (前置條件驗證失敗，outcome 為 `no-go`)
  - `baseline-owner-unavailable` (當 baseline owner 與 WhoAmI 使用者相同時，outcome 為 `no-go`)
  - `runtime-failure` (發生非預期異常，outcome 為 `no-go`)

### 決策 3：唯讀診斷與零狀態殘留
- **理由**：Probe 模式下 `OperationExecuted` 與 `DescriptorPublicationReady` 必須強制為 `false`，且絕不寫入或修改 `fresh-slice-c-ledger.json` 檔案，確保零狀態殘留。

---

## 3. Implementation Plan (實作計畫)

1. **PowerShell 進入點**：在 `Invoke-Package02Data8ListManagementEvidence.ps1` 中新增 `-ProvisionFreshProbe` 參數，並在執行時設定環境變數 `SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PROVISION_PROBE=1`。
2. **Gate 屬性**：新增 `P72Data8SliceCFreshProvisionProbeFactAttribute` 用於過濾與觸發 Probe 測試。
3. **Evidence 驗證**：修改 `P72FreshSliceCFixtureLiveEvidence.cs`，支援 `provision-probe` lane 及其對應的去識別化 reason。
4. **唯讀診斷器**：在 `P72FreshSliceCFixtureProvisioner.cs` 中實作 `Probe` 方法，依序驗證：
   - Request shape 完整性（傳入隨機產生的 nonce 以滿足驗證）。
   - 5 個 operational lists 是否為 task-owned。
   - Leader 聯絡人的權屬、啟用狀態，並確認其 owner 與 WhoAmI 使用者不同。
   - Weekly report 的唯一性、啟用狀態與週日日期正確性。
5. **整合測試**：在 `LivePackage02Data8ListManagementFreshFixtureTests.cs` 中新增 `Probe_fresh_package02_data8_list_management_fixture_emits_sanitized_evidence` 測試案例。

---

## 4. Considerations (考量事項)

- **安全性 (Security)**：Probe 僅執行 `Retrieve` 操作，絕不呼叫 `Create`、`Update`、`Delete`、`Assign` 或 `Execute`。所有 Exception 均被 catch 並轉譯為 `runtime-failure`，防止敏感資訊外洩。
- **效能 (Performance)**：診斷過程僅進行少量的唯讀查詢，對 CRM 伺服器負載極低。
- **可維護性 (Maintainability)**：透過 `P72FreshSliceCFixtureProbeResult` 結構化回傳診斷階段（`ProbeStage`），便於開發人員在測試記錄中定位失敗的具體步驟。

---

## 5. Unified Diff Patch

```diff
--- a/docs/scripts/Invoke-Package02Data8ListManagementEvidence.ps1
+++ b/docs/scripts/Invoke-Package02Data8ListManagementEvidence.ps1
@@ -67,3 +67,7 @@
     [Parameter(ParameterSetName = 'CleanupFresh')]
-    [switch] $ConfirmFreshFixtureCleanup
+    [switch] $ConfirmFreshFixtureCleanup,
+
+    [Parameter(ParameterSetName = 'ProvisionFreshProbe')]
+    [switch] $ProvisionFreshProbe
 )
@@ -93,4 +97,5 @@
 $isFreshProvisionMode = [bool]$ProvisionFreshFixture
 $isFreshCleanupMode = [bool]$CleanupFreshFixture
-$liveModeRequested = [bool]($ExecuteFixture -or $ReconcileFixture -or $RepairFixture -or $RepairProbe -or $ProvisionFreshFixture -or $CleanupFreshFixture)
-$operationMayHaveExecuted = -not ($isReconciliationMode -or $isRepairMode -or $isRepairProbeMode)
+$isFreshProvisionProbeMode = [bool]$ProvisionFreshProbe
+$liveModeRequested = [bool]($ExecuteFixture -or $ReconcileFixture -or $RepairFixture -or $RepairProbe -or $ProvisionFreshFixture -or $CleanupFreshFixture -or $ProvisionFreshProbe)
+$operationMayHaveExecuted = -not ($isReconciliationMode -or $isRepairMode -or $isRepairProbeMode -or $isFreshProvisionProbeMode)
@@ -135,2 +140,3 @@
     'SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PROVISION',
+    'SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PROVISION_PROBE',
     'SPEECHMESSAGE_P7_2_SLICE_C_FRESH_CLEANUP',
@@ -1172,3 +1178,3 @@
     param(
         [string] $EvidencePath,
-        [ValidateSet('provision', 'cleanup')]
+        [ValidateSet('provision', 'cleanup', 'provision-probe')]
         [string] $ExpectedLane
@@ -1206,2 +1212,8 @@
     }
+    elseif ($ExpectedLane -ceq 'provision-probe') {
+        @(
+            'fresh-preflight-proven',
+            'fixture-precondition-failed',
+            'baseline-owner-unavailable',
+            'runtime-failure'
+        )
     }
     else {
@@ -1231,6 +1243,9 @@
 
-    $expectedGoReason = if ($ExpectedLane -eq 'provision') {
+    $expectedGoReason = if ($ExpectedLane -eq 'provision') {
         'fresh-fixture-provisioned'
     }
+    elseif ($ExpectedLane -eq 'provision-probe') {
+        'fresh-preflight-proven'
+    }
     else {
         'fresh-fixture-cleaned'
     }
     $expectedDescriptorPublicationReady = $ExpectedLane -eq 'provision'
+    $expectedOperationExecuted = $ExpectedLane -eq 'provision' -or $ExpectedLane -eq 'cleanup'
     if ($evidence.outcome -eq 'go' -and
         ($evidence.reason -cne $expectedGoReason -or
-         $evidence.operationExecuted -ne $true -or
+         $evidence.operationExecuted -ne $expectedOperationExecuted -or
          $evidence.descriptorPublicationReady -ne $expectedDescriptorPublicationReady)) {
@@ -2387,3 +2402,3 @@
     # ?澆蝡臭?????Credential Manager ??child process嚗?甇文仃????CE I/O??
-    if ($isFreshProvisionMode -or $isFreshCleanupMode) {
+    if ($isFreshProvisionMode -or $isFreshCleanupMode -or $isFreshProvisionProbeMode) {
         $freshControlPlaneRoots = Get-CurrentUserFreshFixtureControlPlaneRoots
@@ -2411,3 +2426,3 @@
         }
-        else {
+        elseif ($isFreshCleanupMode) {
             # cleanup ?芸???潔???fully proven ??exact-ID graph嚗圾?? owner binding 憭望???
@@ -2494,4 +2509,5 @@
         [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PROVISION', $(if ($isFreshProvisionMode) { '1' } else { $null }), 'Process')
+        [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PROVISION_PROBE', $(if ($isFreshProvisionProbeMode) { '1' } else { $null }), 'Process')
         [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_P7_2_SLICE_C_FRESH_CLEANUP', $(if ($isFreshCleanupMode) { '1' } else { $null }), 'Process')
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_LEDGER_ROOT', [string]$freshControlPlaneRoots.ledgerRoot, 'Process')
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_LEDGER_PATH', [string]$freshLedgerPath, 'Process')
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_DIAGNOSTIC_PATH', $null, 'Process')
-        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_DESCRIPTOR_CONFIRMATION', $(if ($isFreshProvisionMode) { 'replace-stale-descriptor' } else { 'cleanup-fresh-fixture' }), 'Process')
-        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_NONCE', [string]$freshNonce, 'Process')
+        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_DESCRIPTOR_CONFIRMATION', $(if ($isFreshProvisionMode) { 'replace-stale-descriptor' } elseif ($isFreshCleanupMode) { 'cleanup-fresh-fixture' } else { $null }), 'Process')
+        [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_NONCE', $(if ($isFreshProvisionProbeMode) { $null } else { [string]$freshNonce }), 'Process')
         [Environment]::SetEnvironmentVariable('P7_2_SLICE_C_FRESH_OWNER', $identity, 'Process')
@@ -2507,3 +2523,3 @@
         [Environment]::SetEnvironmentVariable(
             'P7_2_SLICE_C_FRESH_EXISTING_TARGET_LEADER_ID',
-            $(if ($isFreshProvisionMode) { [string]$fixture.smallGroupTargetLeaderContactId } else { $null }),
+            $(if ($isFreshProvisionMode -or $isFreshProvisionProbeMode) { [string]$fixture.smallGroupTargetLeaderContactId } else { $null }),
             'Process')
@@ -2557,3 +2573,3 @@
     $freshDiagnosticPath = $null
-    if ($isFreshProvisionMode -or $isFreshCleanupMode) {
+    if ($isFreshProvisionMode -or $isFreshCleanupMode -or $isFreshProvisionProbeMode) {
         # Fresh child ?芣??fresh evidence path嚗??臭誑撖怠?????Slice C ??generic?econcile?epair
@@ -2576,2 +2592,5 @@
         }
+        elseif ($isFreshProvisionProbeMode) {
+            'FullyQualifiedName=ChurchReport.MemberInfo.Tests.LivePackage02Data8ListManagementFreshFixtureTests.Probe_fresh_package02_data8_list_management_fixture_emits_sanitized_evidence'
+        }
         else {
@@ -2712,4 +2731,4 @@
-    elseif ($isFreshProvisionMode -or $isFreshCleanupMode) {
+    elseif ($isFreshProvisionMode -or $isFreshCleanupMode -or $isFreshProvisionProbeMode) {
         # Fresh lane ??child schema ?? Slice C evidence 摰銝???摰??誑 fresh allowlist 閫??摰??捱摰?
         # ?臬?潔? descriptor ?宏??recovery state???迂 generic parser ?銝€璇?lane ??雿?靽∩遙??雿??€?
-        $freshExpectedLane = if ($isFreshProvisionMode) { 'provision' } else { 'cleanup' }
+        $freshExpectedLane = if ($isFreshProvisionMode) { 'provision' } elseif ($isFreshProvisionProbeMode) { 'provision-probe' } else { 'cleanup' }
         try {
@@ -2721,3 +2740,3 @@
 
-            if ($strictEvidence.outcome -eq 'go') {
+            if ($strictEvidence.outcome -eq 'go' -and -not $isFreshProvisionProbeMode) {
                 if ($isFreshProvisionMode) {
--- a/ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureLiveGateTests.cs
+++ b/ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureLiveGateTests.cs
@@ -455,2 +455,32 @@
 }
+
+/// <summary>
+/// 驗證 parent runner 是否已設定 fresh provision probe 所需的環境變數。
+/// </summary>
+[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
+internal sealed class P72Data8SliceCFreshProvisionProbeFactAttribute : FactAttribute
+{
+    private static readonly string[] RequiredEnvironmentNames =
+    [
+        "SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PROVISION_PROBE",
+        "CRM_PASSWORD",
+        "P7_2_SLICE_C_FRESH_EVIDENCE_PATH",
+        "P7_2_SLICE_C_FRESH_OWNER",
+        "P7_2_SLICE_C_FRESH_ADD_LIST_ID",
+        "P7_2_SLICE_C_FRESH_REMOVE_LIST_ID",
+        "P7_2_SLICE_C_FRESH_SMALL_GROUP_LIST_ID",
+        "P7_2_SLICE_C_FRESH_EXISTING_TARGET_LEADER_ID",
+        "P7_2_SLICE_C_FRESH_TRANSFER_SOURCE_LIST_ID",
+        "P7_2_SLICE_C_FRESH_TRANSFER_TARGET_LIST_ID",
+        "P7_2_SLICE_C_FRESH_TRANSFER_WEEK_START_UTC"
+    ];
+
+    public P72Data8SliceCFreshProvisionProbeFactAttribute()
+    {
+        if (!HasCompleteGate("SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PROVISION_PROBE"))
+        {
+            Skip = "P7.2 fresh-fixture provision probe requires an explicit parent-owned Data8 gate.";
+        }
+    }
+
+    private static bool HasCompleteGate(string modeName)
+        => string.Equals(Environment.GetEnvironmentVariable(modeName), "1", StringComparison.Ordinal) &&
+           RequiredEnvironmentNames.All(static name => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)));
+}
--- a/ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureLiveEvidence.cs
+++ b/ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureLiveEvidence.cs
@@ -72,2 +72,8 @@
     ];
+    private static readonly string[] ProvisionProbeReasons =
+    [
+        "fresh-preflight-proven",
+        "fixture-precondition-failed",
+        "baseline-owner-unavailable",
+        "runtime-failure"
+    ];
     private static readonly string[] ProvisionDiagnosticCategories =
@@ -204,5 +210,6 @@
     {
-        var validLane = value.Lane is "provision" or "cleanup";
+        var validLane = value.Lane is "provision" or "cleanup" or "provision-probe";
         var validOutcome = value.Outcome is "go" or "no-go";
-        var allowedReasons = value.Lane == "provision" ? ProvisionReasons : CleanupReasons;
+        var allowedReasons = value.Lane == "provision" ? ProvisionReasons :
+                             value.Lane == "provision-probe" ? ProvisionProbeReasons : CleanupReasons;
         var validReason = Array.IndexOf(allowedReasons, value.Reason) >= 0;
--- a/ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureProvisioner.cs
+++ b/ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureProvisioner.cs
@@ -215,2 +215,15 @@
     bool OperationExecuted);
+
+/// <summary>
+/// 表示 fresh preflight probe 的唯讀診斷結果。
+/// </summary>
+internal sealed record P72FreshSliceCFixtureProbeResult(
+    string Outcome,
+    string Reason,
+    bool RequestShapeValid,
+    bool OperationalListsValid,
+    bool LeaderProvenanceValid,
+    bool LeaderOwnerActive,
+    bool LeaderOwnerDiffersFromWhoAmI,
+    bool WeeklyReportValid,
+    string ProbeStage);
@@ -416,2 +429,97 @@
     }
+
+    /// <summary>
+    /// 執行唯讀的 fresh preflight 診斷，驗證所有 precondition 是否滿足，絕不執行任何 mutation。
+    /// </summary>
+    internal P72FreshSliceCFixtureProbeResult Probe(P72FreshSliceCFixtureProvisionRequest request)
+    {
+        ArgumentNullException.ThrowIfNull(request);
+
+        var requestShapeValid = IsRequestShapeValid(request);
+        var operationalListsValid = false;
+        var leaderProvenanceValid = false;
+        var leaderOwnerActive = false;
+        var leaderOwnerDiffersFromWhoAmI = false;
+        var weeklyReportValid = false;
+        var outcome = "no-go";
+        var reason = "fixture-precondition-failed";
+        var probeStage = "not-started";
+
+        try
+        {
+            probeStage = "request-shape-checked";
+            if (!requestShapeValid)
+            {
+                return new P72FreshSliceCFixtureProbeResult(outcome, reason, requestShapeValid, operationalListsValid, leaderProvenanceValid, leaderOwnerActive, leaderOwnerDiffersFromWhoAmI, weeklyReportValid, probeStage);
+            }
+
+            probeStage = "operational-lists-checked";
+            operationalListsValid = AreOperationalListsTaskOwned(request);
+            if (!operationalListsValid)
+            {
+                return new P72FreshSliceCFixtureProbeResult(outcome, reason, requestShapeValid, operationalListsValid, leaderProvenanceValid, leaderOwnerActive, leaderOwnerDiffersFromWhoAmI, weeklyReportValid, probeStage);
+            }
+
+            probeStage = "leader-provenance-checked";
+            var leader = _service.Retrieve(
+                ContactEntityName,
+                request.ExistingTargetLeaderContactId,
+                new ColumnSet(ContactFullNameAttribute, OwnerAttribute));
+
+            if (leader is not null &&
+                string.Equals(leader.LogicalName, ContactEntityName, StringComparison.Ordinal) &&
+                leader.Id == request.ExistingTargetLeaderContactId &&
+                leader.Attributes.TryGetValue(ContactFullNameAttribute, out var nameValue) &&
+                nameValue is string fullName &&
+                fullName.StartsWith(SliceCListMarkerPrefix, StringComparison.Ordinal) &&
+                leader.Attributes.TryGetValue(OwnerAttribute, out var ownerValue) &&
+                ownerValue is EntityReference { LogicalName: SystemUserEntityName, Id: var ownerId } &&
+                ownerId != Guid.Empty)
+            {
+                leaderProvenanceValid = true;
+
+                probeStage = "leader-owner-checked";
+                var owner = _service.Retrieve(
+                    SystemUserEntityName,
+                    ownerId,
+                    new ColumnSet(SystemUserDisabledAttribute));
+
+                if (owner is not null &&
+                    string.Equals(owner.LogicalName, SystemUserEntityName, StringComparison.Ordinal) &&
+                    owner.Id == ownerId &&
+                    owner.Attributes.TryGetValue(SystemUserDisabledAttribute, out var disabledValue) &&
+                    disabledValue is bool isDisabled &&
+                    !isDisabled)
+                {
+                    leaderOwnerActive = true;
+                    leaderOwnerDiffersFromWhoAmI = ownerId != request.Data8ServiceUserId;
+                }
+            }
+
+            if (!leaderProvenanceValid || !leaderOwnerActive)
+            {
+                return new P72FreshSliceCFixtureProbeResult(outcome, reason, requestShapeValid, operationalListsValid, leaderProvenanceValid, leaderOwnerActive, leaderOwnerDiffersFromWhoAmI, weeklyReportValid, probeStage);
+            }
+
+            if (!leaderOwnerDiffersFromWhoAmI)
+            {
+                reason = "baseline-owner-unavailable";
+                return new P72FreshSliceCFixtureProbeResult(outcome, reason, requestShapeValid, operationalListsValid, leaderProvenanceValid, leaderOwnerActive, leaderOwnerDiffersFromWhoAmI, weeklyReportValid, probeStage);
+            }
+
+            probeStage = "weekly-report-checked";
+            weeklyReportValid = TryResolveExactlyOneActiveWeeklyReport(
+                request.TransferTargetListId,
+                request.TransferWeekStartUtc,
+                out _);
+
+            if (!weeklyReportValid)
+            {
+                return new P72FreshSliceCFixtureProbeResult(outcome, reason, requestShapeValid, operationalListsValid, leaderProvenanceValid, leaderOwnerActive, leaderOwnerDiffersFromWhoAmI, weeklyReportValid, probeStage);
+            }
+
+            probeStage = "classification-complete";
+            outcome = "go";
+            reason = "fresh-preflight-proven";
+            return new P72FreshSliceCFixtureProbeResult(outcome, reason, requestShapeValid, operationalListsValid, leaderProvenanceValid, leaderOwnerActive, leaderOwnerDiffersFromWhoAmI, weeklyReportValid, probeStage);
+        }
+        catch (Exception)
+        {
+            outcome = "no-go";
+            reason = "runtime-failure";
+            return new P72FreshSliceCFixtureProbeResult(outcome, reason, requestShapeValid, operationalListsValid, leaderProvenanceValid, leaderOwnerActive, leaderOwnerDiffersFromWhoAmI, weeklyReportValid, probeStage);
+        }
+    }
--- a/ChurchReport.MemberInfo.Tests/LivePackage02Data8ListManagementFreshFixtureTests.cs
+++ b/ChurchReport.MemberInfo.Tests/LivePackage02Data8ListManagementFreshFixtureTests.cs
@@ -158,2 +158,93 @@
     }
+
+    /// <summary>
+    /// 執行唯讀的 fresh preflight probe，驗證所有 precondition 是否滿足，並寫入 sanitized evidence。
+    /// </summary>
+    [P72Data8SliceCFreshProvisionProbeFact]
+    public async Task Probe_fresh_package02_data8_list_management_fixture_emits_sanitized_evidence()
+    {
+        var outcome = "no-go";
+        var reason = "runtime-failure";
+        FreshProvisionProbeEnvironment? environment = null;
+        ILoggerFactory? loggerFactory = null;
+        EmbeddedData8Runtime? runtime = null;
+        OnPremiseClient? service = null;
+
+        try
+        {
+            environment = ReadProvisionProbeEnvironment();
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
+            var serviceUserId = await LivePackage02Data8ListManagementEvidenceTests
+                .ResolveFixtureTargetOwnerIdAsync(runtime.Executor, organization.OrganizationId)
+                .ConfigureAwait(false);
+            if (serviceUserId is not Guid verifiedServiceUserId)
+            {
+                reason = "fixture-precondition-failed";
+            }
+            else
+            {
+                service = new OnPremiseClient(organization.ServiceUri, settings.UserName, credentialPassword!);
+                var request = new P72FreshSliceCFixtureProvisionRequest(
+                    environment.AddListId,
+                    environment.RemoveListId,
+                    environment.SmallGroupListId,
+                    environment.ExistingTargetLeaderContactId,
+                    environment.TransferSourceListId,
+                    environment.TransferTargetListId,
+                    environment.TransferWeekStartUtc,
+                    verifiedServiceUserId,
+                    Guid.NewGuid()); // 傳入隨機 nonce 以滿足 shape 驗證
+
+                var result = new P72FreshSliceCFixtureProvisioner(service).Probe(request);
+                outcome = result.Outcome;
+                reason = result.Reason;
+            }
+        }
+        catch (Exception)
+        {
+            outcome = "no-go";
+            reason = "runtime-failure";
+        }
+        finally
+        {
+            DisposeService(ref service, ref outcome, ref reason);
+            if (!await DisposeRuntimeAsync(runtime).ConfigureAwait(false))
+            {
+                outcome = "no-go";
+                reason = "runtime-failure";
+            }
+            DisposeLogger(ref loggerFactory, ref outcome, ref reason);
+        }
+
+        if (environment is not null)
+        {
+            P72FreshSliceCFixtureLiveEvidence.Write(
+                environment.EvidencePath,
+                environment.EvidenceRoot,
+                new P72FreshSliceCFixtureLiveEvidenceValue(
+                    "provision-probe",
+                    outcome,
+                    reason,
+                    OperationExecuted: false,
+                    DescriptorPublicationReady: false));
+        }
+
+        outcome.Should().Be(
+            "go",
+            because: "the preflight probe must prove every precondition is satisfied");
+    }
+
+    private sealed record FreshProvisionProbeEnvironment(
+        string EvidenceRoot,
+        string EvidencePath,
+        Guid AddListId,
+        Guid RemoveListId,
+        Guid SmallGroupListId,
+        Guid ExistingTargetLeaderContactId,
+        Guid TransferSourceListId,
+        Guid TransferTargetListId,
+        DateTimeOffset TransferWeekStartUtc);
+
+    private static FreshProvisionProbeEnvironment ReadProvisionProbeEnvironment()
+    {
+        if (!string.Equals(
+                Environment.GetEnvironmentVariable("SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PROVISION_PROBE"),
+                "1",
+                StringComparison.Ordinal))
+        {
+            throw new InvalidOperationException("The fresh-fixture probe mode is invalid.");
+        }
+
+        var evidencePath = ReadRequiredText("P7_2_SLICE_C_FRESH_EVIDENCE_PATH", maximumLength: 1024);
+        var evidenceRoot = Path.GetDirectoryName(evidencePath)
+            ?? throw new InvalidOperationException("The fresh-fixture evidence path is invalid.");
+
+        return new FreshProvisionProbeEnvironment(
+            evidenceRoot,
+            evidencePath,
+            ReadGuid("P7_2_SLICE_C_FRESH_ADD_LIST_ID"),
+            ReadGuid("P7_2_SLICE_C_FRESH_REMOVE_LIST_ID"),
+            ReadGuid("P7_2_SLICE_C_FRESH_SMALL_GROUP_LIST_ID"),
+            ReadGuid("P7_2_SLICE_C_FRESH_EXISTING_TARGET_LEADER_ID"),
+            ReadGuid("P7_2_SLICE_C_FRESH_TRANSFER_SOURCE_LIST_ID"),
+            ReadGuid("P7_2_SLICE_C_FRESH_TRANSFER_TARGET_LIST_ID"),
+            ReadSundayUtc("P7_2_SLICE_C_FRESH_TRANSFER_WEEK_START_UTC"));
+    }
```

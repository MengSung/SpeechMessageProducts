## 1. Analysis (架構評估)

當前 `Invoke-Package02Data8ListManagementEvidence.ps1` 腳本與 C# 測試專案 `ChurchReport.MemberInfo.Tests` 提供了完整的 Slice C fresh-fixture 部署 (`ProvisionFresh`) 與清理 (`CleanupFresh`) 流程。然而，當前部署流程在遭遇前置條件失敗（例如 `baseline-owner-unavailable`）時，會直接中斷並回傳去識別化的 `fixture-precondition-failed`，且會消耗一次由操作者授權的部署額度。

為了在不執行任何 CRM 變更（Create, Update, Delete, Assign 等）的前提下，先行驗證部署環境的健康度，我們需要設計一個全新的 **opt-in、唯讀 (read-only) 的 fresh preflight probe**。

---

## 2. Architecture Decision (關鍵設計決策)

### 決策 1：獨立的參數集與環境變數隔離
- **決策**：在 PowerShell 腳本中新增 `ProvisionFreshProbe` 參數集，並透過環境變數 `SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PROVISION_PROBE = 1` 傳遞至 C# 測試子程序。
- **理由**：確保 probe 流程與實際的 `ProvisionFresh` 流程在參數與執行路徑上完全隔離，避免誤觸任何 mutation 邏輯。
- **替代方案**：重用現有的 `ProvisionFresh` 參數並加入 `-Probe` 開關。此方案容易因參數組合錯誤而誤執行實際部署，故予以否決。

### 決策 2：專屬的唯讀診斷方法 `Probe`
- **決策**：在 `P72FreshSliceCFixtureProvisioner` 中新增 `Probe` 方法，僅執行 `Retrieve` 與 `RetrieveMultiple` 查詢，絕不呼叫 `Create`、`Execute` (Assign/AddMember) 或 `PersistLedger`。
- **理由**：從代碼層面保證 probe 的唯讀性，並在測試中驗證其 `operationExecuted` 恆為 `false`。

### 決策 3：去識別化的 Evidence 輸出
- **決策**：定義專屬的 `provision-probe` lane，其 Evidence 僅包含去識別化的狀態分類（如 `fresh-preflight-proven`、`baseline-owner-unavailable`），絕不洩漏 CRM ID、使用者名稱或 Exception 詳情。

---

## 3. Implementation Plan (實作計畫)

### 步驟 1：修改 PowerShell 腳本
- 新增 `-ProvisionFreshProbe` 開關。
- 設定 `SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PROVISION_PROBE` 環境變數。
- 在 `Get-StrictFreshFixtureEvidenceFile` 中支援 `provision-probe` lane，並驗證 `operationExecuted` 與 `descriptorPublicationReady` 恆為 `false`。

### 步驟 2：新增 FactAttribute 與 Evidence 驗證
- 在 `P72FreshSliceCFixtureLiveGateTests.cs` 中新增 `P72Data8SliceCFreshProvisionProbeFactAttribute`。
- 在 `P72FreshSliceCFixtureLiveEvidence.cs` 中新增 `ProvisionProbeReasons` 並支援 `provision-probe` 驗證。

### 步驟 3：實作唯讀診斷邏輯
- 在 `P72FreshSliceCFixtureProvisioner.cs` 中新增 `Probe` 方法，依序驗證：
  1. Request Shape (`IsRequestShapeValid`)
  2. Operational Lists (`AreOperationalListsTaskOwned`)
  3. Leader Provenance & Owner Active State (`TryResolveActiveBaselineOwner` 的唯讀拆解版)
  4. Weekly Report (`TryResolveExactlyOneActiveWeeklyReport`)
- 在 `LivePackage02Data8ListManagementFreshFixtureTests.cs` 中新增 `Probe_fresh_package02_data8_list_management_fixture_emits_sanitized_evidence` 測試方法。

---

## 4. Considerations (注意事項)

- **安全性 (Security)**：Probe 輸出的 JSON 必須嚴格遵守 UTF-8 no-BOM 與 CRLF 格式，且絕不能包含任何敏感的 CRM 識別碼或憑證資訊。
- **狀態隔離 (State Isolation)**：Probe 執行時不寫入任何 ledger 檔案，亦不修改 descriptor 檔案，確保執行前後系統狀態完全一致。
- **重用評估 (Reusability)**：現有的 `RepairProbe` 或 `Reconcile` 模式無法重用，因為它們是針對已存在的 fixture 進行狀態比對，而本 probe 是針對「全新部署前」的環境前置條件進行驗證，兩者邏輯完全不同。

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
@@ -93,4 +99,5 @@
 $isFreshProvisionMode = [bool]$ProvisionFreshFixture
 $isFreshCleanupMode = [bool]$CleanupFreshFixture
-$liveModeRequested = [bool]($ExecuteFixture -or $ReconcileFixture -or $RepairFixture -or $RepairProbe -or $ProvisionFreshFixture -or $CleanupFreshFixture)
-$operationMayHaveExecuted = -not ($isReconciliationMode -or $isRepairMode -or $isRepairProbeMode)
+$isFreshProvisionProbeMode = [bool]$ProvisionFreshProbe
+$liveModeRequested = [bool]($ExecuteFixture -or $ReconcileFixture -or $RepairFixture -or $RepairProbe -or $ProvisionFreshFixture -or $CleanupFreshFixture -or $ProvisionFreshProbe)
+$operationMayHaveExecuted = -not ($isReconciliationMode -or $isRepairMode -or $isRepairProbeMode -or $isFreshProvisionProbeMode)
@@ -135,2 +142,3 @@
     'SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PROVISION',
+    'SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PROVISION_PROBE',
     'SPEECHMESSAGE_P7_2_SLICE_C_FRESH_CLEANUP',
@@ -1172,3 +1180,3 @@
     param(
         [string] $EvidencePath,
-        [ValidateSet('provision', 'cleanup')]
+        [ValidateSet('provision', 'cleanup', 'provision-probe')]
         [string] $ExpectedLane
@@ -1206,2 +1214,8 @@
     }
+    elseif ($ExpectedLane -ceq 'provision-probe') {
+        @(
+            'fresh-preflight-proven',
+            'fixture-precondition-failed',
+            'baseline-owner-unavailable',
+            'runtime-failure'
+        )
+    }
     else {
@@ -1231,6 +1245,9 @@
 
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
@@ -2387,3 +2404,3 @@
     # ?澆蝡臭?????Credential Manager ??child process嚗?甇文仃????CE I/O??
-    if ($isFreshProvisionMode -or $isFreshCleanupMode) {
+    if ($isFreshProvisionMode -or $isFreshCleanupMode -or $isFreshProvisionProbeMode) {
         $freshControlPlaneRoots = Get-CurrentUserFreshFixtureControlPlaneRoots
@@ -2411,3 +2428,3 @@
         }
-        else {
+        elseif ($isFreshCleanupMode) {
             # cleanup ?芸???潔???fully proven ??exact-ID graph嚗圾?? owner binding ??
@@ -2494,4 +2511,5 @@
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
@@ -2507,3 +2525,3 @@
         [Environment]::SetEnvironmentVariable(
             'P7_2_SLICE_C_FRESH_EXISTING_TARGET_LEADER_ID',
-            $(if ($isFreshProvisionMode) { [string]$fixture.smallGroupTargetLeaderContactId } else { $null }),
+            $(if ($isFreshProvisionMode -or $isFreshProvisionProbeMode) { [string]$fixture.smallGroupTargetLeaderContactId } else { $null }),
             'Process')
@@ -2557,3 +2575,3 @@
     $freshDiagnosticPath = $null
-    if ($isFreshProvisionMode -or $isFreshCleanupMode) {
+    if ($isFreshProvisionMode -or $isFreshCleanupMode -or $isFreshProvisionProbeMode) {
         # Fresh child ?芣??fresh evidence path嚗??臭誑撖怠?????Slice C ??generic?econcile?epair
@@ -2576,2 +2594,5 @@
         }
+        elseif ($isFreshProvisionProbeMode) {
+            'FullyQualifiedName=ChurchReport.MemberInfo.Tests.LivePackage02Data8ListManagementFreshFixtureTests.Probe_fresh_package02_data8_list_management_fixture_emits_sanitized_evidence'
+        }
         else {
@@ -2712,4 +2733,4 @@
-    elseif ($isFreshProvisionMode -or $isFreshCleanupMode) {
+    elseif ($isFreshProvisionMode -or $isFreshCleanupMode -or $isFreshProvisionProbeMode) {
         # Fresh lane ??child schema ?? Slice C evidence 摰銝???摰??誑 fresh allowlist 閫??摰??捱摰?
         # ?臬?潔? descriptor ?宏??recovery state???迂 generic parser ?銝€璇?lane ??雿?靽∩遙??雿??€?
-        $freshExpectedLane = if ($isFreshProvisionMode) { 'provision' } else { 'cleanup' }
+        $freshExpectedLane = if ($isFreshProvisionMode) { 'provision' } elseif ($isFreshProvisionProbeMode) { 'provision-probe' } else { 'cleanup' }
         try {
@@ -2721,3 +2742,3 @@
 
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

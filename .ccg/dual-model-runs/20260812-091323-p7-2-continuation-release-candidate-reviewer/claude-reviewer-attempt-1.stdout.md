# P7.2 Continuation Release Candidate Review Report

依 6 項 Required Invariants 對現行工作區變更（`ToolUtilityFacade.cs`、`DownloadListManager.cs`、`ListManager.cs`、`P72ContinuationLocalOnlyCatalog.cs`、`OperationIds.cs`、相關測試與 `docs/scripts/*.ps1`）逐一以檔案內容驗證，未執行或請求任何 CE 操作，未以測試結果推論 live CE 完成度。以下同時交叉檢視了同一 run 目錄中 Gemini 產出的先前報告，對其發現逐條複核。

---

## Critical 🔴

### C1. `ToolUtilityFacade` 三個 overload 完全忽略傳入的 operation-scoped service
**檔案**：`ToolUtility/Core/ToolUtilityFacade.cs:466-482`
```csharp
public EntityCollection RetrieveDynamicMemberList(IOrganizationService service, string strList)
{
    IOrganizationService svc = _organizationService;   // 參數 service 從未被使用
    return RetrieveDynamicMemberList(ref svc, Guid.Parse(strList));
}
```
`RetrieveDynamicMemberList`、`RetrieveDynamicMemberListDynamics365`、`RetrieveDynamicMemberListCrm2011`（非 `ref` overload）都宣告了 `IOrganizationService service` 參數卻完全不用，改用 facade 私有、可變欄位 `_organizationService`（同檔案第 56 行註明「改為可變更,由連接服務方法設定」）。這與同檔案中已被本次 diff 正確修正的 `RetrieveMemberListCollectionByListId*`（改為使用傳入的 `ref` 參數，見 441-460 行）形成明顯不一致，違反 Invariant 1。
已確認目前生產程式碼未直接呼叫這三個非 `ref` overload（`DownloadListManager.cs` 一律呼叫 `ref` 版本），故非立即可觸發的執行路徑，但屬於同一 API 表面上的隱性陷阱，任何未來呼叫者會誤信已取得操作隔離。

### C2. D–H fail-closed 輸入名稱守門邏輯漏檢 token / organization / profile
**檔案**：`SpeechMessage.Dynamics.Abstractions/Operations/P72ContinuationLocalOnlyCatalog.cs:327-334`
```csharp
if (allowedInputNames.Any(name => name.Contains("owner", ...) ||
                                 name.Contains("endpoint", ...) ||
                                 name.Contains("credential", ...) ||
                                 name.Contains("entity", ...) ||
                                 name.Contains("fetch", ...)))
```
只檢查 5 個關鍵字，遺漏 Invariant 4 明確要求的 `token`、`organization`、`profile`。對照同一 PR 新增的測試 `SpeechMessage.Dynamics.Tests/P72ContinuationOperationIdsTests.cs:109-116`：
```csharp
var forbiddenInputFragments = new[]
{ "owner", "entity", "endpoint", "credential", "token", "fetch", "organization", "profile" };
```
測試自行重建了完整 8 詞清單並各自檢查通過，證明開發者清楚完整範圍，卻未同步更新生產碼的建構期守門邏輯。目前 13 筆定義未觸發此漏洞（僥倖通過），但守門機制本身不完整——未來若新增 `AllowedInputNames` 含 `"profileId"` 或 `"orgToken"` 等字樣，`Definition()` 的 `InvalidOperationException` fail-closed 機制不會攔截。

### C3. `ListManager.SetupIntegrateData` 呼叫鏈完全未接上 operation-scoped service
**檔案**：`SpeechMessageProducts.ChurchReport/Models/ListManager.cs:232-256`（本次 diff 未變更此方法）
```csharp
public void SetupIntegrateData( String ListEntityId )
{
    ...
    m_DownloadIntegrateData.SetupIntegrateData( m_Account, m_Password, LoginType, this.m_SelectDate,
        ListEntityId, aWeeklyReportRecord.WeeklyReportEntityId, ref m_ListSmallGroupWeeklyReport);
}
```
已確認 `DownloadIntegrateData.SetupIntegrateData(...)`（`DownloadIntegrateData.Core.cs:109`）簽章上完全沒有 `IOrganizationService` 參數可傳遞。也就是說本次「release candidate」的隔離修正只涵蓋 `GetListManager`/`SetupListManager` 呼叫鏈；同一個 `ListManager` 類別內另一條載入單一小組週報細節的路徑（點選小組時觸發）100% 仍依賴 `ToolUtilityFactory.GetInstance("DYNAMICS365-9.0")` 單例內部 service，完全未被本次修正觸及。

### C4. 隔離修正在目前生產環境只對 1/8 入口實際生效
**檔案**：`SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadListManager.cs:382-384`
```csharp
IOrganizationService serviceToUse = organizationService
    ?? this.m_ToolUtilityClass.m_Crm2011OrganizationService
    ?? this.m_ToolUtilityClass.m_OrganizationService;
```
已逐一檢查全部生產呼叫點：`BaseChurchController.cs`（2 處）、`SmallGroupController.Date.cs`（2 處）、`SmallGroupController.MultiGroupView.cs`（1 處）、`MemberInfoController.cs`（1 處）、`ListManagerCacheExtensions.cs`（2 處）皆呼叫 `SetupListManager` 時**完全省略** `organizationService` 參數（維持預設 `null`）；只有 `AuthenticationController.Private.cs:291-295` 傳入了 `service`。也已確認 `m_ToolUtilityClass = ToolUtilityFactory.GetInstance(...)`（`ToolUtility/Factory/ToolUtilityFactory.cs:50-69`，雙重檢查鎖定）是**真正的 process-wide 單例**，且 `m_Crm2011OrganizationService` 會在建構期由 AD 登入流程設定（`ToolUtilityClass.Core.cs:162`）。
結論：本次新增的所有「operation-scoped 隔離」文件宣稱與程式路徑，在目前 7/8 的生產入口上完全不生效——這些請求仍會經由上述 `??` fallback 命中同一個跨請求共用的單例連線欄位，與新增 docstring（`DownloadListManager.cs:104-107`）宣稱的「避免下一個 HTTP request、使用者或 Dynamics profile 取得前一個操作的可變連線狀態」直接矛盾。這不是本次 diff 引入的退化（fallback 行為與修正前一致），但作為「release candidate」的隔離保證聲明明顯言過其實。

---

## Warning 🟡

### W1. Gemini 先前回報的「編碼亂碼／mojibake」finding 經複核為 False Positive
針對五個檔案（`OperationIds.cs`、`P72ContinuationLocalOnlyCatalog.cs`、`ToolUtilityFacade.cs`、`ListManager.cs`、`DownloadListManager.cs`）以 hex dump 檢查開頭位元組（皆為 `2f 2f`／`//`，無 `EF BB BF` BOM）、並以 Python 對整檔做 UTF-8 decode 與 replacement character（U+FFFD）計數，結果全部檔案：`BOM=False, valid_utf8=True, replacement_chars=0`，檔頭繁體中文註解（例如「檔案：...」「目的：...」）清晰可讀，未發現 Gemini 引用的「`// 瑼?嚗peechMessage...`」字串。判定該 finding 為 Gemini 端 tooling／console 編碼問題造成的誤報，非儲存庫內容缺陷，本次**不採納**；Invariant 6（UTF-8 without BOM）在這五個檔案上成立。

### W2. 新增隔離回歸測試覆蓋率與實際修正範圍不對稱
**檔案**：`ChurchReport.MemberInfo.Tests/WebServiceConnector/DownloadListManagerIsolationTests.cs`
`RetrieveMemberListCollection_WhenOperationScopedServicesAreProvided_UsesOnlyCurrentService`（126-149 行）只驗證了 `RetrieveMemberListCollectionByListIdCrm2011` 這一個 facade 方法，未涵蓋 C1 提到的三個仍忽略 `service` 參數的 overload，也未涵蓋 C3（`SetupIntegrateData`）與 C4（fallback 到單例）路徑。整組新測試會全數通過，但 C1/C3/C4 仍會在既有測試套件下悄悄存在，建議後續 track A 補上對應回歸測試。

---

## Info 🟢

### I1. Slice D–H catalog 正確關閉 CE executor／consumer
`P72ContinuationLocalOnlyCatalog.cs:352-353`：13 筆定義全數 `CeExecutorEnabled = false`、`ConsumerEnabled = false`，符合 Invariant 3。

### I2. Fail-closed 語意有真實執行路徑測試佐證（非僅文件宣稱）
`SpeechMessage.Dynamics.Tests/Data8ProfileOperationExecutorTests.cs` 新增 `Execute_async_rejects_slice_d_to_h_local_only_operations_before_admission`：透過真正的 `Data8ProfileOperationExecutor` / `Data8ConnectorRouter` / `Data8ConnectorPool` 對全部 13 個 D–H operation ID 執行，斷言 `admission.AcquireCount == 0`、`admission.ReleaseCount == 0`、`factory.CreatedCount == 0`、`factory.DisposedCount == 0`，且回傳 `operation.not-supported`。這是本次唯一以「真正執行路徑」而非 metadata 標記證明 Invariant 4（profile/router/admission/lease/client 前 fail closed）的測試，判定為有效證據。

### I3. 週報關聯政策標記符合 Invariant 5 的分類要求
`P72ContinuationLocalOnlyCatalog.cs:347-349`：僅 `Attendance` slice 設為 `ZeroActiveUnlinkedOrExactlyOneLinked`，其餘固定 `NotApplicable`；enum 文件（90-97 行）正確描述 zero-active 不關聯、exactly-one 精確關聯、duplicate/unavailable fail closed 的語意。本次僅涵蓋 catalog metadata 層級的正確標記，實際 duplicate/unavailable 判斷邏輯（executor 端）不在本次 diff 範圍。

### I4. Live evidence 測試斷言放寬為合理架構決策，非缺陷
`ChurchReport.MemberInfo.Tests/LivePackage02Data8ListManagementEvidenceTests.cs:259` 由 `outcome.Should().Be("go", ...)` 改為 `outcome.Should().BeOneOf("go", "no-go")`。對照同一 PR 修改的 `docs/scripts/Invoke-Package02Data8ListManagementEvidence.ps1` 中 `Get-StrictSliceCChildFailureDiagnosticCategory` 文件更新（「非零 child exit 一律代表 process/resource lifecycle 不完整…完整發布的正常 no-go evidence 則必須由 child 以零結束」），此變更是刻意且一致的架構決策：把「child process 完整性」與「CE 業務結果分類」責任分離，讓 parent strict parser 能正確辨識合法 no-go 而非誤判為 child-process-failed。判定為合理修正，未削弱測試對真實錯誤的偵測力。

### I5. Matrix 路徑更新符合封存任務唯讀限制
`Invoke-Package02Data8ListManagementEvidence.ps1`：`$matrixPath` 已改指向 `.trellis\tasks\archive\2026-08\08-07-churchreport-write-action-function-migrations\p7.2-fixture-activation-matrix.json`，並附註「matrix 是 immutable、唯讀的 capability contract」，與 task.json 中 `predecessors` 列出的封存路徑一致，符合「不得重試已封存 P7.2 歷史/最終 CE cycle」的既有限制。

---

## 總結

本 release candidate 在**新增的 D–H 本機 capability 層**（catalog metadata、fail-closed 執行路徑測試、operation ID 索引）品質良好，Invariant 3、5 有確實證據；Invariant 4 有真正執行路徑測試佐證，但守門邏輯本身（C2）與宣稱不完全一致。**既有 ChurchReport 隔離修正**方向正確（移除了 `GetListManager` 中把 operation-scoped service 寫回單例欄位的行為），但範圍不完整且文件宣稱過度：C1（facade 三個 overload 仍忽略參數）、C3（`SetupIntegrateData` 路徑完全未接上）、C4（7/8 生產入口仍會落入單例 fallback）三項 Critical 顯示「單一 CRM service 生命週期隔離」尚未在此 release candidate 中真正達成，建議在標記為 release-ready 前，於 harden-churchreport-error-recovery 或本 continuation task 的後續 Track A 中補齊上述路徑並擴充對應回歸測試。

---
SESSION_ID: cfa30230-616c-4cfb-a124-f2685435baab

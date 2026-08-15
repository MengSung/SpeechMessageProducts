```diff
--- a/.trellis/tasks/08-05-gateway-purpose-and-positioning/task.json
+++ b/.trellis/tasks/08-05-gateway-purpose-and-positioning/task.json
@@ -22,3 +22,3 @@
-  "nextAction": "Commit and archive active 08-14-p7-memberinfo-request-local-authorization-scope. It created only a disabled fail-closed target-authorization contract, and must not be represented as a consumer, CE, feature-gate, traffic, P7.5 or P8 result. After archive, select the next independent matrix-backed P7 capability using the post-runtime-health 70-row matrix.",
+  "nextAction": "Archive the completed 08-14-p7-memberinfo-request-local-authorization-scope child, then create and start the next independently verifiable local-only P7.4 capability child (such as ORG-CALL-00063 weekly meeting statistics paging read) from the authoritative 70-row matrix backlog. Keep all feature gates false; do not start P7.5 removal or P8 until their immutable handoff gates are green.",
```

## 1. Analysis (架構評估)

### 當前架構狀態
根據權威的 70-row matrix（位於 `.trellis/tasks/archive/2026-08/08-14-p7-post-runtime-health-reconciliation/authoritative-gap-matrix.json`），目前的系統狀態如下：
- **Registry**: 28 declared / 13 local-only / 29 not-declared
- **Data8 Executor**: 27 implemented / 13 local-only-rejected / 30 not-implemented
- **ProductClient**: 27 implemented / 43 not-implemented
- **Consumer**: 3 migrated-disabled / 67 not-migrated
- **CE 9.1**: 6 succeeded / 50 evidence-pending / 13 not-executed / 1 historical no-go-closed
- **所有 Row 狀態**: 均為 `temporary-legacy`。

### 最新 Child 進展
最新的 child 任務 `08-14-p7-memberinfo-request-local-authorization-scope` 已經完成。它建立了一個獨立、唯讀、fail-closed 的 request-local 授權合約（`MemberInfoAccessResolver`），但尚未接上任何 consumer 或啟用 feature gate。這為後續需要伺服器端授權（server-derived authorization）的唯讀能力奠定了安全的邊界基礎。

---

## 2. Architecture Decision (架構決策)

基於 DTO-only、無 Session/InMemoryContext 依賴、無憑證 ListManager、無共享可變狀態、無 unbounded response 以及無寫入相鄰性（write adjacency）的嚴格安全限制，我們推薦以下三個最適合下一步的獨立 P7 capability：

### 推薦候選 1: `ORG-CALL-00063` (`stats.meeting.retrieve.by.sunday`)
- **Operation ID**: `stats.meeting.retrieve.by.sunday`
- **來源位置**: `ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeMetadataProvider.cs` (或相關的統計服務)
- **理由**: 
  - 它是唯讀的統計查詢，且已經有 implemented 的 Data8 executor 與 ProductClient。
  - 它是 DTO-only 且沒有 write adjacency。
  - 雖然它有 `paging-result` 的特殊資源需求，但它是 DTO-only 且沒有 write adjacency。在建立了 `memberinfo.request-local.authorization.scope` 之後，可以為其規劃 bounded weekly-meeting paging read family。
- **Rejected Alternatives**: 拒絕直接使用 legacy `Entity` 進行傳輸，必須使用強型別的 DTO。
- **Assumptions**: 假設分頁大小（page size）在 Data8 層級有嚴格的硬編碼限制（例如 32-row/32-KiB），以防止 unbounded response。
- **Potential Side Effects**: 無，因為 feature gate 預設為關閉。

### 推薦候選 2: `ORG-CALL-00006` (`fee.dedication.retrieve.by.contact.date.range`)
- **Operation ID**: `fee.dedication.retrieve.by.contact.date.range`
- **來源位置**: `ChurchReport/Services/DonationFeeQueryService.cs`
- **理由**: 
  - 它是唯讀的奉獻費用查詢，且已經有 implemented 的 Data8 executor 與 ProductClient。
  - 它是 DTO-only 且沒有 write adjacency。
  - 它的 `consumer` 狀態是 `migrated-disabled`，適合做為下一步的接線與驗證。
- **Rejected Alternatives**: 拒絕在未經過 `MemberInfoAccessResolver` 授權的情況下直接解析 contact ID。
- **Assumptions**: 假設傳入的日期範圍有最大區間限制，防止拉取過大數據集。
- **Potential Side Effects**: 無。

### 推薦候選 3: `ORG-CALL-00061` / `ORG-CALL-00062` (`lessons.stor.retrieve.by.contact` / `lessons.stor.retrieve.by.disciplelesson`)
- **Operation ID**: `lessons.stor.retrieve.by.contact` / `lessons.stor.retrieve.by.disciplelesson`
- **來源位置**: `ChurchReport/Services/StorLessonQueryService.cs`
- **理由**: 
  - 它是唯讀的課程查詢，且已經有 implemented 的 Data8 executor 與 ProductClient。
  - 它是 DTO-only 且沒有 write adjacency。
  - 它的 `consumer` 狀態是 `migrated-disabled`，適合做為下一步的接線與驗證。
- **Rejected Alternatives**: 拒絕使用 legacy SDK `EntityCollection` 進行 rehydration。
- **Assumptions**: 假設課程資料為唯讀且不涉及 Session 狀態變更。
- **Potential Side Effects**: 無。

---

## 3. Implementation Plan (實作計畫)

以 **`ORG-CALL-00063`** 為例，下一步的實作步驟如下：

### Step 1: 建立獨立的 P7.4 Child Task
- 建立 `08-14-p74-memberinfo-weekly-meeting-stats-read-boundary`。
- 保持所有 feature gates 預設為 `false`。

### Step 2: 定義 Bounded Paging DTO 與 Interface
- 在 `SpeechMessage.Dynamics.Abstractions` 中定義唯讀的 DTO 與 client 介面，限制最大回傳筆數。

```csharp
// Pseudo-code for DTO
public record WeeklyMeetingStatsDto(
    Guid MeetingId,
    DateTime SundayDate,
    int AttendanceCount,
    string MeetingType
);

public interface IPackage03WeeklyMeetingStatsClient
{
    Task<IReadOnlyList<WeeklyMeetingStatsDto>> GetStatsBySundayAsync(
        DateTime sundayDate, 
        CancellationToken cancellationToken
    );
}
```

### Step 3: 實作 Data8 Executor 與 ProductClient
- 在 `SpeechMessage.Dynamics.Connectors.Data8` 中實作 Data8 查詢，使用固定的 QueryExpression，不允許動態 FetchXML。
- 在 `SpeechMessage.Dynamics.ProductClient` 中實作 DTO 轉換與防禦性複製。

### Step 4: 接入 ChurchReport 並進行本機驗證
- 在 `ChurchReport` 中，將 `MemberInfoCommitmentTypeMetadataProvider` 的呼叫點改為使用 `IPackage03WeeklyMeetingStatsClient`。
- 撰寫單元測試與整合測試，驗證在 gate=false 時走 legacy 流程，gate=true 時走新流程且具備正確的授權與隔離。

---

## 4. Considerations (考量與不安全候選分析)

### Warning: Stale Parent Task Metadata
- **檔案路徑**: `.trellis/tasks/08-05-gateway-purpose-and-positioning/task.json`
- **符號名稱**: `nextAction`
- **說明**: parent task 的 `nextAction` 仍指向已完成的 `08-14-p7-memberinfo-request-local-authorization-scope`，需要更新為指向下一個獨立的 P7.4 child。
- **安全守則**: 絕對不可提早宣稱 P7.5 或 P8 可做，因為所有 70 個 matrix rows 仍是 temporary-legacy，且 legacy source/project/settings 依然存在。

### 不安全候選的 Fail-Closed 原因分析
1. **`ORG-CALL-00064` (`fees.retrieve.by.dedication.period`)**:
   - **Fail-Closed 原因**: 它與 recurring payment writer 相鄰，不能 read-new/write-legacy 混接。如果直接遷移，會造成 write adjacency 的安全缺口。
2. **`ORG-CALL-00030` (`memberinfo.contact.update.basic.info`)**:
   - **Fail-Closed 原因**: 它是寫入操作（`kind: write`），且 legacy composite 有四個欄位，typed contract 只覆蓋部分欄位，如果直接遷移會造成 partial wiring，導致 Gateway 與 ToolUtility 狀態不一致。
3. **`ORG-CALL-00031` / `00032` / `00033`**:
   - **Fail-Closed 原因**: 這些都是 `memberinfo` 相關的查詢，但它們與 Session/InMemoryContext 耦合，且 Shepherd list scope 可能在 provable immutable request-local authorization boundary 以前調用 saved-credential shared ListManager loader。
4. **`ORG-CALL-00052` (`contact.current.group.retrieve`)**:
   - **Fail-Closed 原因**: 它與 Session 耦合，且目前小組讀取與名單異動、出席耦合。
5. **`ORG-CALL-00060` (`payments.contact.resolve.for.dedication.form`)**:
   - **Fail-Closed 原因**: 它與 Session、`InMemoryContext`、可變 manager/form 與 CRM SDK `Entity` 耦合。

# ChurchReport 跨產品資料發布與網路時序防護設計分析報告

## 執行摘要 (Executive Summary)

本分析針對 ChurchReport 初始週報從 `ListManager`、Session Holder、`SmallGroupController` / `NewPersonController` 到前端 DevExtreme Grid 的完整資料流進行了嚴格的唯讀與架構審查。重點解決慢速 Wi-Fi 或防火牆高延遲環境下，偶發「同一資料在畫面重複顯示兩次，但資料庫實質僅有一筆 `PresentRecordId`」之問題。

依據規格與現有程式碼，我們**嚴禁**按 FullName 或內容去重（以免誤刪同名不同 ID 的真實會友），亦**嚴禁**為持久化資料隨機產生前端 Key。報告說明了後端 Publish Guard 與前端 Generation/Lifecycle Coordinator 的最佳組合，並對過度設計與相容性風險給出明確修正建議。

---

## 1. 系統性 UX 與架構分析 (Analysis Framework)

### 1.1 User Impact Assessment (使用者體驗影響評估)
- **競態重複渲染點**: 使用者在慢速網路、無線網路斷續或防火牆攔截後重試時，因為圖表與 Grid 同時發起 HTTP 請求，或切換日期/小組時未終止前次請求，導致先後返回的二個 Response 在前端未經過 Version/Generation 驗證即被注入 Grid，產生視覺上的「同名同姓重複資料」。
- **使用者旅程障礙**: 若按姓名去重（如 `DistinctBy(x => x.FullName)`），將導致教會中真實同名同姓的兩位會友被隱蔽，引發會友出席與奉獻紀錄歸屬錯誤的嚴重業務風險。
- **無障礙與行動端體驗**: 網路波動時重複 Render 會造成畫面抖動 (Layout Shift)，行動裝置在慢速 Wi-Fi 下重複執行 DOM 重繪亦會導致消耗過多電池與記憶體。

### 1.2 Design System Evaluation (設計系統與一致性評估)
- **Key Identity 原則**: DevExtreme DataGrid 必須統一指定 `.Key("PresentRecordId")` 作為 Row Key。`PresentRecordId` 必須是後端持久化的 stable GUID，不得在前端生成假 ID。
- **UI Coordinator 模式**: 前端採用 neutral 的 `CollectionLoadCoordinator` 封裝，與 DevExtreme WebApi DataSource 整合，維護單一 mount owner、generation token 與 bounded refresh，不打破現有 View 與 Controller 之間的 DevExtreme 配置模式。

### 1.3 Frontend Architecture (前端與後端架構評估)
- **無第二條取數管線**: 拒絕建立獨立於 DevExtreme 之外的額外 `fetch()` 管線。DevExtreme DataGrid 自身的 `DataSource` (WebApi) 已處理 Paging/Sorting/Editing，應以 CustomStore / WebApi 攔截器注入 Generation token，而非另建資料管線。
- **狀態與生命週期邊界**: 後端必須保證 `ListManager` 在被讀取時始終回傳獨立且不可變的 `DetachedReadCopy`；前端在 View 卸載或重新載入（Ajax Partial Page Updates）時必須確實呼叫 `dispose()` 清理運作中的 AJAX 請求與事件監聽。

---

## 2. 核心 Task 題號深度解答 (Items 1 - 7)

### 2.1 實際可造成相同 `PresentRecordId` 重複發布或重複渲染的競態／生命週期缺口

| 缺口編號 | 檔案與符號 | 競態與生命週期缺口說明 |
|---|---|---|
| **Gap-1** | `SpeechMessageProducts.ChurchReport/Views/Home/_GeneralGroupGrids.cshtml` (DevExtreme `.WebApi()` 配置) | **前端無 Generation Token 與 Request Abort 防線**：當使用者在慢速 Wi-Fi 或高延遲環境下快速點擊刷新或切換日期，DevExtreme DataGrid 發出多次 `/SmallGroup/LoadIntegrate` GET 請求。若舊請求比新請求晚到達，且無 generation token 進行遮蔽與比對，過期回應將覆蓋新資料或觸發重複 ContentReady 事件導致重複渲染。 |
| **Gap-2** | `SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadIntegrateData.Setup.cs` (及 `DownloadIntegrateData.cs`) 的 `SetupHeaderData` | **中間狀態提前設置 `LoadFlag = true`**：在載入成員清單（`SetupWeeklyReportData`）完成前，標籤 `LoadFlag` 即被設為 `true`。若網路中斷拋出例外，`LoadFlag` 保持為 `true`。後續請求將誤認為資料已載入完成，直接把不完整或半套資料作為 Snapshot 端給前端。 |
| **Gap-3** | `SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs` (`EnsureCorrectUserData()`) | **Session 帳密校驗引發併行 `ListManager` 覆寫**：`EnsureCorrectUserData` 於 `LoadIntegrate` / `LoadNewPersonFollowUp` 入口無鎖執行，若偵測到 Session 與 `ListManager` 帳密不符會觸發 `SetupListManager`，在讀取過程中突變 `m_MultiGroupList` 與 `ActiveListId` 欄位。 |
| **Gap-4** | `SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Date.cs` (`UpdateIntegrateDate`) | **日期切換跨多步寫入無全局鎖保護**：清快取 → 寫 `m_SelectDate` → 呼叫 `SetupIntegrateData` 之間存在時間差。若併行打入 `LoadIntegrate`，會讀到「新日期配舊小組成員」的混亂 state。 |
| **Gap-5** | `SpeechMessageProducts.ChurchReport/Views/Home/IntegrateView.cshtml` 與 Ajax Partial 重新掛載 | **DOM 重新掛載（Re-mount）缺口**：Partial View 重新載入時未執行上一代 Grid 的 `dispose()`，舊的 Event Handlers 與懸空 (Dangling) AJAX Callbacks 仍作用於新 DOM，產生多次觸發與重複渲染。 |

---

### 2.2 現有已生效之防線 (避免破壞既有正確設計)

1. **`ListManager` Instance 級鎖防線 (`m_IntegratePublicationGate`)**:
   - `SpeechMessageProducts.ChurchReport/Models/ListManager.cs:330` 的 `lock (m_IntegratePublicationGate)` 保證單一 `ListManager` 在建立 `IntegrateLoadKey`、比對 Scope、驗證候選資料與發布 `m_ListSmallGroupWeeklyReport` 時具備原子性。
2. **`ValidateIntegrateCandidate` 與 `ValidateUniqueRowKeys` 校驗**:
   - `SpeechMessageProducts.ChurchReport/Models/ListManager.cs:391,408` 在 Snapshot 發布前，使用 `HashSet<string>(StringComparer.OrdinalIgnoreCase)` 檢視 `Members` 集合。若存在非空的重複 `PresentRecordId`，立即拋出 `InvalidOperationException` Fail-Closed，防止污染 Session 共享 Snapshot。
3. **`CreateDetachedReadCopy()` 隔離與深複製防線**:
   - `SpeechMessageProducts.ChurchReport/Models/ListSmallGroupWeeklyReport.cs:132` 在回傳給 Controller 時進行 `CreateDetachedReadCopy()`，讓 Controller 與 `DataSourceLoader` 使用獨立的新物件與清單，不直接枚舉 Session 內的突變 List。
4. **DevExtreme Row Key 指定 (`.Key("PresentRecordId")`)**:
   - `SpeechMessageProducts.ChurchReport/Views/Home/_GeneralGroupGrids.cshtml:267` 已設定 `.Key("PresentRecordId")`，提供 DevExtreme 依據主鍵追蹤列的依據。

---

### 2.3 最小且可測試的後端 Consumer-Boundary Guard 設計

- **核心 Guard 類別**:
  建立無狀態、純單元測試可涵蓋之靜態 Guard：
  ```csharp
  public static class RowPublicationGuard
  {
      public static IEnumerable<T> GuardConsumerBoundary<T>(
          IEnumerable<T> candidateRows,
          Func<T, string> keySelector,
          string consumerName)
      {
          if (candidateRows == null) return Enumerable.Empty<T>();
          var list = candidateRows as IList<T> ?? candidateRows.ToList();
          var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

          foreach (var row in list)
          {
              var key = keySelector(row);
              if (string.IsNullOrWhiteSpace(key))
              {
                  throw new InvalidOperationException($"[{consumerName}] 含有空白 Stable Key (PresentRecordId)。");
              }
              if (!seen.Add(key.Trim()))
              {
                  throw new InvalidOperationException($"[{consumerName}] 偵測到重複發布之 PresentRecordId: '{key.Trim()}'。");
              }
          }
          return list;
      }
  }
  ```
- **配置位置**:
  在 `SmallGroupController.LoadIntegrate` 與 `NewPersonController.LoadNewPersonFollowUp` 回傳給 `DataSourceLoader.Load` 之前的最後關卡調用：
  ```csharp
  var snapshot = InMemoryContext.ListManager.EnsureAndGetIntegrateDetachedRead(id);
  var tasks = snapshot.m_SmallGroupDataList.m_SmallGroupData.Members;
  RowPublicationGuard.GuardConsumerBoundary(tasks, m => m.PresentRecordId, "LoadIntegrate");
  return DataSourceLoader.Load(tasks, loadOptions);
  ```

---

### 2.4 最小且可測試的前端 Single-Owner, Generation Token, Bounded Refresh, Dispose 設計

**原則：不得建立第二條取數管線**，須直接擴充/裝飾 DevExtreme 現有 `DataSource` 或全域 Loader Coordinator。

```javascript
// CollectionLoadCoordinator.js
window.CollectionLoadCoordinator = (function() {
    function Coordinator(containerId) {
        this.containerId = containerId;
        this.generation = 0;
        this.requestToken = 0;
        this.activeXhr = null;
        this.loading = false;
        this.pendingRefresh = false;
        this.mounted = true;
    }

    Coordinator.prototype.nextGeneration = function() {
        this.generation++;
        this.requestToken = 0;
        if (this.activeXhr) {
            this.activeXhr.abort();
            this.activeXhr = null;
        }
        this.loading = false;
    };

    Coordinator.prototype.beginRequest = function(xhr) {
        if (!this.mounted) return false;
        this.requestToken++;
        this.activeXhr = xhr;
        this.loading = true;
        return { token: this.requestToken, gen: this.generation };
    };

    Coordinator.prototype.isCurrent = function(guardState) {
        return this.mounted && 
               guardState && 
               guardState.gen === this.generation && 
               guardState.token === this.requestToken;
    };

    Coordinator.prototype.endRequest = function(guardState, gridInstance) {
        this.loading = false;
        this.activeXhr = null;
        if (this.pendingRefresh && this.mounted) {
            this.pendingRefresh = false;
            if (gridInstance) gridInstance.refresh();
        }
    };

    Coordinator.prototype.dispose = function() {
        this.mounted = false;
        this.nextGeneration();
        var $elem = $('#' + this.containerId);
        if ($elem.length && $elem.data('dxDataGrid')) {
            $elem.dxDataGrid('instance').dispose();
        }
    };

    return {
        getOrCreate: function(containerId) {
            var $elem = $('#' + containerId);
            var instance = $elem.data('loadCoordinator');
            if (instance) {
                instance.dispose(); // 重複 Mount 時清空前代
            }
            instance = new Coordinator(containerId);
            $elem.data('loadCoordinator', instance);
            return instance;
        }
    };
})();
```

---

### 2.5 Session / Memory / Resource Leakage 風險與確定 Cleanup 要求

1. **Session Leakage 風險與清理**:
   - **風險**: 若 `ListManager` 將快取的成員清單直接開放引用或使用 static 字典儲存使用者資料，跨 Session 的 User A 可能讀到 User B 的資料。
   - **Cleanup 要求**: 必須維持 `EnsureAndGetIntegrateDetachedRead` 回傳 `CreateDetachedReadCopy()` 的做法；Controller 內嚴禁儲存任何長生命週期的成員指標。
2. **Memory Leakage 風險與清理**:
   - **風險**: DOM 節點因 Ajax 替換被移除，但 DevExtreme Widget 實例與 jQuery 事件監聽器未解綁，滯留在記憶體中。
   - **Cleanup Requirement**: 在 View 銷毀或 AJAX reload 前，必須明確呼叫 `$(container).dxDataGrid('instance').dispose()` 並調用 `coordinator.dispose()`。
3. **Resource Leakage 風險與清理**:
   - **風險**: 網路卡住時發出的 HTTP XHR 請求、`CancellationTokenRegistration` 與 `Timer` 未正確被 Abort 或 Dispose。
   - **Cleanup Requirement**: 在前端發起新 Request 或 View Unmount 時，`activeXhr.abort()` 必須被執行；後端使用 `CancellationToken` 綁定 Timeout。

---

### 2.6 TDD 測試矩陣 (Test Matrix)

| 編號 | 測試案例名稱 | 測試條件與輸入 | 預期結果 (Assert Criteria) |
|---|---|---|---|
| **T1** | 同名不同 ID 測試 (Same Name, Diff ID) | 建立 2 筆 Member，FullName 均為 `"張三"`，`PresentRecordId` 分別為 `"ID-001"`、`"ID-002"`。 | `RowPublicationGuard` 驗證通過，回傳 2 筆資料，兩位張三均獨立存在且可在 Grid 渲染與編輯。 |
| **T2** | 相同 ID 衝突測試 (Same ID Conflict) | 建立 2 筆 Member，`PresentRecordId` 均為 `"ID-001"`。 | `RowPublicationGuard` 拋出 `InvalidOperationException`；拒絕發布該 Candidate，之前的 Snapshot 保持不受影響。 |
| **T3** | 回應亂序測試 (Out-of-Order Network Response) | 前端發起 Gen=1 請求，網路延遲；隨後發起 Gen=2 請求並先返回；Gen=1 隨後返回。 | `CollectionLoadCoordinator.isCurrent()` 判定 Gen=1 已經 Stale，拒絕將 Gen=1 資料寫入 Grid DOM。 |
| **T4** | 重複 Mount 測試 (Duplicate Mount) | 對同一 DOM containerId 連續呼叫二次初始化 Coordinator 與 DataGrid。 | 第一次的 Coordinator 被自動 Dispose，運作中的 AJAX 被 Abort，無雙重監聽器，僅保留最新實例。 |
| **T5** | A/B Session Isolation 測試 | Session A (帳號 A) 與 Session B (帳號 B) 同時發起 `EnsureAndGetIntegrateDetachedRead`。 | 兩 Session 的 CandidateFactory 與 Scope Key 互不干涉，且回傳的二份 Detached Read 物件指標互異。 |
| **T6** | 取消與 Resource Drain 測試 | 在 API 載入中途引發 Cancellation，並重複進行 50 次載入與銷毀。 | 運作中 Handers、Timers、Memory 趨勢平穩歸零，不產生記憶體攀升或未釋放之 Handle。 |

---

### 2.7 修正規劃中之過度設計、相容性風險與錯誤假設

1. **修正「按 FullName / 內容去重」的錯誤假設**:
   - **錯誤假設**: 某些舊報告或計畫建議在 Controller 採用 `.GroupBy(x => x.FullName).Select(g => g.First())` 止血。
   - **修正理由**: 違反業務真實邏輯。同名同姓在教會系統中為合情合理之現象。去重必須且僅能以持久化的 `PresentRecordId` 為依據。
2. **修正「建立第二條 AJAX 取數管線」的過度設計**:
   - **過度設計**: 試圖繞過 DevExtreme，手寫 `fetch()` AJAX 管線拿 JSON 再塞入 DataGrid。
   - **修正理由**: 破壞 DevExtreme 內建的分頁、排序、篩選與更新機制。應直接使用 `CollectionLoadCoordinator` 整合於現有 WebApi DataSource 與 LoadOptions 流程中。
3. **修正「前端遇到 Empty Key 隨機 generate Guid」的相容性風險**:
   - **相容性風險**: 前端若針對 `PresentRecordId` 為空的持久化列自動生成 `Guid.NewGuid()`。
   - **修正理由**: 這會導致每次 Render 的 Key 都不一樣，導致 DevExtreme inline editing/delete 失效。後端必須在 Candidate 階段保證 `PresentRecordId` 非空且唯一，若為草稿必須由後端分配 Server-owned Draft Key。

---

## 3. Review Findings 診斷清單 (Critical / Warning / Info)

### Critical Findings (嚴重風險)

- **CRITICAL-1: 缺乏前端 Generation Token 與 Request Abort 防線**
  - **檔案**: `SpeechMessageProducts.ChurchReport/Views/Home/_GeneralGroupGrids.cshtml:263`
  - **理據**: 網路延遲時，慢速 GET 回應會覆蓋新請求，造成畫面出現重複列。必須引入 `CollectionLoadCoordinator` 管理 Generation Token 與 Cancel In-Flight Requests。
- **CRITICAL-2: `DownloadIntegrateData` 中間狀態提前設定 `LoadFlag = true`**
  - **檔案**: `SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadIntegrateData.Setup.cs`
  - **理據**: 成員載入未完成前即將 `LoadFlag` 置為 `true`，遇到異常時 Session 留存半成品，重試時只讀到缺損資料。必須改為 Candidate 全數載入與 Guard 驗證通過後才原子化設為 `true`。

### Warning Findings (警告風險)

- **WARNING-1: `EnsureCorrectUserData` 無鎖修改共享 `ListManager`**
  - **檔案**: `SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs:788`
  - **理據**: Session 密碼不符時發起 `SetupListManager`，在併行請求中會突變 `ListManager` 內部欄位。應納入 Scope Guard 或鎖保護。
- **WARNING-2: Ajax Partial Update 未呼叫 DataGrid `dispose()`**
  - **檔案**: `SpeechMessageProducts.ChurchReport/Views/Home/IntegrateView.cshtml`
  - **理據**: 畫面切換與 Partial 重新渲染時未銷毀上一代 Grid 實例，累積懸空監聽器與記憶體洩漏風險。

### Info Findings (參考資訊)

- **INFO-1: `docs/publication-contracts.json` 契約宣告清單**
  - **檔案**: `docs/publication-contracts.json` (建議新增)
  - **理據**: 建議完整記錄 ChurchReport `GET /SmallGroup/LoadIntegrate` 之 Endpoint、View、Identity 欄位 (`PresentRecordId`) 與極限列數 (10000)，利於 CI 靜態契合度檢查。

---

## 4. 結論與建議方案 (Recommendation)

建議採取 **後端嚴格 Fail-Closed Guard + 前端 Single-Owner Coordinator** 雙層防線：

1. **後端方案**:
   - 保留現有 `ListManager` 的 lock 與 `CreateDetachedReadCopy()`。
   - 在 Controller 邊界注入 `RowPublicationGuard.GuardConsumerBoundary` 作為第二道防線，明確驗證 `PresentRecordId` 唯一性。
2. **前端方案**:
   - 導入無額外管線負擔的 `CollectionLoadCoordinator`，以裝飾模式為 DevExtreme WebApi DataSource 加上 Generation Token 與 request cancellation，確保存續期間單一 Owner，並於 View unmount 時確定呼叫 `dispose()`。

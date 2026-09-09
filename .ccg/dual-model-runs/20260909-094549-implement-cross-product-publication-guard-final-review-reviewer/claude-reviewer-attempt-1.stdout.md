## Review: 跨產品資料發布防重複與網路時序防護（最終審查）

### Critical 🔴
無。

### Warning 🟡
- **`SpeechMessageProducts.ChurchReport/Models/ListManager.cs:416-419`** consumer 診斷名稱與本輪剛修正的 `RowPublicationGuard` 常數命名方案不一致，且未共用常數來源
  - Why：本輪修正只把 `docs/publication-contracts.json`、`RowPublicationGuard.SmallGroupGridConsumerName` / `NewPersonGridConsumerName`、controllers 與 `PublicationContractManifestTests.cs` 統一為 `ChurchReport.WeeklyReport.SmallGroupGrid` / `...NewPersonGrid`。但 `ListManager.ValidateIntegrateCandidate`（`EnsureAndGetIntegrateDetachedRead` 的資料載入層防線，早於 Controller 層 `RowPublicationGuard.ValidateRows` 再次驗證）仍用寫死字面量 `"ChurchReport.WeeklyReport.SmallGroup"` / `"...NewPerson"`（無 `Grid` 尾碼），且 `HappyGroup` / `AllMembers` 兩個資料集（`ListManager.cs:418-419`）完全沒有對應的 Controller 層 `RowPublicationGuard.ValidateRows` 呼叫或常數 — `ListManager` 的這道檢查是它們唯一的重複 ID 防線。這正好是 `RowPublicationGuard.cs:24-27` 自己文件註解宣告要避免的情況：「集中管理可避免 manifest、API 與測試在重構後各自保留不同文字，導致錯誤診斷無法對應到實際 consumer」——但集中常數目前只被 Controller 層引用，`ListManager` 這一層仍游離在外。
  - Fix：在 `RowPublicationGuard` 新增 `SmallGroupDatasetName` / `NewPersonDatasetName`（或直接複用/统一到 `SmallGroupGridConsumerName` 等既有常數，若語意等價），並讓 `ListManager.cs:416-417` 改引用常數，避免未來重新命名任一層時另一層診斷字串各自漂移。`HappyGroup` / `AllMembers` 的名稱亦建議收斂進同一組常數集中管理。
  - 已確認：`ChurchReport.MemberInfo.Tests` 內沒有任何測試斷言 `ListManager` 這組舊式名稱，此處目前完全無測試覆蓋，因此命名漂移不會被自動抓到。

### Info 🟢
- **`docs/publication-contracts.json` / `RowPublicationGuard.cs` / controllers / `PublicationContractManifestTests.cs`**：上一輪兩項 Warning 已確實修正 — manifest consumer 名稱、`RowPublicationGuard` 新增常數、`NewPersonController.cs:136`、`SmallGroupController.DataApi.cs:141` 呼叫點與測試斷言三方完全一致，且用集中常數取代散落字面量。
- **`_GeneralGroupGrids.cshtml:30-78`**：初始化失敗路徑已補上 `try/catch`，`catch` 區塊會（1）呼叫 `coordinator.dispose()` 清理已建立的 coordinator，（2）以 `console.error` 記錄不含資料列／Session／credential 的診斷訊息，（3）`throw error` 讓例外持續往外拋、不做任何 fallback 到未包裝的 `store.load`。程式碼結構本身完全符合「fail closed、不得回退未防護 store.load」的要求。唯一保留的殘餘風險（非本次診斷可證明）是：`store.load` 只有在 `coordinator.mount` 成功、且緊接的兩行同步指定完成後才會被換成受保護版本；若拋錯發生在拿到 `store` 之前（例如 `component`／`owner`／`store.load` 缺失），`store.load` 本來就還是 DevExtreme 原生版本、從未被本程式碼包裝過，因此嚴格說不是「回退」而是「從未接管」，語意上仍符合契約要求，僅供記錄。
- **encoding 檢查**：本次變更的 5 個 `.cs`/`.cshtml` 檔案（`PublicationContractManifestTests.cs`、`NewPersonController.cs`、`SmallGroupController.DataApi.cs`、`RowPublicationGuard.cs`、`_GeneralGroupGrids.cshtml`）均為 UTF-8 without BOM、全 CRLF 換行、檔尾為 CRLF，符合契約。`docs/publication-contracts.json` 非 `.cs`/`.cshtml`，不在此編碼契約範圍內（目前是 LF、無 BOM，Git 設定會在下次觸碰時轉為 CRLF，屬既有行為，非本次引入的問題）。
- **cache-hit revalidate / instance synchronization root**：`ListManager.cs:341-376` 的 `EnsureAndGetIntegrateDetachedRead` 確認 cache-hit 路徑（`m_ListSmallGroupWeeklyReport` 命中既有 key）仍會在同一個 `m_IntegratePublicationGate` 鎖內執行 `CreateDetachedReadCopy()` + `ValidateIntegrateCandidate()` 重新驗證，未公開 Session-owned mutable graph；此部分未受本次 diff 影響，維持既有正確行為。
- 既有 Payment naming/source-inspection 測試失敗與本次 diff 無關，本輪未見任何新增回歸。

### Summary
上一輪點出的兩項 Warning（manifest consumer 命名一致性、Grid 初始化失敗 fail-closed）皆已在本次 diff 中確實修正，且有測試與程式碼互相印證，可視為完成。本輪額外發現一項 Warning：`ListManager.cs` 內部資料載入層的重複 ID 驗證仍使用舊式、未進常數化的 consumer 名稱（`ChurchReport.WeeklyReport.SmallGroup`/`NewPerson`，無 `Grid` 尾碼），與本次統一到 `RowPublicationGuard` 常數的命名方案不一致，且完全無測試覆蓋，`HappyGroup`/`AllMembers` 兩個資料集也只靠這條未常數化的路徑防護。建議後續任務收斂為同一組常數來源。不影響本次 diff 的核心正確性（唯一性判斷本身依然以 `PresentRecordId` 為準、fail closed 邏輯未變），故不判定為 Critical。整體判定：**Approve（可合併），但建議追蹤修正上述命名一致性 Warning**。

---
SESSION_ID: 55ce8e6a-94b5-4cdf-a9a7-180bc283ddf7

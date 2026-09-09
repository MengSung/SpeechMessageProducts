Now I have all the evidence needed. Here is the review.

##審查：跨產品資料發布防重複與網路時序防護（工作樹 diff vs HEAD）

**本機重跑驗證**：已重新執行 `dotnet test`（22/22 通過）、`node --test`（5/5 通過）、`dotnet build -c Release`（0 錯誤；`Line.Messaging` 專案有 1 個既有 XML 註解警告，與本次 diff 無關）。核心契約（PresentRecordId 唯一性、fail-closed、detached snapshot 重驗證、instance gate 原子性、JS generation 防護）逐一比對原始碼與測試後，判定如下。

### Critical 🔴
無。已檢視 `ListManager.EnsureAndGetIntegrateDetachedRead`（`ListManager.cs:334-377`）、`RowPublicationGuard`、`SmallGroupData.InsertMember`、`SmallGroupDataList.AddNewPersonToMember` 的鎖與驗證順序，未發現资料以姓名/內容去重、部分發布或鎖序反轉造成死結的情形；鎖序恆為 `m_IntegratePublicationGate → SmallGroupDataList._syncRoot`（單向巢狀，且 `lock` 可重入），未見任何路徑反向取得。

### Warning 🟡

1. **`_GeneralGroupGrids.cshtml:16-33` 的 `initializeWeeklyReportGridPublicationGuard` 在初始化路徑上以 `throw new Error(...)` 中止，缺乏可觀察的降級策略**
   - 情境：若這個 DevExtreme 版本／設定下 `component.getDataSource()`／`dataSource.store()`／`store.load` 的形狀與假設不符（例如未來改用非 WebApi store，或 DevExtreme 升版後事件時序改變 `getDataSource()` 尚未就緒），`OnInitialized` 回呼會同步擲例外。由於這是頁面首次載入 Grid 的必經路徑，且函式為 `window` 全域函式同時服務兩個 Grid（`SmallGroupgridContainer`、`NewPersonGridContainer`），一旦假設不成立，整頁週報 Grid 可能無法初始化，且沒有任何 fallback 或使用者可見錯誤訊息。
   - 違反契約：第 6 點（資源生命週期防護不應以「讓功能整頁失效」為代價）與一般前端穩健性慣例；目前僅有 Node 對 `collection-load-coordinator.js` 模組本身的單元測試（5/5），**未見任何以真實瀏覽器或 DevExtreme mock 驗證 `OnInitialized`/`OnDisposing` 掛接時序的測試**，因此這個整合面向無法由目前測試證明安全。
   - 建議：至少加上 try/catch 並在初始化失敗時記錄可診斷訊息、回退為「不掛世代防護、直接使用原生 `store.load`」而非讓整個 Grid 初始化失敗；並補一個以 devextreme 或簡化 stub 驗證 `OnInitialized`/`OnDisposing` 真正被呼叫且順序正確的整合測試。

2. **`docs/publication-contracts.json` 的 `consumer` 命名與程式碼實際傳給 `RowPublicationGuard`/`ValidateUniqueRowKeys` 的 `consumerName` 字串不一致，且沒有測試互相校驗**
   - 情境：manifest 登記 `WeeklyReport.SmallGroup`、`WeeklyReport.NewPerson`；但 `SmallGroupController.DataApi.cs:141`、`NewPersonController.cs:136` 實際傳入 `"ChurchReport.WeeklyReport.SmallGroupGrid"`、`"ChurchReport.WeeklyReport.NewPersonGrid"`；`ListManager.cs:416-419` 又用第三種 `"ChurchReport.WeeklyReport.SmallGroup"`/`"...NewPerson"`/`"...HappyGroup"`/`"...AllMembers"`。`PublicationContractManifestTests.cs` 只驗證 JSON schema 自洽與 `contractTestSuite` 型別存在，**並未斷言 manifest 的 `consumer` 對應到程式碼中實際使用的診斷字串**。
   - 違反契約：第 9 點「請注意…產品清冊契約」的意圖是讓 manifest 成為可被自動化檢查的實際契約入口，但目前 manifest 與程式碼可各自漂移而不會被任何測試發現（三套字串沒有單一事實來源）。
   - 建議：至少讓 manifest 的 `consumer` 欄位與其中一組實際傳入 `RowPublicationGuard` 的常數字串一致，或在測試中建立反射/字串常數比對，避免未來重構時 silently drift。

### Info 🟢

- **`NewPersonController.cs:172-188`（`InsertNewPresentRecord`）縮排損壞**：方法簽章與 `try {` 被改為 4 個空白縮排，但方法內其餘敘述（`return Ok();`、`catch`、收尾 `}`）仍維持原本 8/12/16 空白縮排，造成同一方法內縮排層級不一致。雖不影響編譯（已用 build 驗證），但明顯降低可維護性，且容易讓後續維護者誤判大括號配對層級，建議以編輯器格式化工具修正。
- **`IntegrateView.cshtml:1`**：檔案開頭原本的 UTF-8 BOM（`﻿@*`）被移除為 `@*`。ASP.NET Core 對 `.cshtml` 不依賴 BOM，功能不受影響，但與同專案其他既有 `.cshtml` 檔案編碼慣例不一致，建議之後統一（此 diff 中的新檔案 `.cs`/`.js` 皆明確聲明「UTF-8 without BOM」，但既有 `.cshtml` 過去慣例為含 BOM，二者現在不一致）。
- **`SmallGroupController.DataApi.cs:138-144`、`NewPersonController.cs:133-139` 對已由 `EnsureAndGetIntegrateDetachedRead` 驗證過的同一份 snapshot 再次執行 `RowPublicationGuard.ValidateRows`**：這是刻意的縱深防禦（comment 已說明「即使來源快照在未來維護中被某個 CRUD 路徑錯誤 append」），設計正確、非缺陷，僅供記錄：屬於已知且已註解說明的效能取捨，不需修改。

### 殘餘風險（無法僅由靜態審查證明）

1. 上述 Warning 1：DevExtreme `OnInitialized`/`OnDisposing` 掛接時序與 `getDataSource()/store()` 形狀假設，僅能由實際瀏覽器操作驗證，目前無此類測試/操作證據。
2. `EnsureCorrectUserData()`（`BaseChurchController.cs:715` 起，本次 diff 未修改其實作，只是新增呼叫點於 `InsertPresentRecord`/`InsertNewPresentRecord`）內部的 30 秒驗證快取與 LINE 票證復原邏輯屬既有程式碼；若其快取命中邏輯與本次新增的 Session-scope 前置檢查交互作用產生非預期結果（例如快取命中但底層 ListManager 已被另一 request 换掉 Session），本次審查僅能確認呼叫順序正確，無法純靠靜態分析排除所有既有邏輯的邊界案例。
3. `AddNewPersonToMember`（`SmallGroupDataList.cs:388-496`）目前對四個 collection 做「任一存在即拒絕」的重複檢查，與 `SmallGroupData.InsertMember`（單一 collection 檢查）是兩條獨立寫入路徑；若未來業務流程讓同一筆新人透過兩種路徑先後寫入，理論上可能在跨路徑情境下產生非預期的 fail-closed 或遺漏檢查，目前程式與測試均假設兩條路徑處理不同來源資料、不會交錯，此假設無法由本次 diff 之測試證明或反證。
4. 任務描述提到既有 Payment naming/source-inspection 測試失敗；本次驗證僅執行與此 diff 相關的目標測試與建置，未重跑完整測試套件確認該既有失敗數量無變化，但本次 diff 未觸及 Payment 相關程式碼，判斷風險低。

### 總結
本次變更的核心正確性契約（PresentRecordId 唯一身份、fail-closed、detached snapshot 重驗證、instance gate 原子性、JS generation/dispose 防護）在程式碼與測試中均得到充分且一致的體現，並通過本機重跑驗證（22/22、5/5、Release 0 錯誤）。無 Critical 發現。建議在合併前處理上述兩項 Warning（JS 初始化錯誤處理策略、manifest 與程式碼命名一致性），Info 項目可視團隊風格決定是否立即修正。

---
SESSION_ID: 81a02df7-4210-4d77-8fb1-327d841bfc6b

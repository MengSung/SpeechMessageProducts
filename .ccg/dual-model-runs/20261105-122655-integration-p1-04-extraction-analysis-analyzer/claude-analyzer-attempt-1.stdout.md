## Analysis: P1-04 抽取前風險分析（ToolUtility.Core 純資料核心抽取）

### Current State

- **邊界原則明確但驗證證據不足**：已核定「只搬 IOrganizationService/SDK 純資料操作」，但現況描述顯示 Operations 層的可搬性無法僅靠 using 陳述式判斷（ContactService 掛 host cache、ComplexQueryService 仍用 ICrmClient、QueryService 碰系統時間與 Trace 原值）。這代表**分類工作本身尚未完成**，是抽取前必須先交付的產出，而非抽取時順便判斷。
- **FeeService 處於「契約未知」狀態**：RetrieveDedicationBooking／RetrieveFee 回空集合，等於沒有可驗證的既有行為；另兩個方法用字串插入組 FetchXML，屬於需同時修正安全問題與補測試的舊代碼。沒有 Fee 專屬測試，代表目前對 Fee 查詢契約的理解主要來自需求敘述，而非可執行驗證。
- **ChurchReport 七處舊名單 facade 呼叫仍存在，但未觸發停止條件**：因為資料操作邊界仍是 IOrganizationService，尚未發現產品 C# 直接消費 ServiceClient/Proxy。這是「目前安全可以繼續」的訊號，但屬於持續觀察項，不是一次性確認後可忽略的狀態。

### Options Evaluated

| 議題 | 選項 | 利 | 弊 | 工作量 |
|---|---|---|---|---|
| 可搬性判定 | A. 逐型別分類表（Operations × 依賴種類）再決定搬移順序 | 避免誤搬帶 cache/logger/connection 的型別；可追溯 | 前置分析時間增加 | 中 |
| | B. 先搬再修（搬完再拔依賴） | 短期進度快 | 違反「不能複製實作」與「不能帶 LINE/檔案依賴」的核定邊界，風險高 | 低（但債務高） |
| FeeService 空實作 | A. 先溯源既有消費者與資料來源，重建契約後再補測試與新方法 | 符合「不能猜測業務」要求，避免新方法建在錯誤假設上 | 需要額外調查時間 | 中 |
| | B. 直接依需求敘述實作三個新方法，空方法原樣搬移 | 速度快 | 把「未完成」誤判為「已完成的 Core 能力」，污染 Core 契約 | 低（但正確性風險極高） |
| 特性測試策略 | A. 先在原服務鎖定現有兩個有效方法的 golden/failure，再開發新方法測試，最後才 git mv | 保證搬移前後行為等價，失敗可歸因 | 流程較長 | 中 |
| | B. 搬移後直接補測試 | 省一輪測試 | 無法證明搬移沒有改變行為；若新測試先天假設錯誤的契約，會把錯誤行為固化進 Core | 低（但喪失可回溯性） |

### Recommendation

採用「先分類、先溯源、先鎖定、後搬移」的四段式順序，因為核定邊界本身要求**不能複製實作、不能假性修正空實作、不能保留未分區的 cache/logger**——這些要求只有在搬移前就把「現狀到底是什麼」證明清楚才能滿足，搬移後再補救等於默認用測試去固化一個未驗證的行為。

### Risks & Mitigations

**Critical**

1. **風險：將 FeeService 的兩個空實作方法（RetrieveDedicationBooking／RetrieveFee）當作「已完成 Core 能力」原樣搬移。**
   → 緩解：抽取前必須先找到同能力的有效舊來源（可能是別的服務或舊版本）與實際消費者，重建查詢契約；若找不到，這兩個方法應標記為「尚未澄清」並排除在本輪搬移範圍外，而不是帶著空殼進 Core。這屬於**需要修正式計畫**的項目，因為它影響抽取範圍的真實邊界，不是實作細節。

2. **風險：Operations 依賴分類（object logger／反射 logger／host LoggerInvoker／cache 介面／ICrmClient／系統時間）若不先逐型別建表，搬移時容易把帶 host 依賴的型別誤判為「純資料操作」而搬進 Core。**
   → 緩解：抽取前產出一份型別 × 依賴種類的分類表，作為 go/no-go 清單；凡是無法 100% 排除 host 依賴的型別，一律留在原服務，不搬。

3. **風險：三個 FetchXML 舊方法直接字串插入輸入值，若原樣搬進 Core 會把 FetchXML injection 風險也一起帶進新的 net462/net10 雙目標專案。**
   → 緩解：這三個方法屬於「不能照抄」的明確指示，搬移時必須同步改為參數化/安全組裝，且新增測試要包含 `< & " '` 等字元的訂單值案例，驗證跳脫正確性而不只是回傳值正確性。

**Warning**

4. **風險：特性測試若只鎖定「目前兩個有實作的 Fee 查詢」的回傳與失敗傳遞，但這兩個查詢本身依賴尚未分類清楚的 Operations（如 ComplexQueryService/ICrmClient），測試會對著一個其實還會變動的依賴鏈拍照。**
   → 緩解：鎖定特性測試前，先確認這兩個查詢方法呼叫鏈上的所有 Operations 型別都已完成依賴分類；否則「鎖定」只是鎖住了一個尚未穩定的狀態。

5. **風險：重複 ID 與同名不同 ID 的測試案例，如果測試資料設計時用名稱而非 ID 去建立唯一性斷言，會與核定邊界「不以名稱去重」互相矛盾，測試本身可能驗證錯方向。**
   → 緩解：測試斷言一律以 ID 為鍵；同名不同 ID 的案例要明確斷言兩筆資料都保留，重複穩定 ID 的案例要明確斷言發布被拒絕（而非靜默覆蓋）。

6. **風險：A/B 並行測試與「借入服務不被釋放」的驗證，若只在單元測試層面做，可能無法反映 host 實際多工情境下的 Dispose 時序問題。**
   → 緩解：至少設計一組測試明確驗證 Core 內部不呼叫借入 IOrganizationService 的 Dispose/using 釋放邏輯，並確認 A、B 兩個連線鍵在並行呼叫時互不干擾（對應 DI 新 product 多載的連線鍵隔離設計）。

**Info**

7. ChurchReport 七處舊名單 facade 呼叫雖未觸發停止條件，但應作為本輪抽取完成後的**下一輪檢查點**記錄，避免未來才發現有新增的 ServiceClient/Proxy 直接消費路徑。

8. Obsolete 舊多載委派 ChurchReport 的設計，建議在測試中加入「呼叫舊多載時確實落到新 product 多載＋正確連線鍵」的驗證，避免 Obsolete 包裝本身出現連線鍵錯置。

### 測試與抽取順序建議

1. **分類**：完成 Operations 型別 × 依賴種類分類表（object logger／反射 logger／host LoggerInvoker／cache／ICrmClient／系統時間），劃出「可搬」「不可搬」清單。
2. **溯源**：對 FeeService 的兩個空實作方法，尋找有效舊來源與消費者，重建契約；若無法重建，排除於本輪搬移。
3. **鎖定（搬移前）**：對目前兩個有實作的 Fee 查詢，在原服務上建立 golden／失敗傳遞的特性測試，確認綠燈。
4. **新增測試（搬移前，仍在原服務）**：為 GetPaidFee／GetNotYetPaidFee／RetrieveFeeByFetchXmlOrderNumber 各寫至少三例（含危險字元訂單值、GUID 驗證），以及同名不同 ID、重複 ID 拒絕、A/B 並行、借入服務不釋放的測試，確認全綠。
5. **git mv**：僅在步驟 1–4 全部綠燈後執行搬移，不在搬移中修改邏輯。
6. **搬移後**：補故障注入測試（例如借入服務拋例外時，確認例外原樣往上傳遞、未被吞掉、host notifier 仍能收到），驗證搬移未改變可觀察行為。

### Action Items

1. [ ] 產出 Operations 依賴分類表，標記可搬/不可搬
2. [ ] 溯源 FeeService 兩個空方法的有效舊契約與消費者
3. [ ] 鎖定既有兩個 Fee 查詢的 golden／failure 特性測試
4. [ ] 補三個新 Fee 方法的測試（含危險字元、GUID、重複 ID、A/B 並行、Dispose 驗證）
5. [ ] 修正三個 FetchXML 方法的字串插入風險，與搬移動作解耦
6. [ ] 確認全部測試綠燈後才執行 git mv，並記錄 ChurchReport 七處 facade 呼叫作為下一輪檢查點

---
SESSION_ID: a315e81a-8080-436f-b564-bdc3c4c3ac0e

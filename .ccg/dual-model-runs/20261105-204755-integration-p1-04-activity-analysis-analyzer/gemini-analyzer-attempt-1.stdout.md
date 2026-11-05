# Gemini 設計分析報告：P1-04 活動切片與 `ActivityService` 審查

## 1. UX Analysis (使用者影響與互動分析)
- **CRM 業務流程完整性**：
  - `SetActivityStatusToCompleted` 與 `SetAppointmentStatusToScheduled` 分別維護活動（如 phonecall、email、appointment）的生命週期狀態。若狀態轉移失敗或因未預期的例外導致業務中斷，將直接影響前線使用者排程、任務追蹤與客戶互動紀錄。
  - 對於參與者（`from`/`to`）的解析涉及活動關聯的聯絡人、客戶或使用者。若解析邏輯出錯或遺漏，會導致通知發送失敗、訊息收件者不對齊或關聯紀錄空白。
- **行動端與桌面端一致性**：
  - 核心服務運行於伺服器端或整合層（支援 .NET Framework 4.62 及 .NET 10 雙目標），雖然不直接渲染 UI，但其穩定性直接決定前後端互動（如 Webhook 通知與 CRM 同步）的回應品質。

## 2. Design Evaluation (設計系統與架構評估)
- **架構契約與向後相容**：
  - 維持既有 `IActivityService` 介面契約、namespace 及舊建構子（Ctor），確保現有呼叫端平順升級。
  - 移除未使用的 logger 欄位，精簡依賴項。同時確認在 SDK 借用情境下，若需保存 SDK 只能限定於 host 授權 scope 且明確生命週期，且 SDK 本身不能被 `IDispose`（依據需求：`SDK借用不能Dispose`）。
- **參數防禦與例外處理**：
  - 顯式維持參數錯誤的前置拒絕（`ArgumentNullException` / `ArgumentException`）。
  - **關鍵安全要求**：真實 SDK 自身的 `ArgumentException` 不能被 catch 型別特判誤放行；固定的 wrapper 必須保留同一 `InnerException`，確保上層呼叫端能正確識別底層 CRM SDK 的例外原因。

## 3. Technical Considerations (技術考量與架構衝擊)
- **`ArrayList` 與執行緒安全/原子性**：
  - 程式碼使用非泛型的 `ArrayList`（`partyList`、`partyTypeList`、`partyIdList`）。兩個 `ArrayList` 出參無法進行原子 swap 參考。
  - 呼叫端必須明載「獨占 / 不可並行 enumerate」特性；應先建構局部 candidate（局部收集），再進行成功的批次 append，不可宣稱對任意自訂 `ArrayList` 或 OOM 也能跨兩物件進行原子發布。
- **異質參與者與查詢對齊問題**：
  - 原始批次查詢將 `Dictionary<Guid, Entity>` 跨表覆寫（若不同實體表剛好有相同的 `Guid`，會發生資料覆蓋），必須改為依 `LogicalName + Guid` 作為穩定複合主鍵（或先按 `partyTypeList` 收集、嚴格驗證根表、完整 ID、重複、未請求 ID、分頁）。
  - 不可盲目地按名稱 `Distinct` 或 `GroupBy` 刪除資料。
  - `from`/`to` 中若缺 `partyid`，可能是合法的 `addressused` 外部收件者，不可未研究就全面拒絕。
- **限量、Batch 與 MoreRecords 策略**：
  - D365 批次查詢與分頁必須有可核實依據，不得為維持舊成功路徑而接受缺 ID、重複、partial 或私密診斷。

---

## 4. 審查發現與分類 (Findings)

### 【Critical】跨表 `Guid` 覆寫風險 (`Dictionary<Guid, Entity>`)
- **檔案路徑**：`ToolUtility/ActivityOperations/ActivityService.cs` (約第 60-77 行)
- **問題描述**：目前使用 `Dictionary<Guid, Entity>` 儲存多個不同實體型別（LogicalName）查詢回來的實體。若不同實體表存在相同的 `Guid`，後查詢的實體會覆蓋前一個，導致 `resultMap[reference.Id]` 抓錯實體型別或資料。
- **建議修正**：改用複合鍵（例如 `(reference.LogicalName, reference.Id)` 或使用 `Tuple<string, Guid>`）作為 `Dictionary` 的 Key，確保異質參與者 logicalname + GUID 的穩定身份。

### 【Critical】`ArrayList` 非原子性輸出與執行緒安全
- **檔案路徑**：`ToolUtility/ActivityOperations/ActivityService.cs` (`GetActivityPartyList` / `GetActivityPartyIdList`)
- **問題描述**：方法接收兩個獨立的 `ArrayList` 參數（`partyList` 與 `partyTypeList`）進行逐步 `Add`。若在過程中發生例外或並行存取，會導致兩個清單長度或順序不對齊。
- **建議修正**：先在區域變數建立完整的區域候選清單（Local Candidate），待全部查詢與對齊驗證成功後，再一次性清除並 `AddRange` 至傳入的 `ArrayList` 中，並在 XML 註解明載 caller 獨占與不可並行 enumerate 的限制。

### 【Warning】SDK 例外封裝與 `ArgumentException` 放行問題
- **檔案路徑**：`ToolUtility/ActivityOperations/ActivityService.cs` (各方法的 `catch (Exception ex)`)
- **問題描述**：全面包裝成 `InvalidOperationException` 時，若底層 CRM SDK 拋出具業務意義的 `ArgumentException`，可能被籠統的 `catch (Exception ex)` 攔截並改變例外型別。
- **建議修正**：確保真 SDK 自身的 `ArgumentException` 不被錯誤吞掉或轉型，或者在 wrapper 中明確保留 `InnerException` 與原例外特徵。

---

## 5. Options (替代方案與取捨)
1. **方案 A（最小修改維持相容）**：保留 `ArrayList` 與既有公開簽章，僅在內部修復 `Dictionary` 複合鍵與區域候選清單原子填入。
   - **優點**：完全相容現有呼叫端，改動範圍最小。
   - **缺點**：仍受限於舊版 `ArrayList` 非型別安全設計。
2. **方案 B（現代化重構）**：引入泛型多載（例如 `List<Entity>`、`List<Guid>`），並保留舊 `ArrayList` 方法作為向後相容轉發器。
   - **優點**：型別安全、效能較佳、維護更容易。
   - **缺點**：需額外維護多載方法。

---

## 6. Recommendation (偏好方案與結論)
- **偏好方案**：採取 **方案 A 結合嚴格防禦**。在不破壞現有 `IActivityService` 公開介面與簽章的前提下：
  1. 將 `resultMap` 的 Key 改為複合識別（如 `string.Concat(group.Key, "_", id)` 或 `Tuple`），徹底解決跨表 `Guid` 衝突。
  2. 實作區域候選清單（Local candidate lists），驗證無誤後再寫入傳入的 `ArrayList` 參數中。
  3. 完善對 `addressused` 外部收件者的相容性研究，避免直接拒絕缺 `partyid` 的合理情境。
  4. 確保 SetState 入口（default 及 external SDK overload）正確保護 `EntityMoniker`、State=1 Status=4 與 appointment State=3 Status=5，並維持真實錯誤的完整傳遞。

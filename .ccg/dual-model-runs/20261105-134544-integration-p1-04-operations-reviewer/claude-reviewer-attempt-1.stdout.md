## Review: P1-04 Operations 抽取（Appointment / Lessons / MeetingStatistics → ToolUtility.Core）

本次為唯讀審查，僅依本次附上的固定快照比對（舊基準 47ab85f8、golden XML、新 Core 程式碼、TRX 證據），未擴讀 Facade／credentials／GoldenQueryAssertions.cs 等未附檔案。

### 逐項核對結論

- **golden 是否符合舊來源**：`AppointmentService`、`LessonsService`、`MeetingStatisticsService` 的欄位順序、filter 條件順序、狀態白名單（含刻意保留空白的 `" 100000004 "` 等）、`RetrieveByWeeklyReportAndContact` 的舊 `uitype='new_disciple_lessons'` 誤值、`RetrieveByDate` 的「宣告吃日期參數但實際不過濾」行為，逐一比對 12 組 golden 檔與 47ab85f8 基準原始碼，欄位/條件/排序/top/distinct 均一致，未發現搬移造成的語義漂移。
- **參數跳脫/日期文化**：`SdkDataQuery.Lookup`／`Condition` 皆以 `XAttribute` 序列化，特殊字元自動編碼；`SdkDataQuery.Date` 固定 `CultureInfo.InvariantCulture` 與 `"yyyy-M-d"`，對應 `Query_ThaiCulture_UsesInvariantGregorianDates` 測試。未發現注入或文化相依問題。
- **ID 發布/隔離**：`SdkDataQuery.ValidatePage` 以 `HashSet<Guid>` 檢查非空、不重複、根實體一致，不按名稱去重，符合「同名不同ID保留」要求，且被 12 組操作 × 4 種故障（duplicate/empty/entity/null）完整覆蓋。
- **資源生命周期**：三個新服務皆為無狀態、僅持有借入的 `IOrganizationService`，不實作 `IDisposable`，符合「不接管借入 SDK」設計；未見 cache/Task/Timer。
- **公開契約相容**：方法簽章、參數順序（含容易搬錯的 `RetrieveByWeeklyReportAndContactAlt` 參數順序）均與舊版一致。

### Warning 🟡

- **[ToolUtility.Core/AppointmentOperations/IAppointmentService.cs、ILessonsService.cs、IMeetingStatisticsService.cs]** 三個介面檔皆缺少 `#nullable enable`，但對應實作檔（`AppointmentService.cs`／`LessonsService.cs`／`MeetingStatisticsService.cs`）全部有 `#nullable enable`。
  - Why：介面是公開契約邊界，若介面處於 nullable-oblivious context，呼叫端（含未來的 Facade）在該介面型別下不會收到任何 `string?`/`string` 可空性提示，而實作內部又以 GUID 嚴格驗證、`?? string.Empty` 等方式隱性接受/拒絕 null，造成契約文件層級與實作行為不對稱。對照之下 `IEntityQueryService.cs`／`EntityQueryService.cs`（同次抽取範圍內另一組）兩者都已 `#nullable enable`，顯示這三組是本輪遺漏，而非既有風格。
  - 可重現：直接開啟三個介面檔頭部即可見缺少 `#nullable enable` pragma；與同目錄下對應 Service 類別比較即可確認不一致。
  - Fix：在三個介面檔補上 `#nullable enable`，並檢視是否需要將允許 null 的顯示名稱參數（如 `contactName`）標為 `string?` 以符合實作中 `displayName ?? string.Empty` 的實際容忍度。

### Info 🔵

- **[ToolUtility.Core/AppointmentOperations/AppointmentService.cs: RetrieveByDate]** `RetrieveByDate` 雖共用 `SdkDataQuery.ValidatePage`，但 `QueryOperationsSafetyTests.InvalidPages`／`Operations()` 列舉的 12 組操作中並未包含此方法，因此沒有針對它的重複ID/空ID/跨實體/null頁直接故障注入測試，僅有一個正常路徑測試（`AppointmentByDate_PreservesLegacyUnfilteredQuery`）。由於驗證邏輯與其他 12 組共用同一段程式碼，風險很低，但嚴格來說這個入口本身沒有獨立證據。
- **[ToolUtility.Tests/TestHelpers/GoldenQueryAssertions*.cs 未附於本輪快照]** golden XML 檔以單引號、多行縮排格式呈現，而產品端 `SdkDataQuery.Run`／`FeeService.Query` 是用 `XElement...ToString(SaveOptions.DisableFormatting)` 產出的緊湊雙引號單行 XML；兩者要能比對相等，`GoldenQueryAssertions.Equal` 必須是結構化 XML 比較（而非純文字比較）。本輪快照未附上該檔案原始碼，無法直接審查其比較邏輯是否會漏掉屬性順序、空白節點等差異；僅能以 TRX 顯示的 207/207、282/282、112/112 全綠作間接佐證，建議下一輪附上該檔案以完整收斂此點（避免重複列為缺口，僅標記為尚待附證據）。
- **行為差異紀錄（非缺陷，僅供變更紀錄）**：`SdkDataQuery.RequireRange`（反向日期區間）與 `SdkDataQuery.Lookup`（嚴格 GUID 格式/非空驗證）相對舊基準（直接把字串拼入 FetchXml，無驗證、送進 SDK 後多半靜默回空頁）是刻意的安全強化且已有對應測試覆蓋（`Query_ReverseDateRange_RejectsBeforeIo`、`Query_InvalidLookup_RejectsBeforeIo`）。提醒：若既有呼叫端過去依賴「格式不良輸入靜默回空頁」的行為，現在會改為拋出 `ArgumentException`，屬於行為升級但非本次審查範圍可驗證（Facade 不在審查範圍內）。

### Summary

三組新搬移的 Appointment／Lessons／MeetingStatistics 服務與 `SdkDataQuery` 共用邏輯，在欄位順序、查詢條件、狀態白名單、日期格式、ID 完整性驗證、安全 XML 編碼與資源借用語義上，均與 47ab85f8 舊基準及本輪 golden 檔一致，測試也確實呼叫真正的產品類別（非僅驗證 mock），斷言涵蓋故障注入、文化不變性、並行隔離與同名不同ID保留，證據充分。未發現 Critical/High 等級缺陷。僅有一項 Warning（介面缺 `#nullable enable` 與實作不一致）建議修正，另有兩項 Info 供下一輪補強測試覆蓋與附件完整性參考。整體可視為此三組搬移本身已達成既定驗收標準；惟如先前紀錄所述，全 P1 仍 0/12，本輪證據不構成實機或完整 P1-04 完成證明。

---
SESSION_ID: 6bec76bb-8510-46c2-ab1a-a579cd96873b

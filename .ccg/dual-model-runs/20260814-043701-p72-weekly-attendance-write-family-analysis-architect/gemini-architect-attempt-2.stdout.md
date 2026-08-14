# 1. Analysis (架構評估)

在當前的系統架構中，`ORG-CALL-00063` 相關的寫入家族（Write Family）主要由 `PersonalQrCodeUtility` 與 `SundayQrCodeUtility` 承載，並深度依賴 `WeeklyReportProcessor`。

### Read Contract 與 Write-Adjacent Legacy Graph 的混淆
現有的 `Package03` 僅定義了簡單的唯讀合約（Read Contract，如 ID、名稱、建立時間與主日日期），但在實際執行路徑中，寫入行為（Write-Adjacent Legacy Graph）卻極為複雜且高度耦合：
1. **連鎖實體建立與更新**：簽到操作會先讀取 `new_meeting_statistics`，接著寫入/更新 `new_present_record`，並自動關聯兩者。
2. **週報狀態鎖定與重新計算**：寫入過程中會觸發 `WeeklyReportProcessor.CreateWeeklyReportAndPresentRecord`，這會自動為整個小組成員建立預設出席紀錄，並將週報 `new_group_present_weekly_report` 的 `new_saved_flag` 設為 `"計算中/存檔中"`。
3. **併發鎖定機制**：`SundayQrCodeUtility` 內部使用了 `lock (m_UpdateSundayWeeklyReportLocker)` 來防止併發寫入週報。

這表明**現有的 Source Audit 並未正確區分 Read Contract 與 Write-Adjacent Legacy Graph**。寫入邏輯並非單純的 DTO 寫入，而是對整個 Dynamics 365 關係圖譜（Relationship Graph）的狀態機變更，具有強烈的副作用。

---

# 2. Architecture Decision (架構決策)

為了降低系統耦合度並避免歷史 `write-not-committed` 失敗再次發生，我們建議引入**防腐層（Anti-Corruption Layer, ACL）**與**讀寫職責分離（CQRS）**模式。

```diff
--- a/SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs
+++ b/SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs
@@ -19,6 +19,20 @@
+using SpeechMessageProducts.ChurchReport.Tools.Interfaces;
+using SpeechMessageProducts.ChurchReport.Tools.Models;
 
 namespace ChurchReport.Tools
 {
     public class PersonalQrCodeUtility
     {
+        private readonly ICheckInLedgerWriter _ledgerWriter;
+        private readonly IMeetingStatisticsReader _statisticsReader;
+
+        public PersonalQrCodeUtility(
+            ICheckInLedgerWriter ledgerWriter,
+            IMeetingStatisticsReader statisticsReader)
+        {
+            _ledgerWriter = ledgerWriter;
+            _statisticsReader = statisticsReader;
+        }
+
         public string SetupQrCodeIdString(string aQrCodeIdString, string UserLineId)
         {
```

### 決策說明：
- **引入 `ICheckInLedgerWriter`**：將所有寫入操作（如建立出席紀錄、更新週報狀態）抽象化為 Append-Only 的 Ledger 寫入，避免業務邏輯直接操作 Dynamics SDK Entity。
- **引入 `IMeetingStatisticsReader`**：將讀取合約與寫入合約完全分離，確保讀取操作不會意外觸發寫入副作用。
- **拒絕方案**：拒絕在現有 Utility 類別中直接修改 SDK 欄位或繼續擴展 legacy 寫入邏輯。

---

# 3. Implementation Plan (實施計劃)

### 步驟一：定義最小 Local-Only Write Slice 邊界
第一個最小的寫入 Slice 應僅限於**「簽到意圖事件（Check-in Intent Event）」**的 Append-Only 寫入。
- **寫入欄位**：`ContactId`、`MeetingStatisticsId`、`Timestamp`、`QrCodeIdString`。
- **邊界限制**：此 Slice **不得**觸發任何週報重新計算（`new_saved_flag`）、**不得**自動為小組其他成員建立預設出席紀錄、**不得**直接更新 `new_present_record` 的狀態。

### 步驟二：隔離 Read-New / Write-Legacy 風險
- **風險分析**：若新系統（Read-New）讀取了尚未由 Legacy 完整計算或關聯的資料，而寫入端（Write-Legacy）又直接修改實體，會導致 `RetrievePresentRecordByFetchXmlAndSundayDate` 因併發衝突而重複建立紀錄，或導致週報鎖定機制失效。
- **隔離策略**：在 Legacy Graph 完整遷移前，所有寫入必須通過一個統一的佇列（Queue）進行非同步序列化處理，避免新舊系統同時對同一個週報實體進行寫入。

---

# 4. Considerations (架構考量與缺口分析)

### 1. Authorization (授權缺口)
- **現狀**：僅依賴 `UserLineId` 進行聯絡人檢索，缺乏 API 級別的 Token 驗證與操作者權限校驗。
- **缺口**：無法防止越權操作（ID Spoofing）。

### 2. Idempotency (冪等性缺口)
- **現狀**：依賴 `aSigningTime.Year <= 1` 判斷是否已簽到，缺乏資料庫層級的唯一性約束（Unique Constraint）或分散式鎖。
- **缺口**：在高併發掃碼時，會產生重複的 `new_present_record`。

### 3. Ledger (帳本缺口)
- **現狀**：直接對狀態實體（State Entity）進行 Update/Create，無獨立的審計日誌。
- **缺口**：一旦寫入失敗或狀態被覆寫，無法進行資料回溯與審計。

### 4. Exact Read-Back & Cleanup (回讀與清理缺口)
- **現狀**：`UpdateEntity` 後未進行 Read-Back 驗證；若後續步驟失敗，無補償交易（Compensating Transaction）清理已建立的髒資料。
- **缺口**：易產生孤立的（Orphaned）出席紀錄與處於「計算中」掛起狀態的週報。

### 5. A/B Isolation & No-Replay (隔離與防重放缺口)
- **現狀**：QR Code 內容無時間戳記與簽章；新舊系統共用同一個 Dynamics 實體。
- **缺口**：QR Code 可被無限次重放攻擊（Replay Attack），且無法進行漸進式流量隔離。

---

# 5. Findings Classification (問題清單)

### 🔴 Critical
- **Legacy Graph 深度耦合與狀態污染風險**：`ORG-CALL-00063` 寫入家族會連鎖觸發整個小組的出席紀錄建立與週報狀態變更（`new_saved_flag`）。在未進行防腐層隔離前，任何寫入操作都會直接污染 Legacy 資料，且極易因併發導致 Deadlock。
- **無防重放與冪等性保障**：QR Code 缺乏時效性校驗與數位簽章，且寫入端無分散式鎖，存在嚴重的重放攻擊與重複寫入風險。
- **精確 NO-GO 判定**：由於歷史 P7.2 Slice C 發生過 `write-not-committed` 失敗且要求永久不可重試，加上目前 P7.5 readiness 仍為 no-go（所有 feature gates 皆為 false），**目前完全不具備安全進入 CE preflight 的條件**。

### ⚠️ Warning
- **缺乏 Exact Read-Back 與 Cleanup 機制**：寫入後無回讀確認，且在交易失敗時缺乏補償機制來清理已建立的 `new_present_record`，會留下髒資料。
- **授權機制薄弱**：僅依賴 LINE User ID 進行關聯，缺乏傳輸與應用程式層級的強授權驗證。

### ℹ️ Info
- **讀寫合約混淆**：現有的 `Package03` 唯讀合約無法代表實際的寫入複雜度，後續重構應將讀寫職責完全分離（CQRS）。

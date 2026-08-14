# 1. Analysis (當前架構評估)

經過對 `PersonalQrCodeUtility.cs`、`SundayQrCodeUtility.cs` 以及 `WeeklyReportProcessor.cs` 的源碼審計，當前的寫入家族（Write Family）架構存在嚴重的強耦合與狀態連鎖反應問題：

*   **Legacy Graph 強耦合**：`ORG-CALL-00063` 的寫入路徑並非單純的 DTO 寫入。當調用 `SigningMeetingStatistics` 時，系統會連鎖觸發 `new_present_record` 的讀取與更新、`new_meeting_statistics` 的關聯綁定，甚至透過 `WeeklyReportProcessor` 自動為整個小組成員建立預設出席紀錄，並將週報 `new_group_present_weekly_report` 的 `new_saved_flag` 標記為 `"閮??箏葉"`（計算中）。
*   **讀寫合約混淆**：現有的合約（如 Package03 DTO）僅定義了唯讀欄位，但實際執行路徑卻深度依賴 Dynamics 365 SDK Entity 的動態屬性與關聯。系統並未在架構層面上將「讀取合約（Read Contract）」與「寫入相鄰遺留圖譜（Write-Adjacent Legacy Graph）」進行物理或邏輯隔離。
*   **併發與鎖定風險**：`SundayQrCodeUtility` 內部使用 `lock (m_UpdateSundayWeeklyReportLocker)` 來防止多個執行緒同時寫入週報狀態。這種記憶體鎖（In-Memory Lock）在分散式或多實例部署下完全失效，極易導致 Dynamics 365 資料庫產生 Lock Contention 或 Deadlock。

---

# 2. Architecture Decision (架構決策)

為了避免重蹈歷史 P7.2 Slice C `write-not-committed` 的覆轍，必須採取以下架構決策：

*   **決策**：**精確 NO-GO**。目前完全不具備安全進入 CE Preflight 的條件。
*   **拒絕替代方案**：拒絕在現有 Legacy Utility 中直接插入 Preflight 寫入測試。因為任何寫入都會觸發 `WeeklyReportProcessor` 的連鎖實體建立，無法做到單一實體的隔離測試。
*   **假設**：假設所有 Feature Gates 目前皆為 `false`，且 P7.5 Readiness 仍為 No-Go，P8 不得啟動。
*   **潛在副作用**：若強行執行寫入，將導致 Dynamics 365 資料庫產生孤立的（Orphaned）出席紀錄與未完成的週報狀態，且由於缺乏補償機制，將造成資料庫污染。

---

# 3. Implementation Plan (實施計劃)

### 步驟一：隔離寫入邊界 (Introduce Ledger Interface)
引入 `ICheckInLedgerWriter` 介面，將「簽到意圖（Intent）」與「遺留狀態更新（Legacy State Update）」解耦。簽到時僅寫入 Append-Only Ledger，後續的週報計算與關聯改由非同步 Worker 處理。

### 步驟二：實作 Feature Gate 隔離
在寫入入口處加入 Feature Gate 檢驗，若 Gate 未開啟，則完全阻斷寫入路徑，避免污染 Legacy Graph。

```diff
--- a/SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs
+++ b/SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs
@@ -36,6 +36,7 @@
 
         // ?? Factory ?? ToolUtilityClass ?桐?撖虫?
         private ToolUtilityClass m_ToolUtilityClass = ToolUtilityFactory.GetInstance("DYNAMICS365-9.0");
+        private readonly ICheckInLedgerWriter m_LedgerWriter;
         private LineMessagingClient m_LineMessagingClient { get; set; }
 
         private PushUtility m_PushUtility { get; set; }
@@ -235,6 +236,13 @@
                 #endregion
                 #region// ?犖????靽株??€脰?蝪賢?€ , ???喳?蝯?
-                SigningMeetingStatistics(m_MeetingStatistics, UserName, m_Contact.Id.ToString(), m_OnboardType);
+                if (FeatureGate.IsEnabled("p72-weekly-attendance-write-new"))
+                {
+                    m_LedgerWriter.WriteCheckInIntent(m_Contact.Id, m_MeetingStatistics.Id, DateTime.Now, m_QrCodeIdString);
+                }
+                else
+                {
+                    SigningMeetingStatistics(m_MeetingStatistics, UserName, m_Contact.Id.ToString(), m_OnboardType);
+                }
                 #endregion
 
                 #region// ?喳?蝯衣雯?偷?唳?蝪賡€€??嚗??臬撌脩偷?圈?鈭?
```

---

# 4. Considerations (架構缺口分析)

1.  **Authorization (授權缺口)**：僅依賴 LINE User ID 換取 Contact Entity，缺乏 API 級別的 Token 驗證與 Scanner/Scanned 關係校驗。
2.  **Idempotency (冪等性缺口)**：依賴 `aSigningTime.Year <= 1` 進行狀態判斷，在高併發下會因 Read-Before-Write 的 Time-of-Check to Time-of-Use (TOCTOU) 漏洞導致重複建立 `new_present_record`。
3.  **Ledger (帳本缺口)**：缺乏 Append-Only 的簽到日誌，直接修改狀態實體，導致寫入失敗時無法追溯原始請求。
4.  **Exact Read-Back (精確回讀缺口)**：`UpdateEntity` 後無 Read-Back 驗證，直接發送 LINE 通知，存在「通知成功但資料庫寫入失敗」的風險。
5.  **Cleanup (清理缺口)**：歷史 `write-not-committed` 失敗時，無補償交易（Compensating Transaction）清理已建立的關聯或週報標記。
6.  **A/B Isolation (隔離缺口)**：新舊流量共用相同的 Dynamics 365 實體與資料庫，缺乏租戶或邏輯分區隔離。
7.  **No-Replay (防重放缺口)**：QR Code 內容無時間戳記與隨機數（Nonce），易遭受重放攻擊。

---

# 5. Findings Classification (審計發現分類)

### [Critical] 寫入家族與遺留圖譜強耦合風險 (Write-Adjacent Legacy Graph Coupling)
*   **檔案路徑**：
    *   `SpeechMessageProducts.ChurchReport\Tools\PersonalQrCodeUtility.cs` (Line 256-321)
    *   `SpeechMessageProducts.ChurchReport\Tools\SundayQrCodeUtility.cs` (Line 254-323)
    *   `SpeechMessageProducts.ChurchReport\Tools\WeeklyReportProcessor.cs` (Line 95-140)
*   **依據與危害**：簽到寫入會連鎖觸發 `WeeklyReportProcessor` 建立整個小組的出席紀錄並修改週報狀態（`new_saved_flag`）。這會導致寫入事務範圍過大，極易因單一成員資料異常或併發鎖定而導致整體失敗，且無法進行局部重試。

### [Critical] 記憶體鎖在分散式環境失效風險 (In-Memory Lock Failure)
*   **檔案路徑**：`SpeechMessageProducts.ChurchReport\Tools\SundayQrCodeUtility.cs` (Line 58, Line 287)
*   **依據與危害**：系統使用 `lock (m_UpdateSundayWeeklyReportLocker)` 來保護週報更新。在多實例部署或負載平衡環境下，此記憶體鎖無法跨進程生效，將導致併發寫入衝突與 Dynamics 365 資料庫死鎖。

### [Warning] QR Code 缺乏防重放機制 (No-Replay Vulnerability)
*   **檔案路徑**：
    *   `SpeechMessageProducts.ChurchReport\Tools\PersonalQrCodeUtility.cs` (Line 160-190)
    *   `SpeechMessageProducts.ChurchReport\Tools\SundayQrCodeUtility.cs` (Line 171-202)
*   **依據與危害**：QR Code 僅由 `Guid`、`Attribute` 與 `OnboardType` 組成，無時間戳記、隨機數或數位簽章，惡意使用者可截獲並無限次重放簽到請求。

### [Info] 缺乏 Exact Read-Back 驗證 (Missing Read-Back Verification)
*   **檔案路徑**：`SpeechMessageProducts.ChurchReport\Tools\PersonalQrCodeUtility.cs` (Line 387-396)
*   **依據與危害**：在呼叫 `UpdateEntity` 後，系統直接將 `m_NotifyLineFlag` 設為 `true` 並準備發送通知，未重新讀取資料庫確認寫入狀態，可能導致虛假成功通知。

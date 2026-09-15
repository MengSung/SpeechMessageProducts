# 奉獻操作頁面與金流同步最終審查報告 (Final Review Report)

**審查目標**：審查 `codex/donation-operation-page` 分支相對 `d276ea270` 之變更與工作區修正，重點確認：
1. `DonationFeePaymentProcessor` 硬編碼 LINE ID 與繞過 `Exception.log` 問題已完成修復。
2. 多類別奉獻頁（最多 7 列、定期定額單列、ATM 單列限制）。
3. CRM 收費單付款金額與付款狀態同步。
4. Callback 冪等性與 `Param1` Fail-Closed 安全防護。

---

## 1. 上一輪指出的議題修復確認

- **[已修復] `DonationFeePaymentProcessor.cs` 硬編碼 LINE ID 與繞過 `Exception.log`**
  - **檔案與行號**：`SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs`（第 89–93 行、第 818–820 行）
  - **確認結果**：
    1. 已移除硬編碼常數 `MENGSUNG_LINE_ID`。
    2. 全域 catch 區塊（第 818–820 行）已改為呼叫 `ChurchReportLineAdminNotificationService.ReportException(nameof(DonationFeePaymentProcessor) + "." + nameof(HandlePaymentReturn), e)`。
    3. 例外發生時會確定性寫入並 flush 至 `Exception.log`，再依系統設定排入 LINE 管理者通知，管道順序完全合規。
    4. HTML 錯誤回應（第 824–834 行）已移除 `PayToken` 與原始例外明細，避免終端使用者洩漏敏感診斷資訊。

---

## 2. 審查發現分類 (Findings)

### Critical（嚴重風險）
> **無**。經過完整檢查，現有分支與 Working Tree 變更中，未發現跨會友資料洩漏、部分入帳、重複累加金額、秘密外洩或 Socket/記憶體洩漏問題。

---

### Warning（警告事項）
> **無**。上一輪回報之「ATM 多類別後端驗證未 Fail-Closed」與「LINE 硬編碼」等問題均已補齊防護並通過驗證。

---

### Info（參考資訊與優良實作）

#### 1. 奉獻表單伺服器端驗證完整落實 Fail-Closed
- **檔案與行號**：`SpeechMessageProducts.ChurchReport/Services/DonationPaymentSubmissionService.cs`（第 42–65 行）
- **說明**：
  - 第 43–44 行：限制一次最多 7 個奉獻類別（`DonationLineItemNormalizer.MaxLines = 7`），與永豐金流 `Param1`（X(255)）相符。
  - 第 47–48 行：信用卡定期定額（每個月）強制單一類別（`lines.Count > 1` 拒絕）。
  - 第 49–50 行：ATM 轉帳/匯款強制單一類別（`lines.Count > 1` 拒絕，阻擋伺服器端繞過）。
  - 第 51–56 行：奉獻類別重複檢查（不分大小寫與前後空白）。
  - 第 59 行：負數金額阻擋。

#### 2. 永豐 Param1 多 Guid 編解碼與 Fail-Closed 防護
- **檔案與行號**：`SpeechMessageProducts.ChurchReport/Services/DonationFeeIdList.cs`（第 25–36 行、第 87–126 行、第 135–177 行）
- **說明**：
  - 單一收費單維持舊有 Guid 格式；多張收費單採用 32 碼 Compact 格式（"N" 格式）與半形逗號串接，總長度嚴格控制於 255 字元內。
  - `Parse`（第 87–126 行）落實 Fail-Closed 原則：若 Param1 內任一項包含非法 Guid、空值、重複值或超過 7 張，一律回傳空陣列（`Array.Empty<Guid>()`），防止部分收費單被誤處理。
  - `SelectGroupMembers`（第 135–177 行）確保 callback 只會處理屬於同一會友且同訂單編號的收費單群組，防止跨會友/跨訂單資料干擾。

#### 3. CRM 收費單逐張實收同步與冪等防護
- **檔案與行號**：`SpeechMessageProducts.ChurchReport/Tools/DonationFeeGroupPaidPlanner.cs`（第 57–109 行）
- **說明**：
  - 多類別群組入帳時，計算每張 `new_fee` 的實收金額等於自己的應收金額（`fee.ShouldPay`），使每張單據的未繳金額精確為 0。
  - `PrimaryNewlyPaid` 僅在 primary 單據首次入帳時為 `true`；對於 `RETURN_URL` 與 `BACKEND_URL` 重複回呼，`AnyToMark` 為 `false`，不會發生二次 CRM 更新或重複發送 LINE 感謝通知。

#### 4. 多類別收費單建立事務補償機制
- **檔案與行號**：`SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.FeeManagement.cs`（第 89–123 行）
- **說明**：
  - `CreateFeesForLines` 包含倒序補償刪除機制 (`ToolUtility.DeleteEntity("new_fee", ...)`)。若迴圈建立 `new_fee` 過程發生未預期例外，會將已建立的收費單實體確定性復原刪除，不會在 CRM 留下孤立的未完成單據。

#### 5. 來源教會資料去硬編碼
- **檔案與行號**：`SpeechMessageProducts.ChurchReport/Views/Dedication/DonationPaymentView.cshtml`（第 8–25 行）
- **說明**：
  - 教會顯示名稱、地址、電話、Email、統編與聯絡人等欄位均已改由 `Configuration["ChurchInfo:..."]` 動態載入，若未配置則退回至安全通用預設值，無特定教會硬編碼殘留。

---

## 3. 審查結論 (Decision)

**RECOMMENDATION: PASS**

所有先前回報之安全與維護性問題均已修復，多類別奉獻頁面、CRM 實收金額/付款狀態同步、Callback 冪等與 Param1 fail-closed 等核心機制皆已符合規格要求與驗收標準。

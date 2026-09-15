# 奉獻操作頁面與金流同步審查報告 (Final Review Report)

**審查分支範圍**：`codex/donation-operation-page`（相對 `d276ea270` 及目前工作區變更）  
**審查目標**：多類別奉獻明細（`Lines[i]`）、上限 7 列、定期定額單列、ATM 預設封鎖、永豐信用卡 Callback Param1 多 Guid Fail-Closed、CRM 收費單實收金額與狀態同步與冪等、Session 隔離、例外告警管道順序、資源生命週期、UTF-8/CRLF 編碼，以及來源教會資料去硬編碼。

---

## 1. 審查結論摘要 (Executive Summary)

整體變更品質良好，核心機制均符合規格要求：
1. **多類別明細與邊界**：已實作 `Lines[i].Category/Amount/Others` 繫結，上限限制為 7 列（與永豐金流 Param1 的 255 字元上限嚴格匹配）。
2. **定期定額單列限制**：前端 UI 與後端 `ProcessRecurringPayment` 均強制限制為 1 列（`lines.Count != 1` 時拒絕建立）。
3. **Param1 多 Guid 解析與 Fail-Closed**：`DonationFeeIdList.Parse` 採用 Fail-Closed 原則，任何單一 Guid 解析失敗或超過 7 個即回傳空集合並引發例外，絕不進行部分處理。
4. **CRM 逐張收費單入帳與冪等性**：透過 `DonationFeeGroupPaidPlanner` 計算入帳計畫，確保同一次交易的所有 `new_fee` 實收金額與付款狀態同步更新，且對 `RETURN_URL` 與 `BACKEND_URL` 重複回呼具備完整冪等保護。
5. **資源生命週期與事務補償**：`CreateFeesForLines` 在多類別收費單建立過程若中途發生例外，會執行反向刪除補償機制（`ToolUtility.DeleteEntity("new_fee", ...)`），防止 CRM 殘留孤立的未完成單據。

---

## 2. 問題與風險分類報告 (Findings)

### Critical（嚴重風險）
> *無 Critical 級別項目。本次變更未發現跨使用者個資洩漏、部分入帳、重複累加金額、秘密外洩或 Socket/記憶體洩漏問題。*

---

### Warning（警告事項）

#### 1. ATM 多類別付款後端驗證防護未完全 Fail-Closed（欠缺 Server-side Validation）
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Services/DonationPaymentSubmissionService.cs`（與 `DonationPaymentView.cshtml:1146`）
* **具體位置與說明**：
  前端 `DonationPaymentView.cshtml:1146` 已明確加載驗證規則：
  `if (state.pay === 'ATM轉帳/匯款' && act.length > 1) out.push('ATM 多類別付款尚未開放，請分開建立奉獻或改用信用卡');`
  然而後端 `DonationPaymentSubmissionService.ValidateDonationForm` 尚未包含相對應的伺服器端驗證規則。若有攻擊者或自訂用戶端繞過前端 JS 直接 POST 多筆 `Lines` 且 `PayWay = "ATM轉帳/匯款"`，後端 `ProcessAtmPayment` 會被執行並產生多筆 ATM 收費單。
* **建議修正**：於 `DonationPaymentSubmissionService.ValidateDonationForm` 中補充伺服器端檢查：當 `PayWay == "ATM轉帳/匯款"` 且 `lines.Count > 1` 時，立即回傳錯誤訊息阻擋建單。

#### 2. `DonationFeePaymentProcessor.cs` Catch 區塊仍殘留硬編碼管理員 LINE User ID 且未走 `ChurchReportLineAdminNotificationService`
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs`（第 94 行、第 821 行）
* **具體位置與說明**：
  `DonationFeePaymentProcessor.cs` 第 94 行定義常數 `MENGSUNG_LINE_ID = @"U7638e4ed509708a3573ba6d69970583d"`，第 821 行在全域 catch 區塊直接呼叫：
  `try { m_PushUtility.SendMessage(MENGSUNG_LINE_ID, ErrorString); } catch { }`
  這違反了兩項專案規範：
  (1) 包含特定來源教會個人/管理員 LINE ID 硬編碼。  
  (2) 繞過了共用例外通知管線（`ChurchReportLineAdminNotificationService.ReportException`），導致例外未先經過 `Exception.log` 確定性寫入與 flush，即直接發送 LINE 訊息。
* **建議修正**：將該 catch 區塊調整為呼叫 `ChurchReportLineAdminNotificationService.ReportException(nameof(DonationFeePaymentProcessor) + ".HandlePaymentReturn", e);`，並移除硬編碼常數 `MENGSUNG_LINE_ID`。

---

### Info（參考資訊 / 良好實作與改進建議）

#### 1. 前端頁面來源教會資訊去硬編碼完全落實
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Views/Dedication/DonationPaymentView.cshtml`（第 8–23 行、第 847 行）
* **說明**：已將舊有硬編碼之教會名稱（如「好牧人」）、地址、電話、統編與說明文案改由 `Configuration["ChurchInfo:..."]` 動態載入，若未設定則安全退回至通用預設值（如「教會辦公室」），避免跨部署教會資料混淆。

#### 2. 多類別收費單建立過程具備完善的補償刪除機制
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.FeeManagement.cs`（第 89–123 行）
* **說明**：`CreateFeesForLines` 在多類別情境下，若迴圈建立 `new_fee` 時任一筆發生例外，會走 `catch` 倒序刪除已建立的 `new_fee` 實體；若刪除過程亦失敗則拋出 `AggregateException`，確實維持 CRM 資料庫的原子性與一致性。

#### 3. 測試覆蓋率與編碼規範良好
* **檔案路徑**：
  - `ChurchReport.MemberInfo.Tests/Dedication/DonationFeeIdListTests.cs`
  - `ChurchReport.MemberInfo.Tests/Dedication/DonationFeeGroupPaidPlannerTests.cs`
* **說明**：針對多 Guid 編解碼（1～7 筆、邊界 255 字元、無非法字元）、組圖入帳計畫（多筆各自應付=實收、舊程式寫入 single fee 之校正、人工調整不覆蓋、重複 callback 冪等性）均已編寫完整單元測試。測試檔案皆維持 UTF-8 without BOM 與 CRLF 換行。

---

## 3. 檢核點對照表 (Checklist Matrix)

| 檢核項目 | 狀態 | 審查說明 |
| :--- | :---: | :--- |
| **`Lines[i].Category/Amount/Others`** |  通過 | 前端動態表單與後端 `DonationPaymentFormModel` 繫結正常 |
| **最多 7 列限制** |  通過 | 前端與 `DonationLineItemNormalizer.MaxLines`（7 列）雙重鎖定 |
| **定期定額單列限制** |  通過 | 前端 UI 自動鎖定 1 列，後端 `ProcessRecurringPayment` 亦有驗證拒絕 |
| **多類別 ATM 預設封鎖** |  警告 | 前端 JS 已封鎖（View:1146），建議後端 `ValidateDonationForm` 補齊相同規則 |
| **信用卡 Param1 多 Guid Fail-Closed** |  通過 | `DonationFeeIdList.Parse` 格式不合或逾限直接回傳空陣列並報例外 |
| **CRM `new_fee_really_paid` / `new_pay_status` 同步** |  通過 | `DonationFeeGroupPaidPlanner` 確保每張單據各自實收金額與狀態同步 |
| **Callback 冪等性** |  通過 | 已通過單元測試驗證，二次 Call 不會重複計算或發送 LINE |
| **Session 隔離** |  通過 | `TryRestoreDonationDonorIdentity` 嚴格互斥 LINE 與網頁登入個資 |
| **例外告警管道順序** |  警告 | 入口 Controller 已走 `ReportException`；`DonationFeePaymentProcessor` 需清理舊 LINE 呼叫 |
| **資源生命週期與事務補償** |  通過 | `CreateFeesForLines` 包含倒序補償刪除機制 |
| **檔案編碼 (UTF-8 / CRLF)** |  通過 | 變更與新增檔案均符合 UTF-8 without BOM 與 CRLF 規範 |
| **來源教會硬編碼資料清理** |  通過 | `DonationPaymentView.cshtml` 已全面轉為 `Configuration` 讀取 |

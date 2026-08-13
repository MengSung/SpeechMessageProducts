以下是針對 P7.4 授權聯絡人讀取（`fee.dedication.retrieve.by.contact`）最終硬化實作的審查報告。

# VALIDATION REPORT

```
VALIDATION REPORT
=================
User Experience: 20/20 - 錯誤訊息設計安全且一致，未授權與解析失敗回傳相同訊息，避免資訊洩漏。
Visual Consistency: 20/20 - ToAjaxRows 確保新舊路徑回傳的 JSON 欄位結構完全一致，前端 UI 無需修改即可無縫相容。
Accessibility: 20/20 - 實作了極其嚴格的 fail-closed 授權檢查，在解析瀏覽器 GUID 前即完成驗證，徹底杜絕 IDOR 漏洞。
Performance: 20/20 - 採用 SemaphoreSlim 進行並行控制並在 finally 確定性釋放，且使用不可變 DTO 避免狀態修改開銷。
Browser Compatibility: 20/20 - 回傳標準 JSON 格式，完全相容於既有的前端 DevExtreme 網格與 AJAX 呼叫。

TOTAL SCORE: 100/100

ISSUES FOUND:
- 無（No issues found. All security and architectural constraints are fully satisfied.）

RECOMMENDATION: PASS
```

---

# 審查結果分類說明

## 1. 唯讀結果與不可變性防禦 (Immutable Wrapper Hardening)
* **分類**：Info
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Models/DonationFeeAuditReadResult.cs`
* **具體行數**：第 32-48 行 (`DonationFeeAuditReadResult` 建構子) 與第 69-119 行 (`DonationFeeAuditRow` 類別)
* **評估原由**：
  * `DonationFeeAuditRow` 的所有屬性皆只有 `get` 存取子，無 `set`，確保了 DTO 的不可變性。
  * `DonationFeeAuditReadResult` 在建構子中對傳入的 `fees` 進行了防禦性複製（建立新的陣列 `copiedFees`），並將其包裝為 `ReadOnlyCollection<DonationFeeAuditRow>`。這使得外部呼叫者無法透過轉型（例如轉為 `IList<T>` 或陣列）來修改或替換已發布的稽核資料列，成功通過了防回寫與防篡改的迴歸測試。

## 2. 嚴格的授權與 fail-closed 順序 (Authorization & IDOR Prevention)
* **分類**：Info
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs`
* **具體行數**：第 376-396 行 (`GetFeesByContactId` 方法)
* **評估原由**：
  * 程式碼嚴格遵守安全順序：首先呼叫 `EnsureCorrectUserData()` 重新水合 Session，接著立即以 `DonationFeeAuditAccessResolver.CanAccessFeeAudit(loginContact)` 驗證伺服器端解析的登入聯絡人角色。
  * 該授權檢查完全位於瀏覽器傳入的 `id` 解析（`Guid.TryParse`）以及任何 `DonationPaymentManager` 存取或分派之前。
  * 若授權失敗或 GUID 解析失敗，皆回傳相同的 `FeeAuditAccessDeniedMessage`，有效防止攻擊者透過錯誤訊息的差異來探測系統中聯絡人 GUID 的存在性。

## 3. 確定性的鎖釋放與取消逃脫 (Async Cancellation & Resource Cleanup)
* **分類**：Info
* **檔案路徑**：
  * `SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs` (第 741-751 行)
  * `SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs` (第 424 行)
* **評估原由**：
  * 在 `DonationPaymentManager.RetrieveFeeAuditByContactAsync` 中，`_feeRefreshLock.WaitAsync(cancellationToken)` 成功取得信號量後，進入 `try` 區塊，並在 `finally` 區塊中確定性地呼叫 `Release()`。這確保了不論非同步操作成功、失敗或被取消，鎖資源都會被安全釋放。
  * 控制器中的 `catch (Exception e) when (e is not OperationCanceledException)` 刻意排除了 `OperationCanceledException`，使取消訊號能正確逃脫通用錯誤處理，讓上層機制能識別並進行對應的連線中斷處理。

## 4. 數值溢位安全檢查 (Integer Overflow Protection)
* **分類**：Info
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Services/DonationFeeQueryService.cs`
* **具體行數**：第 188-200 行 (`RetrieveFeeAuditByContactAsync` 累加) 與第 316-320 行 (`MapFeeAuditRow` 單筆檢查)
* **評估原由**：
  * 在 `MapFeeAuditRow` 中，針對單筆金額進行了 `int.MinValue` 與 `int.MaxValue` 的範圍驗證。
  * 在 `RetrieveFeeAuditByContactAsync` 中，使用 `long totalAmount` 進行累加，並套用 `checked` 關鍵字。累加完成後再檢查是否超出 `int` 範圍，若超出則拋出 `OverflowException`。這雙重保護徹底消除了因惡意大額資料導致整數溢位的風險。

## 5. 隔離性與相容性邊界 (A/B Isolation & Rollback Boundary)
* **分類**：Info
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs`
* **具體行數**：第 399-422 行
* **評估原由**：
  * 當 `IsPackage01FeeReadEnabled` 為 `false` 時，系統完全走 legacy 相容路徑，回傳既有的表單模型總額與列表，確保了部署時的相容性與回滾安全。
  * 當旗標為 `true` 時，僅使用 typed Package01 唯讀操作，且不回寫或修改 `m_DonationPaymentFormModel`，達成了乾淨的讀寫分離。

---

# 總結 (Summary)

本次 P7.4 的最終硬化實作非常成功。程式碼不僅完全滿足了 **fail-closed 授權優先**、**不可變包裝防篡改**、**非同步鎖安全釋放** 與 **數值溢位防護** 等所有安全邊界要求，同時也透過 Source Contract Tests 與單元測試建立了穩固的防護網。本案在維持 `Package01FeeReadsEnabled=false` 的前提下，已具備隨時安全部署至本機環境的品質。

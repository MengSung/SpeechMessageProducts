以下是針對 P7.4 Batch A（atomic Package01 fee projection）未提交變更的程式碼審查報告。

---

# 程式碼審查報告 (Code Review Report)

## 1. 審查摘要 (Summary)
本次審查針對 P7.4 Batch A 的未提交變更進行。變更主要集中在 `DonationFeeQueryService` 及其對應的單元測試 `DonationFeeQueryServiceAsyncTests`。

變更的目的是為了解決 Package01 讀取路徑在 DTO 投影失敗時，可能導致 `DonationPaymentFormModel` 處於半成品（不一致）狀態的問題。修改後的實作採用了**「先在 request-local 區域變數中完成所有投影與計算，成功後才原子地更新 model」**的策略，並新增了對應的單元測試來驗證此原子性。

整體而言，變更設計良好，邏輯正確，且完全符合任務要求的範圍，沒有引入任何越界的功能或配置。

---

## 2. 審查發現 (Findings)

### Critical (嚴重)
*無*。未發現任何嚴重的安全性、正確性或效能問題。

### Warning (警告)

#### 1. 潛在的金額累加溢位風險 (Potential Integer Overflow in Amount Summation)
* **檔案路徑**: `SpeechMessageProducts.ChurchReport/Services/DonationFeeQueryService.cs`
* **行號**: 130-134
* **說明**: 在 `FillFeeListViaPackage01Async` 中，`totalAmount` 的型別為 `int`，並在迴圈中直接累加 `fee.Amount`（也是 `int`）。如果 DTO 集合中的費用筆數極多，或者單筆費用金額極大，累加結果可能會超出 `int.MaxValue`（2,147,483,647）。在 C# 預設的 `unchecked` 上下文中，這會導致數值溢位繞回（變成負數或錯誤的數值）而不會拋出異常；若專案啟用了 `CheckForOverflowUnderflow`，則會拋出 `OverflowException`。
* **建議**: 雖然這與 legacy 實作的行為一致，但建議在累加時使用 `long` 型別，並在賦值給 `model.TotalAmount` 之前進行邊界檢查與限制，以提高程式碼的健壯性。例如：
  ```csharp
  long total = 0;
  foreach (var fee in mappedFees)
  {
      total += fee.Amount;
  }
  if (total > int.MaxValue) total = int.MaxValue;
  if (total < int.MinValue) total = int.MinValue;
  model.TotalAmount = (int)total;
  ```

---

### Info (提示)

#### 1. 檔案編碼與亂碼問題 (File Encoding and Garbled Characters)
* **檔案路徑**: `SpeechMessageProducts.ChurchReport/Services/DonationFeeQueryService.cs`
* **說明**: 檔案中存在一些繁體中文註解和字串字面量（例如 `"??憟"`、`"?暸?"` 等）在讀取時顯示為亂碼。這可能是因為檔案儲存時使用了特定的編碼（如 Big5 或帶有特定 BOM 的 UTF-8），而 Git 或審查工具在解析時出現了偏差。
* **建議**: 雖然這些亂碼並非本次變更所引入（屬於既有程式碼的上下文），但建議在後續的重構或清理工作中，將所有原始碼檔案統一轉換為標準的 UTF-8 (with BOM) 編碼，以確保在不同平台和工具下的可讀性。

#### 2. 對 `rows` 的 Null 檢查 (Null Check for `rows`)
* **檔案路徑**: `SpeechMessageProducts.ChurchReport/Services/DonationFeeQueryService.cs`
* **行號**: 129
* **說明**: 程式碼中直接對 `rows` 呼叫了 `rows.Select(MapFeeDto)`。如果上游的 `RetrieveDedicationFeesByContactDateRangeAsync` 因為某種異常情況回傳了 `null`，這行程式碼會拋出 `ArgumentNullException`。
* **建議**: 雖然這同樣會阻止 `model` 被修改（符合原子性），但為了防禦性程式設計，建議在投影前對 `rows` 進行 null 檢查，或者確保 client 實作永遠不會回傳 `null`（而是回傳空集合）。

---

## 3. 任務要求驗證 (Task Requirements Verification)

* **正確性與原子性 (Correctness & Atomicity)**: **通過**。修改後的程式碼先在 local 變數中完成所有投影與計算，最後才原子地更新 model。若投影過程中拋出任何異常，`model` 的狀態將保持原樣。
* **Null/Fault 行為 (Null/Fault Behavior)**: **通過**。新增的單元測試 `Package01_fee_projection_fault_does_not_mutate_the_model` 成功模擬了 DTO 為 null 時的 fault 情況，並證實了 model 不會被部分修改。
* **非同步/取消語義 (Async/Cancellation Semantics)**: **通過**。`FillFeeListViaPackage01Async` 正確傳遞了 `CancellationToken` 並使用了 `ConfigureAwait(false)`。現有的測試也驗證了取消權杖的傳遞與非阻塞行為。
* **跨請求隔離 (Cross-Request Isolation)**: **通過**。所有計算均在 request-local 變數中進行，無共享狀態，保證了跨請求隔離。
* **資源所有權 (Resource Ownership)**: **通過**。無非受控資源洩漏風險。
* **範圍限制 (Scope Constraints)**: **通過**。未啟用任何 feature gate，未添加 CE 請求或 traffic switch，未移除 `ToolUtility`，亦無 P7.5 或 P8 的工作。`.trellis/tasks` 的變更僅為任務狀態更新，符合流程。

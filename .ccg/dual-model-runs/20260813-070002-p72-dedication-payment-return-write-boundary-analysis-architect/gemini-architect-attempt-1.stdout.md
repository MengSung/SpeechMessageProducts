## 1. Analysis (架構評估)

現有的定期定額認獻付款返回鏈（Recurring Dedication-Payment Return Chain）起點為 `RecurringDonationPaymentProcessor.cs:HandlePaymentReturn`。該流程目前高度耦合於 Dynamics SDK 實體（`Entity`）與 `ToolUtility` 的同步 I/O 操作。

### 當前遺留流程分析：
1. **讀取與等冪性檢查 (Read & Idempotency Check)**：
   - 透過 `RetrieveEntity` 取得 `new_dedication_booking`。
   - 透過 `RetrieveFeeByFetchXml` 檢查期數 `"001"` 的費用是否存在，或認獻的 `new_paid_period` 是否已為 `"001"`。若已存在，則直接返回已處理視圖。
2. **聯絡人更新 (Contact Mutation)**：
   - 讀取關聯的 `contact` 實體。
   - 解析 `new_visa_info`，若當前信用卡 Token 不在列表中，則格式化並追加寫入，隨後呼叫 `UpdateEntity`。
3. **費用建立 (Fee Creation)**：
   - 建立 `new_fee` 實體，設定期數、金額、大寫中文金額、支付狀態等。
   - 呼叫 `CreateEntity` 寫入資料庫，並透過 `AssignOwner` 指派擁有者。
4. **認獻更新 (Dedication Booking Mutation)**：
   - 更新 `new_paid_period` 為當前期數。
   - 根據總期數與當前期數的比較，更新狀態 `new_dedication_booking_status`。
   - 更新說明欄位 `new_explain`，並呼叫 `UpdateEntity`。
5. **通知發送 (Notification)**：
   - 透過 `m_PushUtility.SendMessage` 發送 LINE 訊息。

### 核心架構缺陷：
- **混合寫入邊界**：聯絡人信用卡更新、費用建立、認獻狀態更新與 LINE 通知混雜在同一個控制器方法中，缺乏事務隔離與補償機制。一旦中途失敗，會導致部分寫入（例如信用卡已更新，但費用未建立）。
- **缺乏 Fail-Closed 保護**：在網路逾時、Ambiguous 傳輸或部分寫入失敗時，系統無法安全地 fail-closed，且缺乏確定的清理（cleanup）與對帳（reconciliation）機制。
- **SDK 實體洩漏**：控制器直接操作 Dynamics SDK `Entity`，無法在無 I/O 的本地單元測試中進行完整驗證。

---

## 2. Architecture Decision (架構決策)

為了建立 fail-closed 且 DTO-only 的寫入邊界，我們採取以下決策：

### 決策 1：定義 DTO-only 觀察模型與決策合約
- **Rationale**：將所有 Dynamics SDK 實體與 I/O 隔離在邊界之外。控制器僅負責將原始資料轉換為純 DTO 觀察值（Observation），並交由無副作用的純函數（Pure Function）進行決策。
- **Rejected Alternatives**：繼續在控制器中混合使用 `Entity` 物件，或在決策層中引入唯讀的 SDK 實體。這會破壞本地單元測試的隔離性。
- **Assumptions**：所有必要的 Dynamics 欄位均可在邊界外被正確讀取並映射至 DTO。

### 決策 2：分離變更家族 (Mutation Families)
- **Rationale**：將寫入操作拆分為三個獨立治理的變更家族：
  1. **Contact Visa Mutation** (聯絡人信用卡資訊更新)
  2. **Fee Creation** (費用記錄建立)
  3. **Dedication Booking Update** (認獻狀態與期數更新)
- **Rejected Alternatives**：使用單一的大型 Plan 一併執行所有寫入。這會導致在部分步驟失敗時難以進行精確的對帳與補償。

### 決策 3：嚴格的 Fail-Closed 與 No-Replay 限制
- **Rationale**：任何逾時（Timeout）、狀態不明（Ambiguous）、或清理不確定性（Cleanup Uncertainty）均必須導向 `NoGo` 處置，且 `ProhibitsReplay` 必須為 `true`。
- **Potential Side Effects**：在不穩定的網路環境下，可能會產生較多的 `RequireReconciliation` 狀態，需要後續的對帳機制介入。

---

## 3. Implementation Plan (實施計劃)

### 步驟 1：定義本地 DTO 類型
在 `SpeechMessage.Dynamics.Abstractions/Operations/` 下新增 `P72RecurringPaymentLocalObservation.cs`：

```csharp
public sealed class P72RecurringPaymentLocalObservation
{
    public required bool IsComplete { get; init; }
    public required P72DonationPaymentOutcome Outcome { get; init; }
    public required bool DedicationBookingExists { get; init; }
    public required bool HasFirstPeriodFee { get; init; }
    public required string? PaidPeriod { get; init; }
    public required string? TotalStages { get; init; }
    public required bool IsCreditCardAlreadySaved { get; init; }
    public required string? CCToken { get; init; }
    public required string? OrderNo { get; init; }
}
```

### 步驟 2：建立決策解析器 (Decision Resolver)
新增 `P72RecurringPaymentLocalDecision.cs`，實現無副作用的決策邏輯：
- 若 `IsComplete` 為 `false` 或 `Outcome` 為 `Unknown/Pending` $\rightarrow$ 返回 `NoGo` (Unavailable)。
- 若 `HasFirstPeriodFee` 為 `true` 或 `PaidPeriod == "001"` $\rightarrow$ 返回 `AlreadyProcessed`。
- 若 `Outcome` 為 `Failed` $\rightarrow$ 返回 `RequireReconciliation`。
- 若為全新的成功付款 $\rightarrow$ 返回 `PrepareFutureGovernedDispatch`。

### 步驟 3：建立本地計畫生成器 (Local Plan Builder)
新增 `P72RecurringPaymentLocalPlanBuilder.cs`，根據決策結果生成 `P72ContinuationLocalPlan`：
- 計畫中應包含具名的 Operation IDs（例如 `Payments.RecurringContactVisa.Update`、`Payments.RecurringFee.Create`、`Payments.RecurringBooking.Update`）。
- 所有 Plan 的 `CeDispatchAllowed` 與 `ProductConsumerAllowed` 必須硬編碼為 `false`，以確保 P7.2 的 local-only 限制。

---

## 4. Considerations (考量事項)

- **性能預算 (Performance Budget)**：
  - 由於決策與計畫生成均為純記憶體操作（無 I/O），其執行時間應小於 1 毫秒，對 runtime 效能無影響。
  - 應避免在 DTO 中傳遞大型的 raw response 欄位，以控制記憶體佔用。
- **可維護性 (Maintainability)**：
  - 將中文金額轉換（`MoneyToChinese`）與期數解析（`ProcessStageNumber`）等輔助邏輯移至無副作用的 Utility 類別中，並為其撰寫完整的單元測試。
- **安全性與隔離性 (Security & Isolation)**：
  - 嚴禁在 DTO 中傳遞明文信用卡號或敏感的 CRM 憑證。
  - 確保 A/B 測試隔離，決策器中不得使用任何 `static` 可變狀態或 `ThreadLocal` 快取。

---

## 5. Findings (審查發現)

### 🔴 Critical

#### 1. 寫入操作與控制器邏輯高度耦合且缺乏事務隔離
- **檔案路徑**：`SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs` (第 264-282 行, 第 301 行, 第 345 行)
- **判定理由**：聯絡人信用卡更新（`UpdateEntity`）、費用建立（`CreateFee`）與認獻狀態更新（`UpdateEntity`）直接在 `HandlePaymentReturn` 中以同步且分散的方式執行。若其中一個步驟失敗，已執行的寫入無法回滾，且缺乏補償機制，這在分散式系統中會導致嚴重的資料不一致。必須將這些寫入操作隔離至獨立治理的本地計畫中。

#### 2. 信用卡 Token 寫入缺乏邊界保護
- **檔案路徑**：`SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs` (第 264-282 行)
- **判定理由**：系統直接在控制器中讀取並修改聯絡人的 `new_visa_info` 欄位。此寫入操作未經過任何獨立的授權或邊界驗證，容易受到呼叫端傳入惡意 Token 的影響。

---

### ⚠️ Warning

#### 1. 潛在的 Singleton 資源釋放風險
- **檔案路徑**：`SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs` (第 104 行)
- **判定理由**：在 `Dispose` 方法中呼叫了 `m_ToolUtilityClass.Dispose()`。如果 `m_ToolUtilityClass` 是透過 `ToolUtilityFactory.GetInstance` 取得的進程級（process-wide）共享 Singleton 實例，此呼叫將會釋放該共享資源，導致系統中其他依賴此實例的模組崩潰。

#### 2. 期數解析邏輯過於脆弱
- **檔案路徑**：`SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs` (第 954-977 行)
- **判定理由**：`ProcessStageNumber` 依賴 `OrderNo` 中是否包含底線 `_` 來解析期數。若訂單編號格式異常或未包含底線，系統會預設返回 `"001"`。這在非第一期的扣款異常時，可能會錯誤地判定為第一期，進而繞過等冪性檢查導致重複寫入。

---

### ℹ️ Info

#### 1. 輔助邏輯與控制器耦合
- **檔案路徑**：`SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs` (第 564-906 行)
- **判定理由**：`MoneyToChinese` 與 `IsCreditCardInList` 屬於無副作用的輔助邏輯，但目前直接寫在控制器類別中。建議在重構時將其移至獨立的 Utility 類別，以提高程式碼的可讀性與可測試性。

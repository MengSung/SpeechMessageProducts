# 多類別奉獻操作頁移植與金流同步分析報告

---

## 1. UX Analysis（使用者體驗分析與需求評估）

### 1.1 使用者旅程與操作體驗改善
* **多類別單次結帳**：現有 `DonationPaymentView.cshtml` 僅支援單一奉獻類別選擇（如「十一奉獻」）。移植參考頁的多類別模式後，會眾可在單次操作中同時填寫多筆（最多 7 列）不同類別與金額（例如：十一奉獻 $5,000 + 感恩奉獻 $1,000 + 宣教奉獻 $1,000），大幅減少重複輸入個資與多次跳轉金流頁面的摩擦。
* **定期定額單列限制 UX**：當付款方式切換為「信用卡定期定額(每個月)」時，介面需自動鎖定或提示為單一類別（單列），避免會眾對扣款週期與多項目拆帳產生困惑。
* **行動端（Mobile）與桌面端（Desktop）適應性**：7 列明細在手機小螢幕上使用 DevExtreme Form/DataGrid 時，需提供具備良好觸控目標（Touch Target $\ge 44\text{px}$）的增刪列按鈕，並於底部即時顯示「總金額動態計算與校驗」。

### 1.2 輔助功能與無障礙（Accessibility）
* **鍵盤導覽與 Screen Reader**：動態新增/刪除類別列時，需維持 DOM Focus 焦點管理，避免新增列後焦點遺失。
* **錯誤提示與實時校驗**：當總金額小於等於 0 或超過 7 列限制時，應有明確的 aria-live 告警提示。

---

## 2. Design Evaluation（設計系統與視覺規範評估）

### 2.1 現有模式一致性
* **DevExtreme 元件整合**：現有奉獻頁使用 DevExtreme Web 視覺樣式（`dx.generic.custom-scheme.css` 與 `DonationPaymentView.css`）。多類別列表可透過 DevExtreme DataGrid 或動態 Form Item Template 實現，維持與全站一致的表單邊框、聚焦色彩（`var(--theme-primary)`）與陰影樣式。
* **視覺 Token 與主題配對**：
  - 主色調：`--theme-primary: #0f766e;`
  - 邊框色：`--theme-border: #d9e6f2;`
  - 陰影與圓角：`border-radius: 16px;`

### 2.2 多類別與定期定額互動邏輯
* **類別選單動態過濾**：已選取的奉獻類別在後續列選單中應適度標示，避免重複選擇同一類別。
* **付款方式連動規則**：
  - 信用卡 / ATM / LinePay：開放 1～7 列。
  - 信用卡定期定額：自動強制收合為 1 列，並清空多餘列數。

---

## 3. Technical Considerations（前端與後端架構分析）

### 3.1 現有資料流分析 (Current Data Flow)
```
[User Form UI] 
      │
      ▼ (POST /Dedication/SaveDonationPaymentDedication)
[DedicationController]
      │ TryRestoreDonationDonorIdentity() Verify Session
      ▼
[InMemoryContext.DonationPaymentManager]
      │ m_Contact & m_DonationPaymentFormModel
      ▼
[DonationFeePaymentProcessor]
      │ CreateFeeAsync / CreateFee (CRM Entity "new_fee")
      ▼
[SpeechMessage.Payments Core / Gateway]
      │ Generate PayToken & Redirect Provider
      ▼
[PaymentReturnController] (Bank Callback / User Return)
      │ ReturnCore -> IPaymentGateway.ParseCallbackAsync
      ▼
[DonationPaymentReturnWorkflow]
      ├──> [ChurchReportPaymentRecordUpdater] (Sync CRM new_really_paid_amount & new_pay_status)
      └──> [ChurchReportPaymentPayerNotifier] (Send LINE Gratitude/Failure Notification)
```

### 3.2 缺少的契約與欄位 (Missing Contracts & Models)
1. **`DonationPaymentFormModel` 缺乏多類別集合契約**：
   現有模型僅有單一 `Category`、`Amount`、`Others` 屬性。需擴充多類別明細子集契約（例如 `List<DonationCategoryItemDto> Items`），單列最大限制設為 7。
2. **`DonationPaymentSubmissionService` 驗證規則**：
   缺少 1～7 列數量邊界校驗、列金額合計校驗、定期定額單列強制驗證。
3. **CRM 拆帳/彙整紀錄契約**：
   `DonationFeePaymentProcessor.FeeManagement.cs` 的 `SetFeeCategoryInfo` 僅支援單一 `new_pay_category` 與 `new_others`。需補充「彙整字串（如 `十一奉獻:5000, 感恩:1000`）」或多筆 `new_fee` 建立邏輯，確保 CRM 實收金額與明細可精確紀錄。

### 3.3 最小修改檔案清單 (Minimal Modifiable Files)

| 檔案路徑 | 變更責任範圍 |
| :--- | :--- |
| `SpeechMessageProducts.ChurchReport/Models/DonationPaymentFormModel.cs` | 新增 `DonationCategoryItemDto` 明細集合與預設校驗邏輯 |
| `SpeechMessageProducts.ChurchReport/Views/Dedication/DonationPaymentView.cshtml` | 移植 1~7 列多類別動態表單、定期定額單列切換 JS 邏輯 |
| `SpeechMessageProducts.ChurchReport/Services/DonationPaymentSubmissionService.cs` | 擴充多類別金額總計驗證與定期定額單列限制 |
| `SpeechMessageProducts.ChurchReport/Payments/DonationPaymentFormModelMapper.cs` | 將多類別 `DonationPaymentFormModel` 映射至金流 `PaymentOrderDraft` |
| `SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationFeePaymentProcessor.FeeManagement.cs` | 彙整多類別奉獻項目寫入 CRM `new_fee` 欄位與總金額計算 |
| `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentFormModelNamingTests.cs` | 補齊多類別模型與單元測試斷言 |

### 3.4 主要安全、Session 隔離與資源生命週期風險

* **Critical - Session 跨使用者洩漏風險**：
  `DonationPaymentManager.m_DonationPaymentFormModel` 在歷史架構中曾被掛載於 Singleton/InMemoryContext。必須確保每次 Request 存取時均透過 Request Scope 重新初始化，或於 `DedicationController.SaveDonationPaymentDedication` 中確認 `TryRestoreDonationDonorIdentity` 失敗時確定性清空模型，嚴禁跨 Session 殘留個資。
* **Warning - Callback 冪等性（Idempotency）風險**：
  銀行與第三方金流 Callback（`PaymentReturnController`）可能因網路重試發送多次。`ChurchReportPaymentRecordUpdater` 更新 CRM `new_fee` 時，需先檢查該筆單號是否已處於「已付款（`new_pay_status`）」狀態；若已完成，應直接回傳成功，不得重複累加實收金額或重複觸發 LINE 推播。
* **Warning - 資源生命週期與 HttpClient 釋放**：
  `DonationFeePaymentProcessor` 與 `DedicationController` 存取外部 API 時，需透過 `IHttpClientFactory` 產生實例，並對 `FormUrlEncodedContent` 與 `HttpResponseMessage` 使用 `using var` 確定性釋放，防止 Socket 或 Stream 洩漏。
* **Info - 敏感資訊洩漏防範**：
  日誌紀錄（Trace/Console）與例外處理中，嚴禁輸出 `appsettings.json` 的金流 HashKey、IV、LINE Channel Secret 或信用卡 CCToken 完整明文。

---

## 4. Options（方案比較與權衡）

| 比較項目 | 方案 A：CRM 單筆收費單彙整模式 (Preferred) | 方案 B：CRM 多筆獨立收費單模式 |
| :--- | :--- | :--- |
| **做法描述** | 一次金流交易對應一筆 CRM `new_fee` 紀錄，金額為 7 列總合，類別與明細串接存於 `new_explain` / `new_others`。 | 一次金流交易依類別拆分為最多 7 筆 CRM `new_fee` 紀錄，共享同一個金流交易單號（PayToken）。 |
| **金流整合** | 與 `SpeechMessage.Payments` 100% 相容，訂單金額完全一致，Callback 冪等最容易維持。 | Callback 需遍歷更新 1~7 筆 CRM 紀錄，若中途失敗易造成資料不一致。 |
| **CRM 報表** | 需透過解析備註字串或拆分欄位進行統計。 | 報表查詢簡單，但增加了 CRM 實體建立與數據膨脹開銷。 |
| **移植複雜度** | 低（最小修改檔案、風險可控）。 | 高（需大幅重構 `DonationFeePaymentProcessor` 與後續對帳流程）。 |

---

## 5. Recommendation（建議方案與測試策略）

### 5.1 推薦採行方案
推薦採用 **方案 A（CRM 單筆收費單彙整模式）**，並搭配前端 DevExtreme 動態 DataGrid / Form items 實現多類別體驗：
1. **多類別上限控制**：前端與後端均設定上限為 7 列，計算 `TotalAmount = Sum(Amount_1 .. Amount_N)`。
2. **定期定額單列鎖定**：切換至「信用卡定期定額」時，自動切換至單一列視圖，並禁用「新增類別」按鈕。
3. **Session 與身分隔離**：確保 `DonationPaymentFormModel` 僅在當次 Request 內傳遞，不依賴長生命周期之全域變數。
4. **CRM 狀態同步**：在 `ChurchReportPaymentRecordUpdater` 內進行冪等檢查，金流成功回呼時一次同步實收金額與付款狀態。

### 5.2 建議測試案例 (Test Cases)

1. **單元測試 (Unit Tests)**：
   - `ValidateDonationForm_WithMultiItems_ShouldPassWhenValid`：測試 1~7 列正常金額組合。
   - `ValidateDonationForm_ExceedsSevenItems_ShouldFail`：測試超過 7 列時傳回驗證失敗訊息。
   - `ValidateDonationForm_RecurringWithMultipleItems_ShouldFail`：測試定期定額設定多列時攔截。
2. **Session 隔離測試**：
   - 模擬兩位不同 Session 扣款者同時提交表單，驗證各自 `FullName`、`DedicationNumber` 與奉獻明細完全隔離，無交叉洩漏。
3. **Callback 冪等與 CRM 同步測試**：
   - 模擬第三方金流 Callback 連續發送兩次 200 OK 通知，驗證 CRM `new_really_paid_amount` 只寫入一次，且 LINE 感謝通知僅發送一次。

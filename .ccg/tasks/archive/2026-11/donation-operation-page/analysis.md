# 奉獻操作頁面多類別改造 — 系統分析（唯讀）

Date: 2026-09-15
Role: Claude Systems Analyst（雙模型分析階段）
Scope: `SpeechMessageProducts.ChurchReport` 奉獻頁與付款回呼、`SpeechMessage.Payments` 系列整合
約束：僅分析，不修改任何程式碼；不輸出 appsettings 秘密值。

---

## 1. 現有資料流

### 1.1 表單提交（GET 顯示 / POST 送出）

```
DedicationController.DonationPaymentView (GET)
  → RestoreWebLoginDonationPaymentModel() / TrySetDonationPaymentModelForLineUser()
  → DonationPaymentManager.SetDonationPaymentModel(contact)
      → DonationPaymentModelAssembler.Build(...)   // CRM task、OptionSet、信用卡、認獻清單組裝
  → View(DonationPaymentFormModel)   // 单一 Category / Amount / Others 欄位

DedicationController.SaveDonationPaymentDedication (POST)
  → TryRestoreDonationDonorIdentity()   // Session 還原奉獻者身分（LINE / 網頁登入互斥）
  → DonationPaymentManager.SaveDonationPaymentDedicationAsync(formModel)
      → DonationPaymentSubmissionService.ValidateDonationForm(formModel)
      → m_Contact 檢查（fail-closed，無身分即拒絕建單）
      → DonationPaymentProcessor.CreateFeeAsync(contact, formModel)
          → switch(formModel.PayWay):
              信用卡/銀聯卡/null → ProcessCreditCardPayment
              信用卡定期定額(每個月) → ProcessRecurringPayment
              行動支付 → ProcessMobilePayment
              LinePay → ProcessLinePayPayment
              ATM轉帳/匯款 → ProcessAtmPayment
          → 各分支各自呼叫 CreateFee(...) → SetFeeParameter(...) → 建立「單一」new_fee 收費單
              SetFeeAmounts: new_fee_shoud_pay = formModel.Amount（單一 int，非陣列）
              SetFeeCategoryInfo: new_category = formModel.Category（單一字串，非陣列）
      → 回傳 { status, message, DedicationResult, PayWay } JSON
```

**關鍵觀察：`DonationPaymentFormModel` 是「單列」模型**（`Category`、`Amount`、`Others` 都是純量欄位，不是集合）。`DonationPaymentProcessor.CreateFeeAsync` 對應地只建立**一筆** `new_fee` 收費單。需求要求的「最多七列多類別奉獻」目前在資料模型、View、Processor 三層都不存在對應結構。

### 1.2 付款回呼（Callback / Return）

```
PaymentReturnController.Return (GET/POST, 新舊路由並存)
  → PaymentHttpRequestMapper.MapAsync(Request, profile, Sinopac, ...)
  → IPaymentGateway.ParseCallbackAsync(callbackRequest)      // SpeechMessage.Payments 核心：簽章/解密/驗證
  → IPaymentGateway.QueryPaymentAsync(...)                   // 查詢 provider 最新狀態
  → IDonationPaymentReturnWorkflow.HandleReturn(shopNo, providerOrderRef, statusResult)
      → DonationPaymentReturnWorkflow：PaymentStatusResult → DonationPaymentWorkflowResult（產品層轉譯）
      → IDonationPaymentProductWorkflowDispatcher（DI: DonationPaymentProductWorkflowDispatcher）
          → IsDedicationBooking(category)?
              是 → RecurringDonationPaymentProcessor.HandlePaymentReturn(...)
              否 → new DonationFeePaymentProcessor(toolUtilityProvider, lineNotificationWorkflow)
                     .HandlePaymentReturn(shopNo, payToken, paymentResult)
```

`DonationFeePaymentProcessor.HandlePaymentReturn` 是真正落地 CRM 的地方：

1. 依 `paymentResult.ProductEntityId` 取回單一 `new_fee` 收費單。
2. 用 `existingPaymentRecords.Contains(paymentResult.OrderNo)` + `currentPayStatus == 100000000`（尚未繳費）做**冪等判斷**（`hasProcessedOrder` / `SuccessAlreadyProcessed` 分支）。
3. 成功且未處理過：`new_fee_really_paid` 用**累加**（`existing + this payment`）、寫 `new_payment_records`、更新 `new_pay_status`、儲存信用卡 token、推播 LINE。
4. 已處理過：只回成功結果頁，不重複寫 CRM／不重複發 LINE（這是 RETURN_URL + BACKEND_URL 雙重回呼的防重機制）。

### 1.3 與 `SpeechMessage.Payments` 的邊界

- `SpeechMessage.Payments`（Abstractions/Gateway/Providers/Sinopac/Taishin/MyPay）只處理 provider 協定：簽章、callback 解析、狀態查詢，回傳中性的 `PaymentCallbackResult` / `PaymentStatusResult`。
- `SpeechMessage.Payments.Workflows`（`PaymentPostPaymentWorkflow`、`IPaymentRecordUpdater`、`IPaymentPayerNotifier`）是**已抽離但目前未實際掛接**的共用付款後管線：
  - Startup.cs 有註冊 `ChurchReportPaymentRecordUpdater` / `ChurchReportPaymentPayerNotifier` / `ChurchReportPaymentContextBuilder`（579-584 行）。
  - 但 `DonationPaymentProductWorkflowDispatcher.HandleFeeReturn` 是用 `new DonationFeePaymentProcessor(_toolUtilityProvider, _lineNotificationWorkflow)`（簡易建構子）手動 `new` 出來，**沒有經過 DI**，因此永遠落入 `CreateNoOpPostPaymentWorkflow()` 分支，`m_PaymentContextBuilder == null`，`ExecutePostPaymentWorkflowIfAvailable` 直接 return，共用 workflow 形同虛設。
  - 真正生效的 CRM 更新/冪等/LINE 通知邏輯，100% 落在 `DonationFeePaymentProcessor.HandlePaymentReturn` 這個「舊版但目前唯一生效」的路徑。

### 1.4 Session isolation 現況

`DedicationController` 對 LINE 與網頁登入兩種情境已有相當完整的隔離設計：
- `TryGetVerifiedLineUserId()` 以 Session 內、經 `VerifyLineIdTokenAsync`（LIFF ID Token 對 LINE 官方驗證）寫入的值為唯一權威來源，並與 `LineBindingViewModel` 比對防止跨使用者換綁（`DedicationController.cs:582-621`）。
- `TryRestoreDonationDonorIdentity()`（POST 專用）刻意讓 LINE 情境與網頁情境互斥，避免「畫面顯示 LINE 使用者 B、送出卻用網頁登入 A 的身分建單」的跨人記帳（`DedicationController.cs:232-280`，註解已詳述根因）。
- `ClearLineDonationState()` 在任何身分驗證失敗時，同步清除 `manager.m_Contact`、`m_DonationPaymentFormModel` 個資欄位、`LineBindingViewModel` 欄位、Session key，防止資料殘留給下一位使用者。
- 但 `DonationPaymentManager.m_DonationPaymentFormModel` 是**單一長生命週期物件**（該 Manager 本身是 request-scoped，但 FormModel 欄位是可變的引用，貫穿整個 request 內多次 AJAX 呼叫）。目前只有「單一 Category/Amount」，一旦改為多列陣列，必須確認新的陣列欄位在 `EnsureFormDefaults()`、`ClearLineDonationState()`、`BuildDedicationFeeLineFormModel()` 等既有清空路徑都同步清空，否則會出現「多列資料跨使用者殘留」的新型態隔離漏洞。

---

## 2. 缺少的契約（Gap Analysis）

| # | 缺口 | 說明 |
|---|------|------|
| G1 | **多列奉獻資料模型** | `DonationPaymentFormModel` 沒有 `List<DonationLineItem>`（Category/Amount/Others 各列）欄位；`SpeechMessage.Payments.Workflows` 已有 `PaymentLineItemDraft`/`PaymentOrderDraft` 可重用的多行結構，但 ChurchReport 尚未接上。 |
| G2 | **單筆送出建立多筆收費單的契約** | `DonationPaymentProcessor.CreateFeeAsync` 目前是「一次呼叫 = 一筆 new_fee」。多類別送出時，需要新契約決定：(a) 一次送出建立 N 筆 new_fee（每列一筆），還是 (b) 一筆 new_fee 帶多筆明細子項。兩者對應的付款金流建單金額（單一 order 的總金額 vs 多筆 order）、CRM 收費單结构、以及回呼後如何把「一筆 provider 訂單的錢」分攤回「N 筆收費單」都尚無設計。 |
| G3 | **定期定額單列限制的強制點** | 需求「定期定額限制為單一類別」目前只是 UI 慣例（View 用 `OnPayWaySelectBoxValueChanged` 顯示/隱藏欄位），沒有伺服器端驗證。`DonationPaymentSubmissionService.ValidateDonationForm` 需要新增：PayWay 為「信用卡定期定額」時，多列陣列長度必須 = 1，否則要有明確拒絕契約。 |
| G4 | **ATM 在共用付款後流程的分類** | `DonationFeePaymentProcessor.HandlePaymentReturn` 內的通知文案、`new_pay_way` OptionSet 寫入（`100000001` 信用卡）、Description 標題（「💳 信用卡交易通知」）**全部硬編碼為信用卡語意**，沒有依 `paymentResult.PayType` 或原始 `PayWay` 分流。若 ATM 回呼也走這個處理器，CRM 會被錯誤標記為信用卡付款方式，且 LINE 文案會誤導使用者。這是移植「信用卡與 ATM 並存」體驗前必須先補的契約：**回呼路徑需要知道原始付款方式，才能對應正確 OptionSet 與文案**。 |
| G5 | **多列建單的冪等鍵設計** | 現有冪等靠 `new_payment_records.Contains(OrderNo)` + `new_pay_status == 未繳費`，鍵是「單一 new_fee 的單一 OrderNo」。若一次送出產生多筆 new_fee 對應同一個 provider 訂單號，需要新的冪等契約：以「OrderNo + FeeEntityId」複合鍵判斷「這筆收費單是否已經被這個訂單處理過」，否則會出現「N 筆收費單只有 1 筆被更新、其餘漏更新」或「RETURN_URL/BACKEND_URL 雙回呼時 N 筆全部重複入帳」的兩種相反風險。 |
| G6 | **共用 `PaymentPostPaymentWorkflow` 与舊版邏輯的取捨** | 目前 DI 註冊了 `IPaymentRecordUpdater`/`IPaymentPayerNotifier`（`ChurchReportPaymentRecordUpdater`/`ChurchReportPaymentPayerNotifier`），但 `PaymentCrmService.UpdateFeeEntityWithPaymentResult` **沒有任何冪等判斷**（直接 `SetOptionSetAttribute` + `SetEntityMoneyAttribute` 覆蓋為應付金額），且該路徑目前因為 dispatcher 手動 `new` 處理器而未被實際呼叫（見 §1.3）。若後續改造想要「正確接上共用管線」，必須先把冪等判斷從 `DonationFeePaymentProcessor` 遷移進 `ChurchReportPaymentRecordUpdater`，否則一旦切換路徑，會直接遺失現有的重複回呼防護（回歸性風險）。 |
| G7 | **Session 對多列資料的清空契約** | `ClearLineDonationState()`、`BuildDedicationFeeWebFormModel()` 目前逐欄位手動清空（`FullName`、`Mobile`、`DedicationNumber`...）。新增多列陣列後，這些清空路徑必須同步加入清空邏輯，目前沒有「新增表單欄位時必須同步更新清空清單」的契約或測試守門（`DonationPaymentFormModelNamingTests` 等測試可作為守門起點，但目前測試內容需要確認是否涵蓋欄位清空完整性）。 |

---

## 3. 最小修改檔案清單（依需求推導，非實作，僅供後續規劃）

> 以下清單是「若要落地本需求，理論上需要碰觸的檔案」，供任務拆解與雙模型審查參考，**本次分析不修改任何檔案**。

| 分類 | 檔案 | 預期修改性質 |
|------|------|--------------|
| 資料模型 | `SpeechMessageProducts.ChurchReport/Models/DonationPaymentFormModel.cs` | 新增 `List<DonationLine>`（Category/Amount/Others，上限 7）欄位與 `EnsureFormDefaults` 對應初始化/裁切邏輯 |
| 表單驗證 | `ChurchReport.Payments` 內 `DonationPaymentSubmissionService`（`ValidateDonationForm`） | 新增：陣列長度上限 7、金額 > 0、定期定額必須單列的驗證規則 |
| 建單流程 | `WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.FeeManagement.cs`（`CreateFeeAsync`/`SetFeeParameter`/`SetFeeAmounts`/`SetFeeCategoryInfo`） | 改為依多列迴圈建立對應筆數的 `new_fee`，並決定合併/拆分金流訂單策略 |
| 建單流程 | `WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.PaymentProcessing.cs`（`ProcessCreditCardPayment`/`ProcessAtmPayment`/`ProcessRecurringPayment` 等） | 依上面策略調整：多列信用卡/ATM 建單金額加總邏輯；定期定額分支需強制單列 |
| 回呼冪等 | `Tools/DonationFeePaymentProcessor.cs`（`HandlePaymentReturn`） | 冪等鍵擴充為 OrderNo+FeeEntityId（若走多筆收費單方案）；補上依 PayWay/PayType 分流文案與 `new_pay_way` OptionSet，避免 ATM 被誤標信用卡 |
| Controller | `Controllers/DedicationController.cs`（`ClearLineDonationState`/`BuildDedicationFeeWebFormModel`/`SaveDonationPaymentDedication`） | 同步清空/還原多列欄位，維持既有 Session isolation 不變式 |
| View | `Views/Dedication/DonationPaymentView.cshtml` | 改用 DevExtreme Grid/Repeater 呈現最多七列（Category/Amount/Others），定期定額切換時鎖定為單列 UI |
| 模型組裝 | `DonationPaymentModelAssembler`（`ChurchReport.Payments` 內，`DonationPaymentManager` 建構子中注入） | 若組裝流程需要為多列補齊每列的類別下拉資料來源，需同步調整 |
| 測試 | `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentFormModelMapperTests.cs`、`PaymentReturnControllerTests.cs`、`DonationPaymentManagerNamingTests.cs` | 新增多列情境測試；既有測試需確認未假設「單一 Category/Amount」 |

---

## 4. 主要風險（Critical / Warning / Info）

### Critical

1. **ATM 回呼文案與 OptionSet 誤標為信用卡**（`Tools/DonationFeePaymentProcessor.cs` 全檔）
   - 現況：`HandlePaymentReturn` 的 Description、LINE 訊息、`new_pay_way` 寫入、`ViewBag.PaymentMethod` 全部硬編碼「信用卡」，未依實際 `PayWay`/`PayType` 分流。
   - 失效情境：使用者以 ATM 轉帳完成付款，回呼進到同一個處理器，CRM 收費單 `new_pay_way` 被寫成「信用卡已繳費」，客服對帳與財務報表會顯示錯誤付款方式；LINE 通知文案也會誤導使用者「信用卡交易通知」。
   - 若本次移植要「保留信用卡與 ATM 並存」，此為必須先修的既有缺陷，否則新版多列表單只會放大既有錯誤的影響範圍（N 倍收費單被誤標）。

2. **多列建單與現有單一冪等鍵不相容**
   - 現況：冪等判斷綁在單一 `new_fee`（`ProductEntityId`）與單一 `OrderNo`。
   - 失效情境：若一次送出 7 列對應 7 筆 `new_fee` 但共用 1 個 provider 訂單號，RETURN_URL/BACKEND_URL 雙回呼時，目前 `hasProcessedOrder` 邏輯只判斷「這一筆收費單」是否處理過，需要每筆各自正確判斷；若實作疏忽用「訂單層級」一次性旗標，會出現「第一筆收費單處理後，其餘 6 筆收費單被誤判為『已處理』而漏更新實收金額」或反向的「全部重複入帳」。

3. **共用 `PaymentPostPaymentWorkflow` 路徑缺乏冪等防護，且與生效路徑並存但未接通**
   - 現況：`PaymentCrmService.UpdateFeeEntityWithPaymentResult` 無論呼叫幾次都會覆蓋 CRM 為「已繳費 + 應付金額」，且外層 `ChurchReportPaymentPayerNotifier` 每次呼叫都會發 LINE，沒有 `hasProcessedOrder` 等價機制。
   - 目前因為 `DonationPaymentProductWorkflowDispatcher` 手動 `new DonationFeePaymentProcessor(...)`（簡易建構子）而未被觸發，屬於「死碼但已註冊進 DI」的狀態。
   - 風險：後續重構若有人「順手」把 dispatcher 改成用 DI 解析出完整建構子（例如為了讓多列邏輯更好測試而重構建構方式），會在不知情的情況下移除掉冪等防護與信用卡 token 儲存、課程報名狀態更新等 `DonationFeePaymentProcessor` 特有邏輯，造成重複入帳且不易在程式碼審查中被發現（因為兩條路徑外觀相似、都宣稱走「共用 workflow」）。

### Warning

4. **`DonationPaymentFormModel` 是可變的長生命週期物件，多列陣列若忘記在既有清空路徑補齊會造成資料殘留**
   - `ClearLineDonationState()`、`BuildDedicationFeeWebFormModel()` 找不到登入者時目前逐欄位手動清空。新增 `List<DonationLine>` 後，若忘記在這兩處清空，LINE 使用者 A 登出後、使用者 B 用同一個瀏覽器 Session 進入奉獻頁，理論上可能看到 A 殘留的多列奉獻明細（金額、類別），這是比單一欄位殘留更嚴重的資訊外洩（多筆金額/類別比單一姓名更能推斷他人奉獻行為）。

5. **定期定額「單列限制」目前只在前端 JS 强制（`resetPaymentUI`/`OnPayWaySelectBoxValueChanged`），沒有後端驗證**
   - 惡意或異常用戶端（繞過瀏覽器直接 POST）可以送出「PayWay=信用卡定期定額」但帶多列資料，若 `ProcessRecurringPayment` 沒有在伺服器端拒絕，可能建立非預期的多筆定期定額認獻紀錄，且金額/期數計算可能沿用單列假設而算錯。

6. **建單金流與 CRM 收費單筆數的對應策略未定，影響金額對帳**
   - 若採「N 列 = N 筆 new_fee 但共用 1 筆 provider 訂單」的設計，`new_fee_really_paid` 的分攤邏輯（例如訂單金額如何按列拆分入帳、留意四捨五入導致總額與各列加總不一致）目前完全沒有既有程式碼可參考，需要新契約與新測試，屬於本次改造中風險最高的計算邏輯。

### Info

7. `SpeechMessage.Payments.Workflows` 已有 `PaymentOrderDraft`/`PaymentLineItemDraft`/`PaymentScheduleDraft` 型別，語意上已支援多明細訂單草稿，值得評估是否可重用其結構承載「七列奉獻明細」，避免 ChurchReport 自行重新發明陣列型別，同時維持「產品層資料模型 vs 金流核心中性模型」的既有分工原則（`DonationPaymentFormModel` 類別註解已明確此分工）。

8. 目前 `PaymentReturnController` 與 `DonationFeePaymentProcessor` 對「哪一種付款方式」的判斷高度依賴 `paymentResult.OrderNo.StartsWith("C")`（信用卡訂單編號前綴慣例）等字串慣例，屬於隱性契約，建議在擴充 ATM/多列邏輯時，把這類「靠訂單編號前綴猜付款方式」的隱性規則顯式化為欄位（例如沿用 `PayType`），降低多列/多 provider 情境下猜錯的機率。

---

## 5. 建議測試

### 單元測試（新增/擴充於 `ChurchReport.MemberInfo.Tests/Payments`）

1. `DonationPaymentFormModelMapperTests`：新增「多列（1、7、超過 7）」序列化/繫結測試，確認超過 7 列時的裁切或拒絕行為與需求一致。
2. `DonationPaymentSubmissionServiceTests`（若不存在需新建）：
   - 定期定額 + 多列 → 必須回傳驗證失敗訊息。
   - 空列表 / 全零金額 → 必須回傳驗證失敗訊息。
   - 正常 1~7 列 + 信用卡/ATM → 通過驗證。
3. `DonationFeePaymentProcessorTests`（若不存在需新建，針對 `HandlePaymentReturn`）：
   - 同一 `OrderNo` 對「多筆」`new_fee` 各自呼叫一次 `HandlePaymentReturn`，驗證每筆各自的冪等旗標互不干擾（不會 A 筆處理後讓 B 筆被誤判為已處理）。
   - RETURN_URL 後緊接 BACKEND_URL（相同 OrderNo、相同 FeeEntityId）→ 驗證第二次呼叫不重複累加 `new_fee_really_paid`、不重複發 LINE。
   - ATM 付款結果 → 驗證 CRM `new_pay_way` 寫入值與 LINE 文案不是信用卡專屬文字（若本次一併修正 G4）。
4. `PaymentReturnControllerTests`（既有檔案擴充）：多筆收費單對應同一 provider callback 的端對端解析測試。

### Session / 隔離測試

5. 模擬「LINE 使用者 A 建立 7 列奉獻明細但未送出 → 觸發 `ClearLineDonationState` → 使用者 B 用同一 Session 進入」，斷言 B 看到的表單多列陣列為空、不含 A 的任何金額或類別。
6. 模擬「網頁登入 A 送出多列奉獻 → 登出 → LINE 使用者 B 進入奉獻收費清單頁」，斷言 `BuildDedicationFeeWebFormModel`/`BuildDedicationFeeLineFormModel` 回傳的模型不殘留 A 的多列資料。

### 整合/端對端測試

7. 建立 7 列奉獻（含至少 2 種類別重複、1 列金額為極小值 1 元）→ 走信用卡建單 → 模擬 provider callback 兩次（RETURN_URL + BACKEND_URL）→ 驗證：
   - CRM 中恰好新增 7 筆（或依所選方案的預期筆數）收費單，且每筆金額與所選類別對應正確。
   - 總實收金額 = 7 列金額加總，無四捨五入落差或遺漏。
   - LINE 只收到一次成功通知（不因兩次 callback 而收到兩次）。
8. 定期定額（單列強制）情境：以 API 直接 POST 多列 + PayWay=定期定額，驗證伺服器端拒絕而非僅前端隱藏欄位。
9. ATM 多列情境：確認 ATM 虛擬帳號建立時的金額 = 多列加總，且之後的付款確認回呼能夠正確找到並更新對應的多筆收費單（或合併收費單，依採用的方案）。

---

## 6. 待決策問題（需與需求方/雙模型審查確認後才能進入實作階段）

1. 「最多七列」送出後，CRM 應該產生 **N 筆獨立 new_fee**，還是 **1 筆 new_fee + 明細子表**？這決定了 G2、G5、G6、Critical #2 的具體解法，也決定金流建單是「1 個訂單金額 = 總和」還是「N 個訂單」。
2. ATM 与信用卡是否要走同一個 `DonationFeePaymentProcessor.HandlePaymentReturn`（需先修 Critical #1），還是拆出獨立的 ATM 回呼處理路徑？
3. 是否藉此機會把 `ChurchReportPaymentRecordUpdater`/`PaymentPostPaymentWorkflow` 這條目前「已註冊但未接通」的共用管線，正式接上並補齊冪等邏輯，取代 `DonationFeePaymentProcessor` 內建的字串式冪等判斷？若是，需要額外的遷移期回歸測試。

---

（本檔案僅供分析與後續雙模型審查參考，未變更任何原始程式碼。）

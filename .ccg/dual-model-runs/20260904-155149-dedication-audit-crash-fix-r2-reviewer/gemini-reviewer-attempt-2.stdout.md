# 奉獻稽核當機修復 (Dedication Audit Crash Fix R2) 程式碼審查報告

本次審查方針針對工作樹中相對於 `HEAD` 的所有未提交變更進行詳細檢查，重點驗證導覽頁面進入當機防禦、個資隔離與 Fallback 正確性、AJAX/Grid null 安全性、單元測試品質以及系統層面規範（Session isolation、記憶體生命週期、繁體中文文件、UTF-8 無 BOM / CRLF 與效能）。

---

## 審查總結 (Summary)

本次修復品質優良，成功解決了從 Layout 導覽列進入 `DedicationAuditController.DedicationFeeAuditViewWeb` 時，因 Request-scoped `DonationPaymentManager.m_Contact` 為 `null` 導致的當機問題。

1. **導覽進入當機成功修復**：改由 `InMemoryContext.PersonalInfomationModel?.m_LoginContact` 判斷登入狀態，若為 `null` 則走安全 Fallback，完全避免呼叫會拋出 `ArgumentNullException` 的 `SetDedicationFeeList(Entity)`。
2. **個資安全隔離完全符合**：Fallback 流程顯式清空了姓名、手機、奉獻編號、身分證字號、後六碼、奉獻清單、同名清單並將總額歸零，確保不殘留上一位使用者的資訊。
3. **AJAX/Grid 邊界保護嚴密**：透過新增的 `EnsureAuditFormModel` Helper，確保所有 DataGrid 入口點在表單模型未建立時皆能安全回傳空集合，不拋出 `NullReferenceException`。
4. **測試真實且具防禦力**：新增 4 項 xUnit / FluentAssertions 單元測試，且專案檔已加入 `[InternalsVisibleTo("ChurchReport.MemberInfo.Tests")]` 避免脆弱私有反射測試。
5. **符合專案規範**：註解為正體中文，檔案維持 UTF-8 無 BOM 及 CRLF，無記憶體洩漏與效能疑慮。

---

## 核心審查要點回應 (Key Review Points Verification)

### 1. `DedicationFeeAuditViewWeb` 從 Layout 導覽進入之當機防禦
- **結論**：**通過 (Pass)**。不會再因 `m_Contact` 為 `null` 當機。
- **證據與分析**：
  - 在 `SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs`（第 75-87 行），`DedicationFeeAuditViewWeb()` 呼叫內部建模方法 `BuildAuditWebFormModel()`。
  - `BuildAuditWebFormModel()`（第 134-156 行）檢查 `loginContact = InMemoryContext.PersonalInfomationModel?.m_LoginContact`。當由 Layout 導覽選單進入時，`loginContact` 為 `null`，程式會自動避開傳入 `null` 給 `SetDedicationFeeList(Entity)` 的路徑，改呼叫 `EnsureAuditFormModel(manager)` 並清空個資後回傳安全空白表單。
  - `SetupAuditViewBag(false)`（第 115 行）亦針對 `m_DonationPaymentFormModel?.IsAOfficeWorker` 使用了 null 安全條件運算子 `?.`，確保 ViewBag 初始化過程無 NullReference 疑慮。

### 2. `BuildAuditWebFormModel` 的 null-safe fallback 與個資隔離
- **結論**：**通過 (Pass)**。Fallback 設定正確，個資與清單無洩漏風險。
- **證據與分析**：
  - 在 `SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs`（第 144-155 行）：
    ```csharp
    var model = EnsureAuditFormModel(manager);
    model.FullName = string.Empty;
    model.Mobile = string.Empty;
    model.DedicationNumber = string.Empty;
    model.NationId = string.Empty;
    model.LastSixDigit = string.Empty;
    model.DedicationFeeList ??= new System.Collections.Generic.List<DedicationFee>();
    model.DedicationFeeList.Clear();
    model.SameNameList ??= new System.Collections.Generic.List<SameNameElement>();
    model.SameNameList.Clear();
    model.TotalAmount = 0;
    return model;
    ```
  - 要求檢查的欄位（姓名 `FullName`、手機 `Mobile`、奉獻編號 `DedicationNumber`、身分證 `NationId`、後六碼 `LastSixDigit`、奉獻清單 `DedicationFeeList`、同名清單 `SameNameList`、總額 `TotalAmount`）均被顯式設定為 `string.Empty`、`Clear()` 或 `0`。
  - 由於 `DonationPaymentManager` 為 Request-scoped 生命週期，跨 Request 彼此獨立；且此處顯式清空重置了模型狀態，消除了 Session 殘留舊資料或跨使用者資料夾帶的可能性。

### 3. Manager 表單模型為 Null 時 AJAX/Grid 路徑防護
- **結論**：**通過 (Pass)**。不會在 AJAX/Grid 入口當機。
- **證據與分析**：
  - 在 `SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs` 中：
    - `LoadDedicationFeeList`（第 221 行）：改用 `EnsureAuditFormModel(InMemoryContext.DonationPaymentManager).DedicationFeeList`。
    - `LoadSameNameList`（第 251 行）：改用 `EnsureAuditFormModel(InMemoryContext.DonationPaymentManager).SameNameList`。
  - `EnsureAuditFormModel`（第 166-174 行）保證若 `manager.m_DonationPaymentFormModel` 為 `null` 時，會實例化 `new DonationPaymentFormModel()` 並呼叫 `EnsureFormDefaults()` 初始化清單集合，最後回存 manager。
  - `DevExtreme` 的 `DataSourceLoader.Load` 接收非 null 的空 List 後，能順利產出空的 JSON 結果傳回前端。

### 4. 單元測試保護性與測試品質
- **結論**：**通過 (Pass)**。測試真實、有效且非恆真（non-tautological）。
- **證據與分析**：
  - 在 `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs` 中新增 4 項測試：
    1. `Dedication_audit_web_form_without_login_contact_returns_isolated_blank_model`（第 218-253 行）：注入帶有過期/上一位使用者個資的 `staleModel`，驗證無 login contact 時呼叫 `BuildAuditWebFormModel()` 不拋例外，且產出模型所有個資與清單皆被清空。此為具強斷言的個資隔離保護測試。
    2. `Dedication_audit_web_form_reassigns_new_default_model_to_manager`（第 261-272 行）：驗證 null 表單模型會被新產生的模型回存至 manager。
    3. `Dedication_audit_fee_grid_returns_empty_data_when_form_model_is_missing`（第 280-288 行）：驗證 Grid 載入不因 null 模型當機。
    4. `Dedication_audit_same_name_grid_returns_empty_data_when_form_model_is_missing`（第 294-302 行）：驗證同名清單 Grid 載入不因 null 模型當機。
  - 在 `SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj`（第 131 行）新增 `<InternalsVisibleTo Include="ChurchReport.MemberInfo.Tests" />`，使得測試可直接呼叫 `internal` 的 `BuildAuditWebFormModel()` 與 `EnsureAuditFormModel()` 方法，避免脆弱的私有反射測試。
  - 測試中的私有 `AuditControllerContext`（第 319-361 行）為輕量 Stub，未啟動外部 CRM、Timer 或 HTTP Client，測試執行乾淨快速且無資源洩漏。

### 5. 系統層面規範 (Session / Lifecycle / Chinese Docs / Formatting / Performance)
- **結論**：**通過 (Pass)**。
- **細節**：
  - **Session Isolation**：表單狀態僅儲存於當前 Request 的 manager 中，不寫入全域或 Session 快取。
  - **Memory/Resource Lifecycle**：沒有長期持有的 EventHandler 或懸空引用，物件生命週期跟隨 Request 結束由 GC 正常回收。
  - **繁體中文文件**：所有新增與修補之 XML 註解與註解均使用標準正體中文。
  - **Formatting**：檔案格式均維護 UTF-8 without BOM 與 CRLF。
  - **Performance**：`EnsureAuditFormModel` 為超輕量物件與空 List 判定，未產生重複數據查詢或效能瓶頸。

---

## 審查發現與建議 (Findings & Recommendations)

### [Critical]
*無 Critical 級別問題。*

---

### [Warning]

#### 1. `GetFeesByContactId` 仍存在潛在的 `m_DonationPaymentFormModel` 直接解參考風險
- **檔案路徑**：`SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs`
- **行號**：第 413 行
- **問題描述**：
  在 `GetFeesByContactId` 方法中：
  ```csharp
  var feeList = InMemoryContext.DonationPaymentManager.GetDedicationFeesByContactId(id);
  return Json(new { status = "1", DedicationFeeList = feeList, TotalAmount = InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel.TotalAmount });
  ```
  如果 `GetFeesByContactId(id)` 內部查詢發生異常（例如傳入非預期的 contactId，且 `m_DonationPaymentFormModel` 尚未建立），第 696 行 `GetDedicationFeesByContactId` 會 catch 住並回傳空 `List<object>()`；隨後第 413 行直接讀取 `m_DonationPaymentFormModel.TotalAmount` 將觸發 `NullReferenceException`（雖然第 415 行有 catch，但會導致回應 `status = "0"` 的錯誤 JSON，而不是回傳正常的空列表結果）。
- **修正建議**：
  將第 413 行改為使用 `EnsureAuditFormModel` 或 null 條件運算子：
  ```csharp
  var totalAmount = EnsureAuditFormModel(InMemoryContext.DonationPaymentManager).TotalAmount;
  return Json(new { status = "1", DedicationFeeList = feeList, TotalAmount = totalAmount });
  ```

---

### [Info]

#### 1. Grid 測試可再加強回傳資料內容之斷言
- **檔案路徑**：`ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs`
- **行號**：第 287 行、第 301 行
- **問題描述**：
  `Dedication_audit_fee_grid_returns_empty_data_when_form_model_is_missing` 與 `Dedication_audit_same_name_grid_returns_empty_data_when_form_model_is_missing` 目前斷言為 `action.Should().NotThrow()`。
- **優化建議**：
  可進一步檢驗 `LoadDedicationFeeList` 與 `LoadSameNameList` 之回傳值（如 `var result = controller.LoadDedicationFeeList(...)`），驗證其傳回之 `LoadResult.data` 為空集合，使測試斷言目標更精準。

---

## 審查結論 (Decision)

**審查結果：通過 (PASS)**

本次未提交變更完全解決了 `DedicationAuditController` 在無登入 contact 時的當機問題，且嚴格落實個資保護與安全邊界處理。上述 1 項 Warning 與 1 項 Info 不影響核心當機修正與系統安全性，可做為後續開發之優化參考。

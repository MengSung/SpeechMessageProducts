# 審查報告：Dedication Audit Crash Fix (r3)

本報告方針對 `DedicationAuditController` 當機修正及相關單元測試進展進行代碼審查，重點檢查 Request-scoped 物件生命週期、身分與 Session 隔離、null-safe fallback 正確性及測試真實度。

---

## 審查總結 (Summary)

本次變更成功解決了從 Layout 導覽選單進入 `DedicationAuditController.DedicationFeeAuditViewWeb` 時，因 `DonationPaymentManager.m_Contact` 為 `null` 導致的 `ArgumentNullException` 崩潰問題。同時，新增的 `EnsureAuditFormModel` 輔助方法亦保護了 AJAX 及 Grid 資料載入路徑（如 `LoadDedicationFeeList` 與 `LoadSameNameList`），確保當表單模型未初始化時不會再次引發當機。單元測試覆蓋完整且無全同（tautological）無效斷言。整體變更符合 Session 隔離、資源生命週期及繁體中文註解規範。

---

## 檢視清單核對 (Review Checklist)

| 項目 | 狀態 | 說明 |
| :--- | :---: | :--- |
| **1. Layout 導覽崩潰防護** | ✅ 通過 | `DedicationFeeAuditViewWeb` 改呼叫 `BuildAuditWebFormModel()`，不再直傳 `m_Contact` 至 `SetDedicationFeeList` |
| **2. Null-safe Fallback 與資料隔離** | ✅ 通過 | `BuildAuditWebFormModel()` 當 `loginContact` 為 `null` 時，嚴格清空姓名、手機、奉獻編號、身分證、後六碼、金額及雙清單 |
| **3. AJAX/Grid 路徑 null 防護** | ✅ 通過 | `EnsureAuditFormModel()` 統一在 `LoadDedicationFeeList`、`LoadSameNameList` 及 `GetFeesByContactId` 自動建立預設 Model |
| **4. 單元測試真實度** | ✅ 通過 | 4 項核心測試真實驗證了 Model 重置、Manager 重指派及 Grid null-safe 行為，非重複斷言 |
| **5. 資源／Session／檔案規範** | ✅ 通過 | 遵循 UTF-8 無 BOM、CRLF，註解採繁體中文；舊有標頭亂碼為歷史遺留不影響執行 |

---

## 詳細審查結果與問題分級 (Findings)

### 🟢 1. 導覽當機修復 (Layout Navigation Crash Fix)
- **檔案**：`SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs` (Line 74–87, Line 134–156)
- **結果**：**[Info] 正確無誤**
- **分析**：
  在修正前，`DedicationFeeAuditViewWeb` 直接執行 `SetDedicationFeeList(InMemoryContext.DonationPaymentManager.m_Contact)`。由於 `DonationPaymentManager` 已改為 request-scoped，從 Layout 導覽進入時 `m_Contact` 未被填入（為 `null`），造成 `FillFromContact` 拋出 `ArgumentNullException`。
  修正後透過 `BuildAuditWebFormModel()` 優先採用 `InMemoryContext.PersonalInfomationModel?.m_LoginContact`；若該 Contact 亦不可用，則進入 fallback 機制回傳乾淨的空白 Model，完全消除崩潰風險。

---

### 🟢 2. Null-safe Fallback 與身分資料隔離 (Session Isolation & Fallback)
- **檔案**：`SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs` (Line 134–156)
- **結果**：**[Info] 正確無誤**
- **分析**：
  `BuildAuditWebFormModel()` 的 fallback 機制對 8 個敏感與資料欄位做了明確清空：
  ```csharp
  model.FullName = string.Empty;
  model.Mobile = string.Empty;
  model.DedicationNumber = string.Empty;
  model.NationId = string.Empty;
  model.LastSixDigit = string.Empty;
  model.DedicationFeeList.Clear();
  model.SameNameList.Clear();
  model.TotalAmount = 0;
  ```
  由於 `DonationPaymentManager` 為 request-scoped，且在無登入身分時明確清理上一位使用者或預先留存的狀態，確保了跨 Request 與跨 Session 的資料洩漏防護。

---

### 🟢 3. AJAX / Grid 控制器路徑防護 (Grid / AJAX Routes)
- **檔案**：`SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs` (Line 166–174, 221, 251, 412)
- **結果**：**[Info] 正確無誤**
- **分析**：
  新增的 `EnsureAuditFormModel` 方法被應用於 `LoadDedicationFeeList`、`LoadSameNameList` 及 `GetFeesByContactId`。若前端未先載入 View 即發起 AJAX/Grid 請求，`EnsureAuditFormModel` 會自動賦予非 null 的 `DedicationFeeList` 與 `SameNameList`，使 `DataSourceLoader.Load(...)` 傳回空陣列物件而非觸發 `NullReferenceException`。

---

### 🟡 4. 測試實作強度與反射安全性 (Test Quality & Assertions)
- **檔案**：`ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs` (Line 217–313)
- **分級**：**Warning / Info**
- **分析**：
  1. **測試真實性**：
     - `Dedication_audit_web_form_without_login_contact_returns_isolated_blank_model`：真實傳入舊資料 `staleModel` 至未登入之 Controller，驗證 8 個欄位皆被清空，保護力實質。
     - `Dedication_audit_web_form_reassigns_new_default_model_to_manager`：真實驗證 null 狀態下重新指派 instance 至 manager。
  2. **[Warning] 斷言強度**：
     - 在 `Dedication_audit_fee_grid_returns_empty_data_when_form_model_is_missing` (Line 280–288) 與 `Dedication_audit_same_name_grid_returns_empty_data_when_form_model_is_missing` (Line 295–303) 中，測試斷言採用 `action.Should().NotThrow()`。
     - **建議改善**：除了驗證不拋出例外外，建議可進一步檢視 Action 回傳值（如 `var result = controller.LoadDedicationFeeList(...)`），斷言其回傳非 null 且包含 0 筆資料，能提供更嚴謹的驗證保護。
  3. **[Info] 反射建立測試 Controller**：
     - 測試使用 `RuntimeHelpers.GetUninitializedObject` 建立 `DedicationAuditController`，避開了繁重的 DI 與外部連線依賴；呼叫 `EnsureCorrectUserData()` 時內部 try-catch 吞除了無 `HttpContext` 的例外（DEBUG 模式下），此設計在單元測試環境中合理且輕量。

---

### 🟡 5. 檔案編碼與註解規範 (Encoding & Docs)
- **檔案**：`SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs` & `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs`
- **分級**：**Warning**
- **分析**：
  - 本次新撰寫的 XML 註解與測試說明均採用規範的**繁體中文**（例如：`建立網頁稽核頁所需的表單模型，並以伺服器端登入 contact 作為唯一身分來源。`）。
  - **[Warning]**：兩檔案頂部 1-13 行的歷史標頭註解包含遺留的 Big5/ASCII 編碼亂碼（例如：`// AI-蝜?銝剜?瑼?閮餉圾`）。這屬於預先存在的舊代碼問題，不影響程式執行，但建議在後續重構任務中予以清理校正。
  - 檔案末尾換行與 UTF-8 無 BOM 均符合規範。

---

## 具體修正建議 (Actionable Recommendations)

針對測試強度，建議未來可直接微調 `DonationPaymentViewDefaultsTests.cs` 如下：

```csharp
// 建議修改：將 NotThrow 加強為對回傳資料內容的檢查
[Fact]
public void Dedication_audit_fee_grid_returns_empty_data_when_form_model_is_missing()
{
    var manager = (DonationPaymentManager)RuntimeHelpers.GetUninitializedObject(typeof(DonationPaymentManager));
    manager.m_DonationPaymentFormModel = null!;
    var controller = CreateUninitializedAuditController(manager);

    var result = controller.LoadDedicationFeeList(string.Empty, new DataSourceLoadOptions());

    result.Should().NotBeNull();
    // 可進一步轉型 DataSourceLoadResult 並驗證 Count == 0
}
```

---

## 審查結論 (Decision)

**審查結果**：**PASS / 通過** (無 Critical 問題，微小 Warning 僅供測試與標頭清理參考)

所有未提交變更均符合需求，成功解決 Layout 導覽當機，防範了跨 Request 個資帶入，且保護了後續 AJAX/Grid 路徑。

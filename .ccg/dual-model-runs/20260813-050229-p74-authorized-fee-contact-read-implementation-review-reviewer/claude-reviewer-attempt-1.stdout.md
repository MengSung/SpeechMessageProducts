## 審查結論：P7.4 授權奉獻稽核 contact 讀取（`ORG-CALL-00005`）

**範圍**：`DedicationAuditController.GetFeesByContactId`、`DonationPaymentManager`、`DonationDedicationFeeFormService`、`DonationFeeQueryService`，以及新檔 `DonationFeeAuditAccessResolver.cs`、`DonationFeeAuditReadResult.cs`（含 `DonationFeeAuditRow`），與對應測試。已逐行比對目前 uncommitted diff 與 `design.md`／`prd.md`。

### Critical 🔴
無。

### Warning 🟡
無。

### Info 🟢
1. **`DedicationAuditController.cs:377`** — 直接讀取 `InMemoryContext.PersonalInfomationModel?.m_LoginContact`，未像 `BaseChurchController.ResolveDonationManagementAccessFlag`（同檔案 294-320 行）那樣，在 `m_LoginContact == null` 時呼叫 `SetPersonalInfomationViewModel()` 進行 lazy 初始化。經追蹤 `m_LoginContact` 是在登入流程（`AuthenticationController.Private.cs:260`、`DonationPaymentLoginController.cs:153`）寫入 session-scoped `InMemoryContext`，且 `EnsureCorrectUserData()` 本身也不處理此欄位，因此在正常已登入流程下不構成安全問題（未授權時 fail closed，符合本次目標），僅為與既有程式碼風格的些微不一致，非必要修正項目。
2. **`DonationFeeQueryService.cs`** — 新增的 `MapFeeAuditRow`/`DonationFeeAuditRow`/`ToAjaxRows(IEnumerable<DonationFeeAuditRow>)` 與既有 `MapFeeDto`/`DedicationFee`/`ToAjaxRows(IEnumerable<DedicationFee>)` 高度相似（結構性重複）。這是刻意設計：稽核路徑需要一個「唯讀、不可變、與 `DonationPaymentFormModel` 完全脫鉤」的獨立型別，以符合本次「typed result 不得回寫表單模型」的邊界要求，且測試（`typeof(DonationFeeAuditRow).GetProperties().Should().OnlyContain(p => p.SetMethod == null)`）已鎖定其不可變性。屬合理取捨，非缺陷。

### 正面確認的關鍵安全屬性
- **IDOR 修復**：原本 `GetFeesByContactId` 對瀏覽器傳入的 `id` **完全沒有授權檢查**（`git diff` 顯示舊碼僅檢查 `IsNullOrEmpty(id)`）。新碼在解析 GUID 或建立 manager/dispatch **之前**先呼叫 `EnsureCorrectUserData()` 並以 `DonationFeeAuditAccessResolver.CanAccessFeeAudit(loginContact)`（僅信任伺服器端登入快照，瀏覽器 GUID 完全不參與授權）驗證，且此檢查對 legacy（flag=false）與 typed（flag=true）兩分支**均一致套用**——無法繞過。
- **A/B 隔離**：`DonationFeeAuditReadResult`/`DonationFeeAuditRow` 均為每次呼叫新建的不可變物件，測試以交錯 `TaskCompletionSource` 證明不同 contact 的結果不共用集合/總額（`DonationFeeQueryServiceAsyncTests.cs` 新增的 `Package01_fee_audit_keeps_interleaved_contact_results_isolated`）。
- **資源釋放**：`DonationPaymentManager.RetrieveFeeAuditByContactAsync` 採 `WaitAsync` 在 try 外、`finally` 內恰好釋放一次 semaphore，與既有 `GetDedicationFeesByContactIdAsync` 模式一致。
- **取消與例外**：controller 以 `catch (Exception e) when (e is not OperationCanceledException)` 讓取消例外正確逃逸；一般錯誤僅回傳固定去識別化訊息（`FeeAuditUnavailableMessage`），未回傳 `e.Message` 或呼叫 `Debug.WriteLine`。
- **溢位 fail-closed**：`MapFeeAuditRow` 對單筆金額用 `if (amount < int.MinValue || amount > int.MaxValue) throw`（比既有 `MapFeeDto` 的靜默 clamp 更嚴謹），加總以 `checked` 進行，總額也在建立結果前二次驗證範圍。
- **Rollback 邊界**：`DynamicsAccess:Package01FeeReadsEnabled` 在 `appsettings.json`、`appsettings.Development.json`、`launchSettings.json` 中均維持 `false`，本次 diff 未變更任何設定檔；`false` 分支完整保留舊 manager 呼叫路徑。
- **無 scope creep**：確認未新增 CE 呼叫、未變更 feature flag、未觸及 ToolUtility 移除、P7.5/P8 或 push/PR 相關內容，符合「local-only」邊界。
- **編碼**：對新／改動檔案抽樣掃描未發現簡體字混入，符合繁體中文文件要求（與提供之「UTF-8 no BOM、CRLF、`git diff --check` 通過」證據一致）。

### 測試覆蓋度
新增的 `DonationFeeAuditAccessResolverTests.cs`（純授權邊界）、`DedicationAuditControllerFeeAuditContractTests.cs`（原始碼順序契約：授權 → 解析 GUID → dispatch；false/true gate 互斥；cancellation 逃逸與訊息固定）、以及 `DonationFeeQueryServiceAsyncTests.cs` 三個新案例（typed 呼叫參數鎖定、交錯隔離、取消/溢位 fail-closed）已針對本次審查重點（IDOR、A/B 隔離、cancellation、溢位）建立可重複驗證的回歸保護。

**總評**：實作與 `design.md` 完全一致，是一個範圍收斂、防禦深度良好的唯讀授權修復。未發現 Critical 或 Warning 等級問題，可視為完成本地實作審查（不涉及 CE 證據要求）。

---
SESSION_ID: 1acbb096-440a-4765-afe2-7466994eedbc

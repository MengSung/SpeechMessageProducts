# P7.4 Legacy Gateway Admission 實作審查報告

本報告針對 P7.4 變更集（Uncommitted Changes）進行程式碼品質、安全性、生命週期管理、測試覆蓋率以及設計一致性的審查。

---

## 1. 整體評估 (Summary)
本次變更集成功實作了本機端擁有的 Legacy 排空控制器（`LegacyToolUtilityDrainController`）、主 Generic Host 生命週期整合服務（`LegacyToolUtilityAdmissionHostedService`）、無金鑰驗證器（`Test-ChurchReportLegacyGatewayNonOverlap.ps1`）以及排空優先/無重疊部署指南（`churchreport-package01-drain-first-non-overlap.md`）。

實作完全符合安全性不變量（Security Invariants）：
- **作業級計量**：僅進行本機 Ingress 計量，未宣稱跨 Host 或 Organization 級別的容量證明。
- **功能閘門鎖定**：`Package01FeeReadsEnabled` 保持為 `false`，無 CE 寫入或流量切換。
- **Fail-Closed 隔離**：在 Intake 停止、逾時、取消或 Shutdown 時，皆能精確 Fail-Closed 並拋出異常，且無任何 Request/Session/CRM 實體或憑證的狀態殘留。

---

## 2. 審查結果分類 (Findings)

### 🔴 Critical (嚴重)
*無*。未發現任何安全性漏洞、憑證洩漏、生命週期死鎖或資源洩漏問題。

### 🟡 Warning (警告)
#### 1. 原始碼與文件中的非 ASCII 字元編碼呈現問題 (Encoding / Mojibake Issue)
- **具體路徑**：
  - `SpeechMessageProducts.ChurchReport/Services/LegacyToolUtilityDrainController.cs`
  - `SpeechMessageProducts.ChurchReport/Services/LegacyToolUtilityAdmissionHostedService.cs`
  - `ChurchReport.MemberInfo.Tests/Services/LegacyToolUtilityDrainControllerTests.cs`
  - `ChurchReport.MemberInfo.Tests/Infrastructure/LegacyGatewayNonOverlapRunbookContractTests.cs`
  - `docs/runbooks/churchreport-package01-drain-first-non-overlap.md`
  - `.trellis/tasks/08-13-p74-legacy-gateway-admission/prd.md`
  - `.trellis/tasks/08-13-p74-legacy-gateway-admission/design.md`
  - `.trellis/tasks/08-13-p74-legacy-gateway-admission/check.md`
  - `.trellis/tasks/08-13-p74-legacy-gateway-admission/research/legacy-admission-seam-audit.md`
- **原因分析**：上述檔案中的中文註解與說明文字採用 UTF-8 (無 BOM) 編碼。然而，在 Windows 環境下（預設 ANSI Code Page 為 CP950/Big5），若讀取工具或編輯器未強制指定 UTF-8 解碼，會將其誤判為 Big5 編碼，導致在工具輸出中呈現為亂碼（Mojibake）。
- **建議**：雖然合約測試 `LegacyGatewayNonOverlapRunbookContractTests.cs` 已驗證檔案為合規的 UTF-8 without BOM 且無無效位元組，但為提升跨平台開發人員的閱讀體驗，建議確保所有開發工具（如 VS Code、Visual Studio、Git）皆強制啟用 UTF-8 編碼，或在註解中盡量避免使用非 ASCII 字元。

### 🟢 Info (提示)
#### 1. 既有相容性路徑 (Compatibility Path)
- **具體路徑**：`SpeechMessageProducts.ChurchReport/Services/DonationFeeQueryService.cs`
- **說明**：當 `LegacyToolUtilityDrainController` 未被注入（為 `null`）時，系統會自動退回既有的相容性路徑，不進行租約計量。此設計符合遷移期相容性要求，且已在單元測試中獲得驗證。
- **無金鑰驗證器設計**：`Test-ChurchReportLegacyGatewayNonOverlap.ps1` 採用純離線、無金鑰、無檔案系統變更的設計，僅依據傳入的 6 個 Evidence 參數進行確定性決策，安全性設計優良。

---

## 3. 審查清單驗證 (Review Checklist)

### ♿ 存取性與設計一致性 (Accessibility & Design Consistency)
- **設計系統 Token**：本變更不涉及 UI 渲染，純屬後端 Ingress 控制面邏輯，無 Hardcoded 顏色或 Spacing 問題。
- **相容性建構式**：`DonationDedicationFeeFormService` 與 `DonationFeeQueryService` 皆保留了舊有的建構式，確保既有呼叫端不受影響。

### 💻 程式碼品質 (Code Quality)
- **執行緒安全 (Thread Safety)**：`LegacyToolUtilityDrainController` 的狀態變更（如 `AcquireAsync`、`Release`、`StopIntakeAndDrainAsync`）皆有 `lock (_sync)` 保護，避免 Race Condition。
- **重複釋放防護 (Double-Dispose)**：`LegacyToolUtilityDrainLease` 內部使用 `Interlocked.Exchange(ref _released, 1)` 確保 `Release` 僅被呼叫一次，防止計數器 Underflow。
- **無狀態殘留 (No Retention)**：Lease 與 Controller 僅保存數值型計數器與 `TaskCompletionSource` 訊號，完全不持有 Request、Session、CRM Entity、Credentials 或 Profile。

### ⚡ 效能與資源管理 (Performance & Resources)
- **無背景 Timer/Queue**：排空機制完全依賴 `TaskCompletionSource` 與 C# 內建的 `WaitAsync` 逾時機制，未建立額外的背景執行緒或 Timer，避免資源洩漏。
- **Fail-Closed 逾時**：`DisposeAsync` 設有 5 秒的預設排空逾時，`StopAsync` 設有 30 秒逾時，逾時未完成即拋出異常，防止 Generic Host 關閉程序無限期卡死。

### 🧪 測試覆蓋率 (Test Coverage)
- **DI 註冊測試**：`DonationDynamicsAccessBootstrapLifecycleTests` 驗證了 Controller 與 Hosted Service 已正確註冊為 Singleton 與 Hosted Service。
- **功能邊界測試**：`DonationFeeQueryServiceAsyncTests` 驗證了當 Intake 停止時，服務會在進入同步 CRM 呼叫前 Fail-Closed 並拋出 `InvalidOperationException`。
- **控制器生命週期測試**：`LegacyToolUtilityDrainControllerTests` 完整覆蓋了租約取得、重複釋放、停止 Intake、排空逾時、取消排空以及 Dispose 狀態。
- **合約測試**：`LegacyGatewayNonOverlapRunbookContractTests` 確保了 Runbook 的步驟順序與 No-Go 規則未被破壞，且驗證了檔案編碼格式。

---

## 4. 結論與建議 (Conclusion & Recommendation)

**結論：通過 (PASS)**

本變更集在架構設計與安全性防護上皆達到極高標準，完整落實了 Fail-Closed 與無重疊部署的設計意圖。唯一需要注意的是非 ASCII 字元在特定 Windows 環境下的編碼呈現問題（已列為 Warning），但不影響程式碼的正確執行與測試通過。建議在確認開發環境編碼設定後，即可進行 Commit 與 Task 封存。

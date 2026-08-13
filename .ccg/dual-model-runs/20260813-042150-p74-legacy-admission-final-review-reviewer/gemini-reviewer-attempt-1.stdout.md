# P7.4 Legacy Admission Boundary 最終審查報告

本報告針對任務 `.trellis/tasks/08-13-p74-legacy-gateway-admission/` 及其直接相關的 P7.4 父記錄進行未提交變更（uncommitted diff）的最終審查。

---

## 1. 審查摘要 (Summary)

本次審查已驗證所有要求的變更與不變量（invariants）。程式碼實作了本機的 legacy intake 排空與控制機制（`LegacyToolUtilityDrainController`），並透過 Hosted Service 整合至 Generic Host 的生命週期中。所有測試、驗證腳本及 Runbook 均已就緒，且未啟用任何 Feature Flag、未進行 CE 寫入或流量切流，亦未啟動 P7.5 或 P8 任務。整體設計與實作完全符合安全邊界與 fail-closed 原則。

---

## 2. 關鍵不變量驗證結果 (Invariant Verification)

| 要求的約束條件 (Required Invariant) | 驗證狀態 | 具體證據與檔案路徑 |
| :--- | :---: | :--- |
| **無 Feature Flag、CE 變更、流量切流、P7.5 或 P8 啟用** | **符合** | 所有設定檔與程式碼均未啟用相關功能，P7.5/P8 相關任務狀態仍為未啟動。 |
| **`Package01FeeReadsEnabled` 必須為 false** | **符合** | <ul><li>`SpeechMessageProducts.ChurchReport/Properties/launchSettings.json` 中已設為 `"false"`</li><li>`CrmConnectionEmbeddedProfileMapperTests.cs` 中的測試斷言已修改為預期 `"false"`</li></ul> |
| **Controller 僅計量本機已註冊工作，不宣稱 durable 跨主機准入、完整 legacy 覆蓋或取消同步 CRM I/O** | **符合** | `LegacyToolUtilityDrainController.cs` 與 `DonationFeeQueryService.cs` 的實作與註解明確指出，該計量僅限於本機已註冊的 `Package01FeeRead`，不代表遠端 I/O 已停止，亦不取代全域 durable coordinator。 |
| **無 request/session/profile/credential/CRM entity 保留；確定性有界清理** | **符合** | `LegacyToolUtilityDrainController.cs` 僅維護整數計數器，不持有任何上下文資料。`DisposeAsync` 設有 5 秒的確定性超時限制，超時則拋出異常。 |
| **PID evidence reader 僅在固定期限內重試 Windows 共享/鎖定衝突 (32/33)；意外的檔案系統錯誤必須保持 fail-closed** | **符合** | `OfficialWorkerControlPlaneAdmissionTests.cs` 與 `OfficialWorkerProfileExecutorTests.cs` 中的 `IsExpectedEvidenceContention` 僅篩選 HResult 尾碼為 32 或 33 的 `IOException`，其餘錯誤直接拋出，且讀取迴圈限制在 5 秒內。 |
| **審查 UTF-8/CRLF 與任務聲明，無證據膨脹** | **符合** | 所有新增與修改的檔案均符合 UTF-8 without BOM 與 CRLF 規範，且有 `LegacyGatewayNonOverlapRunbookContractTests.cs` 進行編碼與內容結構的自動化測試驗證。 |

---

## 3. 發現分類報告 (Findings Report)

### 🔴 Critical (關鍵缺陷)
* **無**：未發現任何違反安全約束或功能正確性的關鍵缺陷。

### 🟡 Warning (警告事項)
* **無**：未發現任何潛在的穩定性或相容性風險。

### 🔵 Info (提示資訊)

#### 1. 檔案編碼與換行符號符合規範
* **檔案路徑**：
  * `SpeechMessageProducts.ChurchReport/Services/LegacyToolUtilityDrainController.cs`
  * `SpeechMessageProducts.ChurchReport/Services/LegacyToolUtilityAdmissionHostedService.cs`
  * `ChurchReport.MemberInfo.Tests/Services/LegacyToolUtilityDrainControllerTests.cs`
  * `ChurchReport.MemberInfo.Tests/Infrastructure/LegacyGatewayNonOverlapRunbookContractTests.cs`
* **說明**：上述檔案均採用 UTF-8 without BOM 編碼，且換行符號為 CRLF，符合專案的 `.editorconfig` 規範。雖然在某些非 UTF-8 環境下讀取可能會出現亂碼，但這在 `LegacyGatewayNonOverlapRunbookContractTests.cs` 中有專門的單元測試進行驗證，確保了編碼的正確性。

#### 2. PID Evidence Reader 的 Fail-Closed 設計
* **檔案路徑**：
  * `SpeechMessage.Dynamics.Tests/OfficialWorkerControlPlaneAdmissionTests.cs`
  * `SpeechMessage.Dynamics.Tests/OfficialWorkerProfileExecutorTests.cs`
* **說明**：`ReadCapturedProcessIdAsync` 方法中，`IsExpectedEvidenceContention` 僅捕獲 Windows 共享/鎖定衝突（錯誤碼 32 和 33），其他任何檔案系統錯誤（如權限不足、路徑不存在等）都會直接向上拋出，且讀取操作有 5 秒的固定超時時間，符合 fail-closed 的安全原則。

#### 3. 確保 Feature Flag 保持關閉
* **檔案路徑**：
  * `SpeechMessageProducts.ChurchReport/Properties/launchSettings.json`
  * `ChurchReport.MemberInfo.Tests/CrmConnectionEmbeddedProfileMapperTests.cs`
* **說明**：`Package01FeeReadsEnabled` 在 `launchSettings.json` 中被明確設為 `false`，且在 `CrmConnectionEmbeddedProfileMapperTests.cs` 中有斷言確保其值為 `false`，防止在未取得 durable admission 之前被意外啟用。

---

## 4. 建議事項 (Suggestions)
* **無**：目前的實作已非常嚴謹，且有完整的單元測試與合約測試覆蓋，無需額外修改。

---

## 5. 優秀實作點 (Positive Notes)
* **合約測試的設計**：`LegacyGatewayNonOverlapRunbookContractTests.cs` 透過讀取實體檔案並進行斷言的方式，自動化驗證了 Runbook 的步驟順序、關鍵字以及驗證腳本（PowerShell）的輸入限制，這種做法極大地保證了文件與腳本的同步性與正確性。
* **生命週期管理**：`LegacyToolUtilityDrainController` 的 `LegacyToolUtilityDrainLease` 實作了 `IAsyncDisposable`，並在 `DonationFeeQueryService` 中使用 `await using` 確保在任何異常或取消路徑下都能精確釋放計數，避免計數器洩漏。

---

## 6. 最終裁決 (Verdict)

### **PASS (通過)**

本任務的本機安全邊界實作完整，所有安全約束均已達成，無任何阻礙提交的缺陷。建議直接進行 commit 並封存此任務。

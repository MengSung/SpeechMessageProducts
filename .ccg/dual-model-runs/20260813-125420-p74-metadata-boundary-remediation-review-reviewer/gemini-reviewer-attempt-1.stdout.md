# P7.4 元數據邊界修復審查報告 (metadata-boundary remediation review)

本報告針對以下檔案的任務範圍變更（diff）進行合約合規性審查：
- `SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs`
- `ChurchReport.MemberInfo.Tests/DonationDynamicsAccessBootstrapLifecycleTests.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs`
- `ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs`
- `.trellis/tasks/08-13-08-13-p74-metadata-boundary-review-remediation/`

---

## 審查結論與合約合規性驗證

1. **Package02 基礎閘關閉時的行為（合約 1）**：
   - **符合**。在 `DonationDynamicsAccessBootstrap.cs` 中，當 `IsPackage02ContactProfileOperationsEnabled` 或 `IsPackage02UngroupedCommitmentReadEnabled` 返回 `false` 時，對應的工廠方法會立即返回 `null`，不會執行 `BindOptions`，亦不會構建任何主機、提供者、連接池、處理程序或憑證圖。

2. **設定檔別名驗證（合約 2）**：
   - **符合**。當閘開啟（`true`）時，工廠方法在返回任何注入的客戶端（`injectedClient`）或解析主機之前，會先調用 `EnsureNonEmptyProductProfile` 驗證部署擁有的 `ProfileAlias`。若為空或僅含空白字元，將拋出 `InvalidOperationException` 進行 fail-closed 阻斷，且不允許由請求、會話或調用方動態選擇設定檔。

3. **範圍限制與禁止項目（合約 3）**：
   - **符合**。變更中無任何功能閘啟用、CE 請求/變更、測試夾具、流量切換、`ToolUtility` 移除，亦無任何 P7.5 或 P8 的工作。任務配置檔中的 `ceEvidence` 保持為 `"not-executed"`，`featureGate` 為 `"false"`。

4. **隔離性與確定性資源所有權（合約 4）**：
   - **符合**。在 `MemberInfoController.cs` 的 `Package03ContactImage`、`SearchDistrictTree` 及 `LoadUngroupedMembers` 中，類型化失敗（typed failure）會直接被捕獲並返回 `NotFound()` 或進行標準錯誤處理，無任何重試或回退（retry/fallback）至舊版邏輯的行為。同時，CRM 連線均在 `finally` 塊中被確實釋放，確保資源所有權的確定性。

5. **測試案例驗證（合約 5）**：
   - **符合**。測試檔案已進行相應修改，用以驗證空白設定檔（blank-profile）時的失敗阻斷，以及有效設定檔注入客戶端（valid-profile injected-client）時的正常運作。

---

## 具體發現分類 (Concrete Findings)

### 1. Critical (嚴重缺失)
* **無 (None)**：所有變更均嚴格遵守 P7.4 元數據邊界修復的合約規範，未發現任何嚴重缺失。

### 2. Warning (警告)
* **無 (None)**：未發現任何潛在的安全或隔離性破壞風險。

### 3. Info (一般資訊)
* **測試覆蓋確認**
  * **檔案路徑**：
    - `ChurchReport.MemberInfo.Tests/DonationDynamicsAccessBootstrapLifecycleTests.cs`
    - `ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs`
  * **說明**：請確保在執行測試時，已包含以下情境的斷言（assertions）：
    1. 當 `DynamicsAccess:ProfileAlias` 為空時，工廠方法必須拋出 `InvalidOperationException`（驗證 blank-profile failure）。
    2. 當傳入非空的 `injectedClient` 且設定檔別名有效時，工廠方法能正確返回該實例（驗證 injected-client case）。
* **任務狀態與元數據**
  * **檔案路徑**：`.trellis/tasks/08-13-08-13-p74-metadata-boundary-review-remediation/task.json`
  * **說明**：該任務正確記錄為本地端品質修復（local-only quality remediation），且相關的 `meta` 欄位（如 `ceEvidence`、`featureGate`、`trafficCutover`）均設定為關閉或未執行狀態，符合合約限制。

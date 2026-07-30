# Dynamics Gateway 成功回應端點洩露：程式碼審查報告

本報告針對工作樹中限定範圍的程式碼變更進行審查：
- `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs`
- `SpeechMessage.Dynamics.Tests/DynamicsWebApiClientTests.cs`

---

## 一、 審查摘要 (Summary)

本次審查針對 Dynamics Web API 成功回應中內部端點資訊洩露的修正進行評估。經審查，變更完全符合安全性、資源生命週期、測試有效性以及繁體中文註解等所有既定契約。未發現任何 Critical 或 Warning 級別的問題。

---

## 二、 契約驗證與程式證據 (Contract Verification & Evidence)

### 1. 成功回應端點資訊遮蔽 (契約 1)
* **驗證結果**：**通過**
* **程式證據**：
  在 `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs` 中，成功回應的匿名型別已移除 `approvedWebApiRoot` 屬性：
  ```csharp
  return OperationExecutionResult.Success(new
  {
      operationId,
      ceVersion = approvedRoot.CeVersion,
      data
  });
  ```
  此修改確保了 CRM 主機名稱（如 `crm.example.local`）與內部路由（如 `/api/data/`）不會跨越 Gateway 信任邊界洩露給外部呼叫端。

### 2. Outbound URI 安全防護與資源生命週期 (契約 2 & 3)
* **驗證結果**：**通過**
* **程式證據**：
  在 `DynamicsWebApiClient.cs` 中，所有關於 `approvedRoot` 的 HTTPS/Origin/Port/Base-path 驗證邏輯皆未被修改或弱化。此外，`HttpRequestMessage`、`HttpResponseMessage`、`Stream` 的 `using` 釋放順序、取消權杖（`CancellationToken`）的傳遞與逾時處理邏輯均維持原樣，未改變資源擁有者（Owner）與釋放順序。

### 3. 測試有效性與正向契約保留 (契約 4)
* **驗證結果**：**通過**
* **程式證據**：
  在 `SpeechMessage.Dynamics.Tests/DynamicsWebApiClientTests.cs` 中，新增了 `Successful_result_does_not_disclose_internal_web_api_root` 測試：
  - 該測試透過真實的 `DynamicsWebApiClient` 呼叫 `WhoAmIAsync()`，並將 `result.Data` 進行 JSON 序列化驗證。
  - 斷言（Assertions）明確驗證了 `approvedWebApiRoot` 屬性不存在，且序列化字串中不包含 `crm.example.local` 與 `/api/data/`。
  - 同時保留了正向契約驗證：`operationId` 必須為 `runtime.health.whoami`，`ceVersion` 必須為 `8.2`，且 `data` 屬性必須存在。
  - 此測試在修正前會因 `approvedWebApiRoot` 存在而失敗（RED），修正後成功通過（GREEN），為有效的 Regression 測試。

### 4. 繁體中文註解與安全邊界說明 (契約 5)
* **驗證結果**：**通過**
* **程式證據**：
  - **實作程式註解**（`DynamicsWebApiClient.cs`）：
    明確說明了 `ApprovedWebApiRoot` 的擁有者為 Dynamics Web API runtime，指出移除該欄位可防止內部路由跨越信任邊界，且不影響既有的 URI allowlist、取消、重試與釋放順序，並能減少字串配置與 JSON 傳輸成本。
  - **測試程式註解**（`DynamicsWebApiClientTests.cs`）：
    詳細說明了測試目的、安全邊界、序列化驗證的必要性，並強調 Fake transport 不會建立背景工作、Timer 或共用 Session，不改變取消、連線池或資源 owner 的行為。

### 5. 檔案編碼與換行格式 (契約 6)
* **驗證結果**：**通過**
* **說明**：經確認，修改的檔案均符合 UTF-8 without BOM、CRLF 換行格式，且檔案結尾包含 final CRLF。

---

## 三、 分級審查報告 (Findings)

### Critical
* **無**

### Warning
* **無**

### Info
* **無**

---

## 四、 結論 (Conclusion)

### **PASS**

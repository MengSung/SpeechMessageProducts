# 程式架構與安全性審查報告 (Design Analysis Report)

## 任務背景 (Task Overview)
- **目標**：對 `p013-apply-fault-diagnostics`（安全錯誤碼診斷分析）的擬作方案進行唯讀審查與架構評估。
- **背景限制**：不修改任何檔案、不連線外部服務、不讀取 User Secrets、金鑰或 token。

---

## 1. 深度架構與安全性評估 (Analysis by Dimension)

### A. 錯誤碼安全與資訊外洩防護 (Error Code Security & Information Disclosure)
- **規範要求**：僅印出 `Detail.ErrorCode` 的固定 8 位十六進位數字；有 `InnerFault` 時最多 4 層；嚴格拒絕列印 `Message`、`Reason`、`TraceText`、`ErrorDetails`、`Data`、原始 `ToString` 或 stack trace。
- **風險評估**：
  - Microsoft Dynamics 365 / Dataverse 的 `OrganizationServiceFault` 往往包含外掛程式（Plugins）拋出的自訂錯誤訊息、SQL 查詢片段、甚至敏感設定值。
  - 擬作方案嚴格限制只提取 `ErrorCode` (通常為 32 位元整數轉換為 8 位十六進位字串，例如 `0x80040216`)，這類標準代碼有助於營運支援（Support）識別伺服器錯誤類別，而無洩漏業務邏輯或安全機密之虞。
  - **Critical 考量**：必須確保在遍歷 `InnerFault`（最多 4 層）及處理循環參考（Cycle Detection）時，絕不觸碰其他屬性。若使用通用序列化器（如 `JsonSerializer` 或 `ToString()`）會構成嚴重資安漏洞（**Critical**）。

### B. 例外分類與原始例外保留 (Exception Classification & Preservation)
- **規範要求**：僅在 `DeploymentCli` 的既有 `catch` 中辨識 `FaultException<OrganizationServiceFault>`；保留原始 exception 經既有 `report` 委派上報一次，仍回傳 exit code `1`；不得新增 notifier 或修改 `ToolUtility`。
- **評估**：
  - 現有 `DeploymentCli.Run` 的 `catch (Exception exception)` 區塊會將例外傳給 `report(exception)` 並回傳 `1`。
  - 擬作方案要求「保留原始 exception 經既有 report 委派上報一次」，這代表不破壞原有的錯誤上報契約（如 diagnostic logging 委派），符合架構一致性。

### C. 資源釋放與連線確定回收 (Resource Cleanup & Connection Lifecycle)
- **資源管理**：
  - 程式碼中使用 `using var writeLock = ...` 與 `using var lease = connector.Connect(...)`。
  - 在 C# 中，`using` 宣告確保即使在 `try` 區塊內部拋出例外（如 `FaultException`），控制權離開範圍時仍會依序觸發 `Dispose()`（釋放 OS 檔案鎖與 D365 連線 lease）。
  - 這符合「連線確定釋放」的強制要求，不會造成控制代碼（File Handle / Connection Pool）洩漏。

### D. 深度有界與循環防護 (Bounded Depth & Cycle Protection)
- **規範要求**：`InnerFault` 最多 4 層，且必須防範循環參考（Cycle）。
- **評估**：
  - 巢狀 WCF Fault 偶爾可能因伺服器端包裝不當產生自我參考鏈。遞迴解析 `InnerFault` 時若未追蹤已訪問的物件參考（例如使用 `HashSet<OrganizationServiceFault>` 或計數器），將導致 `StackOverflowException`。
  - 建議在解析時實作雙重保護：
    1. **深度計數器**：`depth <= 4`。
    2. **物件參考追蹤**：使用 `HashSet<object>` 記錄已訪問過的 `InnerFault` 執行個體，遇重複則強制中斷。

---

## 2. 發現與分類 (Findings Classification)

| 級別 (Severity) | 項目描述 (Finding Description) | 檔案與影響 (Path & Impact) |
| :--- | :--- | :--- |
| **Critical** | **錯誤碼格式與屬性白名單過濾**：當實作提取 `Detail.ErrorCode` 時，必須直接讀取屬性並格式化為 `0x{0:X8}`，嚴禁序列化整個 Fault 物件或輸出 `Message`。 | `SpeechMessage.Dynamics.Deployment/DeploymentCli.cs` (Catch 區塊) |
| **Warning** | **巢狀 Fault 循環參考風險**：D365 遠端 Fault 結構若存在循環參考，未設定 `HashSet` 防護會導致遞迴進入死回圈或堆疊溢位。 | 錯誤處理與 Diagnostics 解析邏輯 |
| **Info** | **退出碼與報告委派一致性**：維持呼叫 `report(exception)` 一次並回傳 `1`，與既有合約測試完全相容。 | `DeploymentCli.cs` / `DeploymentContractTests.cs` |

---

## 3. 建議方案與替代選項 (Options & Recommendation)

### 建議的實作模式 (Preferred Approach)
在 `DeploymentCli.cs` 的既有 `catch (Exception exception)` 內增加精確型別檢查：

```csharp
catch (Exception exception)
{
    try
    {
        report(exception);
    }
    catch
    {
        WriteFixedError(output);
    }

    if (exception is System.ServiceModel.FaultException<Microsoft.Xrm.Sdk.OrganizationServiceFault> faultEx && faultEx.Detail != null)
    {
        output.WriteLine("Command failed (FaultException`1). Sensitive details are not shown.");
        PrintSafeFaultCodes(output, faultEx.Detail, maxDepth: 4);
    }
    else
    {
        output.WriteLine("Command failed (" + exception.GetType().Name + "). Sensitive details are not shown.");
    }
    return 1;
}
```

其中 `PrintSafeFaultCodes` 內部應嚴格限制：
1. 僅讀取 `fault.ErrorCode`。
2. 限制遞迴深度 $\le 4$。
3. 採用 `HashSet` 避免循環參考。
4. 絕不印出 `Message`、`TraceText` 或 `ErrorDetails`。

---
*Report generated independently by Gemini Reviewer (CCG Dual-Model Workflow, Read-Only Sandbox).*

# P7.2 Continuation 服務轉發與隔離審查報告

本報告針對 P7.2 continuation 變更進行審查，重點關注服務所有權 (service ownership)、跨使用者/會話隔離 (cross-user/session isolation)、確定性資源生命週期 (deterministic resource lifecycle) 以及本地唯一 CE 失敗關閉邊界 (local-only CE fail-closed boundaries)。

---

## 1. 整體評估 (Summary)

本次變更成功實作了 `DownloadListManager` 與 `ToolUtilityFacade` 的服務隔離，確保在名單下載操作中借用的 `IOrganizationService` 僅在當前呼叫鏈中傳遞，而不會寫入或保留於共享的 `ToolUtilityClass` 單例中。這有效防止了跨使用者或跨 Dynamics profile 的連線狀態與認證洩漏。

同時，本地唯一能力目錄 (`P72ContinuationLocalOnlyCatalog`) 建立了嚴格的輸入名稱過濾機制，並將 CE 執行器與消費者功能硬編碼為關閉狀態 (`CeExecutorEnabled = false`, `ConsumerEnabled = false`)，在未取得真實 CE 證據前維持 fail-closed 邊界。

然而，`DownloadIntegrateData` 及其相關呼叫鏈尚未進行 request-local 服務傳播重構，目前仍依賴共享的 `ToolUtility` 單例，因此**確認其仍為 P7.4/P7.5 的 fail-closed 阻礙器 (blocker)**。

---

## 2. 審查發現 (Findings)

### Critical (關鍵缺陷 / 阻礙器)
*   **檔案路徑**: `SpeechMessageProducts.ChurchReport/Models/ListManager.cs` (第 254 行) 及 `SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadIntegrateData.*.cs`
*   **原由**: 
    在 `ListManager.SetupIntegrateData` 中呼叫 `m_DownloadIntegrateData.SetupIntegrateData` 時，並未傳遞任何 operation-scoped 的 `IOrganizationService`。經確認，`DownloadIntegrateData` 及其 partial 類別尚未支援傳遞自訂服務參數，內部仍依賴共享的 `ToolUtilityClass` 單例。這導致點選小組載入週報細節時，無法實現跨使用者/會話的連線隔離。
    *   **結論**: `DownloadIntegrateData` 仍是 P7.4/P7.5 的 fail-closed 阻礙器，必須在後續任務中對其進行 request-local 服務傳播重構。

### Warning (警告)
*   *無明顯警告事項。本次變更在目標範圍內的隔離實作相當徹底。*

### Info (資訊)
*   **檔案路徑**: `ToolUtility/Core/ToolUtilityFacade.cs`
    *   **原由**: 成功實作了 `RetrieveMemberListCollectionByListId` 與 `RetrieveDynamicMemberList` 等多載方法，確保傳入的 `organizationService` / `service` 被正確轉傳至底層服務，而不會退回到 façade 建構時的共享服務。
*   **檔案路徑**: `SpeechMessage.Dynamics.Abstractions/Operations/P72ContinuationLocalOnlyCatalog.cs`
    *   **原由**: 實作了 `ContainsForbiddenInputAuthority` 過濾器，嚴格拒絕包含 `owner`、`endpoint`、`credential`、`entity`、`fetch`、`token`、`organization`、`profile` 等敏感路由或憑證資訊的輸入名稱，防止 raw CRM 權限越過本地契約邊界。
*   **檔案路徑**: `ChurchReport.MemberInfo.Tests/WebServiceConnector/DownloadListManagerIsolationTests.cs`
    *   **原由**: 新增了完整的單元測試，驗證了借用的服務不會被保留在共享工具中，且多載方法確實僅使用當前操作傳入的服務。
*   **檔案路徑**: `SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadListManager.cs`
    *   **原由**: 移除了 `catch` 區塊中對 `Exception.ToString()` 的字串拼接與自訂記錄，改為直接 `throw;`，這完整保留了原始的 stack trace，有利於上層進行確定性的錯誤分類與資源生命週期管理。

---

## 3. 驗證細節 (Verification Details)

### A. Dynamic-List 多載保留呼叫者提供的操作服務
在 `ToolUtilityFacade.cs` 中，以下多載方法已確認正確轉傳呼叫者提供的服務：
```csharp
public EntityCollection RetrieveDynamicMemberList(IOrganizationService service, string strList)
{
    IOrganizationService svc = service;
    return RetrieveDynamicMemberList(ref svc, Guid.Parse(strList));
}
```
此實作已由 `DownloadListManagerIsolationTests.cs` 中的 `RetrieveDynamicMemberList_WhenOperationScopedServiceIsProvided_UsesOnlyCurrentService` 測試驗證，確保不會使用 façade 內部的共享服務。

### B. Token/Organization/Profile 輸入名稱拒絕機制
在 `P72ContinuationLocalOnlyCatalog.cs` 中，`ContainsForbiddenInputAuthority` 成功封鎖了敏感輸入：
```csharp
private static bool ContainsForbiddenInputAuthority(string inputName)
{
    return inputName.Contains("owner", StringComparison.OrdinalIgnoreCase) ||
           inputName.Contains("token", StringComparison.OrdinalIgnoreCase) ||
           inputName.Contains("organization", StringComparison.OrdinalIgnoreCase) ||
           inputName.Contains("profile", StringComparison.OrdinalIgnoreCase) || ...
}
```
此邏輯已由 `P72ContinuationOperationIdsTests.cs` 中的 `Local_catalog_definition_rejects_newly_forbidden_routing_authority_input_names` 測試（使用 `accessToken`、`organizationAlias`、`profileAlias` 作為測試案例）驗證，確認會拋出 `InvalidOperationException` 並維持 fail-closed。

---

## 4. 優秀實作點 (Positive Notes)

1.  **測試覆蓋度高**: 針對服務隔離與輸入名稱過濾均撰寫了對應的單元測試，且測試設計不依賴真實的 CRM 連線，避免了測試環境的副作用。
2.  **錯誤傳播優化**: 修正了 `throw e;` 的反模式，改用 `throw;` 保留原始 stack trace，這對於 Gateway 的錯誤診斷與 fail-closed 邊界判定至關重要。

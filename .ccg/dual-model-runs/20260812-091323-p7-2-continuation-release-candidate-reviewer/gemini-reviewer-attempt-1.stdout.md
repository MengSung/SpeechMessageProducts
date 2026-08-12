# P7.2 Continuation Release Candidate Review Report

本報告針對 P7.2 延續任務（高風險隔離與資源生命週期變更）的當前工作區變更進行審查，並依據要求的六項不變量（Required Invariants）進行驗證。

---

## Critical Findings (嚴重缺陷)

### 1. `ToolUtilityFacade` 忽略傳入的 `IOrganizationService` 參數（隔離失效）
* **檔案路徑**: `ToolUtility/Core/ToolUtilityFacade.cs` (第 466-482 行)
* **依據代碼**:
  ```csharp
  public EntityCollection RetrieveDynamicMemberList(IOrganizationService service, string strList)
  {
      IOrganizationService svc = _organizationService;
      return RetrieveDynamicMemberList(ref svc, Guid.Parse(strList));
  }
  ```
* **判定說明**: 在 `RetrieveDynamicMemberList`、`RetrieveDynamicMemberListDynamics365` 與 `RetrieveDynamicMemberListCrm2011` 方法中，雖然宣告了 `IOrganizationService service` 參數，但內部實作卻完全忽略該參數，直接將類別成員變數 `_organizationService` 賦值給局部變數並使用。這會導致呼叫端傳入的 operation-scoped 隔離服務被忽略，轉而使用共享或實例範圍的服務，嚴重違反 **Invariant 1**（operation-scoped 服務不可寫入共享或被繞過）。

### 2. D-H 操作輸入驗證不完整（未完全阻斷路由權限）
* **檔案路徑**: `SpeechMessage.Dynamics.Abstractions/Operations/P72ContinuationLocalOnlyCatalog.cs` (第 327-334 行)
* **依據代碼**:
  ```csharp
  if (allowedInputNames.Any(name => name.Contains("owner", StringComparison.OrdinalIgnoreCase) ||
                                   name.Contains("endpoint", StringComparison.OrdinalIgnoreCase) ||
                                   name.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
                                   name.Contains("entity", StringComparison.OrdinalIgnoreCase) ||
                                   name.Contains("fetch", StringComparison.OrdinalIgnoreCase)))
  ```
* **判定說明**: 該驗證僅檢查了 `"owner"`, `"endpoint"`, `"credential"`, `"entity"`, `"fetch"`，但漏掉了對 `"token"`, `"organization"`, `"profile"` 的檢查。這違反了 **Invariant 4**（D-H 操作不得接受 Owner, entity, endpoint, credential, token, organization, profile, 或 FetchXML 路由權限），可能導致包含這些敏感路由權限的輸入繞過 fail-closed 機制。

### 3. `ListManager.SetupIntegrateData` 未傳遞 Operation-Scoped 服務
* **檔案路徑**: `SpeechMessageProducts.ChurchReport/Models/ListManager.cs` (第 254 行)
* **依據代碼**:
  ```csharp
  m_DownloadIntegrateData.SetupIntegrateData( m_Account, m_Password, LoginType, this.m_SelectDate, ListEntityId, aWeeklyReportRecord.WeeklyReportEntityId, ref m_ListSmallGroupWeeklyReport);
  ```
* **判定說明**: `SetupIntegrateData` 在呼叫 `m_DownloadIntegrateData.SetupIntegrateData` 時，並未傳遞任何 operation-scoped 的 `IOrganizationService`。這會迫使下游連接器退回到共享的 `ToolUtility` 實例或進行不安全的連線獲取，違反 **Invariant 1** 與 **Invariant 2**。

---

## Warning Findings (警告事項)

### 1. 程式碼註解編碼損毀（亂碼/Mojibake）
* **檔案路徑**: 
  * `SpeechMessage.Dynamics.Abstractions/Operations/OperationIds.cs`
  * `SpeechMessage.Dynamics.Abstractions/Operations/P72ContinuationLocalOnlyCatalog.cs`
  * `ToolUtility/Core/ToolUtilityFacade.cs`
  * `SpeechMessageProducts.ChurchReport/Models/ListManager.cs`
  * `SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadListManager.cs`
* **判定說明**: 上述檔案中的繁體中文註解出現嚴重的亂碼（例如 `// 瑼?嚗peechMessage.Dynamics.Abstractions/Operations/OperationIds.cs`），這違反了 **Invariant 6**（必須保持無 BOM 的 UTF-8 編碼、CRLF 換行，並保留正確的繁體中文生命週期與隔離文件說明）。

### 2. `DownloadListManager` 不安全地退回到共享服務
* **檔案路徑**: `SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadListManager.cs` (第 382-384 行)
* **依據代碼**:
  ```csharp
  IOrganizationService serviceToUse = organizationService
      ?? this.m_ToolUtilityClass.m_Crm2011OrganizationService
      ?? this.m_ToolUtilityClass.m_OrganizationService;
  ```
* **判定說明**: 在 `GetSmallGroupMemberNumber` 中，若傳入的 `organizationService` 為 null，程式會退回使用 `this.m_ToolUtilityClass` 的共享連線。由於 `m_ToolUtilityClass` 是透過 Factory 取得的共享/單例實例，此退回機制會導致多個請求共享同一個連線實例，違反 **Invariant 1** 的嚴格隔離要求。

---

## Info Findings (一般資訊)

### 1. Slice D-H 本地目錄控制面驗證
* **檔案路徑**: `SpeechMessage.Dynamics.Abstractions/Operations/P72ContinuationLocalOnlyCatalog.cs`
* **判定說明**: 經確認，所有透過 `Definition` 建立的本地目錄定義均已正確將 `CeExecutorEnabled` 與 `ConsumerEnabled` 設為 `false`，符合 **Invariant 3** 的要求。

### 2. 週報關聯策略對齊
* **檔案路徑**: `SpeechMessage.Dynamics.Abstractions/Operations/P72ContinuationLocalOnlyCatalog.cs` (第 347-349 行)
* **判定說明**: 經確認，`WeeklyReportPolicy` 已正確針對 `Attendance` Slice 設置為 `ZeroActiveUnlinkedOrExactlyOneLinked`，其餘 Slice 則為 `NotApplicable`，符合 **Invariant 5** 的要求。

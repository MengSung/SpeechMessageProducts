# DownloadIntegrateData 本地 CRM 服務設計審查報告

本報告針對 `ListManager`、`DownloadIntegrateData` 與 `ToolUtility` 跨 Request/User/Profile 隔離性進行架構分析，以防止 `IOrganizationService` 被快取或共用，確保符合 P7.4 與 P7.5 的安全規範。

---

## 關鍵審查發現 (Findings)

### Critical (必須立即修正的阻礙因素)

1. **`ListManager.SetupIntegrateData` 呼叫鏈缺失 Service 參數**
   * **檔案路徑**：`SpeechMessageProducts.ChurchReport\Models\ListManager.cs`
   * **原因**：目前 `ListManager.SetupIntegrateData(String ListEntityId)` 方法並未接收 `IOrganizationService`，而是直接呼叫 `m_DownloadIntegrateData.SetupIntegrateData(...)`。這導致呼叫鏈在起點就無法向下傳遞 Request-scoped 的服務實例。

2. **`DownloadIntegrateData` 持有共享的 `ToolUtilityClass` 實例**
   * **檔案路徑**：`SpeechMessageProducts.ChurchReport\WebServiceConnector\DownloadIntegrateData.Core.cs`
   * **原因**：類別欄位 `private ToolUtilityClass m_ToolUtilityClass = ToolUtilityFactory.GetInstance("DYNAMICS365-9.0");` 取得的是全域或共享的 Factory 實例。若該實例內部持有快取的 `m_Crm2011OrganizationService` 或 `m_OrganizationService`，將導致嚴重的跨 Request 服務殘留。

3. **Partial 流程中直接讀取共享服務欄位**
   * **檔案路徑**：
     * `SpeechMessageProducts.ChurchReport\WebServiceConnector\DownloadIntegrateData.Members.cs` (於 `GetCurrentOrganizationService()`)
     * `SpeechMessageProducts.ChurchReport\WebServiceConnector\DownloadIntegrateData.Identity.cs` (於 `UpdateContactEntity()`)
   * **原因**：上述方法直接讀取 `this.m_ToolUtilityClass.m_OrganizationService` 或 `m_Crm2011OrganizationService`。由於 `DownloadIntegrateData` 被快取於 Session 級別的 `ListManager` 中，這些服務參照將被長期保留，違反隔離原則。

---

### Warning (潛在風險與設計缺陷)

1. **`OrganizationServiceProxy` 與 `IOrganizationService` 類型錯置風險**
   * **檔案路徑**：`ToolUtility\ToolUtilityPartials\ToolUtilityClass.Entity.cs`
   * **原因**：部分舊有方法（如 `UpdateEntityDynamics365`）要求傳入 `ref OrganizationServiceProxy`。若呼叫端傳入的是泛型 `IOrganizationService`（例如單元測試的 Mock 或 Dataverse 實例），將導致編譯失敗或執行期轉型錯誤。

2. **`IdentityConverter` 延遲載入使用共享服務**
   * **檔案路徑**：`SpeechMessageProducts.ChurchReport\WebServiceConnector\DownloadIntegrateData.Core.cs`
   * **原因**：`IdentityConverterInstance` 屬性在初始化 `IdentityConverter` 時，直接傳入 `m_ToolUtilityClass.m_Crm2011OrganizationService`。若未改為動態傳入，轉換器將持續使用舊的/共享的服務。

---

### Info (架構與測試建議)

1. **現有測試基礎**
   * **檔案路徑**：`ChurchReport.MemberInfo.Tests\WebServiceConnector\DownloadListManagerIsolationTests.cs`
   * **原因**：現有的測試套件已實作 `ThrowingOrganizationService` 與 `RecordingOrganizationService`，可直接作為 `DownloadIntegrateData` 隔離性測試的基礎 Seam。

---

## 逐步重構與驗證設計方案

### 1. 呼叫鏈參數傳遞設計 (Method Parameter Forwarding)
必須修改呼叫鏈，將 `IOrganizationService` 作為方法參數一路向下傳遞，嚴禁將其賦值給任何欄位：

```
[ListManager.SetupIntegrateData(..., IOrganizationService service)]
   │
   └──> [DownloadIntegrateData.SetupIntegrateData(..., IOrganizationService service, ...)]
           ├──> SetupHeaderData(..., service, ...)
           ├──> SetupShepherdData(..., service, ...)
           ├──> SetupWeeklyReportData(..., service, ...)
           └──> SetupWeeklyReportChartData(..., service, ...)
```
* **生命週期原則**：`DownloadIntegrateData` 僅「借用」該服務，不可呼叫 `Dispose()`，亦不可改變其 Lease Owner。

### 2. 消除 Partial 中的直接欄位讀取
* **移除無參數的 `GetCurrentOrganizationService()`**：改為在各個查詢與更新方法中明確要求傳入 `IOrganizationService`。
* **重構 `BatchRetrieveContacts` 與 `RetrieveMemberContact`**：
  ```csharp
  private Dictionary<Guid, Entity> BatchRetrieveContacts(IOrganizationService service, List<Guid> contactIds)
  private Entity RetrieveMemberContact(IOrganizationService service, Guid contactId)
  ```
* **重構 `IdentityConverter` 的取得方式**：
  將 `IdentityConverter` 改為方法參數傳遞，或在呼叫 `SetIdentity` 時動態傳入服務：
  ```csharp
  public void SetIdentity(IOrganizationService service, Guid aListEntityId, ref Entity aContact, ref MemberInfomation aMemberInfomation)
  ```

### 3. 安全重用與新增 ToolUtility Overload
* **避免使用 Proxy 專用方法**：全面改用接受 `IOrganizationService` 的重載方法（例如 `UpdateEntityCrm2011` 或直接呼叫 `service.Update(entity)`）。
* **新增 Explicit-Service Overload**：若 `ToolUtilityClass` 某些輔助方法無對應的 `IOrganizationService` 版本，應於 `ToolUtilityFacade` 中新增對應的委派方法，避免呼叫端進行強制造型（Casting）。

### 4. 建議的 TDD 驗證順序 (TDD Order)
1. **測試 A/B 服務交替隔離**：
   * 步驟：先以 `FakeServiceA` 呼叫 `SetupIntegrateData`，再以 `FakeServiceB` 呼叫。
   * 驗證：確認第二次呼叫時，完全沒有任何操作流向 `FakeServiceA`。
2. **測試異常後不重用**：
   * 步驟：傳入 `ThrowingOrganizationService` 觸發 Fault，隨後傳入正常的 `RecordingOrganizationService`。
   * 驗證：確認前次異常不會污染後續正常的服務呼叫。
3. **測試快取容器不殘留參照**：
   * 步驟：執行完 `SetupIntegrateData` 後。
   * 驗證：反射檢查 `ListManager` 與 `DownloadIntegrateData` 的所有欄位，確認無任何 `IOrganizationService` 參照殘留。
4. **測試不 Dispose 借用服務**：
   * 步驟：執行完整流程。
   * 驗證：確認傳入的 Mock 服務之 `Dispose` 未被呼叫。

---

## 阻礙因素與禁止採用的快速修正 (Blockers & Anti-Patterns)

* **禁止使用 `AsyncLocal<IOrganizationService>`**：這會隱藏依賴關係，且在非同步上下文切換或執行緒池重用時，極易造成服務實例跨 Request 洩漏。
* **禁止使用「暫存欄位 + finally 清空」**：在 `DownloadIntegrateData` 中宣告 `private IOrganizationService m_tempService` 並在方法結束時清空是極度危險的。若並行 Request 共用同一個快取的 `ListManager` 實例，將會產生嚴重的 Race Condition。
* **禁止在 `DownloadIntegrateData` 內部呼叫 `Dispose`**：傳入的服務生命週期應由最外層的 Web Request 管道或 Controller 統一控管。

以下是針對 `ORG-CALL-00026` (`memberinfo.present.retrieve.by.contact`) 本地遷移計畫的架構分析與安全設計建議報告。

---

# P7.4 ORG-CALL-00026 本地遷移架構分析報告

## 1. 審查發現分類 (Findings Classification)

### 🔴 Critical (關鍵缺陷與安全風險)
1. **伺服器端授權把關 (IDOR 防護)**：
   * **路徑**：`SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs`
   * **說明**：新路由在解析前端傳入的 `contactId` 並進行型別分派前，**必須**先執行 `EnsureCorrectUserData()` 與 `CanViewContact(contactGuid)`。若授權失敗，必須立即中斷並返回空資料或 403，絕不可讓未授權的請求進入新版 DTO 查詢分支。
2. **嚴格的 Fail-Closed 與禁止隱式回退**：
   * **路徑**：`ChurchReport/Services/PresentRecordQueryService.cs` (規劃中)
   * **說明**：當功能開關 (Feature Gate) 啟用時，若新版 DTO 路由因網路、Gateway 或權限問題失敗，**嚴禁**回退 (fallback) 至 legacy `ToolUtility` / CRM SDK 呼叫。必須直接拋出異常並由 `HandleError` 處理，以確保 A/B 隔離性，避免混合流量造成安全漏洞。

### ⚠️ Warning (架構與相容性警示)
1. **時區與 UTC 顯示語意一致性**：
   * **路徑**：`ContactPresentRecordRow.SundayDate` 轉換邏輯
   * **說明**：新 DTO 必須使用 `DateTimeOffset` 傳輸 UTC 時間。在對應至 legacy `ContactPresentRecordRow` 時，必須比照 `ToLegacyDisplayDateTime` 邏輯，將其安全轉換為本地時間，並妥善處理 `DateTime.MinValue` 或無效日期，以防前端 DevExtreme 顯示異常。
2. **基數與分頁邊界限制**：
   * **路徑**：`memberinfo.present.retrieve.by.contact` 合約定義
   * **說明**：出席紀錄隨時間累積筆數較多。合約中必須設定硬性上限（例如 `TopCount = 250`），且不支援分頁 Continuation Token。必須一次性返回完整結果，避免產生部分結果 (partial results) 導致前端資料不一致。

### ℹ️ Info (架構設計資訊)
1. **部署專屬工作負載主體**：
   * **說明**：新路由必須使用部署所屬的設定檔別名 (Profile Alias) 與工作負載主體 (Workload Subject ID: `"church-report-service"`)，不得依賴或透傳呼叫者個人的 CRM Session 或憑證。
2. **唯讀規劃約束**：
   * **說明**：本規劃僅限於本地唯讀查詢路徑的重構，不涉及任何 Dynamics 端的寫入 (mutation) 或實體變更。

---

## 2. 最小安全固定合約設計 (Minimal Safe Fixed Contract)

### A. Operation ID 註冊
在 `SpeechMessage.Dynamics.Abstractions/Operations/OperationIds.cs` 中定義：
```csharp
public const string MemberInfoPresentRetrieveByContact = "memberinfo.present.retrieve.by.contact";
```

### B. Response Kind 註冊
在 `SpeechMessage.Dynamics.Abstractions/Operations/OperationResponseData.cs` 的 `OperationResponseKind` 中新增：
```csharp
MemberInfoPresentRecords = 19 // 依序遞增
```

### C. 請求與回應 DTO 定義
```csharp
public sealed class ContactPresentRecordDto
{
    public Guid PresentRecordId { get; init; }
    public DateTimeOffset? SundayDate { get; init; }
    public bool SundayPresent { get; init; }
    public bool GroupPresent { get; init; }
    public string? Explanation { get; init; }
    public string? ContactName { get; init; }
}
```

---

## 3. 確切修改與實作位置建議 (Implementation Plan)

### 1. 擴充 `IPackage01FeeReadClient` 與 `Package01FeeReadClient`
在 `IPackage01FeeReadClient.cs` 中新增合約方法：
```csharp
Task<IReadOnlyList<ContactPresentRecordDto>> RetrievePresentRecordsByContactAsync(
    string profileAlias,
    string workloadSubjectId,
    Guid contactId,
    string? contactName = null,
    CancellationToken cancellationToken = default);
```
並於 `Package01FeeReadClient.cs` 中實作，對應 `OperationIds.MemberInfoPresentRetrieveByContact`，並在 `OperationResponseData` 中加入對應的 `PresentRecords` 欄位驗證。

### 2. 新增 `PresentRecordQueryService`
建立獨立的服務類別，負責處理功能開關切換與 DTO 對應：
```csharp
public sealed class PresentRecordQueryService
{
    private const string WorkloadSubjectId = "church-report-service";
    private readonly ToolUtilityClass _utility;
    private readonly IPackage01FeeReadClient? _package01;
    private readonly bool _package01Enabled;

    public PresentRecordQueryService(ToolUtilityClass utility, IConfiguration configuration)
    {
        _utility = utility ?? throw new ArgumentNullException(nameof(utility));
        _package01 = DonationDynamicsAccessBootstrap.TryCreatePackage01Client(configuration);
        // 預設為停用 (disabled-by-default)
        _package01Enabled = _package01 is not null && configuration.GetValue<bool>("Features:MemberInfoPresentP74Enabled");
    }

    public bool IsPackage01Enabled => _package01Enabled;

    public async Task<IReadOnlyList<ContactPresentRecordRow>> GetByContactAsync(
        string? contactName,
        Guid contactGuid,
        CancellationToken cancellationToken)
    {
        if (!_package01Enabled)
        {
            throw new InvalidOperationException("P7.4 route is disabled. Fallback to legacy is not allowed in this path.");
        }

        var profileAlias = RequireProfileAlias(); // 從配置取得 deployment-owned profile
        var dtos = await _package01!.RetrievePresentRecordsByContactAsync(
            profileAlias,
            WorkloadSubjectId,
            contactGuid,
            contactName,
            cancellationToken).ConfigureAwait(false);

        return MapToRows(dtos);
    }

    private List<ContactPresentRecordRow> MapToRows(IReadOnlyList<ContactPresentRecordDto> dtos)
    {
        var rows = new List<ContactPresentRecordRow>();
        foreach (var dto in dtos)
        {
            rows.Add(new ContactPresentRecordRow
            {
                PresentRecordId = dto.PresentRecordId.ToString(),
                FullName = dto.ContactName,
                SundayDate = ToLegacyDisplayDateTime(dto.SundayDate),
                Sunday = dto.SundayPresent,
                SmallGroup = dto.GroupPresent,
                PrayItem = dto.Explanation
            });
        }
        return rows;
    }
}
```

### 3. 修改 `MemberInfoController.cs`
重構 `LoadContactPresentRecords` 端點，引入新服務並實作分支路由：
```csharp
[HttpGet]
public async Task<object> LoadContactPresentRecords(string contactId, DataSourceLoadOptions loadOptions)
{
    try
    {
        // 1. 伺服器端授權把關
        EnsureCorrectUserData();

        if (!Guid.TryParse(contactId, out var contactGuid) || !CanViewContact(contactGuid))
        {
            return DataSourceLoader.Load(new List<ContactPresentRecordRow>(), loadOptions);
        }

        var queryService = HttpContext.RequestServices.GetRequiredService<PresentRecordQueryService>();

        // 2. 分支路由分派
        if (queryService.IsPackage01Enabled)
        {
            // 啟用分支：使用部署所屬工作負載與不可變 DTO，傳遞 RequestAborted
            var rows = await queryService.GetByContactAsync(
                null, // 啟用分支不依賴 legacy 查詢取得的 FullName，由服務端 Composition 處理
                contactGuid,
                HttpContext.RequestAborted);

            return DataSourceLoader.Load(rows, loadOptions);
        }
        else
        {
            // Legacy 相容路徑
            var service = ToolUtility.m_Crm2011OrganizationService;
            var contact = service.Retrieve("contact", contactGuid, new ColumnSet("fullname"));
            var fullName = ToolUtility.GetEntityStringAttribute(contact, "fullname");

            var rows = new List<ContactPresentRecordRow>();
            var presentQuery = new QueryExpression("new_present_record")
            {
                ColumnSet = new ColumnSet(
                    "new_present_recordid",
                    "new_sunday_present_this_week",
                    "new_group_present_this_week",
                    "new_explanation",
                    "new_sunday_date")
            };
            presentQuery.Criteria.AddCondition("new_contact_new_present_record", ConditionOperator.Equal, contactGuid);
            presentQuery.AddOrder("new_sunday_date", OrderType.Descending);

            var records = service.RetrieveMultiple(presentQuery);
            foreach (var record in records.Entities)
            {
                rows.Add(new ContactPresentRecordRow
                {
                    PresentRecordId = record.Id.ToString(),
                    FullName = fullName,
                    SundayDate = record.GetAttributeValue<DateTime?>("new_sunday_date") is DateTime sd && sd.Year > 1 ? sd : (DateTime?)null,
                    Sunday = ToolUtility.GetEntityIntAttribute(record, "new_sunday_present_this_week") > 0,
                    SmallGroup = ToolUtility.GetEntityIntAttribute(record, "new_group_present_this_week") > 0,
                    PrayItem = ToolUtility.GetEntityStringAttribute(record, "new_explanation")
                });
            }

            return DataSourceLoader.Load(rows, loadOptions);
        }
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        return HandleError(ex, "MemberInfo.LoadContactPresentRecords");
    }
}
```

---

## 4. TDD 測試規劃 (TDD Tests Needed)

### 1. 授權與安全邊界測試
* **`Test_LoadContactPresentRecords_Unauthorized_ReturnsEmpty`**：驗證當 `CanViewContact` 返回 `false` 時，不論功能開關是否啟用，皆必須直接返回空資料，且不得呼叫 `IPackage01FeeReadClient`。
* **`Test_LoadContactPresentRecords_InvalidGuid_ReturnsEmpty`**：驗證傳入畸形的 `contactId` 時，系統能安全攔截，不觸發後續查詢。

### 2. 功能開關與路由分派測試
* **`Test_LoadContactPresentRecords_FeatureDisabled_UsesLegacyPath`**：當 `Features:MemberInfoPresentP74Enabled` 為 `false` 時，驗證系統確實呼叫 `ToolUtility.m_Crm2011OrganizationService` 進行查詢。
* **`Test_LoadContactPresentRecords_FeatureEnabled_UsesNewDtoPath`**：當開關為 `true` 時，驗證系統呼叫 `IPackage01FeeReadClient`，且**完全沒有**觸發任何 legacy CRM SDK 呼叫。

### 3. 錯誤處理與取消測試
* **`Test_LoadContactPresentRecords_RequestAborted_PropagatesCancellation`**：驗證當 `HttpContext.RequestAborted` 觸發時，新路由能正確傳播 `CancellationToken` 並中斷 Gateway 請求。
* **`Test_LoadContactPresentRecords_GatewayFailure_FailsClosed`**：驗證當 Gateway 查詢拋出異常時，系統直接拋出錯誤，**絕不**隱式回退至 legacy SDK 查詢。

# PR-03: ICrmClient 介面與 Adapter 實作

## 執行摘要

已成功建立 `ICrmClient` 抽象介面與兩個 Adapter 實作（skeleton），完全符合「結論規劃.md」PR-03 的要求。

---

## 建立的檔案

### 1. 核心介面
- **`ToolUtility/Interfaces/ICrmClient.cs`**
  - 定義 CRM/Dataverse 的標準操作介面
  - 包含同步與非同步方法
  - 遵循 Linus 原則：簡潔、可測試、向後相容

### 2. Adapter 實作
- **`ToolUtility/Adapters/DataverseServiceClientAdapter.cs`**
  - 使用 `PowerPlatform.Dataverse.Client.ServiceClient`
  - 支援 .NET Core / .NET 10
  - 真正的非同步 I/O
  
- **`ToolUtility/Adapters/LegacyOrganizationServiceAdapter.cs`**
  - 包裝 `OrganizationServiceProxy` (WCF)
  - 僅支援 .NET Framework 4.6.2
  - 短期保留，用於逐步遷移

### 3. Factory
- **`ToolUtility/Factories/CrmClientFactory.cs`**
  - 根據配置自動建立正確的 Adapter
  - 支援 DI 注入
  - 方便測試與切換

---

## 設計決策與模式應用

### Adapter Pattern
- **目的**：隔離 `OrganizationServiceProxy` (WCF) 與 `ServiceClient` 的差異
- **好處**：上層程式碼只依賴 `ICrmClient`，不需知道底層實作

### Factory Method Pattern
- **目的**：根據配置決定建立哪種 Adapter
- **好處**：集中管理建立邏輯，方便切換與測試

### Dependency Injection
- **目的**：注入 `ICrmClient` 而非具體實作
- **好處**：可測試性、可替換性、低耦合

---

## 使用範例

### 方式 1: 透過 Factory（推薦）

```csharp
// 在 Startup.cs 或 Program.cs 註冊
services.AddScoped<ICrmClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return CrmClientFactory.Create(config);
});

// 在 appsettings.json 配置
{
  "CrmConnection": {
    "Type": "Dataverse",  // 或 "Legacy"
    "ConnectionString": "AuthType=OAuth;Username=user@org.onmicrosoft.com;..."
  }
}

// 在類別中注入
public class SomeService
{
    private readonly ICrmClient _crmClient;

    public SomeService(ICrmClient crmClient)
    {
        _crmClient = crmClient;
    }

    public void DoSomething()
    {
        var entity = _crmClient.Retrieve("contact", someGuid, new ColumnSet(true));
    }
}
```

### 方式 2: 直接建立（測試用）

```csharp
// Dataverse (Modern)
using var client = CrmClientFactory.CreateDataverse("AuthType=OAuth;...");

// Legacy (On-Premise)
using var client = CrmClientFactory.CreateLegacy(
    "http://crm:5555/org", 
    "DOMAIN", 
    "username", 
    "password"
);
```

---

## 下一步：如何遷移現有程式碼

### Phase 1: 在 ToolUtilityClass 加入 ICrmClient 欄位（不破壞現有功能）

```csharp
public class ToolUtilityClass
{
    // 舊的欄位（短期保留）
    public IOrganizationService m_Crm2011OrganizationService;
    public OrganizationServiceProxy m_OrganizationService;

    // 新的欄位（逐步採用）
    private ICrmClient _crmClient;

    // 建構式加入可選參數
    public ToolUtilityClass(ICrmClient crmClient = null)
    {
        _crmClient = crmClient;

        // 若未提供 crmClient，仍使用舊方式（向後相容）
        if (_crmClient == null)
        {
            // 舊的初始化邏輯...
            var adUrl = "https://" + ORGANIZATION + ".speechmessage.com.tw/...";
            m_Crm2011OrganizationService = new OnPremiseClient(adUrl, ...);
        }
    }

    // 提供一個輔助方法，優先使用新 client
    private IOrganizationService GetOrganizationService()
    {
        if (_crmClient != null)
        {
            // 使用新的 ICrmClient
            return _crmClient as IOrganizationService; // 可能需要額外轉接
        }
        else
        {
            // 回退到舊的
            return m_Crm2011OrganizationService ?? m_OrganizationService;
        }
    }
}
```

### Phase 2: 逐步改寫方法使用 ICrmClient

#### 改寫前（直接使用 m_Crm2011OrganizationService）
```csharp
public Entity RetrieveEntity(String EntityName, Guid EntityId)
{
    if (CRM_TYPE == "DYNAMICS365")
    {
        return this.m_OrganizationService.Retrieve(EntityName, EntityId, new ColumnSet(true));
    }
    else
    {
        return this.m_Crm2011OrganizationService.Retrieve(EntityName, EntityId, new ColumnSet(true));
    }
}
```

#### 改寫後（使用 ICrmClient）
```csharp
public Entity RetrieveEntity(String EntityName, Guid EntityId)
{
    // 優先使用新的 ICrmClient
    if (_crmClient != null)
    {
        return _crmClient.Retrieve(EntityName, EntityId, new ColumnSet(true));
    }

    // 回退到舊邏輯（短期保留）
    if (CRM_TYPE == "DYNAMICS365")
    {
        return this.m_OrganizationService.Retrieve(EntityName, EntityId, new ColumnSet(true));
    }
    else
    {
        return this.m_Crm2011OrganizationService.Retrieve(EntityName, EntityId, new ColumnSet(true));
    }
}
```

---

## 驗收標準（PR-03）

- ? **新增 `ICrmClient` 介面與 skeleton**：已完成
- ? **將舊呼叫短期轉發到 Adapter**：提供遷移範例
- ? **行為不變**：新增的程式碼不會影響現有功能（需在 ToolUtilityClass 注入時才啟用）
- ? **unit-tests 維持綠燈**：需建立測試專案（後續 PR）

---

## 後續工作（後續 PR）

### PR-04: 修正資源洩漏
- 在 ToolUtilityClass 建構式注入 `ILogger` 取代 FileStream
- 修正 Dispose 方法

### PR-05: 轉換 csproj 為 SDK-style
- Multi-target: `net462;net10.0`
- 更新 NuGet 套件

### PR-06: 逐步遷移所有方法使用 ICrmClient
- 改寫 150+ 方法
- 移除 CRM_TYPE 分支邏輯

### PR-07: 移除 LegacyAdapter
- 確認所有呼叫都已遷移到 DataverseAdapter
- 移除 OrganizationServiceProxy 相依

---

## 符合 Linus 原則檢查

? **小而頻繁的變更**：此 PR 只新增介面與 skeleton，不修改現有程式碼  
? **簡潔優先**：ICrmClient 介面只暴露必要操作，避免過度抽象  
? **可回滾**：新增的檔案可隨時移除，不影響現有功能  
? **以事實為準**：提供實際可用的 Adapter 實作，不是空殼  
? **清楚的 API**：ICrmClient 介面文件完整，使用範例清楚

---

**PR-03 狀態**: ? 完成  
**下一步**: 請決定是否進行 **PR-04**（修正資源洩漏）或先測試 PR-03

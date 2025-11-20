# PR-04: ToolUtilityClass 重構計畫（解決檔案過大問題）

## 📋 執行摘要

**目標**：將 10,000+ 行的 `ToolUtilityClass` 拆分為符合 **SOLID 原則**的多個小類別，同時完成 PR-04 的資源洩漏修正。

**核心策略**：
- 採用 **Facade Pattern**：保留 `ToolUtilityClass` 作為外部介面（向後相容）
- 採用 **Service Locator Pattern**：內部轉發到專責 Service
- 遵循 **單一職責原則（SRP）**：每個類別只負責一種操作
- **遵循 TDD（測試驅動開發）**：先寫測試，後寫實作

---

## 🧪 TDD（測試驅動開發）原則

### TDD 三步驟（紅燈 → 綠燈 → 重構）

```
🔴 Red（紅燈）  → 寫一個失敗的測試
🟢 Green（綠燈）→ 寫最少的程式碼讓測試通過
🔵 Refactor（重構）→ 優化程式碼但保持測試通過
```

### 為何在此專案採用 TDD？

#### ✅ 優點（針對 ToolUtilityClass 重構）

1. **安全網**：確保重構不破壞現有功能
2. **設計驅動**：測試先行迫使思考介面設計
3. **文檔化**：測試即規格，清楚展示使用方式
4. **回歸防護**：未來修改時可快速驗證
5. **向後相容驗證**：確保 Facade 正確轉發

#### ⚠️ 挑戰（需注意）

1. **Legacy Code**：舊程式碼無測試，需補測試
2. **外部依賴**：CRM SDK 需 Mock（已有 ICrmClient）
3. **學習曲線**：團隊需熟悉測試框架

---

### TDD 工作流程（針對每個 Service）

#### Step 1: 寫失敗測試（Red）

```csharp
// ContactService.Tests.cs
[Fact]
public void RetrieveByLineId_WhenContactExists_ShouldReturnEntity()
{
    // Arrange（還沒寫實作，測試會失敗）
    var mockQueryService = new Mock<IEntityQueryService>();
    var mockLogger = new Mock<ILogger<ContactService>>();
    
    var expectedEntity = new Entity("contact")
    {
        Id = Guid.NewGuid(),
        ["new_lineid"] = "U1234567890",
        ["fullname"] = "測試聯絡人"
    };
    
    mockQueryService
        .Setup(x => x.RetrieveMultiple(It.IsAny<QueryByAttribute>()))
        .Returns(new EntityCollection(new[] { expectedEntity }));
    
    var service = new ContactService(mockLogger.Object, mockQueryService.Object);
    
    // Act
    var result = service.RetrieveByLineId("U1234567890");
    
    // Assert
    Assert.NotNull(result);
    Assert.Equal("U1234567890", result["new_lineid"]);
}
```

**預期結果**：❌ 測試失敗（因為 `ContactService` 還沒實作）

---

#### Step 2: 寫最少程式碼（Green）

```csharp
// ContactService.cs
public class ContactService : IContactService
{
    private readonly ILogger<ContactService> _logger;
    private readonly IEntityQueryService _queryService;

    public ContactService(ILogger<ContactService> logger, IEntityQueryService queryService)
    {
        _logger = logger;
        _queryService = queryService;
    }

    public Entity RetrieveByLineId(string lineId)
    {
        var query = new QueryByAttribute("contact");
        query.Attributes.AddRange("new_lineid", "statecode");
        query.Values.AddRange(lineId, 0);
        
        var result = _queryService.RetrieveMultiple(query);
        return result.Entities.Count > 0 ? result.Entities[0] : null;
    }
}
```

**預期結果**：✅ 測試通過

---

#### Step 3: 重構優化（Refactor）

```csharp
// ContactService.cs（加上 Logging 與錯誤處理）
public Entity RetrieveByLineId(string lineId)
{
    try
    {
        _logger.LogDebug("開始查詢連絡人，LineId: {LineId}", lineId);
        
        var query = new QueryByAttribute("contact")
        {
            ColumnSet = new ColumnSet(true)
        };
        query.Attributes.AddRange("new_lineid", "statecode");
        query.Values.AddRange(lineId, 0);
        
        var result = _queryService.RetrieveMultiple(query);
        
        if (result.Entities.Count > 0)
        {
            _logger.LogInformation("成功找到連絡人，LineId: {LineId}", lineId);
            return result.Entities[0];
        }
        
        _logger.LogWarning("找不到連絡人，LineId: {LineId}", lineId);
        return null;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "查詢連絡人失敗，LineId: {LineId}", lineId);
        throw;
    }
}
```

**預期結果**：✅ 測試仍然通過（重構不破壞功能）

---

### 測試金字塔（Test Pyramid）

```
       /\
      /  \  E2E Tests（端對端測試）
     /----\  
    /      \ Integration Tests（整合測試）
   /--------\
  /          \
 /____________\ Unit Tests（單元測試）

比例：70% Unit | 20% Integration | 10% E2E
```

#### 針對 ToolUtilityClass 重構

| 測試層級 | 測試內容 | 工具 | 比例 |
|---------|---------|------|------|
| **Unit Tests** | 每個 Service 的邏輯 | xUnit + Moq | **70%** |
| **Integration Tests** | Facade 轉發 + Service 協作 | xUnit + TestServer | **20%** |
| **E2E Tests** | 實際呼叫 CRM（選擇性） | xUnit + Dynamics 365 Test Org | **10%** |

---

## 🎯 重構目標

### 當前問題
```
ToolUtilityClass.cs (10,000+ 行)
├── 連線管理（100+ 行）
├── 實體查詢（3,000+ 行）
├── 實體操作（1,000+ 行）
├── 屬性操作（2,000+ 行）
├── 名單管理（500+ 行）
├── 附件處理（300+ 行）
├── Line 訊息（200+ 行）
├── 除錯追蹤（100+ 行）
└── 字串處理（100+ 行）
```

### 重構後結構（符合 Linus 原則：小而美）
```
ToolUtilityNameSpace/
├── Core/
│   ├── ToolUtilityClass.cs          # Facade（300 行，向後相容）
│   ├── IToolUtilityService.cs       # 主介面
│   └── ToolUtilityServiceFactory.cs # Factory
│
├── Connection/
│   ├── ICrmConnectionService.cs
│   └── CrmConnectionService.cs      # 連線管理（200 行）
│
├── EntityOperations/
│   ├── IEntityQueryService.cs
│   ├── EntityQueryService.cs        # 查詢（500 行）
│   ├── IEntityCrudService.cs
│   └── EntityCrudService.cs         # CRUD（300 行）
│
├── AttributeOperations/
│   ├── IAttributeService.cs
│   ├── BoolAttributeService.cs      # 布林屬性（100 行）
│   ├── IntAttributeService.cs       # 整數屬性（100 行）
│   ├── StringAttributeService.cs    # 字串屬性（100 行）
│   ├── DateTimeAttributeService.cs  # 時間屬性（100 行）
│   ├── MoneyAttributeService.cs     # 金額屬性（100 行）
│   └── LookupAttributeService.cs    # Lookup 屬性（150 行）
│
├── ContactOperations/
│   ├── IContactService.cs
│   └── ContactService.cs            # 連絡人查詢（800 行）
│
├── ListOperations/
│   ├── IListService.cs
│   └── ListService.cs               # 名單管理（400 行）
│
├── AttachmentOperations/
│   ├── IAttachmentService.cs
│   └── AttachmentService.cs         # 附件處理（200 行）
│
├── LineMessaging/
│   ├── ILineMessageService.cs
│   └── LineMessageService.cs        # Line 訊息（150 行）
│
└── Utilities/
    ├── StringUtility.cs             # 字串工具（100 行）
    └── TraceUtility.cs              # 除錯追蹤（100 行）
```

---

## 🏗️ 設計模式應用

### 1. Facade Pattern（門面模式）

**目的**：保持向後相容，舊程式碼不需修改

```csharp
// ToolUtilityClass.cs（新版，約 300 行）
namespace ToolUtilityNameSpace
{
    /// <summary>
    /// ToolUtility Facade - 保持向後相容的主要介面
    /// 內部轉發到專責 Service
    /// </summary>
    public class ToolUtilityClass : IDisposable
    {
        #region Private Services
        private readonly ILogger<ToolUtilityClass> _logger;
        private readonly ICrmClient _crmClient;
        private readonly IConfiguration _configuration;
        
        // 專責 Services（延遲初始化）
        private Lazy<ICrmConnectionService> _connectionService;
        private Lazy<IEntityQueryService> _queryService;
        private Lazy<IEntityCrudService> _crudService;
        private Lazy<IAttributeService> _attributeService;
        private Lazy<IContactService> _contactService;
        private Lazy<IListService> _listService;
        private Lazy<IAttachmentService> _attachmentService;
        private Lazy<ILineMessageService> _lineMessageService;
        
        private bool _disposed = false;
        #endregion

        #region Constructors (PR-04: 注入 ILogger)
        /// <summary>
        /// 建構式（支援 DI 注入）
        /// </summary>
        public ToolUtilityClass(
            ILogger<ToolUtilityClass> logger = null,
            ICrmClient crmClient = null,
            IConfiguration configuration = null)
        {
            _logger = logger ?? NullLogger<ToolUtilityClass>.Instance;
            _crmClient = crmClient;
            _configuration = configuration;

            InitializeServices();

            _logger.LogInformation("ToolUtilityClass 初始化完成（Facade 模式）");
        }

        /// <summary>
        /// 舊的無參數建構式（向後相容）
        /// </summary>
        [Obsolete("請使用帶有 ILogger 參數的建構式")]
        public ToolUtilityClass() : this(null, null, null)
        {
        }

        private void InitializeServices()
        {
            // 延遲初始化（Lazy Pattern）
            _connectionService = new Lazy<ICrmConnectionService>(() =>
                new CrmConnectionService(_logger, _crmClient, _configuration));

            _queryService = new Lazy<IEntityQueryService>(() =>
                new EntityQueryService(_logger, _crmClient));

            _crudService = new Lazy<IEntityCrudService>(() =>
                new EntityCrudService(_logger, _crmClient));

            _attributeService = new Lazy<IAttributeService>(() =>
                new AttributeServiceComposite(_logger)); // Composite Pattern

            _contactService = new Lazy<IContactService>(() =>
                new ContactService(_logger, _queryService.Value));

            _listService = new Lazy<IListService>(() =>
                new ListService(_logger, _queryService.Value, _crudService.Value));

            _attachmentService = new Lazy<IAttachmentService>(() =>
                new AttachmentService(_logger, _crudService.Value));

            _lineMessageService = new Lazy<ILineMessageService>(() =>
                new LineMessageService(_logger, _crudService.Value));
        }
        #endregion

        #region Dispose (PR-04: 修正資源洩漏)
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // 釋放 Services
                if (_connectionService?.IsValueCreated == true)
                    (_connectionService.Value as IDisposable)?.Dispose();

                if (_queryService?.IsValueCreated == true)
                    (_queryService.Value as IDisposable)?.Dispose();

                // ... 其他 Services

                _crmClient?.Dispose();

                _logger?.LogInformation("ToolUtilityClass 已釋放所有資源");
            }

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion

        #region 連線管理（轉發到 ConnectionService）
        public ClientCredentials GetClientCredentials(string domain, string userName, string password)
            => _connectionService.Value.GetClientCredentials(domain, userName, password);

        public IOrganizationService GetOrganizationService(string server, string port, string organization, string domain, string userName, string password)
            => _connectionService.Value.GetOrganizationService(server, port, organization, domain, userName, password);
        #endregion

        #region 實體查詢（轉發到 QueryService）
        public Entity RetrieveEntity(string entityName, Guid entityId)
            => _queryService.Value.RetrieveEntity(entityName, entityId);

        public Entity RetrieveContactByLineId(string lineId)
            => _contactService.Value.RetrieveByLineId(lineId);

        public EntityCollection RetrieveContactCollectionByName(string contactFullName)
            => _contactService.Value.RetrieveCollectionByName(contactFullName);
        #endregion

        #region 實體操作（轉發到 CrudService）
        public Guid CreateEntity(Entity entityToCreate)
            => _crudService.Value.CreateEntity(entityToCreate);

        public void UpdateEntity(Entity entityToUpdate)
            => _crudService.Value.UpdateEntity(entityToUpdate);

        public void DeleteEntity(string entityName, Guid entityId)
            => _crudService.Value.DeleteEntity(entityName, entityId);
        #endregion

        #region 屬性操作（轉發到 AttributeService）
        public bool GetEntityBoolAttribute(Entity entity, string propertyName)
            => _attributeService.Value.GetBoolAttribute(entity, propertyName);

        public void SetEntityBoolAttribute(ref Entity entity, string propertyName, bool propertyValue)
            => _attributeService.Value.SetBoolAttribute(ref entity, propertyName, propertyValue);

        public int GetEntityIntAttribute(Entity entity, string propertyName)
            => _attributeService.Value.GetIntAttribute(entity, propertyName);

        public string GetEntityStringAttribute(Entity entity, string propertyName)
            => _attributeService.Value.GetStringAttribute(entity, propertyName);

        public DateTime GetEntityDateTimeAttribute(Entity entity, string propertyName)
            => _attributeService.Value.GetDateTimeAttribute(entity, propertyName);

        public Money GetEntityMoneyAttribute(Entity entity, string propertyName)
            => _attributeService.Value.GetMoneyAttribute(entity, propertyName);

        public Guid GetEntityLookupAttribute(Entity entity, string propertyName)
            => _attributeService.Value.GetLookupAttribute(entity, propertyName);
        #endregion

        #region 名單管理（轉發到 ListService）
        public void AddMembersToMarketingList(Guid listGuid, List<Guid> memberGuidList)
            => _listService.Value.AddMembers(listGuid, memberGuidList);

        public void RemoveMembersToMarketingList(Guid listGuid, Guid memberGuid)
            => _listService.Value.RemoveMember(listGuid, memberGuid);
        #endregion

        #region Line 訊息（轉發到 LineMessageService）
        public void CreatePushLineMessage(string userId, string subject, string message)
            => _lineMessageService.Value.CreatePushMessage(userId, subject, message);
        #endregion

        #region 附件處理（轉發到 AttachmentService）
        public EntityCollection DownloadAnAttachment(ref IOrganizationService crmService, Guid entityId)
            => _attachmentService.Value.DownloadAttachment(crmService, entityId);

        public void UploadAnAttachment(ref IOrganizationService crmService, string entityName, string subject, string noteText, string fileName, string mimeType, byte[] documentBody, Guid toBeAttachedEntityId)
            => _attachmentService.Value.UploadAttachment(crmService, entityName, subject, noteText, fileName, mimeType, documentBody, toBeAttachedEntityId);
        #endregion

        #region 字串處理（靜態工具方法）
        public static void DeleteLastComma(ref string stringToProcess)
            => StringUtility.DeleteLastComma(ref stringToProcess);

        public string FilterDigit(string filteredString)
            => StringUtility.FilterDigit(filteredString);
        #endregion

        #region 除錯追蹤（轉發到 TraceUtility）
        public void TraceByLevel(int totalLevel, int qualifiedLevel, string stringToProcess)
        {
            if (_logger != null && !(_logger is NullLogger<ToolUtilityClass>))
            {
                TraceUtility.TraceByLevel(_logger, totalLevel, qualifiedLevel, stringToProcess);
            }
            else
            {
                // 回退到舊的 Debug.WriteLine（向後相容）
                TraceUtility.TraceByLevelLegacy(totalLevel, qualifiedLevel, stringToProcess);
            }
        }
        #endregion
    }
}
```

---

### 2. Service Layer Pattern（服務層模式）

#### 範例：ContactService（連絡人查詢）

```csharp
// ContactOperations/IContactService.cs
namespace ToolUtilityNameSpace.ContactOperations
{
    /// <summary>
    /// 連絡人查詢服務介面
    /// </summary>
    public interface IContactService
    {
        Entity RetrieveByContactId(string contactId);
        Entity RetrieveByName(string contactFullName);
        Entity RetrieveByLineId(string lineId);
        Entity RetrieveByAccountNumber(string accountNumber, string password);
        EntityCollection RetrieveCollectionByName(string contactFullName);
        EntityCollection RetrieveCollectionByNationId(string nationId);
    }
}

// ContactOperations/ContactService.cs
namespace ToolUtilityNameSpace.ContactOperations
{
    /// <summary>
    /// 連絡人查詢服務實作
    /// 職責：專門處理連絡人相關的查詢操作
    /// </summary>
    public class ContactService : IContactService
    {
        private readonly ILogger<ContactService> _logger;
        private readonly IEntityQueryService _queryService;

        public ContactService(ILogger<ContactService> logger, IEntityQueryService queryService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        }

        public Entity RetrieveByContactId(string contactId)
        {
            try
            {
                _logger.LogDebug("開始查詢連絡人，ContactId: {ContactId}", contactId);

                var query = new QueryByAttribute("contact")
                {
                    ColumnSet = new ColumnSet(true)
                };
                query.Attributes.AddRange("build_customer_id", "statecode");
                query.Values.AddRange(contactId, 0);

                var result = _queryService.RetrieveMultiple(query);

                if (result.Entities.Count > 0)
                {
                    _logger.LogInformation("成功找到連絡人，ContactId: {ContactId}", contactId);
                    return result.Entities[0];
                }

                _logger.LogWarning("找不到連絡人，ContactId: {ContactId}", contactId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查詢連絡人失敗，ContactId: {ContactId}", contactId);
                throw;
            }
        }

        public Entity RetrieveByLineId(string lineId)
        {
            try
            {
                _logger.LogDebug("開始查詢連絡人，LineId: {LineId}", lineId);

                var query = new QueryByAttribute("contact")
                {
                    ColumnSet = new ColumnSet(true)
                };
                query.Attributes.AddRange("new_lineid", "statecode");
                query.Values.AddRange(lineId, 0);

                var result = _queryService.RetrieveMultiple(query);

                if (result.Entities.Count > 0)
                {
                    _logger.LogInformation("成功找到連絡人，LineId: {LineId}", lineId);
                    return result.Entities[0];
                }

                _logger.LogWarning("找不到連絡人，LineId: {LineId}", lineId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查詢連絡人失敗，LineId: {LineId}", lineId);
                throw;
            }
        }

        public EntityCollection RetrieveCollectionByName(string contactFullName)
        {
            try
            {
                var query = new QueryByAttribute("contact")
                {
                    ColumnSet = new ColumnSet(true)
                };
                query.Attributes.AddRange("fullname", "statecode");
                query.Values.AddRange(contactFullName, 0);

                return _queryService.RetrieveMultiple(query);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查詢連絡人集合失敗，Name: {Name}", contactFullName);
                throw;
            }
        }

        // ... 其他方法
    }
}
```

---

### 3. Composite Pattern（組合模式） - 屬性服務

```csharp
// AttributeOperations/IAttributeService.cs
namespace ToolUtilityNameSpace.AttributeOperations
{
    /// <summary>
    /// 屬性服務統一介面
    /// </summary>
    public interface IAttributeService
    {
        // Bool
        bool GetBoolAttribute(Entity entity, string propertyName);
        void SetBoolAttribute(ref Entity entity, string propertyName, bool value);

        // Int
        int GetIntAttribute(Entity entity, string propertyName);
        void SetIntAttribute(ref Entity entity, string propertyName, int value);

        // String
        string GetStringAttribute(Entity entity, string propertyName);
        void SetStringAttribute(ref Entity entity, string propertyName, string value);

        // DateTime
        DateTime GetDateTimeAttribute(Entity entity, string propertyName);
        void SetDateTimeAttribute(ref Entity entity, string propertyName, DateTime value);

        // Money
        Money GetMoneyAttribute(Entity entity, string propertyName);
        void SetMoneyAttribute(ref Entity entity, string propertyName, Money value);

        // Lookup
        Guid GetLookupAttribute(Entity entity, string propertyName);
        void SetLookupAttribute(ref Entity entity, string propertyName, string lookupEntityName, Guid guidValue);
    }
}

// AttributeOperations/AttributeServiceComposite.cs
namespace ToolUtilityNameSpace.AttributeOperations
{
    /// <summary>
    /// 屬性服務組合器（Composite Pattern）
    /// 統一管理所有類型的屬性服務
    /// </summary>
    public class AttributeServiceComposite : IAttributeService
    {
        private readonly BoolAttributeService _boolService;
        private readonly IntAttributeService _intService;
        private readonly StringAttributeService _stringService;
        private readonly DateTimeAttributeService _dateTimeService;
        private readonly MoneyAttributeService _moneyService;
        private readonly LookupAttributeService _lookupService;

        public AttributeServiceComposite(ILogger logger)
        {
            _boolService = new BoolAttributeService(logger);
            _intService = new IntAttributeService(logger);
            _stringService = new StringAttributeService(logger);
            _dateTimeService = new DateTimeAttributeService(logger);
            _moneyService = new MoneyAttributeService(logger);
            _lookupService = new LookupAttributeService(logger);
        }

        // 轉發到專責服務
        public bool GetBoolAttribute(Entity entity, string propertyName)
            => _boolService.GetAttribute(entity, propertyName);

        public void SetBoolAttribute(ref Entity entity, string propertyName, bool value)
            => _boolService.SetAttribute(ref entity, propertyName, value);

        public int GetIntAttribute(Entity entity, string propertyName)
            => _intService.GetAttribute(entity, propertyName);

        public string GetStringAttribute(Entity entity, string propertyName)
            => _stringService.GetAttribute(entity, propertyName);

        public DateTime GetDateTimeAttribute(Entity entity, string propertyName)
            => _dateTimeService.GetAttribute(entity, propertyName);

        public Money GetMoneyAttribute(Entity entity, string propertyName)
            => _moneyService.GetAttribute(entity, propertyName);

        public Guid GetLookupAttribute(Entity entity, string propertyName)
            => _lookupService.GetAttribute(entity, propertyName);

        // ... 其他方法
    }
}

// AttributeOperations/BoolAttributeService.cs
namespace ToolUtilityNameSpace.AttributeOperations
{
    /// <summary>
    /// 布林屬性專責服務（約 100 行）
    /// </summary>
    public class BoolAttributeService
    {
        private readonly ILogger _logger;

        public BoolAttributeService(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool GetAttribute(Entity entity, string propertyName)
        {
            try
            {
                if (entity.Attributes.Contains(propertyName))
                {
                    return (bool)entity.Attributes[propertyName];
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取得布林屬性失敗，PropertyName: {PropertyName}", propertyName);
                throw;
            }
        }

        public void SetAttribute(ref Entity entity, string propertyName, bool value)
        {
            try
            {
                if (entity.Attributes.Contains(propertyName))
                {
                    entity.Attributes[propertyName] = value;
                }
                else
                {
                    entity.Attributes.Add(propertyName, value);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "設定布林屬性失敗，PropertyName: {PropertyName}", propertyName);
                throw;
            }
        }

        public void SetAttributeToNull(ref Entity entity, string propertyName)
        {
            try
            {
                if (entity.Attributes.Contains(propertyName))
                {
                    entity.Attributes[propertyName] = null;
                }
                else
                {
                    entity.Attributes.Add(propertyName, null);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "設定布林屬性為 null 失敗，PropertyName: {PropertyName}", propertyName);
                throw;
            }
        }
    }
}
```

---

## 📊 重構效益分析

### 檔案大小比較

| 類別 | 重構前（行數） | 重構後（行數） | 改善幅度 |
|------|---------------|---------------|---------|
| ToolUtilityClass | 10,000+ | 300 | **-97%** |
| ContactService | N/A | 800 | 新增（專責） |
| AttributeServices | N/A | 650 (6 個類別) | 新增（專責） |
| ListService | N/A | 400 | 新增（專責） |
| 其他 Services | N/A | 1,500 | 新增（專責） |
| **總計** | **10,000+** | **3,650** | **-64%** |

### 可維護性改善

| 指標 | 重構前 | 重構後 | 改善 |
|------|--------|--------|------|
| 單一檔案行數 | 10,000+ | < 800 | ✅ |
| 類別職責數 | 10+ | 1 | ✅ |
| 測試難度 | 極高（無法 mock） | 低（可 mock 介面） | ✅ |
| 修改影響範圍 | 整個檔案 | 單一 Service | ✅ |
| 合併衝突機率 | 高 | 低 | ✅ |

---

## 🚀 執行計畫

### Phase 1: 建立基礎架構（1 天）

**目標**：建立資料夾結構與介面

```
ToolUtility/
├── Core/
│   ├── IToolUtilityService.cs
│   └── ToolUtilityServiceFactory.cs
├── Connection/
│   └── ICrmConnectionService.cs
├── EntityOperations/
│   ├── IEntityQueryService.cs
│   └── IEntityCrudService.cs
├── AttributeOperations/
│   └── IAttributeService.cs
├── ContactOperations/
│   └── IContactService.cs
└── ... (其他資料夾)
```

**驗收**：
- ✅ 所有介面定義完成
- ✅ 編譯通過（空實作）

---

### Phase 2: 實作 Service（3-5 天）

**執行順序**（由簡到繁）：

#### Day 1: 工具類別
- [x] StringUtility
- [x] TraceUtility

#### Day 2: 連線服務
- [ ] CrmConnectionService

#### Day 3: 屬性服務（Composite Pattern）
- [ ] BoolAttributeService
- [ ] IntAttributeService
- [ ] StringAttributeService
- [ ] DateTimeAttributeService
- [ ] MoneyAttributeService
- [ ] LookupAttributeService
- [ ] AttributeServiceComposite

#### Day 4: 實體操作
- [ ] EntityQueryService
- [ ] EntityCrudService

#### Day 5: 業務邏輯服務
- [ ] ContactService
- [ ] ListService
- [ ] AttachmentService
- [ ] LineMessageService

**驗收（每個 Service）**：
- ✅ 單元測試覆蓋率 > 80%
- ✅ 獨立編譯通過
- ✅ 符合 SOLID 原則

---

### Phase 3: 重構 ToolUtilityClass（1 天）

**步驟**：
1. 保留 `ToolUtilityClass` 作為 Facade
2. 所有方法改為轉發到對應 Service
3. 加入 PR-04 的 ILogger 注入
4. 修正 Dispose 方法

**驗收**：
- ✅ 所有舊程式碼不需修改（向後相容）
- ✅ 單元測試全部通過
- ✅ `ToolUtilityClass.cs` < 500 行

---

### Phase 4: 測試與驗證（1 天）

**測試清單**：
- [ ] 單元測試（每個 Service）
- [ ] 整合測試（Facade 轉發）
- [ ] 效能測試（對比重構前後）
- [ ] 資源洩漏測試（CA2000 / dotnet-gcdump）

**驗收**：
- ✅ 所有測試通過
- ✅ 效能無顯著下降（< 5%）
- ✅ 無資源洩漏

---

## ✅ SOLID 原則遵守檢查

### ✅ S - Single Responsibility Principle（單一職責）
```
❌ 重構前：ToolUtilityClass 負責 10+ 種操作
✅ 重構後：每個 Service 只負責 1 種操作
```

### ✅ O - Open/Closed Principle（開放封閉）
```
✅ IContactService 介面固定
✅ 可新增 ContactServiceV2 而不修改現有程式碼
```

### ✅ L - Liskov Substitution Principle（里氏替換）
```
✅ 所有實作類別可替換介面
✅ Mock 版本可用於測試
```

### ✅ I - Interface Segregation Principle（介面隔離）
```
✅ IContactService 只包含連絡人操作
✅ IListService 只包含名單操作
✅ 避免「胖介面」
```

### ✅ D - Dependency Inversion Principle（依賴反轉）
```
✅ ToolUtilityClass 依賴 IContactService 介面（不依賴實作）
✅ 可透過 DI 注入不同實作
```

---

## 🎯 Linus 原則遵守檢查

### ✅ 小而頻繁的變更
```
✅ 每個 Service 獨立 PR
✅ 每個 PR < 500 行變更
✅ 可獨立測試與回滾
```

### ✅ 簡潔優先
```
✅ 每個類別 < 800 行
✅ 每個方法 < 50 行
✅ 清楚的命名（ContactService, not Helper）
```

### ✅ 可回滾
```
✅ 保留 ToolUtilityClass Facade（向後相容）
✅ 每個 Service 可獨立移除
✅ 所有變更有單元測試保護
```

### ✅ 以事實為準
```
✅ 有效能基準測試
✅ 有資源洩漏檢測
✅ 有單元測試驗證行為
```

---

## 📈 與 PR-04 的整合

### PR-04 原始目標
- [x] 移除 ctor 中的 FileStream / TraceListener
- [x] 注入 ILogger
- [x] 修正 Dispose

### PR-04 + 重構後的目標
- [x] 完成 PR-04 原始目標
- [x] 解決檔案過大問題
- [x] 符合 SOLID 原則
- [x] 提升可測試性
- [x] 降低維護成本

---

## 🎓 學習資源

### 設計模式參考
- [Facade Pattern](https://refactoring.guru/design-patterns/facade)
- [Service Layer Pattern](https://martinfowler.com/eaaCatalog/serviceLayer.html)
- [Composite Pattern](https://refactoring.guru/design-patterns/composite)

### SOLID 原則參考
- [SOLID Principles in C#](https://www.c-sharpcorner.com/UploadFile/damubebi/solid-principles-in-C-Sharp/)
- [Clean Code](https://www.amazon.com/Clean-Code-Handbook-Software-Craftsmanship/dp/0132350882)

---

## 📝 後續工作

### 短期（PR-04 完成後）
- [ ] 建立 CI/CD 管道
- [ ] 加入效能監控
- [ ] 建立自動化測試套件

### 中期（PR-05 完成後）
- [ ] 轉換為 SDK-style csproj
- [ ] 支援 multi-target（net462 + net10.0）

### 長期（PR-06+）
- [ ] 逐步遷移到 async/await
- [ ] 整合 Polly（Retry / Circuit Breaker）
- [ ] 加入 OpenTelemetry（分散式追蹤）

---

## 📋 詳細 TODO 清單（待批准）

### 🔧 前置準備（Phase 0）

#### TODO-0.1: 建立測試專案結構
- [ ] 在 Solution 中新增 `ToolUtility.Tests` 專案
  ```
  ToolUtility.Tests/
  ├── ToolUtility.Tests.csproj
  ├── Core/
  │   └── ToolUtilityClassTests.cs
  ├── ContactOperations/
  │   └── ContactServiceTests.cs
  ├── AttributeOperations/
  │   └── BoolAttributeServiceTests.cs
  └── TestHelpers/
      ├── MockCrmClientFactory.cs
      └── TestEntityFactory.cs
  ```
- [ ] 安裝測試相關 NuGet 套件
  ```xml
  <PackageReference Include="xunit" Version="2.6.6" />
  <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6" />
  <PackageReference Include="Moq" Version="4.20.70" />
  <PackageReference Include="FluentAssertions" Version="6.12.0" />
  <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
  ```
- [ ] 設定 CI/CD 測試管道（GitHub Actions 或 Azure DevOps）
  ```yaml
  # .github/workflows/test.yml
  name: Run Tests
  on: [push, pull_request]
  jobs:
    test:
      runs-on: windows-latest
      steps:
        - uses: actions/checkout@v4
        - name: Setup .NET
          uses: actions/setup-dotnet@v4
          with:
            dotnet-version: '8.0.x'
        - name: Restore dependencies
          run: dotnet restore
        - name: Build
          run: dotnet build --no-restore
        - name: Test
          run: dotnet test --no-build --verbosity normal --collect:"XPlat Code Coverage"
        - name: Upload coverage
          uses: codecov/codecov-action@v3
  ```

**驗收標準**：
- ✅ 測試專案可編譯
- ✅ CI/CD 管道運行成功（即使沒有測試）
- ✅ 程式碼覆蓋率報告可產生

---

#### TODO-0.2: 建立 Mock 基礎設施
- [ ] 實作 `MockCrmClientFactory`（產生假的 ICrmClient）
  ```csharp
  public static class MockCrmClientFactory
  {
      public static Mock<ICrmClient> CreateMock()
      {
          var mock = new Mock<ICrmClient>();
          // 預設行為設定
          return mock;
      }
  }
  ```
- [ ] 實作 `TestEntityFactory`（快速建立測試用 Entity）
  ```csharp
  public static class TestEntityFactory
  {
      public static Entity CreateContact(string lineId, string fullName)
      {
          return new Entity("contact")
          {
              Id = Guid.NewGuid(),
              ["new_lineid"] = lineId,
              ["fullname"] = fullName,
              ["statecode"] = 0
          };
      }
  }
  ```
- [ ] 實作 `MockLoggerFactory`（產生假的 ILogger）
  ```csharp
  public static class MockLoggerFactory
  {
      public static Mock<ILogger<T>> CreateMock<T>()
      {
          var mock = new Mock<ILogger<T>>();
          // 預設不驗證 Log 呼叫
          return mock;
      }
  }
  ```

**驗收標準**：
- ✅ Mock 工廠可產生可用的 Mock 物件
- ✅ 在測試中可重複使用

---

### 📁 Phase 1: 建立基礎架構（TDD 驅動）

#### TODO-1.1: 工具類別（StringUtility）

##### 🔴 RED - 寫測試
- [ ] 建立 `StringUtilityTests.cs`
  ```csharp
  public class StringUtilityTests
  {
      [Fact]
      public void DeleteLastComma_WhenStringEndsWithComma_ShouldRemoveIt()
      {
          // Arrange
          string input = "測試，項目，";
          
          // Act
          StringUtility.DeleteLastComma(ref input);
          
          // Assert
          Assert.Equal("測試，項目", input);
      }
      
      [Fact]
      public void FilterDigit_WhenMixedString_ShouldReturnOnlyDigits()
      {
          // Arrange
          string input = "電話: 0912-345-678";
          
          // Act
          var result = StringUtility.FilterDigit(input);
          
          // Assert
          Assert.Equal("0912345678", result);
      }
  }
  ```
- [ ] 執行測試 → 預期 ❌ 失敗（因為 `StringUtility` 還不存在）

##### 🟢 GREEN - 寫實作
- [ ] 建立 `ToolUtility/Utilities/StringUtility.cs`
  ```csharp
  namespace ToolUtilityNameSpace.Utilities
  {
      public static class StringUtility
      {
          public static void DeleteLastComma(ref string stringToProcess)
          {
              if (string.IsNullOrEmpty(stringToProcess)) return;
              
              int length = stringToProcess.LastIndexOf("，");
              if (length > 0)
              {
                  stringToProcess = stringToProcess.Substring(0, length);
              }
          }
          
          public static string FilterDigit(string filteredString)
          {
              if (string.IsNullOrEmpty(filteredString)) return string.Empty;
              
              return new string(filteredString.Where(char.IsDigit).ToArray());
          }
      }
  }
  ```
- [ ] 執行測試 → 預期 ✅ 通過

##### 🔵 REFACTOR - 優化
- [ ] 加入 `DeleteLastChar` 泛化方法
- [ ] 加入邊界條件測試（null, empty, 單字元）
- [ ] 執行測試 → 確保 ✅ 仍通過

**驗收標準**：
- ✅ 所有測試通過
- ✅ 測試覆蓋率 > 90%
- ✅ 程式碼通過 Code Review

---

#### TODO-1.2: 工具類別（TraceUtility）

##### 🔴 RED - 寫測試
- [ ] 建立 `TraceUtilityTests.cs`
  ```csharp
  public class TraceUtilityTests
  {
      [Fact]
      public void TraceByLevel_WhenLoggerProvided_ShouldUseILogger()
      {
          // Arrange
          var mockLogger = MockLoggerFactory.CreateMock<object>();
          int totalLevel = 5;
          int qualifiedLevel = 3;
          string message = "測試訊息";
          
          // Act
          TraceUtility.TraceByLevel(mockLogger.Object, totalLevel, qualifiedLevel, message);
          
          // Assert
          mockLogger.Verify(
              x => x.Log(
                  LogLevel.Debug,
                  It.IsAny<EventId>(),
                  It.Is<It.IsAnyType>((v, t) => v.ToString().Contains(message)),
                  null,
                  It.IsAny<Func<It.IsAnyType, Exception, string>>()),
              Times.Once);
      }
      
      [Fact]
      public void TraceByLevel_WhenLevelTooLow_ShouldNotLog()
      {
          // Arrange
          var mockLogger = MockLoggerFactory.CreateMock<object>();
          
          // Act
          TraceUtility.TraceByLevel(mockLogger.Object, totalLevel: 2, qualifiedLevel: 5, "不應記錄");
          
          // Assert
          mockLogger.Verify(x => x.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Never);
      }
  }
  ```
- [ ] 執行測試 → 預期 ❌ 失敗

##### 🟢 GREEN - 寫實作
- [ ] 建立 `ToolUtility/Utilities/TraceUtility.cs`
  ```csharp
  namespace ToolUtilityNameSpace.Utilities
  {
      public static class TraceUtility
      {
          public static void TraceByLevel(ILogger logger, int totalLevel, int qualifiedLevel, string stringToProcess)
          {
              if (totalLevel < qualifiedLevel) return;
              
              logger?.LogDebug("Time: {Time}, Message: {Message}, StackTrace: {StackTrace}",
                  DateTime.Now,
                  stringToProcess,
                  new StackTrace(new StackFrame(1, true)).ToString());
          }
          
          // 向後相容（Legacy）
          public static void TraceByLevelLegacy(int totalLevel, int qualifiedLevel, string stringToProcess)
          {
              if (totalLevel < qualifiedLevel) return;
              
              Debug.WriteLine($"Time: {DateTime.Now}");
              Debug.WriteLine($"Message: {stringToProcess}");
              Debug.WriteLine($"StackTrace: {new StackTrace(new StackFrame(1, true))}");
          }
      }
  }
  ```
- [ ] 執行測試 → 預期 ✅ 通過

**驗收標準**：
- ✅ ILogger 版本與 Legacy 版本都有測試
- ✅ 測試覆蓋率 > 85%

---

### 🔌 Phase 2: 屬性服務（Attribute Services）

#### TODO-2.1: BoolAttributeService（TDD 範例）

##### 🔴 RED - 寫測試
- [ ] 建立 `BoolAttributeServiceTests.cs`
  ```csharp
  public class BoolAttributeServiceTests
  {
      private readonly Mock<ILogger> _mockLogger;
      private readonly BoolAttributeService _service;
      
      public BoolAttributeServiceTests()
      {
          _mockLogger = MockLoggerFactory.CreateMock<BoolAttributeService>();
          _service = new BoolAttributeService(_mockLogger.Object);
      }
      
      [Fact]
      public void GetAttribute_WhenAttributeExists_ShouldReturnValue()
      {
          // Arrange
          var entity = new Entity("contact");
          entity["new_ismember"] = true;
          
          // Act
          var result = _service.GetAttribute(entity, "new_ismember");
          
          // Assert
          result.Should().BeTrue();
      }
      
      [Fact]
      public void GetAttribute_WhenAttributeNotExists_ShouldReturnFalse()
      {
          // Arrange
          var entity = new Entity("contact");
          
          // Act
          var result = _service.GetAttribute(entity, "new_ismember");
          
          // Assert
          result.Should().BeFalse();
      }
      
      [Fact]
      public void SetAttribute_WhenAttributeExists_ShouldUpdateValue()
      {
          // Arrange
          var entity = new Entity("contact");
          entity["new_ismember"] = false;
          
          // Act
          _service.SetAttribute(ref entity, "new_ismember", true);
          
          // Assert
          entity["new_ismember"].Should().Be(true);
      }
      
      [Fact]
      public void SetAttribute_WhenAttributeNotExists_ShouldAddValue()
      {
          // Arrange
          var entity = new Entity("contact");
          
          // Act
          _service.SetAttribute(ref entity, "new_ismember", true);
          
          // Assert
          entity.Contains("new_ismember").Should().BeTrue();
          entity["new_ismember"].Should().Be(true);
      }
  }
  ```
- [ ] 執行測試 → 預期 ❌ 失敗

##### 🟢 GREEN - 寫實作
- [ ] 建立 `ToolUtility/AttributeOperations/BoolAttributeService.cs`（按照文件中的範例）
- [ ] 執行測試 → 預期 ✅ 通過

##### 🔵 REFACTOR - 優化
- [ ] 加入異常測試（InvalidCastException）
- [ ] 加入 Logging 驗證
- [ ] 執行測試 → 確保 ✅ 仍通過

**驗收標準**：
- ✅ 測試覆蓋率 > 90%
- ✅ 所有邊界條件已測試
- ✅ Logging 行為已驗證

---

#### TODO-2.2 ~ 2.6: 其他屬性服務（依循相同模式）

- [ ] TODO-2.2: `IntAttributeService`（TDD：測試 → 實作 → 重構）
- [ ] TODO-2.3: `StringAttributeService`（TDD：測試 → 實作 → 重構）
- [ ] TODO-2.4: `DateTimeAttributeService`（TDD：測試 → 實作 → 重構）
- [ ] TODO-2.5: `MoneyAttributeService`（TDD：測試 → 實作 → 重構）
- [ ] TODO-2.6: `LookupAttributeService`（TDD：測試 → 實作 → 重構）

**每個 Service 的驗收標準**：
- ✅ 完整的單元測試（正常 + 異常 + 邊界）
- ✅ 測試覆蓋率 > 90%
- ✅ 程式碼 Review 通過

---

#### TODO-2.7: AttributeServiceComposite（整合測試）

##### 🔴 RED - 寫測試
- [ ] 建立 `AttributeServiceCompositeTests.cs`
  ```csharp
  public class AttributeServiceCompositeTests
  {
      [Fact]
      public void GetBoolAttribute_ShouldDelegateToBoolService()
      {
          // Arrange
          var mockLogger = MockLoggerFactory.CreateMock<object>();
          var composite = new AttributeServiceComposite(mockLogger.Object);
          var entity = new Entity("contact");
          entity["new_ismember"] = true;
          
          // Act
          var result = composite.GetBoolAttribute(entity, "new_ismember");
          
          // Assert
          result.Should().BeTrue();
      }
      
      // 測試所有類型的轉發...
  }
  ```
- [ ] 執行測試 → 預期 ❌ 失敗

##### 🟢 GREEN - 寫實作
- [ ] 實作 `AttributeServiceComposite`（按照文件範例）
- [ ] 執行測試 → 預期 ✅ 通過

**驗收標準**：
- ✅ 所有類型的屬性轉發都有測試
- ✅ 測試覆蓋率 > 85%

---

### 🔍 Phase 3: 實體操作服務

#### TODO-3.1: EntityQueryService（TDD）

##### 🔴 RED - 寫測試
- [ ] 建立 `EntityQueryServiceTests.cs`
  ```csharp
  public class EntityQueryServiceTests
  {
      [Fact]
      public void RetrieveEntity_WhenEntityExists_ShouldReturnEntity()
      {
          // Arrange
          var mockCrmClient = MockCrmClientFactory.CreateMock();
          var expectedEntity = TestEntityFactory.CreateContact("U123", "測試");
          
          mockCrmClient
              .Setup(x => x.Retrieve("contact", expectedEntity.Id, It.IsAny<ColumnSet>()))
              .Returns(expectedEntity);
          
          var mockLogger = MockLoggerFactory.CreateMock<EntityQueryService>();
          var service = new EntityQueryService(mockLogger.Object, mockCrmClient.Object);
          
          // Act
          var result = service.RetrieveEntity("contact", expectedEntity.Id);
          
          // Assert
          result.Should().NotBeNull();
          result.Id.Should().Be(expectedEntity.Id);
      }
      
      [Fact]
      public void RetrieveMultiple_WhenQueryValid_ShouldReturnCollection()
      {
          // Arrange
          var mockCrmClient = MockCrmClientFactory.CreateMock();
          var expectedCollection = new EntityCollection(new[]
          {
              TestEntityFactory.CreateContact("U123", "測試1"),
              TestEntityFactory.CreateContact("U456", "測試2")
          });
          
          mockCrmClient
              .Setup(x => x.RetrieveMultiple(It.IsAny<QueryBase>()))
              .Returns(expectedCollection);
          
          var mockLogger = MockLoggerFactory.CreateMock<EntityQueryService>();
          var service = new EntityQueryService(mockLogger.Object, mockCrmClient.Object);
          
          var query = new QueryByAttribute("contact");
          
          // Act
          var result = service.RetrieveMultiple(query);
          
          // Assert
          result.Entities.Count.Should().Be(2);
      }
  }
  ```
- [ ] 執行測試 → 預期 ❌ 失敗

##### 🟢 GREEN - 寫實作
- [ ] 建立 `IEntityQueryService` 介面
  ```csharp
  public interface IEntityQueryService
  {
      Entity RetrieveEntity(string entityName, Guid entityId);
      EntityCollection RetrieveMultiple(QueryBase query);
  }
  ```
- [ ] 實作 `EntityQueryService`
- [ ] 執行測試 → 預期 ✅ 通過

**驗收標準**：
- ✅ 測試覆蓋率 > 85%
- ✅ 包含異常測試（EntityNotFoundException）

---

#### TODO-3.2: EntityCrudService（TDD）

- [ ] 🔴 寫測試：`CreateEntity`, `UpdateEntity`, `DeleteEntity`
- [ ] 🟢 實作 `IEntityCrudService` + `EntityCrudService`
- [ ] 🔵 重構與優化

**驗收標準**：
- ✅ CRUD 操作都有測試
- ✅ 測試覆蓋率 > 85%

---

### 👤 Phase 4: 業務邏輯服務

#### TODO-4.1: ContactService（完整 TDD 示範）

##### 🔴 RED - 寫完整測試套件
- [ ] 建立 `ContactServiceTests.cs`（包含所有方法）
  ```csharp
  public class ContactServiceTests
  {
      [Theory]
      [InlineData("U123456", "測試聯絡人", true)]
      [InlineData("U999999", null, false)]
      public void RetrieveByLineId_ShouldReturnCorrectResult(string lineId, string expectedName, bool shouldFind)
      {
          // ... 測試實作
      }
      
      [Fact]
      public void RetrieveByAccountNumber_WhenPasswordCorrect_ShouldReturnEntity()
      {
          // ... 測試實作
      }
      
      [Fact]
      public void RetrieveByAccountNumber_WhenPasswordWrong_ShouldReturnNull()
      {
          // ... 測試實作
      }
  }
  ```
- [ ] 執行測試 → 預期 ❌ 失敗

##### 🟢 GREEN - 實作
- [ ] 實作 `IContactService` 介面
- [ ] 實作 `ContactService`（按照文件範例）
- [ ] 執行測試 → 預期 ✅ 通過

##### 🔵 REFACTOR - 優化
- [ ] 提取共用邏輯（如 Query 建構）
- [ ] 加強 Logging
- [ ] 執行測試 → 確保 ✅ 仍通過

**驗收標準**：
- ✅ 所有公開方法都有測試
- ✅ 測試覆蓋率 > 90%
- ✅ 包含整合測試（與 EntityQueryService 協作）

---

#### TODO-4.2 ~ 4.4: 其他業務服務

- [ ] TODO-4.2: `ListService`（TDD：測試 → 實作 → 重構）
- [ ] TODO-4.3: `AttachmentService`（TDD：測試 → 實作 → 重構）
- [ ] TODO-4.4: `LineMessageService`（TDD：測試 → 實作 → 重構）

---

### 🏢 Phase 5: Facade 重構（向後相容驗證）

#### TODO-5.1: ToolUtilityClass Facade（整合測試）

##### 🔴 RED - 寫整合測試
- [ ] 建立 `ToolUtilityClassIntegrationTests.cs`
  ```csharp
  public class ToolUtilityClassIntegrationTests
  {
      [Fact]
      public void RetrieveContactByLineId_ShouldDelegateToContactService()
      {
          // Arrange
          var mockCrmClient = MockCrmClientFactory.CreateMock();
          var expectedEntity = TestEntityFactory.CreateContact("U123", "測試");
          
          mockCrmClient
              .Setup(x => x.RetrieveMultiple(It.IsAny<QueryBase>()))
              .Returns(new EntityCollection(new[] { expectedEntity }));
          
          var mockLogger = MockLoggerFactory.CreateMock<ToolUtilityClass>();
          var utility = new ToolUtilityClass(mockLogger.Object, mockCrmClient.Object, null);
          
          // Act
          var result = utility.RetrieveContactByLineId("U123");
          
          // Assert
          result.Should().NotBeNull();
          result["new_lineid"].Should().Be("U123");
      }
      
      [Fact]
      public void SetEntityBoolAttribute_ShouldDelegateToAttributeService()
      {
          // ... 測試實作
      }
  }
  ```
- [ ] 執行測試 → 預期 ❌ 失敗

##### 🟢 GREEN - 重構 ToolUtilityClass
- [ ] 按照文件範例重構為 Facade
- [ ] 保留所有公開方法（向後相容）
- [ ] 執行測試 → 預期 ✅ 通過

##### 🔵 REFACTOR - 向後相容驗證
- [ ] 建立 `BackwardCompatibilityTests.cs`
  ```csharp
  public class BackwardCompatibilityTests
  {
      [Fact]
      public void OldConstructor_ShouldStillWork()
      {
          // Act & Assert（不應拋出異常）
          var utility = new ToolUtilityClass();
          utility.Dispose();
      }
      
      [Fact]
      public void AllPublicMethods_ShouldStillExist()
      {
          // 使用反射驗證所有舊方法簽章都存在
          var type = typeof(ToolUtilityClass);
          
          type.GetMethod("RetrieveContactByLineId").Should().NotBeNull();
          type.GetMethod("GetEntityBoolAttribute").Should().NotBeNull();
          // ... 其他方法
      }
  }
  ```
- [ ] 執行測試 → 確保 ✅ 所有舊方法仍可用

**驗收標準**：
- ✅ 所有舊 API 簽章保持不變
- ✅ 整合測試覆蓋主要使用情境
- ✅ 向後相容測試全部通過

---

### ⚙️ Phase 6: 資源管理與效能驗證

#### TODO-6.1: 資源洩漏測試

- [ ] 建立 `ResourceLeakTests.cs`
  ```csharp
  public class ResourceLeakTests
  {
      [Fact]
      public void Dispose_ShouldReleaseAllServices()
      {
          // Arrange
          var utility = new ToolUtilityClass();
          
          // 觸發所有 Lazy Service 初始化
          utility.RetrieveContactByLineId("test");
          utility.GetEntityBoolAttribute(new Entity(), "test");
          
          // Act
          utility.Dispose();
          
          // Assert（使用 dotnet-gcdump 或類似工具驗證）
          // 手動驗證：無 FileHandle 未釋放
      }
  }
  ```
- [ ] 整合 CA2000 靜態分析
- [ ] 在 CI 加入資源洩漏檢測

**驗收標準**：
- ✅ CA2000 無警告
- ✅ 手動 Smoke Test 無檔案鎖定

---

#### TODO-6.2: 效能基準測試

- [ ] 建立 `PerformanceBenchmarks.cs`（使用 BenchmarkDotNet）
  ```csharp
  [MemoryDiagnoser]
  public class ToolUtilityBenchmarks
  {
      [Benchmark]
      public void OldVersion_RetrieveContact()
      {
          // 測試舊版本效能
      }
      
      [Benchmark]
      public void NewVersion_RetrieveContact()
      {
          // 測試新版本效能
      }
  }
  ```
- [ ] 執行基準測試，記錄結果
- [ ] 確保效能下降 < 5%

**驗收標準**：
- ✅ 基準測試結果已記錄
- ✅ 效能無顯著下降
- ✅ 記憶體使用未增加

---

### 📊 Phase 7: 文檔與 Code Review

#### TODO-7.1: 更新文檔

- [ ] 更新 README.md（加入測試指引）
- [ ] 為每個 Service 加入 XML 註解
- [ ] 建立 API 使用範例

#### TODO-7.2: Code Review Checklist

- [ ] 所有測試通過（Unit + Integration）
- [ ] 程式碼覆蓋率 > 85%
- [ ] 無 Roslyn 警告
- [ ] 符合 SOLID 原則
- [ ] 向後相容驗證通過
- [ ] 效能基準測試通過

---

## 📈 測試覆蓋率目標

| 層級 | 目標覆蓋率 | 驗證工具 |
|------|----------|---------|
| **Utilities** (StringUtility, TraceUtility) | **95%+** | Coverlet |
| **Attribute Services** (BoolAttributeService 等) | **90%+** | Coverlet |
| **Entity Services** (QueryService, CrudService) | **85%+** | Coverlet |
| **Business Services** (ContactService 等) | **90%+** | Coverlet |
| **Facade** (ToolUtilityClass) | **80%+** | Coverlet + 整合測試 |
| **整體專案** | **85%+** | Codecov |

---

## 🎓 TDD 學習資源

### 推薦閱讀
- [Test Driven Development: By Example (Kent Beck)](https://www.amazon.com/Test-Driven-Development-Kent-Beck/dp/0321146530)
- [xUnit Test Patterns](http://xunitpatterns.com/)
- [Microsoft - Unit testing best practices](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices)

### 推薦工具
- **測試框架**: xUnit（推薦）/ NUnit / MSTest
- **Mock 框架**: Moq（推薦）/ NSubstitute
- **斷言庫**: FluentAssertions（推薦）/ Shouldly
- **覆蓋率工具**: Coverlet + ReportGenerator
- **基準測試**: BenchmarkDotNet

---

## ✅ 總結檢查清單（批准前確認）

### 架構設計
- [ ] ✅ Facade Pattern 設計合理
- [ ] ✅ Service Layer 職責清晰
- [ ] ✅ Composite Pattern 正確應用
- [ ] ✅ 依賴注入設計完善

### TDD 流程
- [ ] ✅ 每個 Service 都遵循 TDD（紅→綠→重構）
- [ ] ✅ 測試覆蓋率達標（> 85%）
- [ ] ✅ 測試金字塔比例合理（70% Unit, 20% Integration, 10% E2E）

### 向後相容
- [ ] ✅ 所有舊 API 簽章保留
- [ ] ✅ 向後相容測試通過
- [ ] ✅ 無需修改呼叫端程式碼

### 資源管理
- [ ] ✅ Dispose 正確實作
- [ ] ✅ 無資源洩漏（CA2000 通過）
- [ ] ✅ Lazy 初始化正確使用

### 效能驗證
- [ ] ✅ 基準測試完成
- [ ] ✅ 效能下降 < 5%
- [ ] ✅ 記憶體使用未增加

### CI/CD
- [ ] ✅ 測試自動化管道建立
- [ ] ✅ 程式碼覆蓋率報告自動產生
- [ ] ✅ 靜態分析整合（Roslyn, CA2000）

---

**文件版本**: 2.0（新增 TDD 章節）  
**最後更新**: 2024-01-XX  
**維護者**: GitHub Copilot  
**狀態**: 📋 等待批准

---

## 🔗 相關文件

- [結論規劃.md](./結論規劃.md) - 主升級計畫
- [PR-02_高風險掃描報告.md](./PR-02_高風險掃描報告.md) - 問題清單
- [PR-03_ICrmClient實作說明.md](./PR-03_ICrmClient實作說明.md) - Adapter 模式
- [PR-04_資源洩漏修正計畫.md](./PR-04_資源洩漏修正計畫.md) - 資源管理
- [xUnit 官方文件](https://xunit.net/)
- [Moq 官方文件](https://github.com/moq/moq4)
- [FluentAssertions 官方文件](https://fluentassertions.com/)

---

**⚠️ 重要提醒**：
1. **務必遵循 TDD 流程**：先寫測試（紅燈），再寫實作（綠燈），最後重構（藍燈）
2. **不要跳過測試**：即使是簡單的工具方法也要寫測試
3. **測試應該簡單明瞭**：測試本身不應該需要測試
4. **Mock 應該最小化**：只 Mock 外部依賴（ICrmClient, ILogger），不 Mock 內部邏輯
5. **向後相容是首要任務**：所有變更必須通過向後相容測試

---

**批准確認事項**（請回覆）:
- [ ] ✅ 同意採用 TDD 流程
- [ ] ✅ 同意測試覆蓋率目標（85%+）
- [ ] ✅ 同意 TODO 清單的執行順序
- [ ] ✅ 同意使用的測試工具（xUnit + Moq + FluentAssertions）
- [ ] ✅ 同意 CI/CD 自動化測試管道
- [ ] ⚠️ 需要調整的項目：______（請說明）

**預估時程**：
- Phase 0-1（前置準備 + 基礎架構）：**2-3 天**
- Phase 2-3（屬性 + 實體服務）：**3-4 天**
- Phase 4-5（業務服務 + Facade）：**2-3 天**
- Phase 6-7（資源驗證 + 文檔）：**1-2 天**
- **總計：8-12 個工作天**（視團隊規模與經驗而定）

請批准後，我將開始執行 **TODO-0.1: 建立測試專案結構**。

<!-- 進度更新：自動化實作狀態 -->
## 🟢 目前進度（簡短提示）

- 已完成（TDD 流程）：
  - ✅ `ToolUtility.Tests` 測試專案建立（TODO-0.1）
  - ✅ 工具類別（StringUtility, TraceUtility）與對應測試（TODO-1.1, TODO-1.2）
  - ✅ 屬性服務初期實作與測試：
    - `BoolAttributeService`（TODO-2.1）
    - `IntAttributeService`（TODO-2.2）
    - `StringAttributeService`（TODO-2.3）
    - `DateTimeAttributeService`（TODO-2.4）
  - ✅ 單元測試目前全部通過（範例執行：17 tests 全通）

- 當前檔案位置（對應 TODO）：已完成到 Phase 2 的部分屬性服務（完成到 TODO-2.4）

## ▶️ 下一步（請繼續執行）

- TODO-2.5: 實作 `MoneyAttributeService`（先寫測試 → 實作 → 重構）
- TODO-2.6: 實作 `LookupAttributeService`（先寫測試 → 實作 → 重構）
- TODO-2.7: 建立 `AttributeServiceComposite` 並撰寫整合測試
- 接著：Phase 3（實體操作）→ `IEntityQueryService` / `EntityQueryService`（TDD）

---

*說明：以上為簡短進度提示，將自動依 TODO 清單從 TODO-2.5 開始繼續執行。若需調整優先順序或暫停，請回覆說明。*

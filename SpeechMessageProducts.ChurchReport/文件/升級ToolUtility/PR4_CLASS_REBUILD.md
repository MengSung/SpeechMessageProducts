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
        public void CreatePushLineMessage(String userId, String subject, String message)
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

## 🔧 補充執行細節：擴充整合測試、漸進遷移與 CI 設定

下面為三項重點的可執行步驟與範例，依文件上 TODO 清單補充，便於立刻實作。

A. 擴充整合測試（補齊 Facade 的轉發路徑）

目標：整合測試覆蓋 `ToolUtilityFacade`（或 `ToolUtilityClass` 外殼）轉發的主要方法，確保 Create/Update/Delete、Attachment、List、LineMessage 等行為與既有系統一致。

要點：
- 每個測試使用 `MockCrmClientFactory` 提供的 `Mock<ICrmClient>`（或 mock IOrganizationService 行為），並使用 `TestEntityFactory` 建立範例實體。
- 使用 `MockLoggerFactory` 產生 logger mock（不必驗證 log 呼叫，除非測試例外處理路徑）。
- 整合測試應以 Facade 為 SUT（system under test）；針對 `ToolUtilityClass` 外殼寫同樣的整合測試以做向後相容驗證。

建議加入的整合測試清單（每項至少一個 happy-path）：
- CreateEntity_ShouldReturnNewGuid
- UpdateEntity_ShouldCallCrudUpdate
- DeleteEntity_ShouldCallCrudDelete
- DownloadAnAttachment_ShouldReturnEmptyCollectionOrAnnotation
- UploadAnAttachment_ShouldCreateAnnotation
- AddMembersToMarketingList_ShouldCallListService
- RemoveMembersToMarketingList_ShouldCallListService
- CreatePushLineMessage_ShouldCreateLineMessageEntity

範例測試（Create / Update / Delete）：

```csharp
[Fact]
public void Create_Update_Delete_Entity_Via_Facade()
{
    // Arrange
    var mockCrm = MockCrmClientFactory.CreateMock();
    var mockLogger = MockLoggerFactory.CreateMock<object>();

    var createdId = Guid.NewGuid();
    mockCrm.Setup(x => x.Create(It.IsAny<Entity>())).Returns(createdId);

    var facade = new ToolUtilityFacade(mockLogger.Object, mockCrm.Object);

    var entity = new Entity("account") { ["name"] = "TDD Test" };

    // Act - Create
    var id = facade.CreateEntity(entity);

    // Assert
    id.Should().Be(createdId);

    // Act - Update
    entity.Id = id;
    facade.UpdateEntity(entity);
    mockCrm.Verify(x => x.Update(It.Is<Entity>(e => e.Id == id)), Times.Once);

    // Act - Delete
    facade.DeleteEntity("account", id);
    mockCrm.Verify(x => x.Delete("account", id), Times.Once);
}
```

範例測試（Attachment Upload）：

```csharp
[Fact]
public void UploadAttachment_ShouldCallCreateAnnotation()
{
    var mockCrm = MockCrmClientFactory.CreateMock();
    var mockLogger = MockLoggerFactory.CreateMock<object>();
    var facade = new ToolUtilityFacade(mockLogger.Object, mockCrm.Object);

    var crmService = (IOrganizationService)null; // facade requires ref param signature; use null as allowed
    facade.UploadAnAttachment(ref crmService, "contact", "sub", "note", "file.txt", "text/plain", new byte[] { 1,2,3 }, Guid.NewGuid());

    mockCrm.Verify(x => x.Create(It.Is<Entity>(a => a.LogicalName == "annotation" && a["filename"].ToString() == "file.txt")), Times.Once);
}
```

備註：將上述測試放於 `ToolUtility.Tests/Core/ToolUtilityFacadeIntegrationTests.cs`，並使用已有的 `MockCrmClientFactory`、`TestEntityFactory`、`MockLoggerFactory`。

B. 漸進遷移策略（分批 PR）

目標：把舊的 `ToolUtilityClass` 逐步改為內部呼叫 `ToolUtilityFacade`，每個 PR 小且可回滾，保留舊 constructor 以確保向後相容。

策略步驟（每步為一個 PR）：
1. PR-1：新增 `ToolUtilityFacade` 類別（已完成）與介面（若尚未），並新增整合測試骨架。
   - 內容：新增檔案，不改動現有 `ToolUtilityClass`。
2. PR-2：把 `ToolUtilityClass` 改為「薄外殼」但保留所有 public 方法與 constructor。
   - 實作：`ToolUtilityClass` 的每個 public 方法直接轉發到 `ToolUtilityFacade`。
   - 標記舊無參數 ctor 為 `[Obsolete]` 並保留。
   - 新增整合測試以驗證向後相容。
3. PR-3：為每一個大型功能區（Attributes / Contact / List / Attachment / LineMessage）分別提交小 PR，將該區塊的舊實作改為呼叫對應的 Service，並補上單元 + 整合測試。
   - 每個 PR 應包含：
     - 新 Service 的單元測試（綠燈）
     - Facade 的整合測試（驗證轉發）
     - 更新的文件說明
4. PR-4：移除 `ToolUtilityClass` 中已完全轉移且有測試保護的舊內部邏輯，繼續保留外殼直到所有呼叫端改為 DI 注入。
5. PR-Final：當所有呼叫端都使用新的 Service/Facade，並且 `BackwardCompatibilityTests` 表示沒有遺漏的公開方法後，簡化或移除 `ToolUtilityClass`（視需要保留最簡化的 Facade 以兼容外部整合）。

每個 PR 的檢查清單：
- [ ] 編譯通過
- [ ] 單元測試通過（改動區域）
- [ ] 整合測試（Facade）通過
- [ ] 向後相容測試（BackwardCompatibilityTests）通過
- [ ] Performance smoke test（針對關鍵 API）通過

C. 建立 CI（GitHub Actions 範例）

目標：在 PR 與 main branch 上自動跑 unit + integration tests 並上傳覆蓋率報告。

範例 workflow（.github/workflows/test-and-coverage.yml）：

```yaml
name: Test and Coverage
on: [push, pull_request]

jobs:
  build-and-test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Restore
        run: dotnet restore

      - name: Build
        run: dotnet build --no-restore --configuration Release

      - name: Run Unit and Integration Tests
        run: |
          dotnet test ToolUtility.Tests/ToolUtility.Tests.csproj --no-build --logger:"trx;LogFileName=test_results.trx" --collect:"XPlat Code Coverage"

      - name: Publish Test Results
        if: always()
        uses: atlassian/generate-test-report@v2
        with:
          results: '**/TestResults/*.trx'

      - name: Upload code coverage to Codecov
        uses: codecov/codecov-action@v3
        with:
          files: |
            **/coverage.cobertura.xml
            **/coverage.opencover.xml
          token: ${{ secrets.CODECOV_TOKEN }}

      - name: Upload test artifacts
        if: failure()
        uses: actions/upload-artifact@v4
        with:
          name: test-results
          path: TestResults/
```

說明：
- `dotnet test` 使用 Coverlet 收集覆蓋率（測試專案已在 csproj 或命令列啟用 Coverlet）。
- `codecov` action 會上傳報告到 Codecov（請在 repo secrets 加入 `CODECOV_TOKEN`）。
- 若 CI 需要將 unit/integration tests 分開跑，可在 test command 加上 `--filter FullyQualifiedName!~Integration` 或使用 xUnit Trait（例如 Trait = "Category", "Integration"），在 CI 中以不同步驟執行 Integration tests（可選）。

D. 實作細微注意事項

- Ref/out/ref IOrganizationService 參數：舊 API 若有 `ref` 參數，Facade/Service 也應保留 `ref` 簽章，並在轉發中直接轉交給底層 Service（不要改變參考語意）。
- 例外行為一致性：若舊方法在某條件會丟出特定例外（例：找不到資料回傳 `null` vs 丟 exception），在轉發初期保持與舊行為一致，之後再評估是否可改進。
- 日誌（Logging）：在 Service 中使用 `ILogger<T>`，在 Facade 層僅作最小轉發與錯誤彙整；保持舊 log level 行為以便從 log 回溯。
- 多 target 支援：確保 `ICrmClient` adapter 在 net462 與 net10 等目標上可編譯，CI 針對主要 target 執行測試。

E. 推薦開發流程範例（PR 範例描述）

標題：PR-XX: Migrate Contact retrieval to ContactService + tests
說明：
- 新增 `ContactService` 與 `IContactService`（已包含單元測試）
- 更新 `ToolUtilityClass` `RetrieveContactByLineId` 轉發到 `ToolUtilityFacade`（或直接注入 `ContactService`）
- 新增整合測試 `ToolUtilityClassIntegrationTests.RetrieveContactByLineId_ShouldDelegateToContactService`

驗收條件（PR checklist）
- [ ] Unit tests passed
- [ ] Integration tests passed
- [ ] BackwardCompatibility tests passed
- [ ] No performance regression (smoke)

---

以上補充將直接放到 PR-04 遷移手冊中，成為逐步執行與驗證的具體流程。若要我接著：
- 實際新增整合測試檔案（Create/Update/Delete、Attachment、List、LineMessage）並執行測試；或
- 將 `ToolUtilityClass` 修改為薄外殼轉發到 `ToolUtilityFacade`（一次性或分批提交 PR）；或
- 在 repo 新增 GitHub Actions workflow 檔，並執行本地驗證（需要 secrets 設定）。

請選一項我接著替你執行。

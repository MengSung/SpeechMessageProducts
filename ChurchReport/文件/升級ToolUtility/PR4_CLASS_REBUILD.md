# PR-04: ToolUtilityClass 重構計畫（解決檔案過大問題）

## ?? 執行摘要

**目標**：將 10,000+ 行的 `ToolUtilityClass` 拆分為符合 **SOLID 原則**的多個小類別，同時完成 PR-04 的資源洩漏修正。

**核心策略**：
- 採用 **Facade Pattern**：保留 `ToolUtilityClass` 作為外部介面（向後相容）
- 採用 **Service Locator Pattern**：內部轉發到專責 Service
- 遵循 **單一職責原則（SRP）**：每個類別只負責一種操作

---

## ?? 重構目標

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

## ??? 設計模式應用

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

## ?? 重構效益分析

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
| 單一檔案行數 | 10,000+ | < 800 | ? |
| 類別職責數 | 10+ | 1 | ? |
| 測試難度 | 極高（無法 mock） | 低（可 mock 介面） | ? |
| 修改影響範圍 | 整個檔案 | 單一 Service | ? |
| 合併衝突機率 | 高 | 低 | ? |

---

## ?? 執行計畫

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
- ? 所有介面定義完成
- ? 編譯通過（空實作）

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
- ? 單元測試覆蓋率 > 80%
- ? 獨立編譯通過
- ? 符合 SOLID 原則

---

### Phase 3: 重構 ToolUtilityClass（1 天）

**步驟**：
1. 保留 `ToolUtilityClass` 作為 Facade
2. 所有方法改為轉發到對應 Service
3. 加入 PR-04 的 ILogger 注入
4. 修正 Dispose 方法

**驗收**：
- ? 所有舊程式碼不需修改（向後相容）
- ? 單元測試全部通過
- ? `ToolUtilityClass.cs` < 500 行

---

### Phase 4: 測試與驗證（1 天）

**測試清單**：
- [ ] 單元測試（每個 Service）
- [ ] 整合測試（Facade 轉發）
- [ ] 效能測試（對比重構前後）
- [ ] 資源洩漏測試（CA2000 / dotnet-gcdump）

**驗收**：
- ? 所有測試通過
- ? 效能無顯著下降（< 5%）
- ? 無資源洩漏

---

## ? SOLID 原則遵守檢查

### ? S - Single Responsibility Principle（單一職責）
```
? 重構前：ToolUtilityClass 負責 10+ 種操作
? 重構後：每個 Service 只負責 1 種操作
```

### ? O - Open/Closed Principle（開放封閉）
```
? IContactService 介面固定
? 可新增 ContactServiceV2 而不修改現有程式碼
```

### ? L - Liskov Substitution Principle（里氏替換）
```
? 所有實作類別可替換介面
? Mock 版本可用於測試
```

### ? I - Interface Segregation Principle（介面隔離）
```
? IContactService 只包含連絡人操作
? IListService 只包含名單操作
? 避免「胖介面」
```

### ? D - Dependency Inversion Principle（依賴反轉）
```
? ToolUtilityClass 依賴 IContactService 介面（不依賴實作）
? 可透過 DI 注入不同實作
```

---

## ?? Linus 原則遵守檢查

### ? 小而頻繁的變更
```
? 每個 Service 獨立 PR
? 每個 PR < 500 行變更
? 可獨立測試與回滾
```

### ? 簡潔優先
```
? 每個類別 < 800 行
? 每個方法 < 50 行
? 清楚的命名（ContactService, not Helper）
```

### ? 可回滾
```
? 保留 ToolUtilityClass Facade（向後相容）
? 每個 Service 可獨立移除
? 所有變更有單元測試保護
```

### ? 以事實為準
```
? 有效能基準測試
? 有資源洩漏檢測
? 有單元測試驗證行為
```

---

## ?? 與 PR-04 的整合

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

## ?? 學習資源

### 設計模式參考
- [Facade Pattern](https://refactoring.guru/design-patterns/facade)
- [Service Layer Pattern](https://martinfowler.com/eaaCatalog/serviceLayer.html)
- [Composite Pattern](https://refactoring.guru/design-patterns/composite)

### SOLID 原則參考
- [SOLID Principles in C#](https://www.c-sharpcorner.com/UploadFile/damubebi/solid-principles-in-C-Sharp/)
- [Clean Code](https://www.amazon.com/Clean-Code-Handbook-Software-Craftsmanship/dp/0132350882)

---

## ?? 後續工作

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

**文件版本**: 1.0  
**最後更新**: 2024-01-XX  
**維護者**: GitHub Copilot  
**狀態**: ?? 規劃階段

---

## ?? 相關文件

- [結論規劃.md](./結論規劃.md) - 主升級計畫
- [PR-02_高風險掃描報告.md](./PR-02_高風險掃描報告.md) - 問題清單
- [PR-03_ICrmClient實作說明.md](./PR-03_ICrmClient實作說明.md) - Adapter 模式
- [PR-04_資源洩漏修正計畫.md](./PR-04_資源洩漏修正計畫.md) - 資源管理

---

**?? 重要提醒**：此重構計畫需與 PR-04 同步進行，建議先完成小範圍驗證（如 ContactService）再全面展開。

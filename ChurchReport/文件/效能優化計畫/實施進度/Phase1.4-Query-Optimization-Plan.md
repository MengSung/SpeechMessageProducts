# Phase 1.4 實施計畫 - 查詢邏輯優化

## ?? 階段概述

**目標**: 修改查詢邏輯直接使用連接池，實現真正的效能提升  
**優先級**: ?? **最高** - 這是連接池效能提升的關鍵階段  
**預估時間**: 2-3 週  
**狀態**: ? 準備開始

---

## ?? 階段目標

### 核心目標
1. ? 將高頻查詢方法改為直接使用連接池
2. ? 實現連接重用率 > 90%
3. ? 查詢回應時間降低 60-70%
4. ? 並發處理能力提升 400%
5. ? 確保無記憶體洩漏

### 次要目標
1. 建立效能監控機制
2. 記錄效能改善數據
3. 建立最佳實踐範例
4. 更新開發指南

---

## ?? 當前狀態分析

### 已完成 ?
- ? 連接池已實作並通過測試
- ? 所有 Controllers 已注入連接池
- ? 基底類別提供 `GetConnection()` 和 `ReleaseConnection()` 方法
- ? 監控機制已建立

### 待完成 ?
- ?? 查詢邏輯尚未使用連接池
- ?? 連接重用率: 0%（因為未實際使用）
- ?? 效能提升: 尚未實現
- ?? 效能監控: 需要啟用並分析數據

---

## ?? 問題分析

### 當前查詢模式
```csharp
// 現況：每次查詢都創建新連接（透過 ToolUtility）
public IActionResult GetData(string id)
{
    var toolUtility = _toolUtilityProvider.GetToolUtility();
    var entity = toolUtility.RetrieveEntity("contact", new Guid(id));
    return Json(entity);
}
```

**問題**:
1. ? 每次調用都創建新的 CRM 連接（耗時 500ms）
2. ? 連接未重用，浪費資源
3. ? 高並發時連接數過多
4. ? 連接池完全未被使用

### 目標查詢模式
```csharp
// 目標：直接使用連接池
public IActionResult GetData(string id)
{
    IOrganizationService service = null;
    try
    {
        // 從連接池獲取連接（耗時 5ms）
        service = GetConnection();
        
        // 直接使用連接執行查詢
        var entity = service.Retrieve("contact", new Guid(id), new ColumnSet(true));
        return Json(entity);
    }
    finally
    {
        // 歸還連接到池（重要！）
        ReleaseConnection(service);
    }
}
```

**優勢**:
1. ? 連接獲取時間：500ms → 5ms（減少 99%）
2. ? 連接重用率 > 90%
3. ? 支援高並發（100+ req/s）
4. ? 資源使用優化

---

## ?? 實施策略

### 策略 1: 漸進式優化（推薦）

**優點**:
- ? 風險低，逐步驗證
- ? 可隨時回退
- ? 逐步積累經驗

**步驟**:
1. Phase 1.4.1: 優化高頻 Controllers（第 1 週）
2. Phase 1.4.2: 優化中頻 Controllers（第 2 週）
3. Phase 1.4.3: 優化低頻功能（第 3 週）

### 策略 2: 激進式優化（不推薦）

**缺點**:
- ? 風險高
- ? 難以除錯
- ? 可能引入大量 Bug

---

## ?? Phase 1.4.1: 高頻 Controllers 優化

### 優先級排序

| 優先級 | Controller | 使用頻率 | 影響範圍 | 預估工作量 |
|--------|-----------|---------|---------|-----------|
| ?? P0 | AuthenticationController | 極高 | 登入流程 | 4 小時 |
| ?? P0 | SmallGroupController | 極高 | 小組回報 | 8 小時 |
| ?? P1 | PersonalController | 高 | 個人回報 | 6 小時 |
| ?? P1 | NewPersonController | 中高 | 新人管理 | 4 小時 |
| ?? P2 | DedicationController | 中 | 奉獻管理 | 4 小時 |

### P0: AuthenticationController

#### 需要優化的方法
1. **ValidateUserCredentials** - 驗證使用者憑證
2. **RetrieveUserData** - 取得使用者資料
3. **SetupSystemData** - 設定系統資料

#### 修改範例

**修改前**:
```csharp
private (bool isValid, string contactId, string errorMessage) ValidateUserCredentials(GalleryViewModel viewModel)
{
    var toolUtility = ToolUtility;
    string contactIdString = toolUtility.RetrieveContactByAccountNumber(viewModel.Account, viewModel.Password);
    
    if (contactIdString == "密碼錯誤" || contactIdString == "帳號錯誤")
    {
        return (false, "", contactIdString);
    }
    
    return (true, contactIdString, "");
}
```

**修改後**:
```csharp
private (bool isValid, string contactId, string errorMessage) ValidateUserCredentials(GalleryViewModel viewModel)
{
    IOrganizationService service = null;
    try
    {
        // 從連接池獲取連接
        service = GetConnection();
        
        // 直接使用 CRM SDK 查詢
        var query = new QueryExpression("contact")
        {
            ColumnSet = new ColumnSet("contactid", "fullname"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("new_account", ConditionOperator.Equal, viewModel.Account),
                    new ConditionExpression("new_password", ConditionOperator.Equal, viewModel.Password)
                }
            }
        };
        
        var results = service.RetrieveMultiple(query);
        
        if (results.Entities.Count == 0)
        {
            return (false, "", "帳號或密碼錯誤");
        }
        
        return (true, results.Entities[0].Id.ToString(), "");
    }
    catch (Exception ex)
    {
        return (false, "", $"驗證過程發生錯誤: {ex.Message}");
    }
    finally
    {
        // 歸還連接（非常重要！）
        ReleaseConnection(service);
    }
}
```

### P0: SmallGroupController

#### 需要優化的方法
1. **LoadIntegrate** - 載入整合資料
2. **UpdateSmallGroupPresentRecord** - 更新出席記錄
3. **SaveIntegrate** - 儲存整合資料
4. **EnsureIntegrateDataLoaded** - 確保資料已載入

#### 修改策略
由於 `SmallGroupController` 的查詢邏輯主要在 `ListManager` 和 `WebServiceConnector` 中，我們需要：

1. **短期方案**（本階段）:
   - 修改 Controller 中的直接查詢
   - 保持 `ListManager` 暫時不變
   
2. **中期方案**（Phase 1.5）:
   - 重構 `ListManager` 使用連接池
   - 重構 `WebServiceConnector` 使用連接池

#### 修改範例

**修改前**:
```csharp
[HttpGet]
public object LoadIntegrate(string id, DataSourceLoadOptions loadOptions)
{
    try
    {
        EnsureIntegrateDataLoaded(id);
        
        var tasks = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
            .m_SmallGroupDataList.m_SmallGroupData.Members;
        
        return DataSourceLoader.Load(tasks, loadOptions);
    }
    catch (Exception e)
    {
        return HandleError(e, "LoadIntegrate");
    }
}
```

**修改後**:
```csharp
[HttpGet]
public async Task<object> LoadIntegrate(string id, DataSourceLoadOptions loadOptions)
{
    try
    {
        // 使用連接池的優化版本
        await EnsureIntegrateDataLoadedOptimized(id);
        
        var tasks = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport
            .m_SmallGroupDataList.m_SmallGroupData.Members;
        
        return DataSourceLoader.Load(tasks, loadOptions);
    }
    catch (Exception e)
    {
        return HandleError(e, "LoadIntegrate");
    }
}

private async Task EnsureIntegrateDataLoadedOptimized(string id)
{
    var weeklyReport = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport;
    
    if (weeklyReport == null || !weeklyReport.LoadFlag)
    {
        IOrganizationService service = null;
        try
        {
            service = GetConnection();
            
            // 使用連接池執行查詢
            await Task.Run(() => {
                // 查詢邏輯...
            });
        }
        finally
        {
            ReleaseConnection(service);
        }
    }
}
```

---

## ?? Phase 1.4.2: 中頻 Controllers 優化

### 優先級排序

| 優先級 | Controller | 使用頻率 | 影響範圍 | 預估工作量 |
|--------|-----------|---------|---------|-----------|
| ?? P2 | EquipmentController | 中 | 裝備管理 | 6 小時 |
| ?? P2 | DedicationAuditController | 中 | 奉獻審核 | 4 小時 |
| ?? P3 | AppointmentController | 中低 | 行事曆 | 4 小時 |
| ?? P3 | ListManagementController | 中低 | 清單管理 | 4 小時 |

---

## ?? Phase 1.4.3: 低頻功能優化

### 優先級排序

| 優先級 | Controller | 使用頻率 | 影響範圍 | 預估工作量 |
|--------|-----------|---------|---------|-----------|
| ?? P3 | QrCodeController | 低 | QR Code | 2 小時 |
| ?? P3 | PhoneBindingController | 低 | 手機綁定 | 2 小時 |
| ?? P3 | HomeController | 低 | 重導向 | 1 小時 |

---

## ?? 實施步驟

### 步驟 1: 識別需要修改的方法

**工具**: 使用 code_search 搜尋

**搜尋關鍵字**:
- `ToolUtility.Retrieve`
- `toolUtility.Query`
- `_toolUtilityProvider.GetToolUtility()`

**輸出**: 建立待修改方法清單

### 步驟 2: 為每個方法建立測試

**測試類型**:
1. 功能測試：確保修改後功能正常
2. 效能測試：測量回應時間
3. 連接池測試：驗證連接正確歸還

### 步驟 3: 修改查詢邏輯

**修改模板**:
```csharp
public ActionResult YourMethod(string param)
{
    IOrganizationService service = null;
    try
    {
        // 步驟 1: 從連接池獲取連接
        service = GetConnection();
        
        // 步驟 2: 執行查詢
        var query = new QueryExpression("entityname")
        {
            ColumnSet = new ColumnSet("field1", "field2"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("field", ConditionOperator.Equal, param)
                }
            }
        };
        
        var results = service.RetrieveMultiple(query);
        
        // 步驟 3: 處理結果
        return Json(results.Entities);
    }
    catch (Exception ex)
    {
        return HandleError(ex, "YourMethod");
    }
    finally
    {
        // 步驟 4: 歸還連接（重要！）
        ReleaseConnection(service);
    }
}
```

### 步驟 4: 驗證修改

**驗證項目**:
1. ? 功能測試通過
2. ? 編譯無錯誤
3. ? 連接正確歸還
4. ? 效能有改善
5. ? 無記憶體洩漏

### 步驟 5: 效能測試

**測試指標**:
- 回應時間
- 連接重用率
- 並發處理能力
- CPU 使用率
- 記憶體使用量

---

## ?? 預期效果

### 效能指標

| 指標 | 修改前 | 修改後 | 改善幅度 |
|------|--------|--------|---------|
| 連接創建時間 | 500ms | 5ms | ↓ 99% |
| 查詢回應時間 | 3-5秒 | 1-1.5秒 | ↓ 60-70% |
| 並發處理能力 | 20 req/s | 100+ req/s | ↑ 400% |
| 連接重用率 | 0% | > 90% | ↑ 90% |
| CPU 使用率 | 60-80% | 30-50% | ↓ 30-50% |
| 記憶體使用 | 穩定 | 穩定 | 持平 |

### 使用者體驗

| 功能 | 修改前 | 修改後 | 改善 |
|------|--------|--------|------|
| 登入速度 | 5-8秒 | 2-3秒 | ????? |
| 小組回報載入 | 10-15秒 | 3-5秒 | ????? |
| 個人回報儲存 | 3-5秒 | 1-2秒 | ???? |
| 新人資料查詢 | 2-4秒 | < 1秒 | ???? |

---

## ?? 風險與對策

### 風險 1: 連接未正確歸還

**症狀**:
- 連接池耗盡
- TimeoutException
- 系統無回應

**對策**:
- ? 使用 `try-finally` 確保歸還
- ? 使用 `using` 語句包裝連接
- ? 監控連接池統計資訊
- ? 設定連接超時警告

### 風險 2: 查詢邏輯錯誤

**症狀**:
- 查詢結果不正確
- 資料遺失
- 功能異常

**對策**:
- ? 充分的單元測試
- ? 逐步修改並驗證
- ? 保留舊代碼作為參考
- ? 建立回退機制

### 風險 3: 效能反而下降

**症狀**:
- 回應時間增加
- 記憶體使用增加
- CPU 使用率增加

**對策**:
- ? 效能基準測試
- ? 逐步優化並測量
- ? 使用效能分析工具
- ? 必要時回退修改

### 風險 4: 並發問題

**症狀**:
- 資料競爭
- 死鎖
- 連接洩漏

**對策**:
- ? 避免共享可變狀態
- ? 使用 async/await
- ? 設定連接超時
- ? 負載測試驗證

---

## ?? 修改檢查清單

### 修改前檢查
- [ ] 已識別需要修改的方法
- [ ] 已建立功能測試
- [ ] 已記錄當前效能數據
- [ ] 已備份當前代碼

### 修改中檢查
- [ ] 使用 `GetConnection()` 獲取連接
- [ ] 使用 `try-finally` 包裝
- [ ] 在 `finally` 中調用 `ReleaseConnection()`
- [ ] 查詢邏輯正確轉換
- [ ] 編譯無錯誤

### 修改後檢查
- [ ] 功能測試通過
- [ ] 效能測試顯示改善
- [ ] 連接正確歸還
- [ ] 連接池統計正常
- [ ] 無記憶體洩漏
- [ ] 無編譯警告
- [ ] 代碼已審查

---

## ??? 開發輔助工具

### 1. 連接池監控端點

在 `BaseChurchController` 中已提供：

```csharp
[HttpGet]
[Route("/api/connection-pool-stats")]
public IActionResult GetConnectionPoolStats()
{
    var stats = GetConnectionPoolStats();
    return Json(new
    {
        totalConnections = stats.TotalConnections,
        activeConnections = stats.ActiveConnections,
        idleConnections = stats.IdleConnections,
        waitingRequests = stats.WaitingRequests,
        reuseRate = stats.TotalReleaseCount > 0 
            ? (double)(stats.TotalAcquireCount - stats.TotalConnections) / stats.TotalAcquireCount * 100 
            : 0
    });
}
```

### 2. 效能測試工具

**推薦工具**:
- **BenchmarkDotNet**: 微基準測試
- **Apache JMeter**: 負載測試
- **dotTrace**: 效能分析
- **Application Insights**: APM 監控

### 3. 代碼分析工具

**推薦工具**:
- **ReSharper**: 代碼分析
- **StyleCop**: 代碼風格
- **SonarQube**: 代碼品質

---

## ?? 最佳實踐範例

### 範例 1: 簡單查詢

```csharp
public IActionResult GetContact(string contactId)
{
    IOrganizationService service = null;
    try
    {
        service = GetConnection();
        
        var entity = service.Retrieve("contact", new Guid(contactId), 
            new ColumnSet("fullname", "mobilephone", "emailaddress1"));
        
        return Json(entity);
    }
    finally
    {
        ReleaseConnection(service);
    }
}
```

### 範例 2: 複雜查詢

```csharp
public IActionResult GetWeeklyReport(Guid listId, DateTime sunday)
{
    IOrganizationService service = null;
    try
    {
        service = GetConnection();
        
        var query = new QueryExpression("new_weekly_report")
        {
            ColumnSet = new ColumnSet("new_name", "new_date", "new_attendance"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("new_list", ConditionOperator.Equal, listId),
                    new ConditionExpression("new_sunday", ConditionOperator.Equal, sunday)
                }
            },
            LinkEntities =
            {
                new LinkEntity
                {
                    LinkFromEntityName = "new_weekly_report",
                    LinkToEntityName = "contact",
                    LinkFromAttributeName = "new_leader",
                    LinkToAttributeName = "contactid",
                    Columns = new ColumnSet("fullname"),
                    EntityAlias = "leader"
                }
            }
        };
        
        var results = service.RetrieveMultiple(query);
        return Json(results.Entities);
    }
    finally
    {
        ReleaseConnection(service);
    }
}
```

### 範例 3: 批量操作

```csharp
public IActionResult BatchUpdate(List<Entity> entities)
{
    IOrganizationService service = null;
    try
    {
        service = GetConnection();
        
        // 使用 ExecuteMultiple 提升效能
        var multipleRequest = new ExecuteMultipleRequest
        {
            Settings = new ExecuteMultipleSettings
            {
                ContinueOnError = false,
                ReturnResponses = true
            },
            Requests = new OrganizationRequestCollection()
        };
        
        foreach (var entity in entities)
        {
            var updateRequest = new UpdateRequest { Target = entity };
            multipleRequest.Requests.Add(updateRequest);
        }
        
        var multipleResponse = (ExecuteMultipleResponse)service.Execute(multipleRequest);
        
        return Json(new { success = true, count = entities.Count });
    }
    finally
    {
        ReleaseConnection(service);
    }
}
```

### 範例 4: 錯誤處理

```csharp
public IActionResult SafeQuery(string id)
{
    IOrganizationService service = null;
    try
    {
        service = GetConnection();
        
        var entity = service.Retrieve("contact", new Guid(id), new ColumnSet(true));
        return Json(entity);
    }
    catch (FaultException<OrganizationServiceFault> ex)
    {
        // CRM 特定錯誤
        return Json(new { error = $"CRM 錯誤: {ex.Detail.Message}" });
    }
    catch (TimeoutException ex)
    {
        // 連接超時
        return Json(new { error = "查詢超時，請稍後再試" });
    }
    catch (Exception ex)
    {
        // 一般錯誤
        return HandleError(ex, "SafeQuery");
    }
    finally
    {
        // 無論如何都要歸還連接
        ReleaseConnection(service);
    }
}
```

---

## ?? 時程規劃

### 第 1 週

**Phase 1.4.1: 高頻 Controllers（P0）**

| 日期 | 任務 | 負責人 | 狀態 |
|------|------|--------|------|
| Day 1-2 | 優化 AuthenticationController | 開發團隊 | ? |
| Day 3-5 | 優化 SmallGroupController | 開發團隊 | ? |
| Day 5 | 第一週效能測試 | QA 團隊 | ? |

### 第 2 週

**Phase 1.4.2: 中頻 Controllers（P1-P2）**

| 日期 | 任務 | 負責人 | 狀態 |
|------|------|--------|------|
| Day 1-3 | 優化 PersonalController | 開發團隊 | ? |
| Day 3-4 | 優化 NewPersonController | 開發團隊 | ? |
| Day 4-5 | 優化 DedicationController | 開發團隊 | ? |
| Day 5 | 第二週效能測試 | QA 團隊 | ? |

### 第 3 週

**Phase 1.4.3: 低頻功能（P3）**

| 日期 | 任務 | 負責人 | 狀態 |
|------|------|--------|------|
| Day 1-2 | 優化剩餘 Controllers | 開發團隊 | ? |
| Day 3-4 | 完整效能測試 | QA 團隊 | ? |
| Day 5 | 撰寫完成報告 | 開發團隊 | ? |

---

## ?? 成功標準

### 必達標準（Must Have）
- ? 連接重用率 > 90%
- ? 查詢回應時間降低 > 50%
- ? 所有功能測試通過
- ? 無記憶體洩漏
- ? 無編譯錯誤或警告

### 期望標準（Should Have）
- ? 並發處理能力提升 > 300%
- ? CPU 使用率降低 > 30%
- ? 使用者體驗明顯改善
- ? 建立完整的測試套件

### 額外標準（Nice to Have）
- ? 建立效能監控儀表板
- ? 撰寫最佳實踐文檔
- ? 提供開發指南
- ? 建立自動化測試

---

## ?? 學習資源

### Microsoft 官方文檔
- [IOrganizationService Interface](https://docs.microsoft.com/en-us/dotnet/api/microsoft.xrm.sdk.iorganizationservice)
- [Query Data using SDK](https://docs.microsoft.com/en-us/power-apps/developer/data-platform/org-service/entity-operations-query-data)
- [Use ExecuteMultiple](https://docs.microsoft.com/en-us/power-apps/developer/data-platform/org-service/execute-multiple-requests)

### 效能優化
- [.NET Performance Tips](https://docs.microsoft.com/en-us/dotnet/framework/performance/)
- [Connection Pooling Best Practices](https://docs.microsoft.com/en-us/dotnet/framework/data/adonet/sql-server-connection-pooling)

### 設計模式
- [Object Pool Pattern](https://en.wikipedia.org/wiki/Object_pool_pattern)
- [Repository Pattern](https://docs.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/infrastructure-persistence-layer-design)

---

## ?? 下一步行動

### 立即行動（本週）
1. **啟動 Phase 1.4.1**: 開始優化 AuthenticationController
2. **建立效能基準**: 記錄當前效能數據
3. **設定監控**: 啟用連接池監控端點

### 短期行動（2-3 週）
1. 完成所有 Controllers 優化
2. 進行完整效能測試
3. 撰寫 Phase 1.4 完成報告

### 長期行動（1-2 個月）
1. Phase 1.5: WebServiceConnector 優化
2. Phase 1.6: ToolUtility Facade 整合
3. Phase 2: 快取機制實作

---

## ?? 相關文檔

- [Phase 1.3 完成總結](./Phase1.3-完成總結.md) - 前置作業
- [Phase 1.2 完成報告](./Phase1.2-ConnectionPool-完成報告.md) - 連接池實作
- [效能優化 TODO 清單](../效能優化TODO清單.md) - 整體進度
- [連接池使用指南](./Phase1.2-ConnectionPool-Usage-Guide.md) - 使用說明

---

**文件版本**: v1.0  
**建立日期**: 2024-01-XX  
**狀態**: ? 準備開始  
**負責人**: 開發團隊

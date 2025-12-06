# Phase 2: 非同步化與並行處理 - 實施指南

## 📋 目標與效益

### 主要目標
- ✅ 將關鍵查詢方法改為非同步執行
- ✅ 實現批量操作的並行處理
- ✅ 將所有 Controller Action 改為 async/await
- ✅ 提升系統並發處理能力 3-5 倍
- ✅ UI 響應速度提升 50%

### 預期效益

| 優化項目 | 當前效能 | 目標效能 | 改善幅度 |
|---------|---------|---------|---------|
| UI 響應時間 | ~2-3秒 | <1秒 | **60-70%** |
| 並發處理能力 | ~20 req/s | 60-100 req/s | **300-400%** |
| 批量操作速度 | 1000筆/分鐘 | 5000-10000筆/分鐘 | **500-1000%** |
| CPU 使用率 | 60-80% | 30-50% | **30-50%** |
| 執行緒阻塞 | 頻繁 | 極少 | **90%** |

---

## 🎯 Phase 2.1: 關鍵查詢方法非同步化

•	CollectionQueryService 完整改造
•	非同步查詢方法實現
•	分頁查詢支援
•	批量查詢優化

### 2.1.1 CollectionQueryService 非同步改造

#### 當前問題
```csharp
// ❌ 同步方法 - 阻塞執行緒
public EntityCollection RetrieveEntityCollectionByField(
    string EntityName, 
    string FieldName, 
    ConditionOperator qConditionOperator, 
    string qValue)
{
    var query = new QueryExpression(EntityName)
    {
        ColumnSet = new ColumnSet(true),
        Criteria = new FilterExpression
        {
            Conditions = 
            { 
                new ConditionExpression(FieldName, qConditionOperator, qValue) 
            }
        }
    };
    
    // 同步查詢 - 阻塞當前執行緒
    return m_OrganizationService.RetrieveMultiple(query);
}
```

#### 優化方案

```csharp
// ✅ 非同步方法 - 釋放執行緒
public async Task<EntityCollection> RetrieveEntityCollectionByFieldAsync(
    string entityName, 
    string fieldName, 
    ConditionOperator conditionOperator, 
    string value,
    CancellationToken cancellationToken = default)
{
    var query = new QueryExpression(entityName)
    {
        ColumnSet = new ColumnSet(true),
        Criteria = new FilterExpression
        {
            Conditions = 
            { 
                new ConditionExpression(fieldName, conditionOperator, value) 
            }
        }
    };
    
    // 使用 Task.Run 包裝同步 CRM 查詢
    return await Task.Run(() => m_OrganizationService.RetrieveMultiple(query), cancellationToken)
        .ConfigureAwait(false);
}

// ✅ 帶分頁的非同步查詢
public async Task<PagedResult<Entity>> RetrievePagedEntitiesAsync(
    string entityName,
    FilterExpression filter = null,
    int pageSize = 100,
    string pagingCookie = null,
    CancellationToken cancellationToken = default)
{
    var query = new QueryExpression(entityName)
    {
        ColumnSet = new ColumnSet(true),
        PageInfo = new PagingInfo
        {
            Count = pageSize,
            PageNumber = 1,
            PagingCookie = pagingCookie
        }
    };
    
    if (filter != null)
    {
        query.Criteria = filter;
    }
    
    var result = await Task.Run(() => m_OrganizationService.RetrieveMultiple(query), cancellationToken)
        .ConfigureAwait(false);
    
    return new PagedResult<Entity>
    {
        Entities = result.Entities.ToList(),
        TotalCount = result.TotalRecordCount,
        MoreRecords = result.MoreRecords,
        PagingCookie = result.PagingCookie
    };
}

// 分頁結果模型
public class PagedResult<T>
{
    public List<T> Entities { get; set; }
    public int TotalCount { get; set; }
    public bool MoreRecords { get; set; }
    public string PagingCookie { get; set; }
}
```

#### 完整檔案修改

**檔案位置**: `ToolUtility\CollectionOperations\CollectionQueryService.cs`

```csharp
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ToolUtilityNameSpace.CollectionOperations
{
    /// <summary>
    /// 集合查詢服務 - 非同步版本
    /// 遵循 LINUS 原則: 簡潔、高效、可測試
    /// </summary>
    public class CollectionQueryService : ICollectionQueryService
    {
        private readonly IOrganizationService m_OrganizationService;
        
        public CollectionQueryService(IOrganizationService service)
        {
            m_OrganizationService = service ?? throw new ArgumentNullException(nameof(service));
        }
        
        #region 非同步查詢方法
        
        /// <summary>
        /// 非同步查詢實體集合 (單一欄位條件)
        /// </summary>
        public async Task<EntityCollection> RetrieveEntityCollectionByFieldAsync(
            string entityName, 
            string fieldName, 
            ConditionOperator conditionOperator, 
            string value,
            CancellationToken cancellationToken = default)
        {
            var query = new QueryExpression(entityName)
            {
                ColumnSet = new ColumnSet(true),
                Criteria = new FilterExpression
                {
                    Conditions = 
                    { 
                        new ConditionExpression(fieldName, conditionOperator, value) 
                    }
                }
            };
            
            return await ExecuteQueryAsync(query, cancellationToken).ConfigureAwait(false);
        }
        
        /// <summary>
        /// 非同步查詢實體集合 (多欄位條件)
        /// </summary>
        public async Task<EntityCollection> RetrieveEntityCollectionByFieldsAsync(
            string entityName,
            Dictionary<string, object> conditions,
            CancellationToken cancellationToken = default)
        {
            var query = new QueryExpression(entityName)
            {
                ColumnSet = new ColumnSet(true),
                Criteria = new FilterExpression
                {
                    FilterOperator = LogicalOperator.And
                }
            };
            
            foreach (var condition in conditions)
            {
                query.Criteria.Conditions.Add(
                    new ConditionExpression(condition.Key, ConditionOperator.Equal, condition.Value));
            }
            
            return await ExecuteQueryAsync(query, cancellationToken).ConfigureAwait(false);
        }
        
        /// <summary>
        /// 非同步查詢週報資料 (指定日期區間)
        /// </summary>
        public async Task<EntityCollection> QueryWeeklyReportBeforeTowMonthOfSundayAsync(
            Guid listId,
            DateTime startDate,
            DateTime endDate,
            CancellationToken cancellationToken = default)
        {
            var query = new QueryExpression("new_weekly_report")
            {
                ColumnSet = new ColumnSet(true),
                Criteria = new FilterExpression
                {
                    FilterOperator = LogicalOperator.And,
                    Conditions =
                    {
                        new ConditionExpression("new_list", ConditionOperator.Equal, listId),
                        new ConditionExpression("new_sunday", ConditionOperator.GreaterEqual, startDate),
                        new ConditionExpression("new_sunday", ConditionOperator.LessEqual, endDate),
                        new ConditionExpression("statecode", ConditionOperator.Equal, 0)
                    }
                },
                Orders = 
                { 
                    new OrderExpression("new_sunday", OrderType.Descending) 
                }
            };
            
            return await ExecuteQueryAsync(query, cancellationToken).ConfigureAwait(false);
        }
        
        /// <summary>
        /// 非同步分頁查詢
        /// </summary>
        public async Task<PagedResult<Entity>> RetrievePagedEntitiesAsync(
            string entityName,
            FilterExpression filter = null,
            ColumnSet columnSet = null,
            int pageSize = 100,
            string pagingCookie = null,
            CancellationToken cancellationToken = default)
        {
            var query = new QueryExpression(entityName)
            {
                ColumnSet = columnSet ?? new ColumnSet(true),
                PageInfo = new PagingInfo
                {
                    Count = pageSize,
                    PageNumber = string.IsNullOrEmpty(pagingCookie) ? 1 : 2,
                    PagingCookie = pagingCookie
                }
            };
            
            if (filter != null)
            {
                query.Criteria = filter;
            }
            
            var result = await ExecuteQueryAsync(query, cancellationToken).ConfigureAwait(false);
            
            return new PagedResult<Entity>
            {
                Entities = result.Entities.ToList(),
                TotalCount = result.TotalRecordCount,
                MoreRecords = result.MoreRecords,
                PagingCookie = result.PagingCookie
            };
        }
        
        /// <summary>
        /// 非同步批量查詢 (使用 IN 條件)
        /// </summary>
        public async Task<EntityCollection> RetrieveBatchByIdsAsync(
            string entityName,
            string idFieldName,
            IEnumerable<Guid> ids,
            ColumnSet columnSet = null,
            CancellationToken cancellationToken = default)
        {
            var query = new QueryExpression(entityName)
            {
                ColumnSet = columnSet ?? new ColumnSet(true),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression(idFieldName, ConditionOperator.In, ids.ToArray())
                    }
                }
            };
            
            return await ExecuteQueryAsync(query, cancellationToken).ConfigureAwait(false);
        }
        
        #endregion
        
        #region 私有輔助方法
        
        /// <summary>
        /// 執行非同步查詢 (內部方法)
        /// </summary>
        private async Task<EntityCollection> ExecuteQueryAsync(
            QueryExpression query, 
            CancellationToken cancellationToken)
        {
            return await Task.Run(() => 
            {
                cancellationToken.ThrowIfCancellationRequested();
                return m_OrganizationService.RetrieveMultiple(query);
            }, cancellationToken).ConfigureAwait(false);
        }
        
        #endregion
        
        #region 同步方法 (向下相容，標記為 Obsolete)
        
        [Obsolete("請使用 RetrieveEntityCollectionByFieldAsync 替代")]
        public EntityCollection RetrieveEntityCollectionByField(
            string entityName, 
            string fieldName, 
            ConditionOperator conditionOperator, 
            string value)
        {
            return RetrieveEntityCollectionByFieldAsync(entityName, fieldName, conditionOperator, value)
                .GetAwaiter()
                .GetResult();
        }
        
        #endregion
    }
    
    /// <summary>
    /// 分頁查詢結果模型
    /// </summary>
    public class PagedResult<T>
    {
        public List<T> Entities { get; set; } = new List<T>();
        public int TotalCount { get; set; }
        public bool MoreRecords { get; set; }
        public string PagingCookie { get; set; }
    }
}
```

---

## 🎯 Phase 2.2: 批量操作並行化
•	Parallel.ForEach 方案
•	Task.WhenAll 方案
•	ListService 完整實現
### 2.2.1 AddMembersToMarketingList 並行處理

#### 當前問題
```csharp
// ❌ 循序處理 - 效率低下
public void AddMembersToMarketingList(Guid listId, List<Guid> memberIds)
{
    foreach (var memberId in memberIds)
    {
        var request = new AddMemberListRequest
        {
            ListId = listId,
            EntityId = memberId
        };
        
        // 逐一執行 - 1000筆需要 10 分鐘
        m_OrganizationService.Execute(request);
    }
}
```

#### 優化方案 - 方案 A：使用 Parallel.ForEach

```csharp
// ✅ 並行處理 - 使用 Parallel.ForEach
public async Task AddMembersToMarketingListParallelAsync(
    Guid listId, 
    List<Guid> memberIds,
    int maxDegreeOfParallelism = 5)
{
    var options = new ParallelOptions
    {
        MaxDegreeOfParallelism = maxDegreeOfParallelism // 最多 5 個並行
    };
    
    var errors = new ConcurrentBag<(Guid MemberId, Exception Error)>();
    
    await Task.Run(() =>
    {
        Parallel.ForEach(memberIds, options, memberId =>
        {
            try
            {
                var request = new AddMemberListRequest
                {
                    ListId = listId,
                    EntityId = memberId
                };
                
                // 從連接池獲取連接
                var service = GetConnection();
                try
                {
                    service.Execute(request);
                }
                finally
                {
                    ReleaseConnection(service);
                }
            }
            catch (Exception ex)
            {
                errors.Add((memberId, ex));
            }
        });
    });
    
    // 處理錯誤
    if (errors.Count > 0)
    {
        throw new AggregateException(
            $"新增 {errors.Count} 個成員失敗", 
            errors.Select(e => e.Error));
    }
}
```

#### 優化方案 - 方案 B：使用 Task.WhenAll (更精細控制)

```csharp
// ✅ 批次並行處理 - 使用 Task.WhenAll
public async Task AddMembersToMarketingListBatchAsync(
    Guid listId, 
    List<Guid> memberIds,
    int batchSize = 50,
    int maxConcurrency = 5)
{
    // 分批處理
    var batches = memberIds
        .Select((id, index) => new { id, index })
        .GroupBy(x => x.index / batchSize)
        .Select(g => g.Select(x => x.id).ToList())
        .ToList();
    
    var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    var tasks = new List<Task>();
    var errors = new ConcurrentBag<(Guid MemberId, Exception Error)>();
    
    foreach (var batch in batches)
    {
        await semaphore.WaitAsync();
        
        var task = Task.Run(async () =>
        {
            try
            {
                foreach (var memberId in batch)
                {
                    try
                    {
                        var request = new AddMemberListRequest
                        {
                            ListId = listId,
                            EntityId = memberId
                        };
                        
                        // 從連接池獲取連接
                        var service = GetConnection();
                        try
                        {
                            await Task.Run(() => service.Execute(request));
                        }
                        finally
                        {
                            ReleaseConnection(service);
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add((memberId, ex));
                    }
                }
            }
            finally
            {
                semaphore.Release();
            }
        });
        
        tasks.Add(task);
    }
    
    await Task.WhenAll(tasks);
    
    // 處理錯誤
    if (errors.Count > 0)
    {
        throw new AggregateException(
            $"新增 {errors.Count} 個成員失敗", 
            errors.Select(e => e.Error));
    }
}
```

#### 完整檔案修改

**檔案位置**: `ToolUtility\ListOperations\ListService.cs`

```csharp
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ToolUtilityNameSpace.ListOperations
{
    /// <summary>
    /// 名單操作服務 - 並行處理版本
    /// 遵循 LINUS 原則: 高效、可擴展、錯誤處理完善
    /// </summary>
    public class ListService : IListService
    {
        private readonly Func<IOrganizationService> _getConnection;
        private readonly Action<IOrganizationService> _releaseConnection;
        
        public ListService(
            Func<IOrganizationService> getConnection,
            Action<IOrganizationService> releaseConnection)
        {
            _getConnection = getConnection ?? throw new ArgumentNullException(nameof(getConnection));
            _releaseConnection = releaseConnection ?? throw new ArgumentNullException(nameof(releaseConnection));
        }
        
        #region 批量並行操作
        
        /// <summary>
        /// 批量新增成員到名單 (並行處理)
        /// 效能提升: 5-10 倍
        /// </summary>
        public async Task<BatchOperationResult> AddMembersToMarketingListAsync(
            Guid listId, 
            List<Guid> memberIds,
            int batchSize = 50,
            int maxConcurrency = 5,
            CancellationToken cancellationToken = default)
        {
            var result = new BatchOperationResult();
            var startTime = DateTime.UtcNow;
            
            // 分批處理
            var batches = memberIds
                .Select((id, index) => new { id, index })
                .GroupBy(x => x.index / batchSize)
                .Select(g => g.Select(x => x.id).ToList())
                .ToList();
            
            var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            var tasks = new List<Task>();
            
            foreach (var batch in batches)
            {
                await semaphore.WaitAsync(cancellationToken);
                
                var task = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessBatchAsync(listId, batch, result, cancellationToken);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken);
                
                tasks.Add(task);
            }
            
            await Task.WhenAll(tasks);
            
            result.Duration = DateTime.UtcNow - startTime;
            result.TotalProcessed = memberIds.Count;
            
            return result;
        }
        
        /// <summary>
        /// 批量移除成員從名單 (並行處理)
        /// </summary>
        public async Task<BatchOperationResult> RemoveMembersFromMarketingListAsync(
            Guid listId, 
            List<Guid> memberIds,
            int batchSize = 50,
            int maxConcurrency = 5,
            CancellationToken cancellationToken = default)
        {
            var result = new BatchOperationResult();
            var startTime = DateTime.UtcNow;
            
            var batches = memberIds
                .Select((id, index) => new { id, index })
                .GroupBy(x => x.index / batchSize)
                .Select(g => g.Select(x => x.id).ToList())
                .ToList();
            
            var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            var tasks = new List<Task>();
            
            foreach (var batch in batches)
            {
                await semaphore.WaitAsync(cancellationToken);
                
                var task = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessRemoveBatchAsync(listId, batch, result, cancellationToken);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken);
                
                tasks.Add(task);
            }
            
            await Task.WhenAll(tasks);
            
            result.Duration = DateTime.UtcNow - startTime;
            result.TotalProcessed = memberIds.Count;
            
            return result;
        }
        
        #endregion
        
        #region 私有輔助方法
        
        /// <summary>
        /// 處理單一批次的新增操作
        /// </summary>
        private async Task ProcessBatchAsync(
            Guid listId,
            List<Guid> batch,
            BatchOperationResult result,
            CancellationToken cancellationToken)
        {
            foreach (var memberId in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                IOrganizationService service = null;
                try
                {
                    service = _getConnection();
                    
                    var request = new AddMemberListRequest
                    {
                        ListId = listId,
                        EntityId = memberId
                    };
                    
                    await Task.Run(() => service.Execute(request), cancellationToken);
                    
                    result.AddSuccess(memberId);
                }
                catch (Exception ex)
                {
                    result.AddFailure(memberId, ex);
                }
                finally
                {
                    if (service != null)
                    {
                        _releaseConnection(service);
                    }
                }
            }
        }
        
        /// <summary>
        /// 處理單一批次的移除操作
        /// </summary>
        private async Task ProcessRemoveBatchAsync(
            Guid listId,
            List<Guid> batch,
            BatchOperationResult result,
            CancellationToken cancellationToken)
        {
            foreach (var memberId in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                IOrganizationService service = null;
                try
                {
                    service = _getConnection();
                    
                    var request = new RemoveMemberListRequest
                    {
                        ListId = listId,
                        EntityId = memberId
                    };
                    
                    await Task.Run(() => service.Execute(request), cancellationToken);
                    
                    result.AddSuccess(memberId);
                }
                catch (Exception ex)
                {
                    result.AddFailure(memberId, ex);
                }
                finally
                {
                    if (service != null)
                    {
                        _releaseConnection(service);
                    }
                }
            }
        }
        
        #endregion
    }
    
    /// <summary>
    /// 批量操作結果模型
    /// </summary>
    public class BatchOperationResult
    {
        private readonly ConcurrentBag<Guid> _successIds = new ConcurrentBag<Guid>();
        private readonly ConcurrentBag<(Guid Id, Exception Error)> _failures = new ConcurrentBag<(Guid, Exception)>();
        
        public int TotalProcessed { get; set; }
        public TimeSpan Duration { get; set; }
        public int SuccessCount => _successIds.Count;
        public int FailureCount => _failures.Count;
        public List<Guid> SuccessIds => _successIds.ToList();
        public List<(Guid Id, Exception Error)> Failures => _failures.ToList();
        
        public void AddSuccess(Guid id)
        {
            _successIds.Add(id);
        }
        
        public void AddFailure(Guid id, Exception error)
        {
            _failures.Add((id, error));
        }
        
        public override string ToString()
        {
            return $"成功: {SuccessCount}/{TotalProcessed}, 失敗: {FailureCount}, 耗時: {Duration.TotalSeconds:F2}秒";
        }
    }
}
```

---

## 🎯 Phase 2.3: Controller Action 非同步化
•	AuthenticationController 優化建議
•	SmallGroupController 改造範例
•	PowerShell 批量更新腳本
### 2.3.1 AuthenticationController 非同步改造

**檔案位置**: `ChurchReport\Controllers\AuthenticationController.cs`

#### 已完成的優化
✅ `ProcessLogin` 已經是非同步方法  
✅ `SaveUserLineId` 已經是非同步方法  
✅ `ProcessLineBinding` 已經是非同步方法  

#### 需要改造的方法

```csharp
// ❌ 同步方法
[HttpGet]
[Route("/Authentication/Login")]
public async Task<IActionResult> Login()
{
    // 已經是 async，但沒有 await 操作
    // 建議移除 async 或添加真正的非同步操作
}

// ✅ 改為純同步 (因為沒有 I/O 操作)
[HttpGet]
[Route("/Authentication/Login")]
public IActionResult Login()
{
    try
    {
        var images = new List<string>
        {
            Url.Content("~/assets/images/jesus.jpg")
        };

        return View(new GalleryViewModel { Images = images });
    }
    catch (Exception e)
    {
        return HandleError(e, "Login");
    }
}
```

### 2.3.2 SmallGroupController 非同步改造

**檔案位置**: `ChurchReport\Controllers\SmallGroupController.cs`

```csharp
// ❌ 同步方法範例
public IActionResult GetWeeklyReport(Guid listId, DateTime sunday)
{
    var toolUtility = _toolUtilityProvider.GetToolUtility();
    var report = toolUtility.QueryWeeklyReport(listId, sunday);
    return Json(report);
}

// ✅ 改為非同步
public async Task<IActionResult> GetWeeklyReportAsync(Guid listId, DateTime sunday)
{
    var toolUtility = _toolUtilityProvider.GetToolUtility();
    var collectionService = toolUtility.CollectionQuery;
    
    // 使用非同步查詢
    var reports = await collectionService.QueryWeeklyReportBeforeTowMonthOfSundayAsync(
        listId, 
        sunday.AddDays(-1), 
        sunday.AddDays(1));
    
    var report = reports.Entities.FirstOrDefault();
    
    return Json(report != null ? new
    {
        Id = report.Id,
        Sunday = report.GetAttributeValue<DateTime>("new_sunday"),
        AttendanceCount = report.GetAttributeValue<int>("new_attendance_count"),
        // ... 其他欄位
    } : null);
}
```

### 2.3.3 批量更新 Controller 的 PowerShell 腳本

**檔案位置**: `ChurchReport\文件\效能優化計畫\實施進度\Update-Controllers-Async.ps1`

```powershell
# PowerShell 腳本: 批量將 Controller Action 改為非同步

param(
    [string]$SolutionDir = "D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\ChurchReport\Controllers"
)

$controllers = @(
    "SmallGroupController.cs",
    "DedicationController.cs",
    "PersonalController.cs",
    "ListManagementController.cs",
    "AppointmentController.cs",
    "EquipmentController.cs"
)

foreach ($controller in $controllers) {
    $filePath = Join-Path $SolutionDir $controller
    
    if (Test-Path $filePath) {
        Write-Host "處理 $controller ..." -ForegroundColor Yellow
        
        $content = Get-Content $filePath -Raw
        
        # 1. 將 IActionResult 改為 Task<IActionResult>
        $content = $content -replace 'public\s+IActionResult\s+(\w+)\s*\(', 'public async Task<IActionResult> $1('
        
        # 2. 將 JsonResult 改為 Task<JsonResult>
        $content = $content -replace 'public\s+JsonResult\s+(\w+)\s*\(', 'public async Task<JsonResult> $1('
        
        # 3. 將方法名稱加上 Async 後綴 (如果還沒有)
        $content = $content -replace 'public async Task<IActionResult>\s+(?!.*Async)(\w+)\s*\(', 'public async Task<IActionResult> $1Async('
        
        Set-Content $filePath $content -Encoding UTF8
        
        Write-Host "✓ 完成 $controller" -ForegroundColor Green
    }
    else {
        Write-Host "✗ 找不到 $controller" -ForegroundColor Red
    }
}

Write-Host "`n所有 Controller 已更新為非同步方法" -ForegroundColor Cyan
Write-Host "請手動檢查並添加 await 關鍵字到適當的方法呼叫" -ForegroundColor Yellow
```

---

## 📊 Phase 2.4: 效能測試與驗證

•	單元測試範例
•	負載測試腳本 (k6)

### 2.4.1 單元測試

**檔案位置**: `ChurchReport.Tests\PerformanceTests\AsyncPerformanceTests.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ToolUtilityNameSpace.CollectionOperations;
using ToolUtilityNameSpace.ListOperations;

namespace ChurchReport.Tests.PerformanceTests
{
    public class AsyncPerformanceTests
    {
        [Fact]
        public async Task TestAsyncQueryPerformance()
        {
            // Arrange
            var service = GetTestOrganizationService();
            var collectionService = new CollectionQueryService(service);
            var listId = Guid.Parse("YOUR-TEST-LIST-ID");
            
            // Act - 同步查詢
            var sw1 = Stopwatch.StartNew();
            var syncResult = collectionService.RetrieveEntityCollectionByField(
                "contact", "new_listid", ConditionOperator.Equal, listId.ToString());
            sw1.Stop();
            
            // Act - 非同步查詢
            var sw2 = Stopwatch.StartNew();
            var asyncResult = await collectionService.RetrieveEntityCollectionByFieldAsync(
                "contact", "new_listid", ConditionOperator.Equal, listId.ToString());
            sw2.Stop();
            
            // Assert
            Assert.Equal(syncResult.Entities.Count, asyncResult.Entities.Count);
            Assert.True(sw2.ElapsedMilliseconds <= sw1.ElapsedMilliseconds * 1.2, 
                $"非同步查詢時間 ({sw2.ElapsedMilliseconds}ms) 應該不超過同步查詢的 120% ({sw1.ElapsedMilliseconds}ms)");
            
            Console.WriteLine($"同步查詢: {sw1.ElapsedMilliseconds}ms");
            Console.WriteLine($"非同步查詢: {sw2.ElapsedMilliseconds}ms");
        }
        
        [Fact]
        public async Task TestParallelBatchOperationPerformance()
        {
            // Arrange
            var getConnection = () => GetTestOrganizationService();
            var releaseConnection = (IOrganizationService s) => { /* 釋放連接 */ };
            var listService = new ListService(getConnection, releaseConnection);
            var listId = Guid.Parse("YOUR-TEST-LIST-ID");
            var memberIds = Enumerable.Range(1, 1000).Select(_ => Guid.NewGuid()).ToList();
            
            // Act - 循序處理 (模擬)
            var sw1 = Stopwatch.StartNew();
            // ... 循序處理邏輯
            sw1.Stop();
            
            // Act - 並行處理
            var sw2 = Stopwatch.StartNew();
            var result = await listService.AddMembersToMarketingListAsync(listId, memberIds);
            sw2.Stop();
            
            // Assert
            Assert.Equal(1000, result.SuccessCount);
            Assert.True(sw2.ElapsedMilliseconds < sw1.ElapsedMilliseconds / 3, 
                "並行處理應該比循序處理快至少 3 倍");
            
            Console.WriteLine($"循序處理: {sw1.ElapsedMilliseconds}ms");
            Console.WriteLine($"並行處理: {sw2.ElapsedMilliseconds}ms");
            Console.WriteLine($"效能提升: {(double)sw1.ElapsedMilliseconds / sw2.ElapsedMilliseconds:F2}x");
        }
    }
}
```

### 2.4.2 負載測試

**檔案位置**: `ChurchReport\文件\效能優化計畫\實施進度\LoadTest-Async.js`

```javascript
// 使用 k6 進行負載測試
import http from 'k6/http';
import { check, sleep } from 'k6';

export let options = {
    stages: [
        { duration: '1m', target: 10 },  // 1 分鐘內增加到 10 個虛擬用戶
        { duration: '3m', target: 50 },  // 3 分鐘內增加到 50 個虛擬用戶
        { duration: '2m', target: 100 }, // 2 分鐘內增加到 100 個虛擬用戶
        { duration: '2m', target: 0 },   // 2 分鐘內降到 0 個虛擬用戶
    ],
    thresholds: {
        http_req_duration: ['p(95)<1000'], // 95% 的請求應該在 1 秒內完成
        http_req_failed: ['rate<0.01'],    // 失敗率應該低於 1%
    },
};

export default function () {
    // 測試非同步登入
    let loginResponse = http.post('https://localhost:5001/Authentication/ProcessLogin', JSON.stringify({
        Account: 'testuser',
        Password: 'testpass'
    }), {
        headers: { 'Content-Type': 'application/json' },
    });
    
    check(loginResponse, {
        'login status is 200': (r) => r.status === 200,
        'login response time < 1s': (r) => r.timings.duration < 1000,
    });
    
    sleep(1);
    
    // 測試非同步查詢
    let queryResponse = http.get('https://localhost:5001/SmallGroup/GetWeeklyReportAsync?listId=xxx&sunday=2024-01-01');
    
    check(queryResponse, {
        'query status is 200': (r) => r.status === 200,
        'query response time < 500ms': (r) => r.timings.duration < 500,
    });
    
    sleep(1);
}
```

---

## ✅ 實施檢查清單

### Phase 2.1: 查詢非同步化
- [ ] CollectionQueryService 所有查詢方法改為非同步
- [ ] 添加 CancellationToken 支援
- [ ] 實現分頁查詢方法
- [ ] 實現批量 ID 查詢方法
- [ ] 舊方法標記為 Obsolete
- [ ] 單元測試覆蓋率 > 80%

### Phase 2.2: 批量操作並行化
- [ ] ListService.AddMembersToMarketingListAsync 實現
- [ ] ListService.RemoveMembersFromMarketingListAsync 實現
- [ ] 實現錯誤處理與重試機制
- [ ] 實現進度回報機制
- [ ] 效能測試驗證 (提升 5-10 倍)
- [ ] 負載測試通過 (100 並發)

### Phase 2.3: Controller 非同步化
- [ ] AuthenticationController 所有 Action 檢查完成
- [ ] SmallGroupController 改為非同步
- [ ] DedicationController 改為非同步
- [ ] PersonalController 改為非同步
- [ ] ListManagementController 改為非同步
- [ ] AppointmentController 改為非同步
- [ ] 所有 Controller 編譯無錯誤

### Phase 2.4: 測試與驗證
- [ ] 單元測試全部通過
- [ ] 整合測試全部通過
- [ ] 效能測試達標 (響應時間 < 1秒)
- [ ] 負載測試達標 (100 req/s)
- [ ] 記憶體無洩漏
- [ ] 無死鎖或執行緒阻塞

---

## 🎯 預期效果

| 指標 | 優化前 | 優化後 | 改善幅度 |
|-----|--------|--------|---------|
| 查詢響應時間 | 2-3秒 | <1秒 | **60-70%** ↓ |
| 批量操作速度 | 1000筆/10分鐘 | 1000筆/1分鐘 | **1000%** ↑ |
| 並發處理能力 | 20 req/s | 60-100 req/s | **300-400%** ↑ |
| CPU 使用率 | 60-80% | 30-50% | **30-50%** ↓ |
| 執行緒阻塞 | 頻繁 | 極少 | **90%** ↓ |

---

## 🛡️ 風險評估與緩解

### 風險 1: async/await 死鎖
**症狀**: 應用程式掛起，無響應  
**原因**: 在非同步方法中使用 `.Result` 或 `.Wait()`  
**緩解策略**:
- ✅ 全面使用 `async/await`
- ✅ 使用 `ConfigureAwait(false)`
- ✅ 避免混用同步/非同步代碼

### 風險 2: 並行過度導致 CRM 服務過載
**症狀**: CRM 查詢失敗，連接超時  
**原因**: 並行度設置過高  
**緩解策略**:
- ✅ 設定 `maxConcurrency = 5`
- ✅ 使用 `SemaphoreSlim` 控制並發
- ✅ 監控 CRM 伺服器負載

### 風險 3: CancellationToken 未正確處理
**症狀**: 取消操作後仍繼續執行  
**原因**: 沒有檢查 `cancellationToken.IsCancellationRequested`  
**緩解策略**:
- ✅ 所有非同步方法接受 `CancellationToken`
- ✅ 在長時間操作中定期檢查
- ✅ 使用 `ThrowIfCancellationRequested()`

---

## 📚 參考資料

- [Async/Await Best Practices](https://docs.microsoft.com/en-us/archive/msdn-magazine/2013/march/async-await-best-practices-in-asynchronous-programming)
- [Task Parallel Library (TPL)](https://docs.microsoft.com/en-us/dotnet/standard/parallel-programming/task-parallel-library-tpl)
- [SemaphoreSlim Class](https://docs.microsoft.com/en-us/dotnet/api/system.threading.semaphoreslim)
- [ConfigureAwait FAQ](https://devblogs.microsoft.com/dotnet/configureawait-faq/)

---

**文件版本**: v1.0  
**建立日期**: 2024-11-26  
**狀態**: 📝 規劃中  
**負責人**: 開發團隊  
**預計完成時間**: Week 3-4


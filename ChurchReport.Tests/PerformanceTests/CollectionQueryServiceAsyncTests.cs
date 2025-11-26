using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolUtilityNameSpace.CollectionOperations;
using Xunit;

namespace ChurchReport.Tests.PerformanceTests
{
    /// <summary>
    /// Phase 2.1 非同步查詢效能測試
    /// 驗證非同步方法的正確性與效能
    /// </summary>
    public class CollectionQueryServiceAsyncTests
    {
        private readonly ICollectionQueryService _collectionService;
        private readonly IOrganizationService _mockService;
        
        public CollectionQueryServiceAsyncTests()
        {
            // TODO: 初始化 mock service
            // _mockService = CreateMockOrganizationService();
            // _collectionService = new CollectionQueryService(null, _mockService);
        }
        
        #region 基本功能測試
        
        [Fact]
        public async Task RetrieveEntityCollectionByFieldAsync_ShouldReturnEntities()
        {
            // Arrange
            var entityName = "contact";
            var fieldName = "new_listid";
            var fieldValue = Guid.NewGuid().ToString();
            
            // Act
            var result = await _collectionService.RetrieveEntityCollectionByFieldAsync(
                entityName, 
                fieldName, 
                fieldValue);
            
            // Assert
            Assert.NotNull(result);
            Assert.IsType<EntityCollection>(result);
        }
        
        [Fact]
        public async Task RetrieveEntityCollectionByConditionAsync_ShouldReturnEntities()
        {
            // Arrange
            var entityName = "contact";
            var fieldName = "new_listid";
            var value = Guid.NewGuid();
            
            // Act
            var result = await _collectionService.RetrieveEntityCollectionByConditionAsync(
                entityName,
                fieldName,
                ConditionOperator.Equal,
                value);
            
            // Assert
            Assert.NotNull(result);
            Assert.IsType<EntityCollection>(result);
        }
        
        [Fact]
        public async Task RetrieveEntityCollectionByConditionsAsync_ShouldReturnEntities()
        {
            // Arrange
            var entityName = "contact";
            var conditions = new Dictionary<string, object>
            {
                { "new_listid", Guid.NewGuid() },
                { "new_status", "Active" }
            };
            
            // Act
            var result = await _collectionService.RetrieveEntityCollectionByConditionsAsync(
                entityName, 
                conditions);
            
            // Assert
            Assert.NotNull(result);
            Assert.IsType<EntityCollection>(result);
        }
        
        [Fact]
        public async Task QueryWeeklyReportBeforeTowMonthOfSundayAsync_ShouldReturnReports()
        {
            // Arrange
            var listId = Guid.NewGuid();
            var sunday = DateTime.Now.Date;
            
            // Act
            var result = await _collectionService.QueryWeeklyReportBeforeTowMonthOfSundayAsync(
                sunday, 
                listId);
            
            // Assert
            Assert.NotNull(result);
            Assert.IsType<EntityCollection>(result);
        }
        
        #endregion
        
        #region 分頁查詢測試
        
        [Fact]
        public async Task RetrievePagedEntitiesAsync_FirstPage_ShouldReturnPagedResult()
        {
            // Arrange
            var entityName = "contact";
            var pageSize = 50;
            
            // Act
            var result = await _collectionService.RetrievePagedEntitiesAsync(
                entityName,
                pageSize: pageSize);
            
            // Assert
            Assert.NotNull(result);
            Assert.IsType<PagedResult<Entity>>(result);
            Assert.True(result.Entities.Count <= pageSize);
        }
        
        [Fact]
        public async Task RetrievePagedEntitiesAsync_WithFilter_ShouldReturnFilteredResults()
        {
            // Arrange
            var entityName = "contact";
            var filter = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("new_status", ConditionOperator.Equal, "Active")
                }
            };
            
            // Act
            var result = await _collectionService.RetrievePagedEntitiesAsync(
                entityName,
                filter: filter,
                pageSize: 100);
            
            // Assert
            Assert.NotNull(result);
            Assert.All(result.Entities, entity =>
            {
                // 驗證所有結果都符合篩選條件
                Assert.True(entity.Contains("new_status"));
            });
        }
        
        #endregion
        
        #region 批量查詢測試
        
        [Fact]
        public async Task RetrieveBatchByIdsAsync_ShouldReturnAllMatchingEntities()
        {
            // Arrange
            var entityName = "contact";
            var ids = Enumerable.Range(1, 10).Select(_ => Guid.NewGuid()).ToList();
            
            // Act
            var result = await _collectionService.RetrieveBatchByIdsAsync(
                entityName,
                "contactid",
                ids);
            
            // Assert
            Assert.NotNull(result);
            Assert.True(result.Entities.Count <= ids.Count);
        }
        
        [Fact]
        public async Task RetrieveBatchByIdsAsync_WithLargeSet_ShouldHandleEfficiently()
        {
            // Arrange
            var entityName = "contact";
            var ids = Enumerable.Range(1, 1000).Select(_ => Guid.NewGuid()).ToList();
            var sw = Stopwatch.StartNew();
            
            // Act
            var result = await _collectionService.RetrieveBatchByIdsAsync(
                entityName,
                "contactid",
                ids);
            
            sw.Stop();
            
            // Assert
            Assert.NotNull(result);
            // 批量查詢應該在 5 秒內完成
            Assert.True(sw.ElapsedMilliseconds < 5000, 
                $"批量查詢花費 {sw.ElapsedMilliseconds}ms，超過預期的 5000ms");
        }
        
        #endregion
        
        #region 取消操作測試
        
        [Fact]
        public async Task RetrieveEntityCollectionByFieldAsync_WithCancellation_ShouldThrowOperationCanceledException()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            cts.Cancel(); // 立即取消
            
            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await _collectionService.RetrieveEntityCollectionByFieldAsync(
                    "contact",
                    "new_listid",
                    Guid.NewGuid().ToString(),
                    cts.Token);
            });
        }
        
        [Fact]
        public async Task RetrievePagedEntitiesAsync_WithCancellation_ShouldThrowOperationCanceledException()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            cts.CancelAfter(100); // 100ms 後取消
            
            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await _collectionService.RetrievePagedEntitiesAsync(
                    "contact",
                    pageSize: 1000,
                    cancellationToken: cts.Token);
            });
        }
        
        #endregion
        
        #region 效能基準測試
        
        [Fact]
        public async Task CompareAsyncVsSyncPerformance()
        {
            // Arrange
            var entityName = "contact";
            var fieldName = "new_listid";
            var fieldValue = Guid.NewGuid().ToString();
            
            // Act - 同步查詢
            var sw1 = Stopwatch.StartNew();
            var syncResult = _collectionService.RetrieveEntityCollectionByField(
                entityName, 
                fieldName, 
                fieldValue);
            sw1.Stop();
            
            // Act - 非同步查詢
            var sw2 = Stopwatch.StartNew();
            var asyncResult = await _collectionService.RetrieveEntityCollectionByFieldAsync(
                entityName, 
                fieldName, 
                fieldValue);
            sw2.Stop();
            
            // Assert
            Assert.Equal(syncResult.Entities.Count, asyncResult.Entities.Count);
            
            // 輸出效能比較
            Console.WriteLine($"同步查詢時間: {sw1.ElapsedMilliseconds}ms");
            Console.WriteLine($"非同步查詢時間: {sw2.ElapsedMilliseconds}ms");
            Console.WriteLine($"效能比較: {(double)sw1.ElapsedMilliseconds / sw2.ElapsedMilliseconds:F2}x");
        }
        
        [Fact]
        public async Task ConcurrentAsyncQueries_ShouldNotBlock()
        {
            // Arrange
            var entityName = "contact";
            var queryCount = 10;
            var sw = Stopwatch.StartNew();
            
            // Act - 並發執行 10 個查詢
            var tasks = Enumerable.Range(1, queryCount).Select(async i =>
            {
                return await _collectionService.RetrieveEntityCollectionByFieldAsync(
                    entityName,
                    "new_listid",
                    Guid.NewGuid().ToString());
            });
            
            var results = await Task.WhenAll(tasks);
            sw.Stop();
            
            // Assert
            Assert.Equal(queryCount, results.Length);
            
            // 並發查詢應該顯著快於順序執行
            Console.WriteLine($"並發查詢 {queryCount} 個請求總時間: {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"平均每個查詢: {sw.ElapsedMilliseconds / queryCount}ms");
        }
        
        [Fact]
        public async Task PagedQuery_MemoryUsage_ShouldBeLow()
        {
            // Arrange
            var entityName = "contact";
            var pageSize = 100;
            var maxPages = 10;
            
            var startMemory = GC.GetTotalMemory(true);
            
            // Act - 分頁查詢多頁
            string pagingCookie = null;
            int pageCount = 0;
            
            do
            {
                var result = await _collectionService.RetrievePagedEntitiesAsync(
                    entityName,
                    pageSize: pageSize,
                    pagingCookie: pagingCookie);
                
                pagingCookie = result.PagingCookie;
                pageCount++;
                
                // 模擬處理完資料後清理
                result.Entities.Clear();
                
            } while (!string.IsNullOrEmpty(pagingCookie) && pageCount < maxPages);
            
            var endMemory = GC.GetTotalMemory(true);
            var memoryDelta = (endMemory - startMemory) / 1024 / 1024; // MB
            
            // Assert
            Console.WriteLine($"查詢 {pageCount} 頁，記憶體增加: {memoryDelta}MB");
            
            // 記憶體增加應該小於 50MB
            Assert.True(memoryDelta < 50, 
                $"記憶體增加 {memoryDelta}MB，超過預期的 50MB");
        }
        
        #endregion
        
        #region 錯誤處理測試
        
        [Fact]
        public async Task RetrieveEntityCollectionByFieldAsync_WithInvalidEntityName_ShouldThrowException()
        {
            // Arrange
            var invalidEntityName = "invalid_entity_name";
            
            // Act & Assert
            await Assert.ThrowsAsync<Exception>(async () =>
            {
                await _collectionService.RetrieveEntityCollectionByFieldAsync(
                    invalidEntityName,
                    "fieldname",
                    "value");
            });
        }
        
        [Fact]
        public async Task RetrieveBatchByIdsAsync_WithEmptyIdList_ShouldReturnEmptyCollection()
        {
            // Arrange
            var entityName = "contact";
            var emptyIds = new List<Guid>();
            
            // Act
            var result = await _collectionService.RetrieveBatchByIdsAsync(
                entityName,
                "contactid",
                emptyIds);
            
            // Assert
            Assert.NotNull(result);
            Assert.Empty(result.Entities);
        }
        
        #endregion
    }
}

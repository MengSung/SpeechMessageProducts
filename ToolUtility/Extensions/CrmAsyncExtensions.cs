using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ToolUtilityNameSpace.Extensions
{
    /// <summary>
    /// CRM 服務的非同步擴充方法
    /// Phase 3: 提供非同步 CRM 查詢操作，避免執行緒阻塞
    /// </summary>
    public static class CrmAsyncExtensions
    {
        #region 非同步查詢方法

        /// <summary>
        /// 非同步擷取單一實體
        /// </summary>
        /// <param name="service">CRM 服務</param>
        /// <param name="entityName">實體名稱</param>
        /// <param name="id">實體 ID</param>
        /// <param name="columnSet">欄位集合</param>
        /// <param name="cancellationToken">取消權杖</param>
        /// <returns>實體</returns>
        public static async Task<Entity> RetrieveAsync(
            this IOrganizationService service,
            string entityName,
            Guid id,
            ColumnSet columnSet,
            CancellationToken cancellationToken = default)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (string.IsNullOrEmpty(entityName)) throw new ArgumentNullException(nameof(entityName));

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return service.Retrieve(entityName, id, columnSet);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 非同步查詢多筆實體
        /// </summary>
        /// <param name="service">CRM 服務</param>
        /// <param name="query">查詢表達式</param>
        /// <param name="cancellationToken">取消權杖</param>
        /// <returns>實體集合</returns>
        public static async Task<EntityCollection> RetrieveMultipleAsync(
            this IOrganizationService service,
            QueryBase query,
            CancellationToken cancellationToken = default)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (query == null) throw new ArgumentNullException(nameof(query));

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return service.RetrieveMultiple(query);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 非同步建立實體
        /// </summary>
        /// <param name="service">CRM 服務</param>
        /// <param name="entity">要建立的實體</param>
        /// <param name="cancellationToken">取消權杖</param>
        /// <returns>建立的實體 ID</returns>
        public static async Task<Guid> CreateAsync(
            this IOrganizationService service,
            Entity entity,
            CancellationToken cancellationToken = default)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return service.Create(entity);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 非同步更新實體
        /// </summary>
        /// <param name="service">CRM 服務</param>
        /// <param name="entity">要更新的實體</param>
        /// <param name="cancellationToken">取消權杖</param>
        public static async Task UpdateAsync(
            this IOrganizationService service,
            Entity entity,
            CancellationToken cancellationToken = default)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                service.Update(entity);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 非同步刪除實體
        /// </summary>
        /// <param name="service">CRM 服務</param>
        /// <param name="entityName">實體名稱</param>
        /// <param name="id">實體 ID</param>
        /// <param name="cancellationToken">取消權杖</param>
        public static async Task DeleteAsync(
            this IOrganizationService service,
            string entityName,
            Guid id,
            CancellationToken cancellationToken = default)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (string.IsNullOrEmpty(entityName)) throw new ArgumentNullException(nameof(entityName));

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                service.Delete(entityName, id);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 非同步執行 CRM 請求
        /// </summary>
        /// <param name="service">CRM 服務</param>
        /// <param name="request">請求物件</param>
        /// <param name="cancellationToken">取消權杖</param>
        /// <returns>回應物件</returns>
        public static async Task<OrganizationResponse> ExecuteAsync(
            this IOrganizationService service,
            OrganizationRequest request,
            CancellationToken cancellationToken = default)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (request == null) throw new ArgumentNullException(nameof(request));

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return service.Execute(request);
            }, cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region 批次查詢方法

        /// <summary>
        /// 批次擷取多個實體（解決 N+1 查詢問題）
        /// </summary>
        /// <param name="service">CRM 服務</param>
        /// <param name="entityName">實體名稱</param>
        /// <param name="ids">實體 ID 集合</param>
        /// <param name="columnSet">欄位集合</param>
        /// <param name="cancellationToken">取消權杖</param>
        /// <returns>實體字典 (ID -> Entity)</returns>
        public static async Task<Dictionary<Guid, Entity>> BatchRetrieveAsync(
            this IOrganizationService service,
            string entityName,
            IEnumerable<Guid> ids,
            ColumnSet columnSet,
            CancellationToken cancellationToken = default)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (string.IsNullOrEmpty(entityName)) throw new ArgumentNullException(nameof(entityName));
            if (ids == null) throw new ArgumentNullException(nameof(ids));

            var idList = ids.Distinct().ToList();
            if (!idList.Any())
                return new Dictionary<Guid, Entity>();

            // 取得主鍵欄位名稱
            var primaryKey = $"{entityName}id";

            // 建立批次查詢
            var query = new QueryExpression(entityName)
            {
                ColumnSet = columnSet,
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression(primaryKey, ConditionOperator.In, idList.Cast<object>().ToArray())
                    }
                }
            };

            var results = await service.RetrieveMultipleAsync(query, cancellationToken);
            return results.Entities.ToDictionary(e => e.Id);
        }

        /// <summary>
        /// 批次擷取關聯的 Contact 實體
        /// </summary>
        /// <param name="service">CRM 服務</param>
        /// <param name="contactIds">聯絡人 ID 集合</param>
        /// <param name="columnSet">欄位集合（預設為全部欄位）</param>
        /// <param name="cancellationToken">取消權杖</param>
        /// <returns>聯絡人字典 (ID -> Entity)</returns>
        public static async Task<Dictionary<Guid, Entity>> BatchRetrieveContactsAsync(
            this IOrganizationService service,
            IEnumerable<Guid> contactIds,
            ColumnSet columnSet = null,
            CancellationToken cancellationToken = default)
        {
            return await service.BatchRetrieveAsync(
                "contact",
                contactIds,
                columnSet ?? new ColumnSet(true),
                cancellationToken);
        }

        #endregion

        #region 分頁查詢方法

        /// <summary>
        /// 非同步分頁查詢所有實體（自動處理分頁）
        /// </summary>
        /// <param name="service">CRM 服務</param>
        /// <param name="query">查詢表達式</param>
        /// <param name="pageSize">每頁筆數（預設 5000）</param>
        /// <param name="cancellationToken">取消權杖</param>
        /// <returns>所有實體集合</returns>
        public static async Task<List<Entity>> RetrieveAllAsync(
            this IOrganizationService service,
            QueryExpression query,
            int pageSize = 5000,
            CancellationToken cancellationToken = default)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (query == null) throw new ArgumentNullException(nameof(query));

            var allEntities = new List<Entity>();
            query.PageInfo = new PagingInfo
            {
                Count = pageSize,
                PageNumber = 1,
                PagingCookie = null
            };

            EntityCollection results;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                results = await service.RetrieveMultipleAsync(query, cancellationToken);
                allEntities.AddRange(results.Entities);

                if (results.MoreRecords)
                {
                    query.PageInfo.PageNumber++;
                    query.PageInfo.PagingCookie = results.PagingCookie;
                }
            }
            while (results.MoreRecords);

            return allEntities;
        }

        #endregion

        #region 並行查詢方法

        /// <summary>
        /// 並行擷取多個不同的實體
        /// </summary>
        /// <param name="service">CRM 服務</param>
        /// <param name="requests">查詢請求集合 (實體名稱, ID)</param>
        /// <param name="columnSet">欄位集合</param>
        /// <param name="maxDegreeOfParallelism">最大並行度（預設 4）</param>
        /// <param name="cancellationToken">取消權杖</param>
        /// <returns>實體字典 (ID -> Entity)</returns>
        public static async Task<Dictionary<Guid, Entity>> ParallelRetrieveAsync(
            this IOrganizationService service,
            IEnumerable<(string EntityName, Guid Id)> requests,
            ColumnSet columnSet,
            int maxDegreeOfParallelism = 4,
            CancellationToken cancellationToken = default)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (requests == null) throw new ArgumentNullException(nameof(requests));

            var requestList = requests.ToList();
            var results = new Dictionary<Guid, Entity>();
            var semaphore = new SemaphoreSlim(maxDegreeOfParallelism);

            var tasks = requestList.Select(async req =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var entity = await service.RetrieveAsync(
                        req.EntityName,
                        req.Id,
                        columnSet,
                        cancellationToken);

                    lock (results)
                    {
                        results[req.Id] = entity;
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            return results;
        }

        #endregion
    }
}

/*
 * 檔案整體說明
 *
 * 目的與定位：純SDK Task轉接：同步單次I/O不額外排程，明確Parallel入口最多64個worker，單次輸入／完整結果最多5000列。 SDK只借入已授權scope，host先確認連線可並行且擁有timeout；
 * 本類無static可變狀態、cache、credential或結果owner。
 *
 * 主要功能與流程：
 * 本檔宣告 CrmAsyncExtensions。
 * - 單筆查詢委派唯一Core身份驗證，保留原ColumnSet；Task不另起背景工作。
 * - 保留Query原實例與單頁metadata，委派Core驗根表／正式ID後核5000列；不冒稱完整全表。
 * - 單次建立確認非空資料庫ID，故障及不確定寫入不自動重試；SDK不轉移owner。
 * - 只更新具有正式非空ID的操作局部記錄；caller取消在寫入後不抹除故障或暗中重試。
 * 主要操作入口包含 RetrieveAsync、RetrieveMultipleAsync、CreateAsync、UpdateAsync、DeleteAsync、ExecuteAsync、
 * BatchRetrieveAsync、BatchRetrieveContactsAsync；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.DataOperations;
using ToolUtilityNameSpace.EntityOperations;

namespace ToolUtilityNameSpace.Extensions
{
    /// <summary>
    /// 純SDK Task轉接：同步單次I/O不額外排程，明確Parallel入口最多64個worker，單次輸入／完整結果最多5000列。
    /// SDK只借入已授權scope，host先確認連線可並行且擁有timeout；本類無static可變狀態、cache、credential或結果owner。
    /// </summary>
    /// <remarks>
    /// caller取消在I/O前後檢查；內部timeout保持fault，不能因Exception帶有取消token便視為正常取消。
    /// 同步SDK一旦入場不能被中斷，所有worker必定WhenAll排空才回傳或失敗；不留下fire-and-forget／SemaphoreSlim。
    /// 先在局部候選完成所有頁及身份驗證再發布；不以名稱去重、不靜默覆寫相同Guid，不處置借入SDK。
    /// </remarks>
    public static class CrmAsyncExtensions
    {
        private const int MaximumRows = 5000;
        private const int MaximumPages = 1000;
        private const int MaximumCookieCharacters = 65536;

        /// <summary>單筆查詢委派唯一Core身份驗證，保留原ColumnSet；Task不另起背景工作。</summary>
        /// <param name="service">已授權且host擁有的SDK。</param>
        /// <param name="entityName">可信logical name，不作權限。</param>
        /// <param name="id">正式非空資料庫ID。</param>
        /// <param name="columnSet">操作期間caller獨占的欄位。</param>
        /// <param name="cancellationToken">只由caller提供的取消來源。</param>
        public static Task<Entity> RetrieveAsync(this IOrganizationService service, string entityName, Guid id,
            ColumnSet columnSet, CancellationToken cancellationToken = default) => Run(() =>
        {
            using var reader = new EntityQueryService(organizationService: SdkDataQuery.RequireService(service));
            return reader.RetrieveEntity(entityName, id, columnSet);
        }, cancellationToken);

        /// <summary>保留Query原實例與單頁metadata，委派Core驗根表／正式ID後核5000列；不冒稱完整全表。</summary>
        public static Task<EntityCollection> RetrieveMultipleAsync(this IOrganizationService service, QueryBase query,
            CancellationToken cancellationToken = default) => Run(() =>
        {
            using var reader = new EntityQueryService(organizationService: SdkDataQuery.RequireService(service));
            var page = reader.RetrieveMultiple(query);
            if (page.Entities.Count > MaximumRows) throw new InvalidOperationException("Query exceeded the local row budget.");
            return page;
        }, cancellationToken);

        /// <summary>單次建立確認非空資料庫ID，故障及不確定寫入不自動重試；SDK不轉移owner。</summary>
        public static Task<Guid> CreateAsync(this IOrganizationService service, Entity entity,
            CancellationToken cancellationToken = default) => Run(() =>
        {
            var sdk = SdkDataQuery.RequireService(service);
            RequireEntity(entity);
            var id = sdk.Create(entity);
            if (id == Guid.Empty) throw new InvalidOperationException("The SDK returned an empty database identity.");
            return id;
        }, cancellationToken);

        /// <summary>只更新具有正式非空ID的操作局部記錄；caller取消在寫入後不抹除故障或暗中重試。</summary>
        public static Task UpdateAsync(this IOrganizationService service, Entity entity,
            CancellationToken cancellationToken = default) => Run(() =>
        {
            var sdk = SdkDataQuery.RequireService(service);
            RequireEntity(entity); RequireId(entity.Id, nameof(entity));
            sdk.Update(entity); return true;
        }, cancellationToken);

        /// <summary>精確刪除正式ID，不按顯示值推測identity；只執行一次並向host傳原例外。</summary>
        public static Task DeleteAsync(this IOrganizationService service, string entityName, Guid id,
            CancellationToken cancellationToken = default) => Run(() =>
        {
            var sdk = SdkDataQuery.RequireService(service);
            SdkDataQuery.RequireLogicalName(entityName, nameof(entityName)); RequireId(id, nameof(id));
            sdk.Delete(entityName, id); return true;
        }, cancellationToken);

        /// <summary>執行caller獨占的原請求；保持同一SDK回應，null回應明確失敗，不讀取或記錄request值。</summary>
        public static Task<OrganizationResponse> ExecuteAsync(this IOrganizationService service, OrganizationRequest request,
            CancellationToken cancellationToken = default) => Run(() =>
        {
            var sdk = SdkDataQuery.RequireService(service);
#if NETFRAMEWORK
            if (request == null) throw new ArgumentNullException(nameof(request));
#else
            ArgumentNullException.ThrowIfNull(request);
#endif
            return sdk.Execute(request) ?? throw new InvalidOperationException("The SDK returned no response.");
        }, cancellationToken);

        /// <summary>
        /// 保留IN Guid條件及無statecode差異；舊輸入重複ID代表同一讀取可去重，但最多列舉5000個輸入且不能含空ID。
        /// 任何回應ID必須在正式請求集合中；完整分頁成功才建立字典，回應重複ID不是可去重輸入。
        /// </summary>
        public static Task<Dictionary<Guid, Entity>> BatchRetrieveAsync(this IOrganizationService service, string entityName,
            IEnumerable<Guid> ids, ColumnSet columnSet, CancellationToken cancellationToken = default) => Run(() =>
        {
            SdkDataQuery.RequireService(service); SdkDataQuery.RequireLogicalName(entityName, nameof(entityName));
#if NETFRAMEWORK
            if (ids == null) throw new ArgumentNullException(nameof(ids));
#else
            ArgumentNullException.ThrowIfNull(ids);
#endif
            var requested = new HashSet<Guid>();
            var ordered = new List<Guid>();
            var inputs = 0;
            foreach (var id in ids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++inputs > MaximumRows) throw new ArgumentOutOfRangeException(nameof(ids), "Input exceeds the local row budget.");
                RequireId(id, nameof(ids));
                if (requested.Add(id)) ordered.Add(id);
            }
            if (ordered.Count == 0) return new Dictionary<Guid, Entity>();
            var query = new QueryExpression(entityName)
            {
                ColumnSet = columnSet,
                Criteria = new FilterExpression
                {
                    Conditions = { new ConditionExpression(entityName + "id", ConditionOperator.In, ordered.Cast<object>().ToArray()) }
                }
            };
            var rows = ReadComplete(service, query, MaximumRows, false, requested, cancellationToken);
            var result = new Dictionary<Guid, Entity>(rows.Count);
            foreach (var row in rows) result.Add(row.Id, row);
            return result;
        }, cancellationToken);

        /// <summary>聯絡人批次委派同一IN完整讀取；null欄位沿用舊AllColumns，不建立第二套快取或驗證。</summary>
        public static Task<Dictionary<Guid, Entity>> BatchRetrieveContactsAsync(this IOrganizationService service,
            IEnumerable<Guid> contactIds, ColumnSet? columnSet = null, CancellationToken cancellationToken = default) =>
            BatchRetrieveAsync(service, "contact", contactIds, columnSet ?? new ColumnSet(true), cancellationToken);

        /// <summary>
        /// 以原查詢樹完整分頁，caller必須在操作期間獨占Query；只替換操作局部PagingInfo，finally恢復原參照及原值。
        /// 頁大小1..5000、總列5000、總頁1000、cookie64Ki字元；跨頁衝突／後頁故障拒絕全部候選。
        /// </summary>
        public static Task<List<Entity>> RetrieveAllAsync(this IOrganizationService service, QueryExpression query,
            int pageSize = 5000, CancellationToken cancellationToken = default) => Run(() =>
        {
            SdkDataQuery.RequireService(service);
#if NETFRAMEWORK
            if (query == null) throw new ArgumentNullException(nameof(query));
#else
            ArgumentNullException.ThrowIfNull(query);
#endif
            return ReadComplete(service, query, pageSize, true, null, cancellationToken);
        }, cancellationToken);

        /// <summary>
        /// 明確並行入口先有界列舉且驗所有結構/ID，字典只支援Guid唯一，因此跨表相同Guid也必須在I/O前拒絕。
        /// 最多64個固定worker，無每列Task或Semaphore；全部worker排空後才發布，故障不會留下其他SDK工作繼續使用已釋放scope。
        /// </summary>
        public static async Task<Dictionary<Guid, Entity>> ParallelRetrieveAsync(this IOrganizationService service,
            IEnumerable<(string EntityName, Guid Id)> requests, ColumnSet columnSet,
            int maxDegreeOfParallelism = 4, CancellationToken cancellationToken = default)
        {
            SdkDataQuery.RequireService(service);
            if (maxDegreeOfParallelism < 1 || maxDegreeOfParallelism > 64)
                throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism));
#if NETFRAMEWORK
            if (requests == null) throw new ArgumentNullException(nameof(requests));
#else
            ArgumentNullException.ThrowIfNull(requests);
#endif
            var items = new List<(string EntityName, Guid Id)>();
            var identities = new HashSet<Guid>();
            foreach (var item in requests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (items.Count == MaximumRows) throw new ArgumentOutOfRangeException(nameof(requests), "Input exceeds the local row budget.");
                SdkDataQuery.RequireLogicalName(item.EntityName, nameof(requests)); RequireId(item.Id, nameof(requests));
                if (!identities.Add(item.Id)) throw new ArgumentException("Duplicate requested identity.", nameof(requests));
                items.Add(item);
            }
            var candidate = new Entity[items.Count];
            var cursor = -1;
            var failed = 0;
            var workers = new Task[Math.Min(maxDegreeOfParallelism, items.Count)];
            for (var worker = 0; worker < workers.Length; worker++) workers[worker] = Task.Run(() =>
            {
                using var reader = new EntityQueryService(organizationService: service);
                try
                {
                    while (Volatile.Read(ref failed) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var index = Interlocked.Increment(ref cursor);
                        if (index >= items.Count) break;
                        var item = items[index];
                        candidate[index] = reader.RetrieveEntity(item.EntityName, item.Id, columnSet);
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }
                catch (OperationCanceledException failure) when (!cancellationToken.IsCancellationRequested)
                {
                    Interlocked.Exchange(ref failed, 1);
                    throw new InvalidOperationException("The SDK transport was interrupted.", failure);
                }
                catch
                {
                    Interlocked.Exchange(ref failed, 1);
                    throw;
                }
            }, CancellationToken.None);
            try
            {
                await Task.WhenAll(workers).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var result = new Dictionary<Guid, Entity>(candidate.Length);
                foreach (var row in candidate) result.Add(row.Id, row);
                return result;
            }
            finally
            {
                // Task可能被caller長期保留；全部worker已drain才清closure中的借入SDK、輸入及token，
                // 不讓已完成Task成為request scope的額外owner。只有正式回傳字典擁有結果。
                service = null!;
                requests = Array.Empty<(string EntityName, Guid Id)>();
                columnSet = null!;
                cancellationToken = default;
                candidate = Array.Empty<Entity>();
                items.Clear();
                identities.Clear();
            }
        }

        /// <summary>候選最多5000列且每頁先驗budget／正式ID；分頁樹僅屬本次操作，finally保證還原caller的原PagingInfo。</summary>
        private static List<Entity> ReadComplete(IOrganizationService sdk, QueryExpression query, int pageSize,
            bool initializePaging, HashSet<Guid>? requested, CancellationToken token)
        {
            if (pageSize < 1 || pageSize > MaximumRows) throw new ArgumentOutOfRangeException(nameof(pageSize));
            SdkDataQuery.RequireLogicalName(query.EntityName, nameof(query));
            var original = query.PageInfo;
            var rows = new List<Entity>();
            var seen = new HashSet<Guid>();
            var cookies = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                if (initializePaging) query.PageInfo = new PagingInfo { Count = pageSize, PageNumber = 1 };
                for (var page = 1; page <= MaximumPages; page++)
                {
                    token.ThrowIfCancellationRequested();
                    var response = SdkDataQuery.ValidatePage(sdk.RetrieveMultiple(query), query.EntityName, pageSize);
                    token.ThrowIfCancellationRequested();
                    if (response.Entities.Count > MaximumRows - rows.Count)
                        throw new InvalidOperationException("Query exceeded the local row budget.");
                    foreach (var row in response.Entities)
                    {
                        if (!seen.Add(row.Id) || (requested != null && !requested.Contains(row.Id)))
                            throw new InvalidOperationException("Query returned duplicate or unrequested database identities.");
                        rows.Add(row);
                    }
                    if (!response.MoreRecords) return rows;
                    var cookie = response.PagingCookie;
                    if (cookie != null && (cookie.Length > MaximumCookieCharacters || (cookie.Length != 0 && !cookies.Add(cookie))))
                        throw new InvalidOperationException("The paging continuation is invalid.");
                    query.PageInfo = new PagingInfo { Count = pageSize, PageNumber = page + 1, PagingCookie = cookie };
                }
                throw new InvalidOperationException("Query exceeded the local page budget.");
            }
            finally { query.PageInfo = original; }
        }

        /// <summary>Task同步包裝由caller前後token決定正常取消；SDK內部取消保持原fault，operation委派只活至Task建立完畢。</summary>
        private static Task<T> Run<T>(Func<T> operation, CancellationToken token)
        {
            if (token.IsCancellationRequested) return Task.FromCanceled<T>(token);
            try { var result = operation(); token.ThrowIfCancellationRequested(); return Task.FromResult(result); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return Task.FromCanceled<T>(token); }
            catch (Exception failure) { return Task.FromException<T>(failure); }
        }

        /// <summary>只驗結構及非null候選；Create允許空ID由資料庫產生，Update另外要求正式ID。</summary>
        private static void RequireEntity(Entity entity)
        {
#if NETFRAMEWORK
            if (entity == null) throw new ArgumentNullException(nameof(entity));
#else
            ArgumentNullException.ThrowIfNull(entity);
#endif
            SdkDataQuery.RequireLogicalName(entity.LogicalName, nameof(entity));
        }

        /// <summary>正式ID不能用零值代替，錯誤不回顯輸入或使用者資料。</summary>
        private static void RequireId(Guid id, string parameter)
        {
            if (id == Guid.Empty) throw new ArgumentException("A database identifier is required.", parameter);
        }
    }
}

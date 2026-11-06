/*
 * 檔案整體說明
 *
 * 目的與定位：保留原Repository讀取與SDK寫入路徑的純行銷名單服務；不保存查詢結果或request，也不輸出成員ID／例外本文。 host先驗完整subject／organization／教會及名單授權，
 * 再借入同scope依賴；格式正確的ID不構成權限。 完整投影及寫入最多5000列，單頁最多5000列；候選完整驗證才發布，不按名稱合併或跳漏不合法成員。
 *
 * 主要功能與流程：
 * 本檔宣告 MarketingListService。
 * - 借用同一已授權scope的repository與SDK；不接管連線、logger或Dispose所有權。
 * - 保持AllColumns、listid條件與repository路徑；整頁先驗，保留舊失敗包裝層及原InnerException，不洩漏原本文。
 * - 完整讀完同repository各頁後才投影entityid；保留Guid並接受SDK正式EntityReference，缺／空／重複成員拒絕。
 * - 保留AddMemberListRequest原生關聯加入，正式名單／成員ID先驗，真正故障向host傳遞原Exception。
 * 主要操作入口包含 RetrieveMembersByListId、GetAllMemberData、AddMember、AddMembersAsync、AddMembersUsingSdkAsync、
 * RemoveMember、RemoveMembersAsync、MemberQuery；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.DataOperations;
using ToolUtilityNameSpace.EntityOperations;

namespace ToolUtilityNameSpace.ListOperations
{
    /// <summary>
    /// 保留原Repository讀取與SDK寫入路徑的純行銷名單服務；不保存查詢結果或request，也不輸出成員ID／例外本文。
    /// host先驗完整subject／organization／教會及名單授權，再借入同scope依賴；格式正確的ID不構成權限。
    /// 完整投影及寫入最多5000列，單頁最多5000列；候選完整驗證才發布，不按名稱合併或跳漏不合法成員。
    /// </summary>
    /// <remarks>
    /// 批次由唯一有界owner串行執行，所有同步SDK呼叫排空才完成Task；不用逐列Task.Run或吞錯回部分成功。
    /// Dispose清兩個借用參照並拒新入口；不處置repository／SDK。host須await/drain後才關連線。
    /// 原生名單成員訊息維持資料庫關聯唯一性，沒有應用層鎖或自動重試；失敗／取消不回復已提交批次。
    /// </remarks>
    public class MarketingListService : IMarketingListService, IDisposable
    {
        private IEntityRepository? _repository;
        private IOrganizationService? _organizationService;
        private int _disposed;

        /// <summary>借用同一已授權scope的repository與SDK；不接管連線、logger或Dispose所有權。</summary>
        /// <param name="repository">host擁有的資料讀取邊界，不可跨不相容scope共用。</param>
        /// <param name="organizationService">host擁有且已配置timeout的SDK；與repository必須在同一授權範圍。</param>
        public MarketingListService(IEntityRepository repository, IOrganizationService organizationService)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _organizationService = SdkDataQuery.RequireService(organizationService);
        }

        /// <summary>保持AllColumns、listid條件與repository路徑；整頁先驗，保留舊失敗包裝層及原InnerException，不洩漏原本文。</summary>
        public EntityCollection RetrieveMembersByListId(Guid listId)
        {
            var repository = GetRepository();
            try
            {
                var query = MemberQuery(listId);
                return SdkDataQuery.ValidatePage(repository.RetrieveMultiple(query), "listmember", 5000);
            }
            catch (Exception failure)
            {
                throw new InvalidOperationException("The marketing list query failed.", failure);
            }
        }

        /// <summary>完整讀完同repository各頁後才投影entityid；保留Guid並接受SDK正式EntityReference，缺／空／重複成員拒絕。</summary>
        public ArrayList GetAllMemberData(Guid listId)
        {
            var repository = GetRepository();
            var rows = ListMembershipOperations.ReadComplete(repository.RetrieveMultiple, MemberQuery(listId), "listmember");
            return ListMembershipOperations.ProjectMembers(rows, false);
        }

        /// <summary>保留AddMemberListRequest原生關聯加入，正式名單／成員ID先驗，真正故障向host傳遞原Exception。</summary>
        public void AddMember(Guid listGuid, Guid memberGuid)
        {
            var sdk = GetService(); ListMembershipOperations.RequireId(listGuid, nameof(listGuid));
            ListMembershipOperations.RequireId(memberGuid, nameof(memberGuid));
            sdk.Execute(new AddMemberListRequest { ListId = listGuid, EntityId = memberGuid });
        }

        /// <summary>維持每位成員的AddMemberListRequest路徑及預設50；一個串行owner避免同SDK無證明並行，失敗不回部分成功。</summary>
        public Task<int> AddMembersAsync(Guid listGuid, List<Guid> memberGuidList, int batchSize = 50, CancellationToken cancellationToken = default) =>
            ListMembershipOperations.Start(GetService(), listGuid, memberGuidList, batchSize, MembershipWrite.AddSingle, 0, cancellationToken);

        /// <summary>保留借入外部SDK及1000人預設，改用同一有界批次owner；caller取消只停後續工作，已入場必drain。</summary>
        public Task<int> AddMembersUsingSdkAsync(Guid listGuid, List<Guid> memberGuidList, IOrganizationService service,
            int maxBatchSize = 1000, CancellationToken cancellationToken = default)
        {
            GetService(); return ListMembershipOperations.Start(SdkDataQuery.RequireService(service), listGuid, memberGuidList,
                maxBatchSize, MembershipWrite.AddBulk, 0, cancellationToken);
        }

        /// <summary>保留RemoveMemberListRequest原生關聯移除；不用姓名、查詢再刪除或隱藏retry推定成功。</summary>
        public void RemoveMember(Guid listGuid, Guid memberGuid)
        {
            var sdk = GetService(); ListMembershipOperations.RequireId(listGuid, nameof(listGuid));
            ListMembershipOperations.RequireId(memberGuid, nameof(memberGuid));
            sdk.Execute(new RemoveMemberListRequest { ListId = listGuid, EntityId = memberGuid });
        }

        /// <summary>保持原每位RemoveMemberListRequest訊息，單一owner串行排空；原例外保留且不再吞錯。</summary>
        public Task<int> RemoveMembersAsync(Guid listGuid, List<Guid> memberGuidList, int batchSize = 50, CancellationToken cancellationToken = default) =>
            ListMembershipOperations.Start(GetService(), listGuid, memberGuidList, batchSize, MembershipWrite.RemoveSingle, 0, cancellationToken);

        /// <summary>建立操作自有的原query，沒有額外statecode或新排序；非空正式ID不取代host授權。</summary>
        private static QueryExpression MemberQuery(Guid listId)
        {
            ListMembershipOperations.RequireId(listId, nameof(listId));
            var query = new QueryExpression("listmember") { ColumnSet = new ColumnSet(true) };
            query.Criteria.AddCondition("listid", ConditionOperator.Equal, listId); return query;
        }

        /// <summary>取得局部借用repository；Dispose拒新工作，不把ObjectDisposed包成假資料查無。</summary>
        private IEntityRepository GetRepository()
        {
            var repository = Volatile.Read(ref _repository); ThrowIfDisposed();
            return repository ?? throw new ObjectDisposedException(nameof(MarketingListService));
        }

        /// <summary>取得局部SDK供完整操作drain；不在Task closure保存服務本身。</summary>
        private IOrganizationService GetService()
        {
            var sdk = Volatile.Read(ref _organizationService); ThrowIfDisposed();
            return sdk ?? throw new ObjectDisposedException(nameof(MarketingListService));
        }

        /// <summary>統一新入口拒絕判斷；Framework與net10遵守同一生命週期且不降低strict分析器。</summary>
        private void ThrowIfDisposed()
        {
#if NETFRAMEWORK
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(MarketingListService));
#else
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
#endif
        }

        /// <summary>冪等解除兩個借用參照；不處置外部repository／SDK，已開始操作須由host排空。</summary>
        protected virtual void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            { Interlocked.Exchange(ref _repository, null); Interlocked.Exchange(ref _organizationService, null); }
        }

        /// <summary>停止新工作並釋放本物件的強參照；沒有cache、timer或終結器。</summary>
        public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
    }
}

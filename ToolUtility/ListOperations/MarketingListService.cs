// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/ListOperations/MarketingListService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：interface IMarketingListService、class MarketingListService
// 主要成員：RetrieveMembersByListId、GetAllMemberData、AddMember、AddMembersAsync、AddMembersUsingSdkAsync、RemoveMember、RemoveMembersAsync
// 引用命名空間：Microsoft.Crm.Sdk.Messages、Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Query、System、System.Collections、System.Collections.Generic、System.Diagnostics、System.Linq
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolUtilityNameSpace.EntityOperations;

namespace ToolUtilityNameSpace.ListOperations
{
    /// <summary>
    /// 行銷名單服務介面
    /// 遵循 Interface Segregation Principle (ISP)
    /// </summary>
    public interface IMarketingListService
    {
        /// <summary>檢索名單成員集合</summary>
        EntityCollection RetrieveMembersByListId(Guid listId);

        /// <summary>取得名單所有成員資料</summary>
        ArrayList GetAllMemberData(Guid listId);

        /// <summary>添加成員到名單</summary>
        void AddMember(Guid listGuid, Guid memberGuid);

        /// <summary>批次添加成員到名單 (非同步)</summary>
        Task<int> AddMembersAsync(Guid listGuid, List<Guid> memberGuidList, int batchSize = 50, CancellationToken cancellationToken = default);

        /// <summary>使用 SDK 批次添加成員 (最高效)</summary>
        Task<int> AddMembersUsingSdkAsync(Guid listGuid, List<Guid> memberGuidList, IOrganizationService service, int maxBatchSize = 1000, CancellationToken cancellationToken = default);

        /// <summary>移除名單成員</summary>
        void RemoveMember(Guid listGuid, Guid memberGuid);

        /// <summary>批次移除名單成員 (非同步)</summary>
        Task<int> RemoveMembersAsync(Guid listGuid, List<Guid> memberGuidList, int batchSize = 50, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 行銷名單服務實現
    /// 使用 Repository Pattern + Strategy Pattern 優化批次操作
    /// </summary>
    public class MarketingListService : IMarketingListService
    {
        private readonly IEntityRepository _repository;
        private readonly IOrganizationService _organizationService;

        /// <summary>
        /// 建構函數 - 注入依賴
        /// </summary>
        public MarketingListService(IEntityRepository repository, IOrganizationService organizationService)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        }

        /// <summary>
        /// 檢索名單成員集合
        /// </summary>
        public EntityCollection RetrieveMembersByListId(Guid listId)
        {
            try
            {
                if (listId == Guid.Empty)
                    throw new ArgumentException("List ID 不可為空", nameof(listId));

                Trace.WriteLine($"[MarketingListService] Retrieving members for list: {listId}");

                var query = new QueryExpression("listmember")
                {
                    ColumnSet = new ColumnSet(true),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("listid", ConditionOperator.Equal, listId)
                        }
                    }
                };

                var results = _repository.RetrieveMultiple(query);
                Trace.WriteLine($"[MarketingListService] Retrieved {results.Entities.Count} members");
                return results;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[MarketingListService] RetrieveMembersByListId failed: {ex.Message}");
                throw new InvalidOperationException("檢索名單成員失敗", ex);
            }
        }

        /// <summary>
        /// 取得名單所有成員資料
        /// </summary>
        public ArrayList GetAllMemberData(Guid listId)
        {
            try
            {
                var members = RetrieveMembersByListId(listId);
                var memberList = new ArrayList();

                foreach (var member in members.Entities)
                {
                    if (member.Contains("entityid") && member["entityid"] is Guid entityId)
                    {
                        memberList.Add(entityId);
                    }
                }

                return memberList;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[MarketingListService] GetAllMemberData failed: {ex.Message}");
                throw new InvalidOperationException("取得名單成員資料失敗", ex);
            }
        }

        /// <summary>
        /// 添加成員到名單
        /// </summary>
        public void AddMember(Guid listGuid, Guid memberGuid)
        {
            try
            {
                if (listGuid == Guid.Empty)
                    throw new ArgumentException("List GUID 不可為空", nameof(listGuid));

                if (memberGuid == Guid.Empty)
                    throw new ArgumentException("Member GUID 不可為空", nameof(memberGuid));

                Trace.WriteLine($"[MarketingListService] Adding member {memberGuid} to list {listGuid}");

                var request = new AddMemberListRequest
                {
                    ListId = listGuid,
                    EntityId = memberGuid
                };

                _organizationService.Execute(request);
                Trace.WriteLine($"[MarketingListService] Member added successfully");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[MarketingListService] AddMember failed: {ex.Message}");
                throw new InvalidOperationException("添加名單成員失敗", ex);
            }
        }

        /// <summary>
        /// 批次添加成員到名單 (非同步) - Phase 2.3 效能優化
        /// </summary>
        public async Task<int> AddMembersAsync(
            Guid listGuid,
            List<Guid> memberGuidList,
            int batchSize = 50,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (listGuid == Guid.Empty)
                    throw new ArgumentException("List GUID 不可為空", nameof(listGuid));

                if (memberGuidList == null || memberGuidList.Count == 0)
                    return 0;

                Trace.WriteLine($"[MarketingListService] Adding {memberGuidList.Count} members in batches of {batchSize}");

                int successCount = 0;
                var batches = memberGuidList
                    .Select((guid, index) => new { guid, index })
                    .GroupBy(x => x.index / batchSize)
                    .Select(g => g.Select(x => x.guid).ToList())
                    .ToList();

                foreach (var batch in batches)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var tasks = batch.Select(memberGuid =>
                        Task.Run(() =>
                        {
                            try
                            {
                                AddMember(listGuid, memberGuid);
                                return true;
                            }
                            catch (Exception ex)
                            {
                                Trace.WriteLine($"[MarketingListService] Failed to add member {memberGuid}: {ex.Message}");
                                return false;
                            }
                        }, cancellationToken));

                    var results = await Task.WhenAll(tasks);
                    successCount += results.Count(r => r);
                }

                Trace.WriteLine($"[MarketingListService] Successfully added {successCount}/{memberGuidList.Count} members");
                return successCount;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[MarketingListService] AddMembersAsync failed: {ex.Message}");
                throw new InvalidOperationException("批次添加名單成員失敗", ex);
            }
        }

        /// <summary>
        /// 使用 SDK 批次添加成員 (最高效) - Phase 2.3 效能優化
        /// </summary>
        public async Task<int> AddMembersUsingSdkAsync(
            Guid listGuid,
            List<Guid> memberGuidList,
            IOrganizationService service,
            int maxBatchSize = 1000,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (listGuid == Guid.Empty)
                    throw new ArgumentException("List GUID 不可為空", nameof(listGuid));

                if (memberGuidList == null || memberGuidList.Count == 0)
                    return 0;

                Trace.WriteLine($"[MarketingListService] Using SDK batch to add {memberGuidList.Count} members");

                int successCount = 0;
                var batches = memberGuidList
                    .Select((guid, index) => new { guid, index })
                    .GroupBy(x => x.index / maxBatchSize)
                    .Select(g => g.Select(x => x.guid).ToList())
                    .ToList();

                foreach (var batch in batches)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await Task.Run(() =>
                    {
                        try
                        {
                            var request = new AddListMembersListRequest
                            {
                                ListId = listGuid,
                                MemberIds = batch.ToArray()
                            };

                            service.Execute(request);
                            successCount += batch.Count;
                            Trace.WriteLine($"[MarketingListService] Batch of {batch.Count} members added successfully");
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine($"[MarketingListService] Batch add failed: {ex.Message}");
                        }
                    }, cancellationToken);
                }

                Trace.WriteLine($"[MarketingListService] SDK batch completed. Added {successCount}/{memberGuidList.Count} members");
                return successCount;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[MarketingListService] AddMembersUsingSdkAsync failed: {ex.Message}");
                throw new InvalidOperationException("使用 SDK 批次添加名單成員失敗", ex);
            }
        }

        /// <summary>
        /// 移除名單成員
        /// </summary>
        public void RemoveMember(Guid listGuid, Guid memberGuid)
        {
            try
            {
                if (listGuid == Guid.Empty)
                    throw new ArgumentException("List GUID 不可為空", nameof(listGuid));

                if (memberGuid == Guid.Empty)
                    throw new ArgumentException("Member GUID 不可為空", nameof(memberGuid));

                Trace.WriteLine($"[MarketingListService] Removing member {memberGuid} from list {listGuid}");

                var request = new RemoveMemberListRequest
                {
                    ListId = listGuid,
                    EntityId = memberGuid
                };

                _organizationService.Execute(request);
                Trace.WriteLine($"[MarketingListService] Member removed successfully");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[MarketingListService] RemoveMember failed: {ex.Message}");
                throw new InvalidOperationException("移除名單成員失敗", ex);
            }
        }

        /// <summary>
        /// 批次移除名單成員 (非同步)
        /// </summary>
        public async Task<int> RemoveMembersAsync(
            Guid listGuid,
            List<Guid> memberGuidList,
            int batchSize = 50,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (listGuid == Guid.Empty)
                    throw new ArgumentException("List GUID 不可為空", nameof(listGuid));

                if (memberGuidList == null || memberGuidList.Count == 0)
                    return 0;

                Trace.WriteLine($"[MarketingListService] Removing {memberGuidList.Count} members in batches of {batchSize}");

                int successCount = 0;
                var batches = memberGuidList
                    .Select((guid, index) => new { guid, index })
                    .GroupBy(x => x.index / batchSize)
                    .Select(g => g.Select(x => x.guid).ToList())
                    .ToList();

                foreach (var batch in batches)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var tasks = batch.Select(memberGuid =>
                        Task.Run(() =>
                        {
                            try
                            {
                                RemoveMember(listGuid, memberGuid);
                                return true;
                            }
                            catch (Exception ex)
                            {
                                Trace.WriteLine($"[MarketingListService] Failed to remove member {memberGuid}: {ex.Message}");
                                return false;
                            }
                        }, cancellationToken));

                    var results = await Task.WhenAll(tasks);
                    successCount += results.Count(r => r);
                }

                Trace.WriteLine($"[MarketingListService] Successfully removed {successCount}/{memberGuidList.Count} members");
                return successCount;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[MarketingListService] RemoveMembersAsync failed: {ex.Message}");
                throw new InvalidOperationException("批次移除名單成員失敗", ex);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtilityNameSpace.ListOperations;
using ToolUtilityNameSpace.Interfaces;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System.Collections;
using System.Collections.Concurrent;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Crm.Sdk.Messages;

namespace ToolUtilityNameSpace.ListOperations
{
    public class ListService : IListService
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;

        public ListService(object logger, IOrganizationService organizationService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        }

        #region 同步方法 (向下相容)

        public void AddMembers(Guid listGuid, List<Guid> memberGuidList)
        {
            if (memberGuidList == null || memberGuidList.Count == 0) return;

            foreach (var member in memberGuidList)
            {
                // ? 使用 AddMemberListRequest (CRM SDK 專用方法)
                // ?? listmember 既不支援 Create，也不支援 Associate
                var request = new AddMemberListRequest
                {
                    ListId = listGuid,
                    EntityId = member
                };
                _organizationService.Execute(request);
            }
        }

        /// <summary>
        /// 使用 CRM SDK AddListMembersListRequest 批次新增多個成員到名單
        /// </summary>
        public void AddMembersUsingSdk(Guid listGuid, List<Guid> memberGuidList, IOrganizationService service)
        {
            if (memberGuidList == null || memberGuidList.Count == 0) return;
            
            try
            {
                var request = new AddListMembersListRequest
                {
                    ListId = listGuid,
                    MemberIds = memberGuidList.ToArray()
                };
                service.Execute(request);
            }
            catch (Exception ex)
            {
                // Log error and potentially fall back to individual adds
                throw new InvalidOperationException($"Failed to add members to list {listGuid}", ex);
            }
        }

        public void RemoveMember(Guid listGuid, Guid memberGuid)
        {
            // Query listmember records matching list + member, then delete
            var query = new QueryByAttribute("listmember") { ColumnSet = new ColumnSet("listmemberid") };
            query.AddAttributeValue("listid", listGuid);
            query.AddAttributeValue("entityid", memberGuid);
            var coll = _organizationService.RetrieveMultiple(query);
            if (coll == null || coll.Entities.Count == 0) return;
            foreach (var lm in coll.Entities)
            {
                _organizationService.Delete("listmember", lm.Id);
            }
        }

        /// <summary>
        /// 使用 CRM SDK RemoveMemberListRequest 從行銷名單移除成員
        /// </summary>
        public void RemoveMemberUsingSdk(Guid listGuid, Guid memberGuid, IOrganizationService service)
        {
            try
            {
                var request = new RemoveMemberListRequest
                {
                    ListId = listGuid,
                    EntityId = memberGuid
                };
                service.Execute(request);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to remove member {memberGuid} from list {listGuid}", ex);
            }
        }

        public EntityCollection RetrieveMemberListCollectionByListId(Guid listId)
        {
            var query = new QueryByAttribute("listmember") { ColumnSet = new ColumnSet("listmemberid", "entityid", "listid") };
            query.AddAttributeValue("listid", listId);
            return _organizationService.RetrieveMultiple(query);
        }

        public EntityCollection RetrieveMemberListCollectionByListIdUsingService(IOrganizationService externalService, Guid listId)
        {
            if (externalService == null) return new EntityCollection();
            var query = new QueryByAttribute("listmember") { ColumnSet = new ColumnSet("listmemberid", "entityid", "listid") };
            query.AddAttributeValue("listid", listId);
            return externalService.RetrieveMultiple(query);
        }

        public EntityCollection RetrieveMemberListCollectionByListIdUsingProxy(IOrganizationService externalProxy, Guid listId)
        {
            if (externalProxy == null) return new EntityCollection();
            var query = new QueryByAttribute("listmember") { ColumnSet = new ColumnSet("listmemberid", "entityid", "listid") };
            query.AddAttributeValue("listid", listId);
            return externalProxy.RetrieveMultiple(query);
        }

        public EntityCollection RetrieveDynamicMemberList(Guid listId)
        {
            var listEntity = _organizationService.Retrieve("list", listId, new ColumnSet("query"));
            if (listEntity == null || !listEntity.Attributes.Contains("query")) return new EntityCollection();
            var fetchXml = listEntity.GetAttributeValue<string>("query");
            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection RetrieveDynamicMemberListUsingService(IOrganizationService externalService, Guid listId)
        {
            if (externalService == null) return new EntityCollection();
            var listEntity = externalService.Retrieve("list", listId, new ColumnSet("query"));
            if (listEntity == null || !listEntity.Attributes.Contains("query")) return new EntityCollection();
            var fetchXml = listEntity.GetAttributeValue<string>("query");
            return externalService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection RetrieveDynamicMemberListUsingProxy(IOrganizationService externalProxy, Guid listId)
        {
            if (externalProxy == null) return new EntityCollection();
            var listEntity = externalProxy.Retrieve("list", listId, new ColumnSet("query"));
            if (listEntity == null || !listEntity.Attributes.Contains("query")) return new EntityCollection();
            var fetchXml = listEntity.GetAttributeValue<string>("query");
            return externalProxy.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection QueryListByContactId(Guid contactId, string associationName)
        {
            // associationName (e.g., "contact_list") could be used to build fetch; simplified placeholder
            var query = new QueryExpression("listmember")
            {
                ColumnSet = new ColumnSet(true)
            };
            query.Criteria.AddCondition("entityid", ConditionOperator.Equal, contactId);
            return _organizationService.RetrieveMultiple(query);
        }

        public ArrayList GetAllMemberDataFromList(Guid listEntityId)
        {
            var members = new ArrayList();

            // 先取得名單實體以判斷是靜態或動態名單
            var listEntity = _organizationService.Retrieve("list", listEntityId, new ColumnSet("type", "query"));
            if (listEntity == null) return members;

            bool isStaticList = false;
            if (listEntity.Attributes.Contains("type"))
            {
                isStaticList = !listEntity.GetAttributeValue<bool>("type");
            }

            EntityCollection memberCollection;
            if (isStaticList)
            {
                // 靜態名單
                memberCollection = RetrieveMemberListCollectionByListId(listEntityId);
                foreach (Entity memberEntity in memberCollection.Entities)
                {
                    if (memberEntity.Attributes.Contains("entityid"))
                    {
                        var entityRef = (EntityReference)memberEntity.Attributes["entityid"];
                        members.Add(entityRef.Id);
                    }
                }
            }
            else
            {
                // 動態名單
                memberCollection = RetrieveDynamicMemberList(listEntityId);
                foreach (Entity memberEntity in memberCollection.Entities)
                {
                    if (memberEntity.Attributes.Contains("contactid"))
                    {
                        members.Add((Guid)memberEntity.Attributes["contactid"]);
                    }
                }
            }

            return members;
        }

        public EntityCollection RetrieveLists()
        {
            var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='list'>
                        <attribute name='listname' />
                        <attribute name='createdfromcode' />
                        <attribute name='lastusedon' />
                        <attribute name='purpose' />
                        <attribute name='listid' />
                        <order attribute='listname' descending='true' />
                        <filter type='and'>
                          <condition attribute='statuscode' operator='eq' value='0' />
                          <condition attribute='purpose' operator='eq' value='小組名單' />
                          <condition attribute='new_app_named' operator='eq' value='1' />
                        </filter>
                      </entity>
                    </fetch>";
            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        public EntityCollection RetrieveSmallGroupLists()
        {
            var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='list'>
                        <attribute name='listname' />
                        <attribute name='createdfromcode' />
                        <attribute name='lastusedon' />
                        <attribute name='purpose' />
                        <attribute name='new_contact_race_leager_list' />
                        <attribute name='new_contact_family_leader_list' />
                        <attribute name='listid' />
                        <order attribute='listname' descending='true' />
                        <filter type='and'>
                          <condition attribute='new_app_named' operator='eq' value='1' />
                          <condition attribute='statuscode' operator='eq' value='0' />
                          <condition attribute='purpose' operator='eq' value='小組名單' />
                          <condition attribute='listname' operator='not-like' value='%退出%' />
                        </filter>
                      </entity>
                    </fetch>";
            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        /// <summary>
        /// 根據名單名稱查詢名單實體
        /// </summary>
        public Entity RetrieveListEntityByName(string listName)
        {
            var query = new QueryByAttribute("list") { ColumnSet = new ColumnSet("listmemberid", "entityid", "listid") };
            query.Attributes.AddRange("listname", "statecode");
            query.Values.AddRange(listName, 0);
            var coll = _organizationService.RetrieveMultiple(query);
            return (coll != null && coll.Entities.Count > 0) ? coll.Entities[0] : null;
        }

        /// <summary>
        /// 根據連絡人查詢所屬的名單
        /// </summary>
        public EntityCollection RetrieveListByContact(string contactName)
        {
            contactName = $"'{contactName}'";
            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='true'>
                          <entity name='list'>
                            <attribute name='listname' />
                            <attribute name='createdfromcode' />
                            <attribute name='lastusedon' />
                            <attribute name='purpose' />
                            <attribute name='listid' />
                            <order attribute='listname' descending='true' />
                            <filter type='and'>
                              <condition attribute='new_app_named' operator='eq' value='1' />
                              <condition attribute='purpose' operator='eq' value='小組名單' />
                            </filter>
                            <link-entity name='listmember' from='listid' to='listid' visible='false' intersect='true'>
                              <link-entity name='contact' from='contactid' to='entityid' alias='af'>
                                <filter type='and'>
                                  <condition attribute='fullname' operator='eq' value={contactName} />
                                </filter>
                              </link-entity>
                            </link-entity>
                          </entity>
                        </fetch>";

            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        /// <summary>
        /// 根據競賽領袖查詢名單
        /// </summary>
        public EntityCollection RetrieveListByRacerLeader(string contactName, string contactId)
        {
            contactName = $"'{contactName}'";
            contactId = $"'{{{contactId}}}'";

            var fetchXml = $@"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='list'>
                        <attribute name='listname' />
                        <attribute name='createdfromcode' />
                        <attribute name='lastusedon' />
                        <attribute name='purpose' />
                        <attribute name='listid' />
                        <order attribute='listname' descending='true' />
                        <filter type='and'>
                            <condition attribute='new_contact_race_leager_list' operator='eq' uiname={contactName} uitype='contact' value={contactId} />
                        </filter>
                      </entity>
                    </fetch>";

            return _organizationService.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        #endregion

        #region 非同步批量操作 (Phase 2.3 - 效能優化)

        /// <summary>
        /// 批量並行添加成員到名單 (非同步)
        /// ? Phase 2.3: 使用批次 + Task.WhenAll 並行處理
        /// 預期效能提升: 5-10倍
        /// </summary>
        /// <param name="listGuid">名單ID</param>
        /// <param name="memberGuidList">成員ID列表</param>
        /// <param name="batchSize">批次大小 (預設50)</param>
        /// <param name="cancellationToken">取消標記</param>
        /// <returns>成功添加的成員數</returns>
        public async Task<int> AddMembersAsync(
            Guid listGuid, 
            List<Guid> memberGuidList, 
            int batchSize = 50,
            CancellationToken cancellationToken = default)
        {
            if (memberGuidList == null || memberGuidList.Count == 0) 
                return 0;

            int successCount = 0;
            // ConcurrentBag 確保平行寫入的執行緒安全（List<Exception> 非執行緒安全）
            var exceptions = new ConcurrentBag<Exception>();

            try
            {
                var batches = ChunkList(memberGuidList, batchSize).ToList();

                // SemaphoreSlim 節流，避免 Task.Run 無限制產生而爆炸 Thread Pool
                using var throttle = new SemaphoreSlim(Environment.ProcessorCount * 2);

                foreach (var batch in batches)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var tasks = batch.Select(async memberId =>
                    {
                        await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
                        try
                        {
                            await Task.Run(() =>
                            {
                                var request = new AddMemberListRequest { ListId = listGuid, EntityId = memberId };
                                _organizationService.Execute(request);
                            }, cancellationToken).ConfigureAwait(false);
                            return true;
                        }
                        catch (Exception ex)
                        {
                            exceptions.Add(new InvalidOperationException($"Failed to add member {memberId} to list {listGuid}", ex));
                            return false;
                        }
                        finally { throttle.Release(); }
                    }).ToList();

                    var results = await Task.WhenAll(tasks).ConfigureAwait(false);
                    successCount += results.Count(r => r);

                    if (batches.Count > 1)
                        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }

                if (!exceptions.IsEmpty)
                {
                    // Log warnings: {exceptions.Count} members failed, {successCount} succeeded
                }

                return successCount;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to add members to list {listGuid}. Succeeded: {successCount}/{memberGuidList.Count}", 
                    ex);
            }
        }

        /// <summary>
        /// 批量並行移除名單成員 (非同步)
        /// ? Phase 2.3: 使用批次 + Task.WhenAll 並行處理
        /// </summary>
        public async Task<int> RemoveMembersAsync(
            Guid listGuid, 
            List<Guid> memberGuidList, 
            int batchSize = 50,
            CancellationToken cancellationToken = default)
        {
            if (memberGuidList == null || memberGuidList.Count == 0) 
                return 0;

            int successCount = 0;
            // ConcurrentBag 確保平行寫入的執行緒安全
            var exceptions = new ConcurrentBag<Exception>();

            try
            {
                var batches = ChunkList(memberGuidList, batchSize).ToList();

                // SemaphoreSlim 節流，避免 Thread Pool 爆炸
                using var throttle = new SemaphoreSlim(Environment.ProcessorCount * 2);

                foreach (var batch in batches)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // 直接使用 RemoveMemberListRequest，比查詢再刪除更高效（省去一次 CRM 呼叫）
                    var tasks = batch.Select(async memberId =>
                    {
                        await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
                        try
                        {
                            await Task.Run(() =>
                            {
                                var request = new RemoveMemberListRequest { ListId = listGuid, EntityId = memberId };
                                _organizationService.Execute(request);
                            }, cancellationToken).ConfigureAwait(false);
                            return true;
                        }
                        catch (Exception ex)
                        {
                            exceptions.Add(new InvalidOperationException($"Failed to remove member {memberId} from list {listGuid}", ex));
                            return false;
                        }
                        finally { throttle.Release(); }
                    }).ToList();

                    var results = await Task.WhenAll(tasks).ConfigureAwait(false);
                    successCount += results.Count(r => r);

                    if (batches.Count > 1)
                        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }

                return successCount;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to remove members from list {listGuid}. Succeeded: {successCount}/{memberGuidList.Count}", 
                    ex);
            }
        }

        /// <summary>
        /// 使用 CRM SDK 批量添加成員 (非同步)
        /// ? Phase 2.3: 使用 AddListMembersListRequest 批次處理
        /// 這是最高效的方式，CRM API 原生支援批次操作
        /// </summary>
        public async Task<int> AddMembersUsingSdkAsync(
            Guid listGuid, 
            List<Guid> memberGuidList, 
            IOrganizationService service,
            int maxBatchSize = 1000,
            CancellationToken cancellationToken = default)
        {
            if (memberGuidList == null || memberGuidList.Count == 0) 
                return 0;

            if (service == null)
                throw new ArgumentNullException(nameof(service));

            int successCount = 0;

            try
            {
                // ? 按照 CRM API 限制分批 (通常最大1000個)
                var batches = ChunkList(memberGuidList, maxBatchSize);

                foreach (var batch in batches)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // ? 使用 CRM SDK 批次 API
                    await Task.Run(() =>
                    {
                        var request = new AddListMembersListRequest
                        {
                            ListId = listGuid,
                            MemberIds = batch.ToArray()
                        };
                        service.Execute(request);
                    }, cancellationToken).ConfigureAwait(false);

                    successCount += batch.Count;

                    // 批次間延遲，避免過度壓力
                    if (batches.Count() > 1)
                    {
                        await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                    }
                }

                return successCount;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to add members to list {listGuid} using SDK. Succeeded: {successCount}/{memberGuidList.Count}", 
                    ex);
            }
        }

        /// <summary>
        /// 輔助方法: 將列表分批
        /// </summary>
        // GetRange 是 O(1) 直接存取，避免 Skip/Take 每次重新遍歷的 O(n^2) 問題
        private static IEnumerable<List<T>> ChunkList<T>(List<T> source, int chunkSize)
        {
            for (int i = 0; i < source.Count; i += chunkSize)
                yield return source.GetRange(i, Math.Min(chunkSize, source.Count - i));
        }

        #endregion
    }
}

// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/ListOperations/ListService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class ListService
// 主要成員：AddMembers、AddMembersUsingSdk、RemoveMember、RemoveMemberUsingSdk、RetrieveMemberListCollectionByListId、RetrieveMemberListCollectionByListIdUsingService、RetrieveMemberListCollectionByListIdUsingProxy、RetrieveDynamicMemberList、RetrieveDynamicMemberListUsingService、RetrieveDynamicMemberListUsingProxy
// 引用命名空間：System、System.Collections.Generic、System.Linq、System.Threading、System.Threading.Tasks、ToolUtilityNameSpace.EntityOperations、ToolUtilityNameSpace.ListOperations、ToolUtilityNameSpace.Interfaces
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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

        #region 靜態 FetchXml 查詢常數 (避免每次呼叫重新配置字串)

        /// <summary>
        /// 查詢所有啟用中的小組名單 (含 App 標記)
        /// </summary>
        private const string FetchXmlRetrieveLists = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
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

        /// <summary>
        /// 查詢所有小組名單 (排除已退出的名單，含族群領袖與家長欄位)
        /// </summary>
        private const string FetchXmlRetrieveSmallGroupLists = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
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

        #endregion

        public ListService(object logger, IOrganizationService organizationService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        }

        #region 同步方法 (向下相容)

        /// <summary>
        /// 批次新增多個成員到行銷名單
        /// 使用 AddListMembersListRequest 單次 CRM 呼叫完成，
        /// 避免逐一新增的 N 次往返延遲 (效能提升: N 次 → 1 次 CRM 呼叫)
        /// </summary>
        public void AddMembers(Guid listGuid, List<Guid> memberGuidList)
        {
            if (memberGuidList == null || memberGuidList.Count == 0) return;

            // 使用 CRM SDK 批次 API，一次呼叫即可新增所有成員
            var request = new AddListMembersListRequest
            {
                ListId = listGuid,
                MemberIds = memberGuidList.ToArray()
            };
            _organizationService.Execute(request);
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

        /// <summary>
        /// 從行銷名單移除單一成員
        /// 使用 RemoveMemberListRequest 單次 CRM 呼叫完成，
        /// 取代查詢再刪除的兩步驟操作 (效能提升: 2+ 次 → 1 次 CRM 呼叫)
        /// </summary>
        public void RemoveMember(Guid listGuid, Guid memberGuid)
        {
            // 直接使用 CRM SDK RemoveMemberListRequest，無需先查詢再刪除
            var request = new RemoveMemberListRequest
            {
                ListId = listGuid,
                EntityId = memberGuid
            };
            _organizationService.Execute(request);
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

        /// <summary>
        /// 根據名單 ID 取得靜態名單成員清單 (使用內部 CRM 服務)
        /// </summary>
        public EntityCollection RetrieveMemberListCollectionByListId(Guid listId)
            => RetrieveMemberListCore(_organizationService, listId);

        /// <summary>
        /// 根據名單 ID 取得靜態名單成員清單 (使用外部 CRM 服務)
        /// </summary>
        public EntityCollection RetrieveMemberListCollectionByListIdUsingService(IOrganizationService externalService, Guid listId)
            => externalService == null ? new EntityCollection() : RetrieveMemberListCore(externalService, listId);

        /// <summary>
        /// 根據名單 ID 取得靜態名單成員清單 (使用外部 Proxy 服務)
        /// </summary>
        public EntityCollection RetrieveMemberListCollectionByListIdUsingProxy(IOrganizationService externalProxy, Guid listId)
            => externalProxy == null ? new EntityCollection() : RetrieveMemberListCore(externalProxy, listId);

        /// <summary>
        /// 取得動態名單成員清單 (使用內部 CRM 服務)
        /// 動態名單透過 FetchXml 查詢條件即時計算成員
        /// </summary>
        public EntityCollection RetrieveDynamicMemberList(Guid listId)
            => RetrieveDynamicMemberListCore(_organizationService, listId);

        /// <summary>
        /// 取得動態名單成員清單 (使用外部 CRM 服務)
        /// </summary>
        public EntityCollection RetrieveDynamicMemberListUsingService(IOrganizationService externalService, Guid listId)
            => externalService == null ? new EntityCollection() : RetrieveDynamicMemberListCore(externalService, listId);

        /// <summary>
        /// 取得動態名單成員清單 (使用外部 Proxy 服務)
        /// </summary>
        public EntityCollection RetrieveDynamicMemberListUsingProxy(IOrganizationService externalProxy, Guid listId)
            => externalProxy == null ? new EntityCollection() : RetrieveDynamicMemberListCore(externalProxy, listId);

        /// <summary>
        /// 根據連絡人 ID 查詢其所屬的名單 (listmember)
        /// </summary>
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

        /// <summary>
        /// 取得名單中所有成員的 ContactId 清單
        /// 自動判斷靜態/動態名單並使用對應的查詢方式
        /// </summary>
        public ArrayList GetAllMemberDataFromList(Guid listEntityId)
        {
            // 取得名單實體以判斷是靜態或動態名單
            var listEntity = _organizationService.Retrieve("list", listEntityId, new ColumnSet("type", "query"));
            if (listEntity == null) return new ArrayList();

            // type = false 表示靜態名單，type = true 表示動態名單
            bool isStaticList = false;
            if (listEntity.Attributes.Contains("type"))
            {
                isStaticList = !listEntity.GetAttributeValue<bool>("type");
            }

            EntityCollection memberCollection;
            if (isStaticList)
            {
                // 靜態名單: 查詢 listmember 實體取得成員
                memberCollection = RetrieveMemberListCollectionByListId(listEntityId);

                // 預先配置 ArrayList 容量，避免動態擴容的記憶體重新配置成本
                var members = new ArrayList(memberCollection.Entities.Count);
                foreach (Entity memberEntity in memberCollection.Entities)
                {
                    if (memberEntity.Attributes.Contains("entityid"))
                    {
                        var entityRef = (EntityReference)memberEntity.Attributes["entityid"];
                        members.Add(entityRef.Id);
                    }
                }
                return members;
            }
            else
            {
                // 動態名單: 使用名單儲存的 FetchXml 查詢條件即時計算成員
                memberCollection = RetrieveDynamicMemberListCore(_organizationService, listEntityId);

                // 預先配置 ArrayList 容量
                var members = new ArrayList(memberCollection.Entities.Count);
                foreach (Entity memberEntity in memberCollection.Entities)
                {
                    if (memberEntity.Attributes.Contains("contactid"))
                    {
                        members.Add((Guid)memberEntity.Attributes["contactid"]);
                    }
                }
                return members;
            }
        }

        /// <summary>
        /// 取得所有啟用中的小組名單 (含 App 標記)
        /// </summary>
        public EntityCollection RetrieveLists()
            => _organizationService.RetrieveMultiple(new FetchExpression(FetchXmlRetrieveLists));

        /// <summary>
        /// 取得所有小組名單 (排除已退出的名單，含族群領袖與家長欄位)
        /// </summary>
        public EntityCollection RetrieveSmallGroupLists()
            => _organizationService.RetrieveMultiple(new FetchExpression(FetchXmlRetrieveSmallGroupLists));

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
        /// 批量添加成員到名單 (非同步)
        /// 使用 AddListMembersListRequest 批次 API，每批次僅需 1 次 CRM 呼叫
        /// 效能提升: N 次個別呼叫 → ceil(N/batchSize) 次批次呼叫
        /// </summary>
        /// <param name="listGuid">名單 ID</param>
        /// <param name="memberGuidList">成員 ID 列表</param>
        /// <param name="batchSize">每批次成員數 (預設 50)</param>
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
            // ConcurrentBag 確保平行寫入的執行緒安全
            var exceptions = new ConcurrentBag<Exception>();

            try
            {
                // 預先具體化為 List，避免 IEnumerable 重複列舉
                var batches = ChunkList(memberGuidList, batchSize).ToList();

                foreach (var batch in batches)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        // 使用 CRM SDK 批次 API，每批僅 1 次 CRM 呼叫
                        await Task.Run(() =>
                        {
                            var request = new AddListMembersListRequest
                            {
                                ListId = listGuid,
                                MemberIds = batch.ToArray()
                            };
                            _organizationService.Execute(request);
                        }, cancellationToken).ConfigureAwait(false);

                        successCount += batch.Count;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        exceptions.Add(new InvalidOperationException(
                            $"Failed to add batch of {batch.Count} members to list {listGuid}", ex));
                    }

                    // 批次間短暫延遲，避免對 CRM 伺服器造成過大壓力
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
                    $"Failed to add members to list {listGuid}. Succeeded: {successCount}/{memberGuidList.Count}",
                    ex);
            }
        }

        /// <summary>
        /// 批量移除名單成員 (非同步)
        /// 使用 ExecuteMultipleRequest 將同一批次的移除請求合併為單次 CRM 呼叫
        /// 效能提升: N 次個別呼叫 → ceil(N/batchSize) 次批次呼叫
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
                // 預先具體化為 List，避免 IEnumerable 重複列舉
                var batches = ChunkList(memberGuidList, batchSize).ToList();

                foreach (var batch in batches)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        // 使用 ExecuteMultipleRequest 將同批次的移除請求合併為單次 CRM 往返
                        await Task.Run(() =>
                        {
                            var multiRequest = new ExecuteMultipleRequest
                            {
                                Settings = new ExecuteMultipleSettings
                                {
                                    ContinueOnError = true,  // 單筆失敗不影響其他
                                    ReturnResponses = false  // 不需要回傳結果，節省傳輸量
                                },
                                Requests = new OrganizationRequestCollection()
                            };

                            foreach (var memberId in batch)
                            {
                                multiRequest.Requests.Add(new RemoveMemberListRequest
                                {
                                    ListId = listGuid,
                                    EntityId = memberId
                                });
                            }

                            _organizationService.Execute(multiRequest);
                        }, cancellationToken).ConfigureAwait(false);

                        successCount += batch.Count;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        exceptions.Add(new InvalidOperationException(
                            $"Failed to remove batch of {batch.Count} members from list {listGuid}", ex));
                    }

                    // 批次間短暫延遲，避免對 CRM 伺服器造成過大壓力
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
        /// 使用 CRM SDK AddListMembersListRequest 批次新增成員 (非同步)
        /// 這是最高效的方式: CRM API 原生支援批次操作，單次呼叫可新增多達 1000 個成員
        /// </summary>
        /// <param name="listGuid">名單 ID</param>
        /// <param name="memberGuidList">成員 ID 列表</param>
        /// <param name="service">外部 CRM 服務實例</param>
        /// <param name="maxBatchSize">最大批次大小 (CRM API 限制，預設 1000)</param>
        /// <param name="cancellationToken">取消標記</param>
        /// <returns>成功添加的成員數</returns>
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
                // 預先具體化為 List，避免 yield return 的 IEnumerable 被重複列舉
                var batches = ChunkList(memberGuidList, maxBatchSize).ToList();

                foreach (var batch in batches)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // 使用 CRM SDK 批次 API，單次呼叫新增整批成員
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

                    // 批次間延遲，避免 CRM 伺服器過載
                    if (batches.Count > 1)
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
        /// 輔助方法: 將列表依指定大小分批
        /// 使用 GetRange 直接存取子範圍，避免 Skip/Take 的重複遍歷開銷
        /// </summary>
        private static IEnumerable<List<T>> ChunkList<T>(List<T> source, int chunkSize)
        {
            for (int i = 0; i < source.Count; i += chunkSize)
                yield return source.GetRange(i, Math.Min(chunkSize, source.Count - i));
        }

        /// <summary>
        /// 共用私有方法: 根據名單 ID 查詢靜態名單成員
        /// 消除 RetrieveMemberListCollectionByListId / UsingService / UsingProxy 三個方法的重複程式碼
        /// </summary>
        private static EntityCollection RetrieveMemberListCore(IOrganizationService service, Guid listId)
        {
            var query = new QueryByAttribute("listmember")
            {
                ColumnSet = new ColumnSet("listmemberid", "entityid", "listid")
            };
            query.AddAttributeValue("listid", listId);
            return service.RetrieveMultiple(query);
        }

        /// <summary>
        /// 共用私有方法: 根據名單 ID 查詢動態名單成員
        /// 動態名單將儲存的 FetchXml 查詢條件作為即時篩選
        /// 消除 RetrieveDynamicMemberList / UsingService / UsingProxy 三個方法的重複程式碼
        /// </summary>
        private static EntityCollection RetrieveDynamicMemberListCore(IOrganizationService service, Guid listId)
        {
            var listEntity = service.Retrieve("list", listId, new ColumnSet("query"));
            if (listEntity == null || !listEntity.Attributes.Contains("query"))
                return new EntityCollection();

            var fetchXml = listEntity.GetAttributeValue<string>("query");
            return service.RetrieveMultiple(new FetchExpression(fetchXml));
        }

        #endregion
    }
}

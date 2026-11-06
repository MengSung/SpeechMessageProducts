/*
 * 檔案整體說明
 *
 * 目的與定位：名單的純SDK資料服務；沿用原二十個入口、查詢投影、降冪及不同成員來源，不引用host連線或logger。 host必須先驗使用者／租戶／教會及名單權限；任何ID、顯示名稱或儲存Fetch都不是授權來源。
 * 單頁最多5000列，完整成員讀取最多5000列／1000頁；全部正式身份通過才回操作自有候選。
 *
 * 主要功能與流程：
 * 本檔宣告 ListService。
 * - 保留必填logger的來源契約但不保存；SDK只借用同一已授權scope，不取得Dispose所有權。
 * - 沿用單次AddListMembersListRequest；完整驗證至多5000個唯一正式成員再送，不以重複輸入灌成功數。
 * - 使用明確借入SDK送原生批次，外部連線不處置；只發布成功／例外，不吞錯或保存輸入。
 * - 以原生RemoveMemberListRequest移除一個已授權關係；不先查再Delete，無自動重試。
 * 主要操作入口包含 AddMembers、AddMembersUsingSdk、RemoveMember、RemoveMemberUsingSdk、RetrieveMemberListCollectionByListId、
 * RetrieveMemberListCollectionByListIdUsingService、RetrieveMemberListCollectionByListIdUsingProxy、
 * RetrieveDynamicMemberList；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.DataOperations;

namespace ToolUtilityNameSpace.ListOperations
{
    /// <summary>
    /// 名單的純SDK資料服務；沿用原二十個入口、查詢投影、降冪及不同成員來源，不引用host連線或logger。
    /// host必須先驗使用者／租戶／教會及名單權限；任何ID、顯示名稱或儲存Fetch都不是授權來源。
    /// 單頁最多5000列，完整成員讀取最多5000列／1000頁；全部正式身份通過才回操作自有候選。
    /// </summary>
    /// <remarks>
    /// 不保存logger、結果、credential或request。批次只建立一個有界且必須await的owner工作，無逐列Task或隱藏重試。
    /// Dispose拒新入口並清借用SDK；已入場工作使用局部參照至drain，host須先await/drain再關連線。
    /// 寫入沿用CRM原生名單成員訊息，由資料庫關聯唯一性控制；已完成批次不會因後續故障或取消回復。
    /// </remarks>
    public class ListService : IListService, IDisposable
    {
        private IOrganizationService? _organizationService;
        private int _disposed;
        private static readonly ReadOnlyCollection<string> ListFields = Array.AsReadOnly(new[]
            { "listname", "createdfromcode", "lastusedon", "purpose", "listid" });
        private static readonly ReadOnlyCollection<string> SmallFields = Array.AsReadOnly(new[]
            { "listname", "createdfromcode", "lastusedon", "purpose", "new_contact_race_leager_list", "new_contact_family_leader_list", "listid" });
        private static readonly string Purpose = LegacyValue("List.Query.Purpose");
        private static readonly string ExitPattern = LegacyValue("List.Query.ExitPattern");

        /// <summary>保留必填logger的來源契約但不保存；SDK只借用同一已授權scope，不取得Dispose所有權。</summary>
        /// <param name="logger">舊診斷參數，不保存、不反射或處置。</param>
        /// <param name="organizationService">host擁有的SDK；不得跨不相容使用者／租戶共用本服務。</param>
        public ListService(object logger, IOrganizationService organizationService)
        {
#if NETFRAMEWORK
            if (logger == null) throw new ArgumentNullException(nameof(logger));
#else
            ArgumentNullException.ThrowIfNull(logger);
#endif
            _organizationService = SdkDataQuery.RequireService(organizationService);
        }

        /// <summary>沿用單次AddListMembersListRequest；完整驗證至多5000個唯一正式成員再送，不以重複輸入灌成功數。</summary>
        public void AddMembers(Guid listGuid, List<Guid> memberGuidList) => Add(GetService(), listGuid, memberGuidList);

        /// <summary>使用明確借入SDK送原生批次，外部連線不處置；只發布成功／例外，不吞錯或保存輸入。</summary>
        public void AddMembersUsingSdk(Guid listGuid, List<Guid> memberGuidList, IOrganizationService service)
        { GetService(); Add(SdkDataQuery.RequireService(service), listGuid, memberGuidList); }

        /// <summary>以原生RemoveMemberListRequest移除一個已授權關係；不先查再Delete，無自動重試。</summary>
        public void RemoveMember(Guid listGuid, Guid memberGuid) => Remove(GetService(), listGuid, memberGuid);

        /// <summary>保留外部SDK入口；只移除指定正式關係，故障原樣傳給最終host共用告警。</summary>
        public void RemoveMemberUsingSdk(Guid listGuid, Guid memberGuid, IOrganizationService service)
        { GetService(); Remove(SdkDataQuery.RequireService(service), listGuid, memberGuid); }

        /// <summary>保留原QueryByAttribute與三欄投影，單頁metadata不改；5000列與全部正式listmember身份先驗後回。</summary>
        public EntityCollection RetrieveMemberListCollectionByListId(Guid listId) => Members(GetService(), listId);

        /// <summary>沿用external null回空集合；非null只借用該scope SDK，不回退內部連線而跨scope讀取。</summary>
        public EntityCollection RetrieveMemberListCollectionByListIdUsingService(IOrganizationService? externalService, Guid listId)
        { GetService(); return externalService == null ? new EntityCollection() : Members(externalService, listId); }

        /// <summary>保留Proxy命名及IOrganizationService參數，不接觸Proxy的host屬性；null語義與Service入口相同。</summary>
        public EntityCollection RetrieveMemberListCollectionByListIdUsingProxy(IOrganizationService? externalService, Guid listId) =>
            RetrieveMemberListCollectionByListIdUsingService(externalService, listId);

        /// <summary>先驗list根表／ID及儲存Fetch，再由同SDK RetrieveMultiple；單頁metadata原樣，無cache。</summary>
        public EntityCollection RetrieveDynamicMemberList(Guid listId) => Dynamic(GetService(), listId, false);

        /// <summary>明確外部SDK承擔整次動態查詢，null仍回空，不把儲存Fetch文字當授權。</summary>
        public EntityCollection RetrieveDynamicMemberListUsingService(IOrganizationService? externalService, Guid listId)
        { GetService(); return externalService == null ? new EntityCollection() : Dynamic(externalService, listId, false); }

        /// <summary>保留Proxy相容入口並委派唯一動態讀取實作；不建立、重設或關閉任何連線。</summary>
        public EntityCollection RetrieveDynamicMemberListUsingProxy(IOrganizationService? externalService, Guid listId) =>
            RetrieveDynamicMemberListUsingService(externalService, listId);

        /// <summary>保留listmember根表、AllColumns及唯一entityid條件；associationName原未用，不偷偷新增不同查詢語義。</summary>
        public EntityCollection QueryListByContactId(Guid contactId, string associationName)
        {
            var sdk = GetService(); ListMembershipOperations.RequireId(contactId, nameof(contactId));
            var query = new QueryExpression("listmember") { ColumnSet = new ColumnSet(true) };
            query.Criteria.AddCondition("entityid", ConditionOperator.Equal, contactId);
            return Page(sdk, query, "listmember");
        }

        /// <summary>
        /// 依原type判斷靜態／動態，完整續讀後才投影正式成員GUID；單頁API不被冒稱全名單。
        /// 靜態entityid接受正式EntityReference，動態contactid須等於row.Id；缺欄／空／重複成員拒絕整份ArrayList。
        /// </summary>
        public ArrayList GetAllMemberDataFromList(Guid listEntityId)
        {
            var sdk = GetService();
            var list = ListMembershipOperations.ReadList(sdk, listEntityId, new ColumnSet("type", "query"));
            var dynamic = true;
            if (list.Attributes.TryGetValue("type", out var kind))
                dynamic = kind is bool flag ? flag : throw new InvalidOperationException("The SDK returned invalid list metadata.");
            var rows = dynamic ? Dynamic(sdk, listEntityId, true)
                : ListMembershipOperations.ReadComplete(sdk.RetrieveMultiple, MemberQuery(listEntityId), "listmember");
            return ListMembershipOperations.ProjectMembers(rows, dynamic);
        }

        /// <summary>保留statuscode=0、purpose、App條件及listname降冪；不加入原本沒有的statecode。</summary>
        public EntityCollection RetrieveLists()
        {
            var sdk = GetService(); var entity = SdkDataQuery.Entity("list", ListFields, "listname", true);
            entity.Add(SdkDataQuery.Filter(SdkDataQuery.Condition("statuscode", "eq", "0"),
                SdkDataQuery.Condition("purpose", "eq", Purpose), SdkDataQuery.Condition("new_app_named", "eq", "1")));
            return SdkDataQuery.Run(sdk, entity);
        }

        /// <summary>保留兩個leader欄位及原「退出」排除謂詞，不借用另一Fetch服務的不同「測試」條件或排序。</summary>
        public EntityCollection RetrieveSmallGroupLists()
        {
            var sdk = GetService(); var entity = SdkDataQuery.Entity("list", SmallFields, "listname", true);
            entity.Add(SdkDataQuery.Filter(SdkDataQuery.Condition("new_app_named", "eq", "1"),
                SdkDataQuery.Condition("statuscode", "eq", "0"), SdkDataQuery.Condition("purpose", "eq", Purpose),
                SdkDataQuery.Condition("listname", "not-like", ExitPattern)));
            return SdkDataQuery.Run(sdk, entity);
        }

        /// <summary>保持原QueryByAttribute三欄及name/statecode條件；整頁先驗身份，再回舊第一筆／null，姓名非唯一鍵。</summary>
        public Entity? RetrieveListEntityByName(string listName)
        {
            var sdk = GetService();
            var query = new QueryByAttribute("list") { ColumnSet = new ColumnSet("listmemberid", "entityid", "listid") };
            query.Attributes.AddRange("listname", "statecode"); query.Values.AddRange(listName, 0);
            var page = Page(sdk, query, "list"); return page.Entities.Count == 0 ? null : page.Entities[0];
        }

        /// <summary>保留distinct=true、兩層listmember/contact link及原姓名條件；姓名只進XAttribute並安全XML編碼。</summary>
        public EntityCollection RetrieveListByContact(string contactName)
        {
            var sdk = GetService(); var entity = SdkDataQuery.Entity("list", ListFields, "listname", true);
            entity.Add(SdkDataQuery.Filter(SdkDataQuery.Condition("new_app_named", "eq", "1"), SdkDataQuery.Condition("purpose", "eq", Purpose)),
                new XElement("link-entity", new XAttribute("name", "listmember"), new XAttribute("from", "listid"), new XAttribute("to", "listid"),
                    new XAttribute("visible", "false"), new XAttribute("intersect", "true"),
                    new XElement("link-entity", new XAttribute("name", "contact"), new XAttribute("from", "contactid"), new XAttribute("to", "entityid"),
                        new XAttribute("alias", "af"), SdkDataQuery.Filter(SdkDataQuery.Condition("fullname", "eq", contactName)))));
            return SdkDataQuery.Run(sdk, entity, distinct: true);
        }

        /// <summary>保持原leader lookup且不加App/statecode；ID非空GUID與uiname XML編碼，名稱不建立授權。</summary>
        public EntityCollection RetrieveListByRacerLeader(string contactName, string contactId)
        {
            var sdk = GetService(); var entity = SdkDataQuery.Entity("list", ListFields, "listname", true);
            entity.Add(SdkDataQuery.Filter(SdkDataQuery.Lookup("new_contact_race_leager_list", contactName, "contact", contactId)));
            return SdkDataQuery.Run(sdk, entity);
        }

        /// <summary>預設50人；一次有界owner串行送原生批次，保留100ms批間節流，故障立即停止後批並await排空。</summary>
        public Task<int> AddMembersAsync(Guid listGuid, List<Guid> memberGuidList, int batchSize = 50, CancellationToken cancellationToken = default) =>
            ListMembershipOperations.Start(GetService(), listGuid, memberGuidList, batchSize, MembershipWrite.AddBulk, 100, cancellationToken);

        /// <summary>沿用ExecuteMultiple、ContinueOnError與ReturnResponses=false；仍逐Fault檢查，不能把失敗批次算成功。</summary>
        public Task<int> RemoveMembersAsync(Guid listGuid, List<Guid> memberGuidList, int batchSize = 50, CancellationToken cancellationToken = default) =>
            ListMembershipOperations.Start(GetService(), listGuid, memberGuidList, batchSize, MembershipWrite.RemoveBulk, 100, cancellationToken);

        /// <summary>借用指定SDK，保留1000人預設及200ms批間節流；完整輸入snapshot驗完才排程，caller取消不代表rollback。</summary>
        public Task<int> AddMembersUsingSdkAsync(Guid listGuid, List<Guid> memberGuidList, IOrganizationService service,
            int maxBatchSize = 1000, CancellationToken cancellationToken = default)
        {
            GetService(); return ListMembershipOperations.Start(SdkDataQuery.RequireService(service), listGuid, memberGuidList,
                maxBatchSize, MembershipWrite.AddBulk, 200, cancellationToken);
        }

        /// <summary>單筆原生新增訊息；只讀caller輸入的局部snapshot，不保存可變List。</summary>
        private static void Add(IOrganizationService sdk, Guid listId, List<Guid> input)
        {
            var ids = ListMembershipOperations.Snapshot(listId, input);
            if (ids.Length != 0) sdk.Execute(new AddListMembersListRequest { ListId = listId, MemberIds = ids });
        }

        /// <summary>沿用原生移除語義與例外；不以模糊失敗重試可能已提交的寫入。</summary>
        private static void Remove(IOrganizationService sdk, Guid listId, Guid memberId)
        {
            ListMembershipOperations.RequireId(listId, nameof(listId)); ListMembershipOperations.RequireId(memberId, nameof(memberId));
            sdk.Execute(new RemoveMemberListRequest { ListId = listId, EntityId = memberId });
        }

        /// <summary>三個靜態入口共用原單頁query，欄位／條件順序及metadata不改。</summary>
        private static EntityCollection Members(IOrganizationService sdk, Guid id) => Page(sdk, MemberQuery(id), "listmember");

        /// <summary>建立每次獨有的三欄QueryByAttribute，不共用可變ColumnSet或PageInfo。</summary>
        private static QueryByAttribute MemberQuery(Guid id)
        {
            ListMembershipOperations.RequireId(id, nameof(id));
            var query = new QueryByAttribute("listmember") { ColumnSet = new ColumnSet("listmemberid", "entityid", "listid") };
            query.AddAttributeValue("listid", id); return query;
        }

        /// <summary>只在局部使用儲存Fetch，安全解析根且核對list身份；完整投影另用有界續頁，不能回部分結果。</summary>
        private static EntityCollection Dynamic(IOrganizationService sdk, Guid id, bool complete)
        {
            var list = ListMembershipOperations.ReadList(sdk, id, new ColumnSet("query"));
            if (!list.Attributes.TryGetValue("query", out var value) || value is not string xml)
                throw new InvalidOperationException("The dynamic list has no valid query.");
            var table = SdkDataQuery.ExpectedFetchEntity(xml, nameof(id));
            var query = new FetchExpression(xml);
            return complete ? ListMembershipOperations.ReadComplete(sdk.RetrieveMultiple, query, table) : Page(sdk, query, table);
        }

        /// <summary>拒絕null、錯表、空／重複ID及超量後回原單頁；不同ID的同名記錄完整保留。</summary>
        private static EntityCollection Page(IOrganizationService sdk, QueryBase query, string table) =>
            SdkDataQuery.ValidatePage(sdk.RetrieveMultiple(query), table, 5000);

        /// <summary>中立resx的資料庫固定謂詞只取一次不可變字串，不能隨UI語系翻譯而擴張／改變業務查詢。</summary>
        private static string LegacyValue(string key) => ListQueryLegacyData.Rows.Single(row => string.Equals(row[0], key, StringComparison.Ordinal))[1]!;

        /// <summary>Dispose與新入口的線性化界線；已入場者持局部SDK至drain，不能處置借入連線。</summary>
        private IOrganizationService GetService()
        {
            var sdk = Volatile.Read(ref _organizationService);
#if NETFRAMEWORK
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(ListService));
#else
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
#endif
            return sdk ?? throw new ObjectDisposedException(nameof(ListService));
        }

        /// <summary>冪等清除唯一借入SDK參照；已開始的Task仍由caller await，不做fire-and-forget或假稱取消SDK。</summary>
        protected virtual void Dispose(bool disposing)
        { if (Interlocked.Exchange(ref _disposed, 1) == 0) Interlocked.Exchange(ref _organizationService, null); }

        /// <summary>停止新入口並解除SDKowner參照；沒有cache、timer、subscription或unmanaged資源。</summary>
        public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
    }
}

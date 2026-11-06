/*
 * 檔案整體說明
 *
 * 目的與定位：本檔驗證只讀metadata投影真正遵守查詢範圍、完整發布、正式身份與不保留SDK結果的契約。
 * 測試依賴：MetadataService只允許固定metadata messages、組織languagecode與指定metadata表；業務資料／設定本文／任何資料寫入都拒絕。
 * 故障注入：錯回應、空／重複metadata ID、重複名稱、別組織、空續頁、跨頁重複ID、原SDK故障與caller取消。
 * 相容與斷言：379目標多型lookup完整保留而4097拒絕；內建publisher空prefix保留正式身份；5001筆filter完整分頁且不改Core業務5000限制。
 * 隔離與回收：兩個server組織ID千次並行只回自己的scope，來源mutation不能改已回snapshot；百快照弱參照全部死亡，SDK替身持續存活。
 * 資源生命週期：每案只用合成SDK與記憶體metadata，沒有真連線／token／timer；readonly SDK回應屬性只由測試反射建立，不能搬到產品當setter。
 * 維護要求：新增只讀metadata類別時，同步擴充固定白名單及獨立錯誤樣本；不以metadata匯出成功推論VM隔離、備份或寫入驗收。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Organization;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Schema;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>以嚴格白名單SDK驗證metadata投影；任何業務資料查詢、寫入或設定本文欄位都直接失敗，不冒稱真機。</summary>
public sealed class SchemaMetadataTests
{
    /// <summary>讀出真實組織ID／uniqueName／基礎語言與完整metadata，輸出不能持有SDK物件或被後續來源mutation改變。</summary>
    [Fact]
    public void Read_OnlyMetadata_ReturnsDetachedCompleteIdentity()
    {
        var service = new MetadataService();
        var snapshot = SchemaMetadata.Read(service, default);
        Assert.Equal(service.OrganizationId, snapshot.OrganizationId);
        Assert.Equal("goodshepherdback", snapshot.Organization);
        Assert.Equal(1028, snapshot.LanguageCode);
        Assert.Equal(2, snapshot.Entities.Count);
        var primary = snapshot.Entities.Single(entity => entity.LogicalName == "account").Fields.Single();
        Assert.Equal("name", primary.LogicalName);
        service.Entities[0].Attributes[0].LogicalName = "changed";
        Assert.Equal("name", primary.LogicalName);
        Assert.Equal(0, service.Writes);
    }

    /// <summary>錯回應型別、缺metadata ID、重複ID／邏輯身份、別組織及metadata分頁不完整均拒絕，不靜默丟列或當空組織。</summary>
    /// <param name="fault">固定注入種類。</param>
    [Theory]
    [InlineData("wrong-response")]
    [InlineData("empty-id")]
    [InlineData("duplicate-id")]
    [InlineData("duplicate-name")]
    [InlineData("foreign-organization")]
    [InlineData("partial-page")]
    public void Read_InvalidMetadata_RejectsBeforePublication(string fault)
    {
        var service = new MetadataService { Fault = fault };
        Assert.ThrowsAny<Exception>(() => SchemaMetadata.Read(service, default));
        Assert.Equal(0, service.Writes);
    }

    /// <summary>進場前取消不呼叫SDK；唯讀故障傳原Exception，不以空快照吞掉錯誤。</summary>
    [Fact]
    public void Read_CancellationAndFault_PreserveOwnershipAndOriginalFailure()
    {
        var service = new MetadataService();
        using var cancellation = new System.Threading.CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => SchemaMetadata.Read(service, cancellation.Token));
        Assert.Equal(0, service.Calls);
        service.Failure = new InvalidOperationException("synthetic-private-metadata");
        Assert.Same(service.Failure, Assert.Throws<InvalidOperationException>(() => SchemaMetadata.Read(service, default)));
    }

    /// <summary>D365 regarding等多型lookup可合法指向數百表；只讀實測379不能誤用P1單一lookup限制，仍守4096實體預算與唯一性。</summary>
    [Fact]
    public void Read_PolymorphicLookup_PreservesAllTargetsWithinGlobalEntityBudget()
    {
        var service = new MetadataService();
        var lookup = new LookupAttributeMetadata { MetadataId = Guid.NewGuid(), LogicalName = "regardingobjectid",
            Targets = Enumerable.Range(0, 379).Select(index => "synthetic" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray() };
        MetadataService.SetResponse(service.Entities[0], "Attributes", new AttributeMetadata[] { lookup });
        Assert.Equal(379, SchemaMetadata.Read(service, default).Entities.Single(entity => entity.LogicalName == "account").Fields.Single().Targets.Count);
        lookup.Targets = Enumerable.Range(0, 4097).Select(index => "synthetic" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Assert.Throws<InvalidDataException>(() => SchemaMetadata.Read(service, default));
    }

    /// <summary>內建publisher可能沒有自訂prefix，完整唯讀匯出須保留正式ID而非拒絕或刪除；本產品仍只能使用new前綴。</summary>
    [Fact]
    public void Read_BuiltInPublisherWithoutCustomPrefix_PreservesMetadataIdentity()
    {
        var service = new MetadataService();
        var publisher = new Entity("publisher", Guid.NewGuid()) { ["uniquename"] = "syntheticBuiltin", ["customizationprefix"] = "" };
        service.PublisherRows.Add(publisher);
        var actual = Assert.Single(SchemaMetadata.Read(service, default).Publishers);
        Assert.Equal(publisher.Id, actual.Id);
        Assert.Equal("", actual.Prefix);
    }

    /// <summary>組織message filter可多於單次業務查詢5000列；完整metadata要保留5001列，跨頁重複ID仍拒絕，不能提高Core產品查詢上限。</summary>
    [Fact]
    public void Read_MetadataFiltersOverOnePage_PreserveAllRowsAndRejectCrossPageDuplicate()
    {
        var service = new MetadataService();
        for (var index = 0; index < 5001; index++) service.FilterRows.Add(new Entity("sdkmessagefilter", Guid.NewGuid())
        { ["sdkmessageid"] = new EntityReference("sdkmessage", Guid.NewGuid()), ["primaryobjecttypecode"] = "account" });
        Assert.Equal(5001, SchemaMetadata.Read(service, default).Filters.Count);
        service.FilterRows[5000].Id = service.FilterRows[0].Id;
        Assert.Throws<InvalidDataException>(() => SchemaMetadata.Read(service, default));
    }

    /// <summary>兩個獨立server組織ID的metadata並行一千次不得串入另一scope；SDK各自只讀且沒有跨命令全域快取。</summary>
    [Fact]
    public void Read_ParallelTwoOrganizations_PreserveTheirOwnServerIdentity()
    {
        var first = new MetadataService();
        var second = new MetadataService();
        System.Threading.Tasks.Parallel.For(0, 1000, index =>
        {
            var source = index % 2 == 0 ? first : second;
            var actual = SchemaMetadata.Read(source, default);
            Assert.Equal(source.OrganizationId, actual.OrganizationId);
        });
        Assert.Equal(0, first.Writes);
        Assert.Equal(0, second.Writes);
    }

    /// <summary>長生命SDK借用者讀完百份快照後不能保留任一結果，合成SDK仍存活；不只驗scope計數，必須弱參照全部死亡。</summary>
    [Fact]
    public void Read_HundredSnapshots_ReleaseEveryResultWhileSourceLives()
    {
        var service = new MetadataService();
        var references = CollectSnapshots(service);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.All(references, reference => Assert.False(reference.IsAlive));
        GC.KeepAlive(service);
    }

    /// <summary>分離本案JIT區域生命週期，回傳只有弱參照，不靠清空產品快照或放寬回收比例。</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static List<WeakReference> CollectSnapshots(MetadataService service)
    {
        var references = new List<WeakReference>();
        for (var index = 0; index < 100; index++) references.Add(new WeakReference(SchemaMetadata.Read(service, default)));
        return references;
    }

    /// <summary>單案SDK替身只供metadata與純組織語言讀取；不持有真正連線、credential或背景資源。</summary>
    internal class MetadataService : IOrganizationService
    {
        /// <summary>metadata可讀表白名單獨立於產品定義，任何業務表皆拒絕。</summary>
        private static readonly string[] AllowedTables = { "solution", "publisher", "solutioncomponent", "sdkmessagefilter", "sdkmessageprocessingstep" };
        /// <summary>每案獨立組織身份，不能以白名單alias取代實際server組織ID。</summary>
        internal Guid OrganizationId { get; } = Guid.NewGuid();
        /// <summary>每案獨占的SDK來源，測試會mutation確認產品已脫離原物件。</summary>
        internal List<EntityMetadata> Entities { get; } = new() { BaseEntity("account", "name"), BaseEntity("contact", "fullname") };
        /// <summary>可選合成publisher定義，供官方內建空prefix的相容契約；不包含個人或token。</summary>
        internal List<Entity> PublisherRows { get; } = new();
        /// <summary>完整分頁合成filter資料，5000頁面以上僅限metadata匯出，不包含業務列。</summary>
        internal List<Entity> FilterRows { get; } = new();
        /// <summary>固定測試故障碼，不是使用者內容。</summary>
        internal string Fault { get; init; } = "";
        /// <summary>需要驗證保真的原始合成例外，不含真實秘密。</summary>
        internal Exception? Failure { get; set; }
        /// <summary>實際SDK呼叫次數，取消前應保持零。</summary>
        internal int Calls { get; private set; }
        /// <summary>任何寫入都不應被唯讀投影呼叫；方法一律拒絕，不能靠假成功掩蓋寫入。</summary>
        internal int Writes { get; private set; }

        /// <summary>只接受current organization與完整entity metadata messages，其他message視為越界。</summary>
        public virtual OrganizationResponse Execute(OrganizationRequest request)
        {
            Calls++;
            if (Failure is { } failure) throw failure;
            if (Fault == "wrong-response") return new OrganizationResponse();
            if (request is RetrieveCurrentOrganizationRequest)
                return new RetrieveCurrentOrganizationResponse { Results = { ["Detail"] = new OrganizationDetail
                { OrganizationId = OrganizationId, UniqueName = Fault == "foreign-organization" ? "production" : "goodshepherdback", OrganizationVersion = "9.1.0.0" } } };
            if (request is RetrieveAllEntitiesRequest metadata)
            {
                Assert.Equal(EntityFilters.All, metadata.EntityFilters);
                var rows = Entities.ToList();
                if (Fault == "empty-id") rows[0].MetadataId = Guid.Empty;
                if (Fault == "duplicate-id") rows[1].MetadataId = rows[0].MetadataId;
                if (Fault == "duplicate-name") rows[1].LogicalName = rows[0].LogicalName;
                return new RetrieveAllEntitiesResponse { Results = { ["EntityMetadata"] = rows.ToArray() } };
            }
            throw new InvalidOperationException("Unexpected metadata request.");
        }

        /// <summary>只接受組織語言欄位，不准讀取任何業務實體或資料欄。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            Calls++;
            Assert.Equal("organization", entityName);
            Assert.Equal(OrganizationId, id);
            Assert.Equal("languagecode", Assert.Single(columnSet.Columns));
            return new Entity("organization", id) { ["languagecode"] = 1028 };
        }

        /// <summary>只接受solution/publisher/component與filters/steps定義，敏感設定及任意業務列在此拒絕。</summary>
        public virtual EntityCollection RetrieveMultiple(QueryBase query)
        {
            Calls++;
            var expression = Assert.IsType<QueryExpression>(query);
            Assert.Contains(expression.EntityName, AllowedTables);
            Assert.False(expression.ColumnSet.AllColumns);
            Assert.DoesNotContain(expression.ColumnSet.Columns, value => value.Contains("config", StringComparison.OrdinalIgnoreCase));
            var source = expression.EntityName == "publisher" ? PublisherRows : expression.EntityName == "sdkmessagefilter" ? FilterRows : new List<Entity>();
            var page = Math.Max(1, expression.PageInfo.PageNumber);
            return new EntityCollection(source.Skip((page - 1) * 5000).Take(5000).ToList())
            { MoreRecords = Fault == "partial-page" || source.Count > page * 5000,
                PagingCookie = source.Count > page * 5000 ? page.ToString(System.Globalization.CultureInfo.InvariantCulture) : null };
        }

        /// <summary>建立具有正式metadata ID與主名稱欄的基礎實體；只有合成schema，不含業務記錄。</summary>
        private static EntityMetadata BaseEntity(string name, string primary)
        {
            var entity = new EntityMetadata { MetadataId = Guid.NewGuid(), LogicalName = name };
            // SDK的回應屬性由WCF設定而沒有公開setter；只對本案合成物件設定回應值，不修改產品或真連線。
            SetResponse(entity, "PrimaryIdAttribute", name + "id");
            SetResponse(entity, "PrimaryNameAttribute", primary);
            SetResponse(entity, "Attributes", new AttributeMetadata[] { new StringAttributeMetadata { MetadataId = Guid.NewGuid(), LogicalName = primary, MaxLength = 160 } });
            SetResponse(entity, "Keys", Array.Empty<EntityKeyMetadata>());
            return entity;
        }

        /// <summary>僅用於合成SDK回應的readonly屬性，等同序列化器建立已取得metadata，不是授權或真環境反射。</summary>
        internal static void SetResponse(object instance, string property, object value) =>
            instance.GetType().GetProperty(property)!.SetValue(instance, value);

        /// <summary>唯讀來源一旦意外Create立即拒絕並留計數，不模擬成功。</summary>
        public virtual Guid Create(Entity entity) { Writes++; throw new NotSupportedException(); }
        /// <summary>唯讀來源不得更新任何既有資料或metadata。</summary>
        public void Update(Entity entity) { Writes++; throw new NotSupportedException(); }
        /// <summary>唯讀來源不得刪除資料或既有結構。</summary>
        public void Delete(string entityName, Guid id) { Writes++; throw new NotSupportedException(); }
        /// <summary>本包不以關聯操作改變業務資料。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>本包不解除任何正式資料關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}

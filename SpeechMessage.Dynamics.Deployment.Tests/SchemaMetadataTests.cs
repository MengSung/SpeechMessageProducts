using System;
using System.Collections.Generic;
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

    /// <summary>單案SDK替身只供metadata與純組織語言讀取；不持有真正連線、credential或背景資源。</summary>
    internal sealed class MetadataService : IOrganizationService
    {
        /// <summary>metadata可讀表白名單獨立於產品定義，任何業務表皆拒絕。</summary>
        private static readonly string[] AllowedTables = { "solution", "publisher", "solutioncomponent", "sdkmessagefilter", "sdkmessageprocessingstep" };
        /// <summary>每案獨立組織身份，不能以白名單alias取代實際server組織ID。</summary>
        internal Guid OrganizationId { get; } = Guid.NewGuid();
        /// <summary>每案獨占的SDK來源，測試會mutation確認產品已脫離原物件。</summary>
        internal List<EntityMetadata> Entities { get; } = new() { BaseEntity("account", "name"), BaseEntity("contact", "fullname") };
        /// <summary>固定測試故障碼，不是使用者內容。</summary>
        internal string Fault { get; init; } = "";
        /// <summary>需要驗證保真的原始合成例外，不含真實秘密。</summary>
        internal Exception? Failure { get; set; }
        /// <summary>實際SDK呼叫次數，取消前應保持零。</summary>
        internal int Calls { get; private set; }
        /// <summary>任何寫入都不應被唯讀投影呼叫；方法一律拒絕，不能靠假成功掩蓋寫入。</summary>
        internal int Writes { get; private set; }

        /// <summary>只接受current organization與完整entity metadata messages，其他message視為越界。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
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
                Assert.True(metadata.RetrieveAsIfPublished);
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
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            Calls++;
            var expression = Assert.IsType<QueryExpression>(query);
            Assert.Contains(expression.EntityName, AllowedTables);
            Assert.False(expression.ColumnSet.AllColumns);
            Assert.DoesNotContain(expression.ColumnSet.Columns, value => value.Contains("config", StringComparison.OrdinalIgnoreCase));
            return new EntityCollection { MoreRecords = Fault == "partial-page" };
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
        public Guid Create(Entity entity) { Writes++; throw new NotSupportedException(); }
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

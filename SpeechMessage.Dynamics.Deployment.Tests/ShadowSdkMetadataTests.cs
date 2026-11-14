/*
 * 檔案整體說明
 *
 * 目的與定位：保護影子 CLI 寫入前的權威 metadata 邊界，以合成 SDK 回應驗證
 * 欄位存在、主鍵、可寫性與有界查詢，不連 D365、不取得秘密或推播 LINE。
 * 主要功能與流程：由真正 metadata adapter 讀取指定實體，再解碼候選欄位；
 * 注入錯配、缺漏、重複與唯讀欄位，確保首筆業務寫入前拒絕且不發布部分資料。
 * 使用邊界與維護：每案唯一擁有合成 metadata 及服務，不共用可變集合或連線。
 * JSON 文件在 using 範圍釋放，產品不擁有借入 SDK；測試不建立背景工作或暫存檔。
 * 這些離線斷言不代表實機授權、測試綁定、舊入口盤點或模組驗收已完成。
 */
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Shadow;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>離線核對寫入前的 metadata 契約，不以合成欄位推斷正式 schema。</summary>
public sealed class ShadowSdkMetadataTests
{
    /// <summary>缺少權威 metadata adapter 時必須先紅燈，不能讓 CLI 直接按 JSON 型別猜 SDK 值。</summary>
    [Fact]
    public void Metadata_AdapterExistsBeforeCliBinding() =>
        Assert.NotNull(typeof(Program).Assembly.GetType("SpeechMessage.Dynamics.Deployment.Shadow.ShadowSdkMetadata"));

    /// <summary>正式活動主鍵不是 logicalname+id；同表重複輸入只讀一次且完全沒有業務寫入。</summary>
    [Fact]
    public void Metadata_UsesAuthoritativePrimaryAndReadsEachEntityOnce()
    {
        var service = new MetadataService();
        var metadata = new ShadowSdkMetadata(service, ["task", "task"], CancellationToken.None);
        Assert.Equal("activityid", metadata.PrimaryId("task"));
        Assert.Equal(1, service.Reads);
        Assert.Equal(["subject", "activityid"], metadata.ValidateObservation("task", ["subject", "activityid"]));
        Assert.Throws<InvalidDataException>(() => metadata.PrimaryId("account"));
        Assert.Throws<InvalidDataException>(() => metadata.ValidateObservation("task", ["missing"]));
        Assert.Throws<InvalidDataException>(() => metadata.ValidateObservation("task", ["subject", "subject"]));
    }

    /// <summary>明確 Create／Update 權限各自核對；缺值不當成可寫，主鍵及秘密欄位在轉換前拒絕。</summary>
    /// <param name="field">故障欄位名稱。</param>
    /// <param name="create">目前建立或更新路徑。</param>
    [Theory]
    [InlineData("missing", true)]
    [InlineData("activityid", true)]
    [InlineData("readonly", true)]
    [InlineData("unknownpermission", true)]
    [InlineData("createonly", false)]
    [InlineData("updateonly", true)]
    [InlineData("new_lineid", true)]
    [InlineData("new_channeltoken", true)]
    public void Metadata_RejectsUnapprovedWrites(string field, bool create)
    {
        var service = new MetadataService();
        var metadata = new ShadowSdkMetadata(service, ["task"], CancellationToken.None);
        Assert.Throws<InvalidDataException>(() => Convert(metadata, "{\"" + field + "\":\"synthetic\"}", create));
        Assert.Equal(1, service.Reads);
    }

    /// <summary>標記只在正確型別解包，金額保留精度，lookup 每案核對別名並回傳獨立副本。</summary>
    [Fact]
    public void Metadata_DecodesTypedMarkersWithoutGuessingOrRetainingReferences()
    {
        var service = new MetadataService();
        var metadata = new ShadowSdkMetadata(service, ["task"], CancellationToken.None);
        var id = Guid.NewGuid();
        var source = new EntityReference("contact", id);
        using var input = JsonDocument.Parse("""
            {"subject":"synthetic","amount":{"money":1.25},"choice":{"option":7},
             "parent":{"reference":{"alias":"contact-1","entity":"contact"}}}
            """);
        var candidate = metadata.ConvertFields("task", input.RootElement, true, (alias, entity) =>
        {
            Assert.Equal("contact-1", alias);
            Assert.Equal("contact", entity);
            return source;
        });
        source.Id = Guid.NewGuid();
        Assert.Equal(Guid.Empty, candidate.Id);
        Assert.Equal("synthetic", candidate["subject"]);
        Assert.Equal(1.25m, candidate.GetAttributeValue<Money>("amount").Value);
        Assert.Equal(7, candidate.GetAttributeValue<OptionSetValue>("choice").Value);
        Assert.Equal(id, candidate.GetAttributeValue<EntityReference>("parent").Id);
        Assert.Equal("synthetic", Convert(metadata, """{"createonly":"synthetic"}""", true)["createonly"]);
        Assert.Equal("synthetic", Convert(metadata, """{"updateonly":"synthetic"}""", false)["updateonly"]);
    }

    /// <summary>重複欄位、錯型別標記或 marker 額外欄位不能採最後值或略過後繼續。</summary>
    /// <param name="json">不完整或模糊的候選。</param>
    [Theory]
    [InlineData("""{"subject":"a","subject":"b"}""")]
    [InlineData("""{"subject":{"money":1}}""")]
    [InlineData("""{"amount":{"money":1,"extra":2}}""")]
    [InlineData("""{"parent":{"reference":{"alias":"contact-1","entity":"contact"},"extra":2}}""")]
    public void Metadata_MalformedFieldsDoNotPublishPartialCandidate(string json)
    {
        var metadata = new ShadowSdkMetadata(new MetadataService(), ["task"], CancellationToken.None);
        Assert.Throws<InvalidDataException>(() => Convert(metadata, json, true));
    }

    /// <summary>不完整、錯表或重複 metadata 不能用於後續候選，並保留真正 SDK 故障給外層告警。</summary>
    /// <param name="fault">只改變本案 metadata 的故障。</param>
    [Theory]
    [InlineData("wrong-entity")]
    [InlineData("no-primary")]
    [InlineData("duplicate-field")]
    [InlineData("wrong-field-entity")]
    [InlineData("empty-fields")]
    [InlineData("overflow")]
    public void Metadata_RejectsIncompleteAuthoritativeResponse(string fault)
    {
        var service = new MetadataService();
        var row = service.Metadata;
        if (fault == "wrong-entity") row.LogicalName = "account";
        if (fault == "no-primary") Set(row, "PrimaryIdAttribute", "missing");
        if (fault == "duplicate-field") Set(row, "Attributes", row.Attributes.Concat([row.Attributes[0]]).ToArray());
        if (fault == "wrong-field-entity") Set(row.Attributes[0], "EntityLogicalName", "account");
        if (fault == "empty-fields") Set(row, "Attributes", Array.Empty<AttributeMetadata>());
        if (fault == "overflow") Set(row, "Attributes", Enumerable.Repeat<AttributeMetadata>(row.Attributes[0], 4097).ToArray());
        Assert.Throws<InvalidDataException>(() => new ShadowSdkMetadata(service, ["task"], CancellationToken.None));
    }

    /// <summary>取消前零查詢；SDK 完成當下取消不發布 adapter；傳輸例外保留原物件。</summary>
    [Fact]
    public void Metadata_CancellationAndSdkFailureNeverBecomeSuccess()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var service = new MetadataService();
        Assert.Throws<OperationCanceledException>(() => new ShadowSdkMetadata(service, ["task"], cancelled.Token));
        Assert.Equal(0, service.Reads);
        using var during = new CancellationTokenSource();
        service.OnRead = during.Cancel;
        Assert.Throws<OperationCanceledException>(() => new ShadowSdkMetadata(service, ["task"], during.Token));
        var failure = new InvalidOperationException("synthetic");
        service.OnRead = () => throw failure;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            new ShadowSdkMetadata(service, ["task"], CancellationToken.None)));
    }

    /// <summary>輸入名稱及實體數量在呼叫 SDK 前限制，避免無界 metadata 掃描或路徑語法。</summary>
    [Fact]
    public void Metadata_InvalidScopePerformsNoSdkReads()
    {
        var service = new MetadataService();
        Assert.Throws<InvalidDataException>(() => new ShadowSdkMetadata(service, [], CancellationToken.None));
        Assert.Throws<InvalidDataException>(() => new ShadowSdkMetadata(service, ["task/unsafe"], CancellationToken.None));
        Assert.Throws<InvalidDataException>(() => new ShadowSdkMetadata(service, Enumerable.Repeat("task", 65).ToArray(), CancellationToken.None));
        Assert.Equal(0, service.Reads);
    }

    /// <summary>每次獨占 JSON 文件並確定釋放；resolver 預設拒絕，不能自動產生可用的 lookup 身分。</summary>
    private static Entity Convert(ShadowSdkMetadata metadata, string json, bool create)
    {
        using var input = JsonDocument.Parse(json);
        return metadata.ConvertFields("task", input.RootElement, create, (_, _) => throw new InvalidDataException());
    }

    /// <summary>離線建立伺服器回應的唯讀 metadata 屬性，不在產品使用反射繞過權限。</summary>
    private static void Set(object target, string name, object value) =>
        target.GetType().GetProperty(name)!.SetValue(target, value);

    /// <summary>只允許指定 RetrieveEntity；所有業務 I/O 一律拋錯以證明 adapter 沒有副作用。</summary>
    private sealed class MetadataService : IOrganizationService
    {
        internal readonly EntityMetadata Metadata;
        internal int Reads;
        internal Action? OnRead;

        /// <summary>每案獨占正式 SDK 型別的合成欄位，權限與主鍵以獨立文字常值描述。</summary>
        internal MetadataService()
        {
            Metadata = new EntityMetadata { LogicalName = "task" };
            Set(Metadata, "PrimaryIdAttribute", "activityid");
            var option = new PicklistAttributeMetadata { LogicalName = "choice", OptionSet = new OptionSetMetadata() };
            option.OptionSet.Options.Add(new OptionMetadata(new Label("合成", 1028), 7));
            AttributeMetadata[] fields =
            [
                new UniqueIdentifierAttributeMetadata { LogicalName = "activityid" },
                new StringAttributeMetadata { LogicalName = "subject", MaxLength = 100 },
                new StringAttributeMetadata { LogicalName = "readonly", MaxLength = 100 },
                new StringAttributeMetadata { LogicalName = "unknownpermission", MaxLength = 100 },
                new StringAttributeMetadata { LogicalName = "createonly", MaxLength = 100 },
                new StringAttributeMetadata { LogicalName = "updateonly", MaxLength = 100 },
                new StringAttributeMetadata { LogicalName = "new_lineid", MaxLength = 100 },
                new StringAttributeMetadata { LogicalName = "new_channeltoken", MaxLength = 100 },
                new MoneyAttributeMetadata { LogicalName = "amount", MinValue = 0, MaxValue = 100 },
                option,
                new LookupAttributeMetadata { LogicalName = "parent", Targets = ["contact"] }
            ];
            foreach (var field in fields)
            {
                if (field.LogicalName == "unknownpermission") continue;
                Set(field, "IsValidForCreate", field.LogicalName is not ("readonly" or "activityid" or "updateonly"));
                Set(field, "IsValidForUpdate", field.LogicalName is not ("readonly" or "activityid" or "createonly"));
            }
            Set(Metadata, "Attributes", fields);
        }

        /// <summary>檢查實際送出的查詢範圍，不讓 adapter 以全表掃描取得合成成功。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            var read = Assert.IsType<RetrieveEntityRequest>(request);
            Assert.Equal("task", read.LogicalName);
            Assert.Equal(EntityFilters.Attributes, read.EntityFilters);
            Assert.True(read.RetrieveAsIfPublished);
            Reads++;
            OnRead?.Invoke();
            return new RetrieveEntityResponse { Results = { ["EntityMetadata"] = Metadata } };
        }
        /// <summary>不允許業務建立。</summary>
        public Guid Create(Entity entity) => throw new InvalidOperationException();
        /// <summary>不允許業務更新。</summary>
        public void Update(Entity entity) => throw new InvalidOperationException();
        /// <summary>不允許業務刪除。</summary>
        public void Delete(string entityName, Guid id) => throw new InvalidOperationException();
        /// <summary>不允許讀業務記錄。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new InvalidOperationException();
        /// <summary>不允許掃描業務記錄。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => throw new InvalidOperationException();
        /// <summary>不允許建立關聯。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new InvalidOperationException();
        /// <summary>不允許刪除關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new InvalidOperationException();
    }
}

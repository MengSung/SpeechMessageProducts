/*
 * 檔案整體說明
 *
 * 目的與定位：驗證奉獻類別轉換SDK adapter的metadata信任邊界、資料投影、寫入回應及服務借用生命週期。
 * 主要功能與流程：以單一合成IOrganizationService提供固定metadata及資料列，依序驗證查詢、Upsert、
 * ExecuteMultiple批次結果與Dispose後的借用服務釋放行為。
 * SyntheticService 僅回應固定的組織身份、1028語系、三個核定實體metadata及本測試列出的查詢／寫入，
 * 未列入白名單的SDK操作一律拒絕；測試不建立Connector、不讀秘密、不連Dataverse。
 * 故障注入包含錯誤語系、錯誤choice型別、不完整分頁、重複資料庫ID、ExecuteMultiple fault及caller取消。
 * 決定性斷言包含付款人parentcustomerid必須是account、Upsert只接受SDK回傳正式ID、
 * 批次錯碼只輸出數字碼、取消前不進SDK，以及Dispose後不再呼叫借入服務。
 * 使用邊界與維護：每案服務、store與合成集合皆由測試持有；store只借用服務，Dispose先清除其參照，
 * 外層測試再釋放替身；所有結果都是離線合成證據，不代表目前組織的metadata或資料狀態。
 * 所有結果均為離線合成證據，不表示goodshepherdback或其他組織目前的metadata與資料狀態。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Organization;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Offerings;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>以固定SDK替身測奉獻遷移adapter；每個case的服務scope完全獨立且拒絕未授權呼叫。</summary>
public sealed class OfferingMigrationSdkStoreTests
{
    private static readonly Guid OrganizationId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid ChurchId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ContactId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid FeeId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid CategoryId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly string[] ContactTargets = { "contact" };
    private static readonly string[] CategoryTargets = { "new_offeringcategory" };
    private static readonly string[] AccountTargets = { "account" };
    private static readonly string[] CategoryKeyAttributes = { "new_accountid", "new_code" };
    private static readonly OrganizationDefinition Target =
        new("goodshepherdback", "test", "https://localhost/Organization.svc", "off");

    /// <summary>正確付款人只以parentcustomerid account解析教會；輸出plan不含付款人或來源列ID。</summary>
    [Fact]
    public void CreatePlan_PayerAccountParent_ResolvesChurchWithoutPublishingSourceIdentity()
    {
        var service = new SyntheticService();
        service.Rows["account"].Add(new Entity("account", ChurchId));
        service.Rows["contact"].Add(new Entity("contact", ContactId)
            { ["parentcustomerid"] = new EntityReference("account", ChurchId) });
        service.Rows["new_fee"].Add(new Entity("new_fee", FeeId)
        {
            ["new_category"] = new OptionSetValue(100000000),
            ["new_contact_new_fee"] = new EntityReference("contact", ContactId)
        });
        using var store = new OfferingMigrationSdkStore(service, Target, default);

        var plan = store.CreatePlan(Array.Empty<OfferingScreenOption>());

        var category = Assert.Single(plan.Categories);
        Assert.Equal(ChurchId, category.ChurchId);
        Assert.Equal(1, category.RecordCount);
        Assert.Equal(0, plan.UnresolvedChurchRecordCount);
        Assert.DoesNotContain(FeeId.ToString("D"), System.Text.Json.JsonSerializer.Serialize(plan), StringComparison.Ordinal);
        Assert.DoesNotContain(ContactId.ToString("D"), System.Text.Json.JsonSerializer.Serialize(plan), StringComparison.Ordinal);
    }

    /// <summary>server語系不是核定1028時在讀取業務列前拒絕，不以本機文化或標籤預設值代替。</summary>
    [Fact]
    public void Constructor_NonTraditionalChineseOrganizationLanguage_RejectedBeforeBusinessQueries()
    {
        var service = new SyntheticService { LanguageCode = 1033 };

        Assert.Throws<InvalidDataException>(() => new OfferingMigrationSdkStore(service, Target, default));

        Assert.Equal(0, service.QueryCalls);
    }

    /// <summary>舊選項欄的metadata若非Picklist，即使SDK回傳數值也拒絕完整store。</summary>
    [Fact]
    public void Constructor_ChoiceAttributeWithWrongMetadataType_Rejected()
    {
        var service = new SyntheticService { WrongFeeOptionType = true };

        Assert.Throws<InvalidDataException>(() => new OfferingMigrationSdkStore(service, Target, default));

        Assert.Equal(0, service.QueryCalls);
    }

    /// <summary>alternate key必須明確包含在SDK metadata filter，否則實際回應不保證含Keys。</summary>
    [Fact]
    public void Constructor_RetrievesAlternateKeyMetadata()
    {
        var service = new SyntheticService();

        using var store = new OfferingMigrationSdkStore(service, Target, default);

        Assert.Contains(service.KeyRequests, request => request.Entity == "new_offeringcategory"
            && request.Key == "new_ak_offeringcategory" && !request.RetrieveAsIfPublished);
    }

    /// <summary>兩個來源choice可含不同選項值；plan按值聯集且保留各自來源的metadata標籤。</summary>
    [Fact]
    public void CreatePlan_DifferentChoiceSets_MergedByValue()
    {
        var service = new SyntheticService(differentChoiceSets: true);
        service.Rows["account"].Add(new Entity("account", ChurchId));
        service.Rows["contact"].Add(new Entity("contact", ContactId)
            { ["parentcustomerid"] = new EntityReference("account", ChurchId) });
        service.Rows["new_fee"].Add(new Entity("new_fee", FeeId)
        {
            ["new_category"] = new OptionSetValue(100000001),
            ["new_contact_new_fee"] = new EntityReference("contact", ContactId)
        });
        service.Rows["new_dedication_booking"].Add(new Entity("new_dedication_booking", Guid.NewGuid())
        {
            ["new_dedication_category"] = new OptionSetValue(100000002),
            ["new_contact_new_dedication_booking"] = new EntityReference("contact", ContactId)
        });
        using var store = new OfferingMigrationSdkStore(service, Target, default);

        var plan = store.CreatePlan(Array.Empty<OfferingScreenOption>());

        Assert.Collection(plan.Categories.OrderBy(item => item.LegacyOptionValue),
            item =>
            {
                Assert.Equal(100000001, item.LegacyOptionValue);
                Assert.Equal("fee-only label", item.Name);
            },
            item =>
            {
                Assert.Equal(100000002, item.LegacyOptionValue);
                Assert.Equal("booking-only label", item.Name);
            });
    }

    /// <summary>兩來源對同一choice值若標籤衝突，建構時拒絕避免以任一來源覆蓋權威文字。</summary>
    [Fact]
    public void Constructor_ConflictingLabelsForSharedChoiceValue_Rejected()
    {
        var service = new SyntheticService(differentChoiceSets: true, conflictingSharedLabel: true);

        Assert.Throws<InvalidDataException>(() => new OfferingMigrationSdkStore(service, Target, default));

        Assert.Equal(0, service.QueryCalls);
    }

    /// <summary>查詢要求續頁但沒有cookie時拒絕部分結果；不把首頁資料當完整來源投影。</summary>
    [Fact]
    public void ReadSourceRecords_MoreRecordsWithoutCookie_Rejected()
    {
        var service = new SyntheticService { IncompleteFeePage = true };
        using var store = new OfferingMigrationSdkStore(service, Target, default);

        Assert.Throws<InvalidDataException>(() => store.ReadSourceRecords());
    }

    /// <summary>來源集合內重複正式ID必須拒絕，不以Distinct或後值覆蓋處理身份衝突。</summary>
    [Fact]
    public void ReadSourceRecords_DuplicateDatabaseId_Rejected()
    {
        var service = new SyntheticService();
        service.Rows["new_fee"].Add(new Entity("new_fee", FeeId));
        service.Rows["new_fee"].Add(new Entity("new_fee", FeeId));
        using var store = new OfferingMigrationSdkStore(service, Target, default);

        Assert.Throws<InvalidDataException>(() => store.ReadSourceRecords());
    }

    /// <summary>Upsert只回傳SDK正式Target ID，並核對教會＋legacy代碼替代鍵完整送出。</summary>
    [Fact]
    public void UpsertCategory_UsesDatabaseReturnedTargetId()
    {
        var service = new SyntheticService { UpsertId = CategoryId };
        using var store = new OfferingMigrationSdkStore(service, Target, default);
        var category = new OfferingCategoryPlan(ChurchId, "legacy-100000000", "metadata label", 1,
            100000000, 0, "Unknown");

        Assert.Equal(CategoryId, store.UpsertCategory(category));

        Assert.Equal(1, service.UpsertCalls);
    }

    /// <summary>ExecuteMultiple逐筆Fault轉為安全數字碼；故障本文及記錄ID不進純值回應。</summary>
    [Fact]
    public void ExecuteMultiple_FaultReturnsOnlyNumericErrorCode()
    {
        var service = new SyntheticService { BatchFaultCode = unchecked((int)0x80040224) };
        using var store = new OfferingMigrationSdkStore(service, Target, default);
        var update = new OfferingLookupUpdate("new_fee", FeeId, ChurchId, CategoryId);

        var result = Assert.Single(store.ExecuteMultiple(new[] { update }));

        Assert.False(result.Succeeded);
        Assert.Equal("0x80040224", result.ErrorCode);
        Assert.DoesNotContain(FeeId.ToString("D"), result.ErrorCode, StringComparison.Ordinal);
    }

    /// <summary>建構前取消不得進入SDK；Dispose後再次操作拒絕且服務呼叫計數不再增加。</summary>
    [Fact]
    public void CancellationAndDispose_StopSdkReuseAndReleaseBorrowedReference()
    {
        var cancelledService = new SyntheticService();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            new OfferingMigrationSdkStore(cancelledService, Target, cancellation.Token));
        Assert.Equal(0, cancelledService.ExecuteCalls);

        var service = new SyntheticService();
        var store = new OfferingMigrationSdkStore(service, Target, default);
        var calls = service.ExecuteCalls;
        store.Dispose();
        Assert.Throws<ObjectDisposedException>(() => store.ReadSourceRecords());
        Assert.Equal(calls, service.ExecuteCalls);
    }

    /// <summary>固定metadata helper每次產生全新ID與label物件，避免測試間共享可變SDK metadata。</summary>
    private static EntityMetadata Metadata(string logicalName, bool wrongFeeOptionType = false,
        bool differentChoiceSets = false, bool conflictingSharedLabel = false)
    {
        var attributes = new List<AttributeMetadata>();
        if (logicalName == "new_fee")
        {
            attributes.Add(wrongFeeOptionType
                ? new StringAttributeMetadata { LogicalName = "new_category", MaxLength = 100 }
                : Choice("new_category", differentChoiceSets
                    ? new[] { (100000000, "shared label"), (100000001, "fee-only label") }
                    : new[] { (100000000, "metadata label") }));
            attributes.Add(new LookupAttributeMetadata { LogicalName = "new_contact_new_fee", Targets = ContactTargets });
            attributes.Add(new LookupAttributeMetadata { LogicalName = "new_offeringcategoryid", Targets = CategoryTargets });
        }
        else if (logicalName == "new_dedication_booking")
        {
            attributes.Add(Choice("new_dedication_category", differentChoiceSets
                ? new[]
                {
                    (100000000, conflictingSharedLabel ? "conflicting shared label" : "shared label"),
                    (100000002, "booking-only label")
                }
                : new[] { (100000000, "metadata label") }));
            attributes.Add(new LookupAttributeMetadata { LogicalName = "new_contact_new_dedication_booking", Targets = ContactTargets });
            attributes.Add(new LookupAttributeMetadata { LogicalName = "new_offeringcategoryid", Targets = CategoryTargets });
        }
        else
        {
            attributes.Add(new LookupAttributeMetadata { LogicalName = "new_accountid", Targets = AccountTargets });
            attributes.Add(new StringAttributeMetadata { LogicalName = "new_code", MaxLength = 100 });
            attributes.Add(new StringAttributeMetadata { LogicalName = "new_name", MaxLength = 300 });
            attributes.Add(new IntegerAttributeMetadata { LogicalName = "new_sortorder" });
            attributes.Add(new IntegerAttributeMetadata { LogicalName = "new_legacyoptionvalue" });
        }
        var metadata = new EntityMetadata { LogicalName = logicalName };
        SetResponse(metadata, "Attributes", attributes.ToArray());
        SetResponse(metadata, "Keys", logicalName == "new_offeringcategory"
            ? new[]
            {
                new EntityKeyMetadata
                {
                    LogicalName = "new_ak_offeringcategory",
                    KeyAttributes = CategoryKeyAttributes
                }
            }
            : Array.Empty<EntityKeyMetadata>());
        return metadata;
    }

    /// <summary>建構單一choice中繼資料；值／標籤完全由測試fixture提供且語系明確為1028。</summary>
    private static PicklistAttributeMetadata Choice(string logicalName, (int Value, string Label)[] options)
    {
        var metadata = new PicklistAttributeMetadata { LogicalName = logicalName, OptionSet = new OptionSetMetadata() };
        foreach (var (value, label) in options)
            metadata.OptionSet.Options.Add(new OptionMetadata(new Label(label, 1028), value));
        return metadata;
    }

    /// <summary>SDK唯讀回應透過反射設定，模擬WCF反序列化器填入無公開setter的欄位。</summary>
    private static void SetResponse(object instance, string property, object value) =>
        instance.GetType().GetProperty(property)!.SetValue(instance, value);

    /// <summary>拒絕未列明的IOrganizationService操作，作為本案唯一合成SDK owner。</summary>
    private sealed class SyntheticService : IOrganizationService
    {
        private readonly Dictionary<string, EntityMetadata> metadata;

        internal SyntheticService(bool differentChoiceSets = false, bool conflictingSharedLabel = false)
        {
            metadata = new Dictionary<string, EntityMetadata>(StringComparer.Ordinal)
            {
                ["new_fee"] = Metadata("new_fee", differentChoiceSets: differentChoiceSets),
                ["new_dedication_booking"] = Metadata("new_dedication_booking",
                    differentChoiceSets: differentChoiceSets, conflictingSharedLabel: conflictingSharedLabel),
                ["new_offeringcategory"] = Metadata("new_offeringcategory")
            };
        }

        /// <summary>本案固定server回應身份；不代表任何部署環境的真實組織。</summary>
        internal Guid ServerOrganizationId { get; } = OrganizationId;
        /// <summary>可注入基礎語系不符故障，且可斷言不讀取業務資料。</summary>
        internal int LanguageCode { get; init; } = 1028;
        /// <summary>注入choice metadata型別錯誤。</summary>
        internal bool WrongFeeOptionType { get; init; }
        /// <summary>注入非完整來源續頁。</summary>
        internal bool IncompleteFeePage { get; init; }
        /// <summary>Upsert的合成正式資料庫ID。</summary>
        internal Guid UpsertId { get; init; } = CategoryId;
        /// <summary>ExecuteMultiple fault數字碼，不包含本文。</summary>
        internal int BatchFaultCode { get; init; }
        /// <summary>各實體本案獨立的合成資料列集合。</summary>
        internal Dictionary<string, List<Entity>> Rows { get; } = new(StringComparer.Ordinal)
        {
            ["account"] = new(), ["contact"] = new(), ["new_fee"] = new(),
            ["new_dedication_booking"] = new(), ["new_offeringcategory"] = new()
        };
        /// <summary>記錄指定alternate key的獨立SDK查詢，確保不依EntityMetadata.Keys附帶回應。</summary>
        internal List<(string Entity, string Key, bool RetrieveAsIfPublished)> KeyRequests { get; } = new();
        /// <summary>Execute訊息呼叫次數，用於取消及dispose後不再使用服務的斷言。</summary>
        internal int ExecuteCalls { get; private set; }
        /// <summary>分頁查詢呼叫次數，錯誤語系／欄位型別不得開始業務讀取。</summary>
        internal int QueryCalls { get; private set; }
        /// <summary>Upsert要求次數。</summary>
        internal int UpsertCalls { get; private set; }

        /// <summary>只回應身份、固定metadata、Upsert及ExecuteMultiple，其餘訊息即視為越界。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            ExecuteCalls++;
            if (request is RetrieveCurrentOrganizationRequest)
                return new RetrieveCurrentOrganizationResponse
                {
                    Results =
                    {
                        ["Detail"] = new OrganizationDetail
                        {
                            OrganizationId = ServerOrganizationId,
                            UniqueName = "goodshepherdback",
                            OrganizationVersion = "9.1.0.0"
                        }
                    }
                };
            if (request is RetrieveEntityRequest retrieve)
            {
                if (WrongFeeOptionType && retrieve.LogicalName == "new_fee")
                    metadata["new_fee"] = Metadata("new_fee", wrongFeeOptionType: true);
                var entityMetadata = metadata[retrieve.LogicalName];
                if (retrieve.LogicalName == "new_offeringcategory")
                {
                    entityMetadata = Metadata("new_offeringcategory");
                    SetResponse(entityMetadata, "Keys", Array.Empty<EntityKeyMetadata>());
                }
                return new RetrieveEntityResponse { Results = { ["EntityMetadata"] = entityMetadata } };
            }
            if (request is RetrieveEntityKeyRequest keyRequest)
            {
                KeyRequests.Add((keyRequest.EntityLogicalName, keyRequest.LogicalName,
                    keyRequest.RetrieveAsIfPublished));
                var key = metadata[keyRequest.EntityLogicalName].Keys!.Single(item =>
                    item.LogicalName == keyRequest.LogicalName);
                return new RetrieveEntityKeyResponse { Results = { ["EntityKeyMetadata"] = key } };
            }
            if (request is UpsertRequest upsert)
            {
                UpsertCalls++;
                Assert.Equal("new_offeringcategory", upsert.Target.LogicalName);
                Assert.Equal(new EntityReference("account", ChurchId), upsert.Target.KeyAttributes["new_accountid"]);
                Assert.Equal("legacy-100000000", upsert.Target.KeyAttributes["new_code"]);
                return new UpsertResponse { Results = { ["Target"] = new EntityReference("new_offeringcategory", UpsertId) } };
            }
            if (request is ExecuteMultipleRequest batch)
            {
                Assert.Single(batch.Requests);
                var response = new ExecuteMultipleResponse();
                var items = new ExecuteMultipleResponseItemCollection();
                if (BatchFaultCode != 0)
                    items.Add(new ExecuteMultipleResponseItem
                    {
                        RequestIndex = 0,
                        Fault = new OrganizationServiceFault { ErrorCode = BatchFaultCode, Message = "synthetic-sensitive-text" }
                    });
                else
                    items.Add(new ExecuteMultipleResponseItem { RequestIndex = 0, Response = new UpdateResponse() });
                response.Results["Responses"] = items;
                return response;
            }
            throw new InvalidOperationException("Synthetic SDK request is not allowlisted.");
        }

        /// <summary>只回organization.languagecode的合成資料，不提供其他實體或欄位。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            Assert.Equal("organization", entityName);
            Assert.Equal(ServerOrganizationId, id);
            Assert.Equal("languagecode", Assert.Single(columnSet.Columns));
            return new Entity("organization", id) { ["languagecode"] = LanguageCode };
        }

        /// <summary>只回核准的五種實體及明列資料；可對收費單注入壞cookie續頁。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            QueryCalls++;
            var expression = Assert.IsType<QueryExpression>(query);
            Assert.Contains(expression.EntityName, Rows.Keys);
            var rows = Rows[expression.EntityName];
            var firstPage = expression.PageInfo.PageNumber == 1;
            return new EntityCollection(rows.ToList())
            {
                MoreRecords = IncompleteFeePage && expression.EntityName == "new_fee" && firstPage,
                PagingCookie = null
            };
        }

        /// <summary>遷移工具只允許Upsert與ExecuteMultiple，不得使用Create避開替代鍵原子性。</summary>
        public Guid Create(Entity entity) => throw new NotSupportedException();
        /// <summary>所有明確更新皆須經過ExecuteMultiple批次邊界。</summary>
        public void Update(Entity entity) => throw new NotSupportedException();
        /// <summary>本adapter沒有刪除既有類別或來源資料的權限。</summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        /// <summary>本adapter不建立關聯集合。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>本adapter不移除關聯集合。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}

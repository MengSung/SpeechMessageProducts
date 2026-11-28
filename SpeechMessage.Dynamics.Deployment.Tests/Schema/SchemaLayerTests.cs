/*
檔案整體說明
目的與定位：驗證 FS schema 分層解析、實體／欄位衝突、產品隔離及新實體 ownership 契約。
主要流程：對正式舊 schema 核對位元組 SHA 與合併後 canonical hash；以每案獨立的臨時 repo fixture
注入跨層重複實體、平台欄位延伸、欄位重複、缺少 ownership 及租戶業務實體錯誤 ownership。
依賴與隔離：透過 Deployment parser／planner 與公開 CLI，外部 SDK 以每案專屬替身取代，所有端點與資料均為合成值；
fixture 由本測試建立並在 Dispose 刪除；CLI 租約確定歸還，不讀 User Secrets、不連接 D365。
空 UI 替身於兩來源入場辨識表單 FetchXML，還原查詢後依原始格式包裝 alias 空頁；
其他 QueryExpression 仍走原 metadata 白名單，不新增資料或放寬既有產品隔離斷言。
故障注入與決定性斷言：合法控制 fixture 必須能讀取；每種衝突必須拒絕完整候選，
construction／association 的結果不得含教會專屬 entity，且空層合併不得產生部分 schema。
正式平台契約另以三組織的真實分層清單驗證加密憑證安全、工作冪等與租戶文字唯一鍵；
固定期望值來自已核定的既有欄位，不以被測清單反推，能攔下欄位或約束漏帶的回歸。
驗證限制：hash 與所有 parser 結果只屬離線證據，不代表任一組織已 plan、apply、verify 或發布。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Xunit;
using SpeechMessage.Dynamics.Deployment.Schema;
using SpeechMessage.Dynamics.Deployment.Tests.Ui;

namespace SpeechMessage.Dynamics.Deployment.Tests.Schema;

/// <summary>保護多層 schema 的完整合併與 fail-closed 契約；測試 fixture 為短生命週期合成檔案。</summary>
[Collection("Deployment working directory")]
public sealed class SchemaLayerTests
{
    /// <summary>
    /// 平台資料夾新增第二份欄位清單時，layer 層級的公用檢視只載入一次；
    /// 若按每個檔案重複附加，後續 plan 會錯誤出現兩個同名操作。
    /// </summary>
    [Fact]
    public void Layers_MultiplePlatformFiles_LoadViewsExactlyOnce()
    {
        using var fixture = SchemaLayerFixture.Create(
            """{"schemaVersion":1,"organizations":{"construction":["platform"]},"views":{"platform":[{"entity":"account","name":"Tenant view","columns":["new_first"]}]}}""",
            new Dictionary<string, string>
            {
                ["platform/00-core.schema.json"] = SchemaLayerFixture.Schema(SchemaLayerFixture.Account("new_first")),
                ["platform/10-extension.schema.json"] = SchemaLayerFixture.Schema(SchemaLayerFixture.Account("new_second"))
            });
        Assert.Single(SchemaFiles.LoadForOrganization(fixture.Root, "construction").Views);
    }

    /// <summary>同一個實體／名稱的兩個檢視定義不得依最後一筆或建立兩次處理；完整組合必須拒絕。</summary>
    [Fact]
    public void Layers_DuplicateViewDefinition_Refused()
    {
        using var fixture = SchemaLayerFixture.Create(
            """{"schemaVersion":1,"organizations":{"construction":["platform"]},"views":{"platform":[{"entity":"account","name":"Tenant view","columns":["new_first"]},{"entity":"account","name":"Tenant view","columns":["new_first"]}]}}""",
            new Dictionary<string, string>
            { ["platform/00-core.schema.json"] = SchemaLayerFixture.Schema(SchemaLayerFixture.Account("new_first")) });
        Assert.Throws<InvalidDataException>(() => SchemaFiles.LoadForOrganization(fixture.Root, "construction"));
    }

    /// <summary>合法名稱但不在清單的條件欄位仍屬未知輸入；不能等到 XML 產生或寫入階段才發現。</summary>
    [Fact]
    public void Layers_UnknownFilterAttribute_Refused()
    {
        using var fixture = SchemaLayerFixture.Create(
            """{"schemaVersion":1,"organizations":{"construction":["platform"]},"views":{"platform":[{"entity":"account","name":"Tenant view","columns":["new_first"],"filter":["new_unknown"]}]}}""",
            new Dictionary<string, string>
            { ["platform/00-core.schema.json"] = SchemaLayerFixture.Schema(SchemaLayerFixture.Account("new_first")) });
        Assert.Throws<InvalidDataException>(() => SchemaFiles.LoadForOrganization(fixture.Root, "construction"));
    }

    /// <summary>
    /// 教會專屬表誤放平台資料夾也必須攔截；用合法的新表候選避免測試只因格式錯誤而通過，
    /// 兩個非教會組織均不得取得可交給 planner 的候選。
    /// </summary>
    [Theory]
    [InlineData("construction", "new_fee")]
    [InlineData("construction", "new_offeringcategory")]
    [InlineData("construction", "new_dedication_booking")]
    [InlineData("construction", "new_stor_lessons")]
    [InlineData("association", "new_fee")]
    [InlineData("association", "new_offeringcategory")]
    [InlineData("association", "new_dedication_booking")]
    [InlineData("association", "new_stor_lessons")]
    public void Layers_ChurchEntityMisplacedInPlatform_Refused(string organization, string entity)
    {
        using var fixture = SchemaLayerFixture.Create(
            "{\"schemaVersion\":1,\"organizations\":{\"" + organization + "\":[\"platform\"]}}",
            new Dictionary<string, string>
            {
                ["platform/00-core.schema.json"] = SchemaLayerFixture.Schema(
                    SchemaLayerFixture.NewEntity(entity, "UserOwned", tenantLookup: false))
            });
        Assert.Throws<InvalidDataException>(() => SchemaFiles.LoadForOrganization(fixture.Root, organization));
    }

    /// <summary>
    /// 三個新產品的公開 plan --org 必須實際讀分層清單並產生結構計畫，不要求 Plugin DLL。
    /// 只替換外部 SDK；測試檢查正式計畫內容與租約歸還，避免純 parser 測試掩蓋未接線入口。
    /// </summary>
    [Theory]
    [InlineData("goodshepherd20")]
    [InlineData("construction")]
    [InlineData("association")]
    public void Layers_PublicPlan_UsesOrganizationComposition(string organization)
    {
        var catalog = OrganizationCatalog.LoadDefault();
        using var fixture = SchemaLayerFixture.FromRepository();
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        var connector = new LayeredConnector(organization);
        var failures = new List<Exception>();
        var previous = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = fixture.Root;
            var result = DeploymentCli.RunWithCatalog(
                ["plan", "--org", organization], catalog, connector, output, failures.Add, null,
                Path.Combine(fixture.Root, "artifacts", "d365"));
            Assert.Empty(failures);
            Assert.Equal(0, result);
            var plan = SchemaFiles.ReadPlan(Assert.Single(
                Directory.GetFiles(fixture.Root, "schema-plan.json", SearchOption.AllDirectories)));
            using var json = JsonDocument.Parse(File.ReadAllText(Assert.Single(
                Directory.GetFiles(fixture.Root, "schema-plan.json", SearchOption.AllDirectories))));
            Assert.True(json.RootElement.TryGetProperty("ui", out var ui), "Public layered plan must include its UI operations.");
            Assert.Single(ui.GetProperty("views").EnumerateArray());
            Assert.Equal(organization, plan.Organization);
            Assert.Contains(plan.Operations, operation =>
                operation.Entity == "new_linechannel" && operation.Name == "new_accesstokenencrypted");
            Assert.Contains(plan.Operations, operation =>
                operation.Kind == "CreateKey" && operation.Name == "new_ak_localizedtext");
            Assert.Equal(1, connector.Returned);
            Assert.Equal(0, connector.Service.Writes);
        }
        finally { Environment.CurrentDirectory = previous; }
    }

    /// <summary>只在本測試存活的計數租約來源；沒有真實連線、秘密或背景資源，返回即遞增清理證據。</summary>
    private sealed class LayeredConnector(string organization) : IOrganizationConnector
    {
        /// <summary>每案獨立的只讀 metadata 來源，不接受任何業務資料寫入。</summary>
        internal LayeredMetadataService Service { get; } = new(organization);
        /// <summary>已確定歸還的命令租約數，不跨測試共用。</summary>
        internal int Returned { get; private set; }
        /// <summary>只借出本案指定組織的合成 SDK；命令清理必須呼叫唯一 release 委派。</summary>
        public OrganizationLease Connect(string name)
        {
            Assert.Equal(organization, name);
            return new OrganizationLease(Service, () => Returned++);
        }
    }

    /// <summary>沿用嚴格唯讀 metadata 替身，僅將本案合成組織名稱帶入 SDK 正式身分回應。</summary>
    private sealed class LayeredMetadataService : SchemaMetadataTests.MetadataService
    {
        private readonly string organization;

        /// <summary>
        /// 建立本案專屬 SDK 回應；好牧人組織包含三張舊業務表及 F0 已觀察的兩張
        /// 不可變 owner 表。固定管理 ID 僅用於合成契約，不能宣稱此案例讀取了實機。
        /// 最小 metadata 讓 planner 規劃缺項；不放寬缺表、錯身分或 owner 漂移的拒絕。
        /// </summary>
        internal LayeredMetadataService(string organization)
        {
            this.organization = organization;
            if (organization != "goodshepherd20") return;
            OrganizationId = Guid.Parse("ec4d281b-bbe7-f111-8cce-00155d007916");
            foreach (var baseline in new[]
            {
                ("new_localizedtext", "f417a3e7-d3d9-f111-8cca-00155d007916"),
                ("new_offeringcategory", "8407cdf5-d3d9-f111-8cca-00155d007916")
            })
            {
                var entity = new EntityMetadata
                {
                    MetadataId = Guid.Parse(baseline.Item2),
                    LogicalName = baseline.Item1,
                    OwnershipType = OwnershipTypes.OrganizationOwned,
                    IsAuditEnabled = new BooleanManagedProperty(true)
                };
                SetResponse(entity, "IsManaged", false);
                SetResponse(entity, "PrimaryIdAttribute", baseline.Item1 + "id");
                SetResponse(entity, "PrimaryNameAttribute", "new_name");
                SetResponse(entity, "Attributes", Array.Empty<AttributeMetadata>());
                SetResponse(entity, "Keys", Array.Empty<EntityKeyMetadata>());
                Entities.Add(entity);
            }
            foreach (var name in new[] { "new_fee", "new_dedication_booking", "new_stor_lessons" })
            {
                var entity = new EntityMetadata { MetadataId = Guid.NewGuid(), LogicalName = name };
                SetResponse(entity, "PrimaryIdAttribute", name + "id");
                SetResponse(entity, "PrimaryNameAttribute", "new_name");
                SetResponse(entity, "Attributes", new AttributeMetadata[]
                {
                    new StringAttributeMetadata { MetadataId = Guid.NewGuid(), LogicalName = "new_name", MaxLength = 100 }
                });
                SetResponse(entity, "Keys", Array.Empty<EntityKeyMetadata>());
                Entities.Add(entity);
            }
        }

        /// <summary>
        /// 保留基類完整 metadata 與故障邊界，僅替換本案唯一的組織名稱，不修改任何產品查核。
        /// 未發布表單由共用轉接器辨識固定FetchXML，空頁按原始查詢包裝，不補造元件或持有查詢。
        /// </summary>
        public override OrganizationResponse Execute(OrganizationRequest request)
        {
            // 官方唯讀轉換只模擬型別對應；原讀取／分頁／故障注入仍由既有替身負責。
            if (UiFormFetchTestAdapter.Convert(request) is { } converted) return converted;
            if (request is RetrieveUnpublishedMultipleRequest unpublished)
            {
                var query = unpublished.Query;
                _ = UiFormFetchTestAdapter.Query(query);
                return new RetrieveUnpublishedMultipleResponse
                { Results = { ["EntityCollection"] = UiFormFetchTestAdapter.Wrap(query, new EntityCollection()) } };
            }
            var response = base.Execute(request);
            if (response is RetrieveCurrentOrganizationResponse current)
                current.Detail.UniqueName = organization;
            return response;
        }

        /// <summary>
        /// 本合成空 UI 組織沒有任何表單或檢視；先還原表單FetchXML再以原始查詢包裝空回應。
        /// 其他QueryExpression保持原物件並經基類嚴格metadata白名單，不把未知查詢一律當成空UI。
        /// </summary>
        public override EntityCollection RetrieveMultiple(QueryBase query)
        {
            var expression = UiFormFetchTestAdapter.Query(query);
            if (expression.EntityName is "savedquery" or "systemform")
                return UiFormFetchTestAdapter.Wrap(query, new EntityCollection());
            return base.RetrieveMultiple(query);
        }
    }

    /// <summary>
    /// 三產品必須保留 Channel ID 唯一性與加密憑證的欄位安全；缺欄、縮短容量或移除
    /// secured 都必須失敗。讀真實清單但不讀 Channel 資料，fixture 在每案結束時釋放。
    /// </summary>
    [Theory]
    [InlineData("goodshepherd20")]
    [InlineData("construction")]
    [InlineData("association")]
    public void Platform_LineChannel_CredentialContract(string organization)
    {
        using var fixture = SchemaLayerFixture.FromRepository();
        var schema = SchemaFiles.LoadForOrganization(fixture.Root, organization);
        var entity = Assert.Single(schema.Entities, item => item.LogicalName == "new_linechannel");

        Assert.Equal("OrganizationOwned", entity.OwnershipType);
        Assert.Equal("new_name", entity.PrimaryName);
        Assert.False(entity.Audit);
        AssertField(entity, "new_name", "string", maxLength: 100, required: true);
        AssertField(entity, "new_channelid", "string", maxLength: 20);
        AssertField(entity, "new_accesstokenencrypted", "memo", maxLength: 4000, secured: true);
        AssertField(entity, "new_channelsecretencrypted", "memo", maxLength: 1000, secured: true);
        AssertField(entity, "new_richmenucapacityused", "integer");
        var key = Assert.Single(entity.Keys, item => item.LogicalName == "new_ak_linechannel");
        Assert.Equal("new_channelid", Assert.Single(key.Attributes));
    }

    /// <summary>
    /// 送出工作必須完整保留可領取、重試與完成的欄位及樂觀並行政策；固定選項與冪等鍵
    /// 防止跨程序重送或舊 worker 誤讀狀態。缺少其中一項即失敗，不以 parser 成功替代契約。
    /// </summary>
    [Theory]
    [InlineData("goodshepherd20")]
    [InlineData("construction")]
    [InlineData("association")]
    public void Platform_OutboundTask_Contract(string organization)
    {
        using var fixture = SchemaLayerFixture.FromRepository();
        var schema = SchemaFiles.LoadForOrganization(fixture.Root, organization);
        var entity = Assert.Single(schema.Entities, item => item.LogicalName == "new_outboundtask");

        Assert.Equal("OrganizationOwned", entity.OwnershipType);
        Assert.Equal("new_name", entity.PrimaryName);
        Assert.False(entity.Audit);
        Assert.True(entity.OptimisticConcurrency);
        AssertField(entity, "new_name", "string", maxLength: 300, required: true);
        var type = AssertField(entity, "new_type", "choice");
        Assert.Equal(new[]
        {
            new SchemaOption(100000000, "RichMenuGenerate"),
            new SchemaOption(100000001, "QrImageGenerate"),
            new SchemaOption(100000002, "AllotQuery"),
            new SchemaOption(100000003, "MemberNotifyRelay")
        }, type.Options);
        var status = AssertField(entity, "new_taskstatus", "choice");
        Assert.Equal(new[]
        {
            new SchemaOption(100000000, "Pending"),
            new SchemaOption(100000001, "Processing"),
            new SchemaOption(100000002, "Completed"),
            new SchemaOption(100000003, "Failed")
        }, status.Options);
        AssertField(entity, "new_accountid", "lookup", required: true, target: "account");
        AssertField(entity, "new_contactid", "lookup", target: "contact");
        AssertField(entity, "new_payload", "memo", maxLength: 4000);
        AssertField(entity, "new_idempotencykey", "string", maxLength: 200);
        AssertField(entity, "new_attempts", "integer");
        foreach (var name in new[] { "new_nextattempton", "new_claimedon", "new_completedon" })
        {
            var timestamp = AssertField(entity, name, "datetime");
            Assert.Equal("UserLocal", timestamp.Behavior);
            Assert.Equal("DateAndTime", timestamp.Format);
        }

        AssertField(entity, "new_lasterror", "string", maxLength: 400);
        var key = Assert.Single(entity.Keys, item => item.LogicalName == "new_ak_outboundtask");
        Assert.Equal("new_idempotencykey", Assert.Single(key.Attributes));
    }

    /// <summary>
    /// 文字覆寫須以租戶、文字鍵、語系共同唯一，保留使用者擁有權與稽核；租戶 lookup
    /// 或複合鍵漏帶都會破壞隔離，故三組織各自以完整分層輸入作決定性斷言。
    /// </summary>
    [Theory]
    [InlineData("goodshepherd20")]
    [InlineData("construction")]
    [InlineData("association")]
    public void Platform_LocalizedText_Contract(string organization)
    {
        using var fixture = SchemaLayerFixture.FromRepository();
        var schema = SchemaFiles.LoadForOrganization(fixture.Root, organization);
        var entity = Assert.Single(schema.Entities, item => item.LogicalName == "new_localizedtext");

        Assert.Equal("UserOwned", entity.OwnershipType);
        Assert.Equal("new_name", entity.PrimaryName);
        Assert.True(entity.Audit);
        AssertField(entity, "new_name", "string", maxLength: 300, required: true);
        AssertField(entity, "new_accountid", "lookup", required: true, target: "account");
        AssertField(entity, "new_key", "string", maxLength: 200);
        AssertField(entity, "new_culture", "string", maxLength: 16);
        AssertField(entity, "new_text", "memo", maxLength: 2000);
        var key = Assert.Single(entity.Keys, item => item.LogicalName == "new_ak_localizedtext");
        Assert.Collection(key.Attributes,
            attribute => Assert.Equal("new_accountid", attribute),
            attribute => Assert.Equal("new_key", attribute),
            attribute => Assert.Equal("new_culture", attribute));
    }

    /// <summary>
    /// 依呼叫端獨立列出的欄位契約核對唯一欄、型別、階段、容量與安全限制；
    /// 僅借用已載入的純值，不重算期望值，也不保留 fixture 或建立外部資源。
    /// </summary>
    private static SchemaField AssertField(SchemaEntity entity, string name, string type,
        int maxLength = 0, bool required = false, bool secured = false, string target = "")
    {
        var field = Assert.Single(entity.Fields, item => item.LogicalName == name);
        Assert.Equal(type, field.Type);
        Assert.Equal(1, field.Phase);
        Assert.Equal(maxLength, field.MaxLength);
        Assert.Equal(required, field.Required);
        Assert.Equal(secured, field.Secured);
        Assert.Equal(target, field.Target);
        return field;
    }

    /// <summary>
    /// 好牧人產品層拆分不得遺失原奉獻類別的業務欄位與租戶代碼唯一鍵；
    /// 使用正式組合及固定期望值，缺欄、錯階段或不相容層均須使離線回歸失敗。
    /// </summary>
    [Fact]
    public void Layers_GoodShepherd2_OfferingCategoryContractPreserved()
    {
        using var fixture = SchemaLayerFixture.FromRepository();
        var schema = SchemaFiles.LoadForOrganization(fixture.Root, "goodshepherd20");
        Assert.Equal(8, schema.ActivePhase);
        var entity = Assert.Single(schema.Entities, item => item.LogicalName == "new_offeringcategory");
        Assert.Equal("UserOwned", entity.OwnershipType);
        Assert.True(entity.Audit);
        AssertField(entity, "new_name", "string", maxLength: 100, required: true);
        AssertField(entity, "new_accountid", "lookup", required: true, target: "account");
        AssertField(entity, "new_code", "string", maxLength: 50);
        AssertField(entity, "new_sortorder", "integer");
        AssertField(entity, "new_accountingcode", "string", maxLength: 50);
        AssertField(entity, "new_legacyoptionvalue", "integer");
        var key = Assert.Single(entity.Keys, item => item.LogicalName == "new_ak_offeringcategory");
        Assert.Collection(key.Attributes,
            attribute => Assert.Equal("new_accountid", attribute),
            attribute => Assert.Equal("new_code", attribute));
    }

    /// <summary>
    /// 產品實體可參照另一核定層的 account；先各層格式驗證，再完整合併核對目標。
    /// 合成產品層刻意不含 account，避免以重複宣告繞過跨層契約。
    /// </summary>
    [Fact]
    public void Layers_CrossLayerLookup_ValidatedAfterMerge()
    {
        using var fixture = SchemaLayerFixture.Create(
            """{"schemaVersion":1,"organizations":{"construction":["platform","goodbutler"]}}""",
            new Dictionary<string, string>
            {
                ["platform/00-core.schema.json"] = SchemaLayerFixture.Schema(SchemaLayerFixture.Account("new_tenantcode")),
                ["goodbutler.schema.json"] = SchemaLayerFixture.Schema(
                    SchemaLayerFixture.NewEntity("new_business", "UserOwned", tenantLookup: true))
            });

        var schema = SchemaFiles.LoadForOrganization(fixture.Root, "construction");
        var entity = Assert.Single(schema.Entities, item => item.LogicalName == "new_business");
        AssertField(entity, "new_accountid", "lookup", required: true, target: "account");
    }

    /// <summary>
    /// 延後跨層參照驗證不可接受真正缺少的目標；產品層故意引用未納入組合的 contact，
    /// 完整組合仍須拋資料錯誤，不能回傳部分合法的候選給 planner。
    /// </summary>
    [Fact]
    public void Layers_UnknownLookupInMergedSchema_Rejected()
    {
        using var fixture = SchemaLayerFixture.Create(
            """{"schemaVersion":1,"organizations":{"construction":["platform","goodbutler"]}}""",
            new Dictionary<string, string>
            {
                ["platform/00-core.schema.json"] = SchemaLayerFixture.Schema(SchemaLayerFixture.Account("new_tenantcode")),
                ["goodbutler.schema.json"] = SchemaLayerFixture.Schema(
                    SchemaLayerFixture.NewEntity("new_business", "UserOwned", tenantLookup: true)
                        .Replace("\"target\":\"account\"", "\"target\":\"contact\"", StringComparison.Ordinal))
            });

        Assert.Throws<InvalidDataException>(() => SchemaFiles.LoadForOrganization(fixture.Root, "construction"));
    }

    /// <summary>舊 core 檔逐位元組維持 FS 前版本，goodshepherdback 合併 schema 的 canonical hash 也須完全相同。</summary>
    [Fact]
    public void Layers_GoodShepherdBack_MergedSchemaUnchanged()
    {
        var path = SchemaDefinitionTests.SchemaPath();
        var bytes = File.ReadAllBytes(path);
        Assert.Equal("AE008B2B75F9AB35EAFC78A18197CC9300676AA5A53272D33AC0B05BE3185310",
            Convert.ToHexString(SHA256.HashData(bytes)));

        using var fixture = SchemaLayerFixture.FromRepository();
        var before = SchemaFiles.Load(path);
        var merged = SchemaFiles.LoadForOrganization(fixture.Root, "goodshepherdback");

        Assert.Equal(SchemaFiles.Hash(before), SchemaFiles.Hash(merged));
    }

    /// <summary>兩個非教會產品的正式 layer composition 都不得帶入奉獻或課程 entity。</summary>
    [Fact]
    public void Plan_ConstructionOrg_ContainsNoChurchEntities()
    {
        using var fixture = SchemaLayerFixture.FromRepository();
        var forbidden = new[]
        {
            "new_fee",
            "new_offeringcategory",
            "new_dedication_booking",
            "new_stor_lessons"
        };

        foreach (var organization in new[] { "construction", "association" })
        {
            var schema = SchemaFiles.LoadForOrganization(fixture.Root, organization);
            Assert.Empty(schema.Entities.Select(entity => entity.LogicalName).Intersect(forbidden, StringComparer.Ordinal));
        }
    }

    /// <summary>同一平台目錄可在不同 schema 檔擴充同一實體的不同欄位，合併後兩欄都完整保留。</summary>
    [Fact]
    public void Layers_PlatformFolder_EntityExtendedAcrossFiles_Merged()
    {
        using var fixture = SchemaLayerFixture.Create(
            """{"schemaVersion":1,"organizations":{"goodshepherdback":["speechmessagecore"],"construction":["platform"]}}""",
            new Dictionary<string, string>
            {
                ["speechmessagecore.schema.json"] = SchemaLayerFixture.Schema(SchemaLayerFixture.Account("new_old")),
                ["platform/00-core.schema.json"] = SchemaLayerFixture.Schema(SchemaLayerFixture.Account("new_first")),
                ["platform/10-extension.schema.json"] = SchemaLayerFixture.Schema(SchemaLayerFixture.Account("new_second"))
            });

        var merged = SchemaFiles.LoadForOrganization(fixture.Root, "construction");
        var account = Assert.Single(merged.Entities);

        Assert.Contains(account.Fields, field => field.LogicalName == "new_first");
        Assert.Contains(account.Fields, field => field.LogicalName == "new_second");
    }

    /// <summary>產品層不得重複定義平台層 entity；即使其欄位不同也拒絕跨層覆蓋。</summary>
    [Fact]
    public void Layers_DuplicateEntityAcrossLayers_Rejected()
    {
        using var fixture = SchemaLayerFixture.Create(
            """{"schemaVersion":1,"organizations":{"goodshepherdback":["speechmessagecore"],"goodshepherd20":["platform","goodshepherd"]}}""",
            new Dictionary<string, string>
            {
                ["speechmessagecore.schema.json"] = SchemaLayerFixture.Schema(SchemaLayerFixture.Account("new_old")),
                ["platform/00-core.schema.json"] = SchemaLayerFixture.Schema(SchemaLayerFixture.Account("new_platform")),
                ["goodshepherd.schema.json"] = SchemaLayerFixture.Schema(SchemaLayerFixture.Account("new_product"))
            });

        Assert.Throws<InvalidDataException>(() => SchemaFiles.LoadForOrganization(fixture.Root, "goodshepherd20"));
    }

    /// <summary>平台資料夾的欄位名稱是正式身份；跨檔重複欄不可以 last-write-wins 靜默覆蓋。</summary>
    [Fact]
    public void Layers_DuplicateAttributeAcrossFiles_Rejected()
    {
        using var fixture = SchemaLayerFixture.Create(
            """{"schemaVersion":1,"organizations":{"construction":["platform"]}}""",
            new Dictionary<string, string>
            {
                ["platform/00-core.schema.json"] = SchemaLayerFixture.Schema(SchemaLayerFixture.Account("new_same")),
                ["platform/10-extension.schema.json"] = SchemaLayerFixture.Schema(SchemaLayerFixture.Account("new_same"))
            });

        Assert.Throws<InvalidDataException>(() => SchemaFiles.LoadForOrganization(fixture.Root, "construction"));
    }

    /// <summary>新 entity 缺明確 ownershipType 時，在產生任何可部署 schema 候選前拒絕。</summary>
    [Fact]
    public void Schema_NewEntityWithoutOwnershipType_PlanFails()
    {
        using var fixture = SchemaLayerFixture.Create(
            """{"schemaVersion":1,"organizations":{"construction":["goodbutler"]}}""",
            new Dictionary<string, string>
            {
                ["goodbutler.schema.json"] = SchemaLayerFixture.Schema(
                    SchemaLayerFixture.Account("new_tenantcode"),
                    SchemaLayerFixture.NewEntity("new_business", null, tenantLookup: false))
            });

        Assert.Throws<InvalidDataException>(() => SchemaFiles.LoadForOrganization(fixture.Root, "construction"));
    }

    /// <summary>以帳戶作租戶範圍的 localized text 若宣告組織擁有，必須在 plan 輸入邊界拒絕。</summary>
    [Fact]
    public void Schema_TenantBusinessEntityOrganizationOwned_PlanFails()
    {
        using var fixture = SchemaLayerFixture.Create(
            """{"schemaVersion":1,"organizations":{"construction":["platform"]}}""",
            new Dictionary<string, string>
            {
                ["platform/00-core.schema.json"] = SchemaLayerFixture.Schema(
                    SchemaLayerFixture.Account("new_tenantcode"),
                    SchemaLayerFixture.NewEntity("new_localizedtext", "OrganizationOwned", tenantLookup: true))
            });

        Assert.Throws<InvalidDataException>(() => SchemaFiles.LoadForOrganization(fixture.Root, "construction"));
    }
}

/// <summary>建立單一測試擁有的臨時 schema repo；刪除範圍固定為本 fixture 的隨機目錄。</summary>
internal sealed class SchemaLayerFixture : IDisposable
{
    /// <summary>本 fixture 專有且不含任何外部資源的 repo 根目錄。</summary>
    internal string Root { get; }

    /// <summary>只接受剛建立的唯一臨時路徑，立即建立 schema 根目錄。</summary>
    private SchemaLayerFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "3p-fs-schema-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(Root, "d365", "schema"));
        File.WriteAllText(Path.Combine(Root, "SpeechMessageProducts.sln"), "", new UTF8Encoding(false));
    }

    /// <summary>建立 repository-root 測試輸入檔並回傳唯一 owner。</summary>
    internal static SchemaLayerFixture Create(string layers, IReadOnlyDictionary<string, string> schemas)
    {
        var fixture = new SchemaLayerFixture();
        try
        {
            var schemaRoot = Path.Combine(fixture.Root, "d365", "schema");
            Write(Path.Combine(schemaRoot, "layers.json"), layers);
            foreach (var item in schemas)
            {
                var path = Path.Combine(schemaRoot, item.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                Write(path, item.Value);
            }

            return fixture;
        }
        catch
        {
            fixture.Dispose();
            throw;
        }
    }

    /// <summary>供正式repo layer tests 使用實際 d365/schema 來源，而不複製或改動該 repo。</summary>
    internal static SchemaLayerFixture FromRepository()
    {
        var fixture = new SchemaLayerFixture();
        var source = Path.GetDirectoryName(SchemaDefinitionTests.SchemaPath())!;
        var destination = Path.Combine(fixture.Root, "d365", "schema");
        Directory.Delete(destination, recursive: true);
        CopyDirectory(source, destination);
        return fixture;
    }

    /// <summary>將工作樹內的只讀 schema fixture 複製至本案專屬目錄，不建立跨工作樹連結。</summary>
    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    /// <summary>以固定欄位、solution 與 publisher 身份包裝合成 schema entities。</summary>
    internal static string Schema(params string[] entities) =>
        """{"schemaVersion":1,"activePhase":1,"solution":"SpeechMessageCore","publisher":"SpeechMessagePublisher","prefix":"new","entities":["""
        + string.Join(",", entities) + "]}";

    /// <summary>建立只含一個既有 account 欄位的有效平台 entity。</summary>
    internal static string Account(string field) =>
        "{\"logicalName\":\"account\",\"displayName\":\"Account\",\"pluralName\":\"Accounts\",\"isNew\":false,\"audit\":false,\"fields\":[{\"logicalName\":\""
        + field
        + "\",\"displayName\":\"Synthetic field\",\"type\":\"string\",\"phase\":1,\"maxLength\":64}],\"keys\":[]}";

    /// <summary>建立有明確主名稱與可選租戶 lookup 的新 entity 測試資料。</summary>
    internal static string NewEntity(string name, string? ownership, bool tenantLookup)
    {
        var ownershipProperty = ownership is null ? "" : ",\"ownershipType\":\"" + ownership + "\"";
        var tenantField = tenantLookup
            ? ",{\"logicalName\":\"new_accountid\",\"displayName\":\"Account\",\"type\":\"lookup\",\"phase\":1,\"target\":\"account\",\"required\":true}"
            : "";
        return "{\"logicalName\":\"" + name + "\",\"displayName\":\"Synthetic business\",\"pluralName\":\"Synthetic business rows\",\"isNew\":true"
            + ownershipProperty
            + ",\"audit\":false,\"primaryName\":\"new_name\",\"fields\":[{\"logicalName\":\"new_name\",\"displayName\":\"Name\",\"type\":\"string\",\"phase\":1,\"maxLength\":100,\"required\":true}"
            + tenantField
            + "],\"keys\":[]}";
    }

    /// <summary>用無BOM UTF-8、CRLF 寫入 operation-local JSON；不覆寫任何 repo 或其他測試資料。</summary>
    private static void Write(string path, string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .TrimEnd('\n')
            .Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n";
        File.WriteAllText(path, normalized, new UTF8Encoding(false));
    }

    /// <summary>刪除只由本 fixture 建立的唯一臨時 repo，沒有共享或外部資料。</summary>
    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}

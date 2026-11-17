/*
檔案整體說明
目的與定位：P7組織白名單與正式操作護欄離線契約，防止輸入或User Secrets擴張核定目標。
主要流程：先以完整合法合成清單通過控制組，再單點注入未列名稱、重複名稱／主機、未知欄位，
斷言真catalog整體拒絕而不是靜默省略。後續正式旗標與stdin、connector測試沿同一檔案接續。
資源與隔離：只有案例區域JSON純值，不讀User Secrets、不連網或D365，不保存全域可變設定。
錯誤不輸出秘密或真實正式組織名稱，合成名稱均以synthetic命名；控制組可讀才能判定故障意義。
驗證限制：本檔保護離線解析與授權接縫，不能用其通過取代Live、部署或使用者正式核定。
*/
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Text.Json.Nodes;
using Xunit;
using SpeechMessage.Dynamics.Deployment.Schema;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Organization;
using Microsoft.Xrm.Sdk.Messages;
using LineMessagingProcessor.Core.Security;
namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>以合成組織證明catalog拒絕與正式關卡，全部案例零真連線。</summary>
[Collection("Deployment working directory")]
public sealed class OrganizationsTests
{
    /// <summary>合法組織先可讀，再查未列名稱應拒絕；不能按格式通過自動授權。</summary>
    [Fact]
    public void Organizations_UnlistedOrganization_Rejected()
    {
        var catalog = OrganizationCatalog.Parse(Valid().ToJsonString());
        Assert.Equal("synthetic-prod-a", catalog.Get("synthetic-prod-a").Name);
        Assert.Throws<InvalidDataException>(() => catalog.Get("synthetic-unknown"));
    }
    /// <summary>名稱或主機重複都使整份候選失敗，合法不同名稱／主機控制組保持可讀。</summary>
    [Theory]
    [InlineData("name")]
    [InlineData("host")]
    public void Organizations_DuplicateNameOrHost_Rejected(string field)
    {
        var document = Valid();
        _ = OrganizationCatalog.Parse(document.ToJsonString());
        document["organizations"]![1]![field] = document["organizations"]![0]![field]!.GetValue<string>();
        Assert.Throws<InvalidDataException>(() => OrganizationCatalog.Parse(document.ToJsonString()));
    }
    /// <summary>根或單筆多未知欄位不能被忽略，故意混入欄位前先接受完整合法控制組。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Organizations_UnknownField_Rejected(bool root)
    {
        var document = Valid();
        _ = OrganizationCatalog.Parse(document.ToJsonString());
        var target = root ? document : document["organizations"]![0]!.AsObject();
        target["unknown"] = true;
        Assert.Throws<InvalidDataException>(() => OrganizationCatalog.Parse(document.ToJsonString()));
    }
    /// <summary>正式組織所有命令缺旗標在連線前拒絕；唯讀合法控制組應可通過。</summary>
    [Fact]
    public void Organizations_ProductionWithoutFlag_Rejected()
    {
        var catalog = OrganizationCatalog.Parse(Valid().ToJsonString());
        Assert.Equal("synthetic-prod-a", ProductionGuard.Authorize(["export", "--org", "synthetic-prod-a", "--production"], catalog, null, default).Organization.Name);
        Assert.Throws<InvalidDataException>(() => ProductionGuard.Authorize(["export", "--org", "synthetic-prod-a"], catalog, null, default));
    }
    /// <summary>測試組織不接受正式旗標，避免操作者誤以為正在操作正式目標。</summary>
    [Fact]
    public void Organizations_ProductionFlagOnTestOrganization_Rejected()
    {
        var doc = Valid(); var row = doc["organizations"]![0]!;
        row["name"] = "goodshepherdback"; row["kind"] = "test";
        var catalog = OrganizationCatalog.Parse(doc.ToJsonString());
        Assert.Equal("test", ProductionGuard.Authorize(["export", "--org", "goodshepherdback"], catalog, null, default).Organization.Kind);
        Assert.Throws<InvalidDataException>(() => ProductionGuard.Authorize(["export", "--org", "goodshepherdback", "--production"], catalog, null, default));
    }
    /// <summary>寫入即使有旗標仍須名稱確認，空輸入／EOF不得當同意；正確一行為控制組。</summary>
    [Fact]
    public void Organizations_ProductionWriteWithoutNameConfirmation_Rejected()
    {
        var catalog = OrganizationCatalog.Parse(Valid().ToJsonString());
        using var correct = new StringReader("synthetic-prod-a\n");
        Assert.Equal("synthetic-prod-a", ProductionGuard.Authorize(["apply", "--org", "synthetic-prod-a", "--production"], catalog, correct, default).Organization.Name);
        using var empty = new StringReader("");
        Assert.Throws<InvalidDataException>(() => ProductionGuard.Authorize(["apply", "--org", "synthetic-prod-a", "--production"], catalog, empty, default));
    }
    /// <summary>確認必須大小寫完全相同、至多256字元；畸形及其他組織名稱都拒絕且不消費下一行。</summary>
    [Theory]
    [InlineData("SYNTHETIC-prod-a\n")]
    [InlineData("synthetic-prod-b\n")]
    [InlineData(" synthetic-prod-a\n")]
    public void Organizations_ProductionNameConfirmationMismatch_Rejected(string line)
    {
        var catalog = OrganizationCatalog.Parse(Valid().ToJsonString());
        using var input = new StringReader(line);
        Assert.Throws<InvalidDataException>(() => ProductionGuard.Authorize(["apply", "--org", "synthetic-prod-a", "--production"], catalog, input, default));
    }
    /// <summary>正式環境禁止診斷、影子、模組切換與Live驗收及UI匯出；不因有旗標及確認而開放。</summary>
    [Theory]
    [InlineData("smoke")]
    [InlineData("shadow")]
    [InlineData("switch")]
    [InlineData("live-check")]
    [InlineData("ui-export")]
    public void Organizations_ProductionForbidsSmokeShadowModuleSwitchLiveCheckUiExport(string command)
    {
        var catalog = OrganizationCatalog.Parse(Valid().ToJsonString());
        using var input = new StringReader("synthetic-prod-a\n");
        Assert.Throws<InvalidDataException>(() => ProductionGuard.Authorize([command, "--org", "synthetic-prod-a", "--production", "--module", "fees"], catalog, input, default));
    }
    /// <summary>確認只消費一行，保留Channel後續兩行；256以上與無界輸入不能進入後續命令。</summary>
    [Fact]
    public void ProductionConfirmation_SharedInputLeavesChannelLinesAndRejectsOverflow()
    {
        var catalog = OrganizationCatalog.Parse(Valid().ToJsonString());
        using var input = new StringReader("synthetic-prod-a\r\nsynthetic-token\nsynthetic-secret\n");
        var auth = ProductionGuard.Authorize(["protect-token", "--org", "synthetic-prod-a", "--production"], catalog, input, default);
        Assert.DoesNotContain("--production", auth.Arguments);
        Assert.Equal("synthetic-token", input.ReadLine());
        Assert.Equal("synthetic-secret", input.ReadLine());
        using var overflow = new StringReader(new string('a', 257) + "\n");
        Assert.Throws<InvalidDataException>(() => ProductionGuard.Authorize(["apply", "--org", "synthetic-prod-a", "--production"], catalog, overflow, default));
    }
    /// <summary>合成A/B/A連線逐次取自己的設定、獨占SDK與租約；Dispose一次且不借前一組織client。</summary>
    [Fact]
    public void Connector_TwoOrganizations_CredentialsNotShared()
    {
        var catalog = OrganizationCatalog.Parse(Valid().ToJsonString());
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Deployment:Organizations:synthetic-prod-a:OrganizationUrl"] = "https://synthetic-a.invalid/XRMServices/2011/Organization.svc",
            ["Deployment:Organizations:synthetic-prod-a:Username"] = "synthetic-a",
            ["Deployment:Organizations:synthetic-prod-a:Password"] = "synthetic-password-a",
            ["Deployment:Organizations:synthetic-prod-b:OrganizationUrl"] = "https://synthetic-b.invalid/XRMServices/2011/Organization.svc",
            ["Deployment:Organizations:synthetic-prod-b:Username"] = "synthetic-b",
            ["Deployment:Organizations:synthetic-prod-b:Password"] = "synthetic-password-b"
        }).Build();
        var calls = new List<string>(); var services = new List<MemoryOrganization>();
        var connector = new UserSecretsOrganizationConnector(configuration, (url, user, password) =>
        {
            var suffix = user[^1].ToString();
            Assert.Equal("https://synthetic-" + suffix + ".invalid/XRMServices/2011/Organization.svc", url);
            Assert.Equal("synthetic-password-" + suffix, password);
            calls.Add(user); var service = new MemoryOrganization(); services.Add(service); return service;
        }, catalog);
        foreach (var name in new[] { "synthetic-prod-a", "synthetic-prod-b", "synthetic-prod-a" })
        {
            using var lease = connector.Connect(name);
            Assert.Same(services[^1], lease.Service);
        }
        Assert.Collection(calls, value => Assert.Equal("synthetic-a", value), value => Assert.Equal("synthetic-b", value), value => Assert.Equal("synthetic-a", value));
        Assert.All(services, service => Assert.Equal(1, service.DisposeCount));
    }
    /// <summary>白名單host與組織自己的User Secrets URL不同，client工廠必須零次，不以旗標放行。</summary>
    [Fact]
    public void Organizations_HostMismatchWithUserSecretsUrl_Rejected()
    {
        var catalog = OrganizationCatalog.Parse(Valid().ToJsonString());
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Deployment:Organizations:synthetic-prod-a:OrganizationUrl"] = "https://synthetic-b.invalid/XRMServices/2011/Organization.svc",
            ["Deployment:Organizations:synthetic-prod-a:Username"] = "synthetic-a",
            ["Deployment:Organizations:synthetic-prod-a:Password"] = "synthetic-password-a"
        }).Build();
        var calls = 0;
        var connector = new UserSecretsOrganizationConnector(configuration, (_, _, _) => { calls++; throw new InvalidOperationException("Unexpected client creation."); }, catalog);
        Assert.Throws<InvalidOperationException>(() => connector.Connect("synthetic-prod-a"));
        Assert.Equal(0, calls);
    }
    /// <summary>正式唯讀舊註冊盤點即使發現啟用列仍成功並列數，絕不啟用、停用或執行舊程式。</summary>
    [Fact]
    public void Organizations_ProductionExportLegacy_ReportsEnabledWithoutStopping()
    {
        var catalog = OrganizationCatalog.Parse(Valid().ToJsonString());
        using var fixture = new Fixture(); using var output = new StringWriter();
        var assembly = Guid.NewGuid(); var type = Guid.NewGuid(); var message = Guid.NewGuid(); var filter = Guid.NewGuid();
        var rows = new Dictionary<string, Entity[]>
        {
            ["pluginassembly"] = [new("pluginassembly", assembly) { ["name"] = "Synthetic.Legacy", ["version"] = "1.0.0.0", ["isolationmode"] = new OptionSetValue(2) }],
            ["plugintype"] = [new("plugintype", type) { ["pluginassemblyid"] = new EntityReference("pluginassembly", assembly), ["typename"] = "Synthetic.Legacy.Plugin", ["isworkflowactivity"] = false }],
            ["sdkmessage"] = [new("sdkmessage", message) { ["name"] = "Create" }],
            ["sdkmessagefilter"] = [new("sdkmessagefilter", filter) { ["primaryobjecttypecode"] = "contact" }],
            ["sdkmessageprocessingstep"] = [new("sdkmessageprocessingstep", Guid.NewGuid()) { ["eventhandler"] = new EntityReference("plugintype", type), ["sdkmessageid"] = new EntityReference("sdkmessage", message), ["sdkmessagefilterid"] = new EntityReference("sdkmessagefilter", filter), ["statecode"] = new OptionSetValue(0), ["name"] = "Synthetic enabled step", ["stage"] = new OptionSetValue(40), ["mode"] = new OptionSetValue(1), ["rank"] = 1 }]
        };
        var sdk = new QueryOnlyOrganization(query =>
        {
            var expression = Assert.IsType<QueryExpression>(query);
            return new EntityCollection(rows.GetValueOrDefault(expression.EntityName) ?? []);
        }, "synthetic-prod-a");
        var connector = new ProbeConnector(sdk);
        var code = DeploymentCli.RunWithCatalog(["export-legacy", "--org", "synthetic-prod-a", "--production"], catalog, connector, output, _ => Assert.Fail("Unexpected SDK failure"), null, fixture.Root);
        Assert.Equal(0, code); Assert.Equal(1, connector.Calls); Assert.Equal(1, connector.Disposals);
        Assert.Contains("Enabled legacy steps: 1", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Stop and report", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, sdk.Writes);
    }
    /// <summary>四條實際命令分流缺正式旗標都必須零連線；不是只測Guard函式本身。</summary>
    [Theory]
    [InlineData("export-legacy")]
    [InlineData("export-metadata")]
    [InlineData("protect-token")]
    [InlineData("plan")]
    public void ProductionGuard_AllCliRoutes_RejectBeforeConnecting(string command)
    {
        var catalog = OrganizationCatalog.Parse(Valid().ToJsonString());
        var sdk = new QueryOnlyOrganization(_ => throw new InvalidOperationException("Unexpected query.")); var connector = new ProbeConnector(sdk);
        using var output = new StringWriter();
        Assert.Equal(2, DeploymentCli.RunWithCatalog([command, "--org", "synthetic-prod-a"], catalog, connector, output, _ => Assert.Fail("Unexpected failure"), null));
        Assert.Equal(0, connector.Calls);
    }
    /// <summary>只有單次合成查詢的SDK替身；任何寫入、Execute或不屬本契約的入口立即拒絕。</summary>
    private sealed class QueryOnlyOrganization(Func<QueryBase, EntityCollection> read, string? serverName = null, Func<OrganizationRequest, OrganizationResponse>? execute = null) : IOrganizationService
    {
        internal int Writes;
        /// <summary>同步委派本案唯讀列，沒有網路、cache或背景資源。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => read(query);
        /// <summary>拒絕寫入並記次數，以真CLI行為證明盤點沒有副作用。</summary>
        public Guid Create(Entity entity) { Writes++; throw new NotSupportedException(); }
        /// <summary>拒絕更新，不能藉盤點偷偷停用舊步驟。</summary>
        public void Update(Entity entity) { Writes++; throw new NotSupportedException(); }
        /// <summary>拒絕刪除，案例結束只釋放記憶體。</summary>
        public void Delete(string table, Guid id) { Writes++; throw new NotSupportedException(); }
        /// <summary>合成匯出不需要單筆查詢，意外入場直接失敗。</summary>
        public Entity Retrieve(string table, Guid id, ColumnSet columns) => throw new NotSupportedException();
        /// <summary>匯出不能執行舊程式或任意SDK訊息。</summary>
        public OrganizationResponse Execute(OrganizationRequest request) => request is RetrieveCurrentOrganizationRequest && serverName is not null
            ? new RetrieveCurrentOrganizationResponse { Results = { ["Detail"] = new OrganizationDetail { OrganizationId = Guid.NewGuid(), UniqueName = serverName } } }
            : execute is not null ? execute(request) : throw new NotSupportedException();
        /// <summary>拒絕關聯寫入，不提供部分功能假成功。</summary>
        public void Associate(string table, Guid id, Relationship relationship, EntityReferenceCollection values) { Writes++; throw new NotSupportedException(); }
        /// <summary>拒絕移除關聯，不修改本次盤點資料。</summary>
        public void Disassociate(string table, Guid id, Relationship relationship, EntityReferenceCollection values) { Writes++; throw new NotSupportedException(); }
    }
    /// <summary>只計數本案例借用SDK及租約，不建立網路資源；租約清理由真CLI使用using負責。</summary>
    private sealed class ProbeConnector(IOrganizationService sdk) : IOrganizationConnector
    {
        internal int Calls, Disposals;
        /// <summary>把合成服務借給一次CLI，釋放只記計數，不會執行任何真連線。</summary>
        public OrganizationLease Connect(string organization) { Calls++; return new OrganizationLease(sdk, () => Disposals++); }
    }
    /// <summary>真Program的秘密讀取接縫按canonical組織選鍵，交錯A/B不讀預設測試組織值或保留上一筆。</summary>
    [Fact]
    public void Program_OrganizationTokenKey_IsSelectedFromAuthorizedName()
    {
        var catalog = OrganizationCatalog.Parse(Valid().ToJsonString());
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Deployment:Organizations:synthetic-prod-a:TokenKey"] = "synthetic-key-a",
            ["Deployment:Organizations:synthetic-prod-b:TokenKey"] = "synthetic-key-b"
        }).Build();
        Assert.Equal("synthetic-key-a", Program.ReadOrganizationSecret(configuration, catalog, ["apply", "--org", "synthetic-prod-a", "--production"], "TokenKey"));
        Assert.Equal("synthetic-key-b", Program.ReadOrganizationSecret(configuration, catalog, ["apply", "--org", "synthetic-prod-b", "--production"], "TokenKey"));
        Assert.Throws<InvalidDataException>(() => Program.ReadOrganizationSecret(configuration, catalog, ["apply", "--org", "synthetic-unknown", "--production"], "TokenKey"));
    }
    /// <summary>同名合成組織的server正式ID變更，完整plan重算必須在任何寫入前拒絕；原快照可驗為控制組。</summary>
    [Fact]
    public void Organizations_ServerOrganizationIdDiffersFromPlan_Rejected()
    {
        var catalog = OrganizationCatalog.Parse(Valid().ToJsonString());
        var definition = SchemaPlannerTests.Definition();
        var snapshot = SchemaPlannerTests.CompleteSnapshot(definition) with { Organization = "synthetic-prod-a" };
        var plan = SchemaPlanner.Create(definition, snapshot, new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.Zero), catalog);
        SchemaPlanner.ValidatePlan(plan, definition, snapshot, catalog);
        Assert.Throws<InvalidDataException>(() => SchemaPlanner.ValidatePlan(plan, definition, snapshot with { OrganizationId = Guid.NewGuid() }, catalog));
    }
    /// <summary>真repo清單仍只有原測試組織；舊CLI不加旗標唯讀盤點仍成功，租約一次釋放且零寫入。</summary>
    [Fact]
    public void Organizations_GoodshepherdbackBehaviour_Unchanged()
    {
        var catalog = OrganizationCatalog.LoadDefault();
        Assert.Equal("test", catalog.Get("goodshepherdback").Kind);
        using var fixture = new Fixture(); using var output = new StringWriter();
        var sdk = new QueryOnlyOrganization(_ => new EntityCollection()); var connector = new ProbeConnector(sdk);
        Assert.Equal(0, DeploymentCli.Run(["export-legacy", "--org", "goodshepherdback"], connector, output, _ => Assert.Fail("Unexpected SDK failure"), fixture.Root));
        Assert.Equal(1, connector.Calls); Assert.Equal(1, connector.Disposals); Assert.Equal(0, sdk.Writes);
    }
    /// <summary>即使host及catalog名稱合法，服務端回另一組織仍在任何metadata查詢或寫入之前拒絕，並歸還lease。</summary>
    [Fact]
    public void ProductionCli_ServerUniqueNameMismatch_RejectsBeforeQueries()
    {
        var catalog = OrganizationCatalog.Parse(Valid().ToJsonString()); using var fixture = new Fixture();
        var queries = 0; var sdk = new QueryOnlyOrganization(_ => { queries++; return new EntityCollection(); }, "synthetic-prod-b");
        var connector = new ProbeConnector(sdk); using var output = new StringWriter(); var failures = new List<Exception>();
        var code = DeploymentCli.RunWithCatalog(["export-legacy", "--org", "synthetic-prod-a", "--production"], catalog, connector, output, failures.Add, null, fixture.Root);
        Assert.Equal(1, code); Assert.Single(failures); Assert.Equal(0, queries); Assert.Equal(1, connector.Disposals); Assert.Equal(0, sdk.Writes);
    }
    /// <summary>正式schema唯讀匯出走真CLI及完整metadata adapter，寫到合成組織目錄，租約回零且不需Plugin DLL。</summary>
    [Fact]
    public void ProductionSchema_ExportMetadata_UsesCanonicalOrganizationDirectory()
    {
        var catalog = OrganizationCatalog.Parse(Valid().ToJsonString()); using var fixture = new Fixture();
        using var cwd = fixture.EnterCliRepository(); using var output = new StringWriter();
        var sdk = new SyntheticMetadata("synthetic-prod-a"); var connector = new ProbeConnector(sdk); var failures = new List<Exception>();
        Assert.Equal(0, DeploymentCli.RunWithCatalog(["export-metadata", "--org", "synthetic-prod-a", "--production"], catalog, connector, output, failures.Add, null, fixture.Root));
        Assert.Empty(failures); Assert.Equal(1, connector.Disposals); Assert.Equal(0, sdk.Writes);
        Assert.Single(Directory.GetFiles(Path.Combine(fixture.Root, "d365", "snapshots", "synthetic-prod-a"), "metadata.json", SearchOption.AllDirectories));
    }
    /// <summary>確認行和兩行合成Channel秘密經真正同一reader進CLI；只有一次Upsert且解密為原輸入，所有lease歸還。</summary>
    [Fact]
    public void ProductionChannel_ConfirmationThenProtect_SharedReaderCompletes()
    {
        var catalog = OrganizationCatalog.Parse(Valid().ToJsonString()); using var fixture = new Fixture(); using var output = new StringWriter();
        var key = new byte[64]; for (var index = 0; index < key.Length; index++) key[index] = (byte)index;
        try
        {
            var upserts = 0;
            var sdk = new QueryOnlyOrganization(_ => throw new NotSupportedException(), "synthetic-prod-a", request =>
            {
                var upsert = Assert.IsType<UpsertRequest>(request); upserts++;
                Assert.Equal("synthetic-token", ChannelTokenProtector.Unprotect(upsert.Target.GetAttributeValue<string>("new_accesstokenencrypted"), key));
                Assert.Equal("synthetic-secret", ChannelTokenProtector.Unprotect(upsert.Target.GetAttributeValue<string>("new_channelsecretencrypted"), key));
                return new UpsertResponse { Results = { ["Target"] = new EntityReference("new_linechannel", Guid.NewGuid()), ["RecordCreated"] = true } };
            });
            var connector = new ProbeConnector(sdk); using var input = new StringReader("synthetic-prod-a\nsynthetic-token\nsynthetic-secret\n");
            Assert.Equal(0, DeploymentCli.RunWithCatalog(["protect-token", "--org", "synthetic-prod-a", "--production", "--channel-id", "123"], catalog, connector, output, _ => Assert.Fail("Unexpected failure"), input, fixture.Root, _ => Convert.ToBase64String(key)));
            Assert.Equal(1, upserts); Assert.Equal(1, connector.Disposals);
        }
        finally { Array.Clear(key); }
    }
    /// <summary>沿既有完整metadata合成SDK，只替換server uniqueName，其他欄位與拒寫契約保持，沒有網路。</summary>
    private sealed class SyntheticMetadata(string name) : SchemaMetadataTests.MetadataService
    {
        /// <summary>原基類提供真型別response與正式合成ID；只改當案名稱供顯式組織scope測試。</summary>
        public override OrganizationResponse Execute(OrganizationRequest request)
        {
            var response = base.Execute(request);
            if (response is RetrieveCurrentOrganizationResponse current) current.Detail.UniqueName = name;
            return response;
        }
    }
    /// <summary>名稱確認等待逾時只拒絕且零連線；caller預取消回130，不借逾時冒稱正常取消。</summary>
    [Fact]
    public void ProductionConfirmation_TimeoutOrCancellation_NeverConnects()
    {
        var catalog = OrganizationCatalog.Parse(Valid().ToJsonString());
        var sdk = new QueryOnlyOrganization(_ => throw new NotSupportedException()); var connector = new ProbeConnector(sdk);
        using var reader = new TimeoutReader(); using var output = new StringWriter();
        Assert.Equal(2, DeploymentCli.RunWithCatalog(["apply", "--org", "synthetic-prod-a", "--production"], catalog, connector, output, _ => Assert.Fail("Expected refusal"), reader));
        Assert.Equal(130, DeploymentCli.RunWithCatalog(["apply", "--org", "synthetic-prod-a", "--production"], catalog, connector, output, _ => Assert.Fail("Expected cancellation"), reader, cancellationToken: new System.Threading.CancellationToken(true)));
        Assert.Equal(0, connector.Calls);
    }
    /// <summary>有界reader故障注入，不建立真管線或timer，caller using唯一釋放。</summary>
    private sealed class TimeoutReader : StringReader
    {
        /// <summary>只持有空合成來源，不讀真stdin。</summary>
        internal TimeoutReader() : base("") { }
        /// <summary>模擬既有ChannelSecretInput總deadline到期，不能返回假確認。</summary>
        public override int Read() => throw new TimeoutException("Synthetic input deadline.");
    }
    /// <summary>JSON重複鍵與非核定test名稱都拒絕，不能因最後一值覆寫而偽造核定清單。</summary>
    [Fact]
    public void Catalog_DuplicatePropertiesAndOtherTestNames_Rejected()
    {
        Assert.Throws<InvalidDataException>(() => OrganizationCatalog.Parse("{\"schemaVersion\":1,\"schemaVersion\":1,\"organizations\":[]}"));
        var document = Valid(); document["organizations"]![0]!["kind"] = "test";
        Assert.Throws<InvalidDataException>(() => OrganizationCatalog.Parse(document.ToJsonString()));
    }
    /// <summary>兩個只用於離線測試的正式種類組織，不帶真實主機、教會名稱或credential。</summary>
    private static JsonObject Valid() => JsonNode.Parse("""
    {"schemaVersion":1,"organizations":[
      {"name":"synthetic-prod-a","kind":"production","host":"synthetic-a.invalid","switchState":"off","tierOverrides":[]},
      {"name":"synthetic-prod-b","kind":"production","host":"synthetic-b.invalid","switchState":"off","tierOverrides":[]}
    ]}
    """)!.AsObject();
}

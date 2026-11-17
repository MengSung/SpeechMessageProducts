/*
檔案整體說明
目的與定位：P7 Live／observe／defaults 真 CLI 的離線語法與租約契約，不連任何組織。
主要流程：未知或互斥選項在 connector 前拒絕；defaults 以合成 SDK 產生實際 plan，
再故意提供錯誤完整檔 SHA，驗證零更新、敏感值不輸出，以及每命令釋放獨占租約。
資源與隔離：fixture 暫存 repository、工作目錄 scope 及 writer 均 using 關閉；合成
教會 ID 只在本案，不存名稱／秘密。固定來源檔由測試範本產生，沒有修改產品清單。
失敗與驗證：真主入口及 ProductionGuard 共同經過，不以直接呼叫核心取代 CLI 行為。
*/
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Organization;
using Microsoft.Xrm.Sdk.Query;
using Xunit;
namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>序列化工作目錄測試，保護主 CLI 三命令不被原 parser 誤分流。</summary>
[Collection("Deployment working directory")]
public sealed class LiveCliTests
{
    /// <summary>不完整／未知／重複選項一律零連線，不以預設值補齊寫入意圖。</summary>
    [Theory]
    [InlineData("observe --org goodshepherdback")]
    [InlineData("observe --org goodshepherdback --since 2026-11-17T00:00:00+08:00")]
    [InlineData("observe --org goodshepherdback --since 2026-11-17T00:00:00Z --apply")]
    [InlineData("church-defaults --org goodshepherdback --apply")]
    [InlineData("church-defaults --org goodshepherdback --plan unused")]
    [InlineData("church-defaults --org goodshepherdback --apply --plan unused --plan-sha256 wrong")]
    [InlineData("live-check --org goodshepherdback --module care")]
    [InlineData("live-check --org goodshepherdback --module care --module care --scenario-root unused")]
    public void LiveCli_InvalidArguments_ZeroConnect(string text)
    {
        using var output = new StringWriter(); var connector = new Connector(null);
        Assert.Equal(2, DeploymentCli.Run(text.Split(' '), connector, output, _ => Assert.Fail("No exception expected")));
        Assert.Equal(0, connector.Calls);
    }
    /// <summary>plan 真實產檔但零寫入；錯誤檔SHA拒絕，兩次命令lease皆恰釋放一次。</summary>
    [Fact]
    public void ChurchDefaultsCli_PlanThenBadFileHash_NoWritesAndLeaseDrains()
    {
        using var fixture = new Fixture(); using var scope = fixture.EnterCliRepository();
        var catalogText = "{\"schemaVersion\":1,\"organizations\":[{\"name\":\"goodshepherdback\",\"kind\":\"test\",\"host\":\"synthetic.invalid\",\"switchState\":\"off\",\"tierOverrides\":[]}]}";
        File.WriteAllText(Path.Combine(fixture.Root, "d365", "registration", "organizations.json"), catalogText);
        var catalog = OrganizationCatalog.Parse(catalogText);
        fixture.Service.Seed(new Entity("account", Guid.NewGuid()) { ["new_linechannelid"] = new EntityReference("new_linechannel", Guid.NewGuid()), ["name"] = "SENTINEL-BODY" });
        var connector = new Connector(new Service(fixture.Service)); using var output = new StringWriter();
        Exception? failure = null;
        var exit = DeploymentCli.RunWithCatalog(new[] { "church-defaults", "--org", Fixture.Organization }, catalog, connector, output, exception => failure = exception, null);
        Assert.Null(failure);
        Assert.Equal(0, exit);
        var planPath = Assert.Single(Directory.GetFiles(Path.Combine(fixture.Root, "artifacts", "d365"), "church-defaults-plan.json", SearchOption.AllDirectories));
        var json = File.ReadAllText(planPath); Assert.DoesNotContain("SENTINEL", json, StringComparison.Ordinal);
        using var plan = JsonDocument.Parse(json); Assert.Single(plan.RootElement.GetProperty("Entries").EnumerateArray());
        Assert.Empty(fixture.Service.Writes);
        var reports = 0;
        Assert.Equal(1, DeploymentCli.RunWithCatalog(new[] { "church-defaults", "--org", Fixture.Organization, "--apply", "--plan", planPath, "--plan-sha256", new string('0', 64) }, catalog,
            connector, output, _ => reports++, null));
        Assert.Equal(1, reports); Assert.Equal(2, connector.Disposals); Assert.Empty(fixture.Service.Writes);
    }
    /// <summary>獨占合成 lease，非法語法不得到達連線。</summary>
    private sealed class Connector(IOrganizationService? service) : IOrganizationConnector
    {
        internal int Calls { get; private set; }
        internal int Disposals { get; private set; }
        /// <summary>每命令一個 lease，release 計數不釋放 fixture 擁有的記憶體SDK。</summary>
        public OrganizationLease Connect(string organization)
        { Calls++; return new(service ?? throw new InvalidOperationException("unexpected connection"), () => Disposals++); }
    }
    /// <summary>只提供 defaults plan 需要的只讀 SDK，寫入入口全部拒絕。</summary>
    private sealed class Service(MemoryOrganization inner) : IOrganizationService
    {
        /// <summary>組織權威資料固定合成，不使用 caller 名稱作授權。</summary>
        public OrganizationResponse Execute(OrganizationRequest request) => request is RetrieveCurrentOrganizationRequest
            ? new RetrieveCurrentOrganizationResponse { Results = { ["Detail"] = new OrganizationDetail { UniqueName = Fixture.Organization, OrganizationId = Guid.Parse("11111111-1111-1111-1111-111111111111") } } }
            : throw new NotSupportedException();
        /// <summary>完整有界集合由產品驗證。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => inner.RetrieveMultiple(query);
        /// <summary>只讀精確正式ID。</summary>
        public Entity Retrieve(string name, Guid id, ColumnSet columns) => inner.Retrieve(name, id, columns);
        /// <summary>本測試 plan 路徑禁止建立。</summary>
        public Guid Create(Entity row) => throw new NotSupportedException();
        /// <summary>錯hash不得到達更新。</summary>
        public void Update(Entity row) => throw new NotSupportedException();
        /// <summary>預設值不得刪除。</summary>
        public void Delete(string name, Guid id) => throw new NotSupportedException();
        /// <summary>不提供關聯能力。</summary>
        public void Associate(string name, Guid id, Relationship relation, EntityReferenceCollection rows) => throw new NotSupportedException();
        /// <summary>不提供解除關聯能力。</summary>
        public void Disassociate(string name, Guid id, Relationship relation, EntityReferenceCollection rows) => throw new NotSupportedException();
    }
}

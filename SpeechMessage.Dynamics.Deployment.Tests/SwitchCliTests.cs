/*
檔案整體說明
目的與定位：P7 CLI的新切換語法與檔案發布契約；所有SDK與PE皆案例合成，不連D365、不讀真秘密。
主要流程：先以非法組合驗零連線，再以真CLI plan產檔，檢查檔hash與Markdown固定章節及原指令相容。
生命週期：案例獨占temp根、組態與租約，程序工作目錄由scope先還原再刪除；沒有背景工作或跨案狀態。
失敗判準：缺hash仍建立連線、非法選項被忽略、plan輸出缺五段或金鑰實值出現都失敗，不能冒充實機證據。
*/
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;
namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>避免新參數被module switch舊分流吞掉，保護嚴格parse與共同授權入口。</summary>
[Collection("Deployment working directory")]
public sealed class SwitchCliTests
{
    /// <summary>缺少或重複選項及未知target應在connector前拒絕，不浪費或建立未授權連線。</summary>
    [Theory]
    [InlineData("switch --org goodshepherdback --plan unused")]
    [InlineData("switch --org goodshepherdback --plan unused --plan-sha256 bad")]
    [InlineData("plan --org goodshepherdback --switch-to enabled")]
    [InlineData("verify --org goodshepherdback --plan unused --switch-to live")]
    [InlineData("plan --org goodshepherdback --switch-to live --switch-to off")]
    public void InvalidSwitchArguments_RefusedBeforeConnect(string text)
    {
        using var output = new StringWriter();
        var connector = new RefusingConnector();
        Assert.Equal(2, DeploymentCli.Run(text.Split(' '), connector, output, _ => Assert.Fail("No report expected")));
        Assert.Equal(0, connector.Calls);
    }

    /// <summary>新plan語法走實際metadata與PE，結果含受審hash及五段；全程唯讀且租約釋放。</summary>
    [Fact]
    public void SwitchPlanCli_WritesReviewedFiles_ReadOnly()
    {
        using var f = new Fixture();
        using var scope = f.EnterCliRepository();
        var manifest = f.Manifest with { Steps = new[] { f.Manifest.Steps[0] with
            { Module = "attendance", Live = new StepLiveRegistration(), UnsecureConfig = "{\"mode\":\"Shadow\"}" } } };
        var document = new RegistrationDocument { Assemblies = new[] { manifest } };
        File.WriteAllText(Path.Combine(f.Root, "d365", "registration", "plugins.registration.json"), JsonSerializer.Serialize(document, RegistrationManifestLoader.Json));
        var catalogText = "{\"schemaVersion\":1,\"organizations\":[{\"name\":\"goodshepherdback\",\"kind\":\"test\",\"host\":\"synthetic.invalid\",\"switchState\":\"off\",\"tierOverrides\":[]}]}";
        File.WriteAllText(Path.Combine(f.Root, "d365", "registration", "organizations.json"), catalogText);
        var catalog = OrganizationCatalog.Parse(catalogText);
        var dll = Path.Combine(f.Root, "artifacts", "plugins", manifest.AssemblyName + ".dll");
        Directory.CreateDirectory(Path.GetDirectoryName(dll)!);
        File.Copy(f.Emit(), dll);
        var image = AssemblyCatalog.Read(dll, manifest);
        f.Engine.Apply(f.Engine.Plan(manifest, image, Fixture.Organization), manifest, image);
        var count = f.Service.Writes.Count;
        using var output = new StringWriter();
        var connector = new ReadConnector(new SwitchSnapshotReaderTests.ReadService(f.Service));
        Assert.Equal(0, DeploymentCli.RunWithCatalog(new[] { "plan", "--org", Fixture.Organization, "--switch-to", "live" }, catalog,
            connector, output, _ => Assert.Fail("Plan must succeed"), null));
        Assert.Equal(count, f.Service.Writes.Count);
        Assert.Equal(1, connector.Disposals);
        var planPath = Assert.Single(Directory.GetFiles(Path.Combine(f.Root, "artifacts", "d365"), "switch-plan.json", SearchOption.AllDirectories));
        var markdown = File.ReadAllText(Path.ChangeExtension(planPath, ".md"));
        Assert.Contains("會做的事", markdown, StringComparison.Ordinal);
        Assert.Contains("KeepLegacy", markdown, StringComparison.Ordinal);
        Assert.Contains("Manual", markdown, StringComparison.Ordinal);
        Assert.Contains("dormant", markdown, StringComparison.Ordinal);
        Assert.Contains("仍登錄的舊組件", markdown, StringComparison.Ordinal);
        Assert.Contains("核定書摘要", markdown, StringComparison.Ordinal);
    }

    /// <summary>同一真CLI從plan→switch→verify plan→rollback→verify off，直接讀回SDK狀態並檢查唯一log。</summary>
    [Fact]
    public void SwitchCli_FullOfflineCycle_RestoresOff()
    {
        using var f = new Fixture();
        using var scope = f.EnterCliRepository();
        var manifest = f.Manifest with { Steps = new[] { f.Manifest.Steps[0] with
            { Module = "attendance", Live = new StepLiveRegistration(), UnsecureConfig = "{\"mode\":\"Shadow\"}" } } };
        File.WriteAllText(Path.Combine(f.Root, "d365", "registration", "plugins.registration.json"), JsonSerializer.Serialize(new RegistrationDocument { Assemblies = new[] { manifest } }, RegistrationManifestLoader.Json));
        var catalogText = "{\"schemaVersion\":1,\"organizations\":[{\"name\":\"goodshepherdback\",\"kind\":\"test\",\"host\":\"synthetic.invalid\",\"switchState\":\"off\",\"tierOverrides\":[]}]}";
        File.WriteAllText(Path.Combine(f.Root, "d365", "registration", "organizations.json"), catalogText);
        var catalog = OrganizationCatalog.Parse(catalogText);
        var dll = Path.Combine(f.Root, "artifacts", "plugins", manifest.AssemblyName + ".dll");
        Directory.CreateDirectory(Path.GetDirectoryName(dll)!); File.Copy(f.Emit(), dll);
        var image = AssemblyCatalog.Read(dll, manifest);
        f.Engine.Apply(f.Engine.Plan(manifest, image, Fixture.Organization), manifest, image);
        var connector = new ReadConnector(new MutableService(f.Service));
        using var output = new StringWriter();
        int Run(params string[] args) => DeploymentCli.RunWithCatalog(args, catalog, connector, output, exception => throw new InvalidOperationException("Unexpected CLI failure", exception), null);
        Assert.Equal(0, Run("plan", "--org", Fixture.Organization, "--switch-to", "live"));
        var orgRoot = Path.Combine(f.Root, "artifacts", "d365", Fixture.Organization);
        var plan = Assert.Single(Directory.GetFiles(orgRoot, "switch-plan.json", SearchOption.AllDirectories));
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(plan)));
        Assert.Equal(0, Run("switch", "--org", Fixture.Organization, "--plan", plan, "--plan-sha256", hash));
        Assert.Equal(0, f.Service.Rows.Single(row => row.LogicalName == "sdkmessageprocessingstep").GetAttributeValue<Microsoft.Xrm.Sdk.OptionSetValue>("statecode").Value);
        Assert.Equal(0, Run("verify", "--org", Fixture.Organization, "--plan", plan));
        Assert.Equal(1, Run("verify", "--org", Fixture.Organization));
        var log = Assert.Single(Directory.GetFiles(orgRoot, "switch-log.json", SearchOption.AllDirectories));
        Assert.Equal(0, Run("rollback", "--org", Fixture.Organization, "--from", log));
        Assert.Equal(0, Run("verify", "--org", Fixture.Organization));
        Assert.Equal(1, f.Service.Rows.Single(row => row.LogicalName == "sdkmessageprocessingstep").GetAttributeValue<Microsoft.Xrm.Sdk.OptionSetValue>("statecode").Value);
        Assert.Equal(6, connector.Disposals);
    }

    /// <summary>只為全CLI循環提供真記憶體SDK寫入，讀取／WhoAmI沿唯讀轉接，不模擬產品切換邏輯。</summary>
    private sealed class MutableService(MemoryOrganization inner) : Microsoft.Xrm.Sdk.IOrganizationService
    {
        private readonly SwitchSnapshotReaderTests.ReadService reader = new(inner);
        /// <summary>取得完整合成metadata，沒有網路資源。</summary>
        public Microsoft.Xrm.Sdk.EntityCollection RetrieveMultiple(Microsoft.Xrm.Sdk.Query.QueryBase query) => reader.RetrieveMultiple(query);
        /// <summary>正式ID精確讀取，不按名稱合併。</summary>
        public Microsoft.Xrm.Sdk.Entity Retrieve(string entityName, Guid id, Microsoft.Xrm.Sdk.Query.ColumnSet columnSet) => inner.Retrieve(entityName, id, columnSet);
        /// <summary>建立由產品分配的正式ID。</summary>
        public Guid Create(Microsoft.Xrm.Sdk.Entity entity) => inner.Create(entity);
        /// <summary>實際更新本案SDK資料。</summary>
        public void Update(Microsoft.Xrm.Sdk.Entity entity) => inner.Update(entity);
        /// <summary>只刪精確正式ID。</summary>
        public void Delete(string entityName, Guid id) => inner.Delete(entityName, id);
        /// <summary>WhoAmI用合成權威ID，SetState由原SDK執行。</summary>
        public Microsoft.Xrm.Sdk.OrganizationResponse Execute(Microsoft.Xrm.Sdk.OrganizationRequest request) => reader.Execute(request);
        /// <summary>本案沒有關聯寫入，意外呼叫失敗。</summary>
        public void Associate(string entityName, Guid entityId, Microsoft.Xrm.Sdk.Relationship relationship, Microsoft.Xrm.Sdk.EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>本案沒有解除關聯，意外呼叫失敗。</summary>
        public void Disassociate(string entityName, Guid entityId, Microsoft.Xrm.Sdk.Relationship relationship, Microsoft.Xrm.Sdk.EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }

    /// <summary>拒絕建立連線的計數器，無任何外部資源。</summary>
    private sealed class RefusingConnector : IOrganizationConnector
    {
        internal int Calls { get; private set; }
        /// <summary>非法語法不應進入此處，進入即失敗。</summary>
        public OrganizationLease Connect(string organization) { Calls++; throw new InvalidOperationException("Must not connect"); }
    }

    /// <summary>借用案例唯讀SDK的獨占租約，釋放只計數，不釋放fixture所有的底層資料。</summary>
    private sealed class ReadConnector(Microsoft.Xrm.Sdk.IOrganizationService service) : IOrganizationConnector
    {
        internal int Disposals { get; private set; }
        /// <summary>每命令建立新lease並以計數證明finally只釋放一次。</summary>
        public OrganizationLease Connect(string organization) => new(service, () => Disposals++);
    }
}

/*
檔案整體說明
目的與定位：驗證部署工具可依明確組織名稱解析多個測試組織，並保留 production 與 test 的種類邊界。
主要流程：以合成 JSON 建立四組織 catalog，檢查指定 test 的 canonical 描述、未指定唯一 test 時拒絕，
並注入尚未核定的空 host；另驗證跨 repo 寫鎖、Plugin 組織旗標與複製組織 quiesce dry-run。
依賴與隔離：只使用記憶體設定、合成 .invalid 主機、暫存目錄與計數 SDK；不讀 User Secrets、
不建立網路或 D365 連線。catalog、鎖、Task、事件與合成 SDK 都由單一測試擁有並確定釋放。
故障注入與決定性斷言：混入 production kind、空白 endpoint、多 test、延遲鎖競爭及只讀 quiesce；
assert client factory／production SDK 呼叫數為零、第二 repo 等待鎖釋放、停用 dry-run 寫入數為零。
驗證限制：這些離線案例只證明 catalog／connector 契約，不代表三個組織端點已核定或 Live3P 通過。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;
using SpeechMessage.Dynamics.Deployment.Organizations;

namespace SpeechMessage.Dynamics.Deployment.Tests.Organizations;

/// <summary>以合成 catalog 驗證多測試組織解析、種類隔離及未核定端點拒絕，不使用外部連線或使用者秘密。</summary>
public sealed class MultiTestOrganizationTests
{
    /// <summary>四個 test 可依 canonical 名稱獨立解析；不同組織不可因同屬 test 而共用 host 或連線身份。</summary>
    [Fact]
    public void Catalog_MultipleTestOrganizations_ResolvedByName()
    {
        var catalog = OrganizationCatalog.Parse(FourTestOrganizations);

        Assert.Equal("https://construction.test.invalid", catalog.GetTest("construction").Host);
        Assert.Equal("https://association.test.invalid", catalog.GetTest("association").Host);
        Assert.Equal("test", catalog.GetTest("construction").Kind);
    }

    /// <summary>多筆 test 時舊的隱含唯一入口必須拒絕，要求呼叫端明確提供組織名稱。</summary>
    [Fact]
    public void Catalog_MultipleTests_WithoutOrg_Refused()
    {
        var catalog = OrganizationCatalog.Parse(FourTestOrganizations);

        Assert.Throws<InvalidOperationException>(() => _ = catalog.TestOrganization);
    }

    /// <summary>production 即使列在相同 catalog，也不能由 test 專用解析入口取得。</summary>
    [Fact]
    public void Catalog_ProductionKind_NeverReturnedAsTest()
    {
        var catalog = OrganizationCatalog.Parse(TestAndProductionOrganizations);

        Assert.Throws<InvalidDataException>(() => catalog.GetTest("production-a"));
        Assert.Equal("test", catalog.GetTest("goodshepherd20").Kind);
    }

    /// <summary>同一份清單可明確控制各組織的註冊步驟；未啟用組織不得得到 CreateStep 操作。</summary>
    [Fact]
    public void Registration_OrgFlag_OnlyEnabledStepsRegistered()
    {
        var catalog = OrganizationCatalog.Parse(FourTestOrganizations);
        using var fixture = new Fixture();
        var step = fixture.Manifest.Steps[0] with
        {
            EnabledByOrganization = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["construction"] = true,
                ["association"] = false
            }
        };
        var manifest = fixture.Manifest with { Steps = new[] { step } };
        var engine = new DeploymentEngine(fixture.Service, fixture.Root, fixture.Clock,
            _ => throw new InvalidOperationException("No secret should be read while planning."), catalog);

        var construction = engine.Plan(manifest, fixture.Image, "construction");
        var association = engine.Plan(manifest, fixture.Image, "association");

        Assert.Contains(construction.Operations, operation => operation.Kind == "CreateStep");
        Assert.DoesNotContain(association.Operations, operation => operation.Kind == "CreateStep");
    }

    /// <summary>相同組織名稱從兩個 repo artifacts 根取得同一台電腦級鎖；第二個寫入需等第一個釋放。</summary>
    [Fact]
    public async Task WriteLock_TwoRepositoryRoots_SameOrganization_Serialized()
    {
        var root = Path.Combine(Path.GetTempPath(), "3p-write-lock-" + Guid.NewGuid().ToString("N"));
        var lockRoot = Path.Combine(root, "shared-locks");
        var firstRepository = Path.Combine(root, "repo-a", "artifacts", "d365", "construction");
        var secondRepository = Path.Combine(root, "repo-b", "artifacts", "d365", "construction");
        Directory.CreateDirectory(firstRepository);
        Directory.CreateDirectory(secondRepository);
        using var first = OrganizationWriteLock.Acquire(
            firstRepository, TimeSpan.FromSeconds(5), sharedLockDirectory: lockRoot);
        using var started = new ManualResetEventSlim();
        using var acquired = new ManualResetEventSlim();
        var waitingWriter = Task.Run(() =>
        {
            started.Set();
            using var second = OrganizationWriteLock.Acquire(
                secondRepository, TimeSpan.FromSeconds(5), sharedLockDirectory: lockRoot);
            acquired.Set();
        });

        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(2)));
            Assert.False(acquired.Wait(TimeSpan.FromMilliseconds(150)));
            first.Dispose();
            Assert.True(acquired.Wait(TimeSpan.FromSeconds(5)));
            await waitingWriter.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            first.Dispose();
            if (!waitingWriter.IsCompleted)
                await waitingWriter.WaitAsync(TimeSpan.FromSeconds(5));
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>production kind 在任何 SDK 讀取前拒絕停用操作，防止複製清理誤觸正式組織。</summary>
    [Fact]
    public void Quiesce_ProductionOrganization_Refused()
    {
        var catalog = OrganizationCatalog.Parse(TestAndProductionOrganizations);
        var service = new QuiesceProbeService();

        Assert.Throws<InvalidDataException>(() =>
            ClonedOrganizationQuiesce.Run(service, catalog.Get("production-a"),
                apply: true, cancellationToken: CancellationToken.None));
        Assert.Equal(0, service.Reads);
        Assert.Equal(0, service.Writes);
    }

    /// <summary>不帶 apply 只盤點自動化；即使清單非空，也不得送出取消、停用或更新要求。</summary>
    [Fact]
    public void Quiesce_WithoutApply_ListsOnly()
    {
        var catalog = OrganizationCatalog.Parse(FourTestOrganizations);
        var service = new QuiesceProbeService();

        var result = ClonedOrganizationQuiesce.Run(
            service, catalog.GetTest("goodshepherd20"),
            apply: false, cancellationToken: CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(service.Reads > 0);
        Assert.Equal(0, service.Writes);
    }

    /// <summary>未核定 host 以空值表示；connector 即使取得合成 secrets 也必須在 client factory 前拒絕。</summary>
    [Fact]
    public void Connector_PendingTestEndpoint_RefusedBeforeClientCreation()
    {
        var catalog = OrganizationCatalog.Parse(PendingEndpointOrganization);
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Deployment:Organizations:construction:OrganizationUrl"] =
                    "https://construction.test.invalid/XRMServices/2011/Organization.svc",
                ["Deployment:Organizations:construction:Username"] = "synthetic-user",
                ["Deployment:Organizations:construction:Password"] = "synthetic-password"
            })
            .Build();
        var clientFactoryCalls = 0;
        var connector = new UserSecretsOrganizationConnector(
            configuration,
            (_, _, _) =>
            {
                clientFactoryCalls++;
                throw new InvalidOperationException("The pending endpoint must not create a client.");
            },
            catalog);

        Assert.Throws<InvalidOperationException>(() => connector.Connect("construction"));
        Assert.Equal(0, clientFactoryCalls);
    }

    /// <summary>僅含合成主機的四組織控制資料；各 host 唯一且保留生產環境 kind 的獨立測試列。</summary>
    private const string FourTestOrganizations = """
        {"schemaVersion":1,"organizations":[
          {"name":"goodshepherdback","kind":"test","host":"goodshepherdback.test.invalid","switchState":"off","tierOverrides":[]},
          {"name":"goodshepherd20","kind":"test","host":"goodshepherd20.test.invalid","switchState":"off","tierOverrides":[]},
          {"name":"construction","kind":"test","host":"construction.test.invalid","switchState":"off","tierOverrides":[]},
          {"name":"association","kind":"test","host":"association.test.invalid","switchState":"off","tierOverrides":[]}
        ]}
        """;

    /// <summary>只有一個核定測試組織及一個 production 組織的種類邊界樣本，不含任何有效端點。</summary>
    private const string TestAndProductionOrganizations = """
        {"schemaVersion":1,"organizations":[
          {"name":"goodshepherd20","kind":"test","host":"goodshepherd20.test.invalid","switchState":"off","tierOverrides":[]},
          {"name":"production-a","kind":"production","host":"production-a.test.invalid","switchState":"off","tierOverrides":[]}
        ]}
        """;

    /// <summary>空 host 是未核定狀態的明確標記；任何可用 HTTPS 端點都不會與其匹配。</summary>
    private const string PendingEndpointOrganization = """
        {"schemaVersion":1,"organizations":[
          {"name":"construction","kind":"test","host":"","switchState":"off","tierOverrides":[]}
        ]}
        """;

    /// <summary>只計數合成查詢與寫入，未授權案例可斷言拒絕點位於任何 SDK 副作用之前。</summary>
    private sealed class QuiesceProbeService : IOrganizationService
    {
        internal int Reads { get; private set; }
        internal int Writes { get; private set; }

        /// <summary>回傳本案例擁有的空列集合，不連接外部組織或保留資料。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            Reads++;
            return new EntityCollection();
        }

        /// <summary>任何列建立都代表盤點流程越界，計數後立即失敗。</summary>
        public Guid Create(Entity entity) { Writes++; throw new InvalidOperationException(); }
        /// <summary>任何列更新都代表 dry-run 變成寫入，計數後立即失敗。</summary>
        public void Update(Entity entity) { Writes++; throw new InvalidOperationException(); }
        /// <summary>任何列刪除都代表 dry-run 變成破壞性操作，計數後立即失敗。</summary>
        public void Delete(string entityName, Guid id) { Writes++; throw new InvalidOperationException(); }
        /// <summary>單筆讀取不屬於本案例的合成資料，立即拒絕。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
        /// <summary>只允許由清單流程明確核准的 Execute；本空案例未預期任何 SDK 訊息。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            Writes++;
            throw new InvalidOperationException("Unexpected SDK request.");
        }
        /// <summary>關聯操作不在盤點契約內，計數後拒絕。</summary>
        public void Associate(string entityName, Guid id, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
            Writes++;
            throw new InvalidOperationException();
        }
        /// <summary>解除關聯不在盤點契約內，計數後拒絕。</summary>
        public void Disassociate(string entityName, Guid id, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
            Writes++;
            throw new InvalidOperationException();
        }
    }
}

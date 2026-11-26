/*
檔案整體說明
目的與定位：驗證部署工具可依明確組織名稱解析多個測試組織，並保留 production 與 test 的種類邊界。
主要流程：以合成 JSON 建立四組織 catalog，檢查指定 test 的 canonical 描述、未指定唯一 test 時拒絕，
並注入尚未核定的空 host；另驗證跨 repo 寫鎖、Plugin 組織旗標與複製組織 quiesce dry-run。
註冊測試以每個組織獨立的合成 SDK 身分回應通過真正身分守門；不因補齊替身而略過核身或改動生產規則。
quiesce 直接 apply 的成功案例明確提供合成快照 owner，在首筆 Update 前檢查零寫入與信箱原像；
保留三欄投遞斷言並以 ACT None=2 驗證，不把原錯誤的 0 預期當成現場版本證據。
有界落盤、flush、取消與部分失敗的完整原像契約另由 QuiesceMailboxDeliveryTests 驗證。
apply 的合法控制另補齊已審來源指紋，舊正式 ID 分類與所有 mailbox 斷言不變；
重跑時明確先重新盤點與核對合成來源政策，不以停用後已改變的來源集合繼續使用舊指紋。
來源漂移／缺指紋的零寫入與真公開入口另由 QuiesceSourceBindingTests 保護，不解決現場 H1 分類。
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
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Organization;
using Microsoft.Xrm.Sdk.Query;
using Xunit;
using SpeechMessage.Dynamics.Deployment.Organizations;

namespace SpeechMessage.Dynamics.Deployment.Tests.Organizations;

/// <summary>新組織的結構預設路由與顯式 Plugin 註冊需同時可用；只產生本案合成 PE，不讀真正部署映像。</summary>
[Collection("Deployment working directory")]
public sealed class MultiTestRegistrationCliTests
{
    /// <summary>明確 --registration 仍可取得真實 Plugin 計畫；不可以因新結構預設值讓註冊入口永久無法使用。</summary>
    [Theory]
    [InlineData("goodshepherd20")]
    [InlineData("construction")]
    [InlineData("association")]
    public void Registration_PublicPlan_ExplicitModePreservesPluginRoute(string organization)
    {
        using var fixture = new Fixture();
        using var repository = fixture.EnterCliRepository();
        var plugins = Path.Combine(fixture.Root, "artifacts", "plugins");
        Directory.CreateDirectory(plugins);
        File.Copy(fixture.Emit(), Path.Combine(plugins, fixture.Manifest.AssemblyName + ".dll"));
        var connector = new RegistrationConnector(fixture.Service);
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        var failures = new List<Exception>();
        var root = Path.Combine(fixture.Root, "artifacts", "d365");
        Assert.Equal(0, DeploymentCli.Run(["plan", "--org", organization, "--registration"],
            connector, output, failures.Add, root));
        Assert.Empty(failures);
        var path = Assert.Single(Directory.GetFiles(root, "plan.json", SearchOption.AllDirectories));
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(organization, json.RootElement.GetProperty("Organization").GetString());
        Assert.Contains(json.RootElement.GetProperty("Operations").EnumerateArray(),
            operation => operation.GetProperty("Kind").GetString() == "CreateAssembly");
        Assert.Empty(Directory.GetFiles(root, "schema-plan.json", SearchOption.AllDirectories));
        Assert.Equal(1, connector.Returned);
        Assert.Empty(fixture.Service.Writes);
    }

    /// <summary>兩種模式互斥且旗標不能重複；錯誤輸入必須在 connector 之前停止，不應誤選部署模式。</summary>
    [Theory]
    [InlineData("--registration", "--registration")]
    [InlineData("--registration", "--schema", "synthetic.schema.json")]
    public void Registration_ConflictingMode_RefusedBeforeConnect(params string[] options)
    {
        using var fixture = new Fixture();
        var connector = new RegistrationConnector(fixture.Service);
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        string[] arguments = ["plan", "--org", "construction"];
        Assert.Equal(2, DeploymentCli.Run(arguments.Concat(options).ToArray(),
            connector, output, _ => throw new InvalidOperationException()));
        Assert.Equal(0, connector.Returned);
    }

    /// <summary>單案合成 SDK 的租約來源，沒有真實連線資源；歸還計數證明命令已完成清理。</summary>
    private sealed class RegistrationConnector(MemoryOrganization service) : IOrganizationConnector
    {
        internal int Returned { get; private set; }
        /// <summary>caller 組織已由真正 catalog 核定；本來源只借出本案合成註冊資料庫。</summary>
        public OrganizationLease Connect(string organization) =>
            new(new NamedRegistrationService(service, organization), () => Returned++);
    }
}

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
        var constructionService = new NamedRegistrationService(fixture.Service, "construction");
        var constructionEngine = new DeploymentEngine(constructionService, fixture.Root, fixture.Clock,
            _ => throw new InvalidOperationException("No secret should be read while planning."), catalog);
        var associationService = new NamedRegistrationService(fixture.Service, "association");
        var associationEngine = new DeploymentEngine(associationService, fixture.Root, fixture.Clock,
            _ => throw new InvalidOperationException("No secret should be read while planning."), catalog);
        var construction = constructionEngine.Plan(manifest, fixture.Image, "construction");
        var association = associationEngine.Plan(manifest, fixture.Image, "association");

        Assert.Contains(construction.Operations, operation => operation.Kind == "CreateStep");
        Assert.DoesNotContain(association.Operations, operation => operation.Kind == "CreateStep");
        Assert.Equal(1, constructionService.IdentityReads);
        Assert.Equal(1, associationService.IdentityReads);
        Assert.NotEqual(construction.OrganizationId, association.OrganizationId);
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

/// <summary>
/// 單案註冊 SDK 的組織身分轉接器；每個實例只屬於一個固定合成組織，
/// 其餘 metadata 與寫入沿用 fixture 真正記憶體資料庫，無連線、快取或 Dispose 所有權。
/// </summary>
internal sealed class NamedRegistrationService(MemoryOrganization inner, string organization) : IOrganizationService
{
    /// <summary>本實例核身訊息次數，供既有規則案例確認真正經過新增的守門。</summary>
    internal int IdentityReads { get; private set; }

    /// <summary>僅替換唯讀組織身分；其他訊息保留原替身行為及故障，不吞掉未知 SDK 操作。</summary>
    public OrganizationResponse Execute(OrganizationRequest request)
    {
        if (request is not RetrieveCurrentOrganizationRequest) return inner.Execute(request);
        IdentityReads++;
        var id = organization switch
        {
            "goodshepherd20" => new Guid("11000000-0000-0000-0000-000000000001"),
            "construction" => new Guid("11000000-0000-0000-0000-000000000002"),
            "association" => new Guid("11000000-0000-0000-0000-000000000003"),
            _ => throw new InvalidDataException("Unexpected synthetic organization.")
        };
        return new RetrieveCurrentOrganizationResponse
        {
            Results = { ["Detail"] = new OrganizationDetail { UniqueName = organization, OrganizationId = id } }
        };
    }

    /// <summary>合成 metadata 查詢交給本案 fixture，沒有外部資料來源。</summary>
    public EntityCollection RetrieveMultiple(QueryBase query) => inner.RetrieveMultiple(query);
    /// <summary>正式 ID 讀取保留 fixture 的原始失敗行為。</summary>
    public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => inner.Retrieve(entityName, id, columnSet);
    /// <summary>建立操作交給可觀察的原記憶體資料庫，不假裝零副作用。</summary>
    public Guid Create(Entity entity) => inner.Create(entity);
    /// <summary>更新操作保留原記憶體資料庫的紀錄及斷言。</summary>
    public void Update(Entity entity) => inner.Update(entity);
    /// <summary>刪除操作保留 fixture 行為；本轉接器不接管資料生命週期。</summary>
    public void Delete(string entityName, Guid id) => inner.Delete(entityName, id);
    /// <summary>關聯操作不擴張原替身允許的能力。</summary>
    public void Associate(string entityName, Guid entityId, Relationship relationship,
        EntityReferenceCollection relatedEntities) => inner.Associate(entityName, entityId, relationship, relatedEntities);
    /// <summary>解除關聯亦由原替身決定，不引入額外清理責任。</summary>
    public void Disassociate(string entityName, Guid entityId, Relationship relationship,
        EntityReferenceCollection relatedEntities) => inner.Disassociate(entityName, entityId, relationship, relatedEntities);
}

/// <summary>
/// FS 停用命令的公開入口及 SDK 行為驗證；每案只持有合成列與臨時 repo，
/// 注入分頁／狀態錯誤來保護完整盤點、零秘密讀取與租約確定歸還。
/// </summary>
[Collection("Deployment working directory")]
public sealed class QuiescePublicTests
{
    /// <summary>啟用步驟只要有一筆未分類，就必須在工作流程、信箱及等待工作被修改前拒絕。</summary>
    [Fact]
    public void Quiesce_UnclassifiedActivePlugin_RefusesEveryWrite()
    {
        var service = new AutomationService();
        service.PluginIds.Add(Guid.NewGuid());
        Assert.Throws<InvalidDataException>(() => ClonedOrganizationQuiesce.Run(service,
            OrganizationCatalog.LoadDefault().GetTest("goodshepherd20"), true, default));
        Assert.Empty(service.Updates);
    }

    /// <summary>保留流程與無外送步驟不可被停；明確合成 owner 接到完整原像後，才可先停選定外送來源。</summary>
    [Fact]
    public void Quiesce_ClassifiedPluginsAndKeptWorkflow_OnlySelectedIdsChange()
    {
        var service = new AutomationService();
        var disable = Guid.NewGuid();
        var keep = Guid.NewGuid();
        service.PluginIds.AddRange([disable, keep]);
        var policy = new QuiescePolicy
        {
            KeepWorkflowIds = [new Guid("10100000-0000-0000-0000-000000000001")],
            DisablePluginStepIds = [disable], KeepPluginStepIds = [keep]
        };
        policy = SourcePolicy(service, policy);
        _ = ClonedOrganizationQuiesce.Run(service, OrganizationCatalog.LoadDefault().GetTest("goodshepherd20"),
            true, default, policy, beforeWrite: snapshot =>
            {
                Assert.Empty(service.Updates);
                Assert.NotNull(Assert.Single(snapshot.Candidates, row => row.Entity == "mailbox").MailboxBeforeImage);
            });
        Assert.DoesNotContain(service.Updates, row => row.LogicalName == "workflow" || row.Id == keep);
        Assert.Equal(disable, service.Updates[0].Id);
        Assert.Equal(1, service.Updates[0].GetAttributeValue<OptionSetValue>("statecode").Value);
        Assert.Equal(2, service.Updates[0].GetAttributeValue<OptionSetValue>("statuscode").Value);
    }

    /// <summary>公開 apply 缺少 F0 分類清冊時，尚未借用 SDK 就拒絕，不可採猜測的停用範圍。</summary>
    [Fact]
    public void Quiesce_PublicApply_RequiresPolicyBeforeConnect()
    {
        using var fixture = new QuiesceFixture();
        Assert.Equal(2, fixture.Run("quiesce", "--org", "goodshepherd20", "--apply"));
        Assert.Equal(0, fixture.Connector.Returned);
        Assert.Empty(fixture.Connector.Service.Updates);
    }

    /// <summary>分類清冊必須綁同一 server 組織；錯誤 ID 不可寫入，租約必須歸還。</summary>
    [Fact]
    public void Quiesce_PublicApply_WrongOrganizationId_RefusesAndReleases()
    {
        using var fixture = new QuiesceFixture();
        fixture.Policy(Guid.NewGuid());
        Assert.Equal(1, fixture.Run("quiesce", "--org", "goodshepherd20", "--apply"));
        Assert.Equal(1, fixture.Connector.Returned);
        Assert.Empty(fixture.Connector.Service.Updates);
        Assert.Single(fixture.Failures);
    }

    /// <summary>完整公開 apply 先落盤點、逐項停用、再真實重查；重跑前重新核對已變動的合成來源政策，仍零寫入且全部資源釋放。</summary>
    [Fact]
    public void Quiesce_PublicApply_ReadsBackEmptyAndRetryWritesNothing()
    {
        using var fixture = new QuiesceFixture();
        fixture.Policy(new Guid("10000000-0000-0000-0000-000000000001"));
        fixture.Connector.Service.ApplyState = true;
        fixture.Connector.Service.BeforeUpdate = _ =>
        {
            var path = Assert.Single(Directory.GetFiles(fixture.Root, "quiesce-inventory.json", SearchOption.AllDirectories));
            using var evidence = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
            Assert.True(evidence.Length > 0);
        };
        Assert.Equal(0, fixture.Run("quiesce", "--org", "goodshepherd20", "--apply"));
        Assert.Single(Directory.GetFiles(fixture.Root, "quiesce-after.json", SearchOption.AllDirectories));
        var writes = fixture.Connector.Service.Updates.Count;
        fixture.Connector.Service.BeforeUpdate = null;
        fixture.RefreshPolicy();
        Assert.Equal(0, fixture.Run("quiesce", "--org", "goodshepherd20", "--apply"));
        Assert.Equal(writes, fixture.Connector.Service.Updates.Count);
        Assert.Equal(2, fixture.Connector.Returned);
        Assert.Empty(fixture.Failures);
    }

    /// <summary>SDK 停用故障傳入原始例外，後續寫入停止；再取得同一組織寫鎖證明失敗路徑已釋放。</summary>
    [Fact]
    public void Quiesce_PublicApply_SdkFailure_PreservesExceptionAndReleasesLock()
    {
        using var fixture = new QuiesceFixture();
        fixture.Policy(new Guid("10000000-0000-0000-0000-000000000001"));
        var fault = new IOException("Synthetic stop failure.");
        fixture.Connector.Service.BeforeUpdate = _ => throw fault;
        Assert.Equal(1, fixture.Run("quiesce", "--org", "goodshepherd20", "--apply"));
        Assert.Same(fault, Assert.Single(fixture.Failures));
        Assert.Equal(1, fixture.Connector.Returned);
        Assert.Empty(fixture.Connector.Service.Updates);
        using var writeLock = OrganizationWriteLock.Acquire(
            Path.Combine(fixture.Root, "artifacts", "d365", "goodshepherd20"), TimeSpan.FromSeconds(2));
    }

    /// <summary>caller 在同步 SDK 返回時取消，命令回取消碼、零寫入、不告警，且歸還租約。</summary>
    [Fact]
    public void Quiesce_PublicDryRun_CallerCancellation_ReleasesWithoutReporting()
    {
        using var fixture = new QuiesceFixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Connector.Service.AfterRead = cancellation.Cancel;
        Assert.Equal(130, fixture.Run(cancellation.Token, "quiesce", "--org", "goodshepherd20"));
        Assert.Empty(fixture.Failures);
        Assert.Empty(fixture.Connector.Service.Updates);
        Assert.Equal(1, fixture.Connector.Returned);
    }

    /// <summary>等待工作只能限於 workflow；合成快照 owner 先確認原像，定義退回 Draft，信箱三欄改為 None 0／0／2。</summary>
    [Fact]
    public void Quiesce_Apply_UsesCorrectStatesAndMailDelivery()
    {
        var service = new AutomationService();
        _ = ClonedOrganizationQuiesce.Run(service, OrganizationCatalog.LoadDefault().GetTest("goodshepherd20"), true, default,
            SourcePolicy(service),
            beforeWrite: snapshot =>
            {
                Assert.Empty(service.Updates);
                var before = Assert.Single(snapshot.Candidates, row => row.Entity == "mailbox").MailboxBeforeImage;
                Assert.NotNull(before);
                Assert.Equal(1, before.ActDeliveryMethod);
            });
        var workflow = Assert.Single(service.Updates, row => row.LogicalName == "workflow");
        Assert.Equal(0, workflow.GetAttributeValue<OptionSetValue>("statecode").Value);
        Assert.Equal(1, workflow.GetAttributeValue<OptionSetValue>("statuscode").Value);
        var job = Assert.Single(service.Updates, row => row.LogicalName == "asyncoperation");
        Assert.Equal(3, job.GetAttributeValue<OptionSetValue>("statecode").Value);
        Assert.Equal(32, job.GetAttributeValue<OptionSetValue>("statuscode").Value);
        var mailbox = Assert.Single(service.Updates, row => row.LogicalName == "mailbox");
        Assert.Equal(0, mailbox.GetAttributeValue<OptionSetValue>("incomingemaildeliverymethod").Value);
        Assert.Equal(0, mailbox.GetAttributeValue<OptionSetValue>("outgoingemaildeliverymethod").Value);
        Assert.Equal(2, mailbox.GetAttributeValue<OptionSetValue>("actdeliverymethod").Value);
        Assert.False(mailbox.Contains("statecode"));
    }

    /// <summary>
    /// 只有本檔合成成功／Update 故障案例用的政策準備：先唯讀取得管理來源指紋再複製至正式 ID 清冊，
    /// 不修改來源、不自動分類，也不把這份先前盤點當成真正 apply 的 before-image；
    /// 產品仍必須在同次完整 apply 重新讀取及核對，原 Update、flush、例外與信箱斷言不變。
    /// </summary>
    private static QuiescePolicy SourcePolicy(AutomationService service, QuiescePolicy? policy = null) =>
        (policy ?? new QuiescePolicy()) with
        {
            SourceFingerprint = ClonedOrganizationQuiesce.Run(service,
                OrganizationCatalog.LoadDefault().GetTest("goodshepherd20"), false, default,
                includePluginIdentities: true).SourceFingerprint
        };

    /// <summary>第二頁有額外流程時必須完整讀取後才回傳總數，不可把第一頁當完整盤點。</summary>
    [Fact]
    public void Quiesce_FullPagingBeforeAnyWrite()
    {
        var service = new AutomationService { Paged = true };
        var result = ClonedOrganizationQuiesce.Run(service, OrganizationCatalog.LoadDefault().GetTest("goodshepherd20"), false, default);
        Assert.Equal(2, result.WorkflowDefinitionsToDisable);
        Assert.Empty(service.Updates);
    }

    /// <summary>續頁 cookie 不前進代表來源不完整；即使要求 apply，也必須在任何寫入前拒絕。</summary>
    [Fact]
    public void Quiesce_RepeatedPagingCookie_NoWrites()
    {
        var service = new AutomationService { Paged = true, RepeatCookie = true };
        Assert.Throws<InvalidDataException>(() =>
            ClonedOrganizationQuiesce.Run(service, OrganizationCatalog.LoadDefault().GetTest("goodshepherd20"), true, default));
        Assert.Empty(service.Updates);
    }

    /// <summary>查詢只取停用所需純值；不能載入 Plugin configuration 中可能存在的正式密文。</summary>
    [Fact]
    public void Quiesce_OnlyWorkflowJobsAndNoSecretColumns()
    {
        var service = new AutomationService { ValidateQueries = true };
        _ = ClonedOrganizationQuiesce.Run(service, OrganizationCatalog.LoadDefault().GetTest("goodshepherd20"), false, default);
        Assert.Empty(service.Updates);
    }

    /// <summary>真正公開 quiesce dry-run 不需要 DLL，產生盤點檔、零寫入且歸還唯一 SDK 租約。</summary>
    [Fact]
    public void Quiesce_PublicDryRun_ProducesInventoryAndReturnsLease()
    {
        using var fixture = Schema.SchemaLayerFixture.FromRepository();
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        var previous = Environment.CurrentDirectory;
        var connector = new AutomationConnector();
        var failures = new List<Exception>();
        try
        {
            Environment.CurrentDirectory = fixture.Root;
            var result = DeploymentCli.Run(
                ["quiesce", "--org", "goodshepherd20"], connector, output, failures.Add,
                Path.Combine(fixture.Root, "artifacts", "d365"));
            Assert.Empty(failures);
            Assert.Equal(0, result);
            Assert.Single(Directory.GetFiles(fixture.Root, "quiesce-inventory.json", SearchOption.AllDirectories));
            Assert.Equal(1, connector.Returned);
            Assert.Empty(connector.Service.Updates);
        }
        finally { Environment.CurrentDirectory = previous; }
    }

    /// <summary>命令區域合成 connector，無真實 credential 或背景工作；release 是租約歸還的決定性計數。</summary>
    private sealed class AutomationConnector : IOrganizationConnector
    {
        internal AutomationService Service { get; } = new();
        internal int Returned { get; private set; }
        /// <summary>只借出核定測試組織，其他名稱立即拒絕，避免跨組織候選混用。</summary>
        public OrganizationLease Connect(string name)
        {
            Assert.Equal("goodshepherd20", name);
            return new OrganizationLease(Service, () => Returned++);
        }
    }

    /// <summary>單一公開命令測試的檔案與 cwd owner；只寫合成 policy，清理先復原 cwd 再刪本案根。</summary>
    private sealed class QuiesceFixture : IDisposable
    {
        private readonly Schema.SchemaLayerFixture repository = Schema.SchemaLayerFixture.FromRepository();
        private readonly string previous = Environment.CurrentDirectory;
        internal string Root => repository.Root;
        internal AutomationConnector Connector { get; } = new();
        internal List<Exception> Failures { get; } = new();
        /// <summary>所有案例受相同 cwd 集合鎖保護；沒有真實端點或秘密。</summary>
        internal QuiesceFixture() => Environment.CurrentDirectory = Root;
        /// <summary>只在本案 artifacts 寫入正式組織 ID 的合成清冊，不使用呼叫端 tenant 欄位授權。</summary>
        internal void Policy(Guid organizationId) => SpeechMessage.Dynamics.Deployment.Schema.SchemaFiles.WriteNew(
            Path.Combine(Root, "artifacts", "d365", "goodshepherd20"), "quiesce-policy.json",
            SourcePolicy(Connector.Service, new QuiescePolicy { OrganizationId = organizationId }));
        /// <summary>
        /// 第一輪已停用 workflow，active 來源集合因此改變；此合成重試明確重新盤點政策，
        /// 只替換本案臨時政策，原寫前證據保留且不改成功／零重複寫入的斷言。
        /// </summary>
        internal void RefreshPolicy()
        {
            var path = Path.Combine(Root, "artifacts", "d365", "goodshepherd20", "quiesce-policy.json");
            File.Delete(path);
            Policy(new Guid("10000000-0000-0000-0000-000000000001"));
        }
        /// <summary>一般案例使用未取消的明確 caller token。</summary>
        internal int Run(params string[] args) => Run(default, args);
        /// <summary>測試真 CLI 入口及例外回報，只替換外部 SDK 連線。</summary>
        internal int Run(CancellationToken ct, params string[] args)
        {
            using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            return DeploymentCli.Run(args, Connector, output, Failures.Add,
                Path.Combine(Root, "artifacts", "d365"), cancellationToken: ct);
        }
        /// <summary>不清除其他工作樹或程序的資料；所有 SDK／檔案 owner 已由命令退出釋放。</summary>
        public void Dispose()
        {
            Environment.CurrentDirectory = previous;
            repository.Dispose();
        }
    }

    /// <summary>
    /// 本案專屬自動化資料庫；固定 ID 保護跨頁身份，查詢只回 ColumnSet 明列欄位，Update 只記錄 detached 候選，
    /// 無任何外部資源；失敗案例可斷言完整盤點前沒有副作用。
    /// </summary>
    private sealed class AutomationService : IOrganizationService
    {
        private static readonly Guid WorkflowOne = new("10100000-0000-0000-0000-000000000001");
        private static readonly Guid WorkflowTwo = new("10100000-0000-0000-0000-000000000002");
        internal List<Entity> Updates { get; } = new();
        internal bool Paged { get; init; }
        internal bool RepeatCookie { get; init; }
        internal bool ValidateQueries { get; init; }
        internal bool ApplyState { get; set; }
        internal List<Guid> PluginIds { get; } = new();
        internal Action<Entity>? BeforeUpdate { get; set; }
        internal Action? AfterRead { get; set; }

        /// <summary>回應固定候選與可注入的續頁；依呼叫端明列欄位投影，避免替身夾帶其他表的管理欄位，並獨立驗證 workflow 篩選及秘密欄位禁讀。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var expression = Assert.IsType<QueryExpression>(query);
            AfterRead?.Invoke();
            if (ValidateQueries)
            {
                Assert.DoesNotContain("configuration", expression.ColumnSet.Columns);
                if (expression.EntityName == "asyncoperation")
                    Assert.Contains(expression.Criteria.Conditions, condition =>
                        condition.AttributeName == "operationtype" && condition.Operator == ConditionOperator.Equal
                        && Assert.Single(condition.Values) is int value && value == 10);
            }
            if (expression.EntityName == "sdkmessageprocessingstep")
                return new EntityCollection(PluginIds.Where(id => !ApplyState || !Updates.Any(row => row.Id == id))
                    .Select(id => new Entity("sdkmessageprocessingstep", id)
                    { ["statecode"] = new OptionSetValue(0), ["statuscode"] = new OptionSetValue(1) }).ToList());
            var id = expression.EntityName == "workflow"
                ? expression.PageInfo.PageNumber == 1 ? WorkflowOne : WorkflowTwo
                : expression.EntityName == "asyncoperation"
                    ? new Guid("10200000-0000-0000-0000-000000000001")
                    : new Guid("10300000-0000-0000-0000-000000000001");
            if (ApplyState && Updates.Any(row => row.Id == id)) return new EntityCollection();
            var row = new Entity(expression.EntityName, id)
            {
                ["statecode"] = new OptionSetValue(expression.EntityName == "workflow" ? 1 : 0),
                ["statuscode"] = new OptionSetValue(expression.EntityName == "workflow" ? 2 : 0),
                ["operationtype"] = new OptionSetValue(10),
                ["incomingemaildeliverymethod"] = new OptionSetValue(2),
                ["outgoingemaildeliverymethod"] = new OptionSetValue(2),
                ["actdeliverymethod"] = new OptionSetValue(1)
            };
            foreach (var column in row.Attributes.Keys.Where(column => !expression.ColumnSet.Columns.Contains(column)).ToArray())
                row.Attributes.Remove(column);
            return new EntityCollection(new List<Entity> { row })
            {
                MoreRecords = expression.EntityName == "workflow" && Paged
                    && (expression.PageInfo.PageNumber == 1 || RepeatCookie),
                PagingCookie = expression.EntityName == "workflow" && Paged ? "next-page" : null
            };
        }

        /// <summary>只模擬正式組織身份，不回傳任何業務列或機密設定。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            Assert.IsType<RetrieveCurrentOrganizationRequest>(request);
            return new RetrieveCurrentOrganizationResponse
            {
                Results = { ["Detail"] = new OrganizationDetail
                {
                    OrganizationId = new Guid("10000000-0000-0000-0000-000000000001"),
                    UniqueName = "goodshepherd20", OrganizationVersion = "9.1.0.0"
                } }
            };
        }

        /// <summary>本測試唯一寫入觀測點；複製純欄位以避免來源後續 mutation 汙染斷言。</summary>
        public void Update(Entity entity)
        {
            BeforeUpdate?.Invoke(entity);
            var copy = new Entity(entity.LogicalName, entity.Id);
            copy.Attributes.AddRange(entity.Attributes);
            Updates.Add(copy);
        }
        /// <summary>停用契約不建立新列。</summary>
        public Guid Create(Entity entity) => throw new NotSupportedException();
        /// <summary>停用契約不刪除舊證據或業務列。</summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        /// <summary>盤點只用有限欄位集合查詢，不接受任意單列讀取。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
        /// <summary>停用不建立關聯。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>停用不解除關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}

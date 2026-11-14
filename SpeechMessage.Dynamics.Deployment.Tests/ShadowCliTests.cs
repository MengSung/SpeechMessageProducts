/*
 * 檔案整體說明
 *
 * 目的與定位：以真正 DeploymentCli 驗證 shadow 命令的完整離線接線，防止 reader、
 * factory 及 runner 各自通過卻無法由命令執行。所有組織、metadata、身分及追蹤皆為
 * 每案獨占的合成資料，不讀取 User Secrets、不連 D365、不發 LINE。
 * 主要功能與流程：暫存方案含合成 net462 DLL、固定清單及黃金情境，先由正式引擎
 * 部署，再由命令完成權威身分核對、seed、觸發、追蹤比較及報告。故障注入涵蓋錯誤
 * 身分、追蹤內容、觸發例外、外部路徑、寫鎖與取消；決定性斷言為 lease 歸還、原
 * trace 等級與停用狀態恢復、自建 task 零殘留，以及報告不洩漏秘密或實際 GUID。
 * 使用邊界與維護：非平行集合保護程序 cwd；fixture 先恢復 cwd，再刪除自己的唯一根，
 * writer、租約與檔案均確定釋放。替身只擴充所需的權威 metadata 與精確追蹤查詢，
 * 不改既有通用假服務，也不把合成綠燈稱為實機驗收。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Organization;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Shadow;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>由公開命令驗真正 SDK runtime，沒有可繞過生命週期的假 runner 注入。</summary>
[Collection("Deployment working directory")]
public sealed class ShadowCliTests
{
    /// <summary>new 基準只執行新輪；兩次同日執行保留各自證據，且每次確定還原與清理。</summary>
    [Fact]
    public void ShadowCli_NewRoundRunsThroughSdkAndPreservesSameDayEvidence()
    {
        using var f = new CliFixture();
        Assert.Equal(0, f.Run());
        Assert.Equal(0, f.Run());
        f.AssertRestored();
        Assert.Equal(2, f.Connects);
        Assert.Equal(2, f.Releases);
        Assert.Equal(2, f.Service.Triggers);
        var reports = Directory.GetFiles(Path.Combine(f.Core.Root, "artifacts", "shadow"), "*.md", SearchOption.AllDirectories);
        Assert.Equal(2, reports.Length);
        Assert.All(reports, path =>
        {
            var text = File.ReadAllText(path);
            Assert.Contains("basis=new", text, StringComparison.Ordinal);
            Assert.Contains("Passed=true", text, StringComparison.Ordinal);
            Assert.DoesNotContain(Fixture.SecretValue, text, StringComparison.Ordinal);
            Assert.DoesNotContain(f.Service.Church.ToString("D"), text, StringComparison.Ordinal);
        });
        Assert.Empty(f.Reports);
        Assert.DoesNotContain(Fixture.SecretValue, f.Output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>新輪有額外意圖須回一並留失敗報告，不能以 observe 最終值相同掩蓋。</summary>
    [Fact]
    public void ShadowCli_DifferentIntentFailsAfterCleanup()
    {
        using var f = new CliFixture();
        f.Service.DifferentIntent = true;
        Assert.Equal(1, f.Run());
        f.AssertRestored();
        var path = Assert.Single(Directory.GetFiles(Path.Combine(f.Core.Root, "artifacts", "shadow"), "*.md", SearchOption.AllDirectories));
        Assert.Contains("Passed=false", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Empty(f.Reports);
    }

    /// <summary>觸發送達後失敗仍停用及清理，且共用告警必須在 lease 歸還後收到真實例外。</summary>
    [Fact]
    public void ShadowCli_TriggerFailureRestoresBeforeReporting()
    {
        using var f = new CliFixture();
        f.Service.FailTrigger = true;
        Assert.Equal(1, f.Run());
        f.AssertRestored();
        Assert.Same(f.Service.Failure, Assert.Single(f.Reports));
        Assert.DoesNotContain(Fixture.SecretValue, f.Output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>SDK 送達後 caller 取消仍須停用及刪除，清理查詢不得沿用已取消的 token。</summary>
    /// <param name="synchronous">以同步 request 或非同步 CRUD 入口送達。</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShadowCli_CancellationAfterTriggerRestoresWithoutNotification(bool synchronous)
    {
        using var f = new CliFixture(synchronous);
        using var cancellation = new CancellationTokenSource();
        f.Service.AfterTrigger = cancellation.Cancel;
        Assert.Equal(130, f.Run(cancellationToken: cancellation.Token));
        f.AssertRestored();
        Assert.Empty(f.Reports);
    }

    /// <summary>部署或基準不合時在 trace／seed 第一筆寫入前失敗，不能靜默只跑新輪。</summary>
    /// <param name="fault">各案只改變一種準備階段的核對項目。</param>
    [Theory]
    [InlineData("configuration")]
    [InlineData("registration-image")]
    [InlineData("legacy-live-only")]
    [InlineData("no-live-case")]
    public void ShadowCli_UnsupportedOrDriftedStateHasNoWrites(string fault)
    {
        using var f = new CliFixture();
        if (fault == "configuration")
            f.Core.Service.Rows.Single(row => row.LogicalName == "sdkmessageprocessingstep")["configuration"] = "{\"mode\":\"Live\"}";
        if (fault == "registration-image")
            f.Core.Service.Seed(new Entity("sdkmessageprocessingstepimage", Guid.NewGuid())
            {
                ["sdkmessageprocessingstepid"] = f.Core.Service.Rows.Single(row => row.LogicalName == "sdkmessageprocessingstep").ToEntityReference(),
                ["name"] = "UnexpectedImage"
            });
        if (fault == "legacy-live-only")
        {
            var path = Path.Combine(f.ScenarioRoot, "expected.json");
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"basis\":\"new\"", "\"basis\":\"legacy-live-only\"", StringComparison.Ordinal));
        }
        if (fault == "no-live-case")
        {
            var path = Path.Combine(f.ScenarioRoot, "scenarios.json");
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"liveShadow\":true", "\"liveShadow\":false", StringComparison.Ordinal));
        }
        var before = f.Core.Service.Writes.Count;
        Assert.Equal(1, f.Run());
        Assert.Equal(before, f.Core.Service.Writes.Count);
        Assert.Equal(0, f.Service.Triggers);
        Assert.Equal(f.Connects, f.Releases);
    }

    /// <summary>秘密中的 ID 只是候選；與服務端關聯不合時不得寫 trace、seed 或步驟。</summary>
    [Fact]
    public void ShadowCli_WrongConfiguredIdentityHasNoWrites()
    {
        using var f = new CliFixture();
        f.WrongIdentity = true;
        var before = f.Core.Service.Writes.Count;
        Assert.Equal(1, f.Run());
        Assert.Equal(before, f.Core.Service.Writes.Count);
        Assert.Equal(1, f.Connects);
        Assert.Equal(1, f.Releases);
    }

    /// <summary>固定來源邊界、組織、參數及 caller 取消必須在連線前拒絕。</summary>
    /// <param name="fault">只注入一種命令前置失敗。</param>
    [Theory]
    [InlineData("path")]
    [InlineData("organization")]
    [InlineData("unknown-option")]
    [InlineData("duplicate-option")]
    [InlineData("cancelled")]
    public void ShadowCli_InvalidInputRefusesBeforeConnect(string fault)
    {
        using var f = new CliFixture();
        var args = f.Arguments.ToList();
        if (fault == "path") args[^1] = f.Core.Root;
        if (fault == "organization") args[2] = "production";
        if (fault == "unknown-option") args.AddRange(["--token", Fixture.SecretValue]);
        if (fault == "duplicate-option") args.AddRange(["--module", "localization"]);
        using var cancellation = new CancellationTokenSource();
        if (fault == "cancelled") cancellation.Cancel();
        Assert.Equal(fault == "cancelled" ? 130 : 2, f.Run(args.ToArray(), cancellation.Token));
        Assert.Equal(0, f.Connects);
        Assert.DoesNotContain(Fixture.SecretValue, f.Output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>shadow 與 apply 共用組織鎖；取鎖逾時前不能建立第二個 SDK lease。</summary>
    [Fact]
    public void ShadowCli_UsesExistingOrganizationWriteLock()
    {
        using var f = new CliFixture();
        using var held = OrganizationWriteLock.Acquire(Path.Combine(f.ArtifactRoot, Fixture.Organization), TimeSpan.Zero);
        Assert.Equal(1, f.Run());
        Assert.Equal(0, f.Connects);
        Assert.IsType<TimeoutException>(Assert.Single(f.Reports));
    }

    /// <summary>同步步驟沒有 asyncoperation；必須用本次請求 ID、正式 step、型別及時間窗找唯一追蹤。</summary>
    [Fact]
    public void ShadowCli_SynchronousRoundUsesExactRequestTraceWithoutAsyncOperation()
    {
        using var f = new CliFixture(synchronous: true);
        Assert.Equal(0, f.Run());
        Assert.NotEqual(Guid.Empty, f.Service.RequestId);
        Assert.Equal(0, f.Service.AsyncQueries);
        Assert.Equal(1, f.Service.Triggers);
        f.AssertRestored();
    }

    /// <summary>同步追蹤歧義不能取第一筆；請求已建立的記錄仍須由 owner 排空。</summary>
    [Fact]
    public void ShadowCli_DuplicateSynchronousTraceFailsClosedAndRestores()
    {
        using var f = new CliFixture(synchronous: true);
        f.Service.DuplicateTrace = true;
        Assert.Equal(1, f.Run());
        Assert.Equal(1, f.Service.Triggers);
        Assert.Equal(0, f.Service.AsyncQueries);
        Assert.IsType<InvalidDataException>(Assert.Single(f.Reports));
        f.AssertRestored();
    }

    /// <summary>單案例擁有合成方案、cwd、服務及 writer；報告回呼同時驗 lease 先釋放。</summary>
    private sealed class CliFixture : IOrganizationConnector, IDisposable
    {
        private readonly string previousDirectory = Directory.GetCurrentDirectory();
        internal Fixture Core { get; } = new();
        internal TestService Service { get; }
        internal StringWriter Output { get; } = new();
        internal List<Exception> Reports { get; } = [];
        internal int Connects;
        internal int Releases;
        internal bool WrongIdentity;
        internal string ArtifactRoot => Path.Combine(Core.Root, "artifacts", "d365");
        internal string ScenarioRoot => Path.Combine(Core.Root, "SpeechMessageProducts.GoodShepherd.Core.Tests", "Golden", "Legacy", "localization");
        internal string[] Arguments => ["shadow", "--org", Fixture.Organization, "--module", "localization", "--scenario-root", ScenarioRoot];

        /// <summary>使用實際 loader、PE reader 及引擎建立可核對的離線部署，不注入預設成功的驗證。</summary>
        internal CliFixture(bool synchronous = false)
        {
            Service = new TestService(Core) { Synchronous = synchronous };
            var step = Core.Manifest.Steps[0] with { Module = "localization", UnsecureConfig = "{\"mode\":\"Shadow\"}",
                Mode = synchronous ? "Synchronous" : "Asynchronous" };
            var manifest = Core.Manifest with { Steps = [step] };
            var registration = Path.Combine(Core.Root, "d365", "registration");
            var plugins = Path.Combine(Core.Root, "artifacts", "plugins");
            Directory.CreateDirectory(registration);
            Directory.CreateDirectory(plugins);
            Directory.CreateDirectory(ScenarioRoot);
            File.WriteAllText(Path.Combine(Core.Root, "SpeechMessageProducts.sln"), "");
            File.WriteAllText(Path.Combine(registration, "plugins.registration.json"),
                JsonSerializer.Serialize(new RegistrationDocument { Assemblies = [manifest] }, RegistrationManifestLoader.Json));
            var dll = Path.Combine(plugins, manifest.AssemblyName + ".dll");
            File.Copy(Core.Emit(), dll);
            var image = AssemblyCatalog.Read(dll, manifest);
            Core.Engine.Apply(Core.Engine.Plan(manifest, image, Fixture.Organization), manifest, image);
            File.WriteAllText(Path.Combine(ScenarioRoot, "scenarios.json"), """
                {"module":"localization","cases":[{"name":"one","church":{"alias":"church-a","organizationName":"goodshepherdback","timeZone":"Asia/Taipei","culture":"zh-TW"},"seed":[{"alias":"church-a","entity":"account","fields":{}},{"alias":"contact-1","entity":"contact","fields":{}},{"alias":"task-1","entity":"task","fields":{"subject":"synthetic"}}],"trigger":{"project":"Synthetic","type":"SpeechMessage.Dynamics.Plugins.Diagnostics.DiagnosticsPingPlugin","entity":"task","message":"Create","target":"task-1","fields":{},"depth":1},"now":"2026-11-14T00:00:00Z","volatile":[],"liveShadow":true,"observe":[{"alias":"task-1","entity":"task","fields":["subject"]}]}]}
                """);
            File.WriteAllText(Path.Combine(ScenarioRoot, "expected.json"), """
                {"module":"localization","basis":"new","cases":[{"name":"one","entrypoint":"SpeechMessage.Dynamics.Plugins.Diagnostics.DiagnosticsPingPlugin","writes":[],"lineCalls":[],"outputs":{},"exception":null}],"declaredDifferences":[]}
                """);
            Directory.SetCurrentDirectory(Core.Root);
        }

        /// <summary>呼叫正式入口；秘密僅為本案例的常值，沒有磁碟設定或環境依賴。</summary>
        internal int Run(string[]? args = null, CancellationToken cancellationToken = default) =>
            DeploymentCli.Run(args ?? Arguments, this, Output, failure =>
            {
                Assert.Equal(Connects, Releases);
                Reports.Add(failure);
            }, ArtifactRoot, key => key switch
            {
                "ShadowTestChurchId" => (WrongIdentity ? Guid.NewGuid() : Service.Church).ToString("D"),
                "ShadowTestContactId" => Service.Contact.ToString("D"),
                "TestLineUserId" => "synthetic-recipient",
                _ => throw new InvalidOperationException("Unexpected secret request.")
            }, TimeSpan.Zero, cancellationToken);

        /// <summary>合成 lease 由 CLI using 歸還；服務本身由 fixture 持有，不跨案例共用。</summary>
        public OrganizationLease Connect(string organization)
        {
            Assert.Equal(Fixture.Organization, organization);
            Connects++;
            return new OrganizationLease(Service, () => Releases++);
        }

        /// <summary>檢查正式 runtime 的自建清冊已排空，且沒有寫入共享教會與會友。</summary>
        internal void AssertRestored()
        {
            Assert.DoesNotContain(Core.Service.Rows, row => row.LogicalName == "task");
            Assert.Equal(1, Core.Service.Rows.Single(row => row.Id == Service.Organization).GetAttributeValue<OptionSetValue>("plugintracelogsetting").Value);
            Assert.All(Core.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstep"),
                row => Assert.Equal(1, row.GetAttributeValue<OptionSetValue>("statecode").Value));
            Assert.Equal(Connects, Releases);
            Assert.Equal("shared", Core.Service.Rows.Single(row => row.Id == Service.Church).GetAttributeValue<string>("name"));
        }

        /// <summary>即使斷言失敗仍先還原 cwd，再確定釋放案例的 writer 與唯一暫存根。</summary>
        public void Dispose()
        {
            Directory.SetCurrentDirectory(previousDirectory);
            Output.Dispose();
            Core.Dispose();
        }
    }

    /// <summary>只補權威身分、metadata 與有界追蹤的獨占替身，不持有網路或背景資源。</summary>
    private sealed class TestService : IOrganizationService
    {
        private readonly Fixture core;
        private readonly Guid correlation = Guid.NewGuid();
        internal Guid Organization { get; } = Guid.NewGuid();
        internal Guid Church { get; } = Guid.NewGuid();
        internal Guid Contact { get; } = Guid.NewGuid();
        internal Exception Failure { get; } = new InvalidOperationException(Fixture.SecretValue);
        internal bool DifferentIntent;
        internal bool FailTrigger;
        internal Action? AfterTrigger;
        internal int Triggers;
        internal bool Synchronous;
        internal bool DuplicateTrace;
        internal Guid RequestId;
        internal int AsyncQueries;
        private Guid target;

        /// <summary>權威種子由測試安排提供；正式 runtime 不得建立或刪除這些共享列。</summary>
        internal TestService(Fixture core)
        {
            this.core = core;
            core.Service.Seed(new Entity("organization", Organization) { ["plugintracelogsetting"] = new OptionSetValue(1) });
            core.Service.Seed(new Entity("account", Church) { ["name"] = "shared" });
            core.Service.Seed(new Entity("contact", Contact) { ["new_lineid"] = "synthetic-recipient", ["parentcustomerid"] = new EntityReference("account", Church) });
        }

        /// <summary>提供正式 SDK response 型別；未知訊息交既有替身明確白名單。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request is CreateRequest create)
            {
                Assert.True(Synchronous);
                RequestId = create.RequestId ?? Guid.Empty;
                Assert.NotEqual(Guid.Empty, RequestId);
                return new CreateResponse { Results = { ["id"] = Create(create.Target) } };
            }
            if (request is RetrieveCurrentOrganizationRequest)
                return new RetrieveCurrentOrganizationResponse { Results = { ["Detail"] = new OrganizationDetail { OrganizationId = Organization, UniqueName = Fixture.Organization } } };
            if (request is not RetrieveEntityRequest metadata) return core.Service.Execute(request);
            var primary = metadata.LogicalName switch { "task" => "activityid", "account" => "accountid", "contact" => "contactid", _ => throw new InvalidOperationException() };
            var entity = new EntityMetadata { LogicalName = metadata.LogicalName };
            var subject = new StringAttributeMetadata { LogicalName = "subject", MaxLength = 100 };
            subject.GetType().GetProperty("IsValidForCreate")!.SetValue(subject, true);
            subject.GetType().GetProperty("IsValidForUpdate")!.SetValue(subject, true);
            entity.GetType().GetProperty("PrimaryIdAttribute")!.SetValue(entity, primary);
            entity.GetType().GetProperty("Attributes")!.SetValue(entity, new AttributeMetadata[] { new UniqueIdentifierAttributeMetadata { LogicalName = primary }, subject });
            return new RetrieveEntityResponse { Results = { ["EntityMetadata"] = entity } };
        }

        /// <summary>真正呼叫通用服務建列，補官方活動主鍵，故障在送達後注入以驗清理清冊。</summary>
        public Guid Create(Entity entity)
        {
            var id = core.Service.Create(entity);
            if (entity.LogicalName == "task")
            {
                target = id;
                core.Service.Rows.Single(row => row.LogicalName == "task" && row.Id == id)["activityid"] = id;
                Triggers++;
                if (FailTrigger) throw Failure;
                AfterTrigger?.Invoke();
            }
            return id;
        }

        /// <summary>一般讀取委派獨占假組織，仍會核對 ID。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => core.Service.Retrieve(entityName, id, columnSet);
        /// <summary>所有設定及停用寫入皆走真正引擎與通用替身。</summary>
        public void Update(Entity entity) => core.Service.Update(entity);
        /// <summary>清理只接受正式 runtime 提供的精確身份。</summary>
        public void Delete(string entityName, Guid id) => core.Service.Delete(entityName, id);

        /// <summary>精確驗非同步 step、target、correlation 及型別；不以任何查詢都回成功。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var expression = Assert.IsType<QueryExpression>(query);
            if (expression.EntityName is not ("asyncoperation" or "plugintracelog")) return core.Service.RetrieveMultiple(query);
            Assert.Equal(2, expression.TopCount);
            Assert.Contains(expression.Criteria.Conditions, condition => condition.AttributeName == "createdon" && condition.Operator == ConditionOperator.OnOrAfter);
            if (expression.EntityName == "asyncoperation")
            {
                AsyncQueries++;
                Assert.False(Synchronous);
                Assert.Contains(expression.Criteria.Conditions, condition => condition.AttributeName == "regardingobjectid" && Equals(condition.Values[0], target));
                var step = core.Service.Rows.Single(row => row.LogicalName == "sdkmessageprocessingstep");
                Assert.Contains(expression.Criteria.Conditions, condition => condition.AttributeName == "owningextensionid" && Equals(condition.Values[0], step.Id));
                return new EntityCollection([new Entity("asyncoperation", Guid.NewGuid()) { ["correlationid"] = correlation, ["statecode"] = new OptionSetValue(3), ["statuscode"] = new OptionSetValue(30) }]);
            }
            if (Synchronous)
            {
                Assert.Contains(expression.Criteria.Conditions, condition => condition.AttributeName == "requestid" && Equals(condition.Values[0], RequestId));
                Assert.Contains(expression.Criteria.Conditions, condition => condition.AttributeName == "pluginstepid" &&
                    Equals(condition.Values[0], core.Service.Rows.Single(row => row.LogicalName == "sdkmessageprocessingstep").Id));
                Assert.Contains(expression.Criteria.Conditions, condition => condition.AttributeName == "mode" && Equals(condition.Values[0], 0));
            }
            else Assert.Contains(expression.Criteria.Conditions, condition => condition.AttributeName == "correlationid" && Equals(condition.Values[0], correlation));
            Assert.Contains(expression.Criteria.Conditions, condition => condition.AttributeName == "typename" && Equals(condition.Values[0], new StepRegistration().Type));
            var trace = DifferentIntent
                ? "SMV-SHADOW|1|{\"seq\":1,\"operation\":\"Update\",\"entity\":\"task\",\"id\":\"" + target.ToString("D") + "\",\"fields\":{\"score\":9}}\r\nSMV-SHADOW|1|{\"end\":true,\"count\":1}"
                : "SMV-SHADOW|1|{\"end\":true,\"count\":0}";
            var result = new EntityCollection([new Entity("plugintracelog", Guid.NewGuid()) { ["messageblock"] = trace }]);
            if (DuplicateTrace) result.Entities.Add(new Entity("plugintracelog", Guid.NewGuid()) { ["messageblock"] = trace });
            return result;
        }

        /// <summary>此模組沒有關聯寫入，意外呼叫必須失敗。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>此模組沒有解除關聯寫入，意外呼叫必須失敗。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}

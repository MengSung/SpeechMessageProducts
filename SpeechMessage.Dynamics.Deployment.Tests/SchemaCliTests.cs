/*
 * 檔案整體說明
 *
 * 目的與定位：本檔從正式DeploymentCli入口驗P1結構命令與P0路由相容，確保新模式不靠Plugin DLL或測試專用分支運作。
 * 主要流程：在獨立最小repo建立正式schema，經真CLI／engine完成plan→apply→verify、第二輪零寫入與export；六命令六租約必須完整歸還。
 * 拒絕情境：非白名單組織、重複／未知選項、外部schema或其他組織plan路徑，全部在Connect之前停止。
 * 例外與取消：caller明確取消不入場不告警；內部timeout仍失敗；真Program／ExceptionDiagnostics寫一筆且不含原敏感本文。
 * 資源與隔離：Deployment working directory集合序列化cwd切換；CliFixture在finally恢復cwd、Dispose writer並刪單案目錄，來源純合成SDK。
 * 維護要求：保留原62案及本案正負路由，不以mock被呼叫等同整合成功；未知正式憑證／VM備份不會被本測試建立或自動授權。
 */
using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using SpeechMessage.Dynamics.Deployment.Schema;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>真CLI與真引擎離線往返；只替換外部SDK，不需要Plugin DLL或User Secrets，驗路由、租約及寫鎖生命週期。</summary>
[Collection("Deployment working directory")]
public sealed class SchemaCliTests
{
    /// <summary>固定唯讀CLI語法，產品只讀輸入且每案不修改，避免反覆配置相同陣列。</summary>
    private static readonly string[] ExportArguments = { "export-metadata", "--org", "goodshepherdback" };
    /// <summary>schema plan→apply→verify及唯讀export通過，租約每指令歸還且不讀Plugin DLL；第二輪plan/apply零寫入。</summary>
    [Fact]
    public void Run_SchemaLifecycle_IsIndependentFromPluginAndReturnsEveryLease()
    {
        using var fixture = new CliFixture();
        Assert.Equal(0, fixture.Run("plan", "--org", "goodshepherdback", "--schema", fixture.Schema));
        var firstPlan = Assert.Single(Directory.GetFiles(fixture.Root, "schema-plan.json", SearchOption.AllDirectories));
        var readable = File.ReadAllText(Path.Combine(Path.GetDirectoryName(firstPlan)!, "schema-plan.md"));
        Assert.Contains(SchemaFiles.ReadPlan(firstPlan).Sha256, readable, StringComparison.Ordinal);
        Assert.Contains("CreateEntity", readable, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Run("apply", "--org", "goodshepherdback", "--schema", fixture.Schema, "--plan", firstPlan));
        Assert.Equal(0, fixture.Run("verify", "--org", "goodshepherdback", "--schema", fixture.Schema));
        var writes = fixture.Connector.Service.WriteCount;
        Assert.Equal(0, fixture.Run("plan", "--org", "goodshepherdback", "--schema", fixture.Schema));
        var plans = Directory.GetFiles(fixture.Root, "schema-plan.json", SearchOption.AllDirectories);
        var secondPlan = Assert.Single(Array.FindAll(plans, path => path != firstPlan));
        Assert.Empty(SchemaFiles.ReadPlan(secondPlan).Operations);
        Assert.Equal(0, fixture.Run("apply", "--org", "goodshepherdback", "--schema", fixture.Schema, "--plan", secondPlan));
        Assert.Equal(writes, fixture.Connector.Service.WriteCount);
        Assert.Equal(0, fixture.Run("export-metadata", "--org", "goodshepherdback"));
        Assert.Equal(6, fixture.Connector.Opened);
        Assert.Equal(6, fixture.Connector.Returned);
        Assert.Empty(fixture.Failures);
    }

    /// <summary>先以合成SDK完成apply，再讓已發布metadata缺少一個P1欄位；verify必須回差異且零寫入，不能以未發布候選宣告完成。</summary>
    [Fact]
    public void Run_VerifyOnlyUnpublishedFieldExists_RejectsFalseCompletion()
    {
        using var fixture = new CliFixture();
        Assert.Equal(0, fixture.Run("plan", "--org", "goodshepherdback", "--schema", fixture.Schema));
        var plan = Assert.Single(Directory.GetFiles(fixture.Root, "schema-plan.json", SearchOption.AllDirectories));
        Assert.Equal(0, fixture.Run("apply", "--org", "goodshepherdback", "--schema", fixture.Schema, "--plan", plan));
        var writes = fixture.Connector.Service.WriteCount;
        fixture.Connector.Service.MissingPublishedField = true;
        Assert.Equal(1, fixture.Run("verify", "--org", "goodshepherdback", "--schema", fixture.Schema));
        Assert.Equal(writes, fixture.Connector.Service.WriteCount);
        Assert.Equal(3, fixture.Connector.Opened);
        Assert.Equal(3, fixture.Connector.Returned);
        Assert.Empty(fixture.Failures);
    }

    /// <summary>明確schema smoke須先驗已發布結構；缺結構回1而非未知語法2，不可先建任何測試資料，租約完整歸還。</summary>
    [Fact]
    public void Run_SchemaSmoke_DifferencesPreventAnyDataWrite()
    {
        using var fixture = new CliFixture();
        Assert.Equal(1, fixture.Run("smoke", "--org", "goodshepherdback", "--schema", fixture.Schema));
        Assert.Equal(0, fixture.Connector.Service.WriteCount);
        Assert.Equal(1, fixture.Connector.Opened);
        Assert.Equal(1, fixture.Connector.Returned);
        Assert.Empty(fixture.Failures);
    }

    /// <summary>實體雖齊全但key仍Pending時，smoke必須回3且零新增業務列，不能藉唯一鍵測試假裝索引已Active。</summary>
    [Fact]
    public void Run_SchemaSmoke_PendingKeyPreventsAnyDataWrite()
    {
        using var fixture = new CliFixture();fixture.Connector.Service.PendingKey = true;
        Assert.Equal(0, fixture.Run("plan", "--org", "goodshepherdback", "--schema", fixture.Schema));
        var plan = Assert.Single(Directory.GetFiles(fixture.Root, "schema-plan.json", SearchOption.AllDirectories));
        Assert.Equal(3, fixture.Run("apply", "--org", "goodshepherdback", "--schema", fixture.Schema, "--plan", plan));
        var writes = fixture.Connector.Service.WriteCount;
        Assert.Equal(3, fixture.Run("smoke", "--org", "goodshepherdback", "--schema", fixture.Schema));
        Assert.Equal(writes, fixture.Connector.Service.WriteCount);
        Assert.Equal(3, fixture.Connector.Returned);
        Assert.Empty(fixture.Failures);
    }

    /// <summary>非白名單、未知／重複參數、schema外部路徑與其他組織plan皆在連線前拒絕，不讀秘密或建立空成功。</summary>
    /// <param name="fault">要注入的CLI錯誤。</param>
    [Theory]
    [InlineData("organization")]
    [InlineData("duplicate-option")]
    [InlineData("unknown-option")]
    [InlineData("external-schema")]
    [InlineData("external-plan")]
    [InlineData("external-before")]
    public void Run_InvalidSchemaArguments_RejectBeforeConnect(string fault)
    {
        using var fixture = new CliFixture();
        var args = fault switch
        {
            "organization" => new[] { "plan", "--org", "production", "--schema", fixture.Schema },
            "duplicate-option" => new[] { "plan", "--org", "goodshepherdback", "--schema", fixture.Schema, "--schema", fixture.Schema },
            "unknown-option" => new[] { "plan", "--org", "goodshepherdback", "--schema", fixture.Schema, "--password", "synthetic" },
            "external-schema" => new[] { "plan", "--org", "goodshepherdback", "--schema", Path.Combine(fixture.Root, "external.json") },
            "external-plan" => new[] { "apply", "--org", "goodshepherdback", "--schema", fixture.Schema, "--plan", Path.Combine(Path.GetTempPath(), "other-organization.json") },
            "external-before" => new[] { "complete-created-audit", "--org", "goodshepherdback", "--schema", fixture.Schema,
                "--plan", Path.Combine(fixture.Root, "artifacts", "d365", "goodshepherdback", "plan.json"), "--before", Path.Combine(Path.GetTempPath(), "foreign-before.json") },
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(2, fixture.Run(args));
        Assert.Equal(0, fixture.Connector.Opened);
        Assert.Equal(0, fixture.Connector.Service.WriteCount);
    }

    /// <summary>schema verify有差異回非零；SDK故障保同例外交既有report一次，指令lease仍必須返回。</summary>
    [Fact]
    public void Run_DifferenceAndSdkFailure_AreReportedWithoutSensitiveOutput()
    {
        using var fixture = new CliFixture();
        Assert.Equal(1, fixture.Run("verify", "--org", "goodshepherdback", "--schema", fixture.Schema));
        fixture.Connector.Service.Failure = new InvalidOperationException("synthetic-private-value");
        Assert.Equal(1, fixture.Run("export-metadata", "--org", "goodshepherdback"));
        Assert.Same(fixture.Connector.Service.Failure, Assert.Single(fixture.Failures));
        Assert.DoesNotContain("synthetic-private-value", fixture.Output.ToString(), StringComparison.Ordinal);
        Assert.Equal(fixture.Connector.Opened, fixture.Connector.Returned);
    }

    /// <summary>新schema路由必須經真Program共用ExceptionDiagnostics，原例外只記一筆且本文不落檔；不是僅驗mock notifier。</summary>
    [Fact]
    public void Run_RealDiagnosticOwner_RecordsOneIncidentWithoutPrivateBody()
    {
        using var fixture = new CliFixture();
        fixture.Connector.Service.Failure = new InvalidOperationException("synthetic-private-schema-token");
        var configuration = new ConfigurationBuilder().Build();
        var logDirectory = Path.Combine(fixture.Root, "logs");
        try
        {
            Assert.Equal(1, Program.RunCommand(ExportArguments, fixture.Connector,
                fixture.Output, configuration, logDirectory, default));
            var line = Assert.Single(File.ReadAllLines(Path.Combine(logDirectory, "Exception.log")));
            Assert.Contains("InvalidOperationException", line, StringComparison.Ordinal);
            Assert.DoesNotContain("synthetic-private-schema-token", line, StringComparison.Ordinal);
            Assert.Equal(1, fixture.Connector.Returned);
        }
        finally { (configuration as IDisposable)?.Dispose(); }
    }

    /// <summary>明確caller取消在connect之前停止且不告警；內部已取消timeout仍是功能失敗，必須交共用report，不能誤判正常取消。</summary>
    [Fact]
    public void Run_CallerCancellationAndInternalTimeout_HaveDifferentFailureBoundaries()
    {
        using var fixture = new CliFixture();
        using var cancellation = new System.Threading.CancellationTokenSource();
        cancellation.Cancel();
        Assert.Equal(130, DeploymentCli.Run(ExportArguments, fixture.Connector,
            fixture.Output, fixture.Failures.Add, Path.Combine(fixture.Root, "artifacts", "d365"), cancellationToken: cancellation.Token));
        Assert.Equal(0, fixture.Connector.Opened);
        Assert.Empty(fixture.Failures);
        fixture.Connector.Service.Failure = new OperationCanceledException(cancellation.Token);
        Assert.Equal(1, fixture.Run("export-metadata", "--org", "goodshepherdback"));
        Assert.Same(fixture.Connector.Service.Failure, Assert.Single(fixture.Failures));
        Assert.Equal(1, fixture.Connector.Returned);
    }

    /// <summary>同組織兩個apply同時使用同一審核plan，OS寫鎖只容許一個新增；另一個須重驗後拒絕舊plan，不產生重複表或key。</summary>
    [Fact]
    public async System.Threading.Tasks.Task Run_ConcurrentApply_SerializesAndRejectsStaleSecondPlan()
    {
        using var fixture = new CliFixture();
        Assert.Equal(0, fixture.Run("plan", "--org", "goodshepherdback", "--schema", fixture.Schema));
        var plan = Assert.Single(Directory.GetFiles(fixture.Root, "schema-plan.json", SearchOption.AllDirectories));
        var failures = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        var tasks = Enumerable.Range(0, 2).Select(_ => System.Threading.Tasks.Task.Run(() =>
        {
            using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            return DeploymentCli.Run(new[] { "apply", "--org", "goodshepherdback", "--schema", fixture.Schema, "--plan", plan },
                fixture.Connector, output, failures.Enqueue, Path.Combine(fixture.Root, "artifacts", "d365"));
        }));
        var results = await System.Threading.Tasks.Task.WhenAll(tasks);
        Assert.Equal(1, results.Count(result => result == 0));
        Assert.Equal(1, results.Count(result => result == 1));
        Assert.IsType<InvalidDataException>(Assert.Single(failures));
        Assert.Equal(4, fixture.Connector.Service.KeyCreates);
        Assert.Equal(6, fixture.Connector.Service.Entities.Count);
        Assert.Equal(3, fixture.Connector.Opened);
        Assert.Equal(3, fixture.Connector.Returned);
    }

    /// <summary>每案唯一定義／輸出與SDK；只改程序cwd的集合已序列化，finally恢復，沒有跨命令連線或request殘留。</summary>
    private sealed class CliFixture : IDisposable
    {
        private readonly string previous = Environment.CurrentDirectory;
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "p111-cli-" + Guid.NewGuid().ToString("N"));
        internal string Schema { get; }
        internal Connector Connector { get; }
        internal StringWriter Output { get; } = new(System.Globalization.CultureInfo.InvariantCulture);
        internal System.Collections.Generic.List<Exception> Failures { get; } = new();
        /// <summary>最小合成repo根保留正式schema，無Plugin DLL，避免測試意外沿父worktree取得輸入。</summary>
        internal CliFixture()
        {
            var originalSchema = SchemaDefinitionTests.SchemaPath();
            Directory.CreateDirectory(Path.Combine(Root, "d365", "schema"));
            File.WriteAllText(Path.Combine(Root, "SpeechMessageProducts.sln"), "synthetic solution");
            Schema = Path.Combine(Root, "d365", "schema", "speechmessagecore.schema.json");
            File.Copy(originalSchema, Schema);
            File.Copy(Path.Combine(Path.GetDirectoryName(originalSchema)!, "p1-12-acceptance.fixture.json"), Path.Combine(Root, "d365", "schema", "p1-12-acceptance.fixture.json"));
            Connector = new Connector(Root);
            Environment.CurrentDirectory = Root;
        }
        /// <summary>呼叫正式入口，只有connector與輸出根替換，沒有測試專用產品分支。</summary>
        internal int Run(params string[] args) => DeploymentCli.Run(args, Connector, Output, Failures.Add, Path.Combine(Root, "artifacts", "d365"));
        /// <summary>所有指令同步排空後恢復cwd、釋放writer並刪唯一暫存目錄，測試失敗也不遺留全域狀態。</summary>
        public void Dispose() { Environment.CurrentDirectory = previous; Output.Dispose(); Directory.Delete(Root, recursive: true); }
    }

    /// <summary>每指令一個可計數lease，底層完全是合成metadata，沒有真實連線可被共用或洩漏。</summary>
    private sealed class Connector(string directory) : IOrganizationConnector
    {
        internal SchemaEngineTests.Store Service { get; } = new(directory);
        internal int Opened;
        internal int Returned;
        /// <summary>只允許白名單，產品離線操作結束必須歸還一次，不Dispose別案SDK。</summary>
        public OrganizationLease Connect(string organization)
        {
            Assert.Equal("goodshepherdback", organization);
            System.Threading.Interlocked.Increment(ref Opened);
            return new OrganizationLease(Service, () => System.Threading.Interlocked.Increment(ref Returned));
        }
    }
}

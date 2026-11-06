using System;
using System.IO;
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

    /// <summary>非白名單、未知／重複參數、schema外部路徑與其他組織plan皆在連線前拒絕，不讀秘密或建立空成功。</summary>
    /// <param name="fault">要注入的CLI錯誤。</param>
    [Theory]
    [InlineData("organization")]
    [InlineData("duplicate-option")]
    [InlineData("unknown-option")]
    [InlineData("external-schema")]
    [InlineData("external-plan")]
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
            Opened++;
            return new OrganizationLease(Service, () => Returned++);
        }
    }
}

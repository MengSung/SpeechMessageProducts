/*
 * 檔案整體說明
 *
 * 目的與定位：保護 P5-01 外殼的深度政策、Shadow 寫入／推播隔離、例外邊界及
 * 可重用實例的跨教會狀態隔離。測試只使用 Moq 與合成 SDK 服務，不部署、不連
 * goodshepherdback、不呼叫 LINE、不使用任何秘密。
 *
 * 主要功能與流程：以同一個測試外殼交錯執行兩個 context，注入 Create／Update、
 * 業務例外與未知例外；以決定性斷言檢查實服務寫入數、Shadow 記錄、CorrelationId
 * 及原始例外實例。所有 fixture 服務由測試案例擁有，案例結束後清空可變集合。
 *
 * 使用邊界與維護：這些案例是離線契約，不代表 D365 plugintracelog 的實機容量或
 * LINE 送達；任何資源／Task 必須在測試返回前完成，不能以背景工作掩蓋洩漏。
 */
using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Moq;
using Newtonsoft.Json.Linq;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Dynamics.Plugins.Tests.Fakes;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Rules;
using Xunit;

namespace SpeechMessage.Dynamics.Plugins.Tests.Infrastructure;

/// <summary>PluginShellBase 的隔離與失敗契約測試。</summary>
public sealed class PluginShellBaseTests
{
    /// <summary>最大深度的三種例外政策都必須在規則前停止或放行。</summary>
    [Theory]
    [InlineData("None", "Create", 3, false)]
    [InlineData("Create", "Create", 3, true)]
    [InlineData("Update", "Update", 3, false)]
    [InlineData("None", "Update", 2, true)]
    [InlineData("Create", "Update", 3, false)]
    [InlineData("Update", "Delete", 3, true)]
    public void Shell_DepthExceeded_DoesNothing(string exceptionName, string message, int depth, bool expectedRun)
    {
        var service = new FakeOrganizationService();
        var context = new FakePluginExecutionContext(depth: depth, messageName: message);
        var tracing = new FakeTracingService();
        var factory = new Mock<IOrganizationServiceFactory>();
        factory.Setup(x => x.CreateOrganizationService(It.IsAny<Guid>())).Returns(service);
        var provider = new FakeServiceProvider()
            .Add<IPluginExecutionContext>(context.Object)
            .Add<ITracingService>(tracing)
            .Add<IOrganizationServiceFactory>(factory.Object);
        var ran = false;
        var policy = new DepthPolicy(2, (DepthException)Enum.Parse(typeof(DepthException), exceptionName));

        new TestShell(@"{""mode"":""Shadow""}", policy, (_, _) => ran = true).Execute(provider);

        Assert.Equal(expectedRun, ran);
        Assert.Empty(service.Calls);
        factory.Verify(x => x.CreateOrganizationService(It.IsAny<Guid>()), expectedRun ? Times.Once() : Times.Never());
    }

    /// <summary>Shadow 規則的寫入只進操作區域 recording，真服務沒有寫入。</summary>
    [Fact]
    public void Shell_ShadowMode_NoWritesExecuted_AllRecorded()
    {
        var service = new FakeOrganizationService();
        var context = new FakePluginExecutionContext();
        var tracing = new FakeTracingService();
        var factory = new Mock<IOrganizationServiceFactory>();
        factory.Setup(x => x.CreateOrganizationService(It.IsAny<Guid>())).Returns(service);
        var provider = new FakeServiceProvider()
            .Add<IPluginExecutionContext>(context.Object)
            .Add<ITracingService>(tracing)
            .Add<IOrganizationServiceFactory>(factory.Object);

        new TestShell(@"{""mode"":""Shadow""}", new DepthPolicy(2), (input, services) =>
        {
            var id = services.Organization.Create(new Entity("task"));
            services.Organization.Update(new Entity("task", id) { ["subject"] = "synthetic", ["new_status"] = 7 });
        }).Execute(provider);

        Assert.Empty(service.Calls);
        var lines = ShadowLines(tracing);
        Assert.Equal(3, lines.Length);
        Assert.Equal("Create", (string?)lines[0]["operation"]);
        Assert.Equal("Update", (string?)lines[1]["operation"]);
        Assert.Equal(7, (int)lines[1]["fields"]!["new_status"]!);
        Assert.Null(lines[1]["fields"]!["subject"]);
        Assert.Equal((string?)lines[0]["id"], (string?)lines[1]["id"]);
        Assert.Equal(1, (int)lines[0]["seq"]!);
        Assert.Equal(2, (int)lines[1]["seq"]!);
        Assert.True((bool)lines[2]["end"]!);
        Assert.Equal(2, (int)lines[2]["count"]!);
    }

    /// <summary>Shadow 推播只能寫 sink，不能觸發真推播服務。</summary>
    [Fact]
    public void Shell_ShadowMode_NoPushCalled()
    {
        var service = new FakeOrganizationService();
        var tracing = new FakeTracingService();
        var context = new FakePluginExecutionContext();
        var factory = new Mock<IOrganizationServiceFactory>();
        factory.Setup(x => x.CreateOrganizationService(It.IsAny<Guid>())).Returns(service);
        var provider = new FakeServiceProvider()
            .Add<IPluginExecutionContext>(context.Object)
            .Add<ITracingService>(tracing)
            .Add<IOrganizationServiceFactory>(factory.Object);
        var retry = Guid.NewGuid();

        new TestShell(@"{""mode"":""Shadow""}", new DepthPolicy(2), (input, services) =>
            services.ShadowPush!.Record(Guid.NewGuid(), "synthetic", retry, 1, new string('a', 64))).Execute(provider);

        Assert.Empty(service.Calls);
        var lines = ShadowLines(tracing);
        Assert.Equal(2, lines.Length);
        Assert.Equal("Push", (string?)lines[0]["operation"]);
        Assert.Equal(retry.ToString("D"), (string?)lines[0]["retryKey"]);
        Assert.Equal(1, (int)lines[0]["messageCount"]!);
        Assert.Equal(new string('a', 64), (string?)lines[0]["contentSha256"]);
        Assert.True((bool)lines[1]["end"]!);
    }

    /// <summary>業務拒絕轉成標準 Plugin 例外並保留事件 ID。</summary>
    [Fact]
    public void Shell_BusinessError_ThrowsInvalidPluginExecutionWithEventId()
    {
        var context = new FakePluginExecutionContext(correlationId: Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var provider = Provider(context, new FakeOrganizationService(), new FakeTracingService());

        var error = Assert.Throws<InvalidPluginExecutionException>(() =>
            new TestShell(@"{""mode"":""Shadow""}", new DepthPolicy(2),
                (_, _) => throw new BusinessRuleException(new TextKey("Common.Diagnostics.InvalidConfiguration"))).Execute(provider));

        Assert.Contains(context.Object.CorrelationId.ToString("D"), error.Message, StringComparison.Ordinal);
        Assert.Contains("診斷外掛設定格式錯誤。", error.Message, StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }

    /// <summary>業務資源鍵不存在或缺具名參數時，固定後備文字仍保留事件 ID 且不洩漏鍵值。</summary>
    [Theory]
    [InlineData("Common.UnknownSynthetic")]
    [InlineData("Common.Plugin.EventId")]
    public void Shell_BusinessFormattingFailure_PreservesEventId(string key)
    {
        var context = new FakePluginExecutionContext();
        var provider = Provider(context, new FakeOrganizationService(), new FakeTracingService());

        var error = Assert.Throws<InvalidPluginExecutionException>(() =>
            new TestShell(@"{""mode"":""Shadow""}", new DepthPolicy(2),
                (_, _) => throw new BusinessRuleException(new TextKey(key))).Execute(provider));

        Assert.Contains("無法完成此操作。", error.Message, StringComparison.Ordinal);
        Assert.Contains(context.Object.CorrelationId.ToString("D"), error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(key, error.Message, StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }

    /// <summary>未知例外必須重新拋出同一實例，不可被外殼吞掉或包裝。</summary>
    [Fact]
    public void Shell_UnexpectedError_RethrownNotSwallowed()
    {
        var expected = new InvalidOperationException("synthetic");
        var provider = Provider(new FakePluginExecutionContext(), new FakeOrganizationService(), new FakeTracingService());

        var actual = Assert.Throws<InvalidOperationException>(() =>
            new TestShell(@"{""mode"":""Shadow""}", new DepthPolicy(2), (_, _) => throw expected).Execute(provider));

        Assert.Same(expected, actual);
    }

    /// <summary>規則寫入後拒絕時仍輸出完整安全意圖，finally 必須排空兩種 recorder。</summary>
    [Fact]
    public void Shell_FailedRule_RecordsIntentAndDrainsOwnedState()
    {
        var tracing = new FakeTracingService();
        var service = new FakeOrganizationService();
        var provider = Provider(new FakePluginExecutionContext(), service, tracing);
        RecordingOrganizationService? writes = null;
        ShadowPushRecorder? pushes = null;
        var shell = new TestShell(@"{""mode"":""Shadow""}", new DepthPolicy(2), (_, services) =>
        {
            writes = (RecordingOrganizationService)services.Organization;
            pushes = services.ShadowPush!;
            writes.Create(new Entity("task"));
            pushes.Record(Guid.NewGuid(), "synthetic", Guid.NewGuid(), 1, new string('a', 64));
            throw new BusinessRuleException(new TextKey("Common.Diagnostics.InvalidConfiguration"));
        });

        Assert.Throws<InvalidPluginExecutionException>(() => shell.Execute(provider));

        Assert.Empty(service.Calls);
        Assert.Collection(ShadowLines(tracing).Where(line => line["operation"] is not null),
            line => Assert.Equal("Create", (string?)line["operation"]),
            line => Assert.Equal("Push", (string?)line["operation"]));
        Assert.Empty(writes!.Writes);
        Assert.Empty(pushes!.Records);
        Assert.Throws<ObjectDisposedException>(() => writes.Create(new Entity("task")));
        Assert.Throws<ObjectDisposedException>(() => pushes.Record(Guid.NewGuid(), "synthetic", Guid.NewGuid(), 1, new string('a', 64)));
    }

    /// <summary>追蹤型別與收尾輸出都故障時，仍保留規則原始例外並排空操作狀態。</summary>
    [Fact]
    public void Shell_TraceFailure_PreservesOriginalRuleException()
    {
        var service = new FakeOrganizationService();
        var provider = Provider(new FakePluginExecutionContext(), service, new FakeTracingService());
        var brokenTrace = new Mock<ITracingService>();
        brokenTrace.Setup(x => x.Trace(It.IsAny<string>(), It.IsAny<object[]>()))
            .Throws(new InvalidOperationException("trace unavailable"));
        provider.Add<ITracingService>(brokenTrace.Object);
        var expected = new InvalidOperationException("rule failed");
        RecordingOrganizationService? writes = null;
        var shell = new TestShell(@"{""mode"":""Shadow""}", new DepthPolicy(2), (_, services) =>
        {
            writes = (RecordingOrganizationService)services.Organization;
            writes.Create(new Entity("task"));
            throw expected;
        });

        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => shell.Execute(provider)));
        Assert.Empty(writes!.Writes);
        Assert.Empty(service.Calls);
    }

    /// <summary>同一外殼交錯執行不同教會時，操作記錄只屬本次且不共享。</summary>
    [Fact]
    public void Shell_InterleavedChurches_NoStateShared()
    {
        var service = new FakeOrganizationService();
        var tracingA = new FakeTracingService();
        var tracingB = new FakeTracingService();
        var orgA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var orgB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var providerA = Provider(new FakePluginExecutionContext(organizationId: orgA), service, tracingA);
        var providerB = Provider(new FakePluginExecutionContext(organizationId: orgB), service, tracingB);
        var shell = new TestShell(@"{""mode"":""Shadow""}", new DepthPolicy(2),
            (input, services) => services.Organization.Create(new Entity("task", input.OrganizationId)));

        shell.Execute(providerA);
        shell.Execute(providerB);
        shell.Execute(providerA);

        Assert.Contains(tracingA.Entries, entry => entry.Contains(orgA.ToString("D"), StringComparison.Ordinal));
        Assert.DoesNotContain(tracingA.Entries, entry => entry.Contains(orgB.ToString("D"), StringComparison.Ordinal));
        Assert.Contains(tracingB.Entries, entry => entry.Contains(orgB.ToString("D"), StringComparison.Ordinal));
        Assert.DoesNotContain(tracingB.Entries, entry => entry.Contains(orgA.ToString("D"), StringComparison.Ordinal));
        Assert.Empty(service.Calls);
    }

    /// <summary>每個 provider 借用案例 factory；不處置平台服務，案例終了即丟棄。</summary>
    private static FakeServiceProvider Provider(FakePluginExecutionContext context, FakeOrganizationService service, FakeTracingService tracing)
    {
        var factory = new Mock<IOrganizationServiceFactory>();
        factory.Setup(x => x.CreateOrganizationService(It.IsAny<Guid>())).Returns(service);
        return new FakeServiceProvider()
            .Add<IPluginExecutionContext>(context.Object)
            .Add<ITracingService>(tracing)
            .Add<IOrganizationServiceFactory>(factory.Object);
    }

    /// <summary>解析版本化 JSON 行；追蹤的例外型別訊息不屬影子資料。</summary>
    private static JObject[] ShadowLines(FakeTracingService tracing) =>
        tracing.Entries.Where(entry => entry.StartsWith("SMV-SHADOW|1|", StringComparison.Ordinal))
            .Select(entry => JObject.Parse(entry.Substring("SMV-SHADOW|1|".Length))).ToArray();

    /// <summary>只供測試注入規則委派，不持有跨案例狀態或外部資源。</summary>
    private sealed class TestShell : PluginShellBase
    {
        private readonly Action<PluginInput, PluginServices> action;

        /// <summary>借用測試委派，沒有產生非同步或跨案例工作。</summary>
        internal TestShell(string config, DepthPolicy policy, Action<PluginInput, PluginServices> action)
            : base(config, string.Empty, policy)
        {
            this.action = action;
        }

        /// <summary>只在本次 Execute 同步執行合成規則。</summary>
        internal override void ExecuteRule(PluginInput input, PluginServices services) => action(input, services);
    }
}

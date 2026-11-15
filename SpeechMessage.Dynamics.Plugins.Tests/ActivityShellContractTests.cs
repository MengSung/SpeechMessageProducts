/*
 * 檔案整體說明
 *
 * 目的與定位：保護 P5-05 活動外殼的模式參數、影子服務、例外與單次執行生命週期。
 * 以 WorkflowInvoker 執行真實 ActivityShellBase，Moq 僅替代平台 extension；
 * Dataverse 使用案例擁有的記憶體 FakeOrganizationService，不連外、不推 LINE。
 *
 * 主要功能與流程：三個核定測試先驗證 Shadow 意圖、空白 Live 與 probe 參數；
 * 必要故障案例注入規則拒絕、未知例外及追蹤失敗，斷言原例外、完整收尾與 Dispose。
 * 同一活動依序執行 A／B／A，以不同組織、使用者與記錄檢查 scope 不混用。
 * 盤點已確認舊週報活動完全沒有 Input／Output；正式活動加入後，反射契約
 * 仍只能找到新增的 ExecutionMode，不應憑空增加舊業務參數。
 *
 * 資源與隔離：extern alias 將連結共用檔的 Workflows 型別與 Plugins 型別隔開。
 * 每次 Invoke 使用新的 invoker／extension，十秒逾時只供同步測試防護，不代表
 * 可以中斷同步 SDK。沒有背景工作；fixture 在 Invoke 完成後處置自己擁有的
 * fake service、清空追蹤及重設 Mock。外殼只能處置自己的兩個 recorder，
 * 不得關閉借用的 SDK service。本檔的測試證據僅屬離線契約。
 */
extern alias Workflows;

using System;
using System.Activities;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Workflow;
using Moq;
using Newtonsoft.Json.Linq;
using SpeechMessage.Dynamics.Plugins.Tests.Fakes;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Rules;
using Xunit;
using ActivityInput = Workflows::SpeechMessage.Dynamics.Workflows.Infrastructure.ActivityInput;
using ActivityShellBase = Workflows::SpeechMessage.Dynamics.Workflows.Infrastructure.ActivityShellBase;
using PluginServices = Workflows::SpeechMessage.Dynamics.Plugins.Infrastructure.PluginServices;
using RecordingOrganizationService = Workflows::SpeechMessage.Dynamics.Plugins.Infrastructure.RecordingOrganizationService;
using ShadowPushRecorder = Workflows::SpeechMessage.Dynamics.Plugins.Infrastructure.ShadowPushRecorder;

namespace SpeechMessage.Dynamics.Plugins.Tests;

/// <summary>
/// 活動共用外殼的最小行為契約；錯誤使用真服務、遺失 finally 或保留前次 scope
/// 都應由實際 Invoke 的副作用與輸出失敗，而非以原始碼文字充當執行證據。
/// </summary>
public sealed class ActivityShellContractTests
{
    /// <summary>
    /// 注入 Create、同次 Update 與推播意圖；若 Shadow 誤用真服務、遺漏意圖或未處置
    /// recorder，零真寫入、連續序號、完整 end 及 Dispose 後拒絕的斷言必須失敗。
    /// </summary>
    [Fact]
    public void ActivityShell_ShadowMode_NoWritesExecuted_AllRecorded()
    {
        using var scope = new ActivityScope();
        RecordingOrganizationService? writes = null;
        ShadowPushRecorder? pushes = null;
        var activity = new ProbeActivity((input, services) =>
        {
            Assert.True(services.IsShadow);
            writes = Assert.IsType<RecordingOrganizationService>(services.Organization);
            pushes = Assert.IsType<ShadowPushRecorder>(services.ShadowPush);
            var id = services.Organization.Create(new Entity("task"));
            services.Organization.Update(new Entity("task", id) { ["new_status"] = 7 });
            Assert.Equal(7, services.Organization.Retrieve("task", id, new ColumnSet("new_status"))["new_status"]);
            pushes.Record(input.UserId, "synthetic.workflow", input.PrimaryEntityId, 1, new string('a', 64));
        });

        scope.Invoke(activity, "Shadow");

        Assert.Empty(scope.Service.Calls);
        var lines = ShadowLines(scope.Tracing);
        Assert.Equal(4, lines.Length);
        Assert.Equal("Create", (string?)lines[0]["operation"]);
        Assert.Equal("Update", (string?)lines[1]["operation"]);
        Assert.Equal((string?)lines[0]["id"], (string?)lines[1]["id"]);
        Assert.Equal(7, (int)lines[1]["fields"]!["new_status"]!);
        Assert.Equal("Push", (string?)lines[2]["operation"]);
        Assert.Equal(scope.Context.Object.UserId.ToString("D"), (string?)lines[2]["contactId"]);
        Assert.Equal(scope.Context.Object.PrimaryEntityId.ToString("D"), (string?)lines[2]["retryKey"]);
        Assert.Equal<int>([1, 2, 3], lines.Take(3).Select(line => (int)line["seq"]!).ToArray());
        Assert.True((bool)lines[3]["end"]!);
        Assert.Equal(3, (int)lines[3]["count"]!);
        AssertDrained(writes!, pushes!);
        // SDK 只借用：收尾後仍可使用，關閉責任留給本案例 owner。
        Assert.NotEqual(Guid.Empty, scope.Service.Create(new Entity("task")));
    }

    /// <summary>
    /// 未提供、空字串與純空白參數皆沿用 Live；明確 Live 也須直達借用服務，
    /// 以實際資料列與零影子行斷言，避免只檢查模式旗標而漏掉錯誤路由。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    [InlineData("Live")]
    public void ActivityShell_ExecutionModeBlank_IsLive(string? mode)
    {
        using var scope = new ActivityScope();
        var activity = new ProbeActivity((input, services) =>
        {
            Assert.False(services.IsShadow);
            Assert.Null(services.ShadowPush);
            Assert.Same(scope.Service, services.Organization);
            services.Organization.Create(new Entity("task", input.PrimaryEntityId) { ["new_status"] = 9 });
        });

        scope.Invoke(activity, mode);

        Assert.Equal(1, scope.Service.Calls["Create"]);
        Assert.Empty(ShadowLines(scope.Tracing));
        var row = scope.Service.Retrieve("task", scope.Context.Object.PrimaryEntityId, new ColumnSet("new_status"));
        Assert.Equal(9, row["new_status"]);
    }

    /// <summary>
    /// probe 只宣告基礎模式輸入；反射保護公開 CodeActivity、可省略的字串 InArgument
    /// 與 internal 規則入口。舊週報活動已確認沒有 Input／Output，因此新增模式
    /// 必須是唯一參數；目前以 probe 驗證，正式活動加入後沿用相同斷言。
    /// </summary>
    [Fact]
    public void ActivityShell_ArgumentsMatchLegacyNamesAndTypes()
    {
        var shell = typeof(ActivityShellBase);
        Assert.True(shell.IsPublic);
        Assert.True(shell.IsAbstract);
        Assert.True(typeof(CodeActivity).IsAssignableFrom(shell));
        var arguments = typeof(ProbeActivity).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType.IsGenericType &&
                (property.PropertyType.GetGenericTypeDefinition() == typeof(InArgument<>) ||
                 property.PropertyType.GetGenericTypeDefinition() == typeof(OutArgument<>))).ToArray();
        var mode = Assert.Single(arguments);
        Assert.Equal("ExecutionMode", mode.Name);
        Assert.Equal(typeof(InArgument<string>), mode.PropertyType);
        Assert.True(mode.CanRead && mode.CanWrite);
        Assert.Single(mode.GetCustomAttributes(typeof(InputAttribute), true));
        Assert.Empty(mode.GetCustomAttributes(typeof(RequiredArgumentAttribute), true));
        var rule = shell.GetMethod("ExecuteRule", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(rule);
        Assert.True(rule!.IsAssembly && rule.IsAbstract);
        Assert.Equal(typeof(void), rule.ReturnType);
        Assert.Equal<Type>([typeof(ActivityInput), typeof(PluginServices)],
            rule.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        Assert.False(typeof(ActivityInput).IsVisible);
    }

    /// <summary>
    /// 不認得的非空模式必須拒絕而不能降成 Live；合成字串不應進錯誤訊息，
    /// 規則未執行及零真服務呼叫是決定性斷言。
    /// </summary>
    [Fact]
    public void ActivityShell_UnknownMode_RejectsBeforeRule()
    {
        using var scope = new ActivityScope();
        var ran = false;
        var activity = new ProbeActivity((_, _) => ran = true);

        var error = Assert.Throws<InvalidPluginExecutionException>(() => scope.Invoke(activity, "UNKNOWN_MODE_SYNTHETIC"));

        Assert.False(ran);
        Assert.Empty(scope.Service.Calls);
        Assert.DoesNotContain("UNKNOWN_MODE_SYNTHETIC", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 規則在部分寫入後業務拒絕，須比照 Plugin 轉換成含本次事件 ID 的例外；
    /// 有效資源與缺參數資源分別驗證格式化與固定後備，並保留意圖及清理。
    /// </summary>
    [Theory]
    [InlineData("Common.Diagnostics.InvalidConfiguration", "診斷外掛設定格式錯誤。")]
    [InlineData("Common.Plugin.EventId", "無法完成此操作。")]
    public void ActivityShell_BusinessFailure_ConvertsRecordsAndDisposes(string key, string expectedText)
    {
        using var scope = new ActivityScope();
        RecordingOrganizationService? writes = null;
        ShadowPushRecorder? pushes = null;
        var activity = new ProbeActivity((_, services) =>
        {
            writes = (RecordingOrganizationService)services.Organization;
            pushes = services.ShadowPush!;
            writes.Create(new Entity("task"));
            throw new BusinessRuleException(new TextKey(key));
        });

        var error = Assert.Throws<InvalidPluginExecutionException>(() => scope.Invoke(activity, "Shadow"));

        Assert.Contains(expectedText, error.Message, StringComparison.Ordinal);
        Assert.Contains(scope.Context.Object.CorrelationId.ToString("D"), error.Message, StringComparison.Ordinal);
        Assert.Null(error.InnerException);
        Assert.Empty(scope.Service.Calls);
        var lines = ShadowLines(scope.Tracing);
        Assert.Equal(2, lines.Length);
        Assert.Equal("Create", (string?)lines[0]["operation"]);
        Assert.True((bool)lines[1]["end"]!);
        Assert.Equal(1, (int)lines[1]["count"]!);
        AssertDrained(writes!, pushes!);
    }

    /// <summary>
    /// 未知規則錯誤必須保留同一例外；再注入 tracing 全面失敗，
    /// 確認 catch 的追蹤與 finally 的輸出均不能覆蓋原例外，兩種 recorder 都排空。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActivityShell_UnexpectedFailure_TraceCannotReplaceOriginalAndStateDrains(bool traceFails)
    {
        using var scope = new ActivityScope();
        if (traceFails)
            scope.TraceMock.Setup(x => x.Trace(It.IsAny<string>(), It.IsAny<object[]>()))
                .Throws(new InvalidOperationException("合成追蹤故障"));
        var expected = new InvalidOperationException("合成規則故障");
        RecordingOrganizationService? writes = null;
        ShadowPushRecorder? pushes = null;
        var activity = new ProbeActivity((input, services) =>
        {
            writes = (RecordingOrganizationService)services.Organization;
            pushes = services.ShadowPush!;
            writes.Create(new Entity("task"));
            pushes.Record(input.UserId, "synthetic.workflow", input.PrimaryEntityId, 1, new string('a', 64));
            throw expected;
        });

        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => scope.Invoke(activity, "Shadow")));

        Assert.Empty(scope.Service.Calls);
        AssertDrained(writes!, pushes!);
        if (!traceFails)
        {
            var lines = ShadowLines(scope.Tracing);
            Assert.Equal(3, lines.Length);
            Assert.Equal("Create", (string?)lines[0]["operation"]);
            Assert.Equal("Push", (string?)lines[1]["operation"]);
            Assert.True((bool)lines[2]["end"]!);
            Assert.Equal(2, (int)lines[2]["count"]!);
        }
    }

    /// <summary>
    /// 成功規則後 writer 故障仍須回報追蹤失敗，不能假稱影子資料已完成；
    /// 兩個本次擁有的 recorder 即使無法輸出也須處置，SDK 則維持可借用。
    /// </summary>
    [Fact]
    public void ActivityShell_TraceFailureAfterSuccess_ThrowsAndDisposes()
    {
        using var scope = new ActivityScope();
        var expected = new InvalidOperationException("合成影子輸出故障");
        scope.TraceMock.Setup(x => x.Trace(It.IsAny<string>(), It.IsAny<object[]>())).Throws(expected);
        RecordingOrganizationService? writes = null;
        ShadowPushRecorder? pushes = null;
        var activity = new ProbeActivity((_, services) =>
        {
            writes = (RecordingOrganizationService)services.Organization;
            pushes = services.ShadowPush!;
            writes.Create(new Entity("task"));
        });

        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => scope.Invoke(activity, "Shadow")));

        AssertDrained(writes!, pushes!);
        Assert.Empty(scope.Service.Calls);
        Assert.NotEqual(Guid.Empty, scope.Service.Create(new Entity("task")));
    }

    /// <summary>
    /// 同一活動以全新 invoker 依序執行 A／B／A，確保輸入識別、借用使用者、
    /// recorder 與追蹤只屬當次。若沿用前次服務或 scope，識別與零混入斷言會失敗。
    /// </summary>
    [Fact]
    public void ActivityShell_SameActivityDifferentScopes_NoStateShared()
    {
        using var a = new ActivityScope();
        using var b = new ActivityScope();
        var inputs = new List<(Guid Organization, Guid User, Guid Correlation, Guid Record)>();
        var recorders = new List<(RecordingOrganizationService Writes, ShadowPushRecorder Pushes)>();
        var activity = new ProbeActivity((input, services) =>
        {
            inputs.Add((input.OrganizationId, input.UserId, input.CorrelationId, input.PrimaryEntityId));
            var writes = (RecordingOrganizationService)services.Organization;
            var pushes = services.ShadowPush!;
            Assert.Empty(writes.Writes);
            Assert.Empty(pushes.Records);
            recorders.Add((writes, pushes));
            writes.Create(new Entity("task", input.PrimaryEntityId));
            pushes.Record(input.UserId, "synthetic.workflow", input.PrimaryEntityId, 1, new string('a', 64));
        });

        a.Invoke(activity, "Shadow");
        b.Invoke(activity, "Shadow");
        a.Invoke(activity, "Shadow");

        Assert.Equal(new[]
        {
            (a.Context.Object.OrganizationId, a.Context.Object.UserId, a.Context.Object.CorrelationId, a.Context.Object.PrimaryEntityId),
            (b.Context.Object.OrganizationId, b.Context.Object.UserId, b.Context.Object.CorrelationId, b.Context.Object.PrimaryEntityId),
            (a.Context.Object.OrganizationId, a.Context.Object.UserId, a.Context.Object.CorrelationId, a.Context.Object.PrimaryEntityId)
        }, inputs.ToArray());
        Assert.Equal(6, ShadowLines(a.Tracing).Length);
        Assert.Equal(3, ShadowLines(b.Tracing).Length);
        Assert.DoesNotContain(a.Tracing.Entries, line => line.Contains(b.Context.Object.UserId.ToString("D"), StringComparison.Ordinal));
        Assert.DoesNotContain(b.Tracing.Entries, line => line.Contains(a.Context.Object.UserId.ToString("D"), StringComparison.Ordinal));
        Assert.DoesNotContain(a.Tracing.Entries, line => line.Contains(b.Context.Object.PrimaryEntityId.ToString("D"), StringComparison.Ordinal));
        Assert.DoesNotContain(b.Tracing.Entries, line => line.Contains(a.Context.Object.PrimaryEntityId.ToString("D"), StringComparison.Ordinal));
        Assert.Empty(a.Service.Calls);
        Assert.Empty(b.Service.Calls);
        Assert.Equal(3, recorders.Count);
        for (var i = 0; i < recorders.Count; i++)
        {
            AssertDrained(recorders[i].Writes, recorders[i].Pushes);
            for (var j = 0; j < i; j++)
            {
                Assert.NotSame(recorders[j].Writes, recorders[i].Writes);
                Assert.NotSame(recorders[j].Pushes, recorders[i].Pushes);
            }
        }
    }

    /// <summary>只解析有版本前綴的安全影子投影，忽略例外型別診斷，避免混淆兩種輸出。</summary>
    private static JObject[] ShadowLines(FakeTracingService tracing) =>
        tracing.Entries.Where(entry => entry.StartsWith("SMV-SHADOW|1|", StringComparison.Ordinal))
            .Select(entry => JObject.Parse(entry.Substring("SMV-SHADOW|1|".Length))).ToArray();

    /// <summary>在 owner 收尾後驗證摘要及計數回到零，且兩種 recorder 都明確拒絕再寫入。</summary>
    private static void AssertDrained(RecordingOrganizationService writes, ShadowPushRecorder pushes)
    {
        Assert.Empty(writes.Writes);
        Assert.Empty(pushes.Records);
        Assert.Equal(0L, writes.TotalCount);
        Assert.Equal(0L, pushes.TotalCount);
        Assert.Throws<ObjectDisposedException>(() => writes.Create(new Entity("task")));
        Assert.Throws<ObjectDisposedException>(() =>
            pushes.Record(Guid.NewGuid(), "synthetic.workflow", Guid.NewGuid(), 1, new string('a', 64)));
    }

    /// <summary>
    /// 單一合成 scope 的 owner；只在同步 Invoke 期間借出平台服務。
    /// 不跨案例共用 extension，Dispose 在全部呼叫排空後清除服務、追蹤與捕捉回呼。
    /// </summary>
    private sealed class ActivityScope : IDisposable
    {
        /// <summary>建立獨立 scope；factory 只接受本次使用者 ID，錯誤使用者會立即失敗。</summary>
        internal ActivityScope()
        {
            Factory.Setup(x => x.CreateOrganizationService(Context.Object.UserId)).Returns(Service);
            TraceMock.Setup(x => x.Trace(It.IsAny<string>(), It.IsAny<object[]>()))
                .Callback<string, object[]>((format, arguments) => Tracing.Trace(format, arguments));
        }

        /// <summary>本 scope 的合成身分與集合，由收尾重設 Mock 解除參照。</summary>
        internal FakeWorkflowContext Context { get; } = new();
        /// <summary>由本 fixture 唯一擁有並處置的記憶體 SDK，不轉移給外殼。</summary>
        internal FakeOrganizationService Service { get; } = new();
        /// <summary>只保留本 scope 的安全追蹤，Dispose 清空。</summary>
        internal FakeTracingService Tracing { get; } = new();
        /// <summary>故障注入只替代平台 tracing，不替代產品 writer。</summary>
        internal Mock<ITracingService> TraceMock { get; } = new(MockBehavior.Strict);
        /// <summary>受使用者識別約束的合成 factory，不建立真正連線。</summary>
        internal Mock<IOrganizationServiceFactory> Factory { get; } = new(MockBehavior.Strict);

        /// <summary>
        /// 每次建立新 invoker 與 extension，省略 null 模式鍵以驗證無輸入的預設值；
        /// 同步返回後不保存執行 context 或 invoker，也不留下需要背景排空的工作。
        /// </summary>
        internal void Invoke(ActivityShellBase activity, string? mode)
        {
            var invoker = new WorkflowInvoker(activity);
            invoker.Extensions.Add<IWorkflowContext>(() => Context.Object);
            invoker.Extensions.Add<IOrganizationServiceFactory>(() => Factory.Object);
            invoker.Extensions.Add<ITracingService>(() => TraceMock.Object);
            var inputs = new Dictionary<string, object>(StringComparer.Ordinal);
            if (mode is not null) inputs.Add("ExecutionMode", mode);
            invoker.Invoke(inputs, TimeSpan.FromSeconds(10));
        }

        /// <summary>同步執行結束後清除所有合成資料與回呼，不處理不存在的網路或取消資源。</summary>
        public void Dispose()
        {
            Service.Dispose();
            Tracing.Entries.Clear();
            Factory.Reset();
            TraceMock.Reset();
            Context.Mock.Reset();
        }
    }

    /// <summary>
    /// 合成規則 probe；唯讀委派僅由本案例擁有，用來觀察真正外殼交付的輸入與服務。
    /// 不新增業務參數、不保存平台 context；舊活動沒有 Input／Output，
    /// 正式活動加入後也應只有基礎 ExecutionMode，不能把 probe 當成實機驗收。
    /// </summary>
    private sealed class ProbeActivity : ActivityShellBase
    {
        /// <summary>案例區域合成規則，不保存於任何產品快取或 static。</summary>
        private readonly Action<ActivityInput, PluginServices> action;

        /// <summary>借用本案例同步委派，生命週期不超過測試方法。</summary>
        internal ProbeActivity(Action<ActivityInput, PluginServices> action) => this.action = action;

        /// <summary>在外殼建立本次 scope 後同步執行；故障由產品外殼處理與收尾。</summary>
        internal override void ExecuteRule(ActivityInput input, PluginServices services) => action(input, services);
    }
}

/*
 * 檔案整體說明
 *
 * 目的與定位：P5-08 真預約簽核 Plugin／CodeActivity 的外殼與部分信任離線驗證，
 * 保護嵌入 reading 黃金情境、深度、錯誤傳遞、正式收件者、重試與 A/B/A 隔離。
 * 主要功能與流程：一般測試執行真 WorkflowInvoker／Plugin，CAS 子域則使用相同
 * 無動態代理 fixture，檢查產品組件確實受限。完整期望不足時保留紅燈，不以
 * 產品結果、文字片段或通知計數冒充完整訊息相等；Live 活動只測查詢前核定拒絕。
 * 資源與隔離：每案 using owner 清空來源、SDK 回呼與追蹤；同一外殼 A/B/A 使用
 * 不同組織、教會、使用者、記錄及收件者。父域 finally 卸載每個一次性 CAS 域，
 * 只授 Execution 及輸出目錄唯讀，Xrm SDK 是唯一完整信任例外。
 * 本測試不執行 D365、HTTP、真 token、舊程式或背景工作；十秒 Invoke 只約束
 * WF 等待，實際同步工作量另受 fixture 讀取上限限制，不是實機或 soak 證據。
 */
extern alias Workflows;

using System;
using System.Activities;
using System.Linq;
using System.Security;
using System.Security.Permissions;
using System.Security.Policy;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Workflow;
using Newtonsoft.Json.Linq;
using SpeechMessage.Dynamics.Plugins.Appointments;
using SpeechMessageProducts.GoodShepherd.Core.Appointments;
using Xunit;
using SigningActivity = Workflows::SpeechMessage.Dynamics.Workflows.Appointments.AppointmentSigningActivity;

namespace SpeechMessage.Dynamics.Plugins.Tests;

/// <summary>真外殼的有限必要回歸；錯誤期望與安全拒絕均須提供決定性證據。</summary>
public sealed class AppointmentSigningShellTests
{
    /// <summary>嵌入三至六案全部執行，完整意圖／訊息／活動輸出吻合才成功；缺欄明確紅燈。</summary>
    [Fact]
    public void Appointments_ShadowMode_MatchesExpected() => AppointmentSigningGoldenFixture.RunAll();

    /// <summary>兩種 Plugin 訊息深度一執行、二返回；活動沒有憑空添加 Plugin 的深度政策。</summary>
    [Theory]
    [InlineData("Create")]
    [InlineData("Update")]
    public void Appointments_DepthPolicy_MatchesLegacy(string message)
    {
        using var fixture = Normal();
        fixture.Context.MessageName = message;
        fixture.Context.Depth = 1;
        fixture.RunPlugin();
        Assert.Empty(fixture.Operations());
        Assert.Equal(1, fixture.ServiceLeases);
        Assert.Equal(0, fixture.Reads);
        fixture.Context.Depth = 2;
        fixture.RunPlugin();
        Assert.Equal(0, fixture.Reads);
        Assert.Equal(0, fixture.ServiceLeases);
        Assert.Empty(fixture.Trace.Entries);
        fixture.Context.MessageName = "ExecuteWorkflow";
        fixture.RunActivity(parameter: "工作流程");
        Assert.Single(fixture.Operations());
        Assert.True(fixture.Reads > 0);
    }

    /// <summary>活動第一個 SDK 讀取注入真錯誤，保留同一例外而不吞錯，finally 仍完成零意圖收尾。</summary>
    [Fact]
    public void Appointments_UnexpectedError_Rethrown()
    {
        using var fixture = Normal();
        var injected = new InvalidOperationException("Synthetic appointment read failure.");
        fixture.ReadFailure = injected;
        var failure = Assert.ThrowsAny<Exception>(() => Run(fixture, true));
        Assert.Same(injected, failure.GetBaseException());
        Assert.Empty(fixture.Operations());
        Assert.Contains(fixture.Trace.Entries, x => x == "ExceptionType=" + injected.GetType().FullName);
    }

    /// <summary>申請人 A／簽核者 B 的正式教會不同，活動不推播且不得讀頻道，不以顯示名稱當授權。</summary>
    [Fact]
    public void Appointments_RecipientOtherChurch_NoPush()
    {
        using var fixture = Normal();
        fixture.Rows["signer"]["parentcustomerid"] = new EntityReference("account", AppointmentSigningGoldenFixture.Id("B", "church-a"));
        Run(fixture, true);
        Assert.Empty(fixture.Operations());
        Assert.InRange(fixture.Reads, 2, 5);
        Assert.DoesNotContain("account", fixture.ReadTables);
        Assert.DoesNotContain("new_linechannel", fixture.ReadTables);
        Assert.False(fixture.Service.Calls.ContainsKey(nameof(IOrganizationService.RetrieveMultiple)));
    }

    /// <summary>簽核者無 LINE 綁定時不回落開發者帳號，不查頻道、不拋例外且影子完整結束。</summary>
    [Fact]
    public void Appointments_NotBound_NoPushNoThrow()
    {
        using var fixture = Normal();
        fixture.Rows["signer"].Attributes.Remove("new_lineid");
        Run(fixture, true);
        Assert.Empty(fixture.Operations());
        Assert.InRange(fixture.Reads, 2, 5);
        Assert.DoesNotContain("account", fixture.ReadTables);
        Assert.DoesNotContain("new_linechannel", fixture.ReadTables);
        Assert.False(fixture.Service.Calls.ContainsKey(nameof(IOrganizationService.RetrieveMultiple)));
    }

    /// <summary>同一來源重跑重試鍵相同；下一簽核者正式 ID 變更必須有不同事件鍵，不宣稱已去重送達。</summary>
    [Fact]
    public void Appointments_RetryKey_IsStableForSameEvent()
    {
        using var fixture = Normal();
        Run(fixture, true);
        var first = Assert.Single(fixture.Operations());
        fixture.Context.CorrelationId = Guid.NewGuid();
        Run(fixture, true);
        var second = Assert.Single(fixture.Operations());
        Assert.Equal((string?)first["retryKey"], (string?)second["retryKey"]);
        Assert.True(Guid.TryParse((string?)first["retryKey"], out var key) && key != Guid.Empty);
        var signer = fixture.Rows["signer"];
        var replacement = new Entity("contact", AppointmentSigningGoldenFixture.Id("A", "next-signer"));
        foreach (var attribute in signer.Attributes) replacement[attribute.Key] = attribute.Value;
        fixture.Rows["signer"] = replacement;
        fixture.Rows["record"]["new_signing_contact"] = replacement.ToEntityReference();
        Run(fixture, true);
        var third = Assert.Single(fixture.Operations());
        Assert.NotEqual((string?)first["retryKey"], (string?)third["retryKey"]);
        Assert.Equal(replacement.Id.ToString("D"), (string?)third["contactId"]);
    }

    /// <summary>同一產品實例 A/B/A，各組織／使用者／正式身分只發布自己的意圖，最後 A 與起始 A 完全相同。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Appointments_ShellReuse_ABA_Isolated(bool workflow)
    {
        using var a = Normal("A");
        using var b = Normal("B");
        var plugin = new AppointmentPlugin("{\"mode\":\"Shadow\"}", "");
        var activity = new SigningActivity();
        JObject? first = null;
        foreach (var fixture in new[] { a, b, a })
        {
            if (workflow) { fixture.Context.MessageName = "ExecuteWorkflow"; fixture.RunActivity(activity, parameter: "工作流程"); }
            else { fixture.Context.MessageName = "Create"; fixture.RunPlugin(plugin); }
            var operations = fixture.Operations();
            if (!workflow)
            {
                Assert.Empty(operations);
                Assert.Equal(0, fixture.Reads);
                Assert.Equal(1, fixture.ServiceLeases);
                continue;
            }
            var intent = Assert.Single(operations);
            Assert.Equal(fixture.Identity("signer").ToString("D"), (string?)intent["contactId"]);
            if (first is null) first = (JObject)intent.DeepClone();
            else if (ReferenceEquals(fixture, a)) Assert.True(JToken.DeepEquals(first, intent), "A/B/A changed the first scope.");
            else Assert.NotEqual((string?)first["retryKey"], (string?)intent["retryKey"]);
        }
    }

    /// <summary>所有讀取白名單都拒絕 token 欄位／AllColumns；故意放入合成毒值不可進追蹤或影響 Shadow。</summary>
    [Fact]
    public void Appointment_TokenNeverReadFromRecordField()
    {
        using var fixture = Normal();
        fixture.Rows["record"]["new_channel_access_token"] = "SYNTHETIC_FORBIDDEN_RECORD_TOKEN";
        foreach (var workflow in new[] { false, true })
        {
            Run(fixture, workflow);
            if (workflow) { Assert.Single(fixture.Operations()); Assert.True(fixture.Reads > 0); }
            else { Assert.Empty(fixture.Operations()); Assert.Equal(0, fixture.Reads); }
            Assert.DoesNotContain(fixture.Trace.Entries, x => x.Contains("SYNTHETIC_FORBIDDEN_RECORD_TOKEN"));
        }
    }

    /// <summary>未知活動參數不執行舊 MajorExecute 分派；沒有查詢、通知、真寫入或部分結果。</summary>
    [Fact]
    public void Appointments_ActivityUnknownParameter_NoPush()
    {
        using var fixture = Normal();
        fixture.RunActivity(parameter: "unknown");
        Assert.Empty(fixture.Operations());
        Assert.Equal(0, fixture.Reads);
    }

    /// <summary>Live 明示／省略／空白均在任何 SDK 讀寫前拒絕；正常取消或業務拒絕不代替固定缺口例外。</summary>
    [Theory]
    [InlineData("Live")]
    [InlineData(null)]
    [InlineData("")]
    public void Appointments_ActivityLive_RejectsBeforeAnyQuery(string? mode)
    {
        using var fixture = Normal();
        var failure = Assert.ThrowsAny<Exception>(() => fixture.RunActivity(mode: mode));
        var actual = Assert.IsType<InvalidPluginExecutionException>(failure.GetBaseException());
        Assert.Contains("P5-PROP-006", actual.Message, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Reads);
        Assert.Empty(fixture.Service.Calls);
        Assert.Contains(fixture.Trace.Entries, x => x == "ExceptionType=" + actual.GetType().FullName);
        fixture.AssertNoWrites();
    }

    /// <summary>活動來源參照只做相容性驗證，錯 ID 查詢前拒絕，省略則使用平台正式 ID。</summary>
    [Fact]
    public void Appointments_ActivitySource_UsesPlatformIdentity()
    {
        using var fixture = Normal();
        fixture.Context.MessageName = "ExecuteWorkflow";
        Assert.ThrowsAny<Exception>(() => fixture.RunActivity(target: new EntityReference("appointment", Guid.NewGuid())));
        Assert.Equal(0, fixture.Reads);
        Assert.Empty(fixture.Operations());
        fixture.RunActivity(omitTarget: true, parameter: "工作流程");
        Assert.Single(fixture.Operations());
    }

    /// <summary>摘要載入在授權後注入錯誤，保留真例外且完整零通知，不能以第一筆或前次統計發布。</summary>
    [Fact]
    public void Appointments_LeaveSummaryFailure_NoPartialPush()
    {
        using var fixture = new AppointmentSigningGoldenFixture(
            (JObject)AppointmentSigningGoldenFixture.Read("scenarios")["cases"]![0]!);
        fixture.Context.MessageName = "ExecuteWorkflow";
        var injected = new InvalidOperationException("Synthetic appointment summary failure.");
        fixture.QueryFailure = injected;
        var failure = Assert.ThrowsAny<Exception>(() => fixture.RunActivity(parameter: "工作流程"));
        Assert.Same(injected, failure.GetBaseException());
        Assert.Empty(fixture.Operations());
        Assert.DoesNotContain("new_linechannel", fixture.ReadTables);
    }

    /// <summary>舊參數與輸出型別及可省略的 appointment lookup 不得被新模式破壞。</summary>
    [Fact]
    public void Appointments_ActivityMetadata_MatchesLegacy()
    {
        Assert.Equal(typeof(InArgument<string>), typeof(SigningActivity).GetProperty("InputParameter")!.PropertyType);
        Assert.Equal(typeof(OutArgument<int>), typeof(SigningActivity).GetProperty("OutputResult")!.PropertyType);
        var source = typeof(SigningActivity).GetProperty("InputAppointment")!;
        Assert.Equal(typeof(InArgument<EntityReference>), source.PropertyType);
        Assert.Empty(source.GetCustomAttributes(typeof(RequiredArgumentAttribute), false));
        Assert.Equal("appointment", Assert.Single(source.GetCustomAttributes(typeof(ReferenceTargetAttribute), false)
            .Cast<ReferenceTargetAttribute>()).EntityName);
    }

    /// <summary>真 Plugin／活動在受限域讀同份三至六案；活動完整黃金結果與 Plugin 空參數均驗證，缺口保留紅燈。</summary>
    [Theory]
    [InlineData("plugin")]
    [InlineData("activity")]
    public void PartialTrust_AppointmentShadowChain_Runs(string entrypoint)
    {
        var root = AppDomain.CurrentDomain.BaseDirectory;
        var permissions = new PermissionSet(PermissionState.None);
        permissions.AddPermission(new SecurityPermission(SecurityPermissionFlag.Execution));
        permissions.AddPermission(new FileIOPermission(FileIOPermissionAccess.Read | FileIOPermissionAccess.PathDiscovery, root));
        var sdk = typeof(IPlugin).Assembly.Evidence.GetHostEvidence<StrongName>();
        Assert.NotNull(sdk);
        var domain = AppDomain.CreateDomain("P5-Appointments-" + Guid.NewGuid().ToString("N"), null,
            new AppDomainSetup { ApplicationBase = root, ConfigurationFile = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile },
            permissions, sdk);
        try
        {
            var probe = (AppointmentSigningPartialTrustProbe)domain.CreateInstanceAndUnwrap(
                typeof(AppointmentSigningPartialTrustProbe).Assembly.FullName, typeof(AppointmentSigningPartialTrustProbe).FullName);
            var result = probe.Run(entrypoint);
            Assert.True(string.Equals("PASS", result, StringComparison.Ordinal), result);
        }
        finally { AppDomain.Unload(domain); }
    }

    /// <summary>取 reading 第二個場地種子，避免拒絕案例先依賴請假明細查詢；不複製產品答案。</summary>
    private static AppointmentSigningGoldenFixture Normal(string scope = "A") =>
        new((JObject)AppointmentSigningGoldenFixture.Read("scenarios")["cases"]![1]!, scope);

    /// <summary>僅選真入口，不改通知分派邏輯；活動參數仍由本次 Invoke 求值。</summary>
    private static void Run(AppointmentSigningGoldenFixture fixture, bool workflow)
    {
        fixture.Context.MessageName = workflow ? "ExecuteWorkflow" : "Create";
        if (workflow) fixture.RunActivity(parameter: "工作流程");
        else fixture.RunPlugin();
    }
}

/// <summary>一次性 CAS 跨域入口，只回合成結果，不把 SDK／context 或資料列傳回父域。</summary>
public sealed class AppointmentSigningPartialTrustProbe : MarshalByRefObject
{
    /// <summary>即使驗證失敗也不可重用探針；父域接著確定卸載整域。</summary>
    private bool ran;

    /// <summary>驗產品、測試及網域都受限後執行真外殼全部情境；不使用 xUnit 或動態代理。</summary>
    /// <param name="entrypoint">只允許 plugin／activity，由父測試固定指定。</param>
    /// <returns>全部完整契約成功才 PASS；否則回失敗診斷，資料均為合成且沒有秘密。</returns>
    public string Run(string entrypoint)
    {
        try
        {
            AppointmentSigningGoldenFixture.Require(!ran && !AppDomain.CurrentDomain.IsFullyTrusted &&
                !GetType().Assembly.IsFullyTrusted && !typeof(AppointmentPlugin).Assembly.IsFullyTrusted &&
                !typeof(SigningActivity).Assembly.IsFullyTrusted && !typeof(AppointmentSigningRule).Assembly.IsFullyTrusted,
                "restricted one-shot product domain");
            ran = true;
            AppointmentSigningGoldenFixture.RunAll(entrypoint);
            return "PASS";
        }
        catch (Exception failure) { return failure.ToString(); }
    }
}

/*
 * 檔案整體說明
 * 目的與定位：P5-09 三個正式 QR 活動外殼的離線契約測試，與 Core 黃金案例分工。
 * 主要流程：經真 WorkflowInvoker、合成 SDK 與平台 context 驗證完整 Shadow、深度、
 * 原例外及輸入輸出中繼資料；不執行舊 DLL，不連組織、不推播或讀取秘密。
 * 使用邊界與維護：每案擁有服務及追蹤並於同步工作排空後 Dispose；重用活動 A/B/A
 * 時每輪建立獨立來源。斷言涵蓋零真寫入、有界完整投影及清理基線，不代表實機驗收。
 */
extern alias Workflows;
using System;
using System.Activities;
using System.Activities.Statements;
using Microsoft.Xrm.Sdk.Workflow;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;
using LessonActivity = Workflows::SpeechMessage.Dynamics.Workflows.Lessons.LessonQrCodeActivity;
namespace SpeechMessage.Dynamics.Plugins.Tests;
/// <summary>保護正式 QR 活動的三個入口存在，不把純 Core 測試當成外殼測試。</summary>
public sealed class QrCodeShellTests
{
    /// <summary>由 xUnit 擁有的當案輸出管道，只輸出合成追蹤長度，不保留來源資料。</summary>
    private readonly ITestOutputHelper output;
    /// <summary>借用測試輸出 owner；測試類別生命週期結束即不再使用。</summary>
    public QrCodeShellTests(ITestOutputHelper output) => this.output = output;

    /// <summary>先用公開型別名稱驗入口，尚未實作外殼必須出現決定性紅燈。</summary>
    [Theory]
    [InlineData("MeetingQrCodeActivity")]
    [InlineData("PersonalQrCodeActivity")]
    [InlineData("MeetingScanCountActivity")]
    public void QrCodes_Entrypoints_Exist(string entrypoint) =>
        Assert.NotNull(typeof(LessonActivity).Assembly.GetType("SpeechMessage.Dynamics.Workflows.QrCodes." + entrypoint));

    /// <summary>真活動與合成 SDK 比較完整圖片待辦／三十八欄計數，記錄實測長度並驗零真寫入。</summary>
    [Theory]
    [InlineData("meeting")]
    [InlineData("personal")]
    [InlineData("scan")]
    public void QrCodes_ShadowMode_MatchesExpected(string entrypoint)
    {
        using var fixture = new QrCodeCasFixture(entrypoint);
        var result = fixture.Invoke(QrCodeCasFixture.CreateActivity(entrypoint), "not-a-record-id-or-authority");
        Assert.Equal(0, Assert.IsType<int>(result["OutputResult"]));
        fixture.Verify();
        output.WriteLine("{0}: complete trace {1} UTF-16 characters including CRLF; zero real writes.",
            entrypoint, fixture.TraceCharacters);
    }

    /// <summary>舊兩個 depth 分支均執行；三訊息在 1、2、3、99 深度都不得提前略過。</summary>
    [Theory]
    [InlineData("meeting")]
    [InlineData("personal")]
    [InlineData("scan")]
    public void QrCodes_DepthPolicy_MatchesLegacy(string entrypoint)
    {
        var activity = QrCodeCasFixture.CreateActivity(entrypoint);
        foreach (var depth in new[] { 1, 2, 3, 99 })
        foreach (var message in new[] { "ExecuteWorkflow", "Create", "Update" })
        {
            using var fixture = new QrCodeCasFixture(entrypoint, depth, message);
            fixture.Invoke(activity, "");
            fixture.Verify();
        }
    }

    /// <summary>讀取注入原例外，必須原實例傳出真 WF，同時 end=0 且沒有真服務寫入。</summary>
    [Theory]
    [InlineData("meeting")]
    [InlineData("personal")]
    [InlineData("scan")]
    public void QrCodes_UnexpectedError_Rethrown(string entrypoint)
    {
        using var fixture = new QrCodeCasFixture(entrypoint);
        var expected = new InvalidOperationException("Synthetic QR read failure.");
        fixture.ReadFailure = expected;
        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() =>
            fixture.Invoke(QrCodeCasFixture.CreateActivity(entrypoint))));
        Assert.Contains(fixture.Trace.Entries, line => line.Contains("ExceptionType=System.InvalidOperationException"));
        Assert.Contains(fixture.Trace.Entries, line => line.Contains("\"end\":true,\"count\":0"));
        Assert.DoesNotContain(fixture.Trace.Entries, line => line.Contains(expected.Message));
    }

    /// <summary>錯訊息、大小寫或錯表全部零讀取零意圖；不把服務借用誤認成業務查詢。</summary>
    [Theory]
    [InlineData("meeting")]
    [InlineData("personal")]
    [InlineData("scan")]
    public void QrCodes_UnsupportedTrigger_NoIo(string entrypoint)
    {
        var activity = QrCodeCasFixture.CreateActivity(entrypoint);
        foreach (var message in new[] { "Delete", "Assign", "update", "executeworkflow" })
        {
            using var fixture = new QrCodeCasFixture(entrypoint, message: message);
            fixture.Invoke(activity);
            fixture.Verify(noOperation: true);
        }
        using var wrongTable = new QrCodeCasFixture(entrypoint, table: "account");
        wrongTable.Invoke(activity);
        wrongTable.Verify(noOperation: true);
    }

    /// <summary>同一真活動重用 A/B/A，來源、教會、使用者及組織每次隔離，不能沿用 B 的快取。</summary>
    [Theory]
    [InlineData("meeting")]
    [InlineData("personal")]
    [InlineData("scan")]
    public void QrCodes_ReusedActivity_ABA_Isolated(string entrypoint)
    {
        var activity = QrCodeCasFixture.CreateActivity(entrypoint);
        using var a = new QrCodeCasFixture(entrypoint);
        using var b = new QrCodeCasFixture(entrypoint, depth: 7, message: "Update");
        a.Invoke(activity, "caller-A");
        var first = a.Verify();
        b.Invoke(activity, "caller-B");
        var second = b.Verify();
        Assert.NotEqual(a.RecordId, b.RecordId);
        Assert.NotEqual(a.ChurchId, b.ChurchId);
        Assert.False(JToken.DeepEquals(first, second), "Different authoritative source must change complete intent.");
        a.Trace.Entries.Clear();
        a.ResetInvocationLease();
        a.Invoke(activity, "caller-A-after-B");
        Assert.True(JToken.DeepEquals(first, a.Verify()), "A intent must remain identical after B.");
    }

    /// <summary>必要字串及整數輸出 metadata 不變；WF 未綁必要參數先拒絕，正常輸出只有預設值。</summary>
    [Theory]
    [InlineData("meeting")]
    [InlineData("personal")]
    [InlineData("scan")]
    public void QrCodes_ParameterMetadata_PreservedAndOutputUnassigned(string entrypoint)
    {
        var activity = QrCodeCasFixture.CreateActivity(entrypoint);
        var type = activity.GetType();
        var input = type.GetProperty("InputParameter")!;
        Assert.Equal(typeof(InArgument<string>), input.PropertyType);
        Assert.Single(input.GetCustomAttributes(typeof(RequiredArgumentAttribute), false));
        Assert.Equal("InputParameter", Assert.IsType<InputAttribute>(
            Assert.Single(input.GetCustomAttributes(typeof(InputAttribute), false))).Name);
        var result = type.GetProperty("OutputResult")!;
        Assert.Equal(typeof(OutArgument<int>), result.PropertyType);
        Assert.Equal("OutputResult", Assert.IsType<OutputAttribute>(
            Assert.Single(result.GetCustomAttributes(typeof(OutputAttribute), false))).Name);
        using var fixture = new QrCodeCasFixture(entrypoint);
        var error = Assert.Throws<ArgumentException>(() => new WorkflowInvoker(activity).Invoke());
        Assert.Contains("InputParameter", error.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Sdk.Calls);
        // 公開 metadata 反射只用於父域；實際子域執行不需要反射或 Moq。
        Assert.Equal(0, fixture.Invoke(activity)["OutputResult"]);
        fixture.Verify();
    }

    /// <summary>預先綁定非零輸出位置，執行後必須保持原值；可抓出錯誤地 Set(0) 的行為。</summary>
    [Theory]
    [InlineData("meeting")]
    [InlineData("personal")]
    [InlineData("scan")]
    public void QrCodes_OutputLocation_IsNeverAssigned(string entrypoint)
    {
        var activity = QrCodeCasFixture.CreateActivity(entrypoint);
        var sentinel = new Variable<int>("qr-output-sentinel", 739);
        activity.GetType().GetProperty("InputParameter")!.SetValue(activity, new InArgument<string>("unused"));
        activity.GetType().GetProperty("ExecutionMode")!.SetValue(activity, new InArgument<string>("Shadow"));
        activity.GetType().GetProperty("OutputResult")!.SetValue(activity, new OutArgument<int>(sentinel));
        var observer = new OutputObserver { Value = new InArgument<int>(sentinel) };
        var sequence = new Sequence { Variables = { sentinel }, Activities = { activity, observer } };
        using var fixture = new QrCodeCasFixture(entrypoint);
        fixture.Invoke(sequence, bindInputs: false);
        Assert.Equal(739, observer.Observed);
        fixture.Verify();
    }

    /// <summary>反覆更換來源並同步排空，二十四輪成功與讀取故障後 Dispose 都須回零基線。</summary>
    [Theory]
    [InlineData("meeting")]
    [InlineData("personal")]
    [InlineData("scan")]
    public void QrCodes_RepeatedInvocation_DrainsOwnedResources(string entrypoint)
    {
        var activity = QrCodeCasFixture.CreateActivity(entrypoint);
        for (var iteration = 0; iteration < 24; iteration++)
        {
            using var fixture = new QrCodeCasFixture(entrypoint);
            if (iteration % 2 == 0)
            {
                fixture.Invoke(activity);
                fixture.Verify();
            }
            else
            {
                var expected = new InvalidOperationException("Synthetic alternating read failure.");
                fixture.ReadFailure = expected;
                Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => fixture.Invoke(activity)));
            }
            fixture.Dispose();
            Assert.Empty(fixture.Sdk.Calls);
            Assert.Empty(fixture.Trace.Entries);
            Assert.Null(fixture.ReadFailure);
            Assert.Equal(0, fixture.Sdk.EntityCount);
        }
    }

    /// <summary>無出席與跨教會都必須在待辦前拒絕，真 WF 仍收尾 end=0 而非猜預設教會。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QrCodes_MeetingSource_FailsClosed(bool mixedChurch)
    {
        using var fixture = new QrCodeCasFixture("meeting");
        fixture.NoAttendance = !mixedChurch;
        fixture.MixedChurch = mixedChurch;
        Assert.Throws<InvalidOperationException>(() => fixture.Invoke(QrCodeCasFixture.CreateActivity("meeting")));
        Assert.Contains(fixture.Trace.Entries, line => line.Contains("\"end\":true,\"count\":0"));
        Assert.DoesNotContain(fixture.Trace.Entries, line => line.Contains("\"operation\":"));
    }

    /// <summary>當案 WF 觀察者，只保存單一整數至測試結束，不保存 context、SDK 或其他活動資料。</summary>
    private sealed class OutputObserver : CodeActivity
    {
        /// <summary>綁定當案 WF 變數，不保存先前活動的已評估輸出。</summary>
        public InArgument<int> Value { get; set; } = new();
        /// <summary>只供父測試斷言的合成整數，不跨測試共用。</summary>
        internal int Observed { get; private set; }
        /// <summary>讀取正式活動返回後的變數，觀察者不改寫該位置。</summary>
        protected override void Execute(CodeActivityContext context) => Observed = Value.Get(context);
    }
}

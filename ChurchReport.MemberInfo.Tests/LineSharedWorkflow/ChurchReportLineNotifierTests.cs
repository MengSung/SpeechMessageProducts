/*
 * 檔案整體說明
 *
 * 目的與定位：保護 P6-03 產品通知器替代舊送訊殼之後的 legacy 相容契約。
 * 主要功能與流程：假 workflow 核對來源、內容與 RetryKey；真實 SDK 配合有限 HTTP
 * handler 驗證兩頻道各一百次交錯不混用、沒有未完成工作。多播經真實 ToolUtility
 * 加合成 CRM 記錄統計先於唯一 HTTP，另注入 CRM／傳輸故障證明先診斷再傳遞原例外。
 * 使用邊界與維護：不讀秘密、不連 LINE 或 D365。案例只借用自己 using 擁有的 SDK、
 * HTTP、組態與工具；全域診斷以既有非平行 collection 隔離，先解除再排空並刪暫存目錄。
 * 捕捉的訊息與實體均為當案合成資料，handler 不保留 request／response 或使用者憑證；
 * 每案最後檢查作業已完成，釋放順序仍是排空、SDK、HTTP 與測試替身。
 */
using System.Net;
using System.Reflection;
using ChurchReport.Services;
using Line.Messaging;
using LineMessagingProcessor;
using LineMessagingProcessor.Workflows;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.LineSharedWorkflow;

/// <summary>以實際通知器驗證成功、故障、統計及頻道隔離；所有外部 I/O 均以案例區域替身取代。</summary>
[Collection("ExceptionReporting")]
public sealed class ChurchReportLineNotifierTests
{
    /// <summary>文字來源與內文逐字保留；改錯來源或把文字換成另一種內容時必須失敗。</summary>
    [Fact]
    public async Task Notifier_TextBestEffort_SendsUserTextWithLegacySourceTag()
    {
        var workflow = new CapturingWorkflow();
        await new ChurchReportLineNotifier(workflow).SendTextBestEffortAsync("Uone", "原樣文字");
        var request = Assert.Single(workflow.Requests);
        Assert.Equal("Uone", request.Recipient.PrimaryId);
        Assert.Equal(LineNotificationRecipientKind.User, request.Recipient.Kind);
        Assert.Equal("原樣文字", request.Content.Text);
        Assert.Equal("ChurchReport.PushUtility", request.Metadata["source"]);
        Assert.Null(request.RetryKey);
        Assert.Equal(0, workflow.RequiredCalls);
    }

    /// <summary>SDK 集合借用同一操作資料且保留順序；不得改走必要通知或換來源代號。</summary>
    [Fact]
    public async Task Notifier_MessagesBestEffort_SendsSdkMessagesWithLegacySourceTag()
    {
        var workflow = new CapturingWorkflow();
        var messages = new List<ISendMessage> { new TextMessage("原樣"), new StickerMessage("1", "5") };
        await new ChurchReportLineNotifier(workflow).SendMessagesBestEffortAsync("Utwo", messages);
        var request = Assert.Single(workflow.Requests);
        Assert.Equal("Utwo", request.Recipient.PrimaryId);
        Assert.Same(messages, request.Content.SdkMessages);
        Assert.Equal("ChurchReport.PushUtility.BestEffortSdkMessages", request.Metadata["source"]);
        Assert.Equal(0, workflow.RequiredCalls);
    }

    /// <summary>分別注入結果帶原例外與直接拋出，文字／SDK 四路都須只診斷一次並繼續呼叫端。</summary>
    [Fact]
    public async Task Notifier_BestEffortFailure_ReportsAndDoesNotThrow()
    {
        var directory = Path.Combine(Path.GetTempPath(), "p6-notifier-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var diagnostics = new ExceptionDiagnostics(directory, outputOptions: new(true, false)))
            {
                using var registration = ExceptionReporting.Attach(diagnostics);
                foreach (var throwFailure in new[] { false, true })
                {
                    foreach (var sdk in new[] { false, true })
                    {
                        var workflow = new CapturingWorkflow
                        {
                            Failure = new IOException("private-synthetic-notification"),
                            ThrowFailure = throwFailure
                        };
                        var notifier = new ChurchReportLineNotifier(workflow);
                        var failure = await Record.ExceptionAsync(() => sdk
                            ? notifier.SendMessagesBestEffortAsync("Uone", new[] { new TextMessage("private-body") })
                            : notifier.SendTextBestEffortAsync("Uone", "private-body"));
                        Assert.Null(failure);
                        Assert.Single(workflow.Requests);
                    }
                }
                var invalidContentWorkflow = new CapturingWorkflow();
                var invalidContentNotifier = new ChurchReportLineNotifier(invalidContentWorkflow);
                Assert.Null(await Record.ExceptionAsync(() =>
                    invalidContentNotifier.SendMessagesBestEffortAsync("Uone", Array.Empty<ISendMessage>())));
                Assert.Null(await Record.ExceptionAsync(() =>
                    invalidContentNotifier.SendMessagesBestEffortAsync("Uone", null!)));
                Assert.Empty(invalidContentWorkflow.Requests);
            }
            var records = File.ReadAllLines(Path.Combine(directory, "Exception.log"));
            Assert.Equal(6, records.Length);
            Assert.All(records.Take(4), record => Assert.Contains("System.IO.IOException", record));
            Assert.Contains("System.ArgumentException", records[4]);
            Assert.Contains("System.ArgumentNullException", records[5]);
            Assert.DoesNotContain("private-", string.Join("", records));
            Assert.False(ExceptionReporting.IsActive);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    /// <summary>必要通知傳同一 RetryKey、同一失敗；空白輸入在呼叫 workflow 前拒絕，訊息照舊。</summary>
    [Fact]
    public async Task Notifier_Reliable_PassesRetryKeyAndThrowsOnFailure()
    {
        var failure = new IOException("synthetic-reliable");
        var workflow = new CapturingWorkflow { Failure = failure };
        var notifier = new ChurchReportLineNotifier(workflow);
        Assert.Same(failure, await Record.ExceptionAsync(() =>
            notifier.SendReliableTextAsync("Uone", "原樣付款說明", "retry-fixed")));
        var request = Assert.Single(workflow.Requests);
        Assert.Equal("retry-fixed", request.RetryKey);
        Assert.Equal("原樣付款說明", request.Content.Text);
        Assert.Equal("ChurchReport.PushUtility.ReliableText", request.Metadata["source"]);
        Assert.Equal(1, workflow.RequiredCalls);
        foreach (var blank in new[] { "", " " })
        {
            var userError = await Assert.ThrowsAsync<ArgumentException>(() =>
                notifier.SendReliableTextAsync(blank, "text", null));
            Assert.StartsWith("LINE user id is required.", userError.Message);
            var textError = await Assert.ThrowsAsync<ArgumentException>(() =>
                notifier.SendReliableTextAsync("Uone", blank, null));
            Assert.StartsWith("LINE message is required.", textError.Message);
        }
        Assert.Single(workflow.Requests);
    }

    /// <summary>真實統計先寫兩筆後只多播一次；空清單零副作用，兩種故障先落檔才傳同一例外。</summary>
    [Fact]
    public async Task Notifier_Multicast_WritesStatisticsThenSendsOnce()
    {
        var directory = Path.Combine(Path.GetTempPath(), "p6-multicast-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var diagnostics = new ExceptionDiagnostics(directory, outputOptions: new(true, false));
            using var registration = ExceptionReporting.Attach(diagnostics);
            foreach (var fault in new[] { "none", "crm", "http" })
            {
                var events = new List<string>();
                var crm = DispatchProxy.Create<IOrganizationService, StatisticsCrm>();
                var state = (StatisticsCrm)crm;
                var failure = new IOException("synthetic-" + fault);
                state.Events = events;
                state.Failure = fault == "crm" ? failure : null;
                using var configuration = (ConfigurationRoot)new ConfigurationBuilder().Build();
                using var tracer = new NullToolUtilityTracer();
                using var tool = new ToolUtilityClass(crm, tracer, configuration);
                using var handler = new RecordingHandler("channel", events, fault == "http" ? failure : null);
                using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
                using var client = new LineMessagingClient(http, "channel");
                var notifier = new ChurchReportLineNotifier(
                    new LineLegacySendWorkflow(new LineMessagingProcessorClass(client)), tool);
                await notifier.MulticastTextAsync(Array.Empty<string>(), "原樣");
                Assert.Empty(events);
                var error = await Record.ExceptionAsync(() => notifier.MulticastTextAsync(
                    new[] { "Uone", "Utwo" }, "原樣"));
                if (fault == "none")
                {
                    Assert.Null(error);
                    Assert.Equal(new[] { "statistics", "statistics", "http" }, events);
                    Assert.Equal(2, state.Letters.Count);
                    Assert.All(state.Letters, letter =>
                    {
                        Assert.Equal("Line推播統計:文字", letter.GetAttributeValue<string>("subject"));
                        Assert.Equal("原樣", letter.GetAttributeValue<string>("description"));
                    });
                    Assert.Equal(1, handler.Calls);
                    Assert.Contains("/message/multicast", Assert.Single(handler.Bodies));
                }
                else
                {
                    Assert.Same(failure, error);
                    Assert.True(File.Exists(Path.Combine(directory, "Exception.log")));
                    var records = File.ReadAllLines(Path.Combine(directory, "Exception.log"));
                    Assert.Equal(fault == "crm" ? 1 : 2, records.Length);
                    Assert.Equal(fault == "crm" ? 0 : 1, handler.Calls);
                }
                Assert.Equal(0, handler.Pending);
            }
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    /// <summary>各一百次真實 SDK 交錯操作，handler 驗 token 與收件內容不混頻，最後 pending 與未完成工作皆零。</summary>
    [Fact]
    public async Task Notifier_TwoChannelsInterleaved_NoCrossChannelSend()
    {
        using var a = new RecordingHandler("channel-a");
        using var b = new RecordingHandler("channel-b");
        using var ha = new HttpClient(a) { Timeout = TimeSpan.FromSeconds(5) };
        using var hb = new HttpClient(b) { Timeout = TimeSpan.FromSeconds(5) };
        using var ca = new LineMessagingClient(ha, "channel-a");
        using var cb = new LineMessagingClient(hb, "channel-b");
        var na = ChurchReportLineNotifier.ForClient(ca);
        var nb = ChurchReportLineNotifier.ForClient(cb);
        var tasks = Enumerable.Range(0, 100).SelectMany(i => new[]
        {
            na.SendTextBestEffortAsync("Uonly-a", "only-a-" + i),
            nb.SendMessagesBestEffortAsync("Uonly-b", new[] { new TextMessage("only-b-" + i) })
        }).ToArray();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.All(tasks, task => Assert.True(task.IsCompletedSuccessfully));
        Assert.Equal(100, a.Calls);
        Assert.Equal(100, b.Calls);
        Assert.All(a.Bodies, body => { Assert.Contains("Uonly-a", body); Assert.DoesNotContain("only-b", body); });
        Assert.All(b.Bodies, body => { Assert.Contains("Uonly-b", body); Assert.DoesNotContain("only-a", body); });
        Assert.Equal(0, a.Pending);
        Assert.Equal(0, b.Pending);
    }

    /// <summary>
    /// 以錯誤建構模式呼叫四種方法，必須在送訊與統計之前明確拒絕；
    /// 空多播清單也不能掩蓋誤用。合成 CRM、HTTP 與 workflow 計數皆零，
    /// 所有借用資源由本案 using 擁有，不連線或保存跨案例資料。
    /// </summary>
    [Fact]
    public async Task Notifier_WrongConstructionForMethod_FailsFast()
    {
        var workflow = new CapturingWorkflow();
        var single = new ChurchReportLineNotifier(workflow);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            single.MulticastTextAsync(Array.Empty<string>(), "synthetic"));
        Assert.Empty(workflow.Requests);
        Assert.Equal(0, workflow.RequiredCalls);

        var crm = DispatchProxy.Create<IOrganizationService, StatisticsCrm>();
        var state = (StatisticsCrm)crm;
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().Build();
        using var tracer = new NullToolUtilityTracer();
        using var tool = new ToolUtilityClass(crm, tracer, configuration);
        using var handler = new RecordingHandler("channel");
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        using var client = new LineMessagingClient(http, "channel");
        var multicast = new ChurchReportLineNotifier(
            new LineLegacySendWorkflow(new LineMessagingProcessorClass(client)), tool);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            multicast.SendTextBestEffortAsync("Uone", "synthetic"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            multicast.SendMessagesBestEffortAsync("Uone", new[] { new TextMessage("synthetic") }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            multicast.SendReliableTextAsync("Uone", "synthetic", null));
        Assert.Empty(state.Events);
        Assert.Empty(state.Letters);
        Assert.Equal(0, handler.Calls);
        Assert.Equal(0, handler.Pending);
    }

    /// <summary>反射禁止舊殼復活；同時核對 ToolUtility 實際舊 namespace 與計畫文字的 namespace。</summary>
    [Fact]
    public void Source_NoObsoleteSendShellTypes()
    {
        foreach (var name in new[] { "PushUtility", "ReplyUtility", "LineUtilityClass" })
            Assert.Null(typeof(global::ChurchReport.Program).Assembly.GetType("ChurchReport.Tools." + name));
        Assert.Null(typeof(ToolUtilityClass).Assembly.GetType("ToolUtility.PushUtility"));
        Assert.Null(typeof(ToolUtilityClass).Assembly.GetType("ToolUtilityNameSpace.PushUtility"));
    }

    /// <summary>只保留當案請求以核對參數，故障可由結果或原例外注入；無 I/O 或資源 owner。</summary>
    private sealed class CapturingWorkflow : ILineNotificationWorkflow
    {
        internal List<LineNotificationRequest> Requests { get; } = new();
        internal Exception? Failure { get; init; }
        internal bool ThrowFailure { get; init; }
        internal int RequiredCalls { get; private set; }

        /// <summary>捕捉實際通知器組成的請求；以兩種失敗接縫分辨診斷與吞錯行為。</summary>
        public Task<LineNotificationResult> SendAsync(LineNotificationRequest request)
        {
            Requests.Add(request);
            if (ThrowFailure) throw Failure!;
            return Task.FromResult(Failure == null ? LineNotificationResult.Success(request) :
                LineNotificationResult.Failure(request, LineNotificationStatus.ProviderUnavailable,
                    "synthetic", "synthetic", Failure));
        }

        /// <summary>必要入口保留原例外實例，計數可分辨誤走 best-effort。</summary>
        public Task SendOrThrowAsync(LineNotificationRequest request)
        {
            RequiredCalls++;
            Requests.Add(request);
            return Failure == null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }

    /// <summary>合成 CRM 只允許統計查會友及建 letter；資料限定單案，無連線或可釋放資源。</summary>
    public class StatisticsCrm : DispatchProxy
    {
        internal List<string> Events { get; set; } = new();
        internal List<Entity> Letters { get; } = new();
        internal Exception? Failure { get; set; }

        /// <summary>查詢回固定不同於正式環境的合成主鍵；建立時先記序，故障不進 HTTP。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (Failure != null) throw Failure;
            if (targetMethod?.Name == nameof(IOrganizationService.RetrieveMultiple))
                return new EntityCollection(new List<Entity>
                {
                    new("contact", Guid.Parse("55555555-5555-5555-5555-555555555555"))
                });
            if (targetMethod?.Name == nameof(IOrganizationService.Create))
            {
                Letters.Add((Entity)args![0]!);
                Events.Add("statistics");
                return Guid.NewGuid();
            }
            throw new InvalidOperationException("Unexpected synthetic CRM operation.");
        }
    }

    /// <summary>有界合成 HTTP，僅保存遮罩外的合成本文；不保留 request，離開 await 即減少 pending。</summary>
    private sealed class RecordingHandler(string token, List<string>? events = null, Exception? failure = null)
        : HttpMessageHandler
    {
        private int calls;
        private int pending;
        internal int Calls => Volatile.Read(ref calls);
        internal int Pending => Volatile.Read(ref pending);
        internal List<string> Bodies { get; } = new();

        /// <summary>真實 SDK 的 token、路由及本文是斷言邊界，Yield 強迫多個頻道同時存活。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref pending);
            try
            {
                await Task.Yield();
                Assert.Equal(token, request.Headers.Authorization?.Parameter);
                var body = request.RequestUri + "\n" + await request.Content!.ReadAsStringAsync(cancellationToken);
                lock (Bodies) Bodies.Add(body);
                Interlocked.Increment(ref calls);
                events?.Add("http");
                if (failure != null) throw failure;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
            }
            finally { Interlocked.Decrement(ref pending); }
        }

        /// <summary>外層已等待排空後清掉當案合成資料，不保存跨測試的訊息或 client。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing) Bodies.Clear();
            base.Dispose(disposing);
        }
    }
}

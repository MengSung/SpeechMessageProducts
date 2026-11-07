/*
檔案整體說明

目的與定位：保護 P2-01 真正把 LINE 例外傳輸搬入 Hosting 的產品契約，而非只建立
轉接殼。測試只使用合成設定與自有 HttpMessageHandler，不連 LINE、D365 或 User Secrets。

主要功能與流程：
先驗證唯一實作組件，再以可控 HTTP 邊界驗證設定優先序、每筆 Authorization／JSON、
錯誤保密、取消與五秒逾時，以及 request／response／handler 的確定性釋放。
並行案例使用不同部署 owner 的合成 token／收件人，斷言不跨 owner 共用狀態。

使用邊界與維護：
每個案例獨占組態 root、sender 與 transport，先等待所有 SendAsync 完成再 Dispose。
替身只短暫保留合成快照與清理計數，測試結束清空，不建立 socket、timer 常駐工作、
真實憑證或通知。離線通過不代表已完成供應商實機推播或整包 P2 驗收。
*/
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace SpeechMessage.Hosting.Tests;

/// <summary>
/// 以組件歸屬及離線傳輸邊界保護共用 sender 搬移、部署隔離、失敗傳播與資源所有權。
/// </summary>
public sealed class LineExceptionSenderTests
{
    /// <summary>
    /// sender 必須真正定義於 Hosting；只保留 ChurchReport 實作或未接入的 adapter 均失敗。
    /// 本案例只讀組件 metadata，不建構可連線的預設傳輸。
    /// </summary>
    [Fact]
    public void Sender_implementation_belongs_to_Hosting()
    {
        var assembly = typeof(ExceptionReportingAdapter).Assembly;
        var sender = assembly.GetType("SpeechMessage.Hosting.LineExceptionSender");
        Assert.NotNull(sender);
        Assert.Equal(assembly, sender.Assembly);
    }

    /// <summary>以三種合成來源驗證告警專用優先序，不修改程序環境也不讀取真實 token。</summary>
    [Theory]
    [InlineData("environment-A", "organization-A", "default-A", "environment-A")]
    [InlineData(null, "organization-A", "default-A", "organization-A")]
    [InlineData(" ", "", "default-A", "default-A")]
    public async Task Token_selection_preserves_environment_organization_default(
        string? environment, string? organization, string? fallback, string expected)
    {
        using var configuration = Configuration(environment, organization, fallback);
        using var transport = new RecordingTransport();
        using var sender = new LineExceptionSender(configuration, transport: transport);
        await sender.SendAsync("safe-incident", CancellationToken.None);
        var request = Assert.Single(transport.Requests);
        Assert.Equal(expected, request.Token);
        Assert.Equal("recipient-A", request.Recipient);
        Assert.Equal("safe-incident", request.Message);
        Assert.Equal("https://api.line.me/v2/bot/message/push", request.Url);
        Assert.Equal("POST", request.Method);
        Assert.Equal("Bearer", request.Scheme);
        Assert.Equal(0, transport.Active);
        Assert.Equal(1, transport.ContentDisposals);
        Assert.Equal(0, transport.BodyReads);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => transport.LastRequestContent!.ReadAsStringAsync());
    }

    /// <summary>後備管理者只由組合根提供；已建立 sender 保留單次快照，不跟隨可變設定換人。</summary>
    [Fact]
    public async Task Recipient_fallback_and_configuration_snapshot_remain_host_owned()
    {
        using var configuration = Configuration(recipient: null);
        using var transport = new RecordingTransport();
        using var sender = new LineExceptionSender(configuration, "fallback-recipient", transport);
        configuration["ExceptionNotifications:AdminLineUserId"] = "later-recipient";
        configuration["LINE_CHANNEL_ACCESS_TOKEN"] = "later-token";
        await sender.SendAsync("safe-incident", CancellationToken.None);
        var request = Assert.Single(transport.Requests);
        Assert.Equal("fallback-recipient", request.Recipient);
        Assert.Equal("environment-A", request.Token);
    }

    /// <summary>兩個部署 owner 同時送出各自六十四筆，token／收件人／本文必須完全不串流。</summary>
    [Fact]
    public async Task Concurrent_deployments_keep_request_credentials_and_recipients_isolated()
    {
        using var configurationA = Configuration();
        using var configurationB = Configuration(environment: "environment-B", recipient: "recipient-B");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var transportA = new RecordingTransport { HoldUntil = release.Task, ExpectedRequests = 64 };
        using var transportB = new RecordingTransport { HoldUntil = release.Task, ExpectedRequests = 64 };
        using var senderA = new LineExceptionSender(configurationA, transport: transportA);
        using var senderB = new LineExceptionSender(configurationB, transport: transportB);
        var pending = Enumerable.Range(0, 64).SelectMany(index => new[]
        {
            senderA.SendAsync("A-" + index.ToString(CultureInfo.InvariantCulture), CancellationToken.None),
            senderB.SendAsync("B-" + index.ToString(CultureInfo.InvariantCulture), CancellationToken.None)
        }).ToArray();
        try
        {
            await Task.WhenAll(transportA.Entered.Task, transportB.Entered.Task).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(64, transportA.Active);
            Assert.Equal(64, transportB.Active);
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(8));
        }
        Assert.Equal(64, transportA.Requests.Count);
        Assert.Equal(64, transportB.Requests.Count);
        Assert.All(transportA.Requests, request =>
        {
            Assert.Equal("environment-A", request.Token);
            Assert.Equal("recipient-A", request.Recipient);
            Assert.StartsWith("A-", request.Message, StringComparison.Ordinal);
        });
        Assert.All(transportB.Requests, request =>
        {
            Assert.Equal("environment-B", request.Token);
            Assert.Equal("recipient-B", request.Recipient);
            Assert.StartsWith("B-", request.Message, StringComparison.Ordinal);
        });
        Assert.Equal(0, transportA.Active + transportB.Active);
        Assert.Equal(128, transportA.ContentDisposals + transportB.ContentDisposals);
        Assert.Equal(0, transportA.BodyReads + transportB.BodyReads);
    }

    /// <summary>非成功 HTTP 狀態保留分類但不讀供應商本文；同一 request 不進行重試或遞迴通知。</summary>
    [Fact]
    public async Task Provider_failure_has_fixed_message_and_disposes_response_without_reading_body()
    {
        using var configuration = Configuration();
        using var transport = new RecordingTransport { Status = HttpStatusCode.TooManyRequests };
        using var sender = new LineExceptionSender(configuration, transport: transport);
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            sender.SendAsync("safe-incident", CancellationToken.None));
        Assert.Equal("LINE exception delivery failed.", exception.Message);
        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.Single(transport.Requests);
        Assert.Equal(1, transport.ContentDisposals);
        Assert.Equal(0, transport.BodyReads);
        Assert.Equal(0, transport.Active);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => transport.LastRequestContent!.ReadAsStringAsync());
    }

    /// <summary>缺設定必須在傳輸前拒絕，不能沿用另一 sender 的憑證或向測試端點發出空請求。</summary>
    [Fact]
    public async Task Missing_configuration_fails_before_transport()
    {
        using var configuration = Configuration(environment: null, organization: null, fallback: null, recipient: null);
        using var transport = new RecordingTransport();
        using var sender = new LineExceptionSender(configuration, transport: transport);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendAsync("safe-incident", CancellationToken.None));
        Assert.Equal("Exception LINE notification configuration is missing.", exception.Message);
        Assert.Empty(transport.Requests);
        Assert.Equal(0, transport.Active);
    }

    /// <summary>設定驗證先於 handler 所有權移交；建構失敗時原 caller 仍能決定如何釋放輸入。</summary>
    [Fact]
    public void Invalid_configuration_does_not_adopt_external_transport()
    {
        using var configuration = Configuration();
        configuration["CrmConnection:Organization"] = "invalid:organization";
        using var transport = new RecordingTransport();
        Assert.Throws<ArgumentException>(() =>
        {
            using var unexpected = new LineExceptionSender(configuration, transport: transport);
        });
        Assert.Equal(0, transport.DisposeCalls);
        Assert.Empty(transport.Requests);
    }

    /// <summary>無回應的傳輸故障必須保留原始例外身分，讓共用 diagnostics 能正確去重與分類。</summary>
    [Fact]
    public async Task Transport_failure_preserves_exception_identity_and_disposes_request()
    {
        using var configuration = Configuration();
        var failure = new HttpRequestException("Synthetic transport failure.");
        using var transport = new RecordingTransport { TransportFailure = failure };
        using var sender = new LineExceptionSender(configuration, transport: transport);
        var actual = await Assert.ThrowsAsync<HttpRequestException>(() =>
            sender.SendAsync("safe-incident", CancellationToken.None));
        Assert.Same(failure, actual);
        Assert.Equal(0, transport.Active);
        Assert.Single(transport.Requests);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => transport.LastRequestContent!.ReadAsStringAsync());
    }

    /// <summary>caller 主動取消會中斷受控傳輸並排空，不能吞掉取消而宣稱推播成功。</summary>
    [Fact]
    public async Task Caller_cancellation_propagates_and_drains_transport()
    {
        using var configuration = Configuration();
        using var transport = new RecordingTransport { WaitForCancellation = true };
        using var sender = new LineExceptionSender(configuration, transport: transport);
        using var cancellation = new CancellationTokenSource();
        var pending = sender.SendAsync("safe-incident", cancellation.Token);
        await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(0, transport.Active);
        Assert.Single(transport.Requests);
    }

    /// <summary>未取消 caller 的內部五秒逾時必須向上失敗，且 linked cancellation 不留存活工作。</summary>
    [Fact]
    public async Task Internal_timeout_is_not_disguised_as_caller_cancellation()
    {
        using var configuration = Configuration();
        using var transport = new RecordingTransport { WaitForCancellation = true };
        using var sender = new LineExceptionSender(configuration, transport: transport);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sender.SendAsync("safe-incident", cancellation.Token).WaitAsync(TimeSpan.FromSeconds(8)));
        Assert.False(cancellation.IsCancellationRequested);
        Assert.Equal(0, transport.Active);
        Assert.Single(transport.Requests);
    }

    /// <summary>緊急關閉會取消尚未完成的 HTTP 工作；測試等待確定排空，不遺留 delay 或註冊。</summary>
    [Fact]
    public async Task Dispose_cancels_inflight_transport_and_work_returns_to_baseline()
    {
        using var configuration = Configuration();
        using var transport = new RecordingTransport { WaitForCancellation = true };
        using var sender = new LineExceptionSender(configuration, transport: transport);
        var pending = sender.SendAsync("safe-incident", CancellationToken.None);
        await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        sender.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(0, transport.Active);
        Assert.Equal(1, transport.DisposeCalls);
    }

    /// <summary>重複關閉只釋放一個自有 handler；關閉後必須以 disposed 契約拒絕任何新操作。</summary>
    [Fact]
    public async Task Dispose_is_idempotent_and_closed_sender_rejects_new_work()
    {
        using var configuration = Configuration();
        using var transport = new RecordingTransport();
        using var sender = new LineExceptionSender(configuration, transport: transport);
        sender.Dispose();
        sender.Dispose();
        Assert.Equal(1, transport.DisposeCalls);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            sender.SendAsync("safe-incident", CancellationToken.None));
        Assert.Empty(transport.Requests);
    }

    /// <summary>存活但已關閉的 sender 不得繼續保留部署 token 或收件人；反射只讀自有欄位。</summary>
    [Fact]
    public void Dispose_clears_retained_deployment_strings()
    {
        using var configuration = Configuration();
        using var transport = new RecordingTransport();
        using var sender = new LineExceptionSender(configuration, transport: transport);
        sender.Dispose();
        var fields = typeof(LineExceptionSender).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(field => field.FieldType == typeof(string));
        Assert.All(fields, field => Assert.True(
            string.IsNullOrEmpty((string?)field.GetValue(sender)), "Closed sender retains deployment state."));
    }

    /// <summary>transport 關閉失敗仍必須清除部署參照並保留原失敗；第二次關閉不再重入失敗 owner。</summary>
    [Fact]
    public void Dispose_failure_still_clears_state_and_does_not_repeat_cleanup()
    {
        using var configuration = Configuration();
        var transport = new RecordingTransport { ThrowOnDispose = true };
        using var sender = new LineExceptionSender(configuration, transport: transport);
        Assert.Throws<InvalidOperationException>(sender.Dispose);
        var fields = typeof(LineExceptionSender).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(field => field.FieldType == typeof(string));
        Assert.All(fields, field => Assert.True(
            string.IsNullOrEmpty((string?)field.GetValue(sender)), "Failed cleanup retains deployment state."));
        sender.Dispose();
        Assert.Equal(1, transport.DisposeCalls);
    }

    /// <summary>檢查真正預設 handler 的安全設定，不建立連線或用測試替身假冒正式設定。</summary>
    [Fact]
    public void Default_transport_has_no_cookie_redirect_or_unbounded_connection_pool()
    {
        var factory = typeof(LineExceptionSender).GetMethod("CreateTransport", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(factory);
        using var handler = Assert.IsType<SocketsHttpHandler>(factory.Invoke(null, null));
        Assert.False(handler.UseCookies);
        Assert.False(handler.AllowAutoRedirect);
        Assert.Equal(1, handler.MaxConnectionsPerServer);
        Assert.Equal(TimeSpan.FromMinutes(5), handler.PooledConnectionLifetime);
    }

    /// <summary>建立只有合成 token／收件人的組態；每個 caller 負責 Dispose 此 root。</summary>
    private static ConfigurationRoot Configuration(
        string? environment = "environment-A", string? organization = "organization-A",
        string? fallback = "default-A", string? recipient = "recipient-A") =>
        (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CrmConnection:Organization"] = "alpha",
            ["LineMessaging:DefaultOrganization"] = "Fallback",
            ["LINE_CHANNEL_ACCESS_TOKEN"] = environment,
            ["LineMessaging:Alpha:ChannelAccessToken"] = organization,
            ["LineMessaging:Fallback:ChannelAccessToken"] = fallback,
            ["ExceptionNotifications:AdminLineUserId"] = recipient
        }).Build();

    /// <summary>只含合成純值的單筆傳輸快照，不保存 request、response 或 provider 參照。</summary>
    private sealed record CapturedRequest(
        string Token, string Recipient, string Message, string Url, string Method, string Scheme);

    /// <summary>
    /// 每案例唯一 owned transport；保存短暫合成快照與 request content 以驗證 Dispose，
    /// 無 socket 或背景工作；關閉時清空內容參照，計數僅保留無敏感資料的整數。
    /// </summary>
    private sealed class RecordingTransport : HttpMessageHandler
    {
        /// <summary>目前尚未完成的傳輸數；所有成功、故障與取消都必須歸零。</summary>
        private int active;
        /// <summary>已確定釋放的 response content 數，不保存實際回應。</summary>
        private int contentDisposals;
        /// <summary>供應商本文被讀取的次數，正式路徑必須為零。</summary>
        private int bodyReads;
        /// <summary>handler 清理呼叫數，用以辨識 sender 重複關閉。</summary>
        private int disposeCalls;
        /// <summary>已進入傳輸的工作數，讓並行測試不以時序猜測真實重疊。</summary>
        private int enteredCount;
        /// <summary>僅此案例的合成純值快照；Dispose 清空，不跨案例發布。</summary>
        internal ConcurrentQueue<CapturedRequest> Requests { get; } = new();
        /// <summary>達到 ExpectedRequests 才完成，並行案例以此證明所有工作同時存活。</summary>
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>只為驗證 request content 已關閉而短暫借用，Dispose 立即清除參照。</summary>
        internal HttpContent? LastRequestContent { get; private set; }
        /// <summary>本案例注入的固定 HTTP 分類，不模擬供應商實機行為。</summary>
        internal HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        /// <summary>改為等待 token，用來測試正常 caller 取消與 client 內部逾時。</summary>
        internal bool WaitForCancellation { get; init; }
        /// <summary>注入關閉失敗，保護 sender 的 finally 清理與失敗身分。</summary>
        internal bool ThrowOnDispose { get; init; }
        /// <summary>注入一個確定的原始傳輸例外，只保留於案例生命週期內。</summary>
        internal HttpRequestException? TransportFailure { get; set; }
        /// <summary>案例 owner 提供的有界 barrier，必須在 finally 解除並排空所有工作。</summary>
        internal Task? HoldUntil { get; init; }
        /// <summary>本案例要觀測的同時進入數；一般案例為一、隔離案例為六十四。</summary>
        internal int ExpectedRequests { get; init; } = 1;
        /// <summary>目前傳輸數的原子快照。</summary>
        internal int Active => Volatile.Read(ref active);
        /// <summary>已關閉回應本文數的原子快照。</summary>
        internal int ContentDisposals => Volatile.Read(ref contentDisposals);
        /// <summary>不應發生的本文讀取數。</summary>
        internal int BodyReads => Volatile.Read(ref bodyReads);
        /// <summary>已執行 handler Dispose 的次數。</summary>
        internal int DisposeCalls => Volatile.Read(ref disposeCalls);

        /// <summary>讀取合成 request 並回傳可觀測的 response，或等 caller／client 取消以注入逾時。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref active);
            try
            {
                LastRequestContent = request.Content;
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var root = json.RootElement;
                Requests.Enqueue(new CapturedRequest(
                    request.Headers.Authorization!.Parameter!,
                    root.GetProperty("to").GetString()!,
                    root.GetProperty("messages")[0].GetProperty("text").GetString()!,
                    request.RequestUri!.AbsoluteUri, request.Method.Method, request.Headers.Authorization.Scheme));
                if (Interlocked.Increment(ref enteredCount) == ExpectedRequests)
                    Entered.TrySetResult();
                if (TransportFailure is not null) throw TransportFailure;
                if (HoldUntil is not null)
                    await HoldUntil.WaitAsync(cancellationToken);
                if (WaitForCancellation)
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new HttpResponseMessage(Status) { Content = new ProbeContent(this) };
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }

        /// <summary>關閉時清除案例快照與 content 強參照；不得以此方法假裝取消仍未完成的工作。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Interlocked.Increment(ref disposeCalls);
                LastRequestContent = null;
                TransportFailure = null;
                Requests.Clear();
                if (ThrowOnDispose) throw new InvalidOperationException("Synthetic transport cleanup failure.");
            }
            base.Dispose(disposing);
        }

        /// <summary>只用來監視 client 是否誤讀供應商本文，以及 response 是否確實關閉。</summary>
        private sealed class ProbeContent(RecordingTransport owner) : HttpContent
        {
            /// <summary>任何本文讀取均計數；正式 sender 不應呼叫此路徑。</summary>
            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            {
                Interlocked.Increment(ref owner.bodyReads);
                return Task.CompletedTask;
            }

            /// <summary>合成空本文，不配置大型 buffer 或外部 stream。</summary>
            protected override bool TryComputeLength(out long length)
            {
                length = 0;
                return true;
            }

            /// <summary>記錄 response content 釋放，使成功與錯誤路徑均有決定性斷言。</summary>
            protected override void Dispose(bool disposing)
            {
                if (disposing) Interlocked.Increment(ref owner.contentDisposals);
                base.Dispose(disposing);
            }
        }
    }
}

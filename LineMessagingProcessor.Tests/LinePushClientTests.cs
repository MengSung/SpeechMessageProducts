/*
 * 檔案整體說明
 *
 * 目的與定位：以完全離線的 HTTP 替身保護雙目標單人推播契約；故障只在記憶體注入。 所有資料均為合成值；測試擁有 client、handler、取消來源與閘門，結束必須排空並 Dispose。 原列十一個方法完整保留，
 * 額外驗證原始例外、caller 取消、不可變快照及資源釋放。
 *
 * 主要功能與流程：
 * 本檔宣告 LinePushClientTests、Fixture、Handler、TrackedContent。
 * - 固定合成收件 ID，不代表任何真實會友，也不作為授權來源。
 * - 固定業務事件鍵，保證黃金 JSON／標頭不依賴亂數或時間。
 * - 保護雙目標參考依測試宿主選出正確產品輸出：net48 宿主必須載入 Plugin 使用的 net462 及 Framework 專用無參數客戶端；net10 宿主則載入 net10 與注入 HttpClient
 * 的公開建構式。 測試檢查實際載入組件和建構式，不能只由測試專案的目標框架推定產品分支。
 * - 成功回應注入固定 ID；斷言 URL、方法、兩個逐請求標頭及共用標頭沒有被污染。
 * 測試案例入口包含 Assembly_TargetFramework_AndClientBranch_MatchHost、Push_Success_SendsBearerAndRetryKeyHeaders、
 * Push_409_TreatedAsAlreadyAccepted、Push_429_IsRetryable、Push_500_IsRetryable、
 * Push_Timeout_IsRetryableAndDisposesCts、Push_401_IsPermanent、Push_InvalidUserId_ThrowsBeforeHttp；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using LineMessagingProcessor.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LineMessagingProcessor.Tests;

/// <summary>
/// 以完全離線的 HTTP 替身保護雙目標單人推播契約；故障只在記憶體注入。
/// 所有資料均為合成值；測試擁有 client、handler、取消來源與閘門，結束必須排空並 Dispose。
/// 原列十一個方法完整保留，額外驗證原始例外、caller 取消、不可變快照及資源釋放。
/// </summary>
public sealed class LinePushClientTests
{
    /// <summary>固定合成收件 ID，不代表任何真實會友，也不作為授權來源。</summary>
    private const string UserId = "U00000000000000000000000000000000";
    /// <summary>只用於離線標頭比對的合成值，不是有效頻道憑證。</summary>
    private const string Token = "synthetic-channel-secret";
    /// <summary>固定業務事件鍵，保證黃金 JSON／標頭不依賴亂數或時間。</summary>
    private static readonly Guid RetryKey = new("11111111-2222-3333-4444-555555555555");

    /// <summary>
    /// 保護雙目標參考依測試宿主選出正確產品輸出：net48 宿主必須載入 Plugin 使用的 net462
    /// 及 Framework 專用無參數客戶端；net10 宿主則載入 net10 與注入 HttpClient 的公開建構式。
    /// 測試檢查實際載入組件和建構式，不能只由測試專案的目標框架推定產品分支。
    /// </summary>
    [Fact(DisplayName = "Plugin Core 專案參考選取宿主對應產品實作")]
    public void Assembly_TargetFramework_AndClientBranch_MatchHost()
    {
        var assembly = typeof(LinePushClient).Assembly;
        var attribute = assembly.GetCustomAttributes(typeof(TargetFrameworkAttribute), false)
            .Cast<TargetFrameworkAttribute>().Single();

#if NETFRAMEWORK
        Assert.Equal(".NETFramework,Version=v4.6.2", attribute.FrameworkName);
        Assert.NotNull(typeof(LinePushClient).GetConstructor(Type.EmptyTypes));
#else
        Assert.Equal(".NETCoreApp,Version=v10.0", attribute.FrameworkName);
        Assert.Null(typeof(LinePushClient).GetConstructor(Type.EmptyTypes));
        Assert.NotNull(typeof(LinePushClient).GetConstructor(new[] { typeof(HttpClient) }));
#endif
    }

    /// <summary>成功回應注入固定 ID；斷言 URL、方法、兩個逐請求標頭及共用標頭沒有被污染。</summary>
    [Fact(DisplayName = "成功推播使用當次 Bearer 與 Retry Key")]
    public async Task Push_Success_SendsBearerAndRetryKeyHeaders()
    {
        using var fixture = new Fixture();
        var result = await fixture.Client.PushAsync(new(Token), Request(), default);
        Assert.True(result.Success);
        Assert.Equal(LinePushOutcome.Success, result.Outcome);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("synthetic-request-id", result.RequestId);
        Assert.Equal("POST", fixture.Handler.Method);
        Assert.Equal("https://api.line.me/v2/bot/message/push", fixture.Handler.Url);
        Assert.Equal("Bearer " + Token, fixture.Handler.Authorization);
        Assert.Equal(RetryKey.ToString("D"), fixture.Handler.RetryHeader);
        Assert.Null(fixture.Http.DefaultRequestHeaders.Authorization);
        Assert.False(fixture.Http.DefaultRequestHeaders.Contains("X-Line-Retry-Key"));
    }

    /// <summary>注入同鍵已接受回應；必須成功且明確分類，不能要求換鍵再送。</summary>
    [Fact(DisplayName = "409 視為相同事件已接受")]
    public async Task Push_409_TreatedAsAlreadyAccepted()
    {
        using var fixture = new Fixture(409);
        var result = await fixture.Client.PushAsync(new(Token), Request(), default);
        Assert.True(result.Success);
        Assert.Equal(LinePushOutcome.AlreadyAccepted, result.Outcome);
        Assert.Equal((HttpStatusCode)409, result.StatusCode);
    }

    /// <summary>以節流狀態注入可恢復失敗；必須只回傳可重試結果，不在 Core 重送。</summary>
    [Fact(DisplayName = "429 可重試且一次只送一次")]
    public async Task Push_429_IsRetryable() => await AssertStatus(429, LinePushOutcome.Retryable);

    /// <summary>伺服器故障以狀態碼分類，不製造不存在的傳輸例外。</summary>
    [Fact(DisplayName = "500 分類為可重試")]
    public async Task Push_500_IsRetryable() => await AssertStatus(500, LinePushOutcome.Retryable);

    /// <summary>
    /// 手動取消 Core 所有的逾時來源並拋出原始 TaskCanceledException；
    /// 斷言期限為十秒、保留 TaskCanceledException 型別、無狀態碼，回傳前來源與連結 token 均已釋放。
    /// Framework 的取消 Task 會在 HttpClient 邊界正規化例外，不能把 handler 內部實例誤當 Core 收到的實例。
    /// </summary>
    [Fact(DisplayName = "內部十秒逾時保留例外並釋放取消來源")]
    public async Task Push_Timeout_IsRetryableAndDisposesCts()
    {
        using var timeout = new CancellationTokenSource();
        var original = new TaskCanceledException("Synthetic timeout.");
        using var fixture = new Fixture();
        fixture.Handler.Reply = (_, _) => { timeout.Cancel(); throw original; };
        TimeSpan? observed = null;
        var client = new LinePushClient(fixture.Http, duration => { observed = duration; return timeout; });
        var result = await client.PushAsync(new(Token), Request(), default);
        Assert.Equal(TimeSpan.FromSeconds(10), observed);
        Assert.Equal(LinePushOutcome.Retryable, result.Outcome);
        Assert.IsType<TaskCanceledException>(result.TransportException);
        Assert.Null(result.StatusCode);
        Assert.Null(result.RequestId);
        Assert.Throws<ObjectDisposedException>(() => timeout.Cancel());
        Assert.Throws<ObjectDisposedException>(() => _ = fixture.Handler.SentToken.WaitHandle);
    }

    /// <summary>錯誤憑證收到 401，必須永久失敗且 Core 不盲目重試。</summary>
    [Fact(DisplayName = "401 屬永久失敗")]
    public async Task Push_401_IsPermanent() => await AssertStatus(401, LinePushOutcome.Permanent);

    /// <summary>注入格式錯誤、大小寫錯誤與尾端換行；必須在送 HTTP 之前拒絕且訊息不回顯輸入。</summary>
    [Theory(DisplayName = "非法單人 ID 在 HTTP 前拒絕")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ushort")]
    [InlineData("G00000000000000000000000000000000")]
    [InlineData("U0000000000000000000000000000000A")]
    [InlineData("U00000000000000000000000000000000\n")]
    [InlineData("U00000000000000000000000000000000 ")]
    public void Push_InvalidUserId_ThrowsBeforeHttp(string? invalid)
    {
        using var fixture = new Fixture();
        var error = Assert.Throws<ArgumentException>(() => new LinePushRequest(invalid!, new[] { "hello" }, RetryKey));
        Assert.Equal("userId", error.ParamName);
        Assert.DoesNotContain("Ushort", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Handler.Calls);
    }

    /// <summary>以真實請求快照與反射驗證公開面只有單人 ID，沒有多播／廣播方法或多人收件型別。</summary>
    [Fact(DisplayName = "公開能力無法表示多人推播")]
    public void Push_MoreThanOneRecipientNotPossible()
    {
        var request = Request();
        Assert.Equal(UserId, request.UserId);
        Assert.Equal(typeof(string), typeof(LinePushRequest).GetProperty(nameof(LinePushRequest.UserId))!.PropertyType);
        Assert.Single(typeof(ILinePushClient).GetMethods());
        Assert.Equal("PushAsync", typeof(ILinePushClient).GetMethods()[0].Name);
        Assert.DoesNotContain(typeof(LinePushClient).GetMethods(), method =>
            method.Name.IndexOf("multicast", StringComparison.OrdinalIgnoreCase) >= 0 ||
            method.Name.IndexOf("broadcast", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    /// <summary>插值及一般診斷只顯示固定遮罩；精確斷言避免把 token 藏在摘要尾端。</summary>
    [Fact(DisplayName = "憑證字串永遠只回傳固定遮罩")]
    public void Credential_ToString_NeverContainsToken()
        => Assert.Equal("LineChannelCredential(***)", new LineChannelCredential(Token).ToString());

    /// <summary>以檔案黃金資料比較實際傳輸 JSON；只允許單一 to 與 text 訊息，不含重試鍵或 token。</summary>
    [Fact(DisplayName = "單人文字 JSON 與黃金檔完全一致")]
    public async Task Push_RequestBody_MatchesGoldenJson()
    {
        using var fixture = new Fixture();
        await fixture.Client.PushAsync(new(Token), Request(), default);
        var golden = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "push-text.json"));
        Assert.True(JToken.DeepEquals(JToken.Parse(golden), JToken.Parse(fixture.Handler.Body!)));
        Assert.DoesNotContain(Token, fixture.Handler.Body!, StringComparison.Ordinal);
        Assert.Equal("application/json; charset=utf-8", fixture.Handler.ContentType);
    }

    /// <summary>
    /// 同一 client 同時送五十個不同頻道／事件；閘門確保全部在途，逐請求交叉核對，
    /// 等待所有工作完成才釋放 owner，斷言活躍數回零、沒有共用標頭或結果混用。
    /// </summary>
    [Fact(DisplayName = "五十個並行憑證的標頭及結果不互串")]
    public async Task Push_ConcurrentDifferentTokens_HeadersNotMixed()
    {
        const int count = 50;
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        int entered = 0;
        using var fixture = new Fixture();
        fixture.Handler.Reply = async (request, ct) =>
        {
            try
            {
                var body = JObject.Parse(await request.Content!.ReadAsStringAsync());
                var message = body["messages"]![0]!["text"]!.Value<string>()!;
                Assert.Equal("synthetic-" + message, request.Headers.Authorization!.Parameter);
                Assert.Equal(Guid.Parse(message).ToString("D"), request.Headers.GetValues("X-Line-Retry-Key").Single());
                Assert.True(seen.TryAdd(message, true));
                if (Interlocked.Increment(ref entered) == count) gate.TrySetResult(true);
                await gate.Task;
                ct.ThrowIfCancellationRequested();
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            catch (Exception exception)
            {
                // 任何斷言失敗也要打開故障閘門，讓其他請求結束，不能留下永遠等待的測試工作。
                gate.TrySetException(exception);
                throw;
            }
        };
        var client = fixture.Client;
        var tasks = Enumerable.Range(1, count).Select(index =>
        {
            var key = Guid.Parse(index.ToString("x8", CultureInfo.InvariantCulture) + "-0000-0000-0000-000000000000");
            var text = key.ToString("D");
            return client.PushAsync(new("synthetic-" + text), new(UserId, new[] { text }, key), default);
        }).ToArray();
        try
        {
            var results = await Task.WhenAll(tasks);
            Assert.All(results, result => Assert.True(result.Success));
            Assert.Equal(count, seen.Count);
            Assert.Equal(count, fixture.Handler.PeakActive);
            Assert.Equal(0, fixture.Handler.Active);
            Assert.Null(fixture.Http.DefaultRequestHeaders.Authorization);
        }
        finally { gate.TrySetResult(true); await Task.WhenAll(tasks); }
    }

    /// <summary>注入已取消或在途 caller token；必須拋取消而非回結果，並釋放操作資源。</summary>
    [Theory(DisplayName = "caller 取消不誤判可重試故障")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Push_CallerCanceled_ThrowsOperationCanceled_NotRetryable(bool inFlight)
    {
        using var caller = new CancellationTokenSource();
        using var fixture = new Fixture();
        if (!inFlight) caller.Cancel();
        fixture.Handler.Reply = (_, ct) => { caller.Cancel(); throw new OperationCanceledException("Synthetic caller cancellation.", ct); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Client.PushAsync(new(Token), Request(), caller.Token));
        Assert.Equal(inFlight ? 1 : 0, fixture.Handler.Calls);
        Assert.Equal(0, fixture.Handler.Active);
    }

    /// <summary>caller 與網路故障競爭時，已觀察的 caller 取消仍須以取消退出，不把網路故障當結果或洩漏。</summary>
    [Fact(DisplayName = "caller 取消優先於同時發生的網路故障")]
    public async Task Push_CallerCanceledDuringNetworkFailure_ThrowsCancellation()
    {
        using var caller = new CancellationTokenSource();
        using var fixture = new Fixture();
        fixture.Handler.Reply = (_, _) => { caller.Cancel(); throw new HttpRequestException("Synthetic connection fault."); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Client.PushAsync(new(Token), Request(), caller.Token));
    }

    /// <summary>
    /// 假傳輸先建立完整 HTTP 回應，再於交回回應前取消 caller；驗證已知狀態仍依 2xx／429 分類，
    /// 並確認 request、response、連結取消來源與 handler 在途計數均已確定性釋放。
    /// </summary>
    [Theory(DisplayName = "回應抵達後 caller 取消仍回傳狀態分類")]
    [InlineData(200, LinePushOutcome.Success)]
    [InlineData(429, LinePushOutcome.Retryable)]
    public async Task Push_CallerCanceledAfterResponse_ReturnsClassifiedResult(int status, LinePushOutcome expected)
    {
        using var caller = new CancellationTokenSource();
        using var fixture = new Fixture(status);
        using var timeout = new CancellationTokenSource();
        var createResponse = fixture.Handler.Reply;
        fixture.Handler.Reply = async (request, transportToken) =>
        {
            var response = await createResponse(request, transportToken).ConfigureAwait(false);
            caller.Cancel();
            return response;
        };
        var client = new LinePushClient(fixture.Http, _ => timeout);

        var result = await client.PushAsync(new(Token), Request(), caller.Token);

        Assert.True(caller.IsCancellationRequested);
        Assert.Equal(expected, result.Outcome);
        Assert.Equal((HttpStatusCode)status, result.StatusCode);
        Assert.Null(result.TransportException);
        Assert.True(fixture.Handler.ResponseContent!.Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => fixture.Handler.RequestContent!.ReadAsStringAsync());
        Assert.Equal(0, fixture.Handler.Active);
        Assert.Throws<ObjectDisposedException>(() => timeout.Cancel());
        Assert.Throws<ObjectDisposedException>(() => _ = fixture.Handler.SentToken.WaitHandle);
    }

    /// <summary>
    /// 注入 caller 與 Core 逾時來源都未取消的 TaskCanceledException；驗證這不是正常 caller 取消，
    /// 必須成為 Retryable，TransportException 保留傳輸邊界實際收到的取消例外型別，且 handler 排空。
    /// </summary>
    [Fact(DisplayName = "非 caller 取消視為可重試傳輸失敗")]
    public async Task Push_NonCallerCancellation_IsRetryableAndKeepsOriginalException()
    {
        var original = new TaskCanceledException("Synthetic transport cancellation.");
        using var fixture = new Fixture();
        using var caller = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource();
        fixture.Handler.Reply = (_, _) => throw original;
        var client = new LinePushClient(fixture.Http, _ => timeout);

        var result = await client.PushAsync(new(Token), Request(), caller.Token);

        Assert.False(caller.IsCancellationRequested);
        Assert.False(timeout.IsCancellationRequested);
        Assert.Equal(LinePushOutcome.Retryable, result.Outcome);
        Assert.NotNull(result.TransportException);
        Assert.IsType<TaskCanceledException>(result.TransportException);
        Assert.Null(result.StatusCode);
        Assert.Null(result.RequestId);
        Assert.Equal(0, fixture.Handler.Active);
        Assert.Throws<ObjectDisposedException>(() => timeout.Cancel());
    }

    /// <summary>模擬 DNS／連線類故障；傳回原始例外同一實例，不能轉文字、包裝或虛構 HTTP 狀態。</summary>
    [Fact(DisplayName = "網路失敗保留原始傳輸例外")]
    public async Task Push_NetworkFailure_IsRetryableAndKeepsOriginalException()
    {
        var original = new HttpRequestException("Synthetic network unavailable.");
        using var fixture = new Fixture();
        fixture.Handler.Reply = (_, _) => throw original;
        var result = await fixture.Client.PushAsync(new(Token), Request(), default);
        Assert.Equal(LinePushOutcome.Retryable, result.Outcome);
        Assert.False(result.Success);
        Assert.Same(original, result.TransportException);
        Assert.Null(result.StatusCode);
        Assert.Null(result.RequestId);
    }

    /// <summary>已有 HTTP 回應的各分類都不產生傳輸例外；2xx 邊界、4xx 與 5xx 使用相同規則。</summary>
    [Theory(DisplayName = "HTTP 結果不攜帶傳輸例外")]
    [InlineData(201, LinePushOutcome.Success)]
    [InlineData(204, LinePushOutcome.Success)]
    [InlineData(299, LinePushOutcome.Success)]
    [InlineData(400, LinePushOutcome.Permanent)]
    [InlineData(401, LinePushOutcome.Permanent)]
    [InlineData(403, LinePushOutcome.Permanent)]
    [InlineData(404, LinePushOutcome.Permanent)]
    [InlineData(409, LinePushOutcome.AlreadyAccepted)]
    [InlineData(429, LinePushOutcome.Retryable)]
    [InlineData(502, LinePushOutcome.Retryable)]
    [InlineData(599, LinePushOutcome.Retryable)]
    public async Task Push_HttpStatusFailure_HasNoTransportException(int status, LinePushOutcome expected)
        => await AssertStatus(status, expected);

    /// <summary>
    /// 注入安全固定訊息的原始傳輸例外，檢查 Core 不把當次敏感輸入加入結果／例外／摘要。
    /// 原始例外不得改寫；這項測試保護 Core 自產診斷，不宣稱能清洗外部自訂例外的內容。
    /// </summary>
    [Fact(DisplayName = "傳輸故障診斷不加入憑證內文或收件 ID")]
    public async Task Push_FailureDetails_NeverContainTokenOrBody()
    {
        const string body = "synthetic-private-body";
        using var fixture = new Fixture();
        var original = new HttpRequestException("Synthetic transport fault.");
        fixture.Handler.Reply = (_, _) => throw original;
        var result = await fixture.Client.PushAsync(new(Token), new(UserId, new[] { body }, RetryKey), default);
        Assert.Same(original, result.TransportException);
        var diagnostic = result + " " + result.TransportException!.Message + " " + result.TransportException;
        foreach (var forbidden in new[] { Token, body, UserId })
            Assert.DoesNotContain(forbidden, diagnostic + JsonConvert.SerializeObject(result), StringComparison.Ordinal);
    }

    /// <summary>程式錯誤不被 catch-all 轉成可重試；例外同一實例直接往最終邊界傳遞。</summary>
    [Fact(DisplayName = "非預期例外直接往外拋")]
    public async Task Push_UnexpectedException_IsNotConverted()
    {
        var original = new InvalidOperationException("Synthetic programming fault.");
        using var fixture = new Fixture();
        fixture.Handler.Reply = (_, _) => throw original;
        Assert.Same(original, await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Client.PushAsync(new(Token), Request(), default)));
        Assert.Equal(0, fixture.Handler.Active);
    }

    /// <summary>成功與失敗回應在結果發布前釋放；request 內容也已關閉，caller 的 HttpClient 仍可重用。</summary>
    [Theory(DisplayName = "回傳前釋放 HTTP 訊息且不接管外部客戶端")]
    [InlineData(200)]
    [InlineData(500)]
    public async Task Push_ResultDoesNotRetainHttpResources(int status)
    {
        using var fixture = new Fixture(status);
        var result = await fixture.Client.PushAsync(new(Token), Request(), default);
        Assert.True(fixture.Handler.ResponseContent!.Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => fixture.Handler.RequestContent!.ReadAsStringAsync());
        var fields = typeof(LinePushResult).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.DoesNotContain(fields, field => field.FieldType == typeof(HttpRequestMessage) ||
            field.FieldType == typeof(HttpResponseMessage) || field.FieldType == typeof(LineChannelCredential));
        await fixture.Client.PushAsync(new(Token), Request(), default);
        Assert.Equal(2, fixture.Handler.Calls);
        Assert.NotEqual(LinePushOutcome.Rejected, result.Outcome);
    }

    /// <summary>外部清單修改不能改變快照；每則五千與五則上限都可送出，任何 setter 必須拒絕。</summary>
    [Fact(DisplayName = "訊息集合完整複製並唯讀發布")]
    public async Task Request_MessagesAreDetachedAndBounded()
    {
        var source = new[] { new string('x', 5000), "b", "c", "d", "e" };
        var request = new LinePushRequest(UserId, source, RetryKey);
        source[0] = "mutated";
        Assert.Equal(5000, request.Messages[0].Length);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)request.Messages)[0] = "mutated");
        using var fixture = new Fixture();
        Assert.True((await fixture.Client.PushAsync(new(Token), request, default)).Success);
    }

    /// <summary>零則、六則、空文字、過長文字、空事件鍵與 null 都在 HTTP 前拒絕，不輸出輸入值。</summary>
    [Fact(DisplayName = "不合法文字數量長度或事件鍵在傳輸前拒絕")]
    public void Request_InvalidMessagesOrRetryKey_ThrowsBeforeHttp()
    {
        Assert.Throws<ArgumentException>(() => new LinePushRequest(UserId, Array.Empty<string>(), RetryKey));
        Assert.Throws<ArgumentException>(() => new LinePushRequest(UserId, new[] { "a", "b", "c", "d", "e", "f" }, RetryKey));
        Assert.Throws<ArgumentException>(() => new LinePushRequest(UserId, new[] { "" }, RetryKey));
        Assert.Throws<ArgumentException>(() => new LinePushRequest(UserId, new[] { new string('x', 5001) }, RetryKey));
        Assert.Throws<ArgumentException>(() => new LinePushRequest(UserId, new string[] { null! }, RetryKey));
        Assert.Throws<ArgumentNullException>(() => new LinePushRequest(UserId, null!, RetryKey));
        Assert.Throws<ArgumentException>(() => new LinePushRequest(UserId, new[] { "a" }, Guid.Empty));
    }

    /// <summary>標頭注入或空憑證在建構時拒絕；所有錯誤不應附帶 token 本身。</summary>
    [Theory(DisplayName = "無效憑證不進入 HTTP 標頭解析器")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("synthetic secret")]
    [InlineData("synthetic\r\nsecret")]
    public void Credential_InvalidToken_RejectsWithoutEcho(string? invalid)
    {
        var error = Assert.Throws<ArgumentException>(() => new LineChannelCredential(invalid!));
        Assert.DoesNotContain("synthetic", error.Message, StringComparison.Ordinal);
    }

    /// <summary>建立固定可重現的單人雙訊息快照，黃金檔與送訊斷言共用同一資料。</summary>
    private static LinePushRequest Request() => new(UserId, new[] { "hello", "quoted \"text\"" }, RetryKey);

    /// <summary>對實際 HTTP 分類結果斷言，不能只驗證替身自己設定的狀態。</summary>
    private static async Task AssertStatus(int status, LinePushOutcome expected)
    {
        using var fixture = new Fixture(status);
        var result = await fixture.Client.PushAsync(new(Token), Request(), default);
        Assert.Equal(expected, result.Outcome);
        Assert.Equal((HttpStatusCode)status, result.StatusCode);
        Assert.Null(result.TransportException);
        Assert.Equal(expected is LinePushOutcome.Success or LinePushOutcome.AlreadyAccepted, result.Success);
        Assert.Equal(1, fixture.Handler.Calls);
    }

    /// <summary>案例專屬 owner；只建立離線 handler，禁止落到預設網路管線，結束確認所有傳輸排空。</summary>
    private sealed class Fixture : IDisposable
    {
        /// <summary>獨佔的離線傳輸替身，不跨測試共用可變診斷。</summary>
        public Handler Handler { get; }
        /// <summary>案例擁有的客戶端；Core 只借用，不修改全域狀態或釋放此 owner。</summary>
        public HttpClient Http { get; }
        /// <summary>使用公開或友元建構路徑，兩目標的行為及測試主體完全相同。</summary>
        public LinePushClient Client => new(Http);
        /// <summary>配置固定回應狀態，不接觸系統時鐘、使用者設定或外部端點。</summary>
        public Fixture(int status = 200) { Handler = new Handler(status); Http = new HttpClient(Handler) { Timeout = Timeout.InfiniteTimeSpan }; }
        /// <summary>測試結束先驗證沒有在途操作，再由 client 釋放 handler，清除有限診斷參考。</summary>
        public void Dispose() { try { Assert.Equal(0, Handler.Active); } finally { Http.Dispose(); } }
    }

    /// <summary>可注入單次故障的 handler；診斷只保留最近一次合成輸入，owner Dispose 時清空。</summary>
    private sealed class Handler : HttpMessageHandler
    {
        /// <summary>原子在途數，驗證完成或 fault 都經 finally 排空。</summary>
        private int _active;
        /// <summary>歷史峰值，五十案例閘門驗證真正重疊而不是同步逐一執行。</summary>
        private int _peak;
        /// <summary>送出次數供拒絕輸入／無內部重試的決定性斷言。</summary>
        public int Calls;
        /// <summary>目前在途數，不應於 owner 釋放時殘留。</summary>
        public int Active => Volatile.Read(ref _active);
        /// <summary>最大在途數，只是合成測試計數。</summary>
        public int PeakActive => Volatile.Read(ref _peak);
        /// <summary>當次故障注入，回傳回應由 Core 擁有且負責釋放。</summary>
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Reply { get; set; }
        /// <summary>最近傳輸的連結 token，僅用於操作結束後的 Dispose 斷言。</summary>
        public CancellationToken SentToken { get; private set; }
        /// <summary>最近的合成 JSON 快照，不持有完整 request。</summary>
        public string? Body { get; private set; }
        /// <summary>最近合成標頭，並行驗證另直接讀每個獨立 request。</summary>
        public string? Authorization { get; private set; }
        /// <summary>最近重試鍵，沒有真實業務資料。</summary>
        public string? RetryHeader { get; private set; }
        /// <summary>最近固定 URL，用於防止端點被輸入控制。</summary>
        public string? Url { get; private set; }
        /// <summary>最近 HTTP 方法。</summary>
        public string? Method { get; private set; }
        /// <summary>最近 JSON content type，確認傳輸編碼。</summary>
        public string? ContentType { get; private set; }
        /// <summary>只為斷言釋放而暫留的 request content，Dispose 後清除參考。</summary>
        public HttpContent? RequestContent { get; private set; }
        /// <summary>只為斷言釋放而暫留的 response content，Dispose 後清除參考。</summary>
        public TrackedContent? ResponseContent { get; private set; }
        /// <summary>建立固定回應，不做任何外部連線或額外背景工作。</summary>
        public Handler(int status)
        {
            Reply = (_, _) =>
            {
                ResponseContent = new TrackedContent();
                var response = new HttpResponseMessage((HttpStatusCode)status) { Content = ResponseContent };
                response.Headers.Add("x-line-request-id", "synthetic-request-id");
                return Task.FromResult(response);
            };
        }
        /// <summary>擷取傳輸資料後執行注入行為；任何取消／例外都在 finally 歸還活躍計數。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            var active = Interlocked.Increment(ref _active);
            int previous;
            do { previous = Volatile.Read(ref _peak); if (previous >= active) break; }
            while (Interlocked.CompareExchange(ref _peak, active, previous) != previous);
            try
            {
                SentToken = cancellationToken;
                RequestContent = request.Content;
                Body = await request.Content!.ReadAsStringAsync();
                Authorization = request.Headers.Authorization?.ToString();
                RetryHeader = request.Headers.GetValues("X-Line-Retry-Key").Single();
                Url = request.RequestUri!.AbsoluteUri;
                Method = request.Method.Method;
                ContentType = request.Content.Headers.ContentType?.ToString();
                return await Reply(request, cancellationToken);
            }
            finally { Interlocked.Decrement(ref _active); }
        }
        /// <summary>外部 owner 清理時丟棄所有合成輸入參考，避免 fixture 長期留住憑證。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                RequestContent?.Dispose();
                ResponseContent?.Dispose();
                Body = Authorization = RetryHeader = Url = Method = ContentType = null;
                RequestContent = null;
                ResponseContent = null;
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>以記憶體內容追蹤回應 Dispose；若 Core 忘記釋放，即使 GC 最後回收也會測試失敗。</summary>
    private sealed class TrackedContent : StringContent
    {
        /// <summary>明確 Dispose 的決定性證據，不依賴 GC 或 Finalizer。</summary>
        public bool Disposed { get; private set; }
        /// <summary>內容故意含合成敏感文字，Core 不應讀取或把它加入錯誤結果。</summary>
        public TrackedContent() : base("synthetic-response-body", Encoding.UTF8, "application/json") { }
        /// <summary>先記錄明確釋放，再交由基底釋放記憶體串流。</summary>
        protected override void Dispose(bool disposing) { if (disposing) Disposed = true; base.Dispose(disposing); }
    }
}

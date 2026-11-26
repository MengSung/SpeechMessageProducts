/*
 * 檔案整體說明
 *
 * 目的與定位：這是 SpeechMessage.Liff.AspNetCore.Tests 的 F6 ID Token verifier 離線契約測試，
 * 直接對共通層 1.3 計畫 01 的正式產品類別執行，不以本測試代替 LINE 實機驗收。
 * 測試直接呼叫正式的 LiffIdTokenVerifier(HttpClient, IChurchClock) 與 VerifyAsync，
 * 不提供產品替身、不在測試重寫驗章、audience、到期或 JSON 欄位驗證邏輯。
 *
 * 主要功能與流程：以既有 xUnit、手寫 HttpMessageHandler 與固定 IClock 風格，
 * 經真正 ChurchClock 注入受控 UTC；供應商回覆全部在記憶體產生。保護官方 POST form、
 * exp 邊界、audience、不合法／重複 JSON、非 200、五秒逾時、正常取消及超限本文拒絕。
 * 兩個同時存在的 HttpClient 與 verifier 交錯處理各六十四筆不同合成身分，
 * 每筆核對 token／Login Channel／UserId／DisplayName／ExpiresAt，防止跨操作污染。
 *
 * 使用邊界與維護：所有秘密樣式資料都是 synthetic-canary；不讀 User Secrets，
 * 不建立 socket、產品 Host、Session、cookie、資料庫或通知。VerificationOwner 唯一擁有
 * 兩個 HttpClient、共用 handler、停止來源與至多一百二十八筆操作及合成證據；
 * verifier 只借用 client，必須自行釋放每筆 request／content／response／stream。
 * 先在 owner 仍存活時斷言產品的釋放與 active=0，再用離線 probe 證明 client 仍可用；
 * 最後 owner 取消替身、開閘、等待已登錄工作排空，才依序關閉 client 與 handler，
 * 清除請求與回覆強參照。故障清理不作為產品已正確釋放的證據。
 * client 設為無限逾時，五秒 deadline 完全由 verifier 的單次 linked source 提供，
 * 包含 ResponseHeadersRead 後的本文讀取。所有閘門與取消登錄均有界且由測試 owner 清理。
 * 本文上限為 64 KiB；只有完成來源、取消及逐筆清理斷言才視為離線通過。
 */
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SpeechMessage.Liff.AspNetCore;
using SpeechMessage.Localization;
using Xunit;

namespace SpeechMessage.Liff.AspNetCore.Tests;

/// <summary>
/// 保護 F6 的失敗即拒絕、呼叫端取消分類、隔離與借入傳輸所有權。
/// 供應商邊界由合成 handler 控制，決定性斷言在 owner 善後之前執行；
/// 本檔不重寫產品驗證邏輯，測試 owner 的保底清理不能掩蓋產品資源洩漏。
/// </summary>
public sealed class LiffIdTokenVerifierTests
{
    /// <summary>合成 A 請求的短生命週期 ID Token，只在單一測試 owner 內保留。</summary>
    private const string TokenA = "synthetic-canary-id-token-A";

    /// <summary>合成 B 請求的 ID Token，與 A 不同以偵測錯誤快取或共用 form。</summary>
    private const string TokenB = "synthetic-canary-id-token-B";

    /// <summary>公開、非真實的 Login Channel 測試值，不從部署設定取得。</summary>
    private const string ChannelA = "1000000001";

    /// <summary>另一個合成 Login Channel，不代表 Messaging Channel 或授權結果。</summary>
    private const string ChannelB = "1000000002";

    /// <summary>可被辨識的合成 A 身分，不能被錯誤訊息或 B 結果繼承。</summary>
    private const string SubjectA = "synthetic-canary-user-A";

    /// <summary>可被辨識的合成 B 身分，用於偵測同一 verifier 的前次狀態殘留。</summary>
    private const string SubjectB = "synthetic-canary-user-B";

    /// <summary>固定 UTC 瞬間；不讀程序時區或實際日期，因此到期邊界可以重現。</summary>
    private static DateTimeOffset Now => new(2026, 11, 26, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// 注入合法且未到期回覆，核對真正產品輸出的三個純值。
    /// 完成後先證明產品已釋放每筆資源，再讀 identity，保護與 response 生命週期分離的契約。
    /// </summary>
    /// <returns>完成實際 VerifyAsync 與資源／協定斷言的工作。</returns>
    [Fact]
    public async Task Verify_ValidToken_UsesOfficialPostAndDetachedIdentity()
    {
        var expires = Now.AddMinutes(10);
        var reply = Reply(TokenA, ChannelA, SubjectA, "synthetic-canary-name-A", expires);
        await using var owner = new VerificationOwner([reply]);

        var identity = await owner.Track(owner.VerifierA.VerifyAsync(
            TokenA, ChannelA, CancellationToken.None)).WaitAsync(TimeSpan.FromSeconds(8));

        await owner.AssertDrainedAsync(1);
        Assert.Equal(SubjectA, identity.UserId);
        Assert.Equal("synthetic-canary-name-A", identity.DisplayName);
        Assert.Equal(expires, identity.ExpiresAt);
    }

    /// <summary>
    /// 只改變 exp，其他欄位保持合法；等於受控現在與早一秒都必須得到驗證失敗，
    /// 不容許寬限時間或改讀系統時鐘。每案另核對原請求格式與產品釋放。
    /// </summary>
    /// <param name="secondsFromNow">相對固定 UTC 現在的秒差，零也屬到期。</param>
    /// <returns>完成拒絕與排空斷言的工作。</returns>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task Verify_ExpiredToken_Rejected(int secondsFromNow)
    {
        var reply = Reply(TokenA, ChannelA, SubjectA, null, Now.AddSeconds(secondsFromNow));
        await using var owner = new VerificationOwner([reply]);

        var failure = await Assert.ThrowsAsync<LiffVerificationException>(() =>
            owner.Track(owner.VerifierA.VerifyAsync(TokenA, ChannelA, CancellationToken.None))
                .WaitAsync(TimeSpan.FromSeconds(8)));

        AssertSanitized(failure, TokenA, SubjectA);
        Assert.False(failure.IsServiceFailure);
        await owner.AssertDrainedAsync(1);
    }

    /// <summary>
    /// exp 與 sub 均合法，僅供應商回覆的 aud 為 B，呼叫端預期仍為 A。
    /// 必須以 LiffVerificationException 拒絕，不能由其他缺欄位掩蓋 audience 檢查。
    /// </summary>
    /// <returns>完成單獨 audience 故障注入與產品釋放檢查的工作。</returns>
    [Fact]
    public async Task Verify_AudienceMismatch_Rejected()
    {
        var reply = Reply(TokenA, ChannelA, SubjectA, null, Now.AddMinutes(10)) with
        {
            Json = Claims(Now.AddMinutes(10).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                JsonSerializer.Serialize(SubjectA), JsonSerializer.Serialize(ChannelB))
        };
        await using var owner = new VerificationOwner([reply]);

        var failure = await Assert.ThrowsAsync<LiffVerificationException>(() =>
            owner.Track(owner.VerifierA.VerifyAsync(TokenA, ChannelA, CancellationToken.None))
                .WaitAsync(TimeSpan.FromSeconds(8)));

        AssertSanitized(failure, TokenA, SubjectA);
        Assert.False(failure.IsServiceFailure);
        await owner.AssertDrainedAsync(1);
    }

    /// <summary>
    /// 讓 transport 或本文讀取真的等待所收到的取消 token，不預先拋造假的 timeout。
    /// owner 的 HttpClient 不設逾時，verifier 的五秒單次 deadline 必須終止操作；
    /// 呼叫端 token 仍未取消，最終為驗證失敗且保留原始取消例外作為內部故障證據。
    /// </summary>
    /// <param name="stallBody">true 在 response stream 等待；false 在尚未回傳標頭時等待。</param>
    /// <returns>完成真正五秒 deadline 與所有已建立資源釋放檢查的工作。</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Verify_NetworkTimeout_FailsClosed(bool stallBody)
    {
        var reply = Reply(TokenA, ChannelA, SubjectA, null, Now.AddMinutes(10)) with
        {
            StallSending = !stallBody,
            StallReading = stallBody
        };
        await using var owner = new VerificationOwner([reply]);
        using var caller = new CancellationTokenSource();
        var elapsed = Stopwatch.StartNew();

        var failure = await Assert.ThrowsAsync<LiffVerificationException>(() =>
            owner.Track(owner.VerifierA.VerifyAsync(TokenA, ChannelA, caller.Token))
                .WaitAsync(TimeSpan.FromSeconds(12)));

        Assert.InRange(elapsed.Elapsed.TotalSeconds, 4, 12);
        Assert.False(caller.IsCancellationRequested);
        Assert.IsAssignableFrom<OperationCanceledException>(failure.InnerException);
        Assert.True(failure.IsServiceFailure);
        Assert.True(owner.Handler.CancellationsObserved > 0);
        AssertSanitized(failure, TokenA, SubjectA);
        await owner.AssertDrainedAsync(1);
    }

    /// <summary>
    /// 在真正 transport／stream 已進入等待後由呼叫端取消，必須傳播正常取消，
    /// 不能包裝為供應商驗證失敗。短 watchdog 只讓測試有界，並非五秒產品逾時來源。
    /// </summary>
    /// <param name="stallBody">分別覆蓋已取得 response 與尚未取得 response 的取消所有權。</param>
    /// <returns>完成呼叫端取消與排空斷言的工作。</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Verify_CallerCancellation_PropagatesAndDrains(bool stallBody)
    {
        var reply = Reply(TokenA, ChannelA, SubjectA, null, Now.AddMinutes(10)) with
        {
            StallSending = !stallBody,
            StallReading = stallBody
        };
        await using var owner = new VerificationOwner([reply]);
        using var caller = new CancellationTokenSource();
        var pending = owner.Track(owner.VerifierA.VerifyAsync(TokenA, ChannelA, caller.Token));
        await (stallBody ? owner.Handler.BodyEntered.Task : owner.Handler.AllEntered.Task)
            .WaitAsync(TimeSpan.FromSeconds(2));

        await caller.CancelAsync();
        var cancellation = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            pending.WaitAsync(TimeSpan.FromSeconds(2)));

        Assert.IsNotType<LiffVerificationException>(cancellation);
        Assert.True(caller.IsCancellationRequested);
        Assert.True(owner.Handler.CancellationsObserved > 0);
        await owner.AssertDrainedAsync(1);
    }

    /// <summary>
    /// 已取消的明確 caller token 必須立即得到正常取消且 transport 零次進入。
    /// owner 仍檢查兩個借入 client 可用，避免取消路徑順帶關閉共用依賴。
    /// </summary>
    /// <returns>完成預先取消、零外送與借用所有權斷言的工作。</returns>
    [Fact]
    public async Task Verify_AlreadyCancelledCaller_DoesNotSend()
    {
        await using var owner = new VerificationOwner(
            [Reply(TokenA, ChannelA, SubjectA, null, Now.AddMinutes(10))]);
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            owner.Track(owner.VerifierA.VerifyAsync(TokenA, ChannelA, caller.Token))
                .WaitAsync(TimeSpan.FromSeconds(2)));

        await owner.AssertDrainedAsync(0);
    }

    /// <summary>
    /// 合成非 200 回覆包含可辨識的私密錯誤與 token 回音；即使是其他 2xx 也拒絕。
    /// 最終例外不得暴露供應商本文或請求秘密，且 response／content／stream 都必須釋放。
    /// </summary>
    /// <param name="status">計畫只接受 200；其他狀態不得走成功降級。</param>
    /// <returns>完成狀態故障、訊息保密與資源檢查的工作。</returns>
    [Theory]
    [InlineData(201)]
    [InlineData(204)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task Verify_Non200_RejectedWithoutProviderBody(int status)
    {
        var reply = Reply(TokenA, ChannelA, SubjectA, null, Now.AddMinutes(10));
        reply = reply with
        {
            Status = (HttpStatusCode)status,
            // 保留有效 claims，故障只能來自 status，不能因少欄位而掩蓋漏做狀態檢查。
            Json = reply.Json[..^1] +
                ",\"error\":\"synthetic-canary-provider-private-error\",\"echo\":\"synthetic-canary-id-token-A\"}"
        };
        await using var owner = new VerificationOwner([reply]);

        var failure = await Assert.ThrowsAsync<LiffVerificationException>(() =>
            owner.Track(owner.VerifierA.VerifyAsync(TokenA, ChannelA, CancellationToken.None))
                .WaitAsync(TimeSpan.FromSeconds(8)));

        AssertSanitized(failure, TokenA, "synthetic-canary-provider-private-error");
        Assert.Equal(status is not (400 or 401), failure.IsServiceFailure);
        if (failure.IsServiceFailure)
        {
            var transport = Assert.IsType<HttpRequestException>(failure.InnerException);
            Assert.Equal((HttpStatusCode)status, transport.StatusCode);
        }
        Assert.Equal(0, Assert.Single(owner.Handler.Snapshot()).Response!.Body.BytesRead);
        await owner.AssertDrainedAsync(1);
    }

    /// <summary>
    /// 只改變 UTF-8 本文總長度，在精確 64 KiB 時應成功、多一 byte 就拒絕；
    /// 同時覆蓋已知及未知長度，避免 Content-Length 與串流分支採取不同安全邊界。
    /// owner 善後之前檢查處置，精確上限不是由正式實作常數計算預期值。
    /// </summary>
    /// <param name="advertiseLength">是否在傳輸標頭提供長度。</param>
    /// <param name="extraByte">零為合法邊界，一為超限。</param>
    /// <returns>完成正式驗證與資源排空斷言的工作。</returns>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public async Task Verify_64KiBBoundary_Enforced(bool advertiseLength, int extraByte)
    {
        var reply = Reply(TokenA, ChannelA, SubjectA, null, Now.AddMinutes(10));
        var prefix = reply.Json[..^1] + ",\"padding\":\"";
        var padding = 65_536 + extraByte - Encoding.UTF8.GetByteCount(prefix) - 2;
        reply = reply with { Json = prefix + new string('x', padding) + "\"}", AdvertiseLength = advertiseLength };
        await using var owner = new VerificationOwner([reply]);

        if (extraByte == 0)
        {
            var identity = await owner.Track(owner.VerifierA.VerifyAsync(TokenA, ChannelA, CancellationToken.None));
            Assert.Equal(SubjectA, identity.UserId);
        }
        else
        {
            await Assert.ThrowsAsync<LiffVerificationException>(() =>
                owner.Track(owner.VerifierA.VerifyAsync(TokenA, ChannelA, CancellationToken.None)));
        }
        await owner.AssertDrainedAsync(1);
    }

    /// <summary>
    /// 合成 transport 與本文讀取分別拋出同一個原始物件，最終驗證例外必須保留其身分，
    /// 讓 HTTP filter 能把真正故障交給共用 diagnostics；不能為了保密改成只保留字串。
    /// 本案故障訊息只有固定文字，不把原例外直接輸出到記錄或通知。
    /// </summary>
    /// <param name="duringRead">true 為取得 response 後的 stream 故障。</param>
    /// <returns>完成原物件保留、分類及處置斷言的工作。</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Verify_TransportFailure_PreservesOriginalException(bool duringRead)
    {
        Exception fault = duringRead
            ? new IOException("Synthetic body read failed.")
            : new HttpRequestException("Synthetic transport failed.");
        var reply = Reply(TokenA, ChannelA, SubjectA, null, Now.AddMinutes(10)) with
        {
            SendingFailure = duringRead ? null : fault,
            ReadingFailure = duringRead ? fault : null
        };
        await using var owner = new VerificationOwner([reply]);

        var failure = await Assert.ThrowsAsync<LiffVerificationException>(() =>
            owner.Track(owner.VerifierA.VerifyAsync(TokenA, ChannelA, CancellationToken.None)));

        Assert.True(failure.IsServiceFailure);
        Assert.Same(fault, failure.InnerException);
        await owner.AssertDrainedAsync(1);
    }

    /// <summary>
    /// 空白 token 或部署 Channel 必須在外送前拒絕，且不把呼叫值附加到固定錯誤訊息；
    /// 零 request 與仍可使用的借入 client 保護設定缺漏時的失敗邊界。
    /// </summary>
    /// <param name="token">合成無效或合法 token。</param>
    /// <param name="channel">合成無效或合法部署 Channel。</param>
    /// <returns>完成零外送及安全拒絕斷言的工作。</returns>
    [Theory]
    [InlineData("", ChannelA)]
    [InlineData("   ", ChannelA)]
    [InlineData(TokenA, "")]
    [InlineData(TokenA, "   ")]
    public async Task Verify_EmptyInput_RejectsWithoutSending(string token, string channel)
    {
        await using var owner = new VerificationOwner([Reply(TokenA, ChannelA, SubjectA, null, Now.AddMinutes(10))]);

        var failure = await Assert.ThrowsAsync<LiffVerificationException>(() =>
            owner.Track(owner.VerifierA.VerifyAsync(token, channel, CancellationToken.None)));

        Assert.False(failure.IsServiceFailure);
        await owner.AssertDrainedAsync(0);
    }

    /// <summary>
    /// 每筆只注入明列的 JSON 故障，直接交給正式 VerifyAsync；
    /// 替身不解析 claims 或自行決定驗證結果。同值重複欄位也必須拒絕。
    /// </summary>
    /// <param name="caseName">可見的合成故障名稱，不含任何外部輸入。</param>
    /// <param name="json">送入正式解析器的不可信供應商本文。</param>
    /// <returns>完成 JSON 拒絕、保密與產品資源釋放檢查的工作。</returns>
    [Theory]
    [MemberData(nameof(InvalidResponses))]
    public async Task Verify_MalformedOrAmbiguousJson_Rejected(string caseName, string json)
    {
        Assert.False(string.IsNullOrWhiteSpace(caseName));
        var reply = Reply(TokenA, ChannelA, SubjectA, null, Now.AddMinutes(10)) with { Json = json };
        await using var owner = new VerificationOwner([reply]);

        var failure = await Assert.ThrowsAsync<LiffVerificationException>(() =>
            owner.Track(owner.VerifierA.VerifyAsync(TokenA, ChannelA, CancellationToken.None))
                .WaitAsync(TimeSpan.FromSeconds(8)));

        AssertSanitized(failure, TokenA, SubjectA);
        await owner.AssertDrainedAsync(1);
    }

    /// <summary>
    /// 供應有效 claims 加上一 MiB padding，分別提供已知與未知 Content-Length。
    /// 必須在完整讀入超限內容之前拒絕，保護串流路徑不能靠無界先緩衝再檢查長度。
    /// 精確上限另以 64 KiB 邊界案例保護，此案還要求提前終止串流。
    /// </summary>
    /// <param name="advertiseLength">false 模擬未公布本文長度的供應商回覆。</param>
    /// <returns>完成超限拒絕、提前中止與資源排空斷言的工作。</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Verify_OversizedPayload_RejectedBeforeReadingAll(bool advertiseLength)
    {
        var reply = Reply(TokenA, ChannelA, SubjectA, null, Now.AddMinutes(10));
        reply = reply with
        {
            Json = reply.Json[..^1] + ",\"padding\":\"" + new string('x', 1024 * 1024) + "\"}",
            AdvertiseLength = advertiseLength
        };
        await using var owner = new VerificationOwner([reply]);

        var failure = await Assert.ThrowsAsync<LiffVerificationException>(() =>
            owner.Track(owner.VerifierA.VerifyAsync(TokenA, ChannelA, CancellationToken.None))
                .WaitAsync(TimeSpan.FromSeconds(8)));

        AssertSanitized(failure, TokenA, SubjectA);
        var response = Assert.Single(owner.Handler.Snapshot()).Response;
        Assert.NotNull(response);
        Assert.True(response.Body.BytesRead < Encoding.UTF8.GetByteCount(reply.Json),
            "超限回覆必須在完整緩衝之前中止讀取。");
        await owner.AssertDrainedAsync(1);
    }

    /// <summary>
    /// 同一 verifier 先驗 A 的姓名、再驗 B 的 null 姓名；第二筆必須完全反映本次回覆，
    /// 不可從前次身分、快取或 mutable DTO 補入姓名。兩筆皆依真實輸出與釋放狀態判定。
    /// </summary>
    /// <returns>完成同一 client 跨範圍順序呼叫與排空檢查的工作。</returns>
    [Fact]
    public async Task Verify_SequentialScopes_DoesNotRetainPreviousIdentity()
    {
        var expiresA = Now.AddMinutes(10);
        var expiresB = Now.AddMinutes(20);
        await using var owner = new VerificationOwner(
        [
            Reply(TokenA, ChannelA, SubjectA, "synthetic-canary-name-A", expiresA),
            Reply(TokenB, ChannelB, SubjectB, null, expiresB)
        ]);

        var first = await owner.Track(owner.VerifierA.VerifyAsync(TokenA, ChannelA, CancellationToken.None));
        var second = await owner.Track(owner.VerifierA.VerifyAsync(TokenB, ChannelB, CancellationToken.None));

        Assert.Equal(SubjectA, first.UserId);
        Assert.Equal("synthetic-canary-name-A", first.DisplayName);
        Assert.Equal(expiresA, first.ExpiresAt);
        Assert.Equal(SubjectB, second.UserId);
        Assert.Null(second.DisplayName);
        Assert.Equal(expiresB, second.ExpiresAt);
        await owner.AssertDrainedAsync(2);
    }

    /// <summary>
    /// 兩個 client 共用只做合成回覆的 transport，各發六十四筆、依 A/B 順序交錯進入，
    /// 一百二十八筆全部到閘門後才放行，證明確有重疊而非單純序列迴圈。
    /// 每筆 token、sub、姓名、到期都不同，A/B 各用固定且不同的 Channel；完成前後核對 default headers，
    /// 排空後檢查所有資源。owner 的 finally 負責取消並等待任何失敗的待完成工作。
    /// </summary>
    /// <returns>完成全部 A/B 真實 VerifyAsync 與隔離／排空斷言的工作。</returns>
    [Fact]
    public async Task Verify_Concurrent64Plus64_MapsEveryResponseAndDrains()
    {
        var replies = new List<SyntheticReply>(128);
        for (var index = 0; index < 64; index++)
        {
            var suffix = index.ToString("D2", CultureInfo.InvariantCulture);
            replies.Add(Reply(TokenA + "-" + suffix, ChannelA, SubjectA + "-" + suffix,
                "synthetic-canary-name-A-" + suffix, Now.AddMinutes(10).AddSeconds(index)));
            replies.Add(Reply(TokenB + "-" + suffix, ChannelB, SubjectB + "-" + suffix,
                "synthetic-canary-name-B-" + suffix, Now.AddMinutes(20).AddSeconds(index)));
        }
        await using var owner = new VerificationOwner(replies, expectedConcurrent: 128);
        var pending = new List<Task<LiffIdentity>>(128);
        foreach (var reply in replies)
        {
            var verifier = reply.Channel == ChannelA ? owner.VerifierA : owner.VerifierB;
            pending.Add(owner.Track(verifier.VerifyAsync(reply.Token, reply.Channel, CancellationToken.None)));
        }

        await owner.Handler.AllEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(128, owner.Handler.Active);
        owner.AssertDefaultHeadersUnchanged();
        owner.Handler.Release.TrySetResult();
        var identities = await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(8));

        for (var index = 0; index < identities.Length; index++)
        {
            var number = index / 2;
            var suffix = number.ToString("D2", CultureInfo.InvariantCulture);
            var isA = index % 2 == 0;
            Assert.Equal((isA ? SubjectA : SubjectB) + "-" + suffix, identities[index].UserId);
            Assert.Equal("synthetic-canary-name-" + (isA ? "A-" : "B-") + suffix,
                identities[index].DisplayName);
            Assert.Equal(Now.AddMinutes(isA ? 10 : 20).AddSeconds(number), identities[index].ExpiresAt);
        }
        Assert.Equal(128, identities.Select(identity => identity.UserId).Distinct(StringComparer.Ordinal).Count());
        await owner.AssertDrainedAsync(128);
    }

    /// <summary>
    /// 以固定合法 exp／sub／aud 為控制組，產出單一故障及重複欄位向量；
    /// 字串組裝只產生供應商測試資料，沒有任何產品驗證或接受／拒絕邏輯。
    /// </summary>
    /// <returns>有限、可重現且只含 synthetic-canary 的名稱與 JSON 本文。</returns>
    public static IEnumerable<object[]> InvalidResponses()
    {
        var exp = Now.AddMinutes(10).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var sub = JsonSerializer.Serialize(SubjectA);
        var aud = JsonSerializer.Serialize(ChannelA);
        var valid = Claims(exp, sub, aud);
        yield return ["duplicate-exp-same", valid[..^1] + ",\"exp\":" + exp + "}"];
        yield return ["duplicate-exp-different", valid[..^1] + ",\"exp\":0}"];
        yield return ["duplicate-sub-same", valid[..^1] + ",\"sub\":" + sub + "}"];
        yield return ["duplicate-sub-different", valid[..^1] + ",\"sub\":\"synthetic-canary-other\"}"];
        yield return ["duplicate-aud-same", valid[..^1] + ",\"aud\":" + aud + "}"];
        yield return ["duplicate-aud-different", valid[..^1] + ",\"aud\":\"1000000002\"}"];
        yield return ["duplicate-name-same", valid[..^1] + ",\"name\":\"x\",\"name\":\"x\"}"];
        yield return ["duplicate-unknown-same", valid[..^1] + ",\"extra\":1,\"extra\":1}"];
        yield return ["duplicate-nested-property", valid[..^1] + ",\"extra\":{\"k\":1,\"k\":2}}"];
        yield return ["duplicate-escaped-sub", valid[..^1] + ",\"\\u0073ub\":" + sub + "}"];
        foreach (var invalid in new[] { "true", "123", "[]", "{}" })
            yield return ["name-invalid-" + invalid, valid[..^1] + ",\"name\":" + invalid + "}"];
        foreach (var invalid in new[] { "\"" + exp + "\"", exp + ".5", "null", "true", "[]", "{}",
                     "9223372036854775808", "9223372036854775807", "1e100" })
            yield return ["exp-type-or-overflow-" + invalid, Claims(invalid, sub, aud)];
        foreach (var invalid in new[] { "\"\"", "\"   \"", "null", "true", "123", "[]", "{}" })
            yield return ["sub-invalid-" + invalid, Claims(exp, invalid, aud)];
        foreach (var invalid in new[] { "\"\"", "\"   \"", "null", "true", "123", "[]", "{}" })
            yield return ["aud-invalid-" + invalid, Claims(exp, sub, invalid)];
        yield return ["missing-exp", valid.Replace(",\"exp\":" + exp, string.Empty, StringComparison.Ordinal)];
        yield return ["missing-sub", valid.Replace(",\"sub\":" + sub, string.Empty, StringComparison.Ordinal)];
        yield return ["missing-aud", valid.Replace(",\"aud\":" + aud, string.Empty, StringComparison.Ordinal)];
        yield return ["root-array", "[" + valid + "]"];
        yield return ["root-null", "null"];
        yield return ["root-string", "\"synthetic-canary-not-an-object\""];
        yield return ["truncated-json", valid[..^1]];
        yield return ["non-json", "<html>synthetic-canary-provider-private-error</html>"];
    }

    /// <summary>建立合法合成回覆；只序列化固定輸入，不模擬產品的 token 驗證。</summary>
    /// <param name="token">handler 路由使用的合成 token。</param>
    /// <param name="channel">預期的 form client_id，也是此回覆的 aud。</param>
    /// <param name="subject">供應商回傳的合成 sub。</param>
    /// <param name="displayName">本次回覆姓名，可為 null。</param>
    /// <param name="expires">本次回覆的獨立 UTC 到期值。</param>
    /// <returns>沒有資源所有權的不可變合成回覆描述。</returns>
    private static SyntheticReply Reply(string token, string channel, string subject,
        string? displayName, DateTimeOffset expires) => new(token, channel,
        JsonSerializer.Serialize(new
        {
            iss = "https://access.line.me",
            sub = subject,
            aud = channel,
            exp = expires.ToUnixTimeSeconds(),
            iat = Now.AddMinutes(-1).ToUnixTimeSeconds(),
            name = displayName
        }));

    /// <summary>逐字嵌入故障欄位片段，保留重複／非法型別，不經會覆寫重複鍵的字典。</summary>
    /// <param name="exp">完整的 exp JSON 值片段。</param>
    /// <param name="sub">完整的 sub JSON 值片段。</param>
    /// <param name="aud">完整的 aud JSON 值片段。</param>
    /// <returns>只有固定 issuer、iat 及三個待測欄位的 JSON 物件。</returns>
    private static string Claims(string exp, string sub, string aud) =>
        "{\"iss\":\"https://access.line.me\",\"iat\":" +
        Now.AddMinutes(-1).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) +
        ",\"exp\":" + exp + ",\"sub\":" + sub + ",\"aud\":" + aud + "}";

    /// <summary>核對最終故障連同內部例外均不含可辨識的 token 或供應商私密片段。</summary>
    /// <param name="failure">真正產品 VerifyAsync 拋出的最終失敗。</param>
    /// <param name="token">不得出現在錯誤中的合成秘密。</param>
    /// <param name="privateDetail">不得出現在錯誤中的合成身分或供應商本文。</param>
    private static void AssertSanitized(Exception failure, string token, string privateDetail)
    {
        Assert.DoesNotContain(token, failure.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(privateDetail, failure.ToString(), StringComparison.Ordinal);
    }

    /// <summary>沿用 Localization 既有手寫固定 IClock 風格，只存純 UTC 值，沒有 timer 或可變共享狀態。</summary>
    private sealed class FixedClock : IClock
    {
        /// <summary>每次回固定瞬間，真正 ChurchClock 負責提供實際 IChurchClock 介面。</summary>
        public DateTimeOffset UtcNow => Now;
    }

    /// <summary>一筆固定供應商回覆與故障注入設定；生命週期只到單一 owner 清理。</summary>
    /// <param name="Token">handler 對應用的合成 token，不解碼或驗章。</param>
    /// <param name="Channel">此 token 預期的 form client_id，用於事後協定斷言。</param>
    /// <param name="Json">逐字送入真正產品解析器的合成本體。</param>
    /// <param name="Status">預設 200，狀態故障案例可覆寫。</param>
    /// <param name="StallSending">是否等傳輸取消才終止，不預造 timeout。</param>
    /// <param name="StallReading">是否等本文讀取取消才終止。</param>
    /// <param name="AdvertiseLength">是否提供已知長度，以覆蓋未知長度的有界讀取。</param>
    /// <param name="SendingFailure">本筆傳輸的原始合成故障，不跨案例共享。</param>
    /// <param name="ReadingFailure">本筆本文讀取的原始合成故障，處置仍由 response owner 負責。</param>
    private sealed record SyntheticReply(string Token, string Channel, string Json,
        HttpStatusCode Status = HttpStatusCode.OK, bool StallSending = false,
        bool StallReading = false, bool AdvertiseLength = true,
        Exception? SendingFailure = null, Exception? ReadingFailure = null);

    /// <summary>
    /// 每案唯一資源 owner；最多登錄 128 個真正產品工作，兩個 client 借用同一個純合成 handler。
    /// 先斷言產品所有權，再在 DisposeAsync 取消、排空、關閉與清除；不保存跨測試狀態。
    /// </summary>
    private sealed class VerificationOwner : IAsyncDisposable
    {
        /// <summary>只由安排案例的單一執行緒登錄；至多 128 筆，排空後清除。</summary>
        private readonly List<Task<LiffIdentity>> operations = new(128);
        /// <summary>兩個合成 client 的原始 default headers，保留期只到本案例結束。</summary>
        private readonly string[] initialHeaders;
        /// <summary>owner 唯一擁有的 A client；verifier 不得關閉或變更共用認證預設。</summary>
        private readonly HttpClient clientA;
        /// <summary>owner 唯一擁有的 B client；與 A 同時存在以偵測共用可變狀態。</summary>
        private readonly HttpClient clientB;

        /// <summary>建立離線 transport、無限逾時 client 與真正產品 verifier；不用 DI 或環境設定。</summary>
        /// <param name="replies">最多 128 筆、token 各不相同的合成回覆。</param>
        /// <param name="expectedConcurrent">大於零時先等全部進入再由案例放行。</param>
        internal VerificationOwner(IEnumerable<SyntheticReply> replies, int expectedConcurrent = 0)
        {
            Handler = new ObservedHandler(replies, expectedConcurrent);
            clientA = new HttpClient(Handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
            clientB = new HttpClient(Handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
            clientA.DefaultRequestHeaders.Add("X-Synthetic-Canary", "owner-A");
            clientB.DefaultRequestHeaders.Add("X-Synthetic-Canary", "owner-B");
            initialHeaders = [Headers(clientA), Headers(clientB)];
            IChurchClock clock = new ChurchClock(new FixedClock());
            try
            {
                VerifierA = new LiffIdTokenVerifier(clientA, clock);
                VerifierB = new LiffIdTokenVerifier(clientB, clock);
            }
            catch
            {
                // 建構失敗時 await using 尚未接管 owner，必須由此釋放已建立的依賴。
                clientA.Dispose();
                clientB.Dispose();
                Handler.Dispose();
                throw;
            }
        }

        /// <summary>owner 擁有的唯一合成 transport，提供進入、取消與排空證據。</summary>
        internal ObservedHandler Handler { get; }
        /// <summary>借用 A client 的真正產品型別，不由測試代作任何驗證。</summary>
        internal LiffIdTokenVerifier VerifierA { get; }
        /// <summary>借用 B client 的真正產品型別，生命週期限定於本案。</summary>
        internal LiffIdTokenVerifier VerifierB { get; }

        /// <summary>只登錄已直接呼叫的真正 VerifyAsync 工作供故障善後；不改結果、例外或 token。</summary>
        /// <param name="operation">由案例直接呼叫產品取得的工作。</param>
        /// <returns>原工作本身，沒有包裝驗證邏輯。</returns>
        internal Task<LiffIdentity> Track(Task<LiffIdentity> operation)
        {
            Assert.True(operations.Count < 128, "測試 owner 的工作保留上限是 128。");
            operations.Add(operation);
            return operation;
        }

        /// <summary>逐項比對兩個 client 全部 default headers 與 timeout，不只檢查 Authorization。</summary>
        internal void AssertDefaultHeadersUnchanged()
        {
            Assert.Equal(initialHeaders[0], Headers(clientA));
            Assert.Equal(initialHeaders[1], Headers(clientB));
            Assert.Null(clientA.DefaultRequestHeaders.Authorization);
            Assert.Null(clientB.DefaultRequestHeaders.Authorization);
            Assert.Equal(Timeout.InfiniteTimeSpan, clientA.Timeout);
            Assert.Equal(Timeout.InfiniteTimeSpan, clientB.Timeout);
        }

        /// <summary>
        /// owner 尚未善後時核對每筆協定、request/content 的實際已處置狀態及 response/stream 計數。
        /// 最後用離線 probe 證明兩個借入 client 都未被產品關閉；probe 不算 verifier 請求。
        /// </summary>
        /// <param name="expectedCalls">真正驗證操作應進入 handler 的精確次數。</param>
        /// <returns>完成資源可觀察性與借用 client 可用性檢查的工作。</returns>
        internal async Task AssertDrainedAsync(int expectedCalls)
        {
            var exchanges = Handler.Snapshot();
            Assert.Equal(expectedCalls, exchanges.Length);
            Assert.Equal(0, Handler.Active);
            Assert.Equal(0, Handler.DisposeCalls);
            foreach (var exchange in exchanges)
            {
                var expected = exchange.Expected;
                Assert.NotNull(expected);
                Assert.Equal(HttpMethod.Post, exchange.Method);
                Assert.Equal("https://api.line.me/oauth2/v2.1/verify", exchange.Uri?.AbsoluteUri);
                Assert.Equal("application/x-www-form-urlencoded", exchange.MediaType);
                Assert.DoesNotContain(expected.Token, exchange.Uri!.AbsoluteUri, StringComparison.Ordinal);
                Assert.DoesNotContain(expected.Token, exchange.HeaderText, StringComparison.Ordinal);
                Assert.Null(exchange.Authorization);
                Assert.Collection(exchange.Form.OrderBy(pair => pair.Key, StringComparer.Ordinal),
                    pair => { Assert.Equal("client_id", pair.Key); Assert.Equal(expected.Channel, pair.Value); },
                    pair => { Assert.Equal("id_token", pair.Key); Assert.Equal(expected.Token, pair.Value); });
                Assert.Throws<ObjectDisposedException>(() => exchange.Request.Method = HttpMethod.Post);
                Assert.NotNull(exchange.RequestContent);
                await Assert.ThrowsAsync<ObjectDisposedException>(() => exchange.RequestContent.ReadAsByteArrayAsync());
                if (exchange.Response is { } response)
                {
                    Assert.Equal(1, response.DisposeCalls);
                    Assert.Equal(1, response.ContentProbe.DisposeCalls);
                    Assert.Equal(1, response.Body.DisposeCalls);
                    Assert.Equal(0, response.Body.ActiveReads);
                }
            }
            AssertDefaultHeadersUnchanged();
            Assert.Equal(0, Handler.ActiveResources);
            using var probeRequestA = new HttpRequestMessage(HttpMethod.Get, ObservedHandler.ProbeUri);
            using var probeRequestB = new HttpRequestMessage(HttpMethod.Get, ObservedHandler.ProbeUri);
            using var probeA = await clientA.SendAsync(probeRequestA)
                .WaitAsync(TimeSpan.FromSeconds(2));
            using var probeB = await clientB.SendAsync(probeRequestB)
                .WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(HttpStatusCode.NoContent, probeA.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, probeB.StatusCode);
            Assert.Equal(2, Handler.ProbeCalls);
        }

        /// <summary>將全部 default headers 轉成順序固定的合成快照，不記錄到外部日誌。</summary>
        /// <param name="client">owner 擁有的 client。</param>
        /// <returns>僅在案例中比對的短字串。</returns>
        private static string Headers(HttpClient client) => string.Join("\n",
            client.DefaultRequestHeaders.OrderBy(header => header.Key, StringComparer.Ordinal)
                .Select(header => header.Key + ":" + string.Join(",", header.Value)));

        /// <summary>
        /// 無論斷言成功與否都先取消替身並開閘，再觀察全部已登錄工作完成；
        /// 已由案例斷言的產品失敗只在善後觀察，不覆蓋原始測試結果。
        /// 兩秒內不能排空會作為真正善後失敗回報，仍以 finally 關閉 owner 資源。
        /// </summary>
        /// <returns>完成有界 drain 與資源關閉的清理工作。</returns>
        public async ValueTask DisposeAsync()
        {
            try
            {
                Handler.Stop();
                var drain = Task.WhenAll(operations);
                try { await drain.WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (Exception) when (drain.IsCompleted)
                {
                    // 只觀察原先工作已完成的失敗；未排空的 watchdog 例外不能被吞掉。
                }
            }
            finally
            {
                clientA.Dispose();
                clientB.Dispose();
                Handler.Dispose();
                operations.Clear();
            }
        }
    }

    /// <summary>
    /// 保存一筆合成 request 的借入參照與送出前快照供事後斷言；最多 128 筆。
    /// Response 由同一 SendAsync 在回傳前設定；清理只由外層 owner 統一執行。
    /// </summary>
    /// <param name="Request">產品建立、應由產品處置的 request。</param>
    /// <param name="RequestContent">原始 form content，不替換或代為包裝以掩蓋產品所有權。</param>
    /// <param name="Method">送出時的 HTTP method 純值。</param>
    /// <param name="Uri">送出時的 URI，不應含秘密。</param>
    /// <param name="MediaType">送出時的 form media type。</param>
    /// <param name="Form">保留重複鍵的表單快照，不能用字典覆寫。</param>
    /// <param name="HeaderText">僅合成資料的 request/content header 快照。</param>
    /// <param name="Authorization">實際送出時的 Authorization，應為 null。</param>
    /// <param name="Expected">按合成 token 查得的固定回覆描述，並非驗證結果。</param>
    private sealed record Exchange(HttpRequestMessage Request, HttpContent? RequestContent,
        HttpMethod Method, Uri? Uri, string? MediaType, IReadOnlyList<KeyValuePair<string, string>> Form,
        string HeaderText, AuthenticationHeaderValue? Authorization, SyntheticReply? Expected)
    {
        /// <summary>本次已建立的 response；傳輸階段逾時時保持 null。</summary>
        internal ObservedResponse? Response { get; set; }
    }

    /// <summary>
    /// 完全離線、上限 128 筆的 transport owner；只讀 form 決定哪個預設回覆，從不驗證 claims。
    /// 每次 SendAsync 有局部 linked token，離開時即 Dispose；停止來源只活到案例結束。
    /// </summary>
    private sealed class ObservedHandler : HttpMessageHandler
    {
        /// <summary>離線借用所有權 probe，僅由本 handler 回 NoContent，永不建立實際網路。</summary>
        internal const string ProbeUri = "https://example.invalid/synthetic-canary-client-owner";
        /// <summary>保護有界證據集合；鎖內沒有 await 或網路工作。</summary>
        private readonly object sync = new();
        /// <summary>至多 128 筆固定合成資料，Dispose 清除；不可用於驗證產品 token。</summary>
        private readonly Dictionary<string, SyntheticReply> replies = new(StringComparer.Ordinal);
        /// <summary>至多 128 筆 request/response 強參照，產品斷言後由 owner 清除。</summary>
        private readonly List<Exchange> exchanges = new(128);
        /// <summary>owner 在失敗善後取消所有替身等待；Dispose 後釋放來源。</summary>
        private readonly CancellationTokenSource stopping = new();
        /// <summary>大於零時需要全部到場才開閘，防止假並行。</summary>
        private readonly int expectedConcurrent;
        /// <summary>目前進入 SendAsync 且尚未離開的操作數。</summary>
        private int active;
        /// <summary>真正收到取消的 transport 或本文讀取次數。</summary>
        private int cancellationsObserved;
        /// <summary>handler 的第一次清理計數，用以在產品斷言前偵測越權關閉。</summary>
        private int disposeCalls;
        /// <summary>client 可用性 probe 的次數，不混入 verifier 請求。</summary>
        private int probeCalls;

        /// <summary>複製有界合成描述並設定閘門，不接觸真實設定或遠端。</summary>
        /// <param name="source">每個 token 唯一且最多 128 筆的回覆。</param>
        /// <param name="expectedConcurrent">零為立即放行；否則等待案例明確放行。</param>
        internal ObservedHandler(IEnumerable<SyntheticReply> source, int expectedConcurrent)
        {
            this.expectedConcurrent = expectedConcurrent;
            foreach (var reply in source)
            {
                if (replies.Count >= 128) throw new ArgumentOutOfRangeException(nameof(source));
                replies.Add(reply.Token, reply);
            }
            if (expectedConcurrent == 0) Release.TrySetResult();
        }

        /// <summary>全部指定請求已到場的局部訊號，不建立背景執行緒。</summary>
        internal TaskCompletionSource AllEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>本文已開始讀取的局部訊號，用於精確取消注入。</summary>
        internal TaskCompletionSource BodyEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>案例或 owner 停止時開閘，等待本身接收可取消 token。</summary>
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>跨執行緒可觀察的 SendAsync 存量。</summary>
        internal int Active => Volatile.Read(ref active);
        /// <summary>跨執行緒可觀察的取消次數。</summary>
        internal int CancellationsObserved => Volatile.Read(ref cancellationsObserved);
        /// <summary>產品驗證期間應為零，只有測試 owner 可關閉 handler。</summary>
        internal int DisposeCalls => Volatile.Read(ref disposeCalls);
        /// <summary>確認兩個借入 client 仍可使用的離線 probe 次數。</summary>
        internal int ProbeCalls => Volatile.Read(ref probeCalls);
        /// <summary>傳輸、response、content、stream 與讀取工作的總未排空數；斷言時必須為零。</summary>
        internal int ActiveResources => Active + Snapshot().Sum(exchange =>
            exchange.Response is not { } response ? 0 :
                (response.DisposeCalls == 0 ? 1 : 0) +
                (response.ContentProbe.DisposeCalls == 0 ? 1 : 0) +
                (response.Body.DisposeCalls == 0 ? 1 : 0) + response.Body.ActiveReads);

        /// <summary>回傳有界集合複本，讓斷言不持有鎖或讀取正在擴充的 List。</summary>
        /// <returns>只活到本案結束的合成證據陣列。</returns>
        internal Exchange[] Snapshot() { lock (sync) return exchanges.ToArray(); }

        /// <summary>由唯一 owner 呼叫，先取消所有等待再開閘；不直接偽造產品已完成清理。</summary>
        internal void Stop()
        {
            stopping.Cancel();
            Release.TrySetResult();
        }

        /// <summary>
        /// 讀取原始 form 並保留重複鍵，按合成 token 回固定資料；
        /// 協定斷言留在產品呼叫完成後執行，避免 SUT 包裝替身內的 Assert 而造成假通過。
        /// </summary>
        /// <param name="request">產品建立且應自行處置的原始 request。</param>
        /// <param name="cancellationToken">真正 HttpClient／verifier 轉送的操作取消。</param>
        /// <returns>可觀察處置的合成 response，或真實取消例外。</returns>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsoluteUri == ProbeUri)
            {
                Interlocked.Increment(ref probeCalls);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            Interlocked.Increment(ref active);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping.Token);
            try
            {
                var content = request.Content;
                var body = content is null ? string.Empty : await content.ReadAsStringAsync(linked.Token);
                var form = body.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(part =>
                {
                    var split = part.Split('=', 2);
                    return new KeyValuePair<string, string>(Decode(split[0]),
                        split.Length == 2 ? Decode(split[1]) : string.Empty);
                }).ToArray();
                var token = form.FirstOrDefault(pair => pair.Key == "id_token").Value;
                replies.TryGetValue(token ?? string.Empty, out var reply);
                var exchange = new Exchange(request, content, request.Method, request.RequestUri,
                    content?.Headers.ContentType?.MediaType, form,
                    request.Headers + "\n" + content?.Headers, request.Headers.Authorization, reply);
                lock (sync)
                {
                    if (exchanges.Count >= 128) throw new InvalidOperationException("Synthetic fixture capacity exceeded.");
                    exchanges.Add(exchange);
                    if (exchanges.Count >= Math.Max(1, expectedConcurrent)) AllEntered.TrySetResult();
                }
                await Release.Task.WaitAsync(linked.Token);
                if (reply is null) throw new InvalidOperationException("Synthetic fixture has no reply for this form.");
                if (reply.StallSending) await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
                if (reply.SendingFailure is { } fault) throw fault;
                var response = new ObservedResponse(reply, BodyEntered,
                    () => Interlocked.Increment(ref cancellationsObserved), stopping.Token);
                exchange.Response = response;
                return response;
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref cancellationsObserved);
                throw;
            }
            finally { Interlocked.Decrement(ref active); }
        }

        /// <summary>只解碼 form encoding；不解碼 JWT、不判定 audience 或到期。</summary>
        /// <param name="value">單一表單欄位的原始編碼。</param>
        /// <returns>可與合成描述逐字比對的值。</returns>
        private static string Decode(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));

        /// <summary>
        /// owner 已排空後的保底清理，關閉產品可能遺漏的合成資源並清除強參照。
        /// 此路徑在 AssertDrainedAsync 之後，不能拿其計數替產品宣稱已正確清理。
        /// </summary>
        /// <param name="disposing">true 才執行 managed owner 清理。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.CompareExchange(ref disposeCalls, 1, 0) == 0)
            {
                stopping.Cancel();
                Release.TrySetResult();
                foreach (var exchange in Snapshot())
                {
                    exchange.Request.Dispose();
                    exchange.RequestContent?.Dispose();
                    exchange.Response?.Dispose();
                }
                lock (sync) exchanges.Clear();
                replies.Clear();
                stopping.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>每筆獨有的 response owner；只第一次 Dispose 計數，並讓基底確定性關閉 content。</summary>
    private sealed class ObservedResponse : HttpResponseMessage
    {
        /// <summary>第一次關閉的原子標記，避免重複 Dispose 讓資源計數失真。</summary>
        private int disposeCalls;

        /// <summary>建立本筆唯一的 content 與 stream，沒有 socket 或外部資料。</summary>
        /// <param name="reply">固定合成回覆與故障設定。</param>
        /// <param name="stopping">只在測試失敗善後時取消的 owner token。</param>
        /// <param name="bodyEntered">第一個本文讀取開始的訊號。</param>
        /// <param name="onCancellation">讀取觀察到取消時的局部計數委派。</param>
        internal ObservedResponse(SyntheticReply reply, TaskCompletionSource bodyEntered,
            Action onCancellation, CancellationToken stopping) : base(reply.Status)
        {
            Body = new ObservedStream(Encoding.UTF8.GetBytes(reply.Json), reply.StallReading,
                bodyEntered, onCancellation, reply.ReadingFailure, stopping);
            ContentProbe = new ObservedContent(Body, reply.AdvertiseLength);
            Content = ContentProbe;
        }

        /// <summary>本 response 唯一擁有的合成 stream，產品不得留下讀取工作。</summary>
        internal ObservedStream Body { get; }
        /// <summary>同時觀察 content 與 stream 的釋放，避免只關閉 stream 就誤判 response 已關閉。</summary>
        internal ObservedContent ContentProbe { get; }
        /// <summary>產品應在完成前關閉一次；owner 保底關閉不能冒充產品證據。</summary>
        internal int DisposeCalls => Volatile.Read(ref disposeCalls);

        /// <summary>第一次處置紀錄後交由 HttpResponseMessage 關閉其內容，不改變正常所有權鏈。</summary>
        /// <param name="disposing">true 表示明確關閉 managed 資源。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing) Interlocked.CompareExchange(ref disposeCalls, 1, 0);
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// 同時支援串流與緩衝消費的合成 content，唯一擁有本筆 ObservedStream；
    /// 未讀取本文的非 200 路徑也必須在 content 處置時關閉 stream。
    /// </summary>
    private sealed class ObservedContent : HttpContent
    {
        /// <summary>本 content 唯一擁有的 stream，沒有跨回覆共用。</summary>
        private readonly ObservedStream body;
        /// <summary>是否讓讀取端預先得知長度，false 覆蓋未知長度防護。</summary>
        private readonly bool advertiseLength;
        /// <summary>第一次釋放標記，重複關閉保持冪等。</summary>
        private int disposeCalls;

        /// <summary>接管合成 stream，不取得 Serialize 目的地的所有權。</summary>
        /// <param name="body">本筆 response 唯一的 stream。</param>
        /// <param name="advertiseLength">是否公布合成本文長度。</param>
        internal ObservedContent(ObservedStream body, bool advertiseLength)
        {
            this.body = body;
            this.advertiseLength = advertiseLength;
            Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        }

        /// <summary>明確 content 關閉的原子觀察值。</summary>
        internal int DisposeCalls => Volatile.Read(ref disposeCalls);

        /// <summary>舊式緩衝入口同樣經過可觀察 stream；目的地由呼叫端擁有。</summary>
        /// <param name="stream">借入的緩衝目的地。</param>
        /// <param name="context">不用於合成內容的 transport context。</param>
        /// <returns>沒有重寫產品驗證的複製工作。</returns>
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            body.CopyToAsync(stream, 1024, CancellationToken.None);

        /// <summary>新版緩衝入口轉送真實取消 token，讓本文停滯確實受到 operation deadline 管理。</summary>
        /// <param name="stream">借入的緩衝目的地。</param>
        /// <param name="context">不用於合成內容的 transport context。</param>
        /// <param name="cancellationToken">來自產品／HttpClient 的取消。</param>
        /// <returns>有界於取消或 stream 結束的複製工作。</returns>
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context,
            CancellationToken cancellationToken) => body.CopyToAsync(stream, 1024, cancellationToken);

        /// <summary>提供可觀察的原始 stream，不偷偷另建會讓讀取計數失真的 MemoryStream。</summary>
        /// <returns>仍由本 content 擁有的合成 stream。</returns>
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(body);

        /// <summary>提供相同 stream 並尊重讀取前取消，不延長 token 或額外登錄。</summary>
        /// <param name="cancellationToken">實際傳入的操作取消。</param>
        /// <returns>仍由本 content 擁有的合成 stream。</returns>
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Stream>(body);
        }

        /// <summary>只公布已知長度，未知長度案例不得靠 Content-Length 預先擋下。</summary>
        /// <param name="length">固定 byte 數，未知時不應由呼叫端使用。</param>
        /// <returns>是否可以預知長度。</returns>
        protected override bool TryComputeLength(out long length) { length = body.Length; return advertiseLength; }

        /// <summary>先關閉自己擁有的 stream，再讓基底釋放緩衝／快取 stream；各層皆冪等。</summary>
        /// <param name="disposing">true 才釋放 managed stream。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.CompareExchange(ref disposeCalls, 1, 0) == 0) body.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// 每筆獨立、至多約一 MiB 的記憶體 stream，讀取最多一次一 KiB 以觀察提前中止。
    /// 以組合方式持有 MemoryStream，所有 CopyToAsync 都經過本層讀取探針；
    /// stall 只等待實際 operation 或 owner 取消，局部 linked source 隨讀取結束即清理。
    /// </summary>
    private sealed class ObservedStream : Stream
    {
        /// <summary>本層唯一擁有的純記憶體來源；不向消費端公開，第一次處置時關閉。</summary>
        private readonly MemoryStream source;
        /// <summary>是否故意停在第一筆非同步讀取。</summary>
        private readonly bool stall;
        /// <summary>由測試唯一 owner 管理的停止 token，不保留 CancellationTokenSource。</summary>
        private readonly CancellationToken stopping;
        /// <summary>只活在案例內的進入訊號。</summary>
        private readonly TaskCompletionSource entered;
        /// <summary>只修改同一 handler 計數的短生命週期委派。</summary>
        private readonly Action onCancellation;
        /// <summary>只保留到本筆 stream 處置的合成讀取故障，用於檢查原物件是否向上保留。</summary>
        private readonly Exception? readingFailure;
        /// <summary>已實際交付的 bytes，提供超限提前拒絕斷言。</summary>
        private long bytesRead;
        /// <summary>尚未完成的讀取數，操作排空時應歸零。</summary>
        private int activeReads;
        /// <summary>第一次關閉標記，不因重複 Dispose 加倍計數。</summary>
        private int disposeCalls;

        /// <summary>擁有本筆合成 bytes，不使用檔案、socket 或程序共享緩衝。</summary>
        /// <param name="bytes">本筆獨有的固定 UTF-8 本文。</param>
        /// <param name="stall">是否注入真正等待取消的本文讀取。</param>
        /// <param name="stopping">外層 owner 的故障善後停止 token。</param>
        /// <param name="entered">讀取開始訊號，不在建構時提早完成。</param>
        /// <param name="onCancellation">真正觀察到取消後才呼叫的局部計數委派。</param>
        /// <param name="readingFailure">本筆可選的合成讀取故障，不包含個資。</param>
        internal ObservedStream(byte[] bytes, bool stall, TaskCompletionSource entered,
            Action onCancellation, Exception? readingFailure, CancellationToken stopping)
        {
            source = new MemoryStream(bytes, writable: false);
            this.stall = stall;
            this.stopping = stopping;
            this.entered = entered;
            this.onCancellation = onCancellation;
            this.readingFailure = readingFailure;
        }

        /// <summary>原子讀取的實際交付 byte 數，不以 payload.Length 冒充。</summary>
        internal long BytesRead => Interlocked.Read(ref bytesRead);
        /// <summary>仍在等待的非同步讀取，Dispose 前必須已歸零。</summary>
        internal int ActiveReads => Volatile.Read(ref activeReads);
        /// <summary>實際第一次 Dispose 的觀察值。</summary>
        internal int DisposeCalls => Volatile.Read(ref disposeCalls);

        /// <summary>來源未關閉時允許讀取；不宣稱已處置的來源仍可使用。</summary>
        public override bool CanRead => source.CanRead;
        /// <summary>合成 HTTP 本文不提供定位能力，以維持一般網路 stream 的使用方式。</summary>
        public override bool CanSeek => false;
        /// <summary>來源只可讀，不允許消費端改寫本次供應商回覆。</summary>
        public override bool CanWrite => false;
        /// <summary>只供 content 產生可選長度；正式消費仍須受有界讀取限制。</summary>
        public override long Length => source.Length;
        /// <summary>只供探針觀察位置，不允許重新定位繞過串流讀取流程。</summary>
        public override long Position
        {
            get => source.Position;
            set => throw new NotSupportedException("Synthetic response body is not seekable.");
        }

        /// <summary>同步正常讀取也計數；停滯向量要求可取消非同步 I/O，不能阻塞測試執行緒。</summary>
        /// <param name="buffer">借入的呼叫端緩衝。</param>
        /// <param name="offset">寫入起點。</param>
        /// <param name="count">最多讀取數。</param>
        /// <returns>本次實際讀取數。</returns>
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        /// <summary>正常同步路徑只交付一 KiB；停滯案例若誤用同步 I/O 就明確失敗。</summary>
        /// <param name="buffer">本次借入的呼叫端緩衝。</param>
        /// <returns>實際交付 byte 數。</returns>
        public override int Read(Span<byte> buffer)
        {
            entered.TrySetResult();
            if (stall) throw new InvalidOperationException("Synthetic stalled body requires cancellable async reads.");
            var count = source.Read(buffer[..Math.Min(buffer.Length, 1024)]);
            Interlocked.Add(ref bytesRead, count);
            return count;
        }

        /// <summary>轉送到唯一可觀察的 Memory 讀取實作，不建立 Task.Run 或同步 I/O 工作。</summary>
        /// <param name="buffer">借入緩衝。</param>
        /// <param name="offset">寫入起點。</param>
        /// <param name="count">最多讀取數。</param>
        /// <param name="cancellationToken">真實操作取消。</param>
        /// <returns>同一次可觀察非同步讀取工作。</returns>
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        /// <summary>
        /// 開始後才發訊號；注入停滯時等待實際取消，finally 必定減少 active 並釋放 linked source。
        /// 正常分支只把本筆 bytes 複製到呼叫端緩衝，不檢查任何 claims。
        /// </summary>
        /// <param name="buffer">借入的呼叫端緩衝，不保存在 stream。</param>
        /// <param name="cancellationToken">真正傳入的 operation 取消。</param>
        /// <returns>實際 byte 數或原始取消例外。</returns>
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref activeReads);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping);
            try
            {
                entered.TrySetResult();
                linked.Token.ThrowIfCancellationRequested();
                if (stall) await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
                if (readingFailure is { } fault) throw fault;
                var count = source.Read(buffer.Span[..Math.Min(buffer.Length, 1024)]);
                Interlocked.Add(ref bytesRead, count);
                return count;
            }
            catch (OperationCanceledException) { onCancellation(); throw; }
            finally { Interlocked.Decrement(ref activeReads); }
        }

        /// <summary>明確經過 ReadAsync 探針複製，避免 MemoryStream 快速路徑繞過停滯與 bytesRead 計數。</summary>
        /// <param name="destination">借入且不可由本 stream 關閉的目的地。</param>
        /// <param name="bufferSize">呼叫端建議值；本合成來源固定以一 KiB 有界複製。</param>
        /// <param name="cancellationToken">來源與目的地都收到的真實取消。</param>
        /// <returns>直到 EOF、取消或目的地故障的單次複製工作。</returns>
        public override async Task CopyToAsync(Stream destination, int bufferSize,
            CancellationToken cancellationToken)
        {
            var buffer = new byte[1024];
            int count;
            while ((count = await ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
                await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }

        /// <summary>本檔沒有寫入緩衝，讀取來源不提供 flush。</summary>
        public override void Flush() => throw new NotSupportedException("Synthetic response body is read-only.");

        /// <summary>拒絕重新定位，避免測試不小心依賴真實 HTTP 本文沒有的能力。</summary>
        /// <param name="offset">不被接受的定位差。</param>
        /// <param name="origin">不被接受的定位基準。</param>
        /// <returns>此合成 HTTP 本文不提供定位結果。</returns>
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException("Synthetic response body is not seekable.");

        /// <summary>拒絕修改長度，保持本筆不可變合成資料。</summary>
        /// <param name="value">不被接受的新長度。</param>
        public override void SetLength(long value) =>
            throw new NotSupportedException("Synthetic response body is read-only.");

        /// <summary>拒絕寫入，消費端只借用唯讀供應商本文。</summary>
        /// <param name="buffer">不被保存的借入資料。</param>
        /// <param name="offset">不被接受的起點。</param>
        /// <param name="count">不被接受的長度。</param>
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("Synthetic response body is read-only.");

        /// <summary>只記錄第一次明確關閉，仍讓基底確定性終止 MemoryStream 使用。</summary>
        /// <param name="disposing">true 為 owner／產品明確處置。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.CompareExchange(ref disposeCalls, 1, 0) == 0) source.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>非同步處置與同步處置共用同一冪等計數，沒有額外待完成工作。</summary>
        /// <returns>已完成的清理值。</returns>
        public override ValueTask DisposeAsync() => base.DisposeAsync();
    }
}

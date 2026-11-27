/*
檔案整體說明
目的與定位：F3 目錄與傳輸的離線契約測試，使用真 ChannelTokenProtector、HMAC 及產品目錄，來源／HTTP 僅為有合法控制的合成替身，完全不讀 Secrets 或連 LINE／D365。
主要功能與流程：先以最小 throw 骨架取得真正失敗，再驗產品篩選、異動失效、同名不同 ID、重複 ID／鍵拒絕、完整候選發布、32 路隔離、401 二次上限及清理。故障注入後以實際解析結果及呼叫／釋放計數斷言，不能用全面拒絕的替身製造假綠。
使用邊界與維護：各測試擁有目錄、HttpClient、取消來源與暫存診斷 owner，結束時排空並釋放；借入來源不被目錄 Dispose。所有 token 與 key 為測試即時合成，字串只清參照。GC 案例用不內聯 helper 與弱參照觀察已退休憑證，不能宣稱為實機或長期 soak。
*/
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using LineMessagingProcessor.Core;
using LineMessagingProcessor.Core.Security;
using SpeechMessage.Localization;
using ToolUtilityNameSpace.Channels;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace ToolUtility.Tests.Channels;

/// <summary>驗證 F3 owner 的安全及生命週期契約；只有本類具名測試被定向執行。</summary>
public sealed class ChannelDirectoryTests
{
    /// <summary>他產品與空產品同時存在仍只解密本產品，且他產品告警跨讀取去重一次。</summary>
    [Fact]
    public void Directory_LoadsOnlyOwnProductChannels()
    {
        using var fixture = new DirectoryFixture();
        fixture.Source.Rows = [fixture.Row("10001"), fixture.Row("20001", "GoodButler"), fixture.Row("30001", null)];
        using var directory = fixture.Create();
        Assert.Equal(["10001"], directory.ConfiguredChannelIds);
        Assert.True(directory.TryGetCredential("10001", out var own));
        Assert.NotNull(own);
        Assert.False(directory.TryGetCredential("20001", out _));
        Assert.False(directory.TryGetCredential("30001", out _));
        Assert.Equal(2, fixture.DecryptCalls);
        Assert.Single(fixture.Diagnostics.Failures);
    }

    /// <summary>T01 secret 簽署不能授權 T02；合法 T02 控制必須成功，證明不是全面拒絕。</summary>
    [Fact]
    public void Resolver_WrongSecret_SignatureFails()
    {
        using var fixture = new DirectoryFixture();
        fixture.Source.Rows = [fixture.Row("10001"), fixture.Row("10002")];
        using var directory = fixture.Create();
        Assert.True(directory.TryResolveSecret("10001", out var first));
        Assert.True(directory.TryResolveSecret("10002", out var second));
        var body = Encoding.UTF8.GetBytes("{\"events\":[]}");
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(first!), body);
        Assert.False(CryptographicOperations.FixedTimeEquals(signature, HMACSHA256.HashData(Encoding.UTF8.GetBytes(second!), body)));
        Assert.True(CryptographicOperations.FixedTimeEquals(signature, HMACSHA256.HashData(Encoding.UTF8.GetBytes(first!), body)));
    }

    /// <summary>真共用診斷落檔不含任何八字元秘密片段，且真正來源例外仍由目錄拋出。</summary>
    [Fact]
    public async Task Resolver_TokenNeverLogged()
    {
        using var fixture = new DirectoryFixture();
        var folder = Path.Combine(Path.GetTempPath(), "f3-sanitized-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            await using (var owner = new ExceptionDiagnostics(folder, outputOptions: new ExceptionOutputOptions(true, false)))
            {
                using var directory = fixture.Create(new CommonChannelDiagnostics(owner));
                Assert.True(directory.TryGetCredential("10001", out _));
                var original = new InvalidOperationException(DirectoryFixture.Token("10001") + DirectoryFixture.Secret("10001"));
                fixture.Source.Failure = original;
                Assert.Same(original, Assert.Throws<InvalidOperationException>(() => directory.TryGetCredential("10001", out _)));
            }
            var text = string.Join("", Directory.GetFiles(folder).Select(File.ReadAllText));
            Assert.Contains("InvalidOperationException", text, StringComparison.Ordinal);
            foreach (var secret in new[] { DirectoryFixture.Token("10001"), DirectoryFixture.Secret("10001") })
                for (var start = 0; start <= secret.Length - 8; start++)
                    Assert.DoesNotContain(secret.Substring(start, 8), text, StringComparison.Ordinal);
        }
        finally { Directory.Delete(folder, true); }
    }

    /// <summary>每讀版本提早失效；新密文原子取代，不等待十分鐘 TTL。</summary>
    [Fact]
    public void Resolver_ChannelUpdated_SnapshotRefreshed()
    {
        using var fixture = new DirectoryFixture();
        using var directory = fixture.Create();
        Assert.True(directory.TryResolveSecret("10001", out var old));
        fixture.Source.Rows = [fixture.Row("10001", revision: "2", suffix: "rotated")];
        Assert.True(directory.TryResolveSecret("10001", out var updated));
        Assert.NotEqual(old, updated);
        Assert.Equal(DirectoryFixture.Secret("10001") + "rotated", updated);
        Assert.Equal(2, fixture.Source.FullReads);
    }

    /// <summary>
    /// 保護單次目錄解析只借一個來源 lease，並以真實合成 secret 確認成功路徑。
    /// 計數來源實際 OpenAsync 次數，不以 SDK mock 互動或拒絕所有請求的替身代替生命週期斷言。
    /// </summary>
    [Fact]
    public void Directory_SnapshotRefresh_UsesOneSourceLease()
    {
        using var fixture = new DirectoryFixture();
        using var directory = fixture.Create();
        Assert.True(directory.TryResolveSecret("10001", out var secret));
        Assert.Equal(DirectoryFixture.Secret("10001"), secret);
        Assert.Equal(1, fixture.Source.OpenCount);
    }

    /// <summary>空產品不解密、不進健康 HTTP；合法產品控制須真正完成三次呼叫及統計回寫。</summary>
    [Fact]
    public async Task Directory_ChannelWithoutProduct_NotLoadedAndNotCalled()
    {
        using var fixture = new DirectoryFixture();
        fixture.Source.Rows = [fixture.Row("10001"), fixture.Row("30001", null)];
        using var directory = fixture.Create();
        using var handler = new HealthHandler();
        using var client = new HttpClient(handler);
        var checker = new ChannelHealthChecker(directory, client);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => checker.CheckAsync("30001", default));
        Assert.Equal(0, handler.Calls);
        Assert.Empty(fixture.Diagnostics.Failures);
        Assert.Equal(1, fixture.Source.FullReads);
        Assert.Equal(2, fixture.DecryptCalls);
        var result = await checker.CheckAsync("10001", default);
        Assert.True(result.Healthy);
        Assert.Equal(7, result.MonthlyUsage);
        Assert.Equal(2, result.RichMenuCount);
        Assert.Equal(fixture.Clock.UtcNow, result.CheckedAt);
        Assert.Equal(3, handler.Calls);
        Assert.Equal(3, handler.DisposedContents);
        Assert.Null(client.DefaultRequestHeaders.Authorization);
        Assert.Single(fixture.Source.Writes);
        Assert.Equal(2, fixture.DecryptCalls);
    }

    /// <summary>健康 401 只停用已解析正式 Id，清除快照並告警一次；返回前回應已釋放。</summary>
    [Fact]
    public async Task HealthCheck_401_MarksChannelInvalidAndAlerts()
    {
        using var fixture = new DirectoryFixture();
        using var directory = fixture.Create();
        using var handler = new HealthHandler { Status = HttpStatusCode.Unauthorized };
        using var client = new HttpClient(handler);
        var result = await new ChannelHealthChecker(directory, client).CheckAsync("10001", default);
        Assert.False(result.Healthy);
        Assert.Equal(DirectoryChannelStatus.Disabled, fixture.Source.Rows[0].Status);
        Assert.Single(fixture.Source.Writes);
        Assert.Single(fixture.Diagnostics.Failures);
        Assert.IsType<HttpRequestException>(fixture.Diagnostics.Failures[0]);
        Assert.False(directory.TryGetCredential("10001", out _));
        Assert.Equal(1, handler.Calls);
        Assert.Equal(1, handler.DisposedContents);
    }

    /// <summary>兩次 401 合計二次傳輸、一次重解析；中途旋轉要用新 credential，最後一次告警。</summary>
    [Fact]
    public async Task Push_TokenRotatedMidFlight_ResolvesOnceThenFails()
    {
        using var fixture = new DirectoryFixture();
        using var directory = fixture.Create();
        var seen = new List<LineChannelCredential>();
        var disposed = 0;
        var result = await new ChannelCredentialTransport(directory).SendAsync("10001", (credential, ct) =>
        {
            seen.Add(credential);
            ct.ThrowIfCancellationRequested();
            var previous = Assert.Single(fixture.Source.Rows);
            var rotated = fixture.Row("10001", revision: "2", suffix: "rotated");
            fixture.Source.Rows = [new ChannelDirectoryRecord(previous.Id, rotated.Product, rotated.ChannelKey, rotated.Status,
                rotated.Revision, rotated.ModifiedOn, rotated.TokenEncrypted, rotated.SecretEncrypted)];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new TrackingContent("{}", () => disposed++) });
        }, default);
        Assert.False(result.Succeeded);
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.Equal(2, seen.Count);
        Assert.NotSame(seen[0], seen[1]);
        Assert.Equal(2, fixture.Source.FullReads);
        Assert.Equal(2, disposed);
        Assert.Single(fixture.Diagnostics.Failures);
    }

    /// <summary>同樣顯示概念但正式 ID 不同必須保留，重複 ID 或鍵使整份候選拒絕。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Directory_DuplicateIdentityOrKey_FailsClosed(bool duplicateKey)
    {
        using var fixture = new DirectoryFixture();
        var first = fixture.Row("10001");
        var second = fixture.Row(duplicateKey ? "10001" : "10002");
        if (!duplicateKey) second = new ChannelDirectoryRecord(first.Id, second.Product, second.ChannelKey, second.Status, second.Revision, second.ModifiedOn, second.TokenEncrypted, second.SecretEncrypted);
        fixture.Source.Rows = [first, second];
        using var directory = fixture.Create();
        Assert.Throws<InvalidOperationException>(() => directory.ValidateConfiguration());
        Assert.Equal(0, fixture.DecryptCalls);
        fixture.Source.Rows = [fixture.Row("10001"), fixture.Row("10002")];
        Assert.Equal(2, directory.ConfiguredChannelIds.Count);
    }

    /// <summary>刷新失敗不能留下半份候選或沿用舊 secret；來源修復後可重新讀取完整兩筆。</summary>
    [Fact]
    public void Directory_FailureRetry_NoPartialPublicationOrOldCredential()
    {
        using var fixture = new DirectoryFixture();
        using var directory = fixture.Create();
        Assert.True(directory.TryResolveSecret("10001", out _));
        fixture.Source.Rows = [fixture.Row("10001", revision: "2"), fixture.Row("10002")];
        fixture.DecryptFailureAt = 4;
        Assert.Throws<InvalidOperationException>(() => directory.TryGetCredential("10001", out _));
        fixture.DecryptFailureAt = -1;
        Assert.Equal(2, directory.ConfiguredChannelIds.Count);
        Assert.True(directory.TryResolveSecret("10002", out _));
    }

    /// <summary>超出產品容量時整份拒絕，不能以 Take／Distinct 靜默裁切。</summary>
    [Fact]
    public void Directory_CapacityExceeded_RejectsBeforeDecrypt()
    {
        using var fixture = new DirectoryFixture();
        fixture.Source.Rows = [fixture.Row("10001"), fixture.Row("10002")];
        using var directory = fixture.Create(maximum: 1);
        Assert.Throws<InvalidOperationException>(() => directory.ValidateConfiguration());
        Assert.Equal(0, fixture.DecryptCalls);
    }

    /// <summary>32 路兩個固定產品並行；各路 secret 只能屬於自己，沒有單一預設後備或半份發布。</summary>
    [Fact]
    public async Task Directory_ThirtyTwoConcurrent_IsolatedAndSinglePublication()
    {
        using var first = new DirectoryFixture();
        using var second = new DirectoryFixture("GoodButler");
        first.Source.Rows = [first.Row("10001")];
        second.Source.Rows = [second.Row("20001", "GoodButler")];
        using var a = first.Create();
        using var b = second.Create();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = Enumerable.Range(0, 32).Select(async i =>
        {
            await gate.Task;
            var own = i % 2 == 0 ? a : b;
            var key = i % 2 == 0 ? "10001" : "20001";
            Assert.True(own.TryResolveSecret(key, out var secret));
            Assert.Equal(DirectoryFixture.Secret(key), secret);
            Assert.False(own.TryGetCredential(i % 2 == 0 ? "20001" : "10001", out _));
        }).ToArray();
        gate.SetResult();
        await Task.WhenAll(work);
        Assert.Equal(1, first.Source.FullReads);
        Assert.Equal(1, second.Source.FullReads);
    }

    /// <summary>caller 已取消不 HTTP、不告警；操作取消後同一 owner 仍可處理合法控制。</summary>
    [Fact]
    public async Task Health_CallerCancellation_NoAlertAndRetryWorks()
    {
        using var fixture = new DirectoryFixture();
        using var directory = fixture.Create();
        using var handler = new HealthHandler();
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var checker = new ChannelHealthChecker(directory, client);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => checker.CheckAsync("10001", cancellation.Token));
        Assert.Empty(fixture.Diagnostics.Failures);
        Assert.Equal(0, handler.Calls);
        Assert.True((await checker.CheckAsync("10001", default)).Healthy);
    }

    /// <summary>停止會取消在途委派並等待其 finally 完成；外借來源不被 Dispose，之後一律拒絕入場。</summary>
    [Fact]
    public async Task Directory_Dispose_DrainsInFlightAndClearsReferences()
    {
        using var fixture = new DirectoryFixture();
        var directory = fixture.Create();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = false;
        var work = new ChannelCredentialTransport(directory).SendAsync("10001", async (_, ct) =>
        {
            entered.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            finally { exited = true; }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await directory.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
        Assert.True(exited);
        Assert.Throws<ObjectDisposedException>(() => directory.TryGetCredential("10001", out _));
        Assert.False(fixture.Source.Disposed);
        Assert.All(fixture.Key, value => Assert.Equal((byte)19, value));
        directory.Dispose();
    }

    /// <summary>退休快照不保留舊 credential；連續 64 代後強制 GC 可回到弱參照基線。</summary>
    [Fact]
    public void Directory_RotationGc_ReturnsToBaseline()
    {
        using var fixture = new DirectoryFixture();
        using var directory = fixture.Create();
        var weak = Enumerable.Range(1, 64).Select(i => RetireCredential(fixture, directory, i)).ToArray();
        fixture.Source.Rows = [fixture.Row("10001", revision: "999")];
        directory.ValidateConfiguration();
        for (var i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        Assert.All(weak, item => Assert.False(item.IsAlive));
    }

    /// <summary>隔離 JIT 區域生命週期，回傳弱參照前取得一個真實可用的舊 credential。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RetireCredential(DirectoryFixture fixture, DataverseChannelDirectory directory, int revision)
    {
        fixture.Source.Rows = [fixture.Row("10001", revision: revision.ToString(System.Globalization.CultureInfo.InvariantCulture))];
        Assert.True(directory.TryGetCredential("10001", out var credential));
        return new WeakReference(credential!);
    }
}

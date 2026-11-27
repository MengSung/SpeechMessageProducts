/*
檔案整體說明
目的與定位：提供 F3 測試專用的合成來源、時鐘與 HTTP owner；只在測試組件編譯，不連實機、不讀設定或秘密。
主要功能與流程：建立可真正解密的 envelope 與固定產品資料；來源支援正常控制、版本旋轉及原例外注入，HTTP 依固定端點回傳合法 JSON，內容釋放有明確計數。測試以呼叫計數、Id／版本核對及原例外身分區分真綠與全面拒絕的假綠。
使用邊界與維護：fixture 擁有合成 key 並在 Dispose 清零；來源不擁有 SDK 或執行緒。每個 HTTP 回應內容 owner 由產品方法釋放，HttpClient／handler 則由測試 using 管理；時鐘沒有 timer。所有可變計數為執行個體限定，32 路測試以 Interlocked 計數，沒有全域使用者狀態或背景工作。
*/
using System.Net;
using System.Security.Cryptography;
using LineMessagingProcessor.Core.Security;
using SpeechMessage.Localization;
using ToolUtilityNameSpace.Channels;
using Xunit;
namespace ToolUtility.Tests.Channels;

/// <summary>單一測試的合成資料 owner；建立真正可解密與可驗簽的合法控制。</summary>
internal sealed class DirectoryFixture : IDisposable
{
    /// <summary>測試固定產品與來源綁定，不由任何請求決定。</summary>
    internal DirectoryFixture(string product = "GoodShepherd2")
    {
        Product = product;
        Source = new MemoryChannelSource(product);
        Source.Rows = [Row("10001", product)];
    }
    internal string Product { get; }
    internal byte[] Key { get; } = Enumerable.Repeat((byte)19, 64).ToArray();
    internal MemoryChannelSource Source { get; }
    internal TestClock Clock { get; } = new();
    internal RecordingDiagnostics Diagnostics { get; } = new();
    internal int DecryptCalls;
    internal int DecryptFailureAt = -1;
    /// <summary>每個 key 各有唯一合成 token，不含真實 LINE 資訊。</summary>
    internal static string Token(string key) => "synthetic-token-for-" + key;
    /// <summary>每個 key 各有不同 secret，供跨路由 HMAC 拒絕測試。</summary>
    internal static string Secret(string key) => "synthetic-secret-for-" + key;
    /// <summary>建立正式非空 ID 與真正加密 envelope；呼叫端可明確注入空白或其他產品。</summary>
    internal ChannelDirectoryRecord Row(string key, string? product = "GoodShepherd2", string revision = "1", string suffix = "") =>
        new(Guid.NewGuid(), product, key, DirectoryChannelStatus.Operating, revision,
            new DateTime(2026, 11, 26, 0, 0, 0, DateTimeKind.Utc).AddSeconds(long.Parse(revision, System.Globalization.CultureInfo.InvariantCulture)),
            ChannelTokenProtector.Protect(Token(key) + suffix, Key), ChannelTokenProtector.Protect(Secret(key) + suffix, Key));
    /// <summary>注入真解密並計數；故障位置可驗完整候選不部分發布。</summary>
    internal DataverseChannelDirectory Create(IChannelDiagnostics? diagnostics = null, int maximum = 256) =>
        new(Product, Source.OrganizationId, Source, Key, Clock, diagnostics ?? Diagnostics, maximum, (value, key) =>
        {
            if (Interlocked.Increment(ref DecryptCalls) == DecryptFailureAt) throw new InvalidOperationException("Synthetic decryption failure.");
            return ChannelTokenProtector.Unprotect(value, key);
        });
    /// <summary>只有 fixture 可以清除原合成 key；目錄不得改寫它。</summary>
    public void Dispose() => CryptographicOperations.ZeroMemory(Key);
}

/// <summary>固定產品的有限合成來源；回寫仍核對正式 Id、產品與 revision，不是假造無條件成功。</summary>
internal sealed class MemoryChannelSource(string product) : IChannelDirectorySource, IDisposable
{
    private ChannelDirectoryRecord[] rows = [];
    internal ChannelDirectoryRecord[] Rows { get => Volatile.Read(ref rows); set => Volatile.Write(ref rows, value); }
    internal Exception? Failure;
    internal int FullReads;
    internal int OpenCount;
    internal bool Disposed;
    internal List<ChannelDirectoryRecord> Writes { get; } = [];
    public Guid OrganizationId { get; } = Guid.NewGuid();
    public string Product { get; } = product;
    /// <summary>false 明確去除密文；原例外故障不複製或包裝，便於驗證診斷身分。</summary>
    public ValueTask<IChannelDirectorySourceLease> OpenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref OpenCount);
        return ValueTask.FromResult<IChannelDirectorySourceLease>(new SourceLease(this));
    }
    /// <summary>只有相同正式主鍵／產品／版本才允許更新；401 合成來源立即反映 Disabled。</summary>
    private Task WriteHealthAsync(ChannelDirectoryRecord channel, ChannelHealthResult result, bool disable, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actual = Assert.Single(Rows.Where(row => row.Id == channel.Id));
        Assert.Equal(Product, actual.Product);
        Assert.Equal(actual.Revision, channel.Revision);
        Writes.Add(channel);
        if (disable) Rows = Rows.Select(row => row.Id == channel.Id ? new ChannelDirectoryRecord(row.Id, row.Product, row.ChannelKey, DirectoryChannelStatus.Disabled, row.Revision, row.ModifiedOn, row.TokenEncrypted, row.SecretEncrypted) : row).ToArray();
        return Task.CompletedTask;
    }
    /// <summary>僅測試 owner 呼叫，驗證目錄不擅自 Dispose 借入來源。</summary>
    public void Dispose() => Disposed = true;
    /// <summary>每次 source open 的操作租約；釋放只標示 reader 結束，不釋放來源 owner。</summary>
    private sealed class SourceLease(MemoryChannelSource owner) : IChannelDirectorySourceLease
    {
        /// <summary>一個操作期間完整讀取 metadata／secret；依呼叫參數合法取得密文。</summary>
        public Task<IReadOnlyList<ChannelDirectoryRecord>> ReadAsync(bool includeCredentials, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (owner.Failure != null) throw owner.Failure;
            if (includeCredentials) Interlocked.Increment(ref owner.FullReads);
            IReadOnlyList<ChannelDirectoryRecord> values = includeCredentials ? owner.Rows : owner.Rows.Select(row => new ChannelDirectoryRecord(row.Id, row.Product, row.ChannelKey, row.Status, row.Revision, row.ModifiedOn)).ToArray();
            return Task.FromResult(values);
        }
        /// <summary>透過來源 owner 重驗及回寫正式資料列，不直接覆寫 fixture 集合。</summary>
        public Task WriteHealthAsync(ChannelDirectoryRecord channel, ChannelHealthResult result, bool disable, CancellationToken cancellationToken) => owner.WriteHealthAsync(channel, result, disable, cancellationToken);
        /// <summary>釋放單次操作 reader，不 Dispose 外借來源或固定測試 owner。</summary>
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>不保留敏感本文的合成診斷；原例外清單僅在本次測試生命週期中存在。</summary>
internal sealed class RecordingDiagnostics : IChannelDiagnostics
{
    internal List<Exception> Failures { get; } = [];
    /// <summary>正常 caller 取消禁止記錄；固定操作碼不含實際路由或 secret。</summary>
    public void Report(Exception exception, string operation, CancellationToken cancellationToken)
    {
        Assert.False(exception is OperationCanceledException && cancellationToken.IsCancellationRequested);
        lock (Failures) Failures.Add(exception);
    }
}

/// <summary>固定 UTC 時鐘，提供真 ChurchClock 進行其餘轉換，不使用伺服器本地時間。</summary>
internal sealed class TestClock : IChurchClock
{
    private readonly ChurchClock inner = new();
    public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 26, 0, 0, 0, TimeSpan.Zero);
    public DateTime ToChurchTime(DateTime utc, string ianaTimeZoneId) => inner.ToChurchTime(utc, ianaTimeZoneId);
    public DateTime TodayInChurch(string ianaTimeZoneId) => inner.ToChurchTime(UtcNow.UtcDateTime, ianaTimeZoneId).Date;
    public DateTime ToUtc(LocalDateTimeOfChurch local) => inner.ToUtc(local);
}

/// <summary>只允許三個指定健康端點的 HTTP fake；合法本文與每次釋放計數使健康成功可驗證。</summary>
internal sealed class HealthHandler : HttpMessageHandler
{
    internal int Calls;
    internal int DisposedContents;
    internal HttpStatusCode Status = HttpStatusCode.OK;
    /// <summary>每則訊息驗證 Bearer 與固定 HTTPS host，不讀取任何真實網路。</summary>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Assert.Equal("api.line.me", request.RequestUri!.Host);
        Assert.Equal("https", request.RequestUri.Scheme);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal(DirectoryFixture.Token("10001"), request.Headers.Authorization.Parameter);
        Interlocked.Increment(ref Calls);
        var text = request.RequestUri.AbsolutePath switch
        {
            "/v2/bot/info" => "{\"userId\":\"synthetic-bot\"}",
            "/v2/bot/message/quota/consumption" => "{\"totalUsage\":7}",
            "/v2/bot/richmenu/list" => "{\"richmenus\":[{},{}]}",
            _ => throw new InvalidOperationException("Unexpected endpoint.")
        };
        return Task.FromResult(new HttpResponseMessage(Status) { Content = new TrackingContent(text, () => Interlocked.Increment(ref DisposedContents)) });
    }
}

/// <summary>擁有合成 HTTP 本文；計數只在首次確定 Dispose 時增加，產品不能遺漏回應釋放。</summary>
internal sealed class TrackingContent(string text, Action disposed) : StringContent(text)
{
    private int released;
    /// <summary>先記錄單次釋放，再交 BCL 釋放其內部串流；外部 handler 不擁有回應。</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref released, 1) == 0) disposed();
        base.Dispose(disposing);
    }
}

/*
檔案整體說明
目的與定位：提供 P3-03 真 Kestrel 離線測試的主機 owner；沿用 P3-02 去重／佇列替身，
唯獨驗簽走正式 composition。所有 mapping 與 secret 皆為合成設定，不讀部署秘密或連外。
主要功能與流程：空白 builder 注入兩組組織白名單、頻道與 secret，經產品 Program 建立主機，
只綁本機 ephemeral 埠。測試 HMAC 只產生輸入，實際驗證由產品 LineSignature 負責。
使用邊界與維護：案例 await using 擁有主機與 client，先 Stop 再 Dispose、最後清空替身；
每筆 request／response 明確移交或釋放，簽章用 key／digest 立即清零。原始診斷只留本 task
ignored evidence，不寫 TEMP。建構失敗釋放未移交的設定根；不宣稱 LINE 實機驗收。
*/
using System.Net;
using System.Security.Cryptography;
using System.Text;
using LineMessagingProcessor.AspNetCore.Webhooks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SpeechMessageProducts.LineWebhook.Tests;

/// <summary>案例獨立的本機 Host owner；正式 verifier 由產品註冊，外部 SQL／queue 用有界替身。</summary>
internal sealed class WebhookSignatureTestHost : IAsyncDisposable
{
    /// <summary>頻道 A 合成密鑰，僅供離線簽章向量，不能用於部署。</summary>
    internal const string AlphaSecret = "0123456789abcdef0123456789abcdef";
    /// <summary>頻道 B 不同的合成密鑰，讓跨頻道誤用必然失敗。</summary>
    internal const string BetaSecret = "abcdef0123456789abcdef0123456789";
    private readonly string directoryPath;
    private HttpClient? client;
    /// <summary>由案例持有並關閉的主機，啟動拒絕也需釋放。</summary>
    internal WebApplication App { get; }
    /// <summary>已驗收的去重替身，沒有外部 SQL 資源。</summary>
    internal DeduplicatorDouble Dedupe { get; } = new();
    /// <summary>主機結束後清空所有事件參照的測試 queue。</summary>
    internal QueueDouble Queue { get; } = new();
    /// <summary>只讀本案例安全日誌，斷言預期拒絕沒有通知。</summary>
    internal string ExceptionLog => File.Exists(Path.Combine(directoryPath, "Logs", "Exception.log"))
        ? File.ReadAllText(Path.Combine(directoryPath, "Logs", "Exception.log")) : string.Empty;

    /// <summary>從合成組態建立正式組合；成功後設定根交 Host，失敗由本建構式清理。</summary>
    internal WebhookSignatureTestHost(Dictionary<string, string?>? configuration = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SpeechMessageProducts.sln"))) root = root.Parent;
        directoryPath = Path.Combine(root?.FullName ?? throw new InvalidOperationException("Worktree not found."),
            ".trellis", "tasks", "11-09-p3-03-signature", "evidence", "http", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing", ContentRootPath = directoryPath,
            ApplicationName = typeof(Program).Assembly.GetName().Name
        });
        try
        {
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(configuration ?? CreateConfiguration());
            builder.Services.AddSingleton<IWebhookEventDeduplicator>(_ => Dedupe);
            builder.Services.AddSingleton<IWebhookEventQueue>(_ => Queue);
            configureServices?.Invoke(builder.Services);
            App = Program.BuildApplication(builder);
        }
        catch
        {
            ((IDisposable)builder.Configuration).Dispose();
            Dedupe.Dispose(); Queue.Dispose();
            throw;
        }
    }

    /// <summary>建立可獨立修改的合成部署資料，不從環境或 User Secrets 補值。</summary>
    internal static Dictionary<string, string?> CreateConfiguration() => new()
    {
        ["ExceptionNotifications:WriteExceptionLog"] = "true",
        ["ExceptionNotifications:SendLine"] = "false",
        ["LineWebhook:AllowedOrganizations:0"] = "Alpha",
        ["LineWebhook:AllowedOrganizations:1"] = "Beta",
        ["LineWebhook:Channels:123:Organization"] = "Alpha",
        ["LineWebhook:Channels:456:Organization"] = "Beta",
        ["Deployment:Organizations:Alpha:TestChannelSecret"] = AlphaSecret,
        ["Deployment:Organizations:Beta:TestChannelSecret"] = BetaSecret
    };

    /// <summary>成功啟動才建立無 cookie client，沒有固定埠或外部位址。</summary>
    internal async Task StartAsync()
    {
        await App.StartAsync();
        client = new HttpClient(new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false })
        { BaseAddress = new Uri(App.Urls.Single()), Timeout = TimeSpan.FromSeconds(10) };
    }

    /// <summary>原始 UTF-8 bytes 原樣簽章送出；request 自行釋放，response 交案例 using。</summary>
    internal async Task<HttpResponseMessage> PostAsync(string body, string channel = "123",
        string secret = AlphaSecret, bool chunked = false, string[]? headers = null)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/line/webhook/" + channel)
        { Content = chunked ? new UnknownLengthContent(bytes) : new ByteArrayContent(bytes) };
        request.Headers.TryAddWithoutValidation("X-Line-Signature", headers ?? [CreateSignature(bytes, secret)]);
        return await (client ?? throw new InvalidOperationException("Host not started.")).SendAsync(request);
    }

    /// <summary>只產生獨立測試 HMAC 輸入；key／digest 由本方法擁有且 finally 清零。</summary>
    internal static string CreateSignature(byte[] body, string secret)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        byte[]? digest = null;
        try
        {
            digest = HMACSHA256.HashData(key, body);
            return Convert.ToBase64String(digest);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (digest is not null) CryptographicOperations.ZeroMemory(digest);
        }
    }

    /// <summary>先停止主機再釋放 client／Host；關閉失敗仍在 finally 排空兩個案例替身。</summary>
    public async ValueTask DisposeAsync()
    {
        try { await App.StopAsync(); }
        finally
        {
            client?.Dispose();
            try { await App.DisposeAsync(); }
            finally { Dedupe.Dispose(); Queue.Dispose(); }
        }
    }

    /// <summary>不宣告 Content-Length 以驗真正 chunked 路徑；bytes 只活至 request Dispose。</summary>
    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        /// <summary>直接寫出合成 bytes，不重新序列化或建立背景工作。</summary>
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        /// <summary>要求傳輸層分塊，避免誤走已知長度提早拒絕。</summary>
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}

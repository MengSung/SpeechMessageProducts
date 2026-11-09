/*
檔案整體說明
目的與定位：保護正式 SQL 組合根的啟動快照驗證，確保非法或缺失平台設定在 HTTP 與任何 SQL 操作之前拒絕。
主要功能與流程：Generic Host 真正執行平行啟動流程，提供合成設定及計數 factory，精確斷言固定錯誤、零連線建立、零後續 hosted Start；正常字串只在 DI 解析建立去重服務，不自動開連線或建表。
使用邊界與維護：測試不讀環境秘密、不連 SQL；每案例自己擁有組態、Host 與共享診斷，結束 Dispose 清空快照及 CTS。服務解析失敗不可讓 Host.StopAsync 再產生另一個例外，否則會掩蓋原始部署錯誤。
*/
using System.Data.Common;
using LineMessagingProcessor.AspNetCore.Webhooks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace LineMessagingProcessor.AspNetCore.Tests;

/// <summary>只驗正式啟動邊界與生命週期，不以離線設定測試冒充資料庫可達性。</summary>
public sealed class WebhookDedupeRegistrationTests
{
    /// <summary>缺項、空值、語法錯誤、無資料庫與帶子節點都用固定訊息拒絕；Stop 不得覆蓋原錯誤。</summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("invalid-private-marker", false)]
    [InlineData("Server=synthetic", false)]
    [InlineData("Database=synthetic", false)]
    [InlineData("Server=synthetic;Database=synthetic", true)]
    public async Task InvalidSnapshot_RejectsBeforeAnyStartOrConnection(string? value, bool children)
    {
        using var configuration = new ConfigurationManager(); configuration["ConnectionStrings:Platform"] = value;
        if (children) configuration["ConnectionStrings:Platform:Nested"] = "private-marker";
        await using var diagnostics = new ExceptionDiagnostics(Path.Combine(WebhookSqlTestDatabase.Root, ".trellis", "tasks", "11-09-p3-04-sql-dedupe", "evidence", "startup", Guid.NewGuid().ToString("N")), outputOptions: new ExceptionOutputOptions(false, false));
        var probe = new StartProbe(); var connections = 0;
        using var host = new HostBuilder().ConfigureServices(services =>
        {
            services.AddSingleton(diagnostics); services.AddSingleton<Func<DbConnection>>(() => { connections++; throw new InvalidOperationException("Must not create SQL connection."); });
            services.AddSingleton<IWebhookSignatureVerifier>(new Verifier()); services.AddSingleton<IWebhookEventQueue>(new RejectQueue());
            services.Configure<HostOptions>(options => options.ServicesStartConcurrently = true);
            services.AddLineWebhook(configuration); services.AddSingleton<IHostedService>(probe);
        }).Build();
        var error = await Assert.ThrowsAsync<AggregateException>(() => host.StartAsync());
        var failures = error.Flatten().InnerExceptions;
        Assert.Equal(2, failures.Count);
        Assert.All(failures, failure => Assert.Equal("Webhook requires a valid ConnectionStrings:Platform startup snapshot.", Assert.IsType<InvalidOperationException>(failure).Message));
        Assert.Equal(0, probe.Starts); Assert.Equal(0, connections); await host.StopAsync();
    }

    /// <summary>註冊時是有效字串，之後改壞與 Dispose 組態仍可解析服務；解析本身不得開啟 SQL 或自動建表。</summary>
    [Fact]
    public async Task ConfigurationChanges_DoNotAlterStartupSnapshotOrOpenSql()
    {
        var configuration = new ConfigurationManager(); configuration["ConnectionStrings:Platform"] = "Server=synthetic;Database=synthetic;Integrated Security=true";
        await using var diagnostics = new ExceptionDiagnostics(Path.Combine(WebhookSqlTestDatabase.Root, ".trellis", "tasks", "11-09-p3-04-sql-dedupe", "evidence", "startup", Guid.NewGuid().ToString("N")), outputOptions: new ExceptionOutputOptions(false, false));
        var services = new ServiceCollection(); services.AddSingleton(diagnostics); var connections = 0;
        services.AddSingleton<Func<DbConnection>>(() => { connections++; throw new InvalidOperationException("Must not open SQL during resolution."); });
        try { services.AddLineWebhook(configuration); configuration["ConnectionStrings:Platform"] = "invalid-private-marker"; }
        finally { ((IDisposable)configuration).Dispose(); }
        await using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IWebhookEventDeduplicator>()); Assert.Equal(0, connections);
        Assert.Single(services.Where(d => d.ServiceType == typeof(IWebhookEventDeduplicator)));
    }

    /// <summary>純啟動計數器，沒有背景資源，精確證明拒絕發生在所有 Start 之前。</summary>
    private sealed class StartProbe : IHostedService
    {
        /// <summary>真 Host 呼叫次數，不能由測試手動設定結果。</summary>
        internal int Starts;
        /// <summary>只計數，設定驗證失敗時必為零。</summary>
        public Task StartAsync(CancellationToken cancellationToken) { Interlocked.Increment(ref Starts); return Task.CompletedTask; }
        /// <summary>無在途資源，不另啟背景工作。</summary>
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>只隔離 SQL 啟動驗證，無外部驗簽 I/O 或資料保存，永遠不可用於產品。</summary>
    private sealed class Verifier : IWebhookSignatureVerifier
    {
        /// <summary>啟動探測只解析此服務，不送 HTTP；若被呼叫只回拒絕。</summary>
        public ValueTask<bool> VerifyAsync(string channelId, ReadOnlyMemory<byte> body, string signature, CancellationToken cancellationToken) => ValueTask.FromResult(false);
    }
    /// <summary>只供啟動探測，不建立 consumer 或保存事件，避免其他包缺 handler 干擾 SQL 錯誤斷言。</summary>
    private sealed class RejectQueue : IWebhookEventQueue
    {
        /// <summary>永遠拒收且不提交；產品不能透過此替身宣稱功能成功。</summary>
        public ValueTask<bool> TryEnqueueAsync(WebhookEnvelope envelope, IWebhookEventRegistration registration, CancellationToken cancellationToken) => ValueTask.FromResult(false);
    }
}

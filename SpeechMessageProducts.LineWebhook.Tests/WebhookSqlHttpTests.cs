/*
檔案整體說明
目的與定位：以真 Kestrel 端點與正式 SQL 去重組合接點驗證資料庫開啟逾時回 503；只有 DbConnection 為明確故障替身，不宣稱此案例成功連線 SQL。
主要功能與流程：保留既有驗簽與 queue 合成替身，省略記憶體去重，注入每次新建且 OpenAsync 拋同一 TimeoutException 的連線工廠；檢查 HTTP 狀態、零發布、共享日誌及連線精確 Dispose。
使用邊界與維護：本案例只提供固定合成連線，不讀真秘密。每次 Host、request、response 與連線由唯一 owner 清理，無背景工作或靜態資料；錯誤內容含私密標記以確保日誌不輸出原訊息。原有 fixture 證據目錄仍為 ignored evidence，不改其其他包來源。
*/
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using LineMessagingProcessor.AspNetCore.Webhooks;
using Microsoft.Extensions.DependencyInjection;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace SpeechMessageProducts.LineWebhook.Tests;

/// <summary>SQL 最終失敗邊界的真 HTTP 契約；替身只注入開啟失敗，沒有假成功或記憶體判重。</summary>
public sealed class WebhookSqlHttpTests
{
    /// <summary>正式去重必須走連線工廠；原 TimeoutException 經共享入口去重、503、零 queue 發布且全部連線釋放。</summary>
    [Fact]
    public async Task Dedupe_SqlTimeout_Returns503()
    {
        var failure = new TimeoutException("private-sql-connection-marker");
        var opened = 0; var disposed = 0;
        await using var host = new WebhookTestHost(typeof(IWebhookEventDeduplicator),
            new() { ["ConnectionStrings:Platform"] = "Server=synthetic;Database=synthetic;Integrated Security=true" },
            services => services.AddSingleton<Func<DbConnection>>(() => new TimeoutConnection(failure,
                () => Interlocked.Increment(ref opened), () => Interlocked.Increment(ref disposed))));
        Assert.IsAssignableFrom<IWebhookEventDeduplicator>(host.App.Services.GetService<IWebhookEventDeduplicator>());
        await host.StartAsync();
        using var response = await host.PostAsync("{\"events\":[{\"type\":\"follow\",\"webhookEventId\":\"timeout\"}]}");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(host.Queue.Events); Assert.Equal(1, opened); Assert.Equal(1, disposed);
        Assert.Contains("TimeoutException", host.ExceptionLog, StringComparison.Ordinal);
        Assert.DoesNotContain("private-sql-connection-marker", host.ExceptionLog, StringComparison.Ordinal);
        Assert.False(host.App.Services.GetRequiredService<ExceptionDiagnostics>().Report(failure, "WebhookSqlTests.Identity"));
    }

    /// <summary>單次操作擁有的失敗連線；完全不執行 SQL，計數保護開啟失敗仍必須釋放的契約。</summary>
    /// <param name="failure">本案例原始故障物件，用於驗證診斷去重身分。</param>
    /// <param name="opened">本案例有界計數 callback，不保存產品或 request。</param>
    /// <param name="released">唯一釋放 callback，不能由多個 owner 重複呼叫。</param>
    private sealed class TimeoutConnection(Exception failure, Action opened, Action released) : DbConnection
    {
        private int disposed;
        /// <summary>只供抽象基類契約，不保存部署資料或回顯真連線字串。</summary>
        [AllowNull] public override string ConnectionString { get; set; } = string.Empty;
        /// <summary>固定合成庫名，沒有資料庫 I/O。</summary>
        public override string Database => "synthetic";
        /// <summary>固定合成來源，沒有 DNS 或連線。</summary>
        public override string DataSource => "synthetic";
        /// <summary>固定合成版本，不冒充真 SQL Server。</summary>
        public override string ServerVersion => "synthetic";
        /// <summary>永遠未成功開啟；產品不可視為可重用連線。</summary>
        public override ConnectionState State => ConnectionState.Closed;
        /// <summary>拒絕切庫，避免測試意外引入跨 scope 功能。</summary>
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        /// <summary>關閉不建立資源，真正 owner 仍須呼叫 Dispose。</summary>
        public override void Close() { }
        /// <summary>同步開啟不是允許路徑，錯誤可抓到產品退化。</summary>
        public override void Open() => throw new NotSupportedException();
        /// <summary>非同步開啟保留同一故障物件，讓診斷去重可驗其身分。</summary>
        public override Task OpenAsync(CancellationToken cancellationToken) { opened(); return Task.FromException(failure); }
        /// <summary>開啟失敗後不得繼續交易。</summary>
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        /// <summary>開啟失敗後不得繼續執行命令。</summary>
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
        /// <summary>一次精確釋放，第二次為測試失敗，不以冪等掩蓋雙 owner。</summary>
        protected override void Dispose(bool disposing)
        { if (disposing) { Assert.Equal(0, Interlocked.Exchange(ref disposed, 1)); released(); } base.Dispose(disposing); }
    }
}

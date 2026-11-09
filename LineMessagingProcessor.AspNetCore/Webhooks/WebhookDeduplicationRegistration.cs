/*
檔案整體說明
目的與定位：實作 P3-04 的 RegisterDeduplication partial 接點，只組合正式平台 SQL 去重與每日有界清理，不改三個已驗收介面或 Program。
主要功能與流程：既有明確替身註冊由測試組合根擁有，不新增第二個 owner；正式情形快照化 ConnectionStrings:Platform，DI 解析時驗證，再建立無使用者狀態的去重器與唯一 hosted cleanup。各 SQL 操作才建立自己的連線，不在 startup 自動建表。
使用邊界與維護：連線工廠僅能由可信 DI 組合根提供，不能由 HTTP 選擇。快照經 DI singleton factory 接管 Dispose，不註冊多個 disposable alias；diagnostics 與 clock 只借用。缺字串在啟動拒絕，沒有記憶體後備，測試同時涵蓋正式預設與窄連線故障接縫。
*/
using System.Data.Common;
using LineMessagingProcessor.AspNetCore.Webhooks.Deduplication;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ToolUtilityNameSpace.Diagnostics;

namespace LineMessagingProcessor.AspNetCore.Webhooks;

public static partial class WebhookServiceRegistration
{
    /// <summary>只補 SQL 自有接點；啟動快照不讀 reload，容器負責唯一快照與清理 owner。</summary>
    static partial void RegisterDeduplication(IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(d => d.ServiceType == typeof(IWebhookEventDeduplicator))) return;
        var section = configuration.GetSection("ConnectionStrings:Platform");
        var settings = new SqlWebhookSettings(section.Value, section.GetChildren().Any());
        services.AddSingleton(_ => settings);
        services.AddSingleton<IWebhookEventDeduplicator>(provider =>
        {
            var snapshot = provider.GetRequiredService<SqlWebhookSettings>();
            _ = snapshot.ConnectionString;
            return new SqlWebhookEventDeduplicator(provider.GetService<Func<DbConnection>>() ?? (() => new SqlConnection(snapshot.ConnectionString)),
                provider.GetService<TimeProvider>() ?? TimeProvider.System, provider.GetRequiredService<ExceptionDiagnostics>());
        });
        services.AddHostedService(provider => new SqlWebhookCleanupService(() => (SqlWebhookEventDeduplicator)provider.GetRequiredService<IWebhookEventDeduplicator>(),
            provider.GetService<TimeProvider>() ?? TimeProvider.System, provider.GetRequiredService<ExceptionDiagnostics>()));
    }
}

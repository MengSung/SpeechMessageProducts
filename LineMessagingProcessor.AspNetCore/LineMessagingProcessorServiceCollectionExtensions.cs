using Line.Messaging;
using LineMessagingProcessor;
using LineMessagingProcessor.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LineMessagingProcessor.AspNetCore;

/// <summary>
/// ASP.NET Core DI 註冊入口。未來產品只需引用此專案並設定 token，即可取得 processor 與共用 workflow。
/// </summary>
public static class LineMessagingProcessorServiceCollectionExtensions
{
    private const string HttpClientName = "LineMessagingProcessor";

    public static IServiceCollection AddLineMessagingProcessor(
        this IServiceCollection services,
        Action<LineMessagingProcessorOptions> configure)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configure == null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        services.Configure(configure);
        services.AddHttpClient(HttpClientName);
        services.AddTransient(sp =>
        {
            var options = sp.GetRequiredService<IOptions<LineMessagingProcessorOptions>>().Value;
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(HttpClientName);
            return new LineMessagingClient(httpClient, options.ChannelAccessToken, options.ApiBaseUri);
        });
        services.AddTransient(sp =>
            new LineMessagingProcessorClass(sp.GetRequiredService<LineMessagingClient>()));
        services.AddTransient<ILineNotificationWorkflow, LineNotificationWorkflow>();
        services.AddTransient<ILineReplyWorkflow, LineReplyWorkflow>();
        services.AddTransient<ILineRichMenuWorkflow, LineRichMenuWorkflow>();

        return services;
    }
}

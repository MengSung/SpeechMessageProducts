using Line.Messaging;
using LineMessagingProcessor;
using LineMessagingProcessor.RichMenus;
using LineMessagingProcessor.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace LineMessagingProcessor.AspNetCore;

/// <summary>
/// ASP.NET Core DI registration for the shared LINE processor family.
/// The registration is intentionally split so future products can choose the shared RichMenu core
/// without being forced to provide product-specific catalog/policy services at the same time.
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
        services.AddLineRichMenus();

        return services;
    }

    /// <summary>
    /// Registers product-neutral RichMenu services.
    /// This method does not register <see cref="ILineRichMenuCatalog"/> because catalog content is product-specific.
    /// A future ASP.NET Core product can call this after registering LineMessagingProcessorClass, then add its own
    /// catalog, policies, and persistent state store.
    /// </summary>
    public static IServiceCollection AddLineRichMenus(
        this IServiceCollection services,
        Action<LineRichMenuTextTriggerOptions>? configureTextTriggers = null)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configureTextTriggers != null || !services.Any(descriptor => descriptor.ServiceType == typeof(LineRichMenuTextTriggerOptions)))
        {
            var textTriggerOptions = new LineRichMenuTextTriggerOptions();
            configureTextTriggers?.Invoke(textTriggerOptions);

            services.RemoveAll<LineRichMenuTextTriggerOptions>();
            services.AddSingleton(textTriggerOptions);
        }
        services.TryAddSingleton<ILineRichMenuIdCache, InMemoryLineRichMenuIdCache>();
        services.TryAddSingleton<IRichMenuStateStore, InMemoryRichMenuStateStore>();
        services.TryAddTransient<ILineRichMenuProcessor, LineMessagingProcessorRichMenuAdapter>();
        services.TryAddTransient<ILineRichMenuWorkflow, LineRichMenuWorkflow>();
        services.TryAddTransient<ILineRichMenuAssignmentWorkflow, LineRichMenuAssignmentWorkflow>();
        services.TryAddTransient<ILineRichMenuTextTriggerResolver, LineRichMenuTextTriggerResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IRichMenuPolicy, LineRichMenuTextTriggerPolicy>());
        services.TryAddTransient<IRichMenuOrchestrator>(sp =>
            new RichMenuOrchestrator(
                sp.GetServices<IRichMenuPolicy>(),
                sp.GetRequiredService<ILineRichMenuAssignmentWorkflow>()));
        services.TryAddTransient<IRichMenuExpirationSweepWorkflow, RichMenuExpirationSweepWorkflow>();

        return services;
    }

    /// <summary>
    /// Registers RichMenu provisioning with a product-owned catalog.
    /// Keeping this separate from AddLineRichMenus prevents the shared core from forcing every application
    /// to define menus before it can use assignment, text trigger, or workflow services.
    /// </summary>
    public static IServiceCollection AddLineRichMenuProvisioning<TCatalog>(this IServiceCollection services)
        where TCatalog : class, ILineRichMenuCatalog
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.AddLineRichMenus();
        services.TryAddTransient<ILineRichMenuCatalog, TCatalog>();
        services.TryAddTransient<ILineRichMenuProvisioningWorkflow, LineRichMenuProvisioningWorkflow>();

        return services;
    }
}


using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SpeechMessage.Tenancy;

/// <summary>固定產品部署的 DI 組合根；不保存 request、不接管外部 IConfiguration，不建立背景資源。</summary>
public static class TenancyServiceCollectionExtensions
{
    /// <summary>註冊同一產品的共用解析入口及不可變設定 provider；重複相同產品可重入，跨產品誤註冊必須拒絕。</summary>
    /// <param name="services">Host 擁有的服務集合。</param>
    /// <param name="product">本容器唯一服務的固定產品。</param>
    /// <returns>原服務集合。</returns>
    public static IServiceCollection AddSpeechMessageTenancy(this IServiceCollection services, string product)
    {
        ArgumentNullException.ThrowIfNull(services);
        TenantId.RequireIdentifier(product, nameof(product));
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(ProductRegistration) && descriptor.ImplementationInstance is ProductRegistration existing)
            {
                if (!string.Equals(existing.Product, product, StringComparison.Ordinal))
                    throw new InvalidOperationException("Tenancy services already belong to another product.");
                return services;
            }
            if (descriptor.ServiceType == typeof(ILineChannelResolver) || descriptor.ServiceType == typeof(ITenantSettingsProvider))
                throw new InvalidOperationException("Tenancy services already have an explicit owner.");
        }
        services.AddSingleton(new ProductRegistration(product));
        services.AddSingleton(provider => new ConfigurationLineChannelResolver(provider.GetRequiredService<IConfiguration>(),
            product, provider.GetService<ILogger<ConfigurationLineChannelResolver>>()));
        services.AddSingleton<ILineChannelResolver>(provider => provider.GetRequiredService<ConfigurationLineChannelResolver>());
        services.AddSingleton<ITenantSettingsProvider>(provider => new ConfigurationTenantSettingsProvider(
            provider.GetRequiredService<IConfiguration>(), product));
        return services;
    }

    /// <summary>容器私有的固定產品註冊標記；只有非機密產品代碼，不保存設定、租戶或請求。</summary>
    private sealed class ProductRegistration
    {
        /// <summary>本容器唯一 owner，重複註冊必須逐字相同。</summary>
        internal string Product { get; }
        /// <summary>建立固定產品標記，沒有可釋放資源。</summary>
        /// <param name="product">已驗證產品代碼。</param>
        internal ProductRegistration(string product) => Product = product;
    }
}

/// <summary>舊靜態呼叫點的薄橋接；只委派共用 resolver，不複製來源選擇，不保存設定或任何解析結果。</summary>
public static class LegacyLineChannelAccess
{
    /// <summary>保留舊可選 token 入口；未設定回空，來源故障向上拋給原始失敗邊界。呼叫者須先驗證部署組織。</summary>
    /// <param name="configuration">Host 擁有且已通過安全防護的設定。</param>
    /// <param name="organization">已驗證部署組織；null 或空表示只使用原政策的預設來源。</param>
    /// <param name="selection">呼叫點的固定相容政策。</param>
    /// <param name="product">固定產品識別，不得由 request 控制。</param>
    /// <returns>只屬本次操作的 raw token；禁止記錄、序列化或加入共享結果快取。</returns>
    public static string GetOptionalAccessToken(IConfiguration configuration, string? organization = null,
        LineChannelSelection selection = LineChannelSelection.OrganizationThenDefault, string product = "ChurchReport")
    {
        var resolver = new ConfigurationLineChannelResolver(configuration, product, selection: selection);
        if (string.IsNullOrEmpty(organization))
            return resolver.TryResolveDefault(out var defaultChannel) ? defaultChannel.GetAccessTokenForLegacyClient() : string.Empty;
        return resolver.TryResolve(new TenantId(organization, product), out var channel)
            ? channel.GetAccessTokenForLegacyClient() : string.Empty;
    }
}

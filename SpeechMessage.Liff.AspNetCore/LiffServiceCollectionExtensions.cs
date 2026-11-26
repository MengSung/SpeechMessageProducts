/*
檔案整體說明
目的與定位：F6共用LIFF的DI入口，為產品組合根註冊有界且無使用者狀態的傳輸、時鐘與scoped驗證filter；產品自己的actor與selection來源另行提供。
主要功能與流程：啟動時保存公開部署映射，HttpClientFactory唯一管理handler與client；預設禁止Cookie及redirect，五秒逾時、五分鐘連線生命週期、每站最多十六連線。filter與verifier不建立隱藏容器或跨請求身分快取。
使用邊界與維護：不讀User Secrets、不建立LINE產品client，不修改DefaultRequestHeaders；token只在單次request form存在。Host負責停止HTTP後由DI釋放factory資源，產品角色來源未接入時一律空權限，不捏造管理員。測試以真實DI與HTTP驗scope及fault，外部LINE實機另外驗收。
*/
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SpeechMessage.Localization;

namespace SpeechMessage.Liff.AspNetCore;

/// <summary>產品明確提供固定部署映射後才組裝共用驗證；不向產品層反向依賴。</summary>
public static class LiffServiceCollectionExtensions
{
    /// <summary>建立由Host擁有的安全傳輸及scoped filter；呼叫端另註冊自己的actor與selection來源。</summary>
    /// <param name="services">當次Host唯一服務集合。</param>
    /// <param name="options">受信任的公開部署快照，未配置時filter拒絕。</param>
    /// <returns>原服務集合，沒有建置隱藏容器或啟動I/O。</returns>
    public static IServiceCollection AddLiffAuthentication(this IServiceCollection services, LiffOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        if (services.Any(s => s.ServiceType == typeof(LiffOptions)))
            throw new InvalidOperationException("LIFF authentication is already registered.");
        services.AddSingleton(options);
        services.TryAddSingleton<IChurchClock, ChurchClock>();
        services.AddHttpClient("SpeechMessage.Liff.IdToken", client => client.Timeout = TimeSpan.FromSeconds(5))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                UseCookies = false,
                AllowAutoRedirect = false,
                MaxConnectionsPerServer = 16,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                ConnectTimeout = TimeSpan.FromSeconds(5)
            });
        services.AddScoped<LiffHttpClientOwner>();
        services.TryAddScoped(provider => new LiffIdTokenVerifier(
            provider.GetRequiredService<LiffHttpClientOwner>().Client, provider.GetRequiredService<IChurchClock>()));
        services.AddScoped<LiffAuthenticationFilter>();
        services.TryAddScoped<ILiffRoleResolver, EmptyRoleResolver>();
        return services;
    }
}

/// <summary>單次request的client owner，借用factory共用handler而唯一處置client；scope關閉才釋放，不跨request保存token。</summary>
internal sealed class LiffHttpClientOwner : IDisposable
{
    /// <summary>建立本scope唯一client；factory仍擁有handler，client不接管外部factory生命週期。</summary>
    /// <param name="factory">Host唯一factory，不能由request選擇目的地或處理常式。</param>
    public LiffHttpClientOwner(IHttpClientFactory factory) => Client = factory.CreateClient("SpeechMessage.Liff.IdToken");
    /// <summary>本scope內只借用的無狀態client，所有request仍須各自Dispose。</summary>
    public HttpClient Client { get; }
    /// <summary>HTTP請求與filter排空後由DI唯一釋放client，handler繼續由factory管理。</summary>
    public void Dispose() => Client.Dispose();
}

/// <summary>F8尚未接入前的明確空權限來源；不是管理員後備，不建立資料存取或快取。</summary>
internal sealed class EmptyRoleResolver : ILiffRoleResolver
{
    /// <summary>回空集合，正常取消直接傳遞；F8需以真正已驗的租戶角色來源取代本註冊。</summary>
    /// <param name="contactId">已驗Contact ID，不在本預設來源保存。</param>
    /// <param name="tenantId">已驗租戶ID，不推定任何權限。</param>
    /// <param name="cancellationToken">原caller取消。</param>
    /// <returns>沒有權限的不可變空集合。</returns>
    public Task<IReadOnlyCollection<string>> ResolveRolesAsync(Guid contactId, Guid tenantId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyCollection<string>>(Array.Empty<string>());
    }
}

// ============================================================================
// 檔案：SpeechMessage.Dynamics.Embedded/DependencyInjection/EmbeddedServiceCollectionExtensions.cs
// 用途：將已組合的受控 ControlPlane executor 以 EmbeddedHostAdapter 發佈給產品。
//
// 安全與生命週期契約：本檔不建構 HTTP、WCF、Data8 client、timer、CTS、thread 或背景工作；所有可釋放
// 資源仍由呼叫端所提供的 ControlPlane／Data8 composition root 唯一擁有並由其 DI container 確定性釋放。
// ============================================================================

using Microsoft.Extensions.DependencyInjection;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ControlPlane.Guard;

namespace SpeechMessage.Dynamics.Embedded.DependencyInjection;

/// <summary>
/// 提供 Embedded 模式的 DI 註冊入口。Embedded 只取代產品至 Gateway 的 HTTP hop；受控 executor
/// 必須已經包含 ProfileResolver、Organization Admission、IConnectorRouter 與 Data8 Pool，否則呼叫端
/// 不得使用此 API。此類別不保存 service collection 或設定物件，避免把可變組態跨 host generation 保留。
/// </summary>
public static class EmbeddedServiceCollectionExtensions
{
    /// <summary>
    /// 以 DI factory 發佈固定 ProfileAlias 的 stateless Embedded adapter。此 overload 專供產品 composition root
    /// 將 runtime 的唯一 owner 註冊在同一個 <see cref="IServiceProvider"/>：Guard 與受控 executor 都延後至
    /// adapter 首次解析才取得，因此 registration 不會提早建立 Data8 client、WCF channel、HTTP、timer、CTS 或
    /// 背景工作。factory 只可回傳 host-owned singleton；它們不得快取 request、Session、credential、endpoint、
    /// permit 或 client，也不可再建立第二個 ServiceProvider。
    /// </summary>
    /// <param name="services">由產品 Generic Host 擁有並確定性 Dispose 的服務集合。</param>
    /// <param name="productOptions">產品唯一可見的模式與固定 ProfileAlias 設定。</param>
    /// <param name="requestGuardFactory">從同一 DI provider 取得共用 RequestGuard 的 factory。</param>
    /// <param name="controlledExecutorFactory">
    /// 從同一 DI provider 取得已包含 Resolver、Admission、Router 與 Pool 的 executor factory。
    /// </param>
    /// <returns>原 services，供 composition root 繼續完成其他 host 註冊。</returns>
    public static IServiceCollection AddSpeechMessageDynamicsEmbedded(
        this IServiceCollection services,
        ProductDynamicsOptions productOptions,
        Func<IServiceProvider, IRequestGuard> requestGuardFactory,
        Func<IServiceProvider, IDynamicsOperationExecutor> controlledExecutorFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(productOptions);
        ArgumentNullException.ThrowIfNull(requestGuardFactory);
        ArgumentNullException.ThrowIfNull(controlledExecutorFactory);

        ValidateEmbeddedOptions(productOptions);

        // 將 profile alias 複製為純 scalar，避免 provider closure 保留可由組態繫結器後續改寫的 options 物件。
        // Adapter 本身是無狀態 singleton；factory 只在第一次解析時執行，之後不重新讀取組態或建立第二個 runtime。
        var profileAlias = productOptions.ProfileAlias.Trim();
        services.AddSingleton<IDynamicsOperationExecutor>(serviceProvider => new EmbeddedHostAdapter(
            requestGuardFactory(serviceProvider) ?? throw new InvalidOperationException(
                "Embedded Dynamics request guard factory returned null."),
            controlledExecutorFactory(serviceProvider) ?? throw new InvalidOperationException(
                "Embedded Dynamics controlled executor factory returned null."),
            profileAlias));
        return services;
    }

    /// <summary>
    /// 發佈以固定 ProfileAlias 執行的 stateless Embedded adapter。`Gateway.Endpoint` 在此模式完全不讀取，
    /// 因此 localhost、Central endpoint 或其 HTTP session 不會被建立；非 Embedded mode、空 alias 或缺少
    /// 受控依賴都在任何 permit、connector client 或外呼之前 fail closed。
    /// </summary>
    /// <param name="services">由產品 host 擁有並會在關機時依反向順序 dispose 的服務集合。</param>
    /// <param name="productOptions">產品唯一可見的模式與 ProfileAlias 設定。</param>
    /// <param name="requestGuard">Gateway 與 Embedded 共用的同步 Guard。</param>
    /// <param name="controlledExecutor">完整 ControlPlane pipeline 的 executor；其 owner 不是此擴充方法。</param>
    /// <returns>原 services，供 composition root 繼續註冊既有產品服務。</returns>
    public static IServiceCollection AddSpeechMessageDynamicsEmbedded(
        this IServiceCollection services,
        ProductDynamicsOptions productOptions,
        IRequestGuard requestGuard,
        IDynamicsOperationExecutor controlledExecutor)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(productOptions);
        ArgumentNullException.ThrowIfNull(requestGuard);
        ArgumentNullException.ThrowIfNull(controlledExecutor);

        ValidateEmbeddedOptions(productOptions);

        // 以 instance 註冊可保證 adapter 不會嘗試從 DI 解析自己而形成遞迴，也不會額外建立 scope、
        // provider 或可釋放資源。呼叫端必須保留 controlledExecutor 的原始 owner 至 host shutdown。
        services.AddSingleton<IDynamicsOperationExecutor>(
            new EmbeddedHostAdapter(requestGuard, controlledExecutor, productOptions.ProfileAlias));
        return services;
    }

    /// <summary>
    /// 集中驗證兩個 overload 共用的純產品邊界。Embedded 不讀取 <c>Gateway.Endpoint</c>，所以唯一必需值只有
    /// 顯式 ConnectionMode 與固定 alias；驗證發生在服務註冊期，尚未取得 permit、client 或任何 transport 資源。
    /// </summary>
    /// <param name="productOptions">欲註冊 Embedded adapter 的產品設定。</param>
    private static void ValidateEmbeddedOptions(ProductDynamicsOptions productOptions)
    {
        if (productOptions.ConnectionMode != ConnectionMode.Embedded)
        {
            throw new InvalidOperationException(
                "AddSpeechMessageDynamicsEmbedded requires ConnectionMode=Embedded.");
        }

        if (string.IsNullOrWhiteSpace(productOptions.ProfileAlias))
        {
            throw new InvalidOperationException(
                "Embedded Dynamics hosting requires a configured ProfileAlias.");
        }
    }
}

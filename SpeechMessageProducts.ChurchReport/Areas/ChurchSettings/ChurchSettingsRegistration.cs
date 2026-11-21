/*
 * 檔案整體說明
 *
 * 目的與定位：在 ChurchReport Host 組合根註冊 ChurchSettings 的 request-scoped 授權與儲存服務。
 * 主要流程：每個 MVC scope 建立 Access、Dataverse store、UTC clock 與核心設定服務；快取失效器
 * 只借用啟用中的既有 ChurchTextOverrideCache，不另建或擁有 cache。偽語系政策只讀啟動設定快照。
 * 使用邊界與維護：所有 CRM facade 維持 ToolUtility 原有 scoped owner；不把 scoped 依賴捕獲到 singleton，
 * 不建立 Session、背景工作或第二套 D365 連線。容器負責依原始註冊釋放各 scope 服務。
 * 失敗與驗證：缺少或錯誤的偽語系設定不會在此放寬；共用 `LocalizationOptions` 驗證由 Host 啟動守門負責。
 * 離線 composition 測試應確認 scope 間服務不同、cache owner 唯一且關閉時不解析 CRM。
 */
using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpeechMessage.Localization;
using SpeechMessage.Tenancy;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using SpeechMessageProducts.GoodShepherd.Core.Settings;

namespace ChurchReport.Areas.ChurchSettings;

/// <summary>ChurchSettings scoped service registrations; invoked after ToolUtility and localization composition.</summary>
internal static class ChurchSettingsRegistration
{
    private const string ValidatorNameSample = "xxxxxxxxxxxxxx";

    /// <summary>以目前 Host 服務集合註冊教會設定功能所需的 request-scoped adapter。</summary>
    /// <param name="services">ChurchReport 服務集合。</param>
    /// <param name="configuration">Host 啟動設定，用於凍結偽語系 opt-in 政策。</param>
    internal static void AddChurchSettings(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddScoped<IMemberCultureResolver>(_ =>
            new MemberCultureResolver(new LocalizationOptions(
                allowPseudoLocale: configuration.GetValue<bool>("Localization:AllowPseudoLocale"))));
        services.AddScoped<ChurchSettingsAccess>();
        services.AddScoped<IChurchSettingsStore, DataverseChurchSettingsStore>();
        services.AddScoped<IChurchSettingsTermReader>(provider =>
            (IChurchSettingsTermReader)provider.GetRequiredService<IChurchSettingsStore>());
        services.AddScoped<IChurchSettingsClock, ChurchSettingsSystemClock>();
        services.AddScoped<IChurchSettingsCacheInvalidator>(provider =>
            new ChurchSettingsCacheInvalidator(provider.GetService<ChurchTextOverrideCache>()));
        services.AddScoped<ChurchSettingsService>(provider =>
            new ChurchSettingsService(
                provider.GetRequiredService<IChurchSettingsStore>(),
                provider.GetRequiredService<IChurchSettingsCacheInvalidator>(),
                GoodShepherdTextCatalog.CreateOverrideValidator(ValidatorNameSample),
                provider.GetRequiredService<IChurchSettingsClock>()));
    }
}

/// <summary>每次讀取 UTC 時刻的無狀態設定時鐘，沒有背景 timer 或可變欄位。</summary>
internal sealed class ChurchSettingsSystemClock : IChurchSettingsClock
{
    /// <summary>回傳本機系統提供的 UTC DateTime，不改變或快取時鐘值。</summary>
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>把核心服務的單 scope 快取失效呼叫轉交既有 cache owner。</summary>
internal sealed class ChurchSettingsCacheInvalidator : IChurchSettingsCacheInvalidator
{
    private readonly ChurchTextOverrideCache? cache;

    /// <summary>借用啟用時已註冊的唯一 cache；停用覆寫功能時為 null 並保持無副作用。</summary>
    /// <param name="cache">Host singleton 擁有的既有 cache，不由本 adapter Dispose。</param>
    public ChurchSettingsCacheInvalidator(ChurchTextOverrideCache? cache) => this.cache = cache;

    /// <summary>只使同完整文字 scope 的已持久化版本失效，不改動其他教會快照。</summary>
    /// <param name="context">本 request 已授權且完整的文字 scope。</param>
    /// <param name="changedOnUtc">剛寫入 account 的 UTC 版本。</param>
    public void Invalidate(TextContext context, DateTime changedOnUtc)
    {
        if (cache is null) return;
        if (changedOnUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("UTC is required.", nameof(changedOnUtc));
        cache.Invalidate(context, changedOnUtc);
    }
}

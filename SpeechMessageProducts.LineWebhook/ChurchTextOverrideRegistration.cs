/*
 * 檔案整體說明
 *
 * 目的與定位：LineWebhook 的正式教會文字覆寫組合入口，依部署啟動快照決定是否建立覆寫來源與改字輪詢。
 * 主要流程：嚴格讀取 Localization:ChurchOverrides:Enabled；關閉時不註冊來源、cache 或 polling；開啟時
 * 以既有 ToolUtility manager 建立 Dataverse lease factory，為 D365 查詢和快取固定同一組織與 LineWebhook
 * 文字產品範圍，再使用 GoodShepherd 產品字典建立 validator/catalog。
 * 資源與隔離：manager 由 AddToolUtility/Host 擁有且沿用 GoodShepherd 連線池識別；本檔不建立第二個連線池，
 * 不保存 request、使用者或 credential。Polling 依賴 cache 並作為 Host singleton 停止／排空；DI 反向釋放
 * polling、cache 與連線 manager，避免輪詢期間釋放其下游 owner。
 * 失敗與驗證：缺值預設 false；非純量或非 bool 拒絕組態；輪詢錯誤沿既有 ExceptionReporting 傳遞且不前進
 * watermark。Program composition 測試以合成組態解析真實服務圖，完全不啟動外部連線。
 * 最長教會名稱欄位只保存與 limits.json 樣本等長的 ASCII 哨兵；驗證器僅取 Length，不把哨兵當成顯示文字。
 */
using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SpeechMessage.Hosting.Localization;
using SpeechMessage.Localization;
using SpeechMessage.Tenancy;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Localization;

namespace SpeechMessageProducts.LineWebhook;

/// <summary>LineWebhook 教會覆寫的唯一啟動接線點；所有部署值在 Host 建置時凍結。</summary>
internal static class ChurchTextOverrideRegistration
{
    private const string TextProduct = "LineWebhook";
    private const string LongestChurchName = "xxxxxxxxxxxxxx";

    /// <summary>嚴格驗證開關後，選擇性註冊真實 D365 source、快取、產品 catalog 與輪詢 owner。</summary>
    /// <param name="services">目前 Host 擁有且尚未建置的容器服務集合。</param>
    /// <param name="configuration">受信任部署組態；此方法只讀啟動值，不捕捉組態 provider。</param>
    /// <returns>同一服務集合；disabled 時不增加任何覆寫服務。</returns>
    internal static IServiceCollection AddLineWebhookChurchTextOverrides(
        IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!ResolveEnabled(configuration)) return services;

        var organization = configuration["CrmConnection:Organization"];
        if (string.IsNullOrWhiteSpace(organization))
            throw new InvalidOperationException("Church text overrides require a configured deployment organization.");

        services.AddSingleton<IChurchTextOverrideServiceFactory>(provider =>
            new DataverseChurchTextOverrideServiceFactory(
                provider.GetRequiredService<IDataverseConnectionManager>(), organization, TextProduct));
        services.AddSingleton<IChurchTextOverrideSource, D365ChurchTextOverrideSource>();

        Action<Exception, string> reportFailure = static (exception, operation) =>
            ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(exception, operation);
        var validator = GoodShepherdTextCatalog.CreateOverrideValidator(LongestChurchName);
        services.AddChurchTextOverrides(validator,
            provider => new GoodShepherdTextCatalog(
                provider.GetRequiredService<IChurchTextOverrideSnapshotProvider>()),
            reportFailure, TextProduct);

        services.AddSingleton<ITextChangeSource>(provider =>
            new DataverseTextChangeSource(provider.GetRequiredService<IDataverseConnectionManager>()));
        services.AddSingleton<TextChangePollingService>(provider =>
            new TextChangePollingService(provider.GetRequiredService<ITextChangeSource>(),
                provider.GetRequiredService<ChurchTextOverrideCache>(), organization, TextProduct,
                reportFailure: (exception, operation, token) =>
                    ToolUtilityNameSpace.Diagnostics.ExceptionReporting.Report(exception, operation, token)));
        services.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<TextChangePollingService>());
        return services;
    }

    /// <summary>只接受純量 bool；缺值關閉，子節點與無法解析值都在啟動期拒絕。</summary>
    /// <param name="configuration">Host 啟動組態快照。</param>
    /// <returns>明確 true 才啟用覆寫來源。</returns>
    internal static bool ResolveEnabled(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection("Localization:ChurchOverrides:Enabled");
        if (section.GetChildren().Any())
            throw new InvalidOperationException("Church text override enabled setting must be a scalar boolean.");
        if (section.Value is null) return false;
        if (!bool.TryParse(section.Value, out var enabled))
            throw new InvalidOperationException("Church text override enabled setting must be a scalar boolean.");
        return enabled;
    }
}

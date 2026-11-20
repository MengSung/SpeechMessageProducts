/*
 * 檔案整體說明
 *
 * 目的與定位：ChurchReport 的教會文字覆寫組合入口，依啟動期純量布林開關選擇空 provider
 * 或既有 Dataverse source、Tenancy cache、產品 catalog 與改字輪詢器。
 * 主要流程：重用 AddToolUtility 已建立的 ChurchReport manager；啟用時以固定組織和產品建立
 * lease factory，先註冊唯一 source，再經 AddChurchTextOverrides 發布同一個 cache/provider/catalog，
 * 最後註冊 DataverseTextChangeSource 與 TextChangePollingService。
 * 使用邊界與維護：組織名稱只取部署設定，不能由 request 覆寫；manager、cache 與 polling 均由 Host
 * DI 持有。Host 停止時 BackgroundService 先停止並排空，容器反向 Dispose cache 後才 Dispose manager。
 * 此檔不保存 request、使用者、credential 或連線租約；disable 時不建 source、cache 或 polling。
 * 失敗與驗證：缺值關閉，非 bool／複合節點拒絕啟動；錯誤透過既有 ExceptionReporting 傳遞原例外。
 * Composition 測試使用合成組態驗證服務圖，不解析任何 D365 連線或執行實機。
 * 最長教會名稱樣本只供格式長度驗證，明確標記為非顯示資料，不計入產品文案棘輪。
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

namespace ChurchReport;

/// <summary>ChurchReport 覆寫功能的唯一啟動接線，使用既有 ChurchReport manager 與完整 scope。</summary>
internal static class ChurchTextOverrideRegistration
{
    private const string TextProduct = "ChurchReport";
    [I18nIgnore("僅供長度驗證的合成名稱樣本，不是產品顯示文字。")]
    private const string LongestChurchName = "這是一個長度剛好十二字的教會";

    /// <summary>驗證開關並註冊唯一文字 provider；啟用時來源與輪詢共用原有部署 manager。</summary>
    /// <param name="services">已由 Startup 組裝中的 Host 服務集合。</param>
    /// <param name="configuration">由 ChurchReport Host 持有的啟動組態快照。</param>
    internal static void AddChurchReportChurchTextOverrides(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!ResolveEnabled(configuration))
        {
            services.AddSingleton<IChurchTextOverrideSnapshotProvider, ChurchReportEmptyTextOverrideSnapshotProvider>();
            services.AddSingleton<ITextCatalog>(provider =>
                new GoodShepherdTextCatalog(provider.GetRequiredService<IChurchTextOverrideSnapshotProvider>()));
            return;
        }

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
    }

    /// <summary>只接受純量 bool；缺少值固定關閉，任何無法解析或複合節點均拒絕啟動。</summary>
    /// <param name="configuration">Host 啟動組態。</param>
    /// <returns>只有明確 true 才建立 D365 覆寫服務。</returns>
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

/// <summary>只標註不直接顯示於產品介面的固定驗證字串，供 I18nCheck 保留明確排除理由。</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
internal sealed class I18nIgnoreAttribute : Attribute
{
    /// <summary>建立含明確非顯示理由的掃描標記；不參與執行期產品流程。</summary>
    /// <param name="reason">說明常值不屬於使用者介面的原因。</param>
    public I18nIgnoreAttribute(string reason) => _ = reason;
}

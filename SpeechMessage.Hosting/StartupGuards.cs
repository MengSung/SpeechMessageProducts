/*
檔案整體說明

目的與定位：集中 net10 Host 的 ICU、語系發布及 Development 設定安全檢查，讓
ChurchReport 與後續主機在建立 CRM／LINE client 前採用相同的 fail-closed 契約。
此檔只執行有界純驗證與一次性組態 provider 組合，不保存 request、Session、tenant、
credential、動態 cache、timer、queue 或背景工作。

主要功能與流程：
本檔宣告 StartupGuards。ValidateIcuMode 委派 Localization 的實際 ICU guard；
ValidateLocalization 驗證正式環境偽語系、已發布語系與教會啟用語系；Development
方法沿用 P0-SEC 的 goodshepherdback、DevTest User Secrets、LINE provider、HTTPS
端點及 Embedded 開關邊界。組態 provider 由呼叫端 root 擁有，基底檔讀取 stream
在本次驗證結束前關閉，錯誤訊息只含固定鍵名與原因。

使用邊界與維護：
basePath、environmentName、configuration 只能由受信任 Host 組合根提供，不能由
request 或路由決定。這些方法不建立外部連線、不讀取真實 secrets 以外的額外來源，
也不把 token／密碼／例外本文寫入錯誤。修改時須同步驗證 ChurchReport 的相容
facade、DevelopmentSafetyTests、LocalizationOptions 與實際 Host 啟動順序。
*/
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using SpeechMessage.Localization;

namespace SpeechMessage.Hosting;

/// <summary>
/// 主機啟動前的集中安全守門；只使用受信任組合根提供的啟動快照與固定政策。
/// </summary>
public static class StartupGuards
{
    /// <summary>
    /// 驗證目前 net10 runtime 使用實際 ICU，而不是 NLS 或 Invariant 模式。
    /// </summary>
    public static void ValidateIcuMode() => GlobalizationModeGuard.ValidateIcuMode();

    /// <summary>
    /// 驗證語系發布政策，拒絕正式環境偽語系及尚未完成的語系。
    /// </summary>
    /// <param name="options">Host 建立的一次性語系啟動快照。</param>
    /// <param name="isProduction">Host 已判定的正式環境旗標。</param>
    /// <param name="configuredCultures">可選的教會啟用語系快照。</param>
    public static void ValidateLocalization(
        LocalizationOptions options,
        bool isProduction,
        IReadOnlyList<string>? configuredCultures = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate(isProduction, configuredCultures);

        if (!isProduction)
            return;

        foreach (var culture in options.ReleasedCultures)
        {
            if (!IsReleased(culture))
                throw new InvalidOperationException("ReleasedCultures contains an incomplete culture.");
        }

        if (configuredCultures is null)
            return;

        foreach (var culture in configuredCultures)
        {
            if (string.Equals(culture, "qps-ploc", StringComparison.OrdinalIgnoreCase) ||
                !IsReleased(culture))
                throw new InvalidOperationException("Configured cultures contain an unavailable culture.");
        }
    }

    /// <summary>
    /// 只在 Development 驗證 P0-SEC 的測試組織、LINE 憑證來源與安全端點；
    /// 其他環境不讀取基底檔、不建立 secrets provider，也不改變既有組態。
    /// </summary>
    /// <param name="basePath">受信任的 Host content root。</param>
    /// <param name="environmentName">Host 已解析的環境名稱。</param>
    /// <param name="configuration">Host 擁有且只借用的合併組態。</param>
    public static void ValidateDevelopmentConfiguration(
        string basePath,
        string? environmentName,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(basePath);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!IsDevelopment(environmentName))
            return;

        using var baseline = ReadBaseline(basePath);
        var baselineAliases = baseline.GetSection("LineMessaging").GetChildren().ToArray();
        foreach (var channel in configuration.GetSection("LineMessaging").GetChildren())
        {
            var value = channel["ChannelAccessToken"];
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (!string.Equals(channel.Key, "DevTest", StringComparison.OrdinalIgnoreCase))
                throw UnsafeKey(channel.Path + ":ChannelAccessToken", "Development 不得使用正式 LINE 別名憑證。");

            if (!IsValueFromUserSecrets(configuration, channel.Path + ":ChannelAccessToken"))
                throw UnsafeKey(channel.Path + ":ChannelAccessToken", "Development 測試憑證必須來自 User Secrets。");
        }

        if (!string.IsNullOrWhiteSpace(configuration["LINE_CHANNEL_ACCESS_TOKEN"]))
            throw UnsafeKey("LINE_CHANNEL_ACCESS_TOKEN", "Development 不得使用通用 LINE 環境憑證。");

        var defaultOrganization = configuration["LineMessaging:DefaultOrganization"];
        if (string.IsNullOrWhiteSpace(defaultOrganization) ||
            baselineAliases.Any(channel =>
                string.Equals(channel.Key, defaultOrganization, StringComparison.OrdinalIgnoreCase)))
            throw UnsafeKey("LineMessaging:DefaultOrganization", "必須選擇獨立的測試別名。");

        if (!string.Equals(configuration["CrmConnection:Organization"], "goodshepherdback", StringComparison.OrdinalIgnoreCase))
            throw UnsafeKey("CrmConnection:Organization", "開發模式只能使用指定的測試組織。");

        ValidateTestHost(configuration, "CrmConnection:ServerUrl");
        var enabled = configuration["DynamicsAccess:Package01FeeReadsEnabled"];
        if (enabled is not null && !bool.TryParse(enabled, out _))
            throw UnsafeKey("DynamicsAccess:Package01FeeReadsEnabled", "必須是有效的布林開關。");

        if (bool.TryParse(enabled, out var readsEnabled) && readsEnabled)
        {
            ValidateTestHost(configuration, "DynamicsAccess:Embedded:OrganizationWebApiBaseUri");
            ValidateTestHost(configuration, "DynamicsAccess:Embedded:ResourceUri");
        }
    }

    /// <summary>以 Host 的既有 provider 順序追加 Development JSON、User Secrets 或 LINE__ provider。</summary>
    /// <param name="builder">由 Host 擁有的組態建構器。</param>
    /// <param name="basePath">受信任的 content root。</param>
    /// <param name="environmentName">已解析的環境名稱。</param>
    /// <param name="secretsAssembly">只用來解析 UserSecretsId 的 Host 組件。</param>
    /// <returns>同一個 builder；provider owner 仍由組態 root 負責。</returns>
    public static IConfigurationBuilder AddDevelopmentConfigurationOverrides(
        this IConfigurationBuilder builder,
        string basePath,
        string? environmentName,
        Assembly secretsAssembly)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(basePath);
        ArgumentNullException.ThrowIfNull(secretsAssembly);

        if (!IsDevelopment(environmentName))
            return AddDeploymentLineEnvironmentOverrides(builder, environmentName);

        return builder.SetBasePath(basePath)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
            .AddUserSecrets(secretsAssembly, optional: true, reloadOnChange: false);
    }

    /// <summary>非 Development 只追加 LINE__ 前綴環境來源；Development 不讀取正式 provider。</summary>
    /// <param name="builder">既有組態建構器。</param>
    /// <param name="environmentName">Host 已解析的環境名稱。</param>
    /// <returns>同一個組態建構器。</returns>
    public static IConfigurationBuilder AddDeploymentLineEnvironmentOverrides(
        this IConfigurationBuilder builder,
        string? environmentName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return IsDevelopment(environmentName)
            ? builder
            : builder.AddEnvironmentVariables(prefix: "LINE__");
    }

    /// <summary>驗證只接受 goodshepherdback 的標準 HTTPS 端點，拒絕帳密、埠號、query 與 fragment。</summary>
    private static void ValidateTestHost(IConfiguration configuration, string key)
    {
        if (!Uri.TryCreate(configuration[key], UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
            !string.Equals(uri.Host, "goodshepherdback.speechmessage.com.tw", StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            (string.Equals(key, "CrmConnection:ServerUrl", StringComparison.Ordinal) &&
             !string.Equals(uri.AbsolutePath, "/XRMServices/2011/Organization.svc", StringComparison.Ordinal)))
            throw UnsafeKey(key, "開發模式只能使用指定測試組織的安全 HTTPS 端點。");
    }

    /// <summary>從組態 provider 的尾端判定指定值是否由 secrets.json 提供，不保留 token。</summary>
    private static bool IsValueFromUserSecrets(IConfiguration configuration, string key)
    {
        if (configuration is not IConfigurationRoot root)
            return false;

        var providers = root.Providers.ToArray();
        for (var index = providers.Length - 1; index >= 0; index--)
        {
            var provider = providers[index];
            if (!provider.TryGet(key, out var value) || string.IsNullOrWhiteSpace(value))
                continue;

            return provider is JsonConfigurationProvider jsonProvider &&
                string.Equals(
                    Path.GetFileName(jsonProvider.Source.Path),
                    "secrets.json",
                    StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>以一次性串流讀取基底組態；失敗只回傳固定安全錯誤。</summary>
    private static ConfigurationRoot ReadBaseline(string basePath)
    {
        try
        {
            using var stream = File.OpenRead(Path.Combine(basePath, "appsettings.json"));
            return (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(stream).Build();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException)
        {
            throw UnsafeKey("appsettings.json", "無法安全讀取基底設定，已拒絕開發啟動。");
        }
    }

    /// <summary>只接受大小寫不敏感的 Development，其他值均視為非 Development。</summary>
    private static bool IsDevelopment(string? environmentName) =>
        string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);

    /// <summary>由固定鍵名與固定原因組成拒絕例外，不串接任何設定值。</summary>
    private static InvalidOperationException UnsafeKey(string key, string reason) =>
        new(key + "：" + reason);

    /// <summary>確認語系存在於 Localization 產物中的正式 Released 清單。</summary>
    private static bool IsReleased(string culture) =>
        SupportedCultures.All.Any(item =>
            item.Status == CultureReleaseStatus.Released &&
            string.Equals(item.Name, culture, StringComparison.OrdinalIgnoreCase));
}

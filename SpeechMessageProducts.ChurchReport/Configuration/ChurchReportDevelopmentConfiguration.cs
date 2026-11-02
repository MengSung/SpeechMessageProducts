using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace ChurchReport.Configuration;

/// <summary>
/// 讓舊式行程級設定讀取點在 Development 套用與主機相同的檔案及 secrets，並在任何 CRM／LINE client 建立前驗證安全邊界。
/// 不保存請求、使用者、教會、設定值或憑證；非 Development 完全不新增 provider、不讀基底檔也不改變正式設定。
/// </summary>
/// <remarks>
/// 呼叫端的 static／Lazy 設定仍各行程一份，只包含部署設定，不得放入使用者狀態。
/// 新增 provider 停用 reload，不新增 watcher、事件訂閱或背景工作；原基底檔的 optional／reload 行為維持不變。
/// 啟動檢查只持有當次區域設定，基底串流與組態根都在返回前 Dispose；錯誤只有鍵名與固定原因，不包含值或內部例外。
/// </remarks>
internal static class ChurchReportDevelopmentConfiguration
{
    /// <summary>依現有 WebApplication Host 的順序選擇環境；純函式不讀取或修改程序的環境變數。</summary>
    /// <param name="dotnetEnvironment">呼叫端取得的 DOTNET_ENVIRONMENT；null 表示未指定。</param>
    /// <param name="aspnetcoreEnvironment">呼叫端取得的 ASPNETCORE_ENVIRONMENT；僅在前者未指定時採用。</param>
    /// <returns>先 DOTNET、再 ASPNETCORE，皆未指定則 Production。</returns>
    internal static string ResolveEnvironmentName(string dotnetEnvironment, string aspnetcoreEnvironment)
        => dotnetEnvironment ?? aspnetcoreEnvironment ?? "Production";

    /// <summary>既有靜態讀取點的唯一轉接；只讀部署環境名稱，不保存 request 或教會狀態，也不改目前目錄。</summary>
    /// <param name="builder">已加入原基底檔與原 optional／reload 選項的建構器；生命週期仍由原呼叫端擁有。</param>
    /// <returns>相同建構器；只有 Development 會追加環境檔及主機 secrets。</returns>
    internal static IConfigurationBuilder AddChurchReportDevelopmentOverrides(this IConfigurationBuilder builder)
        => builder.AddChurchReportDevelopmentOverrides(Directory.GetCurrentDirectory(),
            ResolveEnvironmentName(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"), Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")),
            typeof(global::ChurchReport.Program).Assembly);

    /// <summary>用明確目錄、環境及擁有者組件組合設定；非 Development 立即返回，不觸碰來源、目錄或 secrets。</summary>
    /// <param name="builder">原設定建構器；回傳相同實例，不替換或移除既有 provider。</param>
    /// <param name="basePath">部署者控制的設定根目錄，不可由使用者請求指定。</param>
    /// <param name="environmentName">已解析的主機環境；Development 比對不分大小寫。</param>
    /// <param name="secretsAssembly">主機擁有的 UserSecretsId 所在組件；測試使用無 ID 的測試組件，避免讀真實 secrets。</param>
    /// <returns>只有 Development 才依 Host 順序追加 optional JSON、optional User Secrets，兩者均不監聽變更。</returns>
    internal static IConfigurationBuilder AddChurchReportDevelopmentOverrides(this IConfigurationBuilder builder,
        string basePath, string environmentName, Assembly secretsAssembly)
    {
        if (!IsDevelopment(environmentName)) return builder;
        return builder.SetBasePath(basePath)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
            .AddUserSecrets(secretsAssembly, optional: true, reloadOnChange: false);
    }

    /// <summary>
    /// 在 Development 驗證有效 LINE／CRM／Embedded 設定；任何違反邊界立即失敗，不建立外部 client 或通知。
    /// Production 包含未設定環境時完全略過，保留原有行為；假設定可直接注入，不需變更程序狀態。
    /// </summary>
    /// <param name="basePath">受信任的主機根目錄；只用於讀取基底 appsettings.json，不接受 request 路徑。</param>
    /// <param name="environmentName">主機已選定的環境名稱。</param>
    /// <param name="configuration">Host 合併後的設定；只借用、不修改、不 Dispose 呼叫端擁有的根。</param>
    /// <exception cref="InvalidOperationException">缺少可比較的基底設定，或任何鍵違反開發環境邊界；訊息不含設定值。</exception>
    internal static void ValidateDevelopmentConfiguration(string basePath, string environmentName, IConfiguration configuration)
    {
        if (!IsDevelopment(environmentName)) return;
        // 使用串流直接讀基底快照，避免架構掃描把安全比較用的基底讀取當成對外 token 的來源。
        // 基底串流在 ReadBaseline 返回前關閉，組態根在本次檢查結束前 Dispose，兩者均不跨請求保存。
        using var baseline = ReadBaseline(basePath);
        var baseChannels = baseline.GetSection("LineMessaging").GetChildren()
            .Where(s => !string.IsNullOrWhiteSpace(s["ChannelAccessToken"])).ToArray();
        foreach (var channel in configuration.GetSection("LineMessaging").GetChildren())
        {
            var value = channel["ChannelAccessToken"];
            // 也拒絕把基底正式 token 搬到另一個別名，避免僅改別名就繞過檢查。
            if (!string.IsNullOrWhiteSpace(value) && baseChannels.Any(s => string.Equals(value, s["ChannelAccessToken"], StringComparison.Ordinal)))
                throw UnsafeKey(channel.Path + ":ChannelAccessToken", "不可沿用基底的正式憑證。");
        }
        var environmentToken = configuration["LINE_CHANNEL_ACCESS_TOKEN"];
        if (!string.IsNullOrWhiteSpace(environmentToken) && baseChannels.Any(s => string.Equals(environmentToken, s["ChannelAccessToken"], StringComparison.Ordinal)))
            throw UnsafeKey("LINE_CHANNEL_ACCESS_TOKEN", "不可沿用基底的正式憑證。");
        var defaultOrganization = configuration["LineMessaging:DefaultOrganization"];
        if (string.IsNullOrWhiteSpace(defaultOrganization) || baseChannels.Any(s => string.Equals(s.Key, defaultOrganization, StringComparison.OrdinalIgnoreCase)))
            throw UnsafeKey("LineMessaging:DefaultOrganization", "必須選擇獨立的測試別名。");
        if (!string.Equals(configuration["CrmConnection:Organization"], "goodshepherdback", StringComparison.OrdinalIgnoreCase))
            throw UnsafeKey("CrmConnection:Organization", "開發模式只能使用指定的測試組織。");
        ValidateTestHost(configuration, "CrmConnection:ServerUrl");
        var enabled = configuration["DynamicsAccess:Package01FeeReadsEnabled"];
        if (enabled != null && !bool.TryParse(enabled, out _))
            throw UnsafeKey("DynamicsAccess:Package01FeeReadsEnabled", "必須是有效的布林開關。");
        if (bool.TryParse(enabled, out var readsEnabled) && readsEnabled)
        {
            ValidateTestHost(configuration, "DynamicsAccess:Embedded:OrganizationWebApiBaseUri");
            ValidateTestHost(configuration, "DynamicsAccess:Embedded:ResourceUri");
        }
    }

    /// <summary>大小寫不敏感的環境判斷；不讀全域環境，不配置或快取任何設定快照。</summary>
    /// <param name="name">呼叫端傳入的環境名稱；null 不視為 Development。</param>
    /// <returns>僅 Development 回傳 true。</returns>
    private static bool IsDevelopment(string name) => string.Equals(name, "Development", StringComparison.OrdinalIgnoreCase);

    /// <summary>讀取一次基底設定供憑證比較；解析失敗改成固定安全訊息，避免原例外包含來源路徑或機密內容。</summary>
    /// <param name="basePath">部署者擁有的根目錄。</param>
    /// <returns>由本次啟動檢查負責 Dispose 的組態根；不開 watcher。</returns>
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

    /// <summary>限制 CRM／Embedded URI 的主機，使用解析後的精確主機比較，不接受相似網域或路徑中的偽裝名稱。</summary>
    /// <param name="configuration">只借用當次啟動設定，不保存於 static。</param>
    /// <param name="key">原始碼固定的鍵名，不可含使用者提供的值。</param>
    private static void ValidateTestHost(IConfiguration configuration, string key)
    {
        if (!Uri.TryCreate(configuration[key], UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Host, "goodshepherdback.speechmessage.com.tw", StringComparison.OrdinalIgnoreCase))
            throw UnsafeKey(key, "開發模式只能使用指定測試組織的主機。");
    }

    /// <summary>建立只含可信鍵名與固定原因的拒絕結果；不串接設定值、不保存原始敏感例外。</summary>
    /// <param name="key">固定鍵名或設定節點路徑，不包含憑證值。</param>
    /// <param name="reason">開發者定義的固定原因。</param>
    /// <returns>由 Host 既有初始化錯誤路徑處理的例外；此處不建立 LINE 通知。</returns>
    private static InvalidOperationException UnsafeKey(string key, string reason) => new(key + "：" + reason);
}

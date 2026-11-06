/*
 * 檔案整體說明
 *
 * 目的與定位：讓舊式行程級設定讀取點在 Development 套用與主機相同的檔案及 secrets，並在任何 CRM／LINE client 建立前驗證安全邊界。 不保存請求、使用者、教會、設定值或憑證；非
 * Development 只追加專用 LINE__ provider， Development 不追加正式環境 provider。
 *
 * 主要功能與流程：
 * 本檔宣告 ChurchReportDevelopmentConfiguration。
 * - 依現有 WebApplication Host 的順序選擇環境；純函式不讀取或修改程序的環境變數。
 * - 既有靜態讀取點的唯一轉接；只讀部署環境名稱，不保存 request 或教會狀態，也不改目前目錄。
 * - 用明確目錄、環境及擁有者組件組合設定；非 Development 立即返回，不觸碰來源、目錄或 secrets。
 * - 在非 Development 組態建構器最後加入專用的 LINE__ 環境變數 provider。 後加入的 provider 具有較高優先權，讓正式主機可覆寫已清空的基底 JSON， 同時不把 LINE token
 * 放回版本控制；Development 不加入此來源，避免把正式部署 的命名慣例誤當成測試秘密來源。provider 不啟用 reload，生命週期由呼叫端組態根擁有。
 * 主要操作入口包含 ResolveEnvironmentName、AddChurchReportDevelopmentOverrides、
 * AddChurchReportDeploymentLineEnvironmentOverrides、ValidateDevelopmentConfiguration、IsDevelopment、
 * IsValueFromUserSecrets、ReadBaseline、ValidateTestHost；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Program.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

namespace ChurchReport.Configuration;

/// <summary>
/// 讓舊式行程級設定讀取點在 Development 套用與主機相同的檔案及 secrets，並在任何 CRM／LINE client 建立前驗證安全邊界。
/// 不保存請求、使用者、教會、設定值或憑證；非 Development 只追加專用 LINE__ provider，
/// Development 不追加正式環境 provider。
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
    /// <returns>相同建構器；Development 追加環境檔及主機 secrets，其他環境追加 LINE__ provider。</returns>
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
        if (!IsDevelopment(environmentName))
            return builder.AddChurchReportDeploymentLineEnvironmentOverrides(environmentName);

        return builder.SetBasePath(basePath)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
            .AddUserSecrets(secretsAssembly, optional: true, reloadOnChange: false);
    }

    /// <summary>
    /// 在非 Development 組態建構器最後加入專用的 LINE__ 環境變數 provider。
    /// 後加入的 provider 具有較高優先權，讓正式主機可覆寫已清空的基底 JSON，
    /// 同時不把 LINE token 放回版本控制；Development 不加入此來源，避免把正式部署
    /// 的命名慣例誤當成測試秘密來源。provider 不啟用 reload，生命週期由呼叫端組態根擁有。
    /// </summary>
    /// <param name="builder">已加入 JSON 或既有環境來源的組態建構器。</param>
    /// <param name="environmentName">Host 已解析的環境名稱。</param>
    /// <returns>同一個建構器。</returns>
    internal static IConfigurationBuilder AddChurchReportDeploymentLineEnvironmentOverrides(
        this IConfigurationBuilder builder,
        string environmentName)
        => IsDevelopment(environmentName)
            ? builder
            : builder.AddEnvironmentVariables(prefix: "LINE__");

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
        // 別名與憑證來源分開：即使基底 token 已清空，正式別名仍不可成為開發預設，
        // 任何正式別名只要在合併組態中出現非空 token 就拒絕，不依賴基底仍保存秘密。
        // 此陣列只屬本次啟動檢查，由 using 擁有的基底根供應，不跨請求或存入快取。
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
        var environmentToken = configuration["LINE_CHANNEL_ACCESS_TOKEN"];
        if (!string.IsNullOrWhiteSpace(environmentToken))
            throw UnsafeKey("LINE_CHANNEL_ACCESS_TOKEN", "Development 不得使用通用 LINE 環境憑證。");
        var defaultOrganization = configuration["LineMessaging:DefaultOrganization"];
        if (string.IsNullOrWhiteSpace(defaultOrganization) || baselineAliases.Any(s => string.Equals(s.Key, defaultOrganization, StringComparison.OrdinalIgnoreCase)))
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

    /// <summary>
    /// 以組態 provider 的覆寫順序判定 DevTest token 是否由 User Secrets 提供。
    /// 反向尋找第一個真正提供該鍵的 JSON provider，容忍 Host 與自訂擴充重複註冊
    /// secrets provider；判斷只使用固定檔名，不保存或輸出 token。
    /// </summary>
    /// <param name="configuration">Host 或測試建立的組態根。</param>
    /// <param name="key">固定的組態鍵名。</param>
    /// <returns>只有最後覆寫該鍵的 provider 是 secrets.json 才回傳 true。</returns>
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

    /// <summary>
    /// CRM／Embedded 只接受測試主機的標準 HTTPS；拒絕 URI 內帳密、額外埠號及 query／fragment。
    /// CRM 另限制完整 Organization.svc 路徑，避免錯誤端點在建線時才發現或明文傳輸憑證。
    /// 只解析一次當次借用值，不建立連線、不記錄或保留原始設定。
    /// </summary>
    /// <param name="configuration">只借用當次啟動設定，不保存於 static。</param>
    /// <param name="key">原始碼固定的鍵名，不可含使用者提供的值。</param>
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

    /// <summary>建立只含可信鍵名與固定原因的拒絕結果；不串接設定值、不保存原始敏感例外。</summary>
    /// <param name="key">固定鍵名或設定節點路徑，不包含憑證值。</param>
    /// <param name="reason">開發者定義的固定原因。</param>
    /// <returns>由 Host 既有初始化錯誤路徑處理的例外；此處不建立 LINE 通知。</returns>
    private static InvalidOperationException UnsafeKey(string key, string reason) => new(key + "：" + reason);
}

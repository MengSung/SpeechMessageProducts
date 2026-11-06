using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SpeechMessage.Tenancy;

/// <summary>從已驗證租戶解析單次頻道，不快取任何使用者、租戶結果或憑證。</summary>
public interface ILineChannelResolver
{
    /// <summary>必填解析；缺少有效 token 明確失敗，不能把空憑證交給下一位使用者。</summary>
    /// <param name="tenant">host 已授權範圍。</param>
    /// <returns>本次 owner 擁有的不可變結果。</returns>
    ResolvedLineChannel Resolve(TenantId tenant);
    /// <summary>可選相容路徑只對未配置回 false，設定來源故障必須保留原例外。</summary>
    /// <param name="tenant">host 已授權範圍。</param>
    /// <param name="channel">完整結果或 null，不回用上一筆結果。</param>
    /// <returns>本次取得有效憑證時為真。</returns>
    bool TryResolve(TenantId tenant, [NotNullWhen(true)] out ResolvedLineChannel? channel);
}

/// <summary>借用部署設定與 logger；沒有新 provider、檔案監聽、queue 或 disposable owner。</summary>
public sealed class ConfigurationLineChannelResolver : ILineChannelResolver
{
    /// <summary>固定安全後備事件，只保存靜態樣板，不捕捉租戶、token 或 request。</summary>
    private static readonly Action<ILogger, Exception?> FallbackWarning = LoggerMessage.Define(LogLevel.Warning,
        new EventId(1101, "LegacyChannelFallback"), "LINE channel uses the configured legacy default.");
    /// <summary>固定未配置事件，正常可選路徑不偽造 Exception 或輸出來源值。</summary>
    private static readonly Action<ILogger, Exception?> MissingWarning = LoggerMessage.Define(LogLevel.Warning,
        new EventId(1102, "ChannelNotConfigured"), "LINE channel credential is not configured.");
    /// <summary>外部 host 擁有的受信任設定，只借用，不建立 watcher 或 Dispose 外部 provider。</summary>
    private readonly IConfiguration configuration;
    /// <summary>固定產品識別，不保存單次租戶資料。</summary>
    private readonly string product;
    /// <summary>外部 logger 或無資源的固定 Trace bridge，僅記錄固定事件。</summary>
    private readonly ILogger logger;
    /// <summary>固定部署政策，不由訊息或使用者切換。</summary>
    private readonly LineChannelSelection selection;
    /// <summary>建立固定產品的解析器；每次結果獨立，設定及 logger 由外部 host 擁有。</summary>
    /// <param name="configuration">已套用安全防護與 provider 順序的部署設定。</param>
    /// <param name="product">本解析器可服務的固定產品。</param>
    /// <param name="logger">只接收固定安全 warning 的 logger。</param>
    /// <param name="selection">部署端選定的舊優先序。</param>
    public ConfigurationLineChannelResolver(IConfiguration configuration, string product,
        ILogger<ConfigurationLineChannelResolver>? logger = null, LineChannelSelection selection = LineChannelSelection.OrganizationThenDefault)
    {
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.product = TenantId.RequireIdentifier(product, nameof(product));
        if (selection is < LineChannelSelection.OrganizationThenDefault or > LineChannelSelection.EnvironmentThenOrganizationThenDefault)
            throw new ArgumentOutOfRangeException(nameof(selection));
        this.selection = selection;
        this.logger = (ILogger?)logger ?? SafeTraceLogger.Instance;
    }
    /// <summary>缺有效憑證時失敗，不吞掉設定來源真正例外。</summary>
    /// <param name="tenant">已驗證範圍。</param>
    /// <returns>完整不可變結果。</returns>
    public ResolvedLineChannel Resolve(TenantId tenant) => TryResolve(tenant, out var channel)
        ? channel : throw new InvalidOperationException("LINE channel credential is not configured.");
    /// <summary>供既有可選設定路徑辨識未配置；只有未配置回 false，真正來源故障仍拋出。</summary>
    /// <param name="tenant">已驗證範圍。</param>
    /// <param name="channel">成功結果或 null，不保留前一次值。</param>
    /// <returns>取得有效憑證時為真。</returns>
    public bool TryResolve(TenantId tenant, [NotNullWhen(true)] out ResolvedLineChannel? channel)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        if (!string.Equals(product, tenant.Product, StringComparison.Ordinal))
            throw new ArgumentException("Tenant product does not match the resolver owner.", nameof(tenant));
        return TryResolveConfiguration(tenant.Organization, out channel);
    }

    /// <summary>舊部署建構式沒有 CRM 組織時直接略過組織層；不捏造可能撞到真實別名的 sentinel。</summary>
    /// <param name="channel">本次完整結果或 null。</param>
    /// <returns>預設政策是否取得有效憑證。</returns>
    internal bool TryResolveDefault([NotNullWhen(true)] out ResolvedLineChannel? channel) => TryResolveConfiguration(null, out channel);

    /// <summary>唯一設定選擇實作，所有公開與舊入口均委派此方法；局部候選不會被其他操作讀到。</summary>
    /// <param name="organization">已驗證組織，或明確沒有組織層。</param>
    /// <param name="channel">完整驗證完成後才發布的結果。</param>
    /// <returns>本次取得有效憑證時為真。</returns>
    private bool TryResolveConfiguration(string? organization, [NotNullWhen(true)] out ResolvedLineChannel? channel)
    {
        channel = null;
        var alias = organization ?? string.Empty;
        string? token = null;
        var usedFallback = false;
        var usesEnvironment = false;
        if (selection is LineChannelSelection.EnvironmentThenDefault or LineChannelSelection.EnvironmentThenOrganizationThenDefault)
        {
            token = configuration["LINE_CHANNEL_ACCESS_TOKEN"];
            if (!string.IsNullOrWhiteSpace(token)) { alias = "environment"; usesEnvironment = true; }
            else token = null;
        }
        if (token is null && organization is not null &&
            selection is LineChannelSelection.OrganizationThenDefault or LineChannelSelection.EnvironmentThenOrganizationThenDefault)
        {
            token = configuration["LineMessaging:" + organization + ":ChannelAccessToken"];
            // 一般產品原先用 IsNullOrEmpty；告警 sender 原先用 IsNullOrWhiteSpace，保留各自優先序。
            if (selection == LineChannelSelection.EnvironmentThenOrganizationThenDefault
                    ? string.IsNullOrWhiteSpace(token) : string.IsNullOrEmpty(token)) token = null;
        }
        if (token is null)
        {
            alias = TenantId.RequireIdentifier(configuration["LineMessaging:DefaultOrganization"] ?? "Jesus", "defaultOrganization");
            token = configuration["LineMessaging:" + alias + ":ChannelAccessToken"];
            usedFallback = true;
            if (selection == LineChannelSelection.DefaultThenEnvironment && token is null)
            {
                token = configuration["LINE_CHANNEL_ACCESS_TOKEN"];
                if (token is not null) { alias = "environment"; usesEnvironment = true; }
            }
            FallbackWarning(logger, null);
        }
        if (string.IsNullOrWhiteSpace(token))
        {
            MissingWarning(logger, null);
            return false;
        }
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = token.Substring(7);
        // 來源必須獨立記錄；真實組織／別名也可叫 environment，不能讓顯示別名決定憑證配對。
        var channelId = usesEnvironment ? configuration["LINE_CHANNEL_ID"]
            : configuration["LineMessaging:" + alias + ":ChannelId"];
        channel = new ResolvedLineChannel(token, channelId, alias, usedFallback);
        return true;
    }

    /// <summary>舊靜態入口尚未能取得 host ILogger 時的固定 bridge；不建立 listener、檔案、scope 或 queue。</summary>
    private sealed class SafeTraceLogger : ILogger
    {
        /// <summary>無狀態共用實例，只輸出固定訊息，不保留日誌 state 或 Exception。</summary>
        internal static SafeTraceLogger Instance { get; } = new();
        /// <summary>沒有 scope 資源或保留。</summary>
        /// <typeparam name="TState">不保留的 scope 型別。</typeparam>
        /// <param name="state">不保留狀態。</param>
        /// <returns>沒有 scope。</returns>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        /// <summary>只接受預期的 warning 事件。</summary>
        /// <param name="logLevel">日誌等級。</param>
        /// <returns>warning 時為真。</returns>
        public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Warning;
        /// <summary>永遠寫固定文字，絕不把來源例外、tenant、token 或任意 state 交給 Trace。</summary>
        /// <typeparam name="TState">不保留的狀態型別。</typeparam>
        /// <param name="logLevel">等級。</param>
        /// <param name="eventId">固定事件。</param>
        /// <param name="state">不保留或輸出的狀態。</param>
        /// <param name="exception">不保留或輸出的例外。</param>
        /// <param name="formatter">不執行的來源格式器。</param>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) Trace.TraceWarning("LINE channel uses a legacy fallback or has no configured credential.");
        }
    }
}

/// <summary>教會設定純值入口，不以 client 提供的 ID 作授權，也不快取可變結果。</summary>
public interface ITenantSettingsProvider
{
    /// <summary>僅讀完整教會範圍，全部欄位驗證後一次回傳。</summary>
    /// <param name="tenant">含非空正式教會 ID 的已授權範圍。</param>
    /// <returns>完整不可變快照。</returns>
    TenantSettings GetSettings(TenantId tenant);
}

/// <summary>讀取完整 organization/product/church 設定鍵；只借用外部設定，不建新 provider 或快取。</summary>
public sealed class ConfigurationTenantSettingsProvider : ITenantSettingsProvider
{
    /// <summary>外部 host 擁有的固定設定，不建立 provider 或背景資源。</summary>
    private readonly IConfiguration configuration;
    /// <summary>本 provider 唯一服務的產品邊界。</summary>
    private readonly string product;
    /// <summary>固定產品 owner 借用啟動設定。</summary>
    /// <param name="configuration">受信任設定。</param>
    /// <param name="product">固定產品。</param>
    public ConfigurationTenantSettingsProvider(IConfiguration configuration, string product)
    {
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.product = TenantId.RequireIdentifier(product, nameof(product));
    }
    /// <summary>完整 materialize 後才發布，不回傳 IConfigurationSection 或來源可變集合。</summary>
    /// <param name="tenant">完整已授權範圍。</param>
    /// <returns>不可變結果。</returns>
    public TenantSettings GetSettings(TenantId tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        if (!string.Equals(product, tenant.Product, StringComparison.Ordinal) || tenant.ChurchId is null)
            throw new ArgumentException("A complete authorized church and product scope is required.", nameof(tenant));
        var prefix = "Tenancy:" + tenant.Organization + ":" + tenant.Product + ":" + tenant.ChurchId.Value.ToString("D") + ":";
        var enabled = Read(prefix, "EnabledCultures", "zh-TW", 4096).Split(',');
        if (enabled.Length > 64) throw new ArgumentException("Enabled cultures exceed their bounded count.");
        for (var index = 0; index < enabled.Length; index++) enabled[index] = enabled[index].Trim();
        var locale = new SpeechMessage.Localization.ChurchLocaleSettings(Read(prefix, "TextCulture", "zh-TW", 128),
            enabled, Read(prefix, "FormatCulture", "zh-TW", 128));
        var zone = Read(prefix, "TimeZoneId", "Asia/Taipei", 128);
        if (string.IsNullOrWhiteSpace(zone)) throw new ArgumentException("A nonempty church time zone is required.");
        var country = ReadCode(prefix, "Country", "TW", 2);
        var currency = ReadCode(prefix, "Currency", "TWD", 3);
        var brand = Read(prefix, "BrandName", string.Empty, 256);
        return new TenantSettings(tenant, locale, zone, country, currency, brand);
    }

    /// <summary>逐欄限長並拒絕控制字元，故障直接向上傳遞；只回純值，不暴露設定 section。</summary>
    /// <param name="prefix">由完整已驗證 scope 建立的固定前綴。</param>
    /// <param name="name">固定欄位。</param>
    /// <param name="fallback">核定缺省值。</param>
    /// <param name="maximum">固定長度上限。</param>
    /// <returns>已驗證純字串。</returns>
    private string Read(string prefix, string name, string fallback, int maximum)
    {
        var value = configuration[prefix + name] ?? fallback;
        if (value.Length > maximum) throw new ArgumentException("Tenant setting exceeds its bounded length.");
        foreach (var character in value)
            if (char.IsControl(character)) throw new ArgumentException("Tenant setting contains control characters.");
        return value;
    }

    /// <summary>國家／幣別身份只接受固定寬度 ASCII 代碼；業務白名單與金額顯示仍由金額地基負責。</summary>
    /// <param name="prefix">完整已授權範圍。</param>
    /// <param name="name">固定欄位。</param>
    /// <param name="fallback">核定缺省值。</param>
    /// <param name="length">代碼固定長度。</param>
    /// <returns>Invariant 大寫純代碼。</returns>
    private string ReadCode(string prefix, string name, string fallback, int length)
    {
        var value = Read(prefix, name, fallback, length);
        if (value.Length != length) throw new ArgumentException("Tenant setting code has an invalid length.");
        foreach (var character in value)
            if (!char.IsAsciiLetter(character)) throw new ArgumentException("Tenant setting code requires ASCII letters.");
        return value.ToUpperInvariant();
    }
}

/*
 * 檔案整體說明
 *
 * 目的與定位：本檔整合下列相關契約：TenantId：host 驗證後的組織／產品及可選教會純值；建構值物件本身不授權，不攜帶登入、request 或憑證。；
 * LineChannelSelection：明確記錄舊呼叫點的設定優先序；不是由使用者輸入控制的路由選項。；ResolvedLineChannel：單次解析結果；憑證只活到本次送訊 owner 結束，禁止快取或記錄原始 
 * token。
 *
 * 主要功能與流程：
 * 本檔宣告 TenantId、LineChannelSelection、ResolvedLineChannel、TenantSettings。
 * - 可選正式教會 ID；部署頻道可只有組織，但讀教會設定時必須提供。
 * - 驗證識別格式，不保存任意 request 或取得資料存取權。
 * - 只允許有界 ASCII 設定識別，不修剪或接受冒號／路徑，避免租戶字串變成設定路由權限。
 * - 舊 Processor 先環境，再部署預設，不增加 CRM 組織層。
 * 主要操作入口包含 RequireIdentifier、GetAccessTokenForLegacyClient、ToString。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Tenancy.Tests/ConfigurationResolverTests.cs、
 * SpeechMessage.Tenancy.Tests/TenancyCompositionTests.cs、SpeechMessage.Tenancy.Tests/TenantSettingsTests.cs、
 * SpeechMessage.Tenancy/ConfigurationLineChannelResolver.cs、
 * SpeechMessage.Tenancy/TenancyServiceCollectionExtensions.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using LineMessagingProcessor.Core;
using SpeechMessage.Localization;

namespace SpeechMessage.Tenancy;

/// <summary>host 驗證後的組織／產品及可選教會純值；建構值物件本身不授權，不攜帶登入、request 或憑證。</summary>
public sealed class TenantId
{
    /// <summary>受信任組織設定邊界，不容許插入設定路徑。</summary>
    public string Organization { get; } = string.Empty;
    /// <summary>受信任產品邊界，不能用另一產品的 resolver。</summary>
    public string Product { get; } = string.Empty;
    /// <summary>可選正式教會 ID；部署頻道可只有組織，但讀教會設定時必須提供。</summary>
    public Guid? ChurchId { get; }
    /// <summary>驗證識別格式，不保存任意 request 或取得資料存取權。</summary>
    /// <param name="organization">已授權組織。</param>
    /// <param name="product">已授權產品。</param>
    /// <param name="churchId">可選非空正式教會 ID。</param>
    public TenantId(string organization, string product, Guid? churchId = null)
    {
        Organization = RequireIdentifier(organization, nameof(organization));
        Product = RequireIdentifier(product, nameof(product));
        if (churchId == Guid.Empty) throw new ArgumentException("A persisted church identity cannot be empty.", nameof(churchId));
        ChurchId = churchId;
    }

    /// <summary>只允許有界 ASCII 設定識別，不修剪或接受冒號／路徑，避免租戶字串變成設定路由權限。</summary>
    /// <param name="value">host 已驗證的組織、產品或頻道別名。</param>
    /// <param name="parameter">固定參數名稱，不包含來源值。</param>
    /// <returns>原始有效識別。</returns>
    internal static string RequireIdentifier(string value, string parameter)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128 || !char.IsAsciiLetterOrDigit(value[0]))
            throw new ArgumentException("A bounded configuration identifier is required.", parameter);
        foreach (var character in value)
            if (!char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-' and not '.')
                throw new ArgumentException("Configuration identifiers cannot contain path delimiters.", parameter);
        return value;
    }
}

/// <summary>明確記錄舊呼叫點的設定優先序；不是由使用者輸入控制的路由選項。</summary>
public enum LineChannelSelection
{
    /// <summary>一般產品先組織，缺 token 才使用部署預設。</summary>
    OrganizationThenDefault,
    /// <summary>舊 Processor 先環境，再部署預設，不增加 CRM 組織層。</summary>
    EnvironmentThenDefault,
    /// <summary>舊 Startup 用預設值的 null 合併規則，再環境；空字串不變成環境後備。</summary>
    DefaultThenEnvironment,
    /// <summary>告警專用 sender 先環境，再組織，再部署預設。</summary>
    EnvironmentThenOrganizationThenDefault
}

/// <summary>單次解析結果；憑證只活到本次送訊 owner 結束，禁止快取或記錄原始 token。</summary>
public sealed class ResolvedLineChannel
{
    /// <summary>只由本次結果擁有的 token 參照；不是 public property，不參與預設序列化或診斷。</summary>
    private readonly string accessToken;
    /// <summary>共用 Core 的不可變憑證，不建立另一套 token 型別。</summary>
    public LineChannelCredential Credential { get; } = null!;
    /// <summary>設定若有提供則回真正頻道 ID；舊設定缺值時為 null，不能捏造供應商 ID。</summary>
    public string? ChannelId { get; }
    /// <summary>選定的部署別名，不代表外部 LINE 數字 ID。</summary>
    public string ChannelAlias { get; } = string.Empty;
    /// <summary>明確標示本次使用舊預設後備。</summary>
    public bool UsedFallback { get; }
    /// <summary>舊 SDK 必須接收原始字串時才明確取得；呼叫者不得保存至共享狀態、序列化或記錄。</summary>
    /// <returns>只供本次舊 SDK 使用的 token。</returns>
    public string GetAccessTokenForLegacyClient() => accessToken;
    /// <summary>診斷永遠只回固定遮罩，不包含頻道、教會或 token。</summary>
    /// <returns>固定安全描述。</returns>
    public override string ToString() => "ResolvedLineChannel(***)";

    /// <summary>完整驗證 token 後才建構結果；不保存 IConfiguration、tenant、logger 或任何連線。</summary>
    /// <param name="token">已移除單一 Bearer 前綴的 header-safe 憑證。</param>
    /// <param name="channelId">真正設定的頻道 ID，允許舊設定缺少。</param>
    /// <param name="alias">選定部署別名。</param>
    /// <param name="usedFallback">是否啟用舊預設後備。</param>
    internal ResolvedLineChannel(string token, string? channelId, string alias, bool usedFallback)
    {
        if (token.Length > 8192) throw new ArgumentException("Channel credential exceeds its bounded length.", nameof(token));
        Credential = new LineChannelCredential(token);
        accessToken = token;
        if (channelId is not null)
        {
            if (channelId.Length == 0 || channelId.Length > 128)
                throw new ArgumentException("Channel identity exceeds its bounded length.", nameof(channelId));
            foreach (var character in channelId)
                if (char.IsControl(character)) throw new ArgumentException("Channel identity contains control characters.", nameof(channelId));
        }
        ChannelId = channelId;
        ChannelAlias = alias;
        UsedFallback = usedFallback;
    }
}

/// <summary>一次操作的完整教會設定快照，只有不可變純值，沒有 request、SDK 或資源擁有權。</summary>
public sealed class TenantSettings
{
    /// <summary>完整已授權範圍，不以語言改變教會。</summary>
    public TenantId Tenant { get; } = null!;
    /// <summary>來源隔離的文字／格式文化及啟用語系列表。</summary>
    public ChurchLocaleSettings Locale { get; } = null!;
    /// <summary>明確 IANA 時區識別，時鐘消費端另驗映射與 DST。</summary>
    public string TimeZoneId { get; } = string.Empty;
    /// <summary>國家代碼純值，不包含位置個資。</summary>
    public string Country { get; } = string.Empty;
    /// <summary>幣別身份，不能由格式文化推導。</summary>
    public string Currency { get; } = string.Empty;
    /// <summary>本教會品牌純文字，沒有設定時為空，由呼叫端選既有品牌後備。</summary>
    public string BrandName { get; } = string.Empty;

    /// <summary>只由 provider 在全部候選驗證完成後發布；值均不可變，locale 已複製來源清單。</summary>
    /// <param name="tenant">已授權完整範圍。</param>
    /// <param name="locale">已完成驗證的純文化設定。</param>
    /// <param name="timeZoneId">明確 IANA 識別。</param>
    /// <param name="country">正規化國家代碼。</param>
    /// <param name="currency">正規化幣別代碼。</param>
    /// <param name="brandName">已限長純品牌文字。</param>
    internal TenantSettings(TenantId tenant, ChurchLocaleSettings locale, string timeZoneId, string country, string currency, string brandName)
    {
        Tenant = tenant;
        Locale = locale;
        TimeZoneId = timeZoneId;
        Country = country;
        Currency = currency;
        BrandName = brandName;
    }
}

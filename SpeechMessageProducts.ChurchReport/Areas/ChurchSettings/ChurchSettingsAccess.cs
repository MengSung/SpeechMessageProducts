/*
 * 檔案整體說明
 *
 * 目的與定位：提供 ChurchSettings Area 唯一的伺服器端權限及教會範圍解析入口。
 * 主要流程：每次呼叫都從已驗證 principal 讀取伺服器簽發的 contact/login type claims，
 * 以目前 request 的 scoped IOrganizationService 重新讀 contact 與其伺服器端 account 語系，
 * 解析登入者顯示語系，再呼叫 MemberInfoAccessResolver 判定 Church 管理權限。授權成功才
 * 建立 AuthorizedChurch 與完整 TextContext；非管理員仍可在 403 頁使用自己的已解析語系。
 * 使用邊界與維護：不讀寫 Session，不保存登入者、組織、教會或 connection；解析結果只在單一
 * request 使用。Route、query、form、JSON 及 ViewModel 的 church ID 均不參與判斷。
 * SDK service 由 Host scoped owner 管理，本類只借用；取消沿用 RequestAborted，查詢失敗不授權。
 * 失敗與驗證：未登入、claim 無效、CRM 查無 contact 或 account 關聯無效時以繁中作文化 fallback；
 * 非 Church 權限或缺少固定部署組織時回傳無授權 scope，但保留可解析的登入者文化供 403 使用。
 * resource filter 將此解析值限制於單一 request 並在 finally 移除；離線測試驗證授權、語系及還原。
 * 文字快取載入：EnsureTextScopeLoadedAsync 只在已授權 scope 之後呼叫，讀 account 的 new_textchangedon
 * 並交由 Host 擁有的 ChurchTextOverrideCache 載入該教會快照（cache 為 null 代表覆寫功能停用，無副作用）；
 * ChurchTextOverrideCache.GetSnapshot 對未載入 scope 會拒絕，所以預覽文字前必須先呼叫。本類不擁有也不釋放 cache。
 */
using System;
using System.Threading;
using System.Threading.Tasks;
using ChurchReport.Security;
using ChurchReport.Services.MemberInfo;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Localization;
using SpeechMessage.Tenancy;
using SpeechMessageProducts.GoodShepherd.Core.Settings;

namespace ChurchReport.Areas.ChurchSettings;

/// <summary>單次 request 內已重新驗證的管理員及其教會文字範圍，不含 SDK Entity。</summary>
internal sealed record ChurchSettingsRequestScope(
    AuthorizedChurch Church,
    TextContext TextContext,
    Guid ContactId);

/// <summary>單一 ChurchSettings request 的授權結果及顯示／格式文化，不保存 SDK Entity。</summary>
/// <param name="Scope">僅伺服器確認為教會管理員時存在的授權 scope。</param>
/// <param name="TextCulture">登入者在該教會可用的文字文化；無法解析時為 zh-TW。</param>
/// <param name="FormatCulture">本 request 格式文化；無法解析時為 zh-TW。</param>
internal sealed record ChurchSettingsRequestResolution(
    ChurchSettingsRequestScope? Scope,
    string TextCulture,
    string FormatCulture)
{
    /// <summary>使用型別身分作為 request-local item key，不含會跨 request 存活的資料。</summary>
    internal static object ItemKey => typeof(ChurchSettingsRequestResolution);
}

/// <summary>以伺服器登入身分建立 ChurchSettings 唯一授權範圍的 request-scoped resolver。</summary>
public sealed class ChurchSettingsAccess
{
    private readonly IOrganizationService organizationService;
    private readonly IConfiguration configuration;
    private readonly IMemberCultureResolver cultureResolver;
    private readonly ChurchTextOverrideCache? overrideCache;

    /// <summary>借用目前 HTTP scope 的 CRM service 與啟動組態；不擁有或釋放連線。</summary>
    /// <param name="organizationService">由 ToolUtility 在本 request 提供的 scoped Dataverse 服務。</param>
    /// <param name="configuration">Host 啟動組態，只讀取固定部署組織名稱。</param>
    /// <param name="cultureResolver">解析會友文字語系。</param>
    /// <param name="overrideCache">覆寫啟用時由 Host singleton 擁有的快取；停用時為 null。</param>
    public ChurchSettingsAccess(
        IOrganizationService organizationService,
        IConfiguration configuration,
        IMemberCultureResolver cultureResolver,
        ChurchTextOverrideCache? overrideCache = null)
    {
        this.overrideCache = overrideCache;
        this.organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.cultureResolver = cultureResolver ?? throw new ArgumentNullException(nameof(cultureResolver));
    }

    /// <summary>
    /// 為已授權 scope 載入該教會的覆寫快照，使同步 TextCatalog 之後能讀到；覆寫停用時不做任何事。
    /// </summary>
    /// <param name="scope">本 request 已重新驗證的 scope，教會 ID 不來自 request 輸入。</param>
    /// <param name="cancellationToken">只取消本次呼叫的等待。</param>
    /// <returns>快照載入完成；來源失敗且無舊快照時拋出原例外。</returns>
    internal async Task EnsureTextScopeLoadedAsync(
        ChurchSettingsRequestScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (overrideCache is null) return;
        cancellationToken.ThrowIfCancellationRequested();
        var account = organizationService.Retrieve("account", scope.Church.Id, new ColumnSet("new_textchangedon"));
        // 缺值使用固定 epoch；cache 版本只前進，既有較新的版本不會被回退。
        var stamp = account?.GetAttributeValue<DateTime?>("new_textchangedon") is { } changed
            ? (changed.Kind == DateTimeKind.Utc ? changed : changed.ToUniversalTime())
            : DateTime.UnixEpoch;
        await overrideCache.LoadAsync(scope.TextContext, stamp, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 重新載入本次登入 contact 與 account，解析顯示文化並判定管理員 scope。
    /// </summary>
    /// <param name="principal">由 ASP.NET Authentication 驗證的當前 principal。</param>
    /// <param name="cancellationToken">僅取消這次 request 的 CRM 查詢。</param>
    /// <returns>只含純值的解析結果；未授權時 Scope 為 null，文化不可解析時回退繁中。</returns>
    internal Task<ChurchSettingsRequestResolution> ResolveRequestAsync(
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (principal?.Identity?.IsAuthenticated != true)
            return Task.FromResult(FallbackResolution());
        if (!Guid.TryParse(principal.FindFirst(LoginClaimsFactory.ContactIdClaim)?.Value, out var contactId)
            || contactId == Guid.Empty)
            return Task.FromResult(FallbackResolution());

        cancellationToken.ThrowIfCancellationRequested();
        var contact = organizationService.Retrieve(
            "contact",
            contactId,
            new ColumnSet("new_church_jobtitle", "parentcustomerid", "new_preferredlanguage"));
        if (contact is null || contact.Id != contactId)
            return Task.FromResult(FallbackResolution());

        var parent = contact.GetAttributeValue<EntityReference>("parentcustomerid");
        if (parent is null
            || !string.Equals(parent.LogicalName, "account", StringComparison.Ordinal)
            || parent.Id == Guid.Empty)
            return Task.FromResult(FallbackResolution());

        cancellationToken.ThrowIfCancellationRequested();
        var account = organizationService.Retrieve(
            "account",
            parent.Id,
            new ColumnSet("new_defaultlanguage", "new_enabledlanguages"));
        if (account is null || account.Id != parent.Id)
            return Task.FromResult(FallbackResolution());
        var enabled = (account.GetAttributeValue<string>("new_enabledlanguages") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var locale = new ChurchLocaleSettings(
            account.GetAttributeValue<string>("new_defaultlanguage") ?? "zh-TW",
            enabled,
            "zh-TW");
        var resolved = cultureResolver.Resolve(
            new MemberLanguagePreference(contact.GetAttributeValue<string>("new_preferredlanguage")),
            locale);
        var organization = configuration["CrmConnection:Organization"];
        var jobTitle = contact.GetAttributeValue<string>("new_church_jobtitle") ?? string.Empty;
        var loginType = principal.FindFirst(LoginClaimsFactory.LoginTypeClaim)?.Value ?? string.Empty;
        if (string.IsNullOrWhiteSpace(organization)
            || !string.Equals(MemberInfoAccessResolver.Resolve(jobTitle, loginType),
                MemberInfoAccess.Church, StringComparison.Ordinal))
            return Task.FromResult(new ChurchSettingsRequestResolution(null,
                resolved.TextCulture, resolved.FormatCulture));

        var church = new AuthorizedChurch(parent.Id);
        var scope = new ChurchSettingsRequestScope(
            church,
            new TextContext(parent.Id, resolved.TextCulture, resolved.FormatCulture, organization, "ChurchReport"),
            contactId);
        return Task.FromResult(new ChurchSettingsRequestResolution(
            scope, resolved.TextCulture, resolved.FormatCulture));
    }

    /// <summary>相容直接呼叫的 controller 契約；MVC 正常路徑由 resource filter 共用同一解析結果。</summary>
    internal async Task<ChurchSettingsRequestScope?> ResolveAsync(
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken) =>
        (await ResolveRequestAsync(principal, cancellationToken).ConfigureAwait(false)).Scope;

    private static ChurchSettingsRequestResolution FallbackResolution() =>
        new(null, "zh-TW", "zh-TW");
}

/*
 * 檔案整體說明
 *
 * 目的與定位：提供 ChurchSettings Area 唯一的伺服器端權限及教會範圍解析入口。
 * 主要流程：每次呼叫都從已驗證 principal 讀取伺服器簽發的 contact/login type claims，
 * 以目前 request 的 scoped IOrganizationService 重新讀 contact，直接呼叫既有
 * MemberInfoAccessResolver，僅接受 Church 權限，並從該 contact 的 account parentcustomerid
 * 建立 AuthorizedChurch 與完整 TextContext。
 * 使用邊界與維護：不讀寫 Session，不保存登入者、組織、教會或 connection；解析結果只在單一
 * request 使用。Route、query、form、JSON 及 ViewModel 的 church ID 均不參與判斷。
 * SDK service 由 Host scoped owner 管理，本類只借用；取消沿用 RequestAborted，查詢失敗不授權。
 * 失敗與驗證：未登入、claim 無效、CRM 查無 contact、非 Church 權限、account 關聯無效或
 * 缺少固定部署組織時回傳 null，由 Controller 統一回 403；離線測試驗證 claims 與範圍契約。
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
using SpeechMessageProducts.GoodShepherd.Core.Settings;

namespace ChurchReport.Areas.ChurchSettings;

/// <summary>單次 request 內已重新驗證的管理員及其教會文字範圍，不含 SDK Entity。</summary>
internal sealed record ChurchSettingsRequestScope(
    AuthorizedChurch Church,
    TextContext TextContext,
    Guid ContactId);

/// <summary>以伺服器登入身分建立 ChurchSettings 唯一授權範圍的 request-scoped resolver。</summary>
public sealed class ChurchSettingsAccess
{
    private readonly IOrganizationService organizationService;
    private readonly IConfiguration configuration;
    private readonly IMemberCultureResolver cultureResolver;

    /// <summary>借用目前 HTTP scope 的 CRM service 與啟動組態；不擁有或釋放連線。</summary>
    /// <param name="organizationService">由 ToolUtility 在本 request 提供的 scoped Dataverse 服務。</param>
    /// <param name="configuration">Host 啟動組態，只讀取固定部署組織名稱。</param>
    public ChurchSettingsAccess(
        IOrganizationService organizationService,
        IConfiguration configuration,
        IMemberCultureResolver cultureResolver)
    {
        this.organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.cultureResolver = cultureResolver ?? throw new ArgumentNullException(nameof(cultureResolver));
    }

    /// <summary>
    /// 重新載入本次登入 contact 並判定管理員及 account 教會範圍；失敗一律不建立授權值。
    /// </summary>
    /// <param name="principal">由 ASP.NET Authentication 驗證的當前 principal。</param>
    /// <param name="cancellationToken">僅取消這次 request 的 CRM 查詢。</param>
    /// <returns>驗證完成的短生命週期 scope；不具有效權限或教會時為 null。</returns>
    internal Task<ChurchSettingsRequestScope?> ResolveAsync(
        System.Security.Claims.ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (principal?.Identity?.IsAuthenticated != true) return Task.FromResult<ChurchSettingsRequestScope?>(null);
        if (!Guid.TryParse(principal.FindFirst(LoginClaimsFactory.ContactIdClaim)?.Value, out var contactId)
            || contactId == Guid.Empty)
            return Task.FromResult<ChurchSettingsRequestScope?>(null);

        cancellationToken.ThrowIfCancellationRequested();
        var contact = organizationService.Retrieve(
            "contact",
            contactId,
            new ColumnSet("new_church_jobtitle", "parentcustomerid", "new_preferredlanguage"));
        if (contact is null || contact.Id != contactId) return Task.FromResult<ChurchSettingsRequestScope?>(null);

        var jobTitle = contact.GetAttributeValue<string>("new_church_jobtitle") ?? string.Empty;
        var loginType = principal.FindFirst(LoginClaimsFactory.LoginTypeClaim)?.Value ?? string.Empty;
        if (!string.Equals(MemberInfoAccessResolver.Resolve(jobTitle, loginType),
                MemberInfoAccess.Church, StringComparison.Ordinal))
            return Task.FromResult<ChurchSettingsRequestScope?>(null);

        var parent = contact.GetAttributeValue<EntityReference>("parentcustomerid");
        if (parent is null
            || !string.Equals(parent.LogicalName, "account", StringComparison.Ordinal)
            || parent.Id == Guid.Empty)
            return Task.FromResult<ChurchSettingsRequestScope?>(null);

        var organization = configuration["CrmConnection:Organization"];
        if (string.IsNullOrWhiteSpace(organization)) return Task.FromResult<ChurchSettingsRequestScope?>(null);

        cancellationToken.ThrowIfCancellationRequested();
        var account = organizationService.Retrieve(
            "account",
            parent.Id,
            new ColumnSet("new_defaultlanguage", "new_enabledlanguages"));
        if (account is null || account.Id != parent.Id) return Task.FromResult<ChurchSettingsRequestScope?>(null);
        var enabled = (account.GetAttributeValue<string>("new_enabledlanguages") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var locale = new ChurchLocaleSettings(
            account.GetAttributeValue<string>("new_defaultlanguage") ?? "zh-TW",
            enabled,
            "zh-TW");
        var resolved = cultureResolver.Resolve(
            new MemberLanguagePreference(contact.GetAttributeValue<string>("new_preferredlanguage")),
            locale);
        var church = new AuthorizedChurch(parent.Id);
        return Task.FromResult<ChurchSettingsRequestScope?>(new ChurchSettingsRequestScope(
            church,
            new TextContext(parent.Id, resolved.TextCulture, resolved.FormatCulture, organization, "ChurchReport"),
            contactId));
    }
}

/*
 * 檔案整體說明
 *
 * 目的與定位：提供教會管理員專用的名詞覆寫、奉獻類別及設定修改紀錄 MVC 頁面。
 * 主要流程：每個 GET 與 POST 均重新解析 ChurchSettingsAccess；只有登入 contact 的伺服器資料
 * 證明其為全教會管理員且 parentcustomerid 指向有效 account 時，才使用該 account 執行查詢／寫入。
 * 覆寫與類別寫入委派給 GoodShepherd.Core ChurchSettingsService，動態輸出交由 Razor 預設編碼。
 * AreaAttribute 是 View 的路由根源，不可移除，否則 MVC 只會搜尋非 Area View 位置。
 * Terms 在讀取預覽文字前必須先 EnsureTextScopeLoadedAsync，否則 cache 會因 scope 未載入而拒絕。
 * 使用邊界與維護：Controller、ViewModel 與 Entity 僅屬單次 request；不讀 Session、不快取授權，
 * 不接受 request church ID、不持有 connection owner。每個寫入 POST 都有 antiforgery 驗證，
 * RequestAborted 只控制本次呼叫。稽核及類別結果必須在 store 端先限定本教會並受 200 筆上限約束。
 * 語系生命週期：ChurchSettingsCultureFilter 在完整 MVC resource pipeline 套用已解析文化，controller
 * 從 HttpContext.Items 重用同次授權結果，避免第二次 CRM 讀取；filter finally 負責還原 ambient culture。
 * 失敗與驗證：無法建立授權 scope 回 403；輸入檢查與核心領域規則共用 ChurchSettingsService；
 * Dataverse 例外沿既有錯誤處理傳遞。測試採離線合成資料，不代表 D365 實機驗收。
 */
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Offerings;
using SpeechMessageProducts.GoodShepherd.Core.Settings;

namespace ChurchReport.Areas.ChurchSettings;

/// <summary>提供本教會可編輯詞彙、奉獻類別與稽核紀錄的 MVC action。</summary>
[Area("ChurchSettings")]
[Route("ChurchSettings")]
[ServiceFilter(typeof(ChurchSettingsCultureFilter))]
public sealed class ChurchSettingsController : Controller
{
    private readonly ChurchSettingsAccess access;
    private readonly ChurchSettingsService settings;
    private readonly IChurchSettingsStore store;
    private readonly IChurchSettingsTermReader termReader;
    private readonly ITextCatalog textCatalog;
    private readonly ISystemTextCatalog systemTextCatalog;

    /// <summary>注入 request-scoped 授權、設定服務與 CRM facade；不延長其生命週期。</summary>
    public ChurchSettingsController(
        ChurchSettingsAccess access,
        ChurchSettingsService settings,
        IChurchSettingsStore store,
        IChurchSettingsTermReader termReader,
        ITextCatalog textCatalog,
        ISystemTextCatalog systemTextCatalog)
    {
        this.access = access ?? throw new ArgumentNullException(nameof(access));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.termReader = termReader ?? throw new ArgumentNullException(nameof(termReader));
        this.textCatalog = textCatalog ?? throw new ArgumentNullException(nameof(textCatalog));
        this.systemTextCatalog = systemTextCatalog ?? throw new ArgumentNullException(nameof(systemTextCatalog));
    }

    /// <summary>顯示系統原文、教會目前覆寫及解析預覽；詞彙只取核心白名單。</summary>
    [HttpGet("Terms")]
    public async Task<IActionResult> Terms()
    {
        var scope = await ResolveScopeAsync().ConfigureAwait(false);
        if (scope is null) return ForbiddenAccess();

        await access.EnsureTextScopeLoadedAsync(scope, HttpContext.RequestAborted).ConfigureAwait(false);
        var keys = new ChurchTermWhitelist().Keys;
        var saved = await termReader.ReadTermOverridesAsync(
                scope.Church.Id, HttpContext.RequestAborted)
            .ConfigureAwait(false);
        if (saved is null || saved.Count > 200)
            throw new InvalidOperationException("The term reader exceeded its bounded result contract.");

        var identities = new System.Collections.Generic.HashSet<Guid>();
        var overrides = new System.Collections.Generic.Dictionary<(string Key, string Culture), string>();
        foreach (var value in saved)
        {
            if (value is null || value.Id == Guid.Empty || value.ChurchId != scope.Church.Id
                || !identities.Add(value.Id)
                || string.IsNullOrWhiteSpace(value.Culture)
                || value.Text is null)
                throw new InvalidOperationException("The term reader returned an invalid church-scoped candidate.");
            if (!keys.Contains(value.Key, StringComparer.Ordinal)) continue;
            if (!overrides.TryAdd((value.Key, value.Culture), value.Text))
                throw new InvalidOperationException("The term reader returned a duplicate editable key and culture.");
        }

        var rows = keys.Select(key =>
        {
            var textKey = new TextKey(key);
            var culture = scope.TextContext.TextCulture;
            var savedText = overrides.TryGetValue((key, culture), out var exactText)
                ? exactText
                : overrides.TryGetValue((key, "*"), out var wildcardText)
                    ? wildcardText
                    : string.Empty;
            return new ChurchTermRow(
                key,
                culture,
                systemTextCatalog.GetText(culture, textKey),
                savedText,
                textCatalog.GetText(scope.TextContext, textKey, GetPreviewArguments(key)));
        }).ToArray();
        return View("Terms", new ChurchTermsPageModel(keys, rows));
    }

    /// <summary>儲存核心白名單內單一教會詞彙覆寫。</summary>
    [HttpPost("Terms/Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTerm(string key, string culture, string text)
    {
        var scope = await ResolveScopeAsync().ConfigureAwait(false);
        if (scope is null) return ForbiddenAccess();
        await settings.SaveOverrideAsync(scope.Church, scope.TextContext, key, culture, text,
            HttpContext.RequestAborted).ConfigureAwait(false);
        return RedirectToAction(nameof(Terms));
    }

    /// <summary>移除單一白名單詞彙覆寫並回復系統文字。</summary>
    [HttpPost("Terms/Reset")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetTerm(string key, string culture)
    {
        var scope = await ResolveScopeAsync().ConfigureAwait(false);
        if (scope is null) return ForbiddenAccess();
        await settings.ResetOverrideAsync(scope.Church, scope.TextContext, key, culture,
            HttpContext.RequestAborted).ConfigureAwait(false);
        return RedirectToAction(nameof(Terms));
    }

    /// <summary>顯示本教會奉獻類別清單；正式資料庫 ID 是唯一列身分。</summary>
    [HttpGet("OfferingCategories")]
    public async Task<IActionResult> OfferingCategories()
    {
        var scope = await ResolveScopeAsync().ConfigureAwait(false);
        if (scope is null) return ForbiddenAccess();
        var categories = await store.ReadCategoriesAsync(scope.Church.Id, HttpContext.RequestAborted)
            .ConfigureAwait(false);
        return View("OfferingCategories", categories);
    }

    /// <summary>新增本教會奉獻類別；業務代碼由核心服務安全產生。</summary>
    [HttpPost("OfferingCategories/Save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCategory(
        Guid? id, string name, int sortOrder, string? accountingCode, int? legacyOptionValue, bool isActive = true)
    {
        var scope = await ResolveScopeAsync().ConfigureAwait(false);
        if (scope is null) return ForbiddenAccess();
        if (id is null)
        {
            await settings.AddOfferingCategoryAsync(scope.Church, scope.TextContext, name, sortOrder,
                accountingCode, legacyOptionValue, HttpContext.RequestAborted).ConfigureAwait(false);
        }
        else
        {
            await settings.UpdateOfferingCategoryAsync(scope.Church, scope.TextContext, id.Value, name,
                sortOrder, accountingCode, legacyOptionValue, isActive, HttpContext.RequestAborted)
                .ConfigureAwait(false);
        }
        return RedirectToAction(nameof(OfferingCategories));
    }

    /// <summary>顯示本教會最近最多 200 筆設定修改紀錄，actor 僅呈現 D365 顯示名稱。</summary>
    [HttpGet("Audit")]
    public async Task<IActionResult> Audit()
    {
        var scope = await ResolveScopeAsync().ConfigureAwait(false);
        if (scope is null) return ForbiddenAccess();
        var records = await settings.ReadAuditAsync(scope.Church, scope.TextContext, HttpContext.RequestAborted)
            .ConfigureAwait(false);
        return View("Audit", records);
    }

    private Task<ChurchSettingsRequestScope?> ResolveScopeAsync()
    {
        if (HttpContext.Items.TryGetValue(ChurchSettingsRequestResolution.ItemKey, out var value)
            && value is ChurchSettingsRequestResolution resolution)
            return Task.FromResult(resolution.Scope);
        return access.ResolveAsync(User, HttpContext.RequestAborted);
    }

    /// <summary>為固定通知範本提供非個資的範例參數；其他白名詞均不需要格式化參數。</summary>
    private static TextArg[] GetPreviewArguments(string key) =>
        string.Equals(key, "Fees.Notify.Template", StringComparison.Ordinal)
            ? new[]
            {
                new TextArg("member", "範例會友"),
                new TextArg("date", "2026/11/21"),
                new TextArg("category", "範例類別"),
                new TextArg("others", string.Empty),
                new TextArg("payway", "轉帳"),
                new TextArg("amount", "100")
            }
            : Array.Empty<TextArg>();

    /// <summary>
    /// 以固定本地化檢視回應明確 HTTP 403；不呼叫 Cookie handler 的 forbid challenge，
    /// 避免全站 AccessDeniedPath 將權限拒絕轉成登入頁重新導向。
    /// </summary>
    private IActionResult ForbiddenAccess()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View("Forbidden", ChurchSettingsText.GetText("ChurchSettings.Forbidden"));
    }
}

/// <summary>單一詞彙的系統原文、教會目前覆寫與套用完整 catalog 後的解析預覽。</summary>
/// <param name="Key">核心白名單中的固定詞彙鍵。</param>
/// <param name="Culture">本次 request 解析出的編輯語系。</param>
/// <param name="SystemText">不套用教會覆寫的原始系統資源文字。</param>
/// <param name="OverrideText">目前教會對此鍵／語系儲存的原文，沒有明確覆寫時為空字串。</param>
/// <param name="Preview">以完整文字 catalog、教會 scope 與合成範例參數解析後的結果。</param>
internal sealed record ChurchTermRow(
    string Key,
    string Culture,
    string SystemText,
    string OverrideText,
    string Preview);

/// <summary>名詞設定頁的不可變輸出模型；只包含核心白名單鍵與授權教會資料。</summary>
/// <param name="Keys">核心唯一白名單，不含關鍵字或 webhook 路由。</param>
/// <param name="Overrides">依白名單順序建置，且已驗證來源 scope 的純值列。</param>
internal sealed record ChurchTermsPageModel(
    System.Collections.Generic.IReadOnlyList<string> Keys,
    System.Collections.Generic.IReadOnlyList<ChurchTermRow> Overrides);

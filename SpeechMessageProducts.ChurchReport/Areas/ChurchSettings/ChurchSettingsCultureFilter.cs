/*
 * 檔案整體說明
 *
 * 目的與定位：在 ChurchSettings Area 的 MVC resource-filter 邊界套用本次 request 的文字與格式文化。
 * 主要流程：使用 ChurchSettingsAccess 依已驗證登入者及伺服器端教會資料解析語系，將純值結果暫存於
 * HttpContext.Items 供 controller 重用；在呼叫 MVC 後續 resource pipeline 前設定 CurrentUICulture 與
 * CurrentCulture，使 action、Razor result 與 view rendering 都處於相同文化範圍。
 * 信任與隔離：文化只來自本 request 已解析的會員偏好和 account 啟用清單；caller route／form 值不參與。
 * HttpContext.Items 與 ambient culture 均限於本次非同步 request，不寫 Session、static 或共享快取。
 * 資源生命週期：resource filter 是文化暫存與 scope item 的唯一 owner；無論結果成功或丟出例外，
 * finally 都先還原進入時的 CurrentCulture／CurrentUICulture，再移除 item，避免後續 request 污染。
 * 未登入、無有效教會或無法解析語系時使用固定 zh-TW；有效但非管理員的 403 仍使用其本人可用文化。
 * 驗證方式：離線 filter 契約測試逐 request 檢查 qps-ploc、後續 zh-TW、403 fallback 與 ambient 還原。
 */
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ChurchReport.Areas.ChurchSettings;

/// <summary>讓 ChurchSettings action 與其 Razor result 共用一份 request-scoped 語系解析結果。</summary>
internal sealed class ChurchSettingsCultureFilter : IAsyncResourceFilter
{
    private readonly ChurchSettingsAccess access;

    /// <summary>借用同一個 MVC scope 的授權／文化解析器，不擁有 Dataverse service。</summary>
    /// <param name="access">request-scoped ChurchSettings access。</param>
    public ChurchSettingsCultureFilter(ChurchSettingsAccess access) =>
        this.access = access ?? throw new System.ArgumentNullException(nameof(access));

    /// <summary>
    /// 在 action 與結果執行期間設定文化，並於成功、取消或例外時還原 ambient 狀態及 request item。
    /// </summary>
    /// <param name="context">本次 MVC resource context。</param>
    /// <param name="next">只執行本次 request 的後續 MVC pipeline。</param>
    public Task OnResourceExecutionAsync(
        ResourceExecutingContext context,
        ResourceExecutionDelegate next) =>
        InvokeAsync(context.HttpContext, async () => { await next().ConfigureAwait(false); });

    /// <summary>以同一套 finally 邊界包住 action／Razor 工作，供介面 adapter 與離線契約測試共用。</summary>
    /// <param name="httpContext">唯一屬於本次 request 的 context。</param>
    /// <param name="next">本 request 的後續工作；不得保存至 request 以外。</param>
    internal async Task InvokeAsync(
        Microsoft.AspNetCore.Http.HttpContext httpContext,
        System.Func<Task> next)
    {
        System.ArgumentNullException.ThrowIfNull(httpContext);
        System.ArgumentNullException.ThrowIfNull(next);
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var resolution = await access.ResolveRequestAsync(
                httpContext.User, httpContext.RequestAborted).ConfigureAwait(false);
            httpContext.Items[ChurchSettingsRequestResolution.ItemKey] = resolution;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(resolution.TextCulture);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(resolution.FormatCulture);
            await next().ConfigureAwait(false);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
            httpContext.Items.Remove(ChurchSettingsRequestResolution.ItemKey);
        }
    }
}

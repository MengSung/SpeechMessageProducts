/*
 * 檔案整體說明
 *
 * 目的與定位：在 ChurchReport 共用導覽列提供 ChurchSettings 入口，且只依伺服器重查的授權結果顯示。
 * 主要功能與流程：每次版面渲染時以當前 authenticated principal 呼叫 ChurchSettingsAccess，
 * 該 resolver 重新讀取登入 contact、全教會權限及 account scope；只有完整解析成功才將 true 傳給檢視。
 * 檢視文字取自 ChurchSettingsText。這個可見性檢查不取代任何 Controller action 的逐次授權。
 * 使用邊界與維護：ViewComponent 為 request scoped 消費者，不保存 principal、教會、Session 或快取，
 * 也不擁有 CRM facade；IOrganizationService 由既有 Host scope 負責釋放。每次導覽需要一次 contact
 * 與有效教會的必要查詢，取消沿用 RequestAborted，錯誤依既有 Host 例外處理傳遞而不假裝無權限。
 * 離線測試以合成 SDK 驗證管理員與非管理員結果，不代表 D365 實機驗收。
 */
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace ChurchReport.Areas.ChurchSettings;

/// <summary>依完整 ChurchSettingsAccess 伺服器授權 scope 控制導覽入口的 request-level 元件。</summary>
public sealed class ChurchSettingsNavViewComponent : ViewComponent
{
    private readonly ChurchSettingsAccess access;

    /// <summary>借用本次 request 的 ChurchSettingsAccess，不保存或延長 scoped 依賴生命週期。</summary>
    /// <param name="access">每次重新查核登入 contact、管理員權限及所屬教會的 resolver。</param>
    public ChurchSettingsNavViewComponent(ChurchSettingsAccess access) =>
        this.access = access ?? throw new System.ArgumentNullException(nameof(access));

    /// <summary>產生只含授權布林值的 view model，不把可授權的教會 ID 傳入檢視。</summary>
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var visible = await CanShowAsync(HttpContext.User, HttpContext.RequestAborted).ConfigureAwait(false);
        return View("Default", visible);
    }

    /// <summary>以相同伺服器 scope resolver 判定導覽是否可見，供隔離測試直接驗證。</summary>
    /// <param name="principal">目前 request 經驗證的 principal。</param>
    /// <param name="cancellationToken">目前 request 的取消 token。</param>
    /// <returns>只有完整管理員及有效教會 scope 都成立時才回傳 true。</returns>
    internal async Task<bool> CanShowAsync(ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        await access.ResolveAsync(principal, cancellationToken).ConfigureAwait(false) is not null;
}

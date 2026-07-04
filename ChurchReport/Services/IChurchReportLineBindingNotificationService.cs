using System.Threading;
using System.Threading.Tasks;

namespace ChurchReport.Services;

/// <summary>
/// ChurchReport 專用的 LINE 綁定通知服務。
/// Controller 只需要表達「目前流程要通知使用者綁定 LINE」，
/// 不應該知道 LINE profile 怎麼查、綁定網址怎麼組、訊息最後怎麼透過 workflow 發送。
/// </summary>
public interface IChurchReportLineBindingNotificationService
{
    /// <summary>
    /// 發送 ChurchReport LINE 帳號綁定提示。
    /// 這個流程會查詢 LINE 使用者顯示名稱，組出 ChurchReport 的綁定頁 URL，
    /// 再透過共用 LINE notification workflow 發送文字訊息。
    /// </summary>
    /// <param name="lineUserId">LINE user id。</param>
    /// <param name="cancellationToken">ASP.NET request 取消權杖。</param>
    Task NotifyLineBindingAsync(string lineUserId, CancellationToken cancellationToken = default);
}


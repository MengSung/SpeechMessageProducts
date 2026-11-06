/*
 * 檔案整體說明
 *
 * 目的與定位：ChurchReport 專用的 LINE 綁定通知服務。 Controller 只需要表達「目前流程要通知使用者綁定 LINE」， 不應該知道 LINE profile 怎麼查、綁定網址怎麼組、
 * 訊息最後怎麼透過 workflow 發送。
 *
 * 主要功能與流程：
 * 本檔宣告 IChurchReportLineBindingNotificationService。
 * - 發送 ChurchReport LINE 帳號綁定提示。 這個流程會查詢 LINE 使用者顯示名稱，組出 ChurchReport 的綁定頁 URL， 再透過共用 LINE notification workflow 
 * 發送文字訊息。
 * 資料／選項欄位包含 IChurchReportLineBindingNotificationService，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Core.cs、
 * SpeechMessageProducts.ChurchReport/Services/ChurchReportLineBindingNotificationService.cs、
 * SpeechMessageProducts.ChurchReport/Startup.cs 追查目前工作樹中的合作／使用位置。
 */
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

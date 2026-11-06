/*
 * 檔案整體說明
 *
 * 目的與定位：導覽服務介面
 *
 * 主要功能與流程：
 * 本檔宣告 INavigationService、RedirectInfo。
 * 資料／選項欄位包含 INavigationService、RedirectInfo、ViewType、ActiveListId，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
using ChurchReport.Models.Authentication;

namespace ChurchReport.Services.Navigation
{
    /// <summary>
    /// 導覽服務介面
    /// </summary>
    public interface INavigationService
    {
        /// <summary>
        /// 決定登入後的重導向資訊
        /// </summary>
        RedirectInfo DetermineRedirect(SessionData sessionData);
    }

    /// <summary>
    /// 重導向資訊
    /// </summary>
    public class RedirectInfo
    {
        public string ViewType { get; set; }
        public string ActiveListId { get; set; }
    }
}

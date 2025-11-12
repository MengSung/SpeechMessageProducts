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

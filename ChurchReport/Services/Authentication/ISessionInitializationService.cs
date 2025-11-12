using ChurchReport.Models.Authentication;
using Microsoft.Xrm.Sdk;
using System.Threading.Tasks;

namespace ChurchReport.Services.Authentication
{
    /// <summary>
    /// Session 初始化服務介面
    /// </summary>
    public interface ISessionInitializationService
    {
        /// <summary>
        /// 初始化使用者 Session
        /// </summary>
        Task<SessionData> InitializeSessionAsync(
            Entity loginContact,
            LoginType loginType,
            string account,
            string password);

        /// <summary>
        /// 清除 Session
        /// </summary>
        void ClearSession();
    }
}

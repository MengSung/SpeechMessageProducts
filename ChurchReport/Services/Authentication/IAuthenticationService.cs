using ChurchReport.Models.Authentication;
using System.Threading.Tasks;

namespace ChurchReport.Services.Authentication
{
    /// <summary>
    /// 認證服務介面
    /// </summary>
    public interface IAuthenticationService
    {
        /// <summary>
        /// 驗證帳號密碼
        /// </summary>
        Task<AuthResult> ValidateCredentialsAsync(string account, string password);

        /// <summary>
        /// 驗證 LINE User ID
        /// </summary>
        Task<AuthResult> ValidateLineIdAsync(string lineUserId);
    }
}

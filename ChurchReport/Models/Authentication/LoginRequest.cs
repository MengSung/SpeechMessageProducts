using System.ComponentModel.DataAnnotations;

namespace ChurchReport.Models.Authentication
{
    /// <summary>
    /// 登入請求模型
    /// </summary>
    public class LoginRequest
    {
        /// <summary>
        /// 帳號
        /// </summary>
        public string Account { get; set; }

        /// <summary>
        /// 密碼
        /// </summary>
        public string Password { get; set; }
    }

    /// <summary>
    /// LINE 登入請求模型
    /// </summary>
    public class LineLoginRequest
    {
        /// <summary>
        /// LINE User ID
        /// </summary>
        [Required]
        public string LineUserId { get; set; }

        /// <summary>
        /// 顯示名稱
        /// </summary>
        public string DisplayName { get; set; }
    }
}

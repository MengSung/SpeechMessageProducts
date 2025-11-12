using Microsoft.Xrm.Sdk;

namespace ChurchReport.Models.Authentication
{
    /// <summary>
    /// 認證結果
    /// </summary>
    public class AuthResult
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 登入的連絡人實體
        /// </summary>
        public Entity LoginContact { get; set; }

        /// <summary>
        /// 全名
        /// </summary>
        public string FullName { get; set; }

        /// <summary>
        /// 登入類型
        /// </summary>
        public LoginType LoginType { get; set; }

        /// <summary>
        /// 錯誤訊息
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// 建立成功結果
        /// </summary>
        public static AuthResult CreateSuccess(Entity contact, string fullName, LoginType type)
        {
            return new AuthResult
            {
                IsSuccess = true,
                LoginContact = contact,
                FullName = fullName,
                LoginType = type
            };
        }

        /// <summary>
        /// 建立失敗結果
        /// </summary>
        public static AuthResult CreateFail(string errorMessage)
        {
            return new AuthResult
            {
                IsSuccess = false,
                ErrorMessage = errorMessage
            };
        }
    }

    /// <summary>
    /// 登入類型列舉
    /// </summary>
    public enum LoginType
    {
        /// <summary>
        /// 帳號密碼登入
        /// </summary>
        AccountPassword,

        /// <summary>
        /// LINE ID 登入
        /// </summary>
        LineId,

        /// <summary>
        /// QR Code 登入
        /// </summary>
        QrCode
    }
}

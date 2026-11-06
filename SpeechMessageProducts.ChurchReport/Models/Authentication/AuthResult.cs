/*
 * 檔案整體說明
 *
 * 目的與定位：認證結果
 *
 * 主要功能與流程：
 * 本檔宣告 AuthResult、LoginType。
 * 主要操作入口包含 CreateSuccess、CreateFail。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/AppointmentController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/DedicationController.cs 追查目前工作樹中的合作／使用位置。
 */
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

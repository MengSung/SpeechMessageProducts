using Microsoft.Xrm.Sdk;

namespace ChurchReport.Models.Authentication
{
    /// <summary>
    /// Session 資料模型
    /// </summary>
    public class SessionData
    {
        /// <summary>
        /// 登入的連絡人實體
        /// </summary>
        public Entity LoginContact { get; set; }

        /// <summary>
        /// 帳號
        /// </summary>
        public string Account { get; set; }

        /// <summary>
        /// 密碼
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// 全名
        /// </summary>
        public string FullName { get; set; }

        /// <summary>
        /// 登入類型
        /// </summary>
        public string LoginType { get; set; }

        /// <summary>
        /// 顯示檢視類型
        /// </summary>
        public string DisplayViewType { get; set; }

        /// <summary>
        /// 活躍清單 ID
        /// </summary>
        public string ActiveListId { get; set; }

        /// <summary>
        /// 使用者類型
        /// </summary>
        public string UserType { get; set; }

        /// <summary>
        /// 是否有幸福小組
        /// </summary>
        public bool HasHappyGroup { get; set; }

        /// <summary>
        /// 是否有繳費資料
        /// </summary>
        public bool HasFeeData { get; set; }
    }
}

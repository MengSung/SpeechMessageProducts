namespace ChurchReport.Models.Authentication
{
    /// <summary>
    /// 登入回應模型
    /// </summary>
    public class LoginResponse
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 顯示檢視類型
        /// </summary>
        public string DisplayViewType { get; set; }

        /// <summary>
        /// 活躍清單 ID
        /// </summary>
        public string ActiveListId { get; set; }

        /// <summary>
        /// 訊息
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// 全名
        /// </summary>
        public string FullName { get; set; }

        /// <summary>
        /// 帳號
        /// </summary>
        public string Account { get; set; }

        /// <summary>
        /// 密碼
        /// </summary>
        public string Password { get; set; }
    }
}

/*
 * 檔案整體說明
 *
 * 目的與定位：登入回應模型
 *
 * 主要功能與流程：
 * 本檔宣告 LoginResponse。
 * 資料／選項欄位包含 LoginResponse、Success、DisplayViewType、ActiveListId、Message、FullName、Account、Password，名稱、
 * 型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 */
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

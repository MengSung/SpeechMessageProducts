/*
 * 檔案整體說明
 *
 * 目的與定位：Session 資料模型
 *
 * 主要功能與流程：
 * 本檔宣告 SessionData。
 * 資料／選項欄位包含 SessionData、LoginContact、Account、Password、FullName、LoginType、DisplayViewType、ActiveListId、UserType、
 * HasHappyGroup、HasFeeData，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Services/Authentication/ISessionInitializationService.cs、
 * SpeechMessageProducts.ChurchReport/Services/Navigation/INavigationService.cs 追查目前工作樹中的合作／使用位置。
 */
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

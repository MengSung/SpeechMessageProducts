// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Models/Authentication/AuthResult.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class AuthResult、enum LoginType
// 主要成員：CreateSuccess、CreateFail、IsSuccess、LoginContact、FullName、LoginType、ErrorMessage
// 引用命名空間：Microsoft.Xrm.Sdk
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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

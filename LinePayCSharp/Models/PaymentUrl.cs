// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LinePayCSharp/Models/PaymentUrl.cs
// 所屬區塊：LINE Pay C# 整合封裝，處理付款 API 模型與呼叫。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class PaymentUrl
// 主要成員：Web、App
// 引用命名空間：Newtonsoft.Json
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Newtonsoft.Json;

namespace Line.Pay.Models
{
    /// <summary>
    /// URL to go to after payment request
    /// </summary>
    public class PaymentUrl
    {
        /// <summary>
        /// Web URL to go to after payment reques
        /// Used if payment request was made in Web environment
        /// URL to the LINE Pay payment waiting screen
        /// Redirected to the provided URL without any additional parameters
        /// When a pop-up browser from a Desktop is opened, Size - Width: 700px, Height : 546px
        /// </summary>
        [JsonProperty("web")]
        public string Web { get; set; }

        /// <summary>
        /// App URL to the Payment Screen
        /// Used if payment request was made in an app
        /// Redirecting URL from Merchant's app to LINE Pay App
        /// </summary>
        [JsonProperty("app")]
        public string App { get; set; }
    }
}

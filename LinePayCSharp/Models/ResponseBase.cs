// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LinePayCSharp/Models/ResponseBase.cs
// 所屬區塊：LINE Pay C# 整合封裝，處理付款 API 模型與呼叫。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class ResponseBase
// 主要成員：ReturnCode、ReturnMessage
// 引用命名空間：Newtonsoft.Json
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Newtonsoft.Json;

namespace Line.Pay.Models
{
    /// <summary>
    /// Response Base Class
    /// </summary>
    public class ResponseBase
    {
        /// <summary>
        /// Result Code
        /// Code Description
        /// 1101 This user is not a LINE Pay user.
        /// 1102 The purchasing user suspended for transaction.
        /// 1104 Merchant not found.
        /// 1105 This Merchant cannot use LINE Pay.
        /// 1106 Header information error
        /// 1110 Not available credit card.
        /// 1124 Error in Amount (scale).
        /// 1133 Invalid oneTimeKey
        /// 1141 Account status error.
        /// 1142 Insufficient balance remains.
        /// 1145 Payment in progress.
        /// 1150 Transaction record not found.
        /// 1152 Transaction has already been made.
        /// 1153 Request amount is different from real amount.
        /// 1154 Preapproved payment account not available.
        /// 1155 The transaction Id not eligible for Refund.
        /// 1159 Omitted request payment information.
        /// 1163 Exceeded the expiration for Refund.
        /// 1164 Refund limit exceeded.
        /// 1165 The transaction has already been refunded.
        /// 1169 Payment method and password must be certificated by LINE Pay.
        /// 1170 User’s account remains have been changed.
        /// 1172 Existing same orderId.
        /// 1177 Exceeded max. number of transactions (100) allowed to be retrieved.
        /// 1178 Unsupported currency.
        /// 1179 Status can not be processed.
        /// 1180 Expired the payment date
        /// 1183 Payment amount must be greater than 0.
        /// 1184 Payment amount exceeds amount requested.
        /// 1190 The regKey does not exist.
        /// 1193 The regKey expired.
        /// 1194 This Merchant cannot use Preapproved Payment.
        /// 1197 Already processing payment with regKey
        /// 1198 Duplicated the request calling API.
        /// 1199 Internal request error.
        /// 1280 Temporary error while making a payment with Credit Card
        /// 1281 Credit Card Payment Error
        /// 1282 Credit Card Authorization Error
        /// 1283 The payment has been declined due to suspected fraud.
        /// 1284 Payment amount must be greater than 0.
        /// 1285 Omitted credit card information
        /// 1286 Incorrect credit card payment information
        /// 1287 Credit card expiration date has passed.
        /// 1288 Credit card has insufficient funds.
        /// 1289 Maximum credit card limit exceeded.
        /// 1290 One-time payment limit exceeded.
        /// 1291 This card has been reported stolen.
        /// 1292 This card has been suspended.
        /// 1293 Invalid Card Verification Number (CVN)
        /// 1294 This card is blacklisted.
        /// 1295 Invalid credit card number
        /// 1296 Invalid amount
        /// 1298 The credit card payment declined.
        /// 2101 Parameter error
        /// 2102 JSON data format error
        /// 9000 Internal error
        /// </summary>
        [JsonProperty("returnCode")]
        public string ReturnCode { get; set; }

        /// <summary>
        /// Result messages or reason for failure
        /// </summary>
        [JsonProperty("returnMessage")]
        public string ReturnMessage { get; set; }
    }
}

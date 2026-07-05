// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Exceptions/LineResponseException.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class LineResponseException
// 主要成員：ToString、StatusCode、ResponseMessage
// 引用命名空間：System、System.Net
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Net;

namespace Line.Messaging
{
    /// <summary>
    /// Capture Error from LINE platform
    /// </summary>
    public class LineResponseException : Exception
    {
        /// <summary>
        /// HTTP Status Code
        /// </summary>
        public HttpStatusCode StatusCode { get; set; }

        /// <summary>
        /// Error returned from LINE Platform
        /// </summary>
        public ErrorResponseMessage ResponseMessage { get; set; }

        public LineResponseException()
        {}

        public LineResponseException(string message) : base(message)
        {}

        public LineResponseException(string message, Exception innerException) : base(message, innerException)
        {}

        public override string ToString()
        {
            return $"StatusCode={StatusCode}, {ResponseMessage}" + Environment.NewLine
                + base.ToString();
        }
    }
}

// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Webhooks/PostbackParams.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class PostbackParams
// 主要成員：Date、Time、DateTime
// 引用命名空間：System、System.Text.RegularExpressions
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Text.RegularExpressions;

namespace Line.Messaging.Webhooks
{
    /// <summary>
    /// Object with the date and time selected by a user through a datetime picker action. The full-date, time-hour, and time-minute formats follow the RFC3339 protocol.
    /// </summary>
    public class PostbackParams
    {
        /// <summary>
        /// Date selected by user. Only included in the date mode. Format: full-date
        /// </summary>
        public string Date { get; }

        /// <summary>
        /// Time selected by the user. Only included in the time mode. Format: time-hour ":" time-minute
        /// </summary>
        public string Time { get; }

        /// <summary>
        /// Date and time selected by the user. Only included in the datetime mode. Format: full-date "T" time-hour ":" time-minute
        /// </summary>
        public string DateTime { get; }

        public PostbackParams(string date, string time, string datetime)
        {
            if (date != null && !Regex.Match(date, @"^(\d{4})-(0[1-9]|1[0-2])-(0[1-9]|[12][0-9]|3[01])$").Success)
            {
                throw new ArgumentException($"Date format must be \"yyyy-MM-dd\".", nameof(date));
            }
            if (time != null && !Regex.Match(time, @"^([01][0-9]|2[0-3]):([0-5][0-9])$").Success)
            {
                throw new ArgumentException($"Time format must be \"HH:mm\".", nameof(time));
            }
            if (datetime != null && !Regex.Match(datetime, @"^(\d{4})-(0[1-9]|1[0-2])-(0[1-9]|[12][0-9]|3[01])T([01][0-9]|2[0-3]):([0-5][0-9])$").Success)
            {
                throw new ArgumentException("Date-Time format must be \"yyyy-MM-ddTHH:mm\".", nameof(datetime));
            }

            Date = date;
            Time = time;
            DateTime = datetime;
        }
    }
}

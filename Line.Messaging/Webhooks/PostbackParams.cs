/*
 * 檔案整體說明
 *
 * 目的與定位：本檔承載日期時間選擇器的日期、時間或日期時間回傳值。
 *
 * 主要功能與流程：
 * 本檔宣告 PostbackParams。
 * 資料／選項欄位包含 PostbackParams、Date、Time、DateTime，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Webhooks/Postback.cs 追查目前工作樹中的合作／使用位置。
 */
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

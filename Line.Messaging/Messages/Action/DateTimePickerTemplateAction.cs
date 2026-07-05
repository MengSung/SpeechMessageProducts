// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Messages/Action/DateTimePickerTemplateAction.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class DateTimePickerTemplateAction
// 主要成員：Initialize、GetDateTimeFormat、CreateFrom、Type、Label、Data、Mode、Initial、Max、Min
// 引用命名空間：System
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;

namespace Line.Messaging
{
    /// <summary>
    /// When a control associated with this action is tapped, a postback event is returned via webhook with the date and time selected by the user from the date and time selection dialog.
    /// https://developers.line.me/en/docs/messaging-api/reference/#datetime-picker-action
    /// </summary>
    public class DateTimePickerTemplateAction : ITemplateAction
    {
        public TemplateActionType Type { get; } = TemplateActionType.Datetimepicker;

        /// <summary>
        /// Label for the action
        /// Required for templates other than image carousel.Max: 20 characters
        /// Optional for image carousel templates.Max: 12 characters.
        /// RichMenu 可省略；用戶端啟用可及性功能時會朗讀此文字，最多 20 個字元。
        /// LINE iOS 8.2.0 以後支援 RichMenu 上的此可及性 label。
        /// </summary>
        public string Label { get; protected set; }

        /// <summary>
        /// String returned via webhook in the postback.data property of the postback event
        /// Max: 300 characters
        /// </summary>
        public string Data { get; protected set; }

        /// <summary>
        /// Action mode
        /// date: Pick date
        /// time: Pick time
        /// datetime: Pick date and time
        /// </summary>
        public DateTimePickerMode Mode { get; protected set; }

        /// <summary>
        /// Initial value of date or time
        /// </summary>
        public string Initial { get; protected set; }

        /// <summary>
        /// Largest date or time value that can be selected.
        /// Must be greater than the min value.
        /// </summary>
        public string Max { get; protected set; }

        /// <summary>
        /// Smallest date or time value that can be selected.
        /// Must be less than the max value.
        /// </summary>
        public string Min { get; protected set; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="label">
        /// Label for the action
        /// Required for templates other than image carousel.Max: 20 characters
        /// Optional for image carousel templates.Max: 12 characters.
        /// RichMenu 可省略；用戶端啟用可及性功能時會朗讀此文字，最多 20 個字元。
        /// LINE iOS 8.2.0 以後支援 RichMenu 上的此可及性 label。
        /// </param>
        /// <param name="data">
        /// String returned via webhook in the postback.data property of the postback event
        /// Max: 300 characters
        /// </param>
        /// <param name="mode">
        /// Action mode
        /// date: Pick date
        /// time: Pick time
        /// datetime: Pick date and time
        /// </param>
        /// <param name="initial">
        /// Initial value of date or time
        /// </param>
        /// <param name="min">
        /// Smallest date or time value that can be selected.
        /// Must be less than the max value.
        /// </param>
        /// <param name="max">
        /// Largest date or time value that can be selected.
        /// Must be greater than the min value.
        /// </param>
        public DateTimePickerTemplateAction(string label, string data, DateTimePickerMode mode, string initial = null, string min = null, string max = null)
        {
            Initialize(label, data, mode, initial, min, max);
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="label">
        /// Label for the action
        /// Required for templates other than image carousel.Max: 20 characters
        /// Optional for image carousel templates.Max: 12 characters.
        /// RichMenu 可省略；用戶端啟用可及性功能時會朗讀此文字，最多 20 個字元。
        /// LINE iOS 8.2.0 以後支援 RichMenu 上的此可及性 label。
        /// </param>
        /// <param name="data">
        /// String returned via webhook in the postback.data property of the postback event
        /// Max: 300 characters
        /// </param>
        /// <param name="mode">
        /// Action mode
        /// date: Pick date
        /// time: Pick time
        /// datetime: Pick date and time
        /// </param>
        /// <param name="initial">
        /// Initial value of date or time
        /// </param>
        /// <param name="min">
        /// Smallest date or time value that can be selected.
        /// Must be less than the max value.
        /// </param>
        /// <param name="max">
        /// Largest date or time value that can be selected.
        /// Must be greater than the min value.
        /// </param>
        public DateTimePickerTemplateAction(string label, string data, DateTimePickerMode mode, DateTime? initial = null, DateTime? min = null, DateTime? max = null)
        {
            var format = GetDateTimeFormat(mode);
            Initialize(label, data, mode,
                initial == null ? null : ((DateTime)initial).ToString(format),
                min == null ? null : ((DateTime)min).ToString(format),
                max == null ? null : ((DateTime)max).ToString(format));
        }

        internal void Initialize(string label, string data, DateTimePickerMode mode, string initial, string min, string max)
        {
            Label = label?.Substring(0, Math.Min(label.Length, 20));
            Data = data.Substring(0, Math.Min(data.Length, 300));
            Mode = mode;
            Initial = initial;
            Min = min;
            Max = max;
        }

        internal static string GetDateTimeFormat(DateTimePickerMode mode)
        {
            var format = "";
            switch (mode)
            {
                case DateTimePickerMode.Date:
                    format = "yyyy-MM-dd";
                    break;
                case DateTimePickerMode.Time:
                    format = "HH:mm";
                    break;
                case DateTimePickerMode.Datetime:
                    format = "yyyy-MM-ddTHH:mm";
                    break;
            }
            return format;
        }

        internal static DateTimePickerTemplateAction CreateFrom(dynamic dynamicObject)
        {
            var mode = (DateTimePickerMode)Enum.Parse(typeof(DateTimePickerMode), dynamicObject?.mode);
            var format = GetDateTimeFormat(mode);
            var initial = DateTime.ParseExact(dynamicObject?.initial, format, null);
            var min = DateTime.ParseExact(dynamicObject?.min, format, null);
            var max = DateTime.ParseExact(dynamicObject?.max, format, null);
            return new DateTimePickerTemplateAction((string)dynamicObject?.label, (string)dynamicObject?.data, mode, initial, min, max);
        }
    }
}

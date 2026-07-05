// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/LineObjects/NumberOfMessages.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class NumberOfSentMessages
// 主要成員：Status、Success
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
namespace Line.Messaging
{
    public class NumberOfSentMessages
    {
        /// <summary>
        /// Status of the counting process. One of the following values is returned:<para>
        /// ready: You can get the number of messages.</para><para>
        /// unready: The message counting process for the date specified in date has not been completed yet.Retry your request later.Normally, the counting process is completed within the next day.</para><para>
        /// out_of_service: The date specified in date is earlier than March 31, 2018, when the operation of the counting system started.</para>
        /// </summary>
        public NumberOfSentMessagesStatus Status { get; set; }

        /// <summary>
        /// The number of messages sent with the Messaging API on the date specified in date. The response has this property only when the value of status is ready.
        /// </summary>
        public int Success { get; set; }

        /// <summary>
        /// Default Constructor
        /// </summary>
        public NumberOfSentMessages()
        {}
    }
}

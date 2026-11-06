/*
 * 檔案整體說明
 *
 * 目的與定位：本檔接收指定日期的已送訊息數與統計就緒狀態，讓呼叫端區分未就緒及實際數量。
 *
 * 主要功能與流程：
 * 本檔宣告 NumberOfSentMessages。
 * 資料／選項欄位包含 NumberOfSentMessages、Status、Success，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/ILineMessagingClient.cs、Line.Messaging/LineMessagingClient.cs 追查目前工作樹中的合作／使用位置。
 */
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

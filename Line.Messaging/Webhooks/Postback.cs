/*
 * 檔案整體說明
 *
 * 目的與定位：本檔承載動作回傳的資料及日期時間參數。
 *
 * 主要功能與流程：
 * 本檔宣告 Postback。
 * 資料／選項欄位包含 Postback、Data、Params，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 LineMessagingProcessor.Workflows.Tests/LineNotificationWorkflowTests.cs、
 * Line.Messaging/Messages/Action/PostbackTemplateAction.cs、Line.Messaging/Messages/Action/TemplateActionType.cs、
 * Line.Messaging/Messages/RichMenu/ActionArea.cs、Line.Messaging/Webhooks/PostbackEvent.cs 追查目前工作樹中的合作／使用位置。
 */
namespace Line.Messaging.Webhooks
{
    /// <summary>
    /// Postback
    /// </summary>
    public class Postback
    {
        /// <summary>
        /// Postback data
        /// </summary>
        public string Data { get; }

        /// <summary>
        /// date and time selected by a user through a datetime picker action.
        /// Only returned for postback actions via the datetime picker.
        /// </summary>
        public PostbackParams Params { get; }

        public Postback(string data, string date, string time, string datetime)
        {
            Data = data;
            Params = new PostbackParams(date, time, datetime);
        }
    }
}

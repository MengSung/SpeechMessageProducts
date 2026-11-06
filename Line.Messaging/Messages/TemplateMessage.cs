/*
 * 檔案整體說明
 *
 * 目的與定位：本檔以替代文字包裝模板內容，讓LINE以預定義版型呈現互動訊息。
 *
 * 主要功能與流程：
 * 本檔宣告 TemplateMessage。
 * 資料／選項欄位包含 Type、QuickReply、Template、AltText，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging.Tests/LineSdkGoldenTests.cs、Line.Messaging/Messages/ISendMessage.cs 追查目前工作樹中的合作／使用位置。
 */
using System;

namespace Line.Messaging
{
    /// <summary>
    /// Template messages are messages with predefined layouts which you can customize. There are four types of templates available that can be used to interact with users through your bot.
    /// </summary>
    public class TemplateMessage : ISendMessage
    {
        public MessageType Type { get; } = MessageType.Template;


        /// <summary>
        /// These properties are used for the quick reply feature
        /// </summary>
        public QuickReply QuickReply { get; set; }


        /// <summary>
        /// A Buttons, Confirm, Carousel, or Image Carousel object.
        /// </summary>
        public ITemplate Template { get; }

        /// <summary>
        /// Alternative text.
        /// Max: 400 characters
        /// </summary>
        public string AltText { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="altText">
        /// Alternative text.
        /// Max: 400 characters
        ///</param>
        /// <param name="template">
        /// A Buttons, Confirm, Carousel, or Image Carousel object.
        /// </param>
        /// <param name="quickReply">
        /// QuickRepy
        /// </param>
        public TemplateMessage(string altText, ITemplate template, QuickReply quickReply = null)
        {
            AltText = altText.Substring(0, Math.Min(altText.Length, 400));
            Template = template;
            QuickReply = quickReply;
        }
    }
}

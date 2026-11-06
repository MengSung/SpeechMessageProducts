/*
 * 檔案整體說明
 *
 * 目的與定位：本檔將bubble或carousel容器及替代文字包裝成Flex訊息，提供既有建立輔助入口；Flex內容的配置模型與模板訊息是不同協定形狀。
 *
 * 主要功能與流程：
 * 本檔宣告 FlexMessage。
 * 主要操作入口包含 CreateBubbleMessage、CreateCarouselMessage。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging.Tests/LineSdkFoundationTests.cs、Line.Messaging.Tests/LineSdkGoldenTests.cs、
 * Line.Messaging/Messages/Flex/Extensions/BubbleContainerFlexMessages.cs、
 * Line.Messaging/Messages/Flex/Extensions/CarouselContainerFlexMessges.cs、Line.Messaging/Messages/ISendMessage.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;

namespace Line.Messaging
{
    /// <summary>
    /// 將 Flex bubble 或 carousel 容器包裝為可傳送訊息，AltText 提供不支援 Flex 顯示時的替代文字。
    /// 本型別只組裝協定資料；版面驗證、授權與 HTTP 傳送分別由相應呼叫端負責，不屬於 TemplateMessage。
    /// </summary>
    public class FlexMessage : ISendMessage
    {
        public MessageType Type { get; } = MessageType.Flex;

        /// <summary>
        /// These properties are used for the quick reply feature
        /// </summary>
        public QuickReply QuickReply { get; set; }

        /// <summary>
        /// Flex Message container object
        /// </summary>
        public IFlexContainer Contents { get; set; }

        /// <summary>
        /// Alternative text.
        /// Max: 400 characters
        /// </summary>
        public string AltText { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="altText">
        /// Alternative text.
        /// Max: 400 characters
        ///</param>
        public FlexMessage(string altText)
        {
            AltText = altText.Substring(0, Math.Min(altText.Length, 400));
        }

        public static BubbleContainerFlexMessage CreateBubbleMessage(string altText)
        {
            return new BubbleContainerFlexMessage(altText)
            {
                Contents = new BubbleContainer()
            };
        }

        public static CarouselContainerFlexMessage CreateCarouselMessage(string altText)
        {
            return new CarouselContainerFlexMessage(altText)
            {
                Contents = new CarouselContainer()
            };
        }
    }

}

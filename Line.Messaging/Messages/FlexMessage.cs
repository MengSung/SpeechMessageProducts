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
 * 可沿 ChurchReport.MemberInfo.Tests/LineSharedWorkflow/LineUtilityClassWorkflowTests.cs、
 * Line.Messaging.Tests/LineSdkFoundationTests.cs、Line.Messaging.Tests/LineSdkGoldenTests.cs、
 * LineMessagingProcessor.Workflows.Tests/LineNotificationWorkflowTests.cs、
 * Line.Messaging/Messages/Flex/Extensions/BubbleContainerFlexMessages.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;

namespace Line.Messaging
{
    /// <summary>
    /// Template messages are messages with predefined layouts which you can customize. There are four types of templates available that can be used to interact with users through your bot.
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

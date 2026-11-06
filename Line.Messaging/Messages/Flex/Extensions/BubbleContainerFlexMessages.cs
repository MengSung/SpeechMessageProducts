/*
 * 檔案整體說明
 *
 * 目的與定位：本檔提供以bubble建立Flex訊息的相容建構入口。
 *
 * 主要功能與流程：
 * 本檔宣告 BubbleContainerFlexMessage。
 * 主要操作入口包含 SetBubbleContainer、SetQuickReply。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/Messages/FlexMessage.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Text;

namespace Line.Messaging
{
    public class BubbleContainerFlexMessage : FlexMessage
    {
        /// <summary>
        /// Sets a Bubble container to the FlexMessage object.
        /// </summary>
        /// <param name="bubbleContainer"></param>
        /// <returns>Flex Message</returns>
        public BubbleContainerFlexMessage SetBubbleContainer(BubbleContainer bubbleContainer)
        {
            Contents = bubbleContainer ?? throw new ArgumentNullException(nameof(bubbleContainer));
            return this;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="altText">alt text</param>
        public BubbleContainerFlexMessage(string altText) : base(altText)
        {

        }

        /// <summary>
        /// Sets a QuickReply object to the FlexMessage objecy.
        /// </summary>
        /// <param name="quickReply"></param>
        /// <returns>Flex Message</returns>
        public BubbleContainerFlexMessage SetQuickReply(QuickReply quickReply)
        {
            QuickReply = quickReply;
            return this;
        }
    }
}

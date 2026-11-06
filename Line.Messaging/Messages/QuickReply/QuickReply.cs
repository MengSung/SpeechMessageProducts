/*
 * 檔案整體說明
 *
 * 目的與定位：本檔將快速回覆按鈕序列附加到支援的訊息，維持官方items封裝形狀。
 *
 * 主要功能與流程：
 * 本檔宣告 QuickReply。
 * 資料／選項欄位包含 QuickReply、Items，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging.Tests/LineSdkGoldenTests.cs、Line.Messaging/Messages/AudioMessage.cs、
 * Line.Messaging/Messages/CouponMessage.cs、Line.Messaging/Messages/Flex/Extensions/BubbleContainerFlexMessages.cs、
 * Line.Messaging/Messages/Flex/Extensions/CarouselContainerFlexMessges.cs 追查目前工作樹中的合作／使用位置。
 */
using System.Collections.Generic;

namespace Line.Messaging
{
    /// <summary>
    /// https://developers.line.me/en/reference/messaging-api/#quick-reply
    /// These properties are used for the quick reply feature. Supported on LINE 8.11.0 and later for iOS and Android. For more information, see Using quick replies.
    /// </summary>
    public class QuickReply
    {
        /// <summary>
        /// Quick reply button objects. Max: 13 objects
        /// </summary>
        public IList<QuickReplyButtonObject> Items { get; set; }

        public QuickReply(IList<QuickReplyButtonObject> items = null)
        {
            Items = items ?? new List<QuickReplyButtonObject>();
        }
    }
}

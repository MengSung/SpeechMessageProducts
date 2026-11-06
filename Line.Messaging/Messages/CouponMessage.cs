/*
 * 檔案整體說明
 *
 * 目的與定位：表示 LINE 官方 Messaging API 的 coupon 訊息。 couponId 來自建立或查詢優惠券 API，deliveryTag 用於 LINE 後台成效路徑統計。
 *
 * 主要功能與流程：
 * 本檔宣告 CouponMessage。
 * 資料／選項欄位包含 Type、QuickReply、CouponId、DeliveryTag，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging.Tests/LineMessageModelTests.cs、
 * LineMessagingProcessor.Workflows.Tests/LineNotificationWorkflowTests.cs、
 * LineMessagingProcessor.Workflows/LineNotificationContent.cs 追查目前工作樹中的合作／使用位置。
 */
using System;

namespace Line.Messaging
{
    /// <summary>
    /// 表示 LINE 官方 Messaging API 的 coupon 訊息。
    /// couponId 來自建立或查詢優惠券 API，deliveryTag 用於 LINE 後台成效路徑統計。
    /// </summary>
    public class CouponMessage : ISendMessage
    {
        public MessageType Type { get; } = MessageType.Coupon;

        public QuickReply QuickReply { get; set; }

        public string CouponId { get; }

        public string DeliveryTag { get; }

        public CouponMessage(string couponId, string deliveryTag = null, QuickReply quickReply = null)
        {
            if (couponId == null)
            {
                throw new ArgumentNullException(nameof(couponId));
            }

            if (deliveryTag != null && deliveryTag.Length > 30)
            {
                throw new ArgumentException("Coupon delivery tag must be 30 characters or fewer.", nameof(deliveryTag));
            }

            CouponId = couponId;
            DeliveryTag = deliveryTag;
            QuickReply = quickReply;
        }
    }
}

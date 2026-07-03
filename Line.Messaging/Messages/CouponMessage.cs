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

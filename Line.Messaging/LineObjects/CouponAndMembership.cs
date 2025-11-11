using Newtonsoft.Json;
using System.Collections.Generic;

namespace Line.Messaging
{
    /// <summary>
    /// Coupon object
    /// https://developers.line.biz/en/reference/messaging-api/#create-coupon
    /// </summary>
    public class Coupon
    {
        /// <summary>
        /// Coupon ID
        /// </summary>
        [JsonProperty("couponId")]
        public string CouponId { get; set; }

        /// <summary>
        /// Coupon name (max 50 characters)
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>
        /// Coupon description (max 200 characters)
        /// </summary>
        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>
        /// Coupon status. One of:
        /// - ACTIVE: Active
        /// - CLOSED: Closed
        /// - EXHAUSTED: Exhausted
        /// </summary>
        [JsonProperty("status")]
        public string Status { get; set; }

        /// <summary>
        /// Number of coupons issued
        /// </summary>
        [JsonProperty("issuedCount")]
        public long IssuedCount { get; set; }

        /// <summary>
        /// Number of coupons used
        /// </summary>
        [JsonProperty("usedCount")]
        public long UsedCount { get; set; }

        /// <summary>
        /// Coupon creation time (Unix timestamp)
        /// </summary>
        [JsonProperty("createdAt")]
        public long CreatedAt { get; set; }

        /// <summary>
        /// Coupon update time (Unix timestamp)
        /// </summary>
        [JsonProperty("updatedAt")]
        public long UpdatedAt { get; set; }

        /// <summary>
        /// Start date (Unix timestamp)
        /// </summary>
        [JsonProperty("startAt")]
        public long? StartAt { get; set; }

        /// <summary>
        /// End date (Unix timestamp)
        /// </summary>
        [JsonProperty("endAt")]
        public long? EndAt { get; set; }

        /// <summary>
        /// Number of days the coupon is valid after issued
        /// </summary>
        [JsonProperty("validityPeriodInDays")]
        public int? ValidityPeriodInDays { get; set; }

        /// <summary>
        /// Maximum number of coupons that can be issued
        /// </summary>
        [JsonProperty("maxIssueLimit")]
        public long? MaxIssueLimit { get; set; }

        /// <summary>
        /// Whether the coupon can be issued more than once to the same user
        /// </summary>
        [JsonProperty("allowReissuance")]
        public bool? AllowReissuance { get; set; }
    }

    /// <summary>
    /// Create coupon request
    /// </summary>
    public class CreateCouponRequest
    {
        /// <summary>
        /// Coupon name (max 50 characters)
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>
        /// Coupon description (max 200 characters)
        /// </summary>
        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>
        /// Start date (Unix timestamp, optional)
        /// </summary>
        [JsonProperty("startAt")]
        public long? StartAt { get; set; }

        /// <summary>
        /// End date (Unix timestamp, optional)
        /// </summary>
        [JsonProperty("endAt")]
        public long? EndAt { get; set; }

        /// <summary>
        /// Number of days the coupon is valid after issued (optional)
        /// </summary>
        [JsonProperty("validityPeriodInDays")]
        public int? ValidityPeriodInDays { get; set; }

        /// <summary>
        /// Maximum number of coupons that can be issued (optional)
        /// </summary>
        [JsonProperty("maxIssueLimit")]
        public long? MaxIssueLimit { get; set; }

        /// <summary>
        /// Whether the coupon can be issued more than once to the same user (optional)
        /// </summary>
        [JsonProperty("allowReissuance")]
        public bool? AllowReissuance { get; set; }
    }

    /// <summary>
    /// Coupon list response
    /// </summary>
    public class CouponList
    {
        /// <summary>
        /// List of coupons
        /// </summary>
        [JsonProperty("coupons")]
        public List<Coupon> Coupons { get; set; }

        /// <summary>
        /// Whether there is a next page
        /// </summary>
        [JsonProperty("hasNext")]
        public bool HasNext { get; set; }

        /// <summary>
        /// Next page token
        /// </summary>
        [JsonProperty("next")]
        public string Next { get; set; }
    }

    /// <summary>
    /// Membership subscription status
    /// https://developers.line.biz/en/reference/messaging-api/#get-membership-subscription
    /// </summary>
    public class MembershipSubscription
    {
        /// <summary>
        /// User ID
        /// </summary>
        [JsonProperty("userId")]
        public string UserId { get; set; }

        /// <summary>
        /// Membership ID
        /// </summary>
        [JsonProperty("membershipId")]
        public string MembershipId { get; set; }

        /// <summary>
        /// Subscription status. One of:
        /// - ACTIVE: Active
        /// - EXPIRED: Expired
        /// - CANCELLED: Cancelled
        /// </summary>
        [JsonProperty("status")]
        public string Status { get; set; }

        /// <summary>
        /// Subscription start date (Unix timestamp)
        /// </summary>
        [JsonProperty("subscribedAt")]
        public long SubscribedAt { get; set; }

        /// <summary>
        /// Subscription end date (Unix timestamp, optional)
        /// </summary>
        [JsonProperty("expiredAt")]
        public long? ExpiredAt { get; set; }
    }

    /// <summary>
    /// Membership plan
    /// </summary>
    public class MembershipPlan
    {
        /// <summary>
        /// Membership ID
        /// </summary>
        [JsonProperty("membershipId")]
        public string MembershipId { get; set; }

        /// <summary>
        /// Membership name
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>
        /// Membership description
        /// </summary>
        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>
        /// Number of members
        /// </summary>
        [JsonProperty("memberCount")]
        public long MemberCount { get; set; }

        /// <summary>
        /// Membership status. One of:
        /// - ACTIVE: Active
        /// - INACTIVE: Inactive
        /// </summary>
        [JsonProperty("status")]
        public string Status { get; set; }

        /// <summary>
        /// Creation time (Unix timestamp)
        /// </summary>
        [JsonProperty("createdAt")]
        public long CreatedAt { get; set; }
    }

    /// <summary>
    /// Membership plan list
    /// </summary>
    public class MembershipPlanList
    {
        /// <summary>
        /// List of membership plans
        /// </summary>
        [JsonProperty("memberships")]
        public List<MembershipPlan> Memberships { get; set; }
    }

    /// <summary>
    /// Membership user IDs list
    /// </summary>
    public class MembershipUserIds
    {
        /// <summary>
        /// List of user IDs
        /// </summary>
        [JsonProperty("userIds")]
        public List<string> UserIds { get; set; }

        /// <summary>
        /// Continuation token for next page
        /// </summary>
        [JsonProperty("next")]
        public string Next { get; set; }
    }
}

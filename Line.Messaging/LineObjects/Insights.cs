using Newtonsoft.Json;
using System.Collections.Generic;

namespace Line.Messaging
{
    /// <summary>
    /// Message delivery statistics
    /// https://developers.line.biz/en/reference/messaging-api/#get-number-of-delivery-messages
    /// </summary>
    public class MessageDelivery
    {
        /// <summary>
        /// Status of the counting process. One of:
        /// - ready: Counting is complete
        /// - unready: Counting is in progress
        /// - out_of_service: Aggregation has failed or is no longer available
        /// </summary>
        [JsonProperty("status")]
        public string Status { get; set; }

        /// <summary>
        /// Number of messages successfully sent
        /// </summary>
        [JsonProperty("success")]
        public long? Success { get; set; }
    }

    /// <summary>
    /// Follower statistics
    /// https://developers.line.biz/en/reference/messaging-api/#get-number-of-followers
    /// </summary>
    public class FollowerStatistics
    {
        /// <summary>
        /// Status of the counting process. One of:
        /// - ready: Counting is complete
        /// - unready: Counting is in progress
        /// - out_of_service: Aggregation has failed or is no longer available
        /// </summary>
        [JsonProperty("status")]
        public string Status { get; set; }

        /// <summary>
        /// Number of followers (friends)
        /// </summary>
        [JsonProperty("followers")]
        public long? Followers { get; set; }

        /// <summary>
        /// Number of target reach users
        /// </summary>
        [JsonProperty("targetedReaches")]
        public long? TargetedReaches { get; set; }

        /// <summary>
        /// Number of users who have blocked the account
        /// </summary>
        [JsonProperty("blocks")]
        public long? Blocks { get; set; }
    }

    /// <summary>
    /// Friend demographic statistics
    /// https://developers.line.biz/en/reference/messaging-api/#get-demographic
    /// </summary>
    public class DemographicStatistics
    {
        /// <summary>
        /// true: The data is ready / false: The data is not ready
        /// </summary>
        [JsonProperty("available")]
        public bool Available { get; set; }

        /// <summary>
        /// Gender demographics
        /// </summary>
        [JsonProperty("genders")]
        public List<DemographicItem> Genders { get; set; }

        /// <summary>
        /// Age demographics
        /// </summary>
        [JsonProperty("ages")]
        public List<DemographicItem> Ages { get; set; }

        /// <summary>
        /// Area demographics
        /// </summary>
        [JsonProperty("areas")]
        public List<DemographicItem> Areas { get; set; }

        /// <summary>
        /// App type demographics
        /// </summary>
        [JsonProperty("appTypes")]
        public List<DemographicItem> AppTypes { get; set; }

        /// <summary>
        /// Subscription period demographics
        /// </summary>
        [JsonProperty("subscriptionPeriods")]
        public List<DemographicItem> SubscriptionPeriods { get; set; }
    }

    /// <summary>
    /// Demographic item
    /// </summary>
    public class DemographicItem
    {
        /// <summary>
        /// Category name (e.g., "male", "female", "age_50~")
        /// </summary>
        [JsonProperty("category")]
        public string Category { get; set; }

        /// <summary>
        /// Percentage (0-100)
        /// </summary>
        [JsonProperty("percentage")]
        public decimal Percentage { get; set; }
    }

    /// <summary>
    /// User interaction statistics
    /// https://developers.line.biz/en/reference/messaging-api/#get-message-event
    /// </summary>
    public class UserInteractionStatistics
    {
        /// <summary>
        /// Overview statistics
        /// </summary>
        [JsonProperty("overview")]
        public InteractionOverview Overview { get; set; }

        /// <summary>
        /// Messages sent
        /// </summary>
        [JsonProperty("messages")]
        public List<MessageInteraction> Messages { get; set; }

        /// <summary>
        /// Clicks on URLs
        /// </summary>
        [JsonProperty("clicks")]
        public List<ClickInteraction> Clicks { get; set; }
    }

    /// <summary>
    /// Interaction overview
    /// </summary>
    public class InteractionOverview
    {
        /// <summary>
        /// Request ID
        /// </summary>
        [JsonProperty("requestId")]
        public string RequestId { get; set; }

        /// <summary>
        /// Timestamp
        /// </summary>
        [JsonProperty("timestamp")]
        public long Timestamp { get; set; }

        /// <summary>
        /// Delivered count
        /// </summary>
        [JsonProperty("delivered")]
        public long Delivered { get; set; }

        /// <summary>
        /// Unique opened count
        /// </summary>
        [JsonProperty("uniqueOpened")]
        public long UniqueOpened { get; set; }

        /// <summary>
        /// Unique clicked count
        /// </summary>
        [JsonProperty("uniqueClicked")]
        public long UniqueClicked { get; set; }

        /// <summary>
        /// Unique impression count
        /// </summary>
        [JsonProperty("uniqueImpression")]
        public long? UniqueImpression { get; set; }

        /// <summary>
        /// Unique media played count
        /// </summary>
        [JsonProperty("uniqueMediaPlayed")]
        public long? UniqueMediaPlayed { get; set; }

        /// <summary>
        /// Unique media played 25% count
        /// </summary>
        [JsonProperty("uniqueMediaPlayed25Percent")]
        public long? UniqueMediaPlayed25Percent { get; set; }

        /// <summary>
        /// Unique media played 50% count
        /// </summary>
        [JsonProperty("uniqueMediaPlayed50Percent")]
        public long? UniqueMediaPlayed50Percent { get; set; }

        /// <summary>
        /// Unique media played 75% count
        /// </summary>
        [JsonProperty("uniqueMediaPlayed75Percent")]
        public long? UniqueMediaPlayed75Percent { get; set; }

        /// <summary>
        /// Unique media played 100% count
        /// </summary>
        [JsonProperty("uniqueMediaPlayed100Percent")]
        public long? UniqueMediaPlayed100Percent { get; set; }
    }

    /// <summary>
    /// Message interaction
    /// </summary>
    public class MessageInteraction
    {
        /// <summary>
        /// Sequence number
        /// </summary>
        [JsonProperty("seq")]
        public int Seq { get; set; }

        /// <summary>
        /// Impression count
        /// </summary>
        [JsonProperty("impression")]
        public long? Impression { get; set; }

        /// <summary>
        /// Media played count
        /// </summary>
        [JsonProperty("mediaPlayed")]
        public long? MediaPlayed { get; set; }

        /// <summary>
        /// Media played 25% count
        /// </summary>
        [JsonProperty("mediaPlayed25Percent")]
        public long? MediaPlayed25Percent { get; set; }

        /// <summary>
        /// Media played 50% count
        /// </summary>
        [JsonProperty("mediaPlayed50Percent")]
        public long? MediaPlayed50Percent { get; set; }

        /// <summary>
        /// Media played 75% count
        /// </summary>
        [JsonProperty("mediaPlayed75Percent")]
        public long? MediaPlayed75Percent { get; set; }

        /// <summary>
        /// Media played 100% count
        /// </summary>
        [JsonProperty("mediaPlayed100Percent")]
        public long? MediaPlayed100Percent { get; set; }

        /// <summary>
        /// Unique impression count
        /// </summary>
        [JsonProperty("uniqueImpression")]
        public long? UniqueImpression { get; set; }

        /// <summary>
        /// Unique media played count
        /// </summary>
        [JsonProperty("uniqueMediaPlayed")]
        public long? UniqueMediaPlayed { get; set; }

        /// <summary>
        /// Unique media played 25% count
        /// </summary>
        [JsonProperty("uniqueMediaPlayed25Percent")]
        public long? UniqueMediaPlayed25Percent { get; set; }

        /// <summary>
        /// Unique media played 50% count
        /// </summary>
        [JsonProperty("uniqueMediaPlayed50Percent")]
        public long? UniqueMediaPlayed50Percent { get; set; }

        /// <summary>
        /// Unique media played 75% count
        /// </summary>
        [JsonProperty("uniqueMediaPlayed75Percent")]
        public long? UniqueMediaPlayed75Percent { get; set; }

        /// <summary>
        /// Unique media played 100% count
        /// </summary>
        [JsonProperty("uniqueMediaPlayed100Percent")]
        public long? UniqueMediaPlayed100Percent { get; set; }
    }

    /// <summary>
    /// Click interaction
    /// </summary>
    public class ClickInteraction
    {
        /// <summary>
        /// Sequence number
        /// </summary>
        [JsonProperty("seq")]
        public int Seq { get; set; }

        /// <summary>
        /// URL
        /// </summary>
        [JsonProperty("url")]
        public string Url { get; set; }

        /// <summary>
        /// Click count
        /// </summary>
        [JsonProperty("click")]
        public long Click { get; set; }

        /// <summary>
        /// Unique click count
        /// </summary>
        [JsonProperty("uniqueClick")]
        public long UniqueClick { get; set; }

        /// <summary>
        /// Unique click of request
        /// </summary>
        [JsonProperty("uniqueClickOfRequest")]
        public long? UniqueClickOfRequest { get; set; }
    }

    /// <summary>
    /// Statistics per unit aggregation
    /// https://developers.line.biz/en/reference/messaging-api/#get-statistics-per-unit
    /// </summary>
    public class StatisticsPerUnit
    {
        /// <summary>
        /// Custom aggregation unit name
        /// </summary>
        [JsonProperty("customAggregationUnit")]
        public string CustomAggregationUnit { get; set; }

        /// <summary>
        /// Number of messages
        /// </summary>
        [JsonProperty("messages")]
        public long Messages { get; set; }

        /// <summary>
        /// Number of delivered messages
        /// </summary>
        [JsonProperty("delivered")]
        public long Delivered { get; set; }

        /// <summary>
        /// Number of opened messages
        /// </summary>
        [JsonProperty("uniqueOpened")]
        public long UniqueOpened { get; set; }

        /// <summary>
        /// Number of users who opened and clicked
        /// </summary>
        [JsonProperty("uniqueClicked")]
        public long UniqueClicked { get; set; }
    }

    /// <summary>
    /// Aggregation info
    /// </summary>
    public class AggregationInfo
    {
        /// <summary>
        /// Number of aggregation units. Max: 100
        /// </summary>
        [JsonProperty("customAggregationUnitCount")]
        public int CustomAggregationUnitCount { get; set; }

        /// <summary>
        /// The date when the number of sent messages recorded reached 100
        /// </summary>
        [JsonProperty("customAggregationUnitCountReachedAt")]
        public long? CustomAggregationUnitCountReachedAt { get; set; }
    }

    /// <summary>
    /// Aggregation unit name list
    /// </summary>
    public class AggregationUnitNameList
    {
        /// <summary>
        /// List of aggregation unit names
        /// </summary>
        [JsonProperty("customAggregationUnits")]
        public List<string> CustomAggregationUnits { get; set; }

        /// <summary>
        /// Continuation token for next page
        /// </summary>
        [JsonProperty("next")]
        public string Next { get; set; }
    }
}

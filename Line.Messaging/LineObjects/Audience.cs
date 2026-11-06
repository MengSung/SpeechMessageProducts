/*
 * 檔案整體說明
 *
 * 目的與定位：本檔承載受眾群組與建立／更新受眾所需資料，使上傳使用者ID與受眾狀態維持官方JSON形狀。
 *
 * 主要功能與流程：
 * 本檔宣告 AudienceGroup、CreateUploadAudienceGroupRequest、AudienceRecipient、AddAudienceToGroupRequest、
 * CreateClickAudienceGroupRequest、CreateImpAudienceGroupRequest、CreateAudienceGroupResponse、AudienceGroupList。
 * 資料／選項欄位包含 AudienceGroup、AudienceGroupId、Description、Type、AudienceCount、Created、Permission、Status、FailedType、
 * CreateRoute、RequestId、JobStatus、CreateUploadAudienceGroupRequest、IsIfaAudience、UploadDescription、Audiences，名稱、
 * 型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging/ILineMessagingClient.cs、Line.Messaging/LineMessagingClient.cs 追查目前工作樹中的合作／使用位置。
 */
using Newtonsoft.Json;
using System.Collections.Generic;

namespace Line.Messaging
{
    /// <summary>
    /// Audience group for uploading user IDs
    /// https://developers.line.biz/en/reference/messaging-api/#create-upload-audience-group
    /// </summary>
    public class AudienceGroup
    {
        /// <summary>
        /// The audience ID
        /// </summary>
        [JsonProperty("audienceGroupId")]
        public long AudienceGroupId { get; set; }

        /// <summary>
        /// The audience's name
        /// </summary>
        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>
        /// Audience type. One of:
        /// - UPLOAD: Audience for uploading user IDs
        /// - CLICK: Audience for message click
        /// - IMP: Audience for message impression
        /// </summary>
        [JsonProperty("type")]
        public string Type { get; set; }

        /// <summary>
        /// The number of users included in the audience
        /// </summary>
        [JsonProperty("audienceCount")]
        public long AudienceCount { get; set; }

        /// <summary>
        /// When the audience was created (Unix timestamp)
        /// </summary>
        [JsonProperty("created")]
        public long Created { get; set; }

        /// <summary>
        /// Audience's update permission. One of:
        /// - READ: Can view
        /// - READ_WRITE: Can view and update
        /// </summary>
        [JsonProperty("permission")]
        public string Permission { get; set; }

        /// <summary>
        /// Audience group status. One of:
        /// - IN_PROGRESS
        /// - READY
        /// - FAILED
        /// - EXPIRED
        /// </summary>
        [JsonProperty("status")]
        public string Status { get; set; }

        /// <summary>
        /// Failed type (only when status is FAILED)
        /// </summary>
        [JsonProperty("failedType")]
        public string FailedType { get; set; }

        /// <summary>
        /// How the audience was created. One of:
        /// - OA_MANAGER: Created in LINE Official Account Manager
        /// - MESSAGING_API: Created with Messaging API
        /// </summary>
        [JsonProperty("createRoute")]
        public string CreateRoute { get; set; }

        /// <summary>
        /// Audience group request ID
        /// </summary>
        [JsonProperty("requestId")]
        public string RequestId { get; set; }

        /// <summary>
        /// Job status. One of:
        /// - QUEUED
        /// - WORKING
        /// - FINISHED
        /// - FAILED
        /// </summary>
        [JsonProperty("jobStatus")]
        public string JobStatus { get; set; }
    }

    /// <summary>
    /// Request to create audience for uploading user IDs
    /// </summary>
    public class CreateUploadAudienceGroupRequest
    {
        /// <summary>
        /// The audience's name (max 120 characters)
        /// </summary>
        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>
        /// To specify recipients by IFAs: set true. To specify recipients by user IDs: set false or omit.
        /// </summary>
        [JsonProperty("isIfaAudience")]
        public bool? IsIfaAudience { get; set; }

        /// <summary>
        /// Upload description (optional)
        /// </summary>
        [JsonProperty("uploadDescription")]
        public string UploadDescription { get; set; }

        /// <summary>
        /// An array of user IDs or IFAs. Max: 10,000
        /// </summary>
        [JsonProperty("audiences")]
        public List<AudienceRecipient> Audiences { get; set; }
    }

    /// <summary>
    /// Audience recipient (user ID or IFA)
    /// </summary>
    public class AudienceRecipient
    {
        /// <summary>
        /// User ID or IFA
        /// </summary>
        [JsonProperty("id")]
        public string Id { get; set; }
    }

    /// <summary>
    /// Request to add user IDs or IFAs to audience
    /// </summary>
    public class AddAudienceToGroupRequest
    {
        /// <summary>
        /// Audience group ID
        /// </summary>
        [JsonProperty("audienceGroupId")]
        public long AudienceGroupId { get; set; }

        /// <summary>
        /// Upload description (optional)
        /// </summary>
        [JsonProperty("uploadDescription")]
        public string UploadDescription { get; set; }

        /// <summary>
        /// An array of user IDs or IFAs to add. Max: 10,000
        /// </summary>
        [JsonProperty("audiences")]
        public List<AudienceRecipient> Audiences { get; set; }
    }

    /// <summary>
    /// Request to create click-based audience
    /// </summary>
    public class CreateClickAudienceGroupRequest
    {
        /// <summary>
        /// The audience's name (max 120 characters)
        /// </summary>
        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>
        /// Request ID of narrowcast or broadcast message
        /// </summary>
        [JsonProperty("requestId")]
        public string RequestId { get; set; }

        /// <summary>
        /// URL clicked by the user
        /// </summary>
        [JsonProperty("clickUrl")]
        public string ClickUrl { get; set; }
    }

    /// <summary>
    /// Request to create impression-based audience
    /// </summary>
    public class CreateImpAudienceGroupRequest
    {
        /// <summary>
        /// The audience's name (max 120 characters)
        /// </summary>
        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>
        /// Request ID of narrowcast or broadcast message
        /// </summary>
        [JsonProperty("requestId")]
        public string RequestId { get; set; }
    }

    /// <summary>
    /// Response when creating audience group
    /// </summary>
    public class CreateAudienceGroupResponse
    {
        /// <summary>
        /// The audience ID
        /// </summary>
        [JsonProperty("audienceGroupId")]
        public long AudienceGroupId { get; set; }

        /// <summary>
        /// Audience group type
        /// </summary>
        [JsonProperty("type")]
        public string Type { get; set; }

        /// <summary>
        /// The audience's name
        /// </summary>
        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>
        /// When the audience was created (Unix timestamp)
        /// </summary>
        [JsonProperty("created")]
        public long Created { get; set; }

        /// <summary>
        /// Audience group request ID
        /// </summary>
        [JsonProperty("requestId")]
        public string RequestId { get; set; }
    }

    /// <summary>
    /// List of audience groups
    /// </summary>
    public class AudienceGroupList
    {
        /// <summary>
        /// An array of audience data
        /// </summary>
        [JsonProperty("audienceGroups")]
        public List<AudienceGroup> AudienceGroups { get; set; }

        /// <summary>
        /// true when there is more data
        /// </summary>
        [JsonProperty("hasNextPage")]
        public bool HasNextPage { get; set; }

        /// <summary>
        /// Of the audience IDs under the official account, the total number of audiences where includesExternalPublicGroups is true.
        /// </summary>
        [JsonProperty("totalCount")]
        public long TotalCount { get; set; }

        /// <summary>
        /// A continuation token to get next page
        /// </summary>
        [JsonProperty("readWriteAudienceGroupTotalCount")]
        public long ReadWriteAudienceGroupTotalCount { get; set; }

        /// <summary>
        /// The next page token
        /// </summary>
        [JsonProperty("page")]
        public long Page { get; set; }

        /// <summary>
        /// The number of elements per page
        /// </summary>
        [JsonProperty("size")]
        public long Size { get; set; }
    }
}

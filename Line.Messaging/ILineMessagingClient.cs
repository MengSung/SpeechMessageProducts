using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Line.Messaging
{
    /// <summary>
    /// LINE Messaging API client, which handles request/response to LINE server.
    /// </summary>
    public interface ILineMessagingClient
    {
        #region Message 

        /// <summary>
        /// Respond to events from users, groups, and rooms
        /// https://developers.line.biz/en/reference/messaging-api/#send-reply-message
        /// </summary>
        /// <param name="replyToken">ReplyToken</param>
        /// <param name="messages">Reply messages. Up to 5 messages.</param>
        Task ReplyMessageAsync(string replyToken, IList<ISendMessage> messages);

        /// <summary>
        /// Respond to events from users, groups, and rooms
        /// https://developers.line.biz/en/reference/messaging-api/#send-reply-message
        /// </summary>
        /// <param name="replyToken">ReplyToken</param>
        /// <param name="messages">Reply Text messages. Up to 5 messages.</param>
        Task ReplyMessageAsync(string replyToken, params string[] messages);

        /// <summary>
        /// Respond to events from users, groups, and rooms
        /// https://developers.line.biz/en/reference/messaging-api/#send-reply-message
        /// </summary>
        /// <param name="replyToken">ReplyToken</param>
        /// <param name="messages">Set reply messages with Json string.</param>
        Task ReplyMessageWithJsonAsync(string replyToken, params string[] messages);

        /// <summary>
        /// Send messages to a user, group, or room at any time.
        /// Note: Use of push messages are limited to certain plans.
        /// </summary>
        /// <param name="to">ID of the receiver</param>
        /// <param name="messages">Reply messages. Up to 5 messages.</param>
        Task PushMessageAsync(string to, IList<ISendMessage> messages);

        /// <summary>
        /// Send messages to a user, group, or room with LINE retry-key support.
        /// </summary>
        /// <param name="to">ID of the receiver</param>
        /// <param name="messages">Reply messages. Up to 5 messages.</param>
        /// <param name="retryKey">Optional LINE retry key for idempotent retries. Null or whitespace means no retry header.</param>
        Task PushMessageAsync(string to, IList<ISendMessage> messages, string? retryKey);

        /// <summary>
        /// Send messages to a user, group, or room at any time.
        /// Note: Use of push messages are limited to certain plans.
        /// </summary>
        /// <param name="to">ID of the receiver</param>
        /// <param name="messages">Set reply messages with Json string.</param>
        Task PushMessageWithJsonAsync(string to, params string[] messages);

        /// <summary>
        /// Send text messages to a user, group, or room at any time.
        /// Note: Use of push messages are limited to certain plans.
        /// </summary>
        /// <param name="to">ID of the receiver</param>
        /// <param name="messages">Reply text messages. Up to 5 messages.</param>
        Task PushMessageAsync(string to, params string[] messages);

        /// <summary>
        /// Send push messages to multiple users at any time.
        /// Only available for plans which support push messages. Messages cannot be sent to groups or rooms
        /// https://developers.line.biz/en/reference/messaging-api/#send-multicast-messages
        /// </summary>
        /// <param name="to">IDs of the receivers. Max: 500 users</param>
        /// <param name="messages">Reply messages. Up to 5 messages.</param>
        Task MultiCastMessageAsync(IList<string> to, IList<ISendMessage> messages);

        /// <summary>
        /// Send push messages to multiple users with LINE retry-key support.
        /// </summary>
        /// <param name="to">IDs of the receivers. Max: 500 users</param>
        /// <param name="messages">Reply messages. Up to 5 messages.</param>
        /// <param name="retryKey">Optional LINE retry key for idempotent retries. Null or whitespace means no retry header.</param>
        Task MultiCastMessageAsync(IList<string> to, IList<ISendMessage> messages, string? retryKey);

        /// <summary>
        /// Send push messages to multiple users at any time.
        /// Only available for plans which support push messages. Messages cannot be sent to groups or rooms
        /// https://developers.line.biz/en/reference/messaging-api/#send-multicast-messages
        /// </summary>
        /// <param name="to">IDs of the receivers. Max: 500 users</param>
        /// <param name="messages">Set reply messages with Json string.</param>
        Task MultiCastMessageWithJsonAsync(IList<string> to, params string[] messages);

        /// <summary>
        /// Send push text messages to multiple users at any time.
        /// Only available for plans which support push messages. Messages cannot be sent to groups or rooms
        /// https://developers.line.biz/en/reference/messaging-api/#send-multicast-messages
        /// </summary>
        /// <param name="to">IDs of the receivers. Max: 500 users</param>
        /// <param name="messages">Reply text messages. Up to 5 messages.</param>
        Task MultiCastMessageAsync(IList<string> to, params string[] messages);


        /// <summary>
        /// Sends push messages to multiple users at any time. Use IDs of groups or rooms to send messages to all users in a group or room.
        /// https://developers.line.biz/en/reference/messaging-api/#send-broadcast-message
        /// </summary>
        /// <param name="messages">Messages to send. Max: 5 messages</param>
        Task BroadcastMessageAsync(IList<ISendMessage> messages);

        /// <summary>
        /// Broadcasts messages with LINE retry-key support.
        /// </summary>
        /// <param name="messages">Messages to send. Max: 5 messages</param>
        /// <param name="retryKey">Optional LINE retry key for idempotent retries. Null or whitespace means no retry header.</param>
        Task BroadcastMessageAsync(IList<ISendMessage> messages, string? retryKey);

        /// <summary>
        /// Sends push messages to multiple users specified by attributes (such as gender, age, OS, region, friendship duration) or retargeting (audiences).
        /// https://developers.line.biz/en/reference/messaging-api/#send-narrowcast-message
        /// </summary>
        /// <param name="messages">Messages to send. Max: 5 messages</param>
        /// <param name="recipient">Recipient object (Optional). Specify the recipient using a filter or audience ID. Max: 10 recipient objects</param>
        /// <param name="filter">Filter object (Optional). Demographic filter object. You can use friends added as friends on LINE Official Account</param>
        /// <param name="limit">Limit object (Optional). The maximum number of narrowcast messages to send. Use this when you want to limit the number of people who will receive messages.</param>
        /// <returns>Request ID</returns>
        Task<string> NarrowcastMessageAsync(IList<ISendMessage> messages, object recipient = null, object filter = null, object limit = null);

        /// <summary>
        /// Gets the status of narrowcast message.
        /// https://developers.line.biz/en/reference/messaging-api/#get-narrowcast-progress-status
        /// </summary>
        /// <param name="requestId">Request ID returned by narrowcast message sending</param>
        /// <returns>Narrowcast progress</returns>
        Task<NarrowcastProgress> GetNarrowcastProgressAsync(string requestId);

        /// <summary>
        /// Mark webhook messages as read by using LINE's official mark-as-read token.
        /// https://developers.line.biz/en/reference/messaging-api/#mark-messages-as-read
        /// </summary>
        /// <param name="markAsReadToken">Token received from the webhook event that identifies the messages to mark as read.</param>
        Task MarkAsReadByTokenAsync(string markAsReadToken);

        /// <summary>
        /// Legacy API placeholder kept only so older callers fail with a clear message instead of sending chatId as markAsReadToken.
        /// </summary>
        /// <param name="chatId">Legacy chat ID parameter. LINE's current official API requires a webhook markAsReadToken instead.</param>
        [Obsolete("Use MarkAsReadByTokenAsync(markAsReadToken). LINE official API uses markAsReadToken, not chatId.")]
        Task MarkAsReadAsync(string chatId);

        /// <summary>
        /// Display a loading animation on the chat screen.
        /// https://developers.line.biz/en/reference/messaging-api/#display-a-loading-animation
        /// </summary>
        /// <param name="chatId">The identifier of chat. The chat can be a user, group, or room. Use userId for user, groupId for group, roomId for room.</param>
        /// <param name="loadingSeconds">The number of seconds to display the loading animation. Max: 60 seconds. Default: 20 seconds</param>
        Task ShowLoadingAnimationAsync(string chatId, int loadingSeconds = 20);

        /// <summary>
        /// Get the target limit for sending messages in the current month.
        /// https://developers.line.biz/en/reference/messaging-api/#get-quota
        /// </summary>
        /// <returns>Message quota</returns>
        Task<MessageQuota> GetMessageQuotaAsync();

        /// <summary>
        /// Get the number of sent messages in the current month.
        /// https://developers.line.biz/en/reference/messaging-api/#get-consumption
        /// </summary>
        /// <returns>Message quota consumption</returns>
        Task<MessageQuotaConsumption> GetMessageQuotaConsumptionAsync();

        /// <summary>
        /// Get number of sent broadcast messages.
        /// https://developers.line.biz/en/reference/messaging-api/#get-number-of-broadcast-messages
        /// </summary>
        /// <param name="date">Date the messages were sent (format: yyyyMMdd, timezone: UTC+9)</param>
        /// <returns>Number of sent messages</returns>
        Task<NumberOfSentMessages> GetNumberOfSentBroadcastMessagesAsync(DateTime date);

        /// <summary>
        /// Retrieve image, video, and audio data sent by users as Stream
        /// https://developers.line.biz/en/reference/messaging-api/#get-content
        /// </summary>
        /// <param name="messageId">Message ID</param>
        /// <returns>Content as ContentStream</returns>
        Task<ContentStream> GetContentStreamAsync(string messageId);

        /// <summary>
        /// Retrieve image, video, and audio data sent by users as byte array
        /// https://developers.line.biz/en/reference/messaging-api/#get-content
        /// </summary>
        /// <param name="messageId">Message ID</param>
        /// <returns>Content as byte array</returns>
        Task<byte[]> GetContentBytesAsync(string messageId);

        /// <summary>
        /// Verify the preparation status of a video or audio for getting.
        /// https://developers.line.biz/en/reference/messaging-api/#verify-the-preparation-status
        /// </summary>
        /// <param name="messageId">Message ID</param>
        /// <returns>True if content is ready, false if still processing</returns>
        Task<bool> VerifyContentPreparationAsync(string messageId);

        /// <summary>
        /// Get a preview image of the image or video.
        /// https://developers.line.biz/en/reference/messaging-api/#get-a-preview-image-of-the-image-or-video
        /// </summary>
        /// <param name="messageId">Message ID</param>
        /// <returns>Preview image as ContentStream</returns>
        Task<ContentStream> GetContentPreviewAsync(string messageId);

        #endregion

        #region Profile

        /// <summary>
        /// Get user profile information.
        /// https://developers.line.biz/en/reference/messaging-api/#get-profile
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <returns></returns>
        Task<UserProfile> GetUserProfileAsync(string userId);

        #endregion

        #region Bot

        /// <summary>
        /// Get bot information.
        /// https://developers.line.biz/en/reference/messaging-api/#get-bot-info
        /// </summary>
        /// <returns>Bot information</returns>
        Task<BotInfo> GetBotInfoAsync();

        #endregion

        #region Group

        /// <summary>
        /// Gets the user profile of a member of a group that the bot is in. This includes user profiles of users who have not added the bot as a friend or have blocked the bot.
        /// Use the group ID and user ID returned in the source object of webhook event objects. Do not use the LINE ID used in the LINE app. 
        /// https://developers.line.biz/en/reference/messaging-api/#get-group-member-profile
        /// </summary>
        /// <param name="groupId">Identifier of the group</param>
        /// <param name="userId">Identifier of the user</param>
        /// <returns>User Profile</returns>
        Task<UserProfile> GetGroupMemberProfileAsync(string groupId, string userId);

        /// <summary>
        /// Gets the user IDs of the members of a group that the bot is in. This includes the user IDs of users who have not added the bot as a friend or has blocked the bot.
        /// This feature is only available for LINE@ Approved accounts or official accounts.
        /// Use the group Id returned in the source object of webhook event objects. 
        /// Users who have not agreed to the Official Accounts Terms of Use are not included in memberIds. There is no fixed number of memberIds. 
        /// https://developers.line.biz/en/reference/messaging-api/#get-group-member-user-ids
        /// </summary>
        /// <param name="groupId">Identifier of the group</param>
        /// <param name="continuationToken">ContinuationToken</param>
        /// <returns>GroupMemberIds</returns>
        Task<GroupMemberIds> GetGroupMemberIdsAsync(string groupId, string continuationToken);

        /// <summary>
        /// Gets the user profiles of the members of a group that the bot is in. This includes the user IDs of users who have not added the bot as a friend or has blocked the bot.
        /// Use the group Id returned in the source object of webhook event objects. 
        /// This feature is only available for LINE@ Approved accounts or official accounts
        /// </summary>
        /// <param name="groupId">Identifier of the group</param>
        /// <returns>List of UserProfile</returns>
        Task<IList<UserProfile>> GetGroupMemberProfilesAsync(string groupId);

        /// <summary>
        /// Get group summary.
        /// https://developers.line.biz/en/reference/messaging-api/#get-group-summary
        /// </summary>
        /// <param name="groupId">Group ID</param>
        /// <returns>Group summary</returns>
        Task<GroupSummary> GetGroupSummaryAsync(string groupId);

        /// <summary>
        /// Get number of users in a group.
        /// https://developers.line.biz/en/reference/messaging-api/#get-members-group-count
        /// </summary>
        /// <param name="groupId">Group ID</param>
        /// <returns>Member count</returns>
        Task<int> GetGroupMemberCountAsync(string groupId);

        /// <summary>
        /// Leave a group.
        /// Use the ID that is returned via webhook from the source group. 
        /// https://developers.line.biz/en/reference/messaging-api/#leave-group
        /// </summary>
        /// <param name="groupId">Group ID</param>
        /// <returns></returns>
        Task LeaveFromGroupAsync(string groupId);

        #endregion

        #region Room

        /// <summary>
        /// Gets the user profile of a member of a room that the bot is in. This includes user profiles of users who have not added the bot as a friend or have blocked the bot.
        /// Use the room ID and user ID returned in the source object of webhook event objects. Do not use the LINE ID used in the LINE app
        /// </summary>
        /// <param name="roomId">Identifier of the room</param>
        /// <param name="userId">Identifier of the user</param>
        /// <returns></returns>
        Task<UserProfile> GetRoomMemberProfileAsync(string roomId, string userId);

        /// <summary>
        /// Gets the user IDs of the members of a room that the bot is in. This includes the user IDs of users who have not added the bot as a friend or has blocked the bot.
        /// Use the room ID returned in the source object of webhook event objects. 
        /// This feature is only available for LINE@ Approved accounts or official accounts.
        /// https://developers.line.biz/en/reference/messaging-api/#get-room-member-user-ids
        /// </summary>
        /// <param name="roomId">Identifier of the room</param>
        /// <param name="continuationToken">ContinuationToken</param>
        /// <returns>GroupMemberIds</returns>
        Task<GroupMemberIds> GetRoomMemberIdsAsync(string roomId, string continuationToken = null);

        /// <summary>
        /// Gets the user profiles of the members of a room that the bot is in. This includes the user IDs of users who have not added the bot as a friend or has blocked the bot.
        /// Use the room ID returned in the source object of webhook event objects. 
        /// This feature is only available for LINE@ Approved accounts or official accounts.
        /// </summary>
        /// <param name="roomId">Identifier of the room</param>
        /// <returns>List of UserProfiles</returns>
        Task<IList<UserProfile>> GetRoomMemberProfilesAsync(string roomId);

        /// <summary>
        /// Get number of users in a multi-person chat.
        /// https://developers.line.biz/en/reference/messaging-api/#get-members-room-count
        /// </summary>
        /// <param name="roomId">Room ID</param>
        /// <returns>Member count</returns>
        Task<int> GetRoomMemberCountAsync(string roomId);

        /// <summary>
        /// Leave a room.
        /// Use the ID that is returned via webhook from the source room. 
        /// </summary>
        /// <param name="roomId">Room ID</param>
        Task LeaveFromRoomAsync(string roomId);

        #endregion

        #region Webhook

        /// <summary>
        /// Set webhook endpoint URL.
        /// https://developers.line.biz/en/reference/messaging-api/#set-webhook-endpoint-url
        /// </summary>
        /// <param name="endpoint">Webhook URL</param>
        Task SetWebhookEndpointAsync(string endpoint);

        /// <summary>
        /// Get webhook endpoint information.
        /// https://developers.line.biz/en/reference/messaging-api/#get-webhook-endpoint-information
        /// </summary>
        /// <returns>Webhook endpoint information</returns>
        Task<WebhookEndpoint> GetWebhookEndpointAsync();

        /// <summary>
        /// Test webhook endpoint.
        /// https://developers.line.biz/en/reference/messaging-api/#test-webhook-endpoint
        /// </summary>
        /// <param name="endpoint">Webhook URL to test (optional, uses configured endpoint if not specified)</param>
        /// <returns>Webhook test result</returns>
        Task<WebhookTestResult> TestWebhookEndpointAsync(string endpoint = null);

        #endregion

        #region Rich menu

        /// <summary>
        /// 透過 LINE provider 端的 richMenuId 取得單一 RichMenu 定義。
        /// 此方法只查詢已建立的 RichMenu metadata / area 設定，不會下載圖片內容。
        /// https://developers.line.biz/en/reference/messaging-api/#get-rich-menu
        /// </summary>
        /// <param name="richMenuId">LINE 已上傳 RichMenu 的 provider ID。</param>
        /// <returns>RichMenu 版面與 action 設定。</returns>
        Task<RichMenu> GetRichMenuAsync(string richMenuId);

        /// <summary>
        /// 建立 RichMenu metadata 與可點擊區域設定，並回傳 LINE 產生的 richMenuId。
        /// 建立後還必須另外上傳圖片，並將 RichMenu 綁定到使用者或設為預設選單，使用者端才會看到。
        /// LINE 官方帳號最多可建立 1000 個 RichMenu，因此 provisioning 流程需避免重複建立。
        /// https://developers.line.biz/en/reference/messaging-api/#create-rich-menu
        /// </summary>
        /// <param name="richMenu">要送到 LINE 建立的 RichMenu 版面物件。</param>
        /// <returns>LINE provider 產生的 richMenuId。</returns>
        Task<string> CreateRichMenuAsync(RichMenu richMenu);

        /// <summary>
        /// 驗證 RichMenu 物件是否符合 LINE 建立規則，但不實際建立。
        /// https://developers.line.biz/en/reference/messaging-api/#validate-rich-menu-object
        /// </summary>
        /// <param name="richMenu">要驗證的 RichMenu 版面物件。</param>
        Task ValidateRichMenuAsync(RichMenu richMenu);

        /// <summary>
        /// 刪除指定 provider richMenuId 的 RichMenu。
        /// 此操作會移除 LINE 端資源；若同一 RichMenu 被多位使用者或 alias 共用，呼叫端必須先確認生命週期。
        /// https://developers.line.biz/en/reference/messaging-api/#delete-rich-menu
        /// </summary>
        /// <param name="richMenuId">要刪除的 LINE provider richMenuId。</param>
        Task DeleteRichMenuAsync(string richMenuId);

        /// <summary>
        /// 查詢單一使用者目前被直接綁定的 RichMenu ID。
        /// 回傳值是 provider richMenuId，不是應用程式 menu key 或 alias id。
        /// https://developers.line.biz/en/reference/messaging-api/#get-rich-menu-id-of-user
        /// </summary>
        /// <param name="userId">LINE webhook event 中的 userId。</param>
        /// <returns>使用者目前綁定的 provider richMenuId。</returns>
        Task<string> GetRichMenuIdOfUserAsync(string userId);

        /// <summary>
        /// 設定官方帳號層級的預設 RichMenu。
        /// 預設 RichMenu 會影響沒有個人綁定選單的使用者，屬於較大範圍的 channel 設定。
        /// https://developers.line.biz/en/reference/messaging-api/#set-default-rich-menu
        /// </summary>
        /// <param name="richMenuId">要設為預設選單的 provider richMenuId。</param>
        Task SetDefaultRichMenuAsync(string richMenuId);

        /// <summary>
        /// 取得官方帳號目前設定的預設 RichMenu ID。
        /// https://developers.line.biz/en/reference/messaging-api/#get-default-rich-menu-id
        /// </summary>
        /// <returns>目前預設選單的 provider richMenuId。</returns>
        Task<string> GetDefaultRichMenuIdAsync();

        /// <summary>
        /// 取消官方帳號目前設定的預設 RichMenu。
        /// https://developers.line.biz/en/reference/messaging-api/#cancel-default-rich-menu
        /// </summary>
        Task CancelDefaultRichMenuAsync();

        /// <summary>
        /// 將 RichMenu 直接綁定到單一使用者。
        /// LINE 同一時間只允許一位使用者有一個直接綁定的 RichMenu；再次綁定會覆蓋原本選單。
        /// https://developers.line.biz/en/reference/messaging-api/#link-rich-menu-to-user
        /// </summary>
        /// <param name="userId">LINE webhook event 中的 userId。</param>
        /// <param name="richMenuId">要綁定的 provider richMenuId。</param>
        Task LinkRichMenuToUserAsync(string userId, string richMenuId);

        /// <summary>
        /// 將同一個 RichMenu 批次綁定到多位使用者。
        /// https://developers.line.biz/en/reference/messaging-api/#link-rich-menu-to-users
        /// </summary>
        /// <param name="richMenuId">要綁定的 provider richMenuId。</param>
        /// <param name="userIds">LINE userId 清單，最多 500 位使用者。</param>
        Task LinkRichMenuToUsersAsync(string richMenuId, IList<string> userIds);

        /// <summary>
        /// 解除單一使用者目前直接綁定的 RichMenu。
        /// https://developers.line.biz/en/reference/messaging-api/#unlink-rich-menu-from-user
        /// </summary>
        /// <param name="userId">LINE webhook event 中的 userId。</param>
        Task UnLinkRichMenuFromUserAsync(string userId);

        /// <summary>
        /// 批次解除多位使用者目前直接綁定的 RichMenu。
        /// https://developers.line.biz/en/reference/messaging-api/#unlink-rich-menu-from-users
        /// </summary>
        /// <param name="userIds">LINE userId 清單，最多 500 位使用者。</param>
        Task UnLinkRichMenuFromUsersAsync(IList<string> userIds);

        /// <summary>
        /// 以 batch-control API 批次替換或解除 RichMenu 綁定。
        /// 此 API 由 LINE 非同步處理，呼叫端需另外查詢 requestId 的進度。
        /// https://developers.line.biz/en/reference/messaging-api/#batch-control-rich-menus
        /// </summary>
        /// <param name="operations">RichMenu batch operation 集合，最多 30 筆操作。</param>
        Task RichMenuBatchOperationAsync(IList<RichMenuBatchOperation> operations);

        /// <summary>
        /// 查詢 RichMenu batch-control request 的非同步處理狀態。
        /// https://developers.line.biz/en/reference/messaging-api/#get-batch-control-rich-menus-progress-status
        /// </summary>
        /// <param name="requestId">batch-control operation 回傳的 requestId。</param>
        /// <returns>LINE 回報的 batch 進度。</returns>
        Task<RichMenuBatchProgress> GetRichMenuBatchProgressAsync(string requestId);

        /// <summary>
        /// 驗證 RichMenu batch-control request 是否符合 LINE 格式要求，但不實際送出。
        /// https://developers.line.biz/en/reference/messaging-api/#validate-batch-control-rich-menus-request
        /// </summary>
        /// <param name="operations">要驗證的 batch operation 集合。</param>
        Task ValidateRichMenuBatchRequestAsync(IList<RichMenuBatchOperation> operations);

        /// <summary>
        /// 下載指定 RichMenu 目前關聯的圖片內容。
        /// https://developers.line.biz/en/reference/messaging-api/#download-rich-menu-image
        /// </summary>
        /// <param name="richMenuId">要下載圖片的 provider richMenuId。</param>
        /// <returns>RichMenu 圖片內容 stream。</returns>
        Task<ContentStream> DownloadRichMenuImageAsync(string richMenuId);

        /// <summary>
        /// 上傳 JPEG 圖片並關聯到指定 RichMenu。
        /// 圖片尺寸必須是 2500x1686 或 2500x843。
        /// LINE 不允許替換已關聯圖片；若要更新圖片，需建立新的 RichMenu 並重新上傳。
        /// https://developers.line.biz/en/reference/messaging-api/#upload-rich-menu-image
        /// </summary>
        /// <param name="stream">要上傳的 JPEG 圖片 stream。</param>
        /// <param name="richMenuId">要關聯圖片的 provider richMenuId。</param>
        Task UploadRichMenuJpegImageAsync(Stream stream, string richMenuId);

        /// <summary>
        /// 上傳 PNG 圖片並關聯到指定 RichMenu。
        /// 圖片尺寸必須是 2500x1686 或 2500x843。
        /// LINE 不允許替換已關聯圖片；若要更新圖片，需建立新的 RichMenu 並重新上傳。
        /// https://developers.line.biz/en/reference/messaging-api/#upload-rich-menu-image
        /// </summary>
        /// <param name="stream">要上傳的 PNG 圖片 stream。</param>
        /// <param name="richMenuId">要關聯圖片的 provider richMenuId。</param>
        Task UploadRichMenuPngImageAsync(Stream stream, string richMenuId);

        /// <summary>
        /// 取得官方帳號底下所有已上傳 RichMenu 的清單。
        /// https://developers.line.biz/en/reference/messaging-api/#get-rich-menu-list
        /// </summary>
        /// <returns>LINE 回傳的 ResponseRichMenu 清單。</returns>
        Task<IList<ResponseRichMenu>> GetRichMenuListAsync();

        #endregion

        #region Rich menu alias

        /// <summary>
        /// 建立 RichMenu alias，讓應用程式用穩定 alias 指向 provider richMenuId。
        /// https://developers.line.biz/en/reference/messaging-api/#create-rich-menu-alias
        /// </summary>
        /// <param name="richMenuId">alias 要指向的 provider richMenuId。</param>
        /// <param name="richMenuAliasId">RichMenu 別名 ID，最多 100 字元。</param>
        Task CreateRichMenuAliasAsync(string richMenuId, string richMenuAliasId);

        /// <summary>
        /// 刪除 RichMenu alias。
        /// https://developers.line.biz/en/reference/messaging-api/#delete-rich-menu-alias
        /// </summary>
        /// <param name="richMenuAliasId">要刪除的 RichMenu 別名 ID。</param>
        Task DeleteRichMenuAliasAsync(string richMenuAliasId);

        /// <summary>
        /// 更新 RichMenu alias 指向的 provider richMenuId。
        /// https://developers.line.biz/en/reference/messaging-api/#update-rich-menu-alias
        /// </summary>
        /// <param name="richMenuAliasId">要更新的 RichMenu 別名 ID。</param>
        /// <param name="richMenuId">alias 新的 provider richMenuId。</param>
        Task UpdateRichMenuAliasAsync(string richMenuAliasId, string richMenuId);

        /// <summary>
        /// 取得單一 RichMenu alias 的目前指向資訊。
        /// https://developers.line.biz/en/reference/messaging-api/#get-rich-menu-alias-information
        /// </summary>
        /// <param name="richMenuAliasId">RichMenu 別名 ID。</param>
        /// <returns>RichMenu alias 與其指向的 provider richMenuId。</returns>
        Task<RichMenuAlias> GetRichMenuAliasAsync(string richMenuAliasId);

        /// <summary>
        /// 取得官方帳號底下所有 RichMenu alias。
        /// https://developers.line.biz/en/reference/messaging-api/#get-rich-menu-alias-list
        /// </summary>
        /// <returns>RichMenu alias 清單。</returns>
        Task<RichMenuAliasList> GetRichMenuAliasListAsync();

        #endregion

        #region Account Link

        /// <summary>
        /// Issues a link token used for the account link feature.
        /// <para>https://developers.line.biz/en/reference/messaging-api/#issue-link-token</para>
        /// </summary>
        /// <param name="userId">
        /// User ID for the LINE account to be linked. Found in the source object of account link event objects. Do not use the LINE ID used in the LINE app.
        /// </param>
        /// <returns>
        /// Returns the status code 200 and a link token. Link tokens are valid for 10 minutes and can only be used once.
        /// Note: The validity period may change without notice.
        /// </returns>
        Task<string> IssueLinkTokenAsync(string userId);

        #endregion

        #region Number of sent messages

        /// <summary>
        /// Gets the number of messages sent with the /bot/message/reply endpoint.
        /// The number of messages retrieved by this operation does not include the number of messages sent from LINE Official Account Manager.
        /// </summary>
        /// <param name="date">
        /// - Date the messages were sent
        /// - Format: yyyyMMdd(Example: 20191231)
        /// - Timezone: UTC+9
        /// </param>
        /// <returns>
        /// <see cref="Line.Messaging.NumberOfSentMessages"/>
        /// </returns>
        Task<NumberOfSentMessages> GetNumberOfSentReplyMessagesAsync(DateTime date);

        /// <summary>
        /// Gets the number of messages sent with the /bot/message/push endpoint.
        /// The number of messages retrieved by this operation does not include the number of messages sent from LINE Official Account Manager.
        ///</summary>
        /// <param name="date">
        /// - Date the messages were sent
        /// - Format: yyyyMMdd(Example: 20191231)
        /// - Timezone: UTC+9
        /// </param>
        /// <returns>
        /// <see cref="Line.Messaging.NumberOfSentMessages"/>
        /// </returns>
        Task<NumberOfSentMessages> GetNumberOfSentPushMessagesAsync(DateTime date);

        /// <summary>
        /// Gets the number of messages sent with the /bot/message/multicast endpoint.
        /// The number of messages retrieved by this operation does not include the number of messages sent from LINE Official Account Manager.
        /// </summary>
        /// <param name="date">
        /// - Date the messages were sent
        /// - Format: yyyyMMdd(Example: 20191231)
        /// - Timezone: UTC+9
        /// </param>
        /// <returns>
        /// <see cref="Line.Messaging.NumberOfSentMessages"/>
        /// </returns>
        Task<NumberOfSentMessages> GetNumberOfSentMulticastMessagesAsync(DateTime date);

        #endregion

        #region Message Validation

        /// <summary>
        /// Validate message objects of a reply message.
        /// https://developers.line.biz/en/reference/messaging-api/#validate-reply-message
        /// </summary>
        /// <param name="messages">Messages to validate (max 5)</param>
        Task ValidateReplyMessageAsync(IList<ISendMessage> messages);

        /// <summary>
        /// Validate message objects of a push message.
        /// https://developers.line.biz/en/reference/messaging-api/#validate-push-message
        /// </summary>
        /// <param name="messages">Messages to validate (max 5)</param>
        Task ValidatePushMessageAsync(IList<ISendMessage> messages);

        /// <summary>
        /// Validate message objects of a multicast message.
        /// https://developers.line.biz/en/reference/messaging-api/#validate-multicast-message
        /// </summary>
        /// <param name="messages">Messages to validate (max 5)</param>
        Task ValidateMulticastMessageAsync(IList<ISendMessage> messages);

        /// <summary>
        /// Validate message objects of a narrowcast message.
        /// https://developers.line.biz/en/reference/messaging-api/#validate-narrowcast-message
        /// </summary>
        /// <param name="messages">Messages to validate (max 5)</param>
        Task ValidateNarrowcastMessageAsync(IList<ISendMessage> messages);

        /// <summary>
        /// Validate message objects of a broadcast message.
        /// https://developers.line.biz/en/reference/messaging-api/#validate-broadcast-message
        /// </summary>
        /// <param name="messages">Messages to validate (max 5)</param>
        Task ValidateBroadcastMessageAsync(IList<ISendMessage> messages);

        #endregion

        #region Audience Management

        /// <summary>
        /// Create audience for uploading user IDs (by JSON).
        /// https://developers.line.biz/en/reference/messaging-api/#create-upload-audience-group
        /// </summary>
        /// <param name="request">Create audience request</param>
        /// <returns>Audience group response</returns>
        Task<CreateAudienceGroupResponse> CreateUploadAudienceGroupAsync(CreateUploadAudienceGroupRequest request);

        /// <summary>
        /// Create audience for uploading user IDs (by file).
        /// https://developers.line.biz/en/reference/messaging-api/#create-upload-audience-group-by-file
        /// </summary>
        /// <param name="description">Audience name</param>
        /// <param name="isIfaAudience">Whether to use IFA</param>
        /// <param name="uploadDescription">Upload description</param>
        /// <param name="fileStream">CSV file stream containing user IDs or IFAs</param>
        /// <returns>Audience group response</returns>
        Task<CreateAudienceGroupResponse> CreateUploadAudienceGroupByFileAsync(string description, bool? isIfaAudience, string uploadDescription, System.IO.Stream fileStream);

        /// <summary>
        /// Add user IDs or IFAs to an audience for uploading user IDs (by JSON).
        /// https://developers.line.biz/en/reference/messaging-api/#update-upload-audience-group
        /// </summary>
        /// <param name="request">Add audience request</param>
        Task AddAudienceToGroupAsync(AddAudienceToGroupRequest request);

        /// <summary>
        /// Add user IDs or IFAs to an audience for uploading user IDs (by file).
        /// https://developers.line.biz/en/reference/messaging-api/#update-upload-audience-group-by-file
        /// </summary>
        /// <param name="audienceGroupId">Audience group ID</param>
        /// <param name="uploadDescription">Upload description</param>
        /// <param name="fileStream">CSV file stream</param>
        Task AddAudienceToGroupByFileAsync(long audienceGroupId, string uploadDescription, System.IO.Stream fileStream);

        /// <summary>
        /// Create audience for click-based messages.
        /// https://developers.line.biz/en/reference/messaging-api/#create-click-audience-group
        /// </summary>
        /// <param name="request">Create click audience request</param>
        /// <returns>Audience group response</returns>
        Task<CreateAudienceGroupResponse> CreateClickAudienceGroupAsync(CreateClickAudienceGroupRequest request);

        /// <summary>
        /// Create audience for impression-based messages.
        /// https://developers.line.biz/en/reference/messaging-api/#create-imp-audience-group
        /// </summary>
        /// <param name="request">Create impression audience request</param>
        /// <returns>Audience group response</returns>
        Task<CreateAudienceGroupResponse> CreateImpAudienceGroupAsync(CreateImpAudienceGroupRequest request);

        /// <summary>
        /// Rename an audience.
        /// https://developers.line.biz/en/reference/messaging-api/#set-description-audience-group
        /// </summary>
        /// <param name="audienceGroupId">Audience group ID</param>
        /// <param name="description">New audience name</param>
        Task UpdateAudienceGroupDescriptionAsync(long audienceGroupId, string description);

        /// <summary>
        /// Delete audience.
        /// https://developers.line.biz/en/reference/messaging-api/#delete-audience-group
        /// </summary>
        /// <param name="audienceGroupId">Audience group ID</param>
        Task DeleteAudienceGroupAsync(long audienceGroupId);

        /// <summary>
        /// Get audience data.
        /// https://developers.line.biz/en/reference/messaging-api/#get-audience-group
        /// </summary>
        /// <param name="audienceGroupId">Audience group ID</param>
        /// <returns>Audience group</returns>
        Task<AudienceGroup> GetAudienceGroupAsync(long audienceGroupId);

        /// <summary>
        /// Get data for multiple audiences.
        /// https://developers.line.biz/en/reference/messaging-api/#get-audience-groups
        /// </summary>
        /// <param name="page">Page number (starting from 1)</param>
        /// <param name="description">Audience name to search (optional)</param>
        /// <param name="status">Status filter (optional): READY, EXPIRED, FAILED</param>
        /// <param name="size">Number per page (default 20, max 40)</param>
        /// <param name="includesExternalPublicGroups">Include shared audiences (default true)</param>
        /// <param name="createRoute">Filter by create route (optional): OA_MANAGER, MESSAGING_API</param>
        /// <returns>List of audience groups</returns>
        Task<AudienceGroupList> GetAudienceGroupsAsync(long page = 1, string description = null, string status = null, long size = 20, bool includesExternalPublicGroups = true, string createRoute = null);

        /// <summary>
        /// Get authority level.
        /// https://developers.line.biz/en/reference/messaging-api/#get-authority-level
        /// </summary>
        /// <returns>Authority level (READ or READ_WRITE)</returns>
        Task<string> GetAudienceGroupAuthorityLevelAsync();

        /// <summary>
        /// Change authority level.
        /// https://developers.line.biz/en/reference/messaging-api/#change-authority-level
        /// </summary>
        /// <param name="authorityLevel">Authority level (READ or READ_WRITE)</param>
        Task ChangeAudienceGroupAuthorityLevelAsync(string authorityLevel);

        #endregion

        #region Insights

        /// <summary>
        /// Get number of message deliveries.
        /// https://developers.line.biz/en/reference/messaging-api/#get-number-of-delivery-messages
        /// </summary>
        /// <param name="date">Date (format: yyyyMMdd, timezone: UTC+9)</param>
        /// <returns>Message delivery statistics</returns>
        Task<MessageDelivery> GetMessageDeliveryAsync(DateTime date);

        /// <summary>
        /// Get number of followers.
        /// https://developers.line.biz/en/reference/messaging-api/#get-number-of-followers
        /// </summary>
        /// <param name="date">Date (format: yyyyMMdd, timezone: UTC+9)</param>
        /// <returns>Follower statistics</returns>
        Task<FollowerStatistics> GetFollowerStatisticsAsync(DateTime date);

        /// <summary>
        /// Get friend demographics.
        /// https://developers.line.biz/en/reference/messaging-api/#get-demographic
        /// </summary>
        /// <returns>Demographic statistics</returns>
        Task<DemographicStatistics> GetFriendDemographicsAsync();

        /// <summary>
        /// Get user interaction statistics.
        /// https://developers.line.biz/en/reference/messaging-api/#get-message-event
        /// </summary>
        /// <param name="requestId">Request ID returned by narrowcast or broadcast</param>
        /// <returns>User interaction statistics</returns>
        Task<UserInteractionStatistics> GetUserInteractionStatisticsAsync(string requestId);

        /// <summary>
        /// Get statistics per unit.
        /// https://developers.line.biz/en/reference/messaging-api/#get-statistics-per-unit
        /// </summary>
        /// <param name="customAggregationUnit">Custom aggregation unit name</param>
        /// <param name="from">Start date (format: yyyyMMdd)</param>
        /// <param name="to">End date (format: yyyyMMdd)</param>
        /// <returns>Statistics per unit</returns>
        Task<StatisticsPerUnit> GetStatisticsPerUnitAsync(string customAggregationUnit, string from, string to);

        /// <summary>
        /// Get aggregation info.
        /// https://developers.line.biz/en/reference/messaging-api/#get-aggregation-info
        /// </summary>
        /// <returns>Aggregation info</returns>
        Task<AggregationInfo> GetAggregationInfoAsync();

        /// <summary>
        /// Get aggregation unit name list.
        /// https://developers.line.biz/en/reference/messaging-api/#get-aggregation-list
        /// </summary>
        /// <param name="limit">Number of units to retrieve (default 100, max 100)</param>
        /// <param name="start">Continuation token</param>
        /// <returns>Aggregation unit name list</returns>
        Task<AggregationUnitNameList> GetAggregationUnitNameListAsync(int limit = 100, string start = null);

        #endregion

        #region Coupon

        /// <summary>
        /// Create a coupon.
        /// https://developers.line.biz/en/reference/messaging-api/#create-coupon
        /// </summary>
        /// <param name="request">Create coupon request</param>
        /// <returns>Coupon object</returns>
        Task<Coupon> CreateCouponAsync(CreateCouponRequest request);

        /// <summary>
        /// Discontinue a coupon.
        /// https://developers.line.biz/en/reference/messaging-api/#close-coupon
        /// </summary>
        /// <param name="couponId">Coupon ID</param>
        Task CloseCouponAsync(string couponId);

        /// <summary>
        /// Get a list of coupons.
        /// https://developers.line.biz/en/reference/messaging-api/#get-coupon-list
        /// </summary>
        /// <param name="limit">Number of coupons to retrieve (default 20, max 100)</param>
        /// <param name="next">Continuation token</param>
        /// <returns>Coupon list</returns>
        Task<CouponList> GetCouponListAsync(int limit = 20, string next = null);

        /// <summary>
        /// Get details of a coupon.
        /// https://developers.line.biz/en/reference/messaging-api/#get-coupon
        /// </summary>
        /// <param name="couponId">Coupon ID</param>
        /// <returns>Coupon object</returns>
        Task<Coupon> GetCouponAsync(string couponId);

        #endregion

        #region Membership

        /// <summary>
        /// Get a user's membership subscription status.
        /// https://developers.line.biz/en/reference/messaging-api/#get-membership-subscription
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <returns>Membership subscription</returns>
        Task<MembershipSubscription> GetMembershipSubscriptionAsync(string userId);

        /// <summary>
        /// Get a list of users who have joined the membership.
        /// https://developers.line.biz/en/reference/messaging-api/#get-membership-users
        /// </summary>
        /// <param name="membershipId">Membership ID</param>
        /// <param name="limit">Number of users to retrieve (default 100, max 100)</param>
        /// <param name="next">Continuation token</param>
        /// <returns>Membership user IDs</returns>
        Task<MembershipUserIds> GetMembershipUserIdsAsync(string membershipId, int limit = 100, string next = null);

        /// <summary>
        /// Get membership plans being offered.
        /// https://developers.line.biz/en/reference/messaging-api/#get-membership-list
        /// </summary>
        /// <returns>Membership plan list</returns>
        Task<MembershipPlanList> GetMembershipPlansAsync();

        #endregion
    }
}

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq; // ...added for rich menu list parsing

namespace Line.Messaging
{
    /// <summary>
    /// LINE Messaging API 客戶端，處理與 LINE 伺服器的請求和回應
    /// LINE Messaging API client, which handles request/response to LINE server.
    /// </summary>
    /// <remarks>
    /// 此類別提供完整的 LINE Messaging API 功能，包括：
    /// - 訊息傳送（回覆、推播、多播、廣播）
    /// - 使用者資料管理
    /// - 群組與聊天室管理
    /// - Rich Menu 管理
    /// - Webhook 設定
    /// - 訊息配額查詢
    /// <para>
    /// This class provides complete LINE Messaging API functionality including:
    /// - Message sending (reply, push, multicast, broadcast)
    /// - User profile management
    /// - Group and room management
    /// - Rich Menu management
    /// - Webhook configuration
    /// - Message quota inquiry
    /// </para>
    /// <para>
    /// 官方文件：https://developers.line.biz/en/reference/messaging-api/
    /// Official documentation: https://developers.line.biz/en/reference/messaging-api/
    /// </para>
    /// </remarks>
    /// <example>
    /// 基本使用範例：
    /// <code>
    /// // 建立客戶端
    /// var client = new LineMessagingClient("YOUR_CHANNEL_ACCESS_TOKEN");
    /// 
    /// // 回覆訊息
    /// await client.ReplyMessageAsync(replyToken, new TextMessage("Hello!"));
    /// 
    /// // 推播訊息
    /// await client.PushMessageAsync(userId, new TextMessage("Hi!"));
    /// 
    /// // 取得使用者資料
    /// var profile = await client.GetUserProfileAsync(userId);
    /// </code>
    /// </example>
    /// <seealso cref="ILineMessagingClient"/>
    public class LineMessagingClient : ILineMessagingClient, IDisposable
    {
        /// <summary>
        /// LINE API 預設 URI
        /// Default LINE API URI
        /// </summary>
        private const string DEFAULT_URI = "https://api.line.me/v2";

        /// <summary>
        /// LINE binary data API 預設 URI。
        /// Content 與 Rich Menu 圖檔端點官方要求使用 api-data.line.me，不可共用一般 JSON API host。
        /// </summary>
        private const string DEFAULT_DATA_URI = "https://api-data.line.me/v2";

        /// <summary>
        /// HTTP 客戶端，用於發送 API 請求
        /// HTTP client for sending API requests
        /// </summary>
        private readonly HttpClient _client;

        /// <summary>
        /// 是否由此類別負責釋放 HttpClient
        /// Whether this class is responsible for disposing HttpClient
        /// </summary>
        private readonly bool _disposeClient;

        /// <summary>
        /// JSON 序列化設定（使用 Camel Case 命名）
        /// JSON serializer settings (using Camel Case naming)
        /// </summary>
        private JsonSerializerSettings _jsonSerializerSettings;

        /// <summary>
        /// LINE API 的基礎 URI
        /// Base URI for LINE API
        /// </summary>
        private string _uri;

        /// <summary>
        /// LINE binary data API 的基礎 URI。
        /// </summary>
        private string _dataUri;

        /// <summary>
        /// 初始化 LineMessagingClient - 使用外部提供的 HttpClient（建議用於 DI 情境）
        /// Initializes LineMessagingClient using externally provided HttpClient (recommended for DI scenarios)
        /// </summary>
        /// <param name="httpClient">外部提供的 HttpClient 實例（不會被 Dispose）</param>
        /// <param name="channelAccessToken">Channel Access Token</param>
        /// <param name="uri">LINE API 的基礎 URI（選用）</param>
        public LineMessagingClient(HttpClient httpClient, string channelAccessToken, string uri = DEFAULT_URI)
        {
            _client = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _disposeClient = false;
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", channelAccessToken);
            _jsonSerializerSettings = new CamelCaseJsonSerializerSettings();
            _uri = NormalizeLineApiBaseUri(uri);
            _dataUri = DeriveDataUri(_uri);
        }

        /// <summary>
        /// 初始化 LineMessagingClient - 建立內部 HttpClient（向後相容，但不建議用於生產環境）
        /// Initializes LineMessagingClient with internal HttpClient (backward compatible, not recommended for production)
        /// </summary>
        /// <param name="channelAccessToken">Channel Access Token</param>
        /// <param name="uri">LINE API 的基礎 URI（選用）</param>
        [Obsolete("建議使用接受 HttpClient 參數的建構函式，以避免 Socket 耗盡問題。Use constructor with HttpClient parameter to avoid socket exhaustion.")]
        public LineMessagingClient(string channelAccessToken, string uri = DEFAULT_URI)
        {
            _client = new HttpClient();
            _disposeClient = true;
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", channelAccessToken);
            _jsonSerializerSettings = new CamelCaseJsonSerializerSettings();
            _uri = NormalizeLineApiBaseUri(uri);
            _dataUri = DeriveDataUri(_uri);
        }

        private static string NormalizeLineApiBaseUri(string apiUri)
        {
            if (string.IsNullOrWhiteSpace(apiUri))
            {
                return DEFAULT_URI;
            }

            var normalizedUri = apiUri.TrimEnd('/');
            return normalizedUri.EndsWith("/v2", StringComparison.OrdinalIgnoreCase)
                ? normalizedUri
                : normalizedUri + "/v2";
        }

        private static string DeriveDataUri(string apiUri)
        {
            if (string.IsNullOrWhiteSpace(apiUri))
            {
                return DEFAULT_DATA_URI;
            }

            return apiUri.Replace("https://api.line.me", "https://api-data.line.me");
        }

        private string ApiUrl(string path)
        {
            return CombineBaseAndPath(_uri, path);
        }

        private string DataUrl(string path)
        {
            return CombineBaseAndPath(_dataUri, path);
        }

        private static string CombineBaseAndPath(string baseUri, string path)
        {
            if (string.IsNullOrWhiteSpace(baseUri))
            {
                throw new ArgumentException("Base URI is required.", nameof(baseUri));
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path is required.", nameof(path));
            }

            var normalizedBase = baseUri.TrimEnd('/');
            var normalizedPath = path.TrimStart('/');

            return normalizedBase + "/" + normalizedPath;
        }

        #region OAuth
        /// <summary>
        /// 發行 Channel Access Token（靜態方法）
        /// Issues a Channel Access Token (static method)
        /// </summary>
        /// <param name="httpClient">
        /// HTTP 客戶端實例
        /// HTTP client instance
        /// </param>
        /// <param name="channelId">
        /// Channel ID（從 LINE Developers Console 取得）
        /// Channel ID (from LINE Developers Console)
        /// </param>
        /// <param name="channelAccessToken">
        /// Channel Secret（從 LINE Developers Console 取得）
        /// Channel Secret (from LINE Developers Console)
        /// </param>
        /// <param name="uri">
        /// LINE API 的基礎 URI（選用）
        /// Base URI for LINE API (optional)
        /// </param>
        /// <returns>
        /// Channel Access Token 物件，包含 access token 和過期時間
        /// Channel Access Token object containing access token and expiration time
        /// </returns>
        /// <remarks>
        /// 此方法使用 OAuth 2.0 Client Credentials 流程來取得 Channel Access Token。
        /// Token 有效期為 30 天（v2.1）或作為無狀態 token（v3）。
        /// <para>
        /// This method uses OAuth 2.0 Client Credentials flow to obtain Channel Access Token.
        /// Token is valid for 30 days (v2.1) or as stateless token (v3).
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#issue-channel-access-token
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#issue-channel-access-token
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// using (var httpClient = new HttpClient())
        /// {
        ///     var token = await LineMessagingClient.IssueChannelAccessTokenAsync(
        ///         httpClient,
        ///         "YOUR_CHANNEL_ID",
        ///         "YOUR_CHANNEL_SECRET"
        ///     );
        ///     Console.WriteLine($"Access Token: {token.AccessToken}");
        ///     Console.WriteLine($"Expires In: {token.ExpiresIn} seconds");
        /// }
        /// </code>
        /// </example>
        public static async Task<ChannelAccessToken> IssueChannelAccessTokenAsync(HttpClient httpClient, string channelId, string channelAccessToken, string uri = DEFAULT_URI)
        {
            var response = await httpClient.PostAsync($"{uri}/oauth/accessToken",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = channelId,
                    ["client_secret"] = channelAccessToken
                })).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonConvert.DeserializeObject<ChannelAccessToken>(json,
                new JsonSerializerSettings
                {
                    ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() }
                });
        }

        /// <summary>
        /// 撤銷 Channel Access Token（靜態方法）
        /// Revokes a Channel Access Token (static method)
        /// </summary>
        /// <param name="httpClient">
        /// HTTP 客戶端實例
        /// HTTP client instance
        /// </param>
        /// <param name="channelAccessToken">
        /// 要撤銷的 Channel Access Token
        /// Channel Access Token to revoke
        /// </param>
        /// <param name="uri">
        /// LINE API 的基礎 URI（選用）
        /// Base URI for LINE API (optional)
        /// </param>
        /// <remarks>
        /// 撤銷後，該 Access Token 將立即失效，無法再用於 API 呼叫。
        /// <para>
        /// After revocation, the Access Token becomes immediately invalid and cannot be used for API calls.
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#revoke-channel-access-token
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#revoke-channel-access-token
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// using (var httpClient = new HttpClient())
        /// {
        ///     await LineMessagingClient.RevokeChannelAccessTokenAsync(
        ///         httpClient,
        ///         "YOUR_ACCESS_TOKEN_TO_REVOKE"
        ///     );
        ///     Console.WriteLine("Access Token has been revoked.");
        /// }
        /// </code>
        /// </example>
        public static async Task RevokeChannelAccessTokenAsync(HttpClient httpClient, string channelAccessToken, string uri = DEFAULT_URI)
        {
            var response = await httpClient.PostAsync($"{uri}/oauth/revoke",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["access_token"] = channelAccessToken })).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 建立 LineMessagingClient 實例（使用 Channel ID 和 Secret 自動取得 Access Token）
        /// Creates a LineMessagingClient instance (automatically obtains Access Token using Channel ID and Secret)
        /// </summary>
        /// <param name="channelId">
        /// Channel ID（從 LINE Developers Console 取得）
        /// Channel ID (from LINE Developers Console)
        /// </param>
        /// <param name="channelSecret">
        /// Channel Secret（從 LINE Developers Console 取得）
        /// Channel Secret (from LINE Developers Console)
        /// </param>
        /// <param name="uri">
        /// LINE API 的基礎 URI（選用）
        /// Base URI for LINE API (optional)
        /// </param>
        /// <returns>
        /// 已設定好的 LineMessagingClient 實例
        /// Configured LineMessagingClient instance
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// 當 channelId 或 channelSecret 為 null 或空字串時拋出
        /// Thrown when channelId or channelSecret is null or empty
        /// </exception>
        /// <remarks>
        /// 此方法會自動執行以下步驟：
        /// 1. 使用 Channel ID 和 Secret 取得 Access Token
        /// 2. 建立並回傳 LineMessagingClient 實例
        /// <para>
        /// This method automatically performs the following steps:
        /// 1. Obtains Access Token using Channel ID and Secret
        /// 2. Creates and returns LineMessagingClient instance
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 自動取得 Access Token 並建立客戶端
        /// var client = await LineMessagingClient.CreateAsync(
        ///     "YOUR_CHANNEL_ID",
        ///     "YOUR_CHANNEL_SECRET"
        /// );
        /// 
        /// // 立即使用客戶端
        /// await client.PushMessageAsync(userId, new TextMessage("Hello!"));
        /// </code>
        /// </example>
        public static async Task<LineMessagingClient> CreateAsync(HttpClient httpClient, string channelId, string channelSecret, string uri = DEFAULT_URI)
        {
            if (httpClient == null) throw new ArgumentNullException(nameof(httpClient));
            if (string.IsNullOrEmpty(channelId)) throw new ArgumentNullException(nameof(channelId));
            if (string.IsNullOrEmpty(channelSecret)) throw new ArgumentNullException(nameof(channelSecret));
            
            var accessToken = await IssueChannelAccessTokenAsync(httpClient, channelId, channelSecret, uri).ConfigureAwait(false);
            return new LineMessagingClient(httpClient, accessToken.AccessToken, uri);
        }

        /// <summary>
        /// 建立 LineMessagingClient 實例（向後相容版本，不建議用於生產環境）
        /// </summary>
        [Obsolete("建議使用接受 HttpClient 參數的 CreateAsync 方法，以避免 Socket 耗盡問題")]
        public static async Task<LineMessagingClient> CreateAsync(string channelId, string channelSecret, string uri = DEFAULT_URI)
        {
            if (string.IsNullOrEmpty(channelId)) throw new ArgumentNullException(nameof(channelId));
            if (string.IsNullOrEmpty(channelSecret)) throw new ArgumentNullException(nameof(channelSecret));
            using (var client = new HttpClient())
            {
                var accessToken = await IssueChannelAccessTokenAsync(client, channelId, channelSecret, uri).ConfigureAwait(false);
#pragma warning disable CS0618 // 使用過時的建構函式（向後相容）
                return new LineMessagingClient(accessToken.AccessToken, uri);
#pragma warning restore CS0618
            }
        }
        #endregion

        #region Message
        /// <summary>
        /// 回覆訊息給使用者、群組或聊天室
        /// Responds to events from users, groups, and rooms
        /// </summary>
        /// <param name="replyToken">
        /// Reply Token，從 Webhook 事件中取得（有效期 30 秒，只能使用一次）
        /// Reply Token obtained from webhook event (valid for 30 seconds, can only be used once)
        /// </param>
        /// <param name="messages">
        /// 要回覆的訊息清單（最多 5 則）
        /// List of messages to reply (maximum 5 messages)
        /// </param>
        /// <remarks>
        /// 注意事項：
        /// - Reply Token 只能使用一次，且必須在收到 Webhook 後 30 秒內使用
        /// - 最多可以回覆 5 則訊息
        /// - 超過時間或重複使用會導致 API 錯誤
        /// <para>
        /// Important notes:
        /// - Reply Token can only be used once and must be used within 30 seconds after receiving webhook
        /// - Maximum 5 messages can be sent
        /// - Timeout or duplicate use will cause API error
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#send-reply-message
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#send-reply-message
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 回覆單一文字訊息
        /// await client.ReplyMessageAsync(replyToken, new List&lt;ISendMessage&gt;
        /// {
        ///     new TextMessage("感謝您的訊息！")
        /// });
        /// 
        /// // 回覆多則訊息（文字 + 圖片）
        /// await client.ReplyMessageAsync(replyToken, new List&lt;ISendMessage&gt;
        /// {
        ///     new TextMessage("這是一張照片："),
        ///     new ImageMessage("https://example.com/image.jpg", "https://example.com/thumb.jpg")
        /// });
        /// </code>
        /// </example>
        /// <seealso cref="PushMessageAsync(string, IList{ISendMessage})"/>
        /// <seealso cref="MultiCastMessageAsync(IList{string}, IList{ISendMessage})"/>
        public virtual async Task ReplyMessageAsync(string replyToken, IList<ISendMessage> messages)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/reply");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { replyToken, messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            var response = await _client.SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 回覆文字訊息給使用者、群組或聊天室（簡化版）
        /// Responds with text messages to events from users, groups, and rooms (simplified version)
        /// </summary>
        /// <param name="replyToken">
        /// Reply Token，從 Webhook 事件中取得
        /// Reply Token obtained from webhook event
        /// </param>
        /// <param name="messages">
        /// 要回覆的文字訊息陣列（最多 5 則，每則最多 2000 字元）
        /// Array of text messages to reply (maximum 5 messages, each up to 2000 characters)
        /// </param>
        /// <remarks>
        /// 這是 ReplyMessageAsync 的簡化版本，自動將文字轉換為 TextMessage 物件。
        /// <para>
        /// This is a simplified version of ReplyMessageAsync that automatically converts text to TextMessage objects.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 回覆單一文字訊息
        /// await client.ReplyMessageAsync(replyToken, "謝謝！");
        /// 
        /// // 回覆多則文字訊息
        /// await client.ReplyMessageAsync(replyToken, "第一則訊息", "第二則訊息", "第三則訊息");
        /// </code>
        /// </example>
        public virtual Task ReplyMessageAsync(string replyToken, params string[] messages) 
            => ReplyMessageAsync(replyToken, messages.Select(m => new TextMessage(m)).ToArray());

        /// <summary>
        /// 使用 JSON 字串回覆訊息
        /// Replies with messages using JSON strings
        /// </summary>
        /// <param name="replyToken">
        /// Reply Token，從 Webhook 事件中取得
        /// Reply Token obtained from webhook event
        /// </param>
        /// <param name="messages">
        /// JSON 格式的訊息陣列（每個元素為完整的訊息 JSON 字串）
        /// Array of messages in JSON format (each element is a complete message JSON string)
        /// </param>
        /// <remarks>
        /// 此方法適合於需要完全控制 JSON 格式的進階情境。
        /// 一般情況建議使用 ReplyMessageAsync(string, IList&lt;ISendMessage&gt;)。
        /// <para>
        /// This method is suitable for advanced scenarios requiring full control of JSON format.
        /// For general use, ReplyMessageAsync(string, IList&lt;ISendMessage&gt;) is recommended.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// await client.ReplyMessageWithJsonAsync(
        ///     replyToken,
        ///     "{\"type\":\"text\",\"text\":\"Hello\"}",
        ///     "{\"type\":\"sticker\",\"packageId\":\"1\",\"stickerId\":\"1\"}"
        /// );
        /// </code>
        /// </example>
        public virtual async Task ReplyMessageWithJsonAsync(string replyToken, params string[] messages)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/reply");
            var json = $"{{ \"replyToken\" : \"{replyToken}\", \"messages\" : [{string.Join(", ", messages)}] }}";
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _client.SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 推播訊息給使用者、群組或聊天室
        /// Sends push messages to a user, group, or room at any time
        /// </summary>
        /// <param name="to">
        /// 接收者的 ID（userId、groupId 或 roomId）
        /// ID of the receiver (userId, groupId, or roomId)
        /// </param>
        /// <param name="messages">
        /// 要傳送的訊息清單（最多 5 則）
        /// List of messages to send (maximum 5 messages)
        /// </param>
        /// <remarks>
        /// 推播訊息的限制：
        /// - 僅特定方案支援推播訊息功能
        /// - 需消耗訊息配額
        /// - 可隨時傳送，不需要 Reply Token
        /// - 最多可傳送 5 則訊息
        /// <para>
        /// Push message limitations:
        /// - Only available for certain plans
        /// - Consumes message quota
        /// - Can be sent at any time without Reply Token
        /// - Maximum 5 messages can be sent
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#send-push-message
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#send-push-message
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 推播給特定使用者
        /// await client.PushMessageAsync(userId, new List&lt;ISendMessage&gt;
        /// {
        ///     new TextMessage("這是一則推播訊息！")
        /// });
        /// 
        /// // 推播給群組
        /// await client.PushMessageAsync(groupId, new List&lt;ISendMessage&gt;
        /// {
        ///     new TextMessage("群組公告"),
        ///     new ImageMessage("https://example.com/notice.jpg", "https://example.com/notice_thumb.jpg")
        /// });
        /// </code>
        /// </example>
        /// <seealso cref="ReplyMessageAsync(string, IList{ISendMessage})"/>
        /// <seealso cref="MultiCastMessageAsync(IList{string}, IList{ISendMessage})"/>
        public virtual async Task PushMessageAsync(string to, IList<ISendMessage> messages)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/push");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { to, messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            var response = await _client.SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 使用 JSON 字串推播訊息
        /// Sends push messages using JSON strings
        /// </summary>
        /// <param name="to">
        /// 接收者的 ID
        /// ID of the receiver
        /// </param>
        /// <param name="messages">
        /// JSON 格式的訊息陣列
        /// Array of messages in JSON format
        /// </param>
        /// <remarks>
        /// 此方法適用於需要完全控制 JSON 格式的進階情境。
        /// <para>
        /// This method is suitable for advanced scenarios requiring full control of JSON format.
        /// </para>
        /// </remarks>
        public virtual async Task PushMessageWithJsonAsync(string to, params string[] messages)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/push");
            var json = $"{{ \"to\" : \"{to}\", \"messages\" : [{string.Join(", ", messages)}] }}";
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _client.SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 推播文字訊息（簡化版）
        /// Sends push text messages (simplified version)
        /// </summary>
        /// <param name="to">
        /// 接收者的 ID
        /// ID of the receiver
        /// </param>
        /// <param name="messages">
        /// 要推播的文字訊息陣列（最多 5 則）
        /// Array of text messages to send (maximum 5 messages)
        /// </param>
        /// <example>
        /// <code>
        /// await client.PushMessageAsync(userId, "通知訊息", "第二則訊息");
        /// </code>
        /// </example>
        public virtual Task PushMessageAsync(string to, params string[] messages) 
            => PushMessageAsync(to, messages.Select(m => new TextMessage(m)).ToArray());

        /// <summary>
        /// 多播訊息給多位使用者
        /// Sends push messages to multiple users at any time
        /// </summary>
        /// <param name="to">
        /// 接收者的 ID 清單（最多 500 位使用者）
        /// List of receiver IDs (maximum 500 users)
        /// </param>
        /// <param name="messages">
        /// 要傳送的訊息清單（最多 5 則）
        /// List of messages to send (maximum 5 messages)
        /// </param>
        /// <remarks>
        /// 多播訊息的限制：
        /// - 僅支援推播訊息的方案可用
        /// - 無法傳送至群組或聊天室
        /// - 最多 500 位接收者
        /// - 最多 5 則訊息
        /// <para>
        /// Multicast message limitations:
        /// - Only available for plans supporting push messages
        /// - Cannot be sent to groups or rooms
        /// - Maximum 500 receivers
        /// - Maximum 5 messages
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#send-multicast-message
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#send-multicast-message
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var userIds = new List&lt;string&gt; { "U1234567890", "U0987654321", "U1111111111" };
        /// await client.MultiCastMessageAsync(userIds, new List&lt;ISendMessage&gt;
        /// {
        ///     new TextMessage("群發訊息給多位使用者")
        /// });
        /// </code>
        /// </example>
        public virtual async Task MultiCastMessageAsync(IList<string> to, IList<ISendMessage> messages)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/multicast");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { to, messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            var response = await _client.SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 使用 JSON 字串多播訊息
        /// Sends multicast messages using JSON strings
        /// </summary>
        /// <param name="to">
        /// 接收者的 ID 清單
        /// List of receiver IDs
        /// </param>
        /// <param name="messages">
        /// JSON 格式的訊息陣列
        /// Array of messages in JSON format
        /// </param>
        public virtual async Task MultiCastMessageWithJsonAsync(IList<string> to, params string[] messages)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/multicast");
            var quotedIds = string.Join(", ", to.Select(x => $"\"{x}\""));
            var json = $"{{ \"to\" : [{quotedIds}], \"messages\" : [{string.Join(", ", messages)}] }}";
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _client.SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 多播文字訊息（簡化版）
        /// Sends multicast text messages (simplified version)
        /// </summary>
        /// <param name="to">
        /// 接收者的 ID 清單
        /// List of receiver IDs
        /// </param>
        /// <param name="messages">
        /// 要多播的文字訊息陣列
        /// Array of text messages to multicast
        /// </param>
        public virtual Task MultiCastMessageAsync(IList<string> to, params string[] messages) 
            => MultiCastMessageAsync(to, messages.Select(m => new TextMessage(m)).ToArray());

        /// <summary>
        /// 廣播訊息給所有好友
        /// Broadcasts push messages to all friends
        /// </summary>
        /// <param name="messages">
        /// 要廣播的訊息清單（最多 5 則）
        /// List of messages to broadcast (maximum 5 messages)
        /// </param>
        /// <remarks>
        /// 廣播訊息會傳送給所有加入機器人為好友的使用者（包含群組和聊天室）。
        /// 注意：會消耗大量訊息配額，請謹慎使用。
        /// <para>
        /// Broadcast messages are sent to all users who have added the bot as a friend (including groups and rooms).
        /// Note: Consumes large message quota, use with caution.
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#send-broadcast-message
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#send-broadcast-message
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 廣播重要公告
        /// await client.BroadcastMessageAsync(new List&lt;ISendMessage&gt;
        /// {
        ///     new TextMessage("重要公告：系統將於今晚維護")
        /// });
        /// </code>
        /// </example>
        public virtual async Task BroadcastMessageAsync(IList<ISendMessage> messages)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/broadcast");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            var response = await _client.SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 窄播訊息給特定條件的使用者
        /// Sends narrowcast messages to users matching specific criteria
        /// </summary>
        /// <param name="messages">
        /// 要傳送的訊息清單（最多 5 則）
        /// List of messages to send (maximum 5 messages)
        /// </param>
        /// <param name="recipient">
        /// 接收者物件（選用）- 使用篩選器或受眾 ID 指定接收者（最多 10 個接收者物件）
        /// Recipient object (optional) - Specify recipients using filters or audience IDs (maximum 10 recipient objects)
        /// </param>
        /// <param name="filter">
        /// 篩選器物件（選用）- 人口統計篩選器物件，可使用 LINE 官方帳號的好友資料
        /// Filter object (optional) - Demographic filter object using LINE Official Account friends data
        /// </param>
        /// <param name="limit">
        /// 限制物件（選用）- 窄播訊息的最大傳送數量，用於限制接收訊息的人數
        /// Limit object (optional) - Maximum number of narrowcast messages to send
        /// </param>
        /// <returns>
        /// Request ID - 用於查詢窄播進度
        /// Request ID - Used to query narrowcast progress
        /// </returns>
        /// <remarks>
        /// 窄播訊息可根據以下條件篩選接收者：
        /// - 性別、年齡、作業系統、地區
        /// - 加好友的時間長度
        /// - 重新定向受眾（audiences）
        /// <para>
        /// Narrowcast messages can filter recipients based on:
        /// - Gender, age, OS, region
        /// - Friendship duration
        /// - Retargeting audiences
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#send-narrowcast-message
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#send-narrowcast-message
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 傳送給特定受眾
        /// var requestId = await client.NarrowcastMessageAsync(
        ///     new List&lt;ISendMessage&gt; { new TextMessage("窄播訊息") },
        ///     recipient: new { type = "audience", audienceGroupId = "12345" }
        /// );
        /// 
        /// // 查詢傳送進度
        /// var progress = await client.GetNarrowcastProgressAsync(requestId);
        /// </code>
        /// </example>
        public virtual async Task<string> NarrowcastMessageAsync(IList<ISendMessage> messages, object recipient = null, object filter = null, object limit = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/narrowcast");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { messages, recipient, filter, limit }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            var response = await _client.SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
            if (response.Headers.TryGetValues("X-Line-Request-Id", out var values)) return values.FirstOrDefault();
            return null;
        }

        /// <summary>
        /// 取得窄播訊息的傳送進度
        /// Gets the status of narrowcast message sending
        /// </summary>
        /// <param name="requestId">
        /// 窄播訊息傳送時回傳的 Request ID
        /// Request ID returned when sending narrowcast message
        /// </param>
        /// <returns>
        /// 窄播進度資訊，包含成功、失敗數量等
        /// Narrowcast progress information including success and failure counts
        /// </returns>
        /// <remarks>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-narrowcast-progress-status
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-narrowcast-progress-status
        /// </remarks>
        /// <example>
        /// <code>
        /// var progress = await client.GetNarrowcastProgressAsync(requestId);
        /// Console.WriteLine($"狀態: {progress.Phase}");
        /// Console.WriteLine($"成功: {progress.SuccessCount}");
        /// Console.WriteLine($"失敗: {progress.FailureCount}");
        /// </code>
        /// </example>
        public virtual async Task<NarrowcastProgress> GetNarrowcastProgressAsync(string requestId)
        {
            var json = await GetStringAsync($"{_uri}/bot/message/progress/narrowcast?requestId={requestId}").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<NarrowcastProgress>(json);
        }

        /// <summary>
        /// 將來自使用者的訊息標記為已讀
        /// Marks messages from users as read
        /// </summary>
        /// <param name="chatId">
        /// 聊天識別碼 - 可以是 userId（一對一聊天）、groupId（群組）或 roomId（聊天室）
        /// Chat identifier - Can be userId (one-on-one chat), groupId (group), or roomId (room)
        /// </param>
        /// <remarks>
        /// 標記為已讀後，使用者端會顯示訊息已讀取。
        /// <para>
        /// After marking as read, the user will see the message as read.
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#mark-messages-as-read
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#mark-messages-as-read
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 標記一對一聊天為已讀
        /// await client.MarkAsReadAsync(userId);
        /// 
        /// // 標記群組訊息為已讀
        /// await client.MarkAsReadAsync(groupId);
        /// </code>
        /// </example>
        public virtual async Task MarkAsReadByTokenAsync(string markAsReadToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl("/bot/chat/markAsRead"));
            request.Content = new StringContent(
                JsonConvert.SerializeObject(new { markAsReadToken }, _jsonSerializerSettings),
                Encoding.UTF8,
                "application/json");
            var response = await _client.SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        [Obsolete("Use MarkAsReadByTokenAsync(markAsReadToken). LINE official API uses markAsReadToken, not chatId.")]
        public virtual Task MarkAsReadAsync(string chatId)
        {
            throw new NotSupportedException(
                "LINE mark-as-read now requires markAsReadToken from webhook events. Use MarkAsReadByTokenAsync(markAsReadToken) instead of passing a chatId.");
        }

        /// <summary>
        /// 在聊天畫面顯示載入動畫
        /// Displays a loading animation on the chat screen
        /// </summary>
        /// <param name="chatId">
        /// 聊天識別碼 - 可以是 userId、groupId 或 roomId
        /// Chat identifier - Can be userId, groupId, or roomId
        /// </param>
        /// <param name="loadingSeconds">
        /// 顯示載入動畫的秒數（最多 60 秒，預設 20 秒）
        /// Number of seconds to display loading animation (maximum 60 seconds, default 20 seconds)
        /// </param>
        /// <remarks>
        /// 載入動畫適用於需要較長處理時間的情境，讓使用者知道機器人正在處理中。
        /// <para>
        /// Loading animation is suitable for scenarios requiring longer processing time, letting users know the bot is working.
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#display-a-loading-animation
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#display-a-loading-animation
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 顯示預設 20 秒的載入動畫
        /// await client.ShowLoadingAnimationAsync(userId);
        /// 
        /// // 顯示 30 秒的載入動畫
        /// await client.ShowLoadingAnimationAsync(userId, 30);
        /// </code>
        /// </example>
        public virtual async Task ShowLoadingAnimationAsync(string chatId, int loadingSeconds = 20)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/chat/loading/start");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { chatId, loadingSeconds }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            var response = await _client.SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 取得使用者傳送的圖片、影片、音訊等內容（以串流形式）
        /// Retrieves image, video, and audio data sent by users as a stream
        /// </summary>
        /// <param name="messageId">
        /// 訊息 ID（從 Webhook 事件中取得）
        /// Message ID (obtained from webhook event)
        /// </param>
        /// <returns>
        /// ContentStream 物件，包含內容資料和 HTTP 標頭
        /// ContentStream object containing content data and HTTP headers
        /// </returns>
        /// <remarks>
        /// 支援的內容類型：
        /// - 圖片（JPEG、PNG）
        /// - 影片（MP4）
        /// - 音訊（M4A）
        /// - 檔案
        /// <para>
        /// Supported content types:
        /// - Images (JPEG, PNG)
        /// - Videos (MP4)
        /// - Audio (M4A)
        /// - Files
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-content
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-content
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 下載使用者傳送的圖片
        /// using (var stream = await client.GetContentStreamAsync(messageId))
        /// {
        ///     var contentType = stream.ContentHeaders.ContentType.MediaType;
        ///     Console.WriteLine($"Content Type: {contentType}");
        ///     
        ///     // 儲存到檔案
        ///     using (var fileStream = File.Create("downloaded_image.jpg"))
        ///     {
        ///         await stream.CopyToAsync(fileStream);
        ///     }
        /// }
        /// </code>
        /// </example>
        /// <seealso cref="GetContentBytesAsync(string)"/>
        public virtual async Task<ContentStream> GetContentStreamAsync(string messageId)
        {
            var response = await _client.GetAsync(DataUrl($"/bot/message/{messageId}/content")).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
            return new ContentStream(await response.Content.ReadAsStreamAsync(), response.Content.Headers);
        }

        /// <summary>
        /// 取得使用者傳送的圖片、影片、音訊等內容（以位元組陣列形式）
        /// Retrieves image, video, and audio data sent by users as a byte array
        /// </summary>
        /// <param name="messageId">
        /// 訊息 ID（從 Webhook 事件中取得）
        /// Message ID (obtained from webhook event)
        /// </param>
        /// <returns>
        /// 內容的位元組陣列
        /// Byte array of the content
        /// </returns>
        /// <remarks>
        /// 此方法適合較小的檔案。對於大型檔案，建議使用 GetContentStreamAsync。
        /// <para>
        /// This method is suitable for smaller files. For large files, GetContentStreamAsync is recommended.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 下載為位元組陣列
        /// var bytes = await client.GetContentBytesAsync(messageId);
        /// File.WriteAllBytes("downloaded_image.jpg", bytes);
        /// </code>
        /// </example>
        /// <seealso cref="GetContentStreamAsync(string)"/>
        public virtual async Task<byte[]> GetContentBytesAsync(string messageId)
        {
            var response = await _client.GetAsync(DataUrl($"/bot/message/{messageId}/content")).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 驗證影片或音訊內容的準備狀態
        /// Verifies the preparation status of video or audio content
        /// </summary>
        /// <param name="messageId">
        /// 訊息 ID
        /// Message ID
        /// </param>
        /// <returns>
        /// true 表示內容已就緒可下載；false 表示仍在處理中
        /// true if content is ready for download; false if still processing
        /// </returns>
        /// <remarks>
        /// 使用者上傳的影片或音訊可能需要一些時間進行處理。
        /// 在下載前先驗證可避免取得未完成處理的內容。
        /// <para>
        /// Videos or audio uploaded by users may require processing time.
        /// Verification before download prevents getting unprocessed content.
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#verify-the-preparation-status
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#verify-the-preparation-status
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 等待內容準備完成
        /// while (!await client.VerifyContentPreparationAsync(messageId))
        /// {
        ///     await Task.Delay(1000); // 等待 1 秒後重試
        /// }
        /// 
        /// // 內容已就緒，可以下載
        /// var content = await client.GetContentStreamAsync(messageId);
        /// </code>
        /// </example>
        public virtual async Task<bool> VerifyContentPreparationAsync(string messageId)
        {
            var response = await _client.GetAsync(DataUrl($"/bot/message/{messageId}/content/transcoding")).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var anon = new { available = (bool?)null, status = "" };
                    try
                    {
                        var info = JsonConvert.DeserializeAnonymousType(json, anon);
                        if (info != null)
                        {
                            if (info.available.HasValue) return info.available.Value;

                            // LINE 官方 /content/transcoding 回傳的狀態只有 processing / succeeded / failed。
                            // 這裡只把 succeeded 視為可下載，其他狀態維持 false，讓呼叫端可以繼續等待或自行處理失敗狀態。
                            if (!string.IsNullOrEmpty(info.status)) return string.Equals(info.status, "succeeded", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                    catch { }
                }
                return true;
            }
            if (response.StatusCode == System.Net.HttpStatusCode.Accepted) return false;
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
            return false;
        }

        /// <summary>
        /// 取得圖片或影片的預覽圖
        /// Gets a preview image of an image or video
        /// </summary>
        /// <param name="messageId">
        /// 訊息 ID
        /// Message ID
        /// </param>
        /// <returns>
        /// 預覽圖的 ContentStream
        /// ContentStream of the preview image
        /// </returns>
        /// <remarks>
        /// 預覽圖通常是較小的縮圖，適合用於顯示預覽。
        /// <para>
        /// Preview images are usually smaller thumbnails suitable for display previews.
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-a-preview-image-of-the-image-or-video
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-a-preview-image-of-the-image-or-video
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 取得預覽圖
        /// using (var preview = await client.GetContentPreviewAsync(messageId))
        /// {
        ///     using (var fileStream = File.Create("preview.jpg"))
        ///     {
        ///         await preview.CopyToAsync(fileStream);
        ///     }
        /// }
        /// </code>
        /// </example>
        public virtual async Task<ContentStream> GetContentPreviewAsync(string messageId)
        {
            var response = await _client.GetAsync(DataUrl($"/bot/message/{messageId}/content/preview")).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
            return new ContentStream(await response.Content.ReadAsStreamAsync(), response.Content.Headers);
        }
        #endregion

        #region Profile / Bot
        /// <summary>
        /// 取得使用者的個人資料
        /// Gets user profile information
        /// </summary>
        /// <param name="userId">
        /// 使用者 ID（從 Webhook 事件中取得，不要使用 LINE app 中的 LINE ID）
        /// User ID (obtained from webhook event, do not use LINE ID from LINE app)
        /// </param>
        /// <returns>
        /// UserProfile 物件，包含顯示名稱、圖片 URL、狀態訊息等
        /// UserProfile object containing display name, picture URL, status message, etc.
        /// </returns>
        /// <remarks>
        /// 可取得的個人資料包括：
        /// - displayName：顯示名稱
        /// - pictureUrl：大頭貼圖片 URL
        /// - statusMessage：狀態訊息
        /// - language：語言設定
        /// <para>
        /// Available profile information includes:
        /// - displayName: Display name
        /// - pictureUrl: Profile picture URL
        /// - statusMessage: Status message
        /// - language: Language settings
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-profile
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-profile
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var profile = await client.GetUserProfileAsync(userId);
        /// Console.WriteLine($"名稱: {profile.DisplayName}");
        /// Console.WriteLine($"圖片: {profile.PictureUrl}");
        /// Console.WriteLine($"狀態: {profile.StatusMessage}");
        /// </code>
        /// </example>
        public virtual async Task<UserProfile> GetUserProfileAsync(string userId)
        {
            var json = await GetStringAsync($"{_uri}/bot/profile/{userId}").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<UserProfile>(json);
        }

        /// <summary>
        /// 取得機器人的資訊
        /// Gets bot information
        /// </summary>
        /// <returns>
        /// BotInfo 物件，包含機器人的基本資訊
        /// BotInfo object containing basic bot information
        /// </returns>
        /// <remarks>
        /// 可取得的機器人資訊包括：
        /// - userId：機器人的使用者 ID
        /// - basicId：機器人的基本 ID
        /// - premiumId：機器人的進階 ID（如果有設定）
        /// - displayName：機器人的顯示名稱
        /// - pictureUrl：機器人的圖片 URL
        /// - chatMode：聊天設定（chat 或 bot）
        /// - markAsReadMode：已讀設定（auto 或 manual）
        /// <para>
        /// Available bot information includes:
        /// - userId: Bot's user ID
        /// - basicId: Bot's basic ID
        /// - premiumId: Bot's premium ID (if set)
        /// - displayName: Bot's display name
        /// - pictureUrl: Bot's picture URL
        /// - chatMode: Chat settings (chat or bot)
        /// - markAsReadMode: Read settings (auto or manual)
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-bot-info
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-bot-info
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var botInfo = await client.GetBotInfoAsync();
        /// Console.WriteLine($"機器人名稱: {botInfo.DisplayName}");
        /// Console.WriteLine($"Basic ID: {botInfo.BasicId}");
        /// Console.WriteLine($"聊天模式: {botInfo.ChatMode}");
        /// </code>
        /// </example>
        public virtual async Task<BotInfo> GetBotInfoAsync()
        {
            var json = await GetStringAsync($"{_uri}/bot/info").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<BotInfo>(json);
        }
        #endregion

        #region Group
        /// <summary>
        /// 取得群組成員的個人資料
        /// Gets the user profile of a group member
        /// </summary>
        /// <param name="groupId">
        /// 群組識別碼（從 Webhook 事件的 source 物件中取得）
        /// Group identifier (obtained from source object of webhook event)
        /// </param>
        /// <param name="userId">
        /// 使用者識別碼（從 Webhook 事件的 source 物件中取得，不要使用 LINE app 中的 LINE ID）
        /// User identifier (obtained from source object of webhook event, do not use LINE ID from LINE app)
        /// </param>
        /// <returns>
        /// UserProfile 物件，包含成員的個人資料
        /// UserProfile object containing member's profile information
        /// </returns>
        /// <remarks>
        /// 此方法可取得群組中成員的資料，包括未加機器人為好友或已封鎖機器人的使用者。
        /// <para>
        /// This method can get member profiles in a group, including users who have not added the bot as a friend or have blocked the bot.
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-group-member-profile
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-group-member-profile
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var profile = await client.GetGroupMemberProfileAsync(groupId, userId);
        /// Console.WriteLine($"群組成員: {profile.DisplayName}");
        /// </code>
        /// </example>
        public virtual async Task<UserProfile> GetGroupMemberProfileAsync(string groupId, string userId)
        {
            var json = await GetStringAsync($"{_uri}/bot/group/{groupId}/member/{userId}").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<UserProfile>(json);
        }

        /// <summary>
        /// 取得群組成員的使用者 ID 清單
        /// Gets the user IDs of group members
        /// </summary>
        /// <param name="groupId">
        /// 群組識別碼
        /// Group identifier
        /// </param>
        /// <param name="continuationToken">
        /// 延續令牌（用於分頁，首次呼叫傳入 null）
        /// Continuation token (for pagination, pass null for first call)
        /// </param>
        /// <returns>
        /// GroupMemberIds 物件，包含使用者 ID 清單和下一頁的令牌
        /// GroupMemberIds object containing user ID list and next page token
        /// </returns>
        /// <remarks>
        /// 限制：
        /// - 此功能僅適用於 LINE@ 認證帳號或官方帳號
        /// - 每次最多回傳 100 個使用者 ID
        /// - 未同意官方帳號服務條款的使用者不會包含在結果中
        /// <para>
        /// Limitations:
        /// - This feature is only available for LINE@ Approved accounts or official accounts
        /// - Maximum 100 user IDs per response
        /// - Users who have not agreed to Official Accounts Terms of Use are not included
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-group-member-user-ids
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-group-member-user-ids
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 取得第一頁成員 ID
        /// var memberIds = await client.GetGroupMemberIdsAsync(groupId, null);
        /// foreach (var id in memberIds.MemberIds)
        /// {
        ///     Console.WriteLine($"Member ID: {id}");
        /// }
        /// 
        /// // 如果有下一頁
        /// if (memberIds.Next != null)
        /// {
        ///     var nextPage = await client.GetGroupMemberIdsAsync(groupId, memberIds.Next);
        /// }
        /// </code>
        /// </example>
        public virtual async Task<GroupMemberIds> GetGroupMemberIdsAsync(string groupId, string continuationToken)
        {
            var url = $"{_uri}/bot/group/{groupId}/members/ids" + (continuationToken != null ? $"?start={continuationToken}" : "");
            var json = await GetStringAsync(url).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<GroupMemberIds>(json);
        }

        /// <summary>
        /// 取得群組中所有成員的個人資料
        /// Gets the user profiles of all members in a group
        /// </summary>
        /// <param name="groupId">
        /// 群組識別碼
        /// Group identifier
        /// </param>
        /// <returns>
        /// 所有成員的 UserProfile 清單
        /// List of all members' UserProfile
        /// </returns>
        /// <remarks>
        /// 此方法會自動處理分頁，取得群組中所有成員的完整資料。
        /// 注意：對於大型群組，此操作可能需要較長時間。
        /// <para>
        /// This method automatically handles pagination to get complete data for all members in the group.
        /// Note: For large groups, this operation may take longer.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var profiles = await client.GetGroupMemberProfilesAsync(groupId);
        /// Console.WriteLine($"群組總人數: {profiles.Count}");
        /// foreach (var profile in profiles)
        /// {
        ///     Console.WriteLine($"- {profile.DisplayName}");
        /// }
        /// </code>
        /// </example>
        public virtual async Task<IList<UserProfile>> GetGroupMemberProfilesAsync(string groupId)
        {
            var result = new List<UserProfile>();
            string token = null;
            do
            {
                var ids = await GetGroupMemberIdsAsync(groupId, token).ConfigureAwait(false);
                var profiles = await Task.WhenAll(ids.MemberIds.Select(id => GetGroupMemberProfileAsync(groupId, id))).ConfigureAwait(false);
                result.AddRange(profiles);
                token = ids.Next;
            } while (token != null);
            return result;
        }

        /// <summary>
        /// 取得群組摘要資訊
        /// Gets group summary information
        /// </summary>
        /// <param name="groupId">
        /// 群組 ID
        /// Group ID
        /// </param>
        /// <returns>
        /// GroupSummary 物件，包含群組名稱、圖片等資訊
        /// GroupSummary object containing group name, picture, etc.
        /// </returns>
        /// <remarks>
        /// 群組摘要包含：
        /// - groupId：群組 ID
        /// - groupName：群組名稱
        /// - pictureUrl：群組圖示 URL（如果有設定）
        /// <para>
        /// Group summary includes:
        /// - groupId: Group ID
        /// - groupName: Group name
        /// - pictureUrl: Group icon URL (if set)
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-group-summary
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-group-summary
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var summary = await client.GetGroupSummaryAsync(groupId);
        /// Console.WriteLine($"群組名稱: {summary.GroupName}");
        /// Console.WriteLine($"群組圖示: {summary.PictureUrl}");
        /// </code>
        /// </example>
        public virtual async Task<GroupSummary> GetGroupSummaryAsync(string groupId)
        {
            var json = await GetStringAsync($"{_uri}/bot/group/{groupId}/summary").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<GroupSummary>(json);
        }

        /// <summary>
        /// 取得群組中的成員數量
        /// Gets the number of members in a group
        /// </summary>
        /// <param name="groupId">
        /// 群組 ID
        /// Group ID
        /// </param>
        /// <returns>
        /// 成員數量
        /// Member count
        /// </returns>
        /// <remarks>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-members-group-count
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-members-group-count
        /// </remarks>
        /// <example>
        /// <code>
        /// var count = await client.GetGroupMemberCountAsync(groupId);
        /// Console.WriteLine($"群組人數: {count}");
        /// </code>
        /// </example>
        public virtual async Task<int> GetGroupMemberCountAsync(string groupId)
        {
            var json = await GetStringAsync($"{_uri}/bot/group/{groupId}/members/count").ConfigureAwait(false);
            var mc = JsonConvert.DeserializeObject<MemberCount>(json);
            return mc != null ? mc.Count : 0;
        }

        /// <summary>
        /// 讓機器人離開群組
        /// Makes the bot leave a group
        /// </summary>
        /// <param name="groupId">
        /// 群組 ID（使用從 Webhook 回傳的 source 群組 ID）
        /// Group ID (use the ID returned via webhook from source group)
        /// </param>
        /// <remarks>
        /// 離開後，機器人將無法再接收該群組的訊息。
        /// <para>
        /// After leaving, the bot will no longer receive messages from that group.
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#leave-group
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#leave-group
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// await client.LeaveFromGroupAsync(groupId);
        /// Console.WriteLine("機器人已離開群組");
        /// </code>
        /// </example>
        public virtual async Task LeaveFromGroupAsync(string groupId)
        {
            var response = await _client.PostAsync($"{_uri}/bot/group/{groupId}/leave", null).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }
        #endregion

        #region Room
        /// <summary>
        /// 取得聊天室成員的個人資料
        /// Gets the user profile of a room member
        /// </summary>
        /// <param name="roomId">
        /// 聊天室識別碼（從 Webhook 事件的 source 物件中取得）
        /// Room identifier (obtained from source object of webhook event)
        /// </param>
        /// <param name="userId">
        /// 使用者識別碼（從 Webhook 事件的 source 物件中取得，不要使用 LINE app 中的 LINE ID）
        /// User identifier (obtained from source object of webhook event, do not use LINE ID from LINE app)
        /// </param>
        /// <returns>
        /// UserProfile 物件，包含成員的個人資料
        /// UserProfile object containing member's profile information
        /// </returns>
        /// <remarks>
        /// 此方法可取得聊天室中成員的資料，包括未加機器人為好友或已封鎖機器人的使用者。
        /// <para>
        /// This method can get member profiles in a room, including users who have not added the bot as a friend or have blocked the bot.
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-room-member-profile
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-room-member-profile
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var profile = await client.GetRoomMemberProfileAsync(roomId, userId);
        /// Console.WriteLine($"聊天室成員: {profile.DisplayName}");
        /// </code>
        /// </example>
        public virtual async Task<UserProfile> GetRoomMemberProfileAsync(string roomId, string userId)
        {
            var json = await GetStringAsync($"{_uri}/bot/room/{roomId}/member/{userId}").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<UserProfile>(json);
        }

        /// <summary>
        /// 取得聊天室成員的使用者 ID 清單
        /// Gets the user IDs of room members
        /// </summary>
        /// <param name="roomId">
        /// 聊天室識別碼
        /// Room identifier
        /// </param>
        /// <param name="continuationToken">
        /// 延續令牌（用於分頁，首次呼叫傳入 null）
        /// Continuation token (for pagination, pass null for first call)
        /// </param>
        /// <returns>
        /// GroupMemberIds 物件，包含使用者 ID 清單和下一頁的令牌
        /// GroupMemberIds object containing user ID list and next page token
        /// </returns>
        /// <remarks>
        /// 限制：
        /// - 此功能僅適用於 LINE@ 認證帳號或官方帳號
        /// - 每次最多回傳 100 個使用者 ID
        /// - 未同意官方帳號服務條款的使用者不會包含在結果中
        /// <para>
        /// Limitations:
        /// - This feature is only available for LINE@ Approved accounts or official accounts
        /// - Maximum 100 user IDs per response
        /// - Users who have not agreed to Official Accounts Terms of Use are not included
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-room-member-user-ids
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-room-member-user-ids
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var memberIds = await client.GetRoomMemberIdsAsync(roomId, null);
        /// foreach (var id in memberIds.MemberIds)
        /// {
        ///     Console.WriteLine($"Member ID: {id}");
        /// }
        /// </code>
        /// </example>
        public virtual async Task<GroupMemberIds> GetRoomMemberIdsAsync(string roomId, string continuationToken = null)
        {
            var url = $"{_uri}/bot/room/{roomId}/members/ids" + (continuationToken != null ? $"?start={continuationToken}" : "");
            var json = await GetStringAsync(url).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<GroupMemberIds>(json);
        }

        /// <summary>
        /// 取得聊天室中所有成員的個人資料
        /// Gets the user profiles of all members in a room
        /// </summary>
        /// <param name="roomId">
        /// 聊天室識別碼
        /// Room identifier
        /// </param>
        /// <returns>
        /// 所有成員的 UserProfile 清單
        /// List of all members' UserProfile
        /// </returns>
        /// <remarks>
        /// 此方法會自動處理分頁，取得聊天室中所有成員的完整資料。
        /// <para>
        /// This method automatically handles pagination to get complete data for all members in the room.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var profiles = await client.GetRoomMemberProfilesAsync(roomId);
        /// Console.WriteLine($"聊天室總人數: {profiles.Count}");
        /// </code>
        /// </example>
        public virtual async Task<IList<UserProfile>> GetRoomMemberProfilesAsync(string roomId)
        {
            var result = new List<UserProfile>();
            string token = null;
            do
            {
                var ids = await GetRoomMemberIdsAsync(roomId, token).ConfigureAwait(false);
                var profiles = await Task.WhenAll(ids.MemberIds.Select(id => GetRoomMemberProfileAsync(roomId, id))).ConfigureAwait(false);
                result.AddRange(profiles);
                token = ids.Next;
            } while (token != null);
            return result;
        }

        /// <summary>
        /// 取得聊天室中的成員數量
        /// Gets the number of members in a room
        /// </summary>
        /// <param name="roomId">
        /// 聊天室 ID
        /// Room ID
        /// </param>
        /// <returns>
        /// 成員數量
        /// Member count
        /// </returns>
        /// <remarks>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-members-room-count
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-members-room-count
        /// </remarks>
        /// <example>
        /// <code>
        /// var count = await client.GetRoomMemberCountAsync(roomId);
        /// Console.WriteLine($"聊天室人數: {count}");
        /// </code>
        /// </example>
        public virtual async Task<int> GetRoomMemberCountAsync(string roomId)
        {
            var json = await GetStringAsync($"{_uri}/bot/room/{roomId}/members/count").ConfigureAwait(false);
            var mc = JsonConvert.DeserializeObject<MemberCount>(json);
            return mc != null ? mc.Count : 0;
        }

        /// <summary>
        /// 讓機器人離開聊天室
        /// Makes the bot leave a room
        /// </summary>
        /// <param name="roomId">
        /// 聊天室 ID（使用從 Webhook 回傳的 source 聊天室 ID）
        /// Room ID (use the ID returned via webhook from source room)
        /// </param>
        /// <remarks>
        /// 離開後，機器人將無法再接收該聊天室的訊息。
        /// <para>
        /// After leaving, the bot will no longer receive messages from that room.
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#leave-room
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#leave-room
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// await client.LeaveFromRoomAsync(roomId);
        /// Console.WriteLine("機器人已離開聊天室");
        /// </code>
        /// </example>
        public virtual async Task LeaveFromRoomAsync(string roomId)
        {
            var response = await _client.PostAsync($"{_uri}/bot/room/{roomId}/leave", null).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }
        #endregion

        #region Webhook
        /// <summary>
        /// 設定 Webhook 端點 URL
        /// Sets webhook endpoint URL
        /// </summary>
        /// <param name="endpoint">
        /// Webhook URL（必須是 HTTPS，且可從網際網路存取）
        /// Webhook URL (must be HTTPS and accessible from the internet)
        /// </param>
        /// <remarks>
        /// Webhook URL 要求：
        /// - 必須使用 HTTPS 協定
        /// - 必須可從網際網路存取
        /// - 必須在 10 秒內回應
        /// - 必須回傳 HTTP 200 狀態碼
        /// <para>
        /// Webhook URL requirements:
        /// - Must use HTTPS protocol
        /// - Must be accessible from the internet
        /// - Must respond within 10 seconds
        /// - Must return HTTP 200 status code
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#set-webhook-endpoint-url
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#set-webhook-endpoint-url
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// await client.SetWebhookEndpointAsync("https://example.com/webhook");
        /// Console.WriteLine("Webhook 端點已設定");
        /// </code>
        /// </example>
        public virtual async Task SetWebhookEndpointAsync(string endpoint)
        {
            var request = new HttpRequestMessage(HttpMethod.Put, $"{_uri}/bot/channel/webhook/endpoint");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { endpoint }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            var response = await _client.SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 取得 Webhook 端點資訊
        /// Gets webhook endpoint information
        /// </summary>
        /// <returns>
        /// WebhookEndpoint 物件，包含端點 URL 和啟用狀態
        /// WebhookEndpoint object containing endpoint URL and active status
        /// </returns>
        /// <remarks>
        /// 回傳資訊包括：
        /// - endpoint：Webhook URL
        /// - active：是否啟用
        /// <para>
        /// Returned information includes:
        /// - endpoint: Webhook URL
        /// - active: Whether it is enabled
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-webhook-endpoint-information
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-webhook-endpoint-information
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var webhookInfo = await client.GetWebhookEndpointAsync();
        /// Console.WriteLine($"Webhook URL: {webhookInfo.Endpoint}");
        /// Console.WriteLine($"啟用狀態: {webhookInfo.Active}");
        /// </code>
        /// </example>
        public virtual async Task<WebhookEndpoint> GetWebhookEndpointAsync()
        {
            var json = await GetStringAsync($"{_uri}/bot/channel/webhook/endpoint").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<WebhookEndpoint>(json);
        }

        /// <summary>
        /// 測試 Webhook 端點
        /// Tests webhook endpoint
        /// </summary>
        /// <param name="endpoint">
        /// 要測試的 Webhook URL（選用，未指定則使用已設定的端點）
        /// Webhook URL to test (optional, uses configured endpoint if not specified)
        /// </param>
        /// <returns>
        /// WebhookTestResult 物件，包含測試結果
        /// WebhookTestResult object containing test results
        /// </returns>
        /// <remarks>
        /// 測試會發送一個模擬的 Webhook 事件到指定的端點。
        /// 端點必須在 10 秒內回應 HTTP 200，測試才會通過。
        /// <para>
        /// Test sends a simulated webhook event to the specified endpoint.
        /// Endpoint must respond with HTTP 200 within 10 seconds for the test to pass.
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#test-webhook-endpoint
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#test-webhook-endpoint
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 測試目前設定的端點
        /// var result = await client.TestWebhookEndpointAsync();
        /// Console.WriteLine($"測試結果: {result.Success}");
        /// 
        /// // 測試特定端點
        /// var result2 = await client.TestWebhookEndpointAsync("https://example.com/webhook");
        /// </code>
        /// </example>
        public virtual async Task<WebhookTestResult> TestWebhookEndpointAsync(string endpoint = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/channel/webhook/test");
            string body = endpoint == null ? "{}" : JsonConvert.SerializeObject(new { endpoint }, _jsonSerializerSettings);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await _client.SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonConvert.DeserializeObject<WebhookTestResult>(json);
        }
        #endregion

        #region Rich Menu & Alias & Batch
        /// <summary>
        /// 取得 Rich Menu 資訊
        /// Gets a rich menu via a rich menu ID
        /// </summary>
        /// <param name="richMenuId">
        /// 已上傳的 Rich Menu ID
        /// ID of an uploaded rich menu
        /// </param>
        /// <returns>
        /// RichMenu 物件，包含 Rich Menu 的完整設定
        /// RichMenu object containing complete rich menu configuration
        /// </returns>
        /// <remarks>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-rich-menu
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-rich-menu
        /// </remarks>
        /// <example>
        /// <code>
        /// var richMenu = await client.GetRichMenuAsync(richMenuId);
        /// Console.WriteLine($"Rich Menu 名稱: {richMenu.Name}");
        /// Console.WriteLine($"選擇區域數量: {richMenu.Areas.Count}");
        /// </code>
        /// </example>
        public virtual async Task<RichMenu> GetRichMenuAsync(string richMenuId)
        {
            var json = await GetStringAsync($"{_uri}/bot/richmenu/{richMenuId}").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<ResponseRichMenu>(json);
        }

        /// <summary>
        /// 建立 Rich Menu
        /// Creates a rich menu
        /// </summary>
        /// <param name="richMenu">
        /// Rich Menu 物件，定義選單的結構和行為
        /// Rich menu object defining the menu structure and behavior
        /// </param>
        /// <returns>
        /// 建立的 Rich Menu ID
        /// Created Rich Menu ID
        /// </returns>
        /// <remarks>
        /// 注意事項：
        /// - 必須上傳 Rich Menu 圖片並連結到使用者才會顯示
        /// - 一個機器人最多可建立 1000 個 Rich Menu
        /// - Rich Menu 以物件形式表示
        /// <para>
        /// Important notes:
        /// - Must upload rich menu image and link to user for display
        /// - Maximum 1000 rich menus per bot
        /// - Rich menu is represented as an object
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#create-rich-menu
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#create-rich-menu
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var richMenu = new RichMenu
        /// {
        ///     Size = new RichMenuSize { Width = 2500, Height = 1686 },
        ///     Selected = false,
        ///     Name = "我的選單",
        ///     ChatBarText = "開啟選單",
        ///     Areas = new List&lt;ActionArea&gt;
        ///     {
        ///         new ActionArea
        ///         {
        ///             Bounds = new AreaBounds { X = 0, Y = 0, Width = 1250, Height = 1686 },
        ///             Action = new MessageTemplateAction("選項 A", "A")
        ///         }
        ///     }
        /// };
        /// var richMenuId = await client.CreateRichMenuAsync(richMenu);
        /// </code>
        /// </example>
        public virtual async Task<string> CreateRichMenuAsync(RichMenu richMenu)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/richmenu");
            request.Content = new StringContent(JsonConvert.SerializeObject(richMenu, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            var response = await _client.SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonConvert.DeserializeAnonymousType(json, new { richMenuId = "" }).richMenuId;
        }

        /// <summary>
        /// 驗證 Rich Menu 物件
        /// Validates a rich menu object
        /// </summary>
        /// <param name="richMenu">
        /// 要驗證的 Rich Menu 物件
        /// Rich menu object to validate
        /// </param>
        /// <remarks>
        /// 在建立 Rich Menu 前，可先使用此方法驗證設定是否正確。
        /// 驗證不通過會拋出例外。
        /// <para>
        /// Before creating a rich menu, use this method to validate if the configuration is correct.
        /// Validation failure will throw an exception.
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#validate-rich-menu-object
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#validate-rich-menu-object
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// try
        /// {
        ///     await client.ValidateRichMenuAsync(richMenu);
        ///     Console.WriteLine("Rich Menu 設定有效");
        /// }
        /// catch (Exception ex)
        /// {
        ///     Console.WriteLine($"驗證失敗: {ex.Message}");
        /// }
        /// </code>
        /// </example>
        public virtual async Task ValidateRichMenuAsync(RichMenu richMenu)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/richmenu/validate");
            request.Content = new StringContent(JsonConvert.SerializeObject(richMenu, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            var response = await _client.SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 刪除 Rich Menu
        /// Deletes a rich menu
        /// </summary>
        /// <param name="richMenuId">
        /// 要刪除的 Rich Menu ID
        /// Rich Menu ID to delete
        /// </param>
        /// <remarks>
        /// 刪除後，該 Rich Menu 將無法再使用，且已連結的使用者將不再看到此選單。
        /// <para>
        /// After deletion, the rich menu cannot be used, and users linked to it will no longer see this menu.
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#delete-rich-menu
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#delete-rich-menu
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// await client.DeleteRichMenuAsync(richMenuId);
        /// Console.WriteLine("Rich Menu 已刪除");
        /// </code>
        /// </example>
        public virtual async Task DeleteRichMenuAsync(string richMenuId)
        {
            var response = await _client.DeleteAsync($"{_uri}/bot/richmenu/{richMenuId}").ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 建立 Rich Menu 別名
        /// Creates a rich menu alias
        /// </summary>
        /// <param name="richMenuId">
        /// 要關聯的 Rich Menu ID
        /// Rich menu ID to be associated with the alias
        /// </param>
        /// <param name="richMenuAliasId">
        /// Rich Menu 別名 ID（最多 100 字元）
        /// Rich menu alias ID (maximum 100 characters)
        /// </param>
        /// <remarks>
        /// Rich Menu 別名可讓您使用自訂 ID 來管理 Rich Menu，而不需要記住系統產生的 Rich Menu ID。
        /// <para>
        /// Rich menu alias allows you to manage rich menus using custom IDs instead of system-generated rich menu IDs.
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#create-rich-menu-alias
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#create-rich-menu-alias
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// await client.CreateRichMenuAliasAsync(richMenuId, "summer-menu-2024");
        /// Console.WriteLine("Rich Menu 別名已建立");
        /// </code>
        /// </example>
        public virtual async Task CreateRichMenuAliasAsync(string richMenuId, string richMenuAliasId)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/richmenu/alias");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { richMenuId, richMenuAliasId }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            var response = await _client.SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 刪除 Rich Menu 別名
        /// Deletes a rich menu alias
        /// </summary>
        /// <param name="richMenuAliasId">
        /// 要刪除的 Rich Menu 別名 ID
        /// Rich menu alias ID to delete
        /// </param>
        /// <remarks>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#delete-rich-menu-alias
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#delete-rich-menu-alias
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// await client.DeleteRichMenuAliasAsync("summer-menu-2024");
        /// Console.WriteLine("Rich Menu 別名已刪除");
        /// </code>
        /// </example>
        public virtual async Task DeleteRichMenuAliasAsync(string richMenuAliasId)
        {
            var response = await _client.DeleteAsync($"{_uri}/bot/richmenu/alias/{richMenuAliasId}").ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 更新 Rich Menu 別名
        /// Updates a rich menu alias
        /// </summary>
        /// <param name="richMenuAliasId">
        /// 要更新的 Rich Menu 別名 ID
        /// Rich menu alias ID to update
        /// </param>
        /// <param name="richMenuId">
        /// 新的 Rich Menu ID
        /// New rich menu ID to be associated
        /// </param>
        /// <remarks>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#update-rich-menu-alias
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#update-rich-menu-alias
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// await client.UpdateRichMenuAliasAsync("summer-menu-2024", newRichMenuId);
        /// Console.WriteLine("Rich Menu 別名已更新");
        /// </code>
        /// </example>
        public virtual async Task UpdateRichMenuAliasAsync(string richMenuAliasId, string richMenuId)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/richmenu/alias/{richMenuAliasId}");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { richMenuId }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            var response = await _client.SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 取得 Rich Menu 別名資訊
        /// Gets rich menu alias information
        /// </summary>
        /// <param name="richMenuAliasId">
        /// Rich Menu 別名 ID
        /// Rich menu alias ID
        /// </param>
        /// <returns>
        /// RichMenuAlias 物件，包含別名和關聯的 Rich Menu ID
        /// RichMenuAlias object containing alias and associated rich menu ID
        /// </returns>
        /// <remarks>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-rich-menu-alias-information
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-rich-menu-alias-information
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var alias = await client.GetRichMenuAliasAsync("summer-menu-2024");
        /// Console.WriteLine($"別名: {alias.RichMenuAliasId}");
        /// Console.WriteLine($"關聯的 Rich Menu: {alias.RichMenuId}");
        /// </code>
        /// </example>
        public virtual async Task<RichMenuAlias> GetRichMenuAliasAsync(string richMenuAliasId)
        {
            var json = await GetStringAsync($"{_uri}/bot/richmenu/alias/{richMenuAliasId}").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<RichMenuAlias>(json);
        }

        /// <summary>
        /// 取得 Rich Menu 別名清單
        /// Gets list of rich menu aliases
        /// </summary>
        public virtual async Task<RichMenuAliasList> GetRichMenuAliasListAsync()
        {
            var json = await GetStringAsync($"{_uri}/bot/richmenu/alias/list").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<RichMenuAliasList>(json);
        }

        /// <summary>
        /// 取得使用者目前連結的 Rich Menu ID
        /// Gets the ID of the rich menu linked to a user
        /// </summary>
        public virtual async Task<string> GetRichMenuIdOfUserAsync(string userId)
        {
            var json = await GetStringAsync($"{_uri}/bot/user/{userId}/richmenu").ConfigureAwait(false);
            return JsonConvert.DeserializeAnonymousType(json, new { richMenuId = "" }).richMenuId;
        }

        /// <summary>
        /// 設定預設 Rich Menu
        /// Sets a default rich menu
        /// </summary>
        public virtual async Task SetDefaultRichMenuAsync(string richMenuId)
        {
            var response = await _client.PostAsync($"{_uri}/bot/user/all/richmenu/{richMenuId}", null).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 取得預設 Rich Menu ID
        /// Gets default rich menu ID
        /// </summary>
        public virtual async Task<string> GetDefaultRichMenuIdAsync()
        {
            var json = await GetStringAsync($"{_uri}/bot/user/all/richmenu").ConfigureAwait(false);
            return JsonConvert.DeserializeAnonymousType(json, new { richMenuId = "" }).richMenuId;
        }

        /// <summary>
        /// 取消預設 Rich Menu
        /// Cancels default rich menu
        /// </summary>
        public virtual async Task CancelDefaultRichMenuAsync()
        {
            var response = await _client.DeleteAsync($"{_uri}/bot/user/all/richmenu").ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 將 Rich Menu 連結到使用者
        /// Links a rich menu to a user
        /// </summary>
        public virtual async Task LinkRichMenuToUserAsync(string userId, string richMenuId)
        {
            var response = await _client.PostAsync($"{_uri}/bot/user/{userId}/richmenu/{richMenuId}", null).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 將 Rich Menu 連結到多位使用者
        /// Links a rich menu to multiple users
        /// </summary>
        public virtual async Task LinkRichMenuToUsersAsync(string richMenuId, IList<string> userIds)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/richmenu/bulk/link");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { richMenuId, userIds }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            var response = await _client.SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 將 Rich Menu 自使用者解除連結
        /// Unlinks a rich menu from a user
        /// </summary>
        public virtual async Task UnLinkRichMenuFromUserAsync(string userId)
        {
            var response = await _client.DeleteAsync($"{_uri}/bot/user/{userId}/richmenu").ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 將 Rich Menu 自多位使用者解除連結
        /// Unlinks rich menus from multiple users
        /// </summary>
        public virtual async Task UnLinkRichMenuFromUsersAsync(IList<string> userIds)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/richmenu/bulk/unlink");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { userIds }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            var response = await _client.SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 批次控制 Rich Menu (link/unlink/unlinkAll)
        /// Batch control rich menus
        /// </summary>
        public virtual async Task RichMenuBatchOperationAsync(IList<RichMenuBatchOperation> operations)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/richmenu/batch");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { operations }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            var response = await _client.SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 取得批次控制進度
        /// Gets batch control progress
        /// </summary>
        public virtual async Task<RichMenuBatchProgress> GetRichMenuBatchProgressAsync(string requestId)
        {
            var json = await GetStringAsync(ApiUrl($"/bot/richmenu/progress/batch?requestId={Uri.EscapeDataString(requestId)}")).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<RichMenuBatchProgress>(json);
        }

        /// <summary>
        /// 驗證批次控制請求
        /// Validates batch control request
        /// </summary>
        public virtual async Task ValidateRichMenuBatchRequestAsync(IList<RichMenuBatchOperation> operations)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl("/bot/richmenu/validate/batch"));
            request.Content = new StringContent(JsonConvert.SerializeObject(new { operations }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            var response = await _client.SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 下載 Rich Menu 圖片
        /// Downloads rich menu image
        /// </summary>
        public virtual async Task<ContentStream> DownloadRichMenuImageAsync(string richMenuId)
        {
            var response = await _client.GetAsync(DataUrl($"/bot/richmenu/{richMenuId}/content")).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
            return new ContentStream(await response.Content.ReadAsStreamAsync().ConfigureAwait(false), response.Content.Headers);
        }

        /// <summary>
        /// 上傳 JPEG Rich Menu 圖片
        /// Uploads JPEG rich menu image
        /// </summary>
        public virtual async Task UploadRichMenuJpegImageAsync(Stream stream, string richMenuId)
        {
            var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            var response = await _client.PostAsync(DataUrl($"/bot/richmenu/{richMenuId}/content"), content).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 上傳 PNG Rich Menu 圖片
        /// Uploads PNG rich menu image
        /// </summary>
        public virtual async Task UploadRichMenuPngImageAsync(Stream stream, string richMenuId)
        {
            var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            var response = await _client.PostAsync(DataUrl($"/bot/richmenu/{richMenuId}/content"), content).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 取得所有 Rich Menu 清單
        /// Gets list of all uploaded rich menus
        /// </summary>
        public virtual async Task<IList<ResponseRichMenu>> GetRichMenuListAsync()
        {
            var json = await GetStringAsync($"{_uri}/bot/richmenu/list").ConfigureAwait(false);
            var list = new List<ResponseRichMenu>();
            if (!string.IsNullOrWhiteSpace(json))
            {
                var obj = JObject.Parse(json);
                var richmenus = obj["richmenus"] as JArray;
                if (richmenus != null)
                {
                    foreach (var rm in richmenus)
                    {
                        list.Add(ResponseRichMenu.CreateFrom(rm));
                    }
                }
            }
            return list;
        }
        #endregion

        #region Insights
        /// <summary>
        /// 取得訊息傳送統計
        /// Gets message delivery statistics
        /// </summary>
        public virtual async Task<MessageDelivery> GetMessageDeliveryAsync(DateTime date)
        {
            var json = await GetStringAsync(ApiUrl($"/bot/insight/message/delivery?date={date:yyyyMMdd}")).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<MessageDelivery>(json);
        }

        /// <summary>
        /// 取得關注者統計
        /// Gets follower statistics
        /// </summary>
        public virtual async Task<FollowerStatistics> GetFollowerStatisticsAsync(DateTime date)
        {
            var json = await GetStringAsync(ApiUrl($"/bot/insight/followers?date={date:yyyyMMdd}")).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<FollowerStatistics>(json);
        }

        /// <summary>
        /// 取得好友人口統計
        /// Gets friend demographics
        /// </summary>
        public virtual async Task<DemographicStatistics> GetFriendDemographicsAsync()
        {
            var json = await GetStringAsync(ApiUrl("/bot/insight/demographic")).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<DemographicStatistics>(json);
        }

        /// <summary>
        /// 取得用戶互動統計
        /// Gets user interaction statistics
        /// </summary>
        public virtual async Task<UserInteractionStatistics> GetUserInteractionStatisticsAsync(string requestId)
        {
            var json = await GetStringAsync(ApiUrl($"/bot/insight/message/event?requestId={requestId}")).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<UserInteractionStatistics>(json);
        }

        /// <summary>
        /// 取得單位統計
        /// Gets statistics per unit
        /// </summary>
        public virtual async Task<StatisticsPerUnit> GetStatisticsPerUnitAsync(string customAggregationUnit, string from, string to)
        {
            var json = await GetStringAsync(ApiUrl($"/bot/insight/message/event/aggregation?customAggregationUnit={Uri.EscapeDataString(customAggregationUnit)}&from={from}&to={to}")).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<StatisticsPerUnit>(json);
        }

        /// <summary>
        /// 取得聚合資訊
        /// Gets aggregation info
        /// </summary>
        public virtual async Task<AggregationInfo> GetAggregationInfoAsync()
        {
            var json = await GetStringAsync(ApiUrl("/bot/message/aggregation/info")).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<AggregationInfo>(json);
        }

        /// <summary>
        /// 取得聚合單位名稱清單
        /// Gets aggregation unit name list
        /// </summary>
        public virtual async Task<AggregationUnitNameList> GetAggregationUnitNameListAsync(int limit = 100, string start = null)
        {
            var url = ApiUrl($"/bot/message/aggregation/list?limit={limit}");
            if (!string.IsNullOrEmpty(start))
                url += $"&start={start}";
            var json = await GetStringAsync(url).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<AggregationUnitNameList>(json);
        }
        #endregion

        #region Account Link
        /// <summary>
        /// 發行用於帳號連結功能的 Link Token
        /// Issues a link token used for the account link feature
        /// </summary>
        /// <param name="userId">
        /// 要連結的 LINE 帳號使用者 ID（從帳號連結事件物件的 source 中取得，不要使用 LINE app 中的 LINE ID）
        /// User ID for the LINE account to be linked (found in source object of account link event, do not use LINE ID from LINE app)
        /// </param>
        /// <returns>
        /// Link Token（有效期 10 分鐘，只能使用一次）
        /// Link token (valid for 10 minutes, can only be used once)
        /// </returns>
        /// <remarks>
        /// Link Token 的特性：
        /// - 有效期為 10 分鐘
        /// - 只能使用一次
        /// - 有效期可能會變更，恕不另行通知
        /// <para>
        /// Link token characteristics:
        /// - Valid for 10 minutes
        /// - Can only be used once
        /// - Validity period may change without notice
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#issue-link-token
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#issue-link-token
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var linkToken = await client.IssueLinkTokenAsync(userId);
        /// Console.WriteLine($"Link Token: {linkToken}");
        /// // 將 Link Token 傳送給使用者進行帳號連結
        /// </code>
        /// </example>
        public virtual async Task<string> IssueLinkTokenAsync(string userId)
        {
            var response = await _client.PostAsync($"{_uri}/bot/user/{userId}/linkToken", null).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonConvert.DeserializeAnonymousType(json, new { linkToken = "" }).linkToken;
        }
        #endregion

        #region Number of sent messages / Quota
        /// <summary>
        /// 取得廣播訊息的傳送數量
        /// Gets the number of messages sent with the broadcast endpoint
        /// </summary>
        /// <param name="date">
        /// 訊息傳送日期（格式：yyyyMMdd，例如：20191231，時區：UTC+9）
        /// Date the messages were sent (format: yyyyMMdd, example: 20191231, timezone: UTC+9)
        /// </param>
        /// <returns>
        /// NumberOfSentMessages 物件，包含傳送數量統計
        /// NumberOfSentMessages object containing sent message statistics
        /// </returns>
        /// <remarks>
        /// 此操作取得的訊息數量不包含從 LINE 官方帳號管理後台傳送的訊息。
        /// <para>
        /// The number of messages retrieved by this operation does not include messages sent from LINE Official Account Manager.
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-number-of-broadcast-messages
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-number-of-broadcast-messages
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var count = await client.GetNumberOfSentBroadcastMessagesAsync(new DateTime(2024, 1, 15));
        /// Console.WriteLine($"廣播訊息數量: {count.TotalCount}");
        /// </code>
        /// </example>
        public virtual async Task<NumberOfSentMessages> GetNumberOfSentBroadcastMessagesAsync(DateTime date)
        {
            var json = await GetStringAsync($"{_uri}/bot/message/delivery/broadcast?date={date:yyyyMMdd}").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<NumberOfSentMessages>(json);
        }

        /// <summary>
        /// 取得回覆訊息的傳送數量
        /// Gets the number of messages sent with the reply endpoint
        /// </summary>
        /// <param name="date">
        /// 訊息傳送日期（格式：yyyyMMdd，時區：UTC+9）
        /// Date the messages were sent (format: yyyyMMdd, timezone: UTC+9)
        /// </param>
        /// <returns>
        /// NumberOfSentMessages 物件，包含傳送數量統計
        /// NumberOfSentMessages object containing sent message statistics
        /// </returns>
        /// <remarks>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-number-of-reply-messages
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-number-of-reply-messages
        /// </remarks>
        /// <example>
        /// <code>
        /// var count = await client.GetNumberOfSentReplyMessagesAsync(DateTime.Today);
        /// Console.WriteLine($"回覆訊息數量: {count.TotalCount}");
        /// </code>
        /// </example>
        public virtual async Task<NumberOfSentMessages> GetNumberOfSentReplyMessagesAsync(DateTime date)
        {
            var json = await GetStringAsync($"{_uri}/bot/message/delivery/reply?date={date:yyyyMMdd}").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<NumberOfSentMessages>(json);
        }

        /// <summary>
        /// 取得推播訊息的傳送數量
        /// Gets the number of messages sent with the push endpoint
        /// </summary>
        /// <param name="date">
        /// 訊息傳送日期（格式：yyyyMMdd，時區：UTC+9）
        /// Date the messages were sent (format: yyyyMMdd, timezone: UTC+9)
        /// </param>
        /// <returns>
        /// NumberOfSentMessages 物件，包含傳送數量統計
        /// NumberOfSentMessages object containing sent message statistics
        /// </returns>
        /// <remarks>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-number-of-push-messages
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-number-of-push-messages
        /// </remarks>
        /// <example>
        /// <code>
        /// var count = await client.GetNumberOfSentPushMessagesAsync(DateTime.Today);
        /// Console.WriteLine($"推播訊息數量: {count.TotalCount}");
        /// </code>
        /// </example>
        public virtual async Task<NumberOfSentMessages> GetNumberOfSentPushMessagesAsync(DateTime date)
        {
            var json = await GetStringAsync($"{_uri}/bot/message/delivery/push?date={date:yyyyMMdd}").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<NumberOfSentMessages>(json);
        }

        /// <summary>
        /// 取得多播訊息的傳送數量
        /// Gets the number of messages sent with the multicast endpoint
        /// </summary>
        /// <param name="date">
        /// 訊息傳送日期（格式：yyyyMMdd，時區：UTC+9）
        /// Date the messages were sent (format: yyyyMMdd, timezone: UTC+9)
        /// </param>
        /// <returns>
        /// NumberOfSentMessages 物件，包含傳送數量統計
        /// NumberOfSentMessages object containing sent message statistics
        /// </returns>
        /// <remarks>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-number-of-multicast-messages
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-number-of-multicast-messages
        /// </remarks>
        /// <example>
        /// <code>
        /// var count = await client.GetNumberOfSentMulticastMessagesAsync(DateTime.Today);
        /// Console.WriteLine($"多播訊息數量: {count.TotalCount}");
        /// </code>
        /// </example>
        public virtual async Task<NumberOfSentMessages> GetNumberOfSentMulticastMessagesAsync(DateTime date)
        {
            var json = await GetStringAsync($"{_uri}/bot/message/delivery/multicast?date={date:yyyyMMdd}").ConfigureAwait(false);
            return JsonConvert.DeserializeObject<NumberOfSentMessages>(json);
        }

        /// <summary>
        /// 取得當月傳送訊息的目標上限
        /// Gets the target limit for sending messages in the current month
        /// </summary>
        /// <returns>
        /// MessageQuota 物件，包含配額類型和限制
        /// MessageQuota object containing quota type and limits
        /// </returns>
        /// <remarks>
        /// 配額類型：
        /// - none：無訊息數量限制
        /// - limited：有訊息數量限制
        /// <para>
        /// Quota types:
        /// - none: No limit on the number of messages
        /// - limited: There is a limit on the number of messages
        /// </para>
        /// <para>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-quota
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-quota
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// var quota = await client.GetMessageQuotaAsync();
        /// Console.WriteLine($"配額類型: {quota.Type}");
        /// if (quota.Type == QuotaType.Limited)
        /// {
        ///     Console.WriteLine($"總配額: {quota.Value}");
        /// }
        /// </code>
        /// </example>
        public virtual async Task<MessageQuota> GetMessageQuotaAsync()
        {
            var json = await GetStringAsync($"{_uri}/bot/message/quota"). ConfigureAwait(false);
            return JsonConvert.DeserializeObject<MessageQuota>(json);
        }

        /// <summary>
        /// 取得當月已傳送的訊息數量
        /// Gets the number of sent messages in the current month
        /// </summary>
        /// <returns>
        /// MessageQuotaConsumption 物件，包含已使用的訊息數量
        /// MessageQuotaConsumption object containing the number of messages used
        /// </returns>
        /// <remarks>
        /// 官方文件：https://developers.line.biz/en/reference/messaging-api/#get-consumption
        /// Official documentation: https://developers.line.biz/en/reference/messaging-api/#get-consumption
        /// </remarks>
        /// <example>
        /// <code>
        /// var consumption = await client.GetMessageQuotaConsumptionAsync();
        /// Console.WriteLine($"已使用訊息數: {consumption.TotalUsage}");
        /// </code>
        /// </example>
        public virtual async Task<MessageQuotaConsumption> GetMessageQuotaConsumptionAsync()
        {
            var json = await GetStringAsync($"{_uri}/bot/message/quota/consumption"). ConfigureAwait(false);
            return JsonConvert.DeserializeObject<MessageQuotaConsumption>(json);
        }
        #endregion

        #region Coupon
        /// <summary>
        /// 建立優惠券
        /// Creates a coupon
        /// </summary>
        public virtual async Task<Coupon> CreateCouponAsync(CreateCouponRequest request)
        {
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, ApiUrl("/bot/coupon"));
            httpRequest.Content = new StringContent(JsonConvert.SerializeObject(request, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            var response = await _client.SendAsync(httpRequest).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonConvert.DeserializeObject<Coupon>(json);
        }

        /// <summary>
        /// 停止優惠券
        /// Closes a coupon
        /// </summary>
        public virtual async Task CloseCouponAsync(string couponId)
        {
            var httpRequest = new HttpRequestMessage(HttpMethod.Put, ApiUrl($"/bot/coupon/{couponId}/close"));
            var response = await _client.SendAsync(httpRequest).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 取得優惠券清單
        /// Gets coupon list
        /// </summary>
        public virtual async Task<CouponList> GetCouponListAsync(int limit = 20, string next = null)
        {
            var url = ApiUrl($"/bot/coupon?limit={limit}");
            if (!string.IsNullOrEmpty(next))
                url += $"&next={next}";
            var json = await GetStringAsync(url).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<CouponList>(json);
        }

        /// <summary>
        /// 取得優惠券詳情
        /// Gets coupon details
        /// </summary>
        public virtual async Task<Coupon> GetCouponAsync(string couponId)
        {
            var json = await GetStringAsync(ApiUrl($"/bot/coupon/{couponId}")).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<Coupon>(json);
        }
        #endregion

        #region Membership
        /// <summary>
        /// 取得用戶會員資格訂閱狀態
        /// Gets user's membership subscription status
        /// </summary>
        public virtual async Task<MembershipSubscription> GetMembershipSubscriptionAsync(string userId)
        {
            var json = await GetStringAsync(ApiUrl($"/bot/membership/subscription/{userId}")).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<MembershipSubscription>(json);
        }

        /// <summary>
        /// 取得會員方案的用戶 ID 清單
        /// Gets list of user IDs who have joined a membership plan
        /// </summary>
        /// <param name="membershipId">
        /// 會員方案 ID
        /// Membership plan ID
        /// </param>
        /// <param name="limit">
        /// 每次查詢的最大數量（預設 100）
        /// Maximum number of items per query (default 100)
        /// </param>
        /// <param name="next">
        /// 延續令牌（用於分頁）
        /// Continuation token (for pagination)
        /// </param>
        /// <returns>
        /// 會員方案的用戶 ID 清單
        /// List of user IDs who joined the membership plan
        /// </returns>
        public virtual async Task<MembershipUserIds> GetMembershipUserIdsAsync(string membershipId, int limit = 100, string next = null)
        {
            var url = ApiUrl($"/bot/membership/{membershipId}/users/ids?limit={limit}");
            if (!string.IsNullOrEmpty(next))
                url += $"&next={next}";
            var json = await GetStringAsync(url).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<MembershipUserIds>(json);
        }

        /// <summary>
        /// 取得會員方案清單
        /// Gets list of membership plans
        /// </summary>
        /// <returns>
        /// 會員方案清單
        /// List of membership plans
        /// </returns>
        public virtual async Task<MembershipPlanList> GetMembershipPlansAsync()
        {
            var json = await GetStringAsync(ApiUrl("/bot/membership/list")).ConfigureAwait(false);
            return JsonConvert.DeserializeObject<MembershipPlanList>(json);
        }
        #endregion

        #region Message Validation
        /// <summary>
        /// 驗證回覆訊息內容
        /// Validates reply message content
        /// </summary>
        /// <param name="messages">
        /// 要驗證的訊息清單
        /// List of messages to validate
        /// </param>
        /// <remarks>
        /// 此方法會檢查訊息格式是否正確，但不會實際傳送訊息。
        /// <para>
        /// This method checks if the message format is correct but does not actually send messages.
        /// </para>
        /// </remarks>
        public virtual async Task ValidateReplyMessageAsync(IList<ISendMessage> messages)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/validate/reply");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            var response = await _client.SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 驗證推播訊息內容
        /// Validates push message content
        /// </summary>
        /// <param name="messages">
        /// 要驗證的訊息清單
        /// List of messages to validate
        /// </param>
        public virtual async Task ValidatePushMessageAsync(IList<ISendMessage> messages)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/validate/push");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            var response = await _client.SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 驗證多播訊息內容
        /// Validates multicast message content
        /// </summary>
        /// <param name="messages">
        /// 要驗證的訊息清單
        /// List of messages to validate
        /// </param>
        public virtual async Task ValidateMulticastMessageAsync(IList<ISendMessage> messages)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/validate/multicast");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            var response = await _client.SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 驗證窄播訊息內容
        /// Validates narrowcast message content
        /// </summary>
        /// <param name="messages">
        /// 要驗證的訊息清單
        /// List of messages to validate
        /// </param>
        public virtual async Task ValidateNarrowcastMessageAsync(IList<ISendMessage> messages)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/validate/narrowcast");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            var response = await _client.SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 驗證廣播訊息內容
        /// Validates broadcast message content
        /// </summary>
        /// <param name="messages">
        /// 要驗證的訊息清單
        /// List of messages to validate
        /// </param>
        public virtual async Task ValidateBroadcastMessageAsync(IList<ISendMessage> messages)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_uri}/bot/message/validate/broadcast");
            request.Content = new StringContent(JsonConvert.SerializeObject(new { messages }, _jsonSerializerSettings), Encoding.UTF8, "application/json");
            var response = await _client.SendAsync(request).ConfigureAwait(false);
            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
        }
        #endregion

        #region Audience (Currently not implemented - placeholders for interface compliance)
        /// <summary>
        /// 建立上傳型受眾群組（目前未實作）
        /// Creates upload audience group (currently not implemented)
        /// </summary>
        public virtual Task<CreateAudienceGroupResponse> CreateUploadAudienceGroupAsync(CreateUploadAudienceGroupRequest request)
        {
            throw new NotImplementedException("Audience Group APIs are not yet implemented. Please refer to LINE API documentation.");
        }

        /// <summary>
        /// 透過檔案建立上傳型受眾群組（目前未實作）
        /// Creates upload audience group by file (currently not implemented)
        /// </summary>
        public virtual Task<CreateAudienceGroupResponse> CreateUploadAudienceGroupByFileAsync(string description, bool? isIfaAudience, string uploadDescription, Stream file)
        {
            throw new NotImplementedException("Audience Group APIs are not yet implemented. Please refer to LINE API documentation.");
        }

        /// <summary>
        /// 將受眾加入群組（目前未實作）
        /// Adds audience to group (currently not implemented)
        /// </summary>
        public virtual Task AddAudienceToGroupAsync(AddAudienceToGroupRequest request)
        {
            throw new NotImplementedException("Audience Group APIs are not yet implemented. Please refer to LINE API documentation.");
        }

        /// <summary>
        /// 透過檔案將受眾加入群組（目前未實作）
        /// Adds audience to group by file (currently not implemented)
        /// </summary>
        public virtual Task AddAudienceToGroupByFileAsync(long audienceGroupId, string uploadDescription, Stream file)
        {
            throw new NotImplementedException("Audience Group APIs are not yet implemented. Please refer to LINE API documentation.");
        }

        /// <summary>
        /// 建立點擊型受眾群組（目前未實作）
        /// Creates click-based audience group (currently not implemented)
        /// </summary>
        public virtual Task<CreateAudienceGroupResponse> CreateClickAudienceGroupAsync(CreateClickAudienceGroupRequest request)
        {
            throw new NotImplementedException("Audience Group APIs are not yet implemented. Please refer to LINE API documentation.");
        }

        /// <summary>
        /// 建立曝光型受眾群組（目前未實作）
        /// Creates impression-based audience group (currently not implemented)
        /// </summary>
        public virtual Task<CreateAudienceGroupResponse> CreateImpAudienceGroupAsync(CreateImpAudienceGroupRequest request)
        {
            throw new NotImplementedException("Audience Group APIs are not yet implemented. Please refer to LINE API documentation.");
        }

        /// <summary>
        /// 更新受眾群組描述（目前未實作）
        /// Updates audience group description (currently not implemented)
        /// </summary>
        public virtual Task UpdateAudienceGroupDescriptionAsync(long audienceGroupId, string description)
        {
            throw new NotImplementedException("Audience Group APIs are not yet implemented. Please refer to LINE API documentation.");
        }

        /// <summary>
        /// 刪除受眾群組（目前未實作）
        /// Deletes audience group (currently not implemented)
        /// </summary>
        public virtual Task DeleteAudienceGroupAsync(long audienceGroupId)
        {
            throw new NotImplementedException("Audience Group APIs are not yet implemented. Please refer to LINE API documentation.");
        }

        /// <summary>
        /// 取得受眾群組（目前未實作）
        /// Gets audience group (currently not implemented)
        /// </summary>
        public virtual Task<AudienceGroup> GetAudienceGroupAsync(long audienceGroupId)
        {
            throw new NotImplementedException("Audience Group APIs are not yet implemented. Please refer to LINE API documentation.");
        }

        /// <summary>
        /// 取得受眾群組清單（目前未實作）
        /// Gets list of audience groups (currently not implemented)
        /// </summary>
        public virtual Task<AudienceGroupList> GetAudienceGroupsAsync(long page, string description, string status, long size, bool includesExternalPublicGroups, string createRoute)
        {
            throw new NotImplementedException("Audience Group APIs are not yet implemented. Please refer to LINE API documentation.");
        }

        /// <summary>
        /// 取得受眾群組授權層級（目前未實作）
        /// Gets audience group authority level (currently not implemented)
        /// </summary>
        public virtual Task<string> GetAudienceGroupAuthorityLevelAsync()
        {
            throw new NotImplementedException("Audience Group APIs are not yet implemented. Please refer to LINE API documentation.");
        }

        /// <summary>
        /// 變更受眾群組授權層級（目前未實作）
        /// Changes audience group authority level (currently not implemented)
        /// </summary>
        public virtual Task ChangeAudienceGroupAuthorityLevelAsync(string authorityLevel)
        {
            throw new NotImplementedException("Audience Group APIs are not yet implemented. Please refer to LINE API documentation.");
        }
        #endregion

        #region Followers (custom extension)
        /// <summary>
        /// 取得關注者ID (輔助方法 - 非官方 API)
        /// Gets follower IDs (helper method - not official API)
        /// </summary>
        public virtual async Task<IList<string>> GetFollowersAsync()
        {
            // Note: This is a custom implementation
            // The actual implementation depends on your requirements
            return new List<string>();
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// HTTP GET 輔助方法
        /// HTTP GET helper method
        /// </summary>
        private async Task<string> GetStringAsync(string url)
        {
            var response = await _client.GetAsync(url).ConfigureAwait(false);

            // Enhanced error handling for authentication issues
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                throw new UnauthorizedAccessException(
                    $"LINE API authentication failed. Status: 401 Unauthorized. " +
                    $"Error: {errorContent}. " +
                    $"Please verify your Channel Access Token is valid and not expired. " +
                    $"URL: {url}");
            }

            await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        #endregion

        #region IDisposable
        /// <summary>
        /// 釋放資源
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            if (_disposeClient)
            {
                _client?.Dispose();
            }
        }
        #endregion
    }
}

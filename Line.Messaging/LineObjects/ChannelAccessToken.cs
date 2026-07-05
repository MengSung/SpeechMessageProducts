// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/LineObjects/ChannelAccessToken.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class ChannelAccessToken、class ChannelAccessTokenKeyIds、class StatelessChannelAccessTokenRequest
// 主要成員：AccessToken、ExpiresIn、TokenType、KeyId、Kids、ClientId、ClientSecret、GrantType、ClientAssertionType、ClientAssertion
// 引用命名空間：Newtonsoft.Json、System.Collections.Generic
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Newtonsoft.Json;
using System.Collections.Generic;

namespace Line.Messaging
{
    /// <summary>
    /// Channel access token response
    /// https://developers.line.biz/en/reference/messaging-api/#issue-channel-access-token-v2-1
    /// </summary>
    public class ChannelAccessToken
    {
        /// <summary>
        /// Channel access token.
        /// Valid for 30 days (v2.1) or as stateless token (v3)
        /// </summary>
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        /// <summary>
        /// Time until channel access token expires in seconds from time the token is issued
        /// </summary>
        [JsonProperty("expires_in")]
        public long ExpiresIn { get; set; }

        /// <summary>
        /// Bearer
        /// </summary>
        [JsonProperty("token_type")]
        public string TokenType { get; set; } = "Bearer";

        /// <summary>
        /// Token key ID. Returned only for v2.1 tokens.
        /// Used to revoke the channel access token.
        /// </summary>
        [JsonProperty("key_id")]
        public string KeyId { get; set; }
    }

    /// <summary>
    /// Channel access token key ID list (v2.1)
    /// https://developers.line.biz/en/reference/messaging-api/#get-all-valid-channel-access-token-key-ids-v2-1
    /// </summary>
    public class ChannelAccessTokenKeyIds
    {
        /// <summary>
        /// List of key IDs
        /// </summary>
        [JsonProperty("kids")]
        public List<string> Kids { get; set; }
    }

    /// <summary>
    /// Stateless channel access token request (v3)
    /// https://developers.line.biz/en/reference/messaging-api/#issue-stateless-channel-access-token
    /// </summary>
    public class StatelessChannelAccessTokenRequest
    {
        /// <summary>
        /// Channel ID
        /// </summary>
        [JsonProperty("client_id")]
        public string ClientId { get; set; }

        /// <summary>
        /// Channel secret
        /// </summary>
        [JsonProperty("client_secret")]
        public string ClientSecret { get; set; }

        /// <summary>
        /// OAuth grant type value
        /// </summary>
        [JsonProperty("grant_type")]
        public string GrantType { get; } = "client_credentials";

        /// <summary>
        /// Client assertion type (Optional). Typically: "urn:ietf:params:oauth:client-assertion-type:jwt-bearer"
        /// If specified, token is generated with reference to the short-lived channel access token.
        /// </summary>
        [JsonProperty("client_assertion_type")]
        public string ClientAssertionType { get; set; }

        /// <summary>
        /// JWT (JSON Web Token) (Optional)
        /// Required if client_assertion_type is specified
        /// </summary>
        [JsonProperty("client_assertion")]
        public string ClientAssertion { get; set; }
    }
}

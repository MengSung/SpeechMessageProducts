/*
 * 檔案整體說明
 *
 * 目的與定位：本檔承載頻道存取token與有效期限等授權回應；物件含機密，不能當一般診斷資料或跨頻道共用結果。
 *
 * 主要功能與流程：
 * 本檔宣告 ChannelAccessToken、ChannelAccessTokenKeyIds、StatelessChannelAccessTokenRequest。
 * 資料／選項欄位包含 ChannelAccessToken、AccessToken、ExpiresIn、TokenType、KeyId、ChannelAccessTokenKeyIds、Kids、
 * StatelessChannelAccessTokenRequest、ClientId、ClientSecret、GrantType、ClientAssertionType、ClientAssertion，名稱、
 * 型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 * 可沿 Line.Messaging.Tests/LineMessagingProcessorCredentialTests.cs、Line.Messaging/LineMessagingClient.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
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

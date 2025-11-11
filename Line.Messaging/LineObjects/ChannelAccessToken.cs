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

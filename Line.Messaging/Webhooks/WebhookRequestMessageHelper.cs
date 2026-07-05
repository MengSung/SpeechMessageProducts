// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Webhooks/WebhookRequestMessageHelper.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class WebhookRequestMessageHelper
// 主要成員：GetWebhookEventsAsync、VerifySignature、SlowEquals
// 引用命名空間：Newtonsoft.Json、System、System.Collections.Generic、System.Linq、System.Net.Http、System.Security.Cryptography、System.Text、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
#if !NETSTANDARD1_4
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Line.Messaging.Webhooks
{
    public static class WebhookRequestMessageHelper
    {
        /// <summary>
        /// Verify if the request is valid, then returns LINE Webhook events from the request
        /// </summary>
        /// <param name="request">HttpRequestMessage</param>
        /// <param name="channelSecret">ChannelSecret</param>
        /// <param name="botUserId">BotUserId</param>
        /// <returns>List of WebhookEvent</returns>
        public static async Task<IEnumerable<WebhookEvent>> GetWebhookEventsAsync(this HttpRequestMessage request, string channelSecret, string botUserId = null)
        {
            if (request == null) { throw new ArgumentNullException(nameof(request)); }
            if (channelSecret == null) { throw new ArgumentNullException(nameof(channelSecret)); }

            var content = await request.Content.ReadAsStringAsync();

            var xLineSignature = request.Headers.GetValues("X-Line-Signature").FirstOrDefault();
            if (string.IsNullOrEmpty(xLineSignature) || !VerifySignature(channelSecret, xLineSignature, content))
            {
                throw new InvalidSignatureException("Signature validation faild.");
            }

            dynamic json = JsonConvert.DeserializeObject(content);

            if (!string.IsNullOrEmpty(botUserId))
            {
                if(botUserId != (string)json.destination)
                {
                    throw new UserIdMismatchException("Bot user ID does not match.");
                }
            }
            return WebhookEventParser.ParseEvents(json.events);
        }

        /// <summary>
        /// The signature in the X-Line-Signature request header must be verified to confirm that the request was sent from the LINE Platform.
        /// Authentication is performed as follows.
        /// 1. With the channel secret as the secret key, your application retrieves the digest value in the request body created using the HMAC-SHA256 algorithm.
        /// 2. The server confirms that the signature in the request header matches the digest value which is Base64 encoded
        /// https://developers.line.me/en/docs/messaging-api/reference/#signature-validation
        /// </summary>
        /// <param name="channelSecret">ChannelSecret</param>
        /// <param name="xLineSignature">X-Line-Signature header</param>
        /// <param name="requestBody">RequestBody</param>
        /// <returns></returns>
        internal static bool VerifySignature(string channelSecret, string xLineSignature, string requestBody)
        {
            try
            {
                var key = Encoding.UTF8.GetBytes(channelSecret);
                var body = Encoding.UTF8.GetBytes(requestBody);

                using (HMACSHA256 hmac = new HMACSHA256(key))
                {
                    var hash = hmac.ComputeHash(body, 0, body.Length);
                    var xLineBytes = Convert.FromBase64String(xLineSignature);
                    return SlowEquals(xLineBytes, hash);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Compares two-byte arrays in length-constant time.
        /// This comparison method is used so that password hashes cannot be extracted from on-line systems using a timing attack and then attacked off-line.
        /// <para> http://bryanavery.co.uk/cryptography-net-avoiding-timing-attack/#comment-85　</para>
        /// </summary>
        private static bool SlowEquals(byte[] a, byte[] b)
        {
            uint diff = (uint)a.Length ^ (uint)b.Length;
            for (int i = 0; i < a.Length && i < b.Length; i++)
                diff |= (uint)(a[i] ^ b[i]);
            return diff == 0;
        }
    }
}
#endif
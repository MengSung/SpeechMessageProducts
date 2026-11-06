/*
 * 檔案整體說明
 *
 * 目的與定位：本檔承接以下操作邊界：先對傳輸收到的原始 body 位元組驗證 HMAC，再以 UTF-8 解析同一份內容；不受 Content-Type charset 影響。 body 與解析字串僅存活於本次呼叫，
 * request 及其 content 仍由呼叫端擁有，本方法不保存跨請求狀態。
 *
 * 主要功能與流程：
 * 本檔宣告 WebhookRequestMessageHelper。
 * - 先對傳輸收到的原始 body 位元組驗證 HMAC，再以 UTF-8 解析同一份內容；不受 Content-Type charset 影響。 body 與解析字串僅存活於本次呼叫，request 及其 content 
 * 仍由呼叫端擁有，本方法不保存跨請求狀態。
 * 主要操作入口包含 GetWebhookEventsAsync。
 *
 * 使用邊界與維護：
 * 協定模型與產品身分／業務授權分離；欄位或列舉值變更須同步檢查JSON相容性、解析分派及雙目標測試。
 * 可變模型由建立它的操作持有；事件來源、reply token及profile不得被當作跨使用者共用的可變狀態。
 */
#if !NETSTANDARD1_4
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Line.Messaging.Webhooks
{
    public static class WebhookRequestMessageHelper
    {
        /// <summary>
        /// 先對傳輸收到的原始 body 位元組驗證 HMAC，再以 UTF-8 解析同一份內容；不受 Content-Type charset 影響。
        /// body 與解析字串僅存活於本次呼叫，request 及其 content 仍由呼叫端擁有，本方法不保存跨請求狀態。
        /// </summary>
        /// <param name="request">由呼叫端擁有且不可在處理期間並行修改的 HTTP request。</param>
        /// <param name="channelSecret">伺服器端已授權 channel 的密鑰。</param>
        /// <param name="botUserId">選用的預期 bot ID；不匹配時 fail closed。</param>
        /// <returns>此次 request 的已解析事件集合。</returns>
        public static async Task<IEnumerable<WebhookEvent>> GetWebhookEventsAsync(this HttpRequestMessage request, string channelSecret, string botUserId = null)
        {
            if (request == null) { throw new ArgumentNullException(nameof(request)); }
            if (channelSecret == null) { throw new ArgumentNullException(nameof(channelSecret)); }
            if (request.Content == null) { throw new ArgumentException("Webhook request content is required.", nameof(request)); }

            var body = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

            var xLineSignature = request.Headers.TryGetValues("X-Line-Signature", out var signatures)
                ? signatures.FirstOrDefault()
                : null;
            if (string.IsNullOrEmpty(xLineSignature) || !LineSignature.Verify(body, xLineSignature, channelSecret))
            {
                throw new InvalidSignatureException("Signature validation failed.");
            }

            // LINE body 是 UTF-8 JSON；忽略可被中介層錯誤設定的 Content-Type charset，並拒絕非法 UTF-8。
            var content = new UTF8Encoding(false, true).GetString(body);
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
    }
}
#endif

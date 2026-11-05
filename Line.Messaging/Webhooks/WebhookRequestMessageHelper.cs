// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/Webhooks/WebhookRequestMessageHelper.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class WebhookRequestMessageHelper
// 主要成員：GetWebhookEventsAsync
// 引用命名空間：Newtonsoft.Json、System、System.Collections.Generic、System.Linq、System.Net.Http、System.Text、System.Threading.Tasks
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

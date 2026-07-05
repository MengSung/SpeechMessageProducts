// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging/HttpResponseMessageExtensions.cs
// 所屬區塊：LINE Messaging SDK 封裝層，定義 LINE API DTO、Client 呼叫與訊息模型。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class HttpResponseMessageExtensions
// 主要成員：EnsureSuccessStatusCodeAsync
// 引用命名空間：Newtonsoft.Json、System.Net.Http、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Newtonsoft.Json;
using System.Net.Http;
using System.Threading.Tasks;

namespace Line.Messaging
{
    internal static class HttpResponseMessageExtensions
    {
        /// <summary>
        /// Validate the response status.
        /// </summary>
        /// <param name="response">HttpResponseMessage</param>
        /// <returns>HttpResponseMessage</returns>
        internal static async Task<HttpResponseMessage> EnsureSuccessStatusCodeAsync(this HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
            {
                return response;
            }
            else
            {
                ErrorResponseMessage errorMessage;
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                try
                {
                    errorMessage = JsonConvert.DeserializeObject<ErrorResponseMessage>(content, new CamelCaseJsonSerializerSettings());
                }
                catch
                {
                    errorMessage = new ErrorResponseMessage() { Message = content, Details = new ErrorResponseMessage.ErrorDetails[0] };
                }
                throw new LineResponseException(errorMessage.Message) { StatusCode = response.StatusCode, ResponseMessage = errorMessage };

            }
        }
    }
}

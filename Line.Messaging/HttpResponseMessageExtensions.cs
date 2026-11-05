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
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Line.Messaging
{
    internal static class HttpResponseMessageExtensions
    {
        /// <summary>
        /// 驗證遠端 HTTP 狀態。錯誤 body 是不受信任的遠端資料，可能帶入個資或憑證；
        /// 不放進 Exception.Message、公開 DTO 或 ToString，避免上層記錄錯誤時跨請求洩漏。
        /// </summary>
        /// <param name="response">由單次 API 呼叫擁有、稍後確定性 Dispose 的 response。</param>
        /// <returns>成功時原 response；失敗時拋只含 status code 的安全錯誤。</returns>
        internal static Task<HttpResponseMessage> EnsureSuccessStatusCodeAsync(this HttpResponseMessage response)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));
            if (response.IsSuccessStatusCode) return Task.FromResult(response);

            // 僅保留 HTTP 狀態分類；不解析、回顯或保存遠端錯誤 body。
            var safeError = new ErrorResponseMessage
            {
                Message = "LINE API request failed.",
                Details = Array.Empty<ErrorResponseMessage.ErrorDetails>()
            };
            throw new LineResponseException($"{safeError.Message} HTTP {(int)response.StatusCode}.")
            {
                StatusCode = response.StatusCode,
                ResponseMessage = safeError
            };
        }
    }
}

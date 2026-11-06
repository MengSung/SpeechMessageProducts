/*
 * 檔案整體說明
 *
 * 目的與定位：本檔承接以下操作邊界：驗證遠端 HTTP 狀態。錯誤 body 是不受信任的遠端資料，可能帶入個資或憑證； 不放進 Exception.Message、公開 DTO 或 ToString，
 * 避免上層記錄錯誤時跨請求洩漏。
 *
 * 主要功能與流程：
 * 本檔宣告 HttpResponseMessageExtensions。
 * - 驗證遠端 HTTP 狀態。錯誤 body 是不受信任的遠端資料，可能帶入個資或憑證； 不放進 Exception.Message、公開 DTO 或 ToString，避免上層記錄錯誤時跨請求洩漏。
 * 主要操作入口包含 EnsureSuccessStatusCodeAsync。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
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

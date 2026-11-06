/*
 * 檔案整體說明
 *
 * 目的與定位：Messaging 與 LIFF 共用的單一 channel 傳輸與 request 所有權實作。 credential 僅屬此實例且只附加於當次 request，不修改共用 HttpClient 的預設標頭。 
 * 不建立 queue、timer 或背景 task；借入 transport 必須由組合根設定有限逾時、無使用者 cookie， 並先等待全部操作結束再 Dispose SDK。回應唯一交回呼叫方法 using 或 
 * ContentStream。
 *
 * 主要功能與流程：
 * 本檔宣告 ChannelHttpTransport。
 * - 借入由主機管理的 transport；不取得其 Dispose 責任，也不保存 request 或 response。
 * - 相容舊 API 的自有 transport；先驗證 header 再配置 client，避免建構失敗留下無 owner 的資源。
 * - 接管當次 request，等待 handler 完成後確定性釋放 request/content；沒有延遲或背景重用。 自訂 handler 不得在回傳 response 後繼續存取 request。response 
 * 由上層擁有，不由此處提前關閉。
 * - 冪等禁止新操作並清掉 credential；不關閉借入 client，不主動遺棄仍在進行的作業。 呼叫端須依公開 owner 契約先排空，再結束本實例；自有 client 才由此方法關閉。
 * 主要操作入口包含 SendAsync、Dispose。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 Line.Messaging/Liff/LiffClient.cs、Line.Messaging/LineMessagingClient.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Line.Messaging;

/// <summary>
/// Messaging 與 LIFF 共用的單一 channel 傳輸與 request 所有權實作。
/// credential 僅屬此實例且只附加於當次 request，不修改共用 HttpClient 的預設標頭。
/// 不建立 queue、timer 或背景 task；借入 transport 必須由組合根設定有限逾時、無使用者 cookie，
/// 並先等待全部操作結束再 Dispose SDK。回應唯一交回呼叫方法 using 或 ContentStream。
/// </summary>
internal sealed class ChannelHttpTransport : IDisposable
{
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private AuthenticationHeaderValue authorization;

    /// <summary>借入由主機管理的 transport；不取得其 Dispose 責任，也不保存 request 或 response。</summary>
    /// <param name="client">主機擁有且不含其他使用者預設狀態的 transport。</param>
    /// <param name="token">伺服器端已驗證 channel 的 credential，存活至本實例 Dispose。</param>
    internal ChannelHttpTransport(HttpClient client, string token)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>相容舊 API 的自有 transport；先驗證 header 再配置 client，避免建構失敗留下無 owner 的資源。</summary>
    /// <param name="token">此實例獨享、Dispose 時清除參照的 channel credential。</param>
    internal ChannelHttpTransport(string token)
    {
        authorization = new AuthenticationHeaderValue("Bearer", token);
        client = new HttpClient();
        ownsClient = true;
    }

    /// <summary>
    /// 接管當次 request，等待 handler 完成後確定性釋放 request/content；沒有延遲或背景重用。
    /// 自訂 handler 不得在回傳 response 後繼續存取 request。response 由上層擁有，不由此處提前關閉。
    /// </summary>
    /// <param name="request">只可傳送一次且所有權轉交本方法的 request。</param>
    /// <param name="completion">一般 API 讀完整 body；媒體使用 ResponseHeadersRead 交給串流 owner。</param>
    /// <returns>呼叫方法必須確定性處置或轉交唯一 owner 的 response。</returns>
    internal async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        using (request)
        {
            var credential = Volatile.Read(ref authorization);
            if (credential == null) throw new ObjectDisposedException(nameof(ChannelHttpTransport));
            request.Headers.Authorization = credential;
            return await client.SendAsync(request, completion).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 冪等禁止新操作並清掉 credential；不關閉借入 client，不主動遺棄仍在進行的作業。
    /// 呼叫端須依公開 owner 契約先排空，再結束本實例；自有 client 才由此方法關閉。
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref authorization, null) == null) return;
        if (ownsClient) client.Dispose();
    }
}

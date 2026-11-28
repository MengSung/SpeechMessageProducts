/*
檔案整體說明
目的與定位：F11 好牧人主機的單批 HTTP 觀測接縫，供既有 F5 唯一發送出口使用；不承擔授權、重試或發送紀錄。
主要功能與流程：傳輸前取得單次計數票據；沒有 response 的故障由 handler 歸還，成功則把票據移交給包裝 content。讀完內容不提早扣數，直到 response／content Dispose 完成內層 stream 清理才歸還，涵蓋 F5 判讀與所有例外路徑。
使用邊界與維護：Meter 為借用的 Host owner；handler 及內層 transport 由同一 HttpClient 確定釋放，不保存 request、credential 或租戶資料。每批最多一張票據與一層 content，沒有額外 buffer、timer 或背景工作；caller 取消原樣傳播，重試仍由 F5 決定。
*/
using System.Net;
using SpeechMessage.Hosting.Observability;

namespace SpeechMessageProducts.GoodShepherd2.Web.Observability;

/// <summary>由單一產品 HttpClient 擁有的觀測管線，回應內容是成功傳輸後票據的唯一 owner。</summary>
internal sealed class PushBatchTrackingHandler(HostingMeters meters, HttpMessageHandler inner) : DelegatingHandler(inner)
{
    /// <summary>只借用目前 Host 的固定計數器，不接受 request 指定另一個 owner。</summary>
    private readonly HostingMeters meters = meters;

    /// <summary>票據移交前的故障在本方法清理；移交後直到 content 關閉才歸還，不持有 response 以外的操作資料。</summary>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        IDisposable? observation = meters.TrackPushBatch();
        HttpResponseMessage? response = null;
        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.Content = new ObservedContent(response.Content, observation);
            observation = null;
            return response;
        }
        catch
        {
            response?.Dispose();
            throw;
        }
        finally { observation?.Dispose(); }
    }

    /// <summary>只委派內容讀取，不複製 body；唯一清理先完成內層資源，再扣回單批計數，重入無副作用。</summary>
    private sealed class ObservedContent : HttpContent
    {
        private HttpContent? content;
        private IDisposable? observation;
        private readonly long? sourceLength;

        /// <summary>建構時複製無狀態的 HTTP headers；失敗時票據仍由外層 handler 擁有，不提早接管。</summary>
        internal ObservedContent(HttpContent content, IDisposable observation)
        {
            // 只讀來源的長度；讀取本包裝的 ContentLength 會回呼 TryComputeLength 而無限遞迴。
            sourceLength = content.Headers.ContentLength;
            foreach (var header in content.Headers) Headers.TryAddWithoutValidation(header.Key, header.Value);
            this.content = content;
            this.observation = observation;
        }

        /// <summary>以 framework 的取消感知複製委派 body，沒有自己的緩衝區或非同步背景讀取。</summary>
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            GetContent().CopyToAsync(stream, context);

        /// <summary>保留同一 caller token，不把中止後的讀取留在背景執行。</summary>
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
            GetContent().CopyToAsync(stream, context, cancellationToken);

        /// <summary>使用內層原 stream，仍由內層 HttpContent 持有及在 Dispose 時釋放。</summary>
        protected override Task<Stream> CreateContentReadStreamAsync() => GetContent().ReadAsStreamAsync();

        /// <summary>有取消能力的 stream 取得沿原 token 委派；不建立第二個 stream owner。</summary>
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
            GetContent().ReadAsStreamAsync(cancellationToken);

        /// <summary>只有來源公開精確長度時才回報，否則交 HttpClient 原有串流政策，不為量測讀取 body。</summary>
        protected override bool TryComputeLength(out long length)
        {
            length = sourceLength ?? 0;
            return sourceLength.HasValue;
        }

        /// <summary>原子解除唯一內容 owner；內層清理即使拋錯仍歸還票據，基底 buffer 亦於 finally 清理。</summary>
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    var owned = Interlocked.Exchange(ref content, null);
                    if (owned is null) return;
                    try { owned.Dispose(); }
                    finally { Interlocked.Exchange(ref observation, null)?.Dispose(); }
                }
            }
            finally { base.Dispose(disposing); }
        }

        /// <summary>關閉後拒絕新讀取，不重新取得另一批 response。</summary>
        private HttpContent GetContent() => Volatile.Read(ref content) ?? throw new ObjectDisposedException(nameof(ObservedContent));
    }
}

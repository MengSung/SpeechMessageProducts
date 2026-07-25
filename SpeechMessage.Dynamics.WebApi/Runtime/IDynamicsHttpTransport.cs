// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Runtime/IDynamicsHttpTransport.cs
// 目的：抽象出 profile 級 HTTP 傳輸層，方便測試注入 fake handler。
//
// 保母教學：
// - 一個 profile runtime 擁有一個長壽命 transport。
// - 不要每個 request new HttpClient。
// - 不要快取 per-user cookie / session。
// ============================================================================

namespace SpeechMessage.Dynamics.WebApi.Runtime;

/// <summary>
/// Dynamics Web API HTTP 傳輸介面。
/// </summary>
public interface IDynamicsHttpTransport : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// 發送一個請求。呼叫端負責建立/釋放 HttpRequestMessage。
    /// </summary>
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}

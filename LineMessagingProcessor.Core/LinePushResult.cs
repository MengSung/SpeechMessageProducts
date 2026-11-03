using System;
using System.Globalization;
using System.Net;
using Newtonsoft.Json;

namespace LineMessagingProcessor.Core;

/// <summary>
/// 單次呼叫的不可變結果，只保存分類與必要診斷值，不持有 HTTP 訊息或憑證。
/// 傳輸例外保留原始實例供最終邊界上報；不得直接序列化或輸出其 Message／ToString。
/// 呼叫端消費後應放棄結果參考，Core 不在 static 或快取保存它。
/// </summary>
public sealed class LinePushResult
{
    /// <summary>只允許傳輸實作建立一致結果；有 HTTP 狀態與無回應故障為互斥路徑。</summary>
    /// <param name="outcome">已分類結果。</param>
    /// <param name="statusCode">有回應才提供的 HTTP 狀態。</param>
    /// <param name="requestId">有回應才提供的平台診斷 ID。</param>
    /// <param name="transportException">只在未收到回應的故障傳入原始實例。</param>
    internal LinePushResult(LinePushOutcome outcome, HttpStatusCode? statusCode, string? requestId, Exception? transportException)
    {
        Outcome = outcome;
        StatusCode = statusCode;
        RequestId = requestId;
        TransportException = transportException;
    }

    /// <summary>取得 HTTP／傳輸分類，不把可恢復失敗當作成功。</summary>
    public LinePushOutcome Outcome { get; }

    /// <summary>只有平台接受或同鍵已接受才成功，Rejected 不在本階段產生。</summary>
    public bool Success => Outcome is LinePushOutcome.Success or LinePushOutcome.AlreadyAccepted;

    /// <summary>收到回應時的狀態碼；傳輸失敗沒有 HTTP 狀態碼。</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>平台 x-line-request-id；不讀取或保存可能含敏感資訊的錯誤回應本文。</summary>
    public string? RequestId { get; }

    /// <summary>僅無回應的傳輸失敗保留原始例外；有 HTTP 狀態的結果必須為 null。</summary>
    [JsonIgnore]
    public Exception? TransportException { get; }

    /// <summary>只輸出固定格式的分類與數字狀態碼，不包含 ID、例外本文或任何請求內容。</summary>
    /// <returns>可安全用於基本診斷的摘要。</returns>
    public override string ToString() => "LinePushResult(" + Outcome + ", " +
        (StatusCode.HasValue ? ((int)StatusCode.Value).ToString(CultureInfo.InvariantCulture) : "no-response") + ")";
}

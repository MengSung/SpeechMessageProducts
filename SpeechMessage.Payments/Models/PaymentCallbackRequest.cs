namespace SpeechMessage.Payments.Models;

/// <summary>
/// 產品層將 web request 攤平成這個 callback DTO 後交給金流核心。
/// 核心因此不需要參考 ASP.NET web runtime 型別，保持可被其他產品重用。
/// </summary>
public sealed record PaymentCallbackRequest
{
    public string ProfileName { get; init; } = string.Empty;
    public PaymentProviderKind? ProviderHint { get; init; }
    public string HttpMethod { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    // RawBody 保留給 JSON 或 form-urlencoded callback parser；產品層讀取後必須 rewind request body。
    public string RawBody { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Query { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Form { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
}

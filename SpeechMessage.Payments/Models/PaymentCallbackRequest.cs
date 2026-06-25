namespace SpeechMessage.Payments.Models;

public sealed record PaymentCallbackRequest
{
    public string ProfileName { get; init; } = string.Empty;
    public PaymentProviderKind? ProviderHint { get; init; }
    public string HttpMethod { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public string RawBody { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Query { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Form { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
}

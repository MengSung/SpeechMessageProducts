using Newtonsoft.Json;

namespace SpeechMessage.Payments.Providers.MyPay;

internal sealed record MyPayCreatePayload
{
    [JsonProperty("store_uid")]
    public string StoreUid { get; init; } = string.Empty;

    [JsonProperty("order_id")]
    public string OrderId { get; init; } = string.Empty;

    [JsonProperty("cost")]
    public string Cost { get; init; } = string.Empty;

    [JsonProperty("currency")]
    public string Currency { get; init; } = "TWD";

    [JsonProperty("product_name")]
    public string ProductName { get; init; } = string.Empty;

    [JsonProperty("pfn")]
    public string PaymentMethod { get; init; } = string.Empty;

    [JsonProperty("user_name")]
    public string UserName { get; init; } = string.Empty;

    [JsonProperty("user_email")]
    public string UserEmail { get; init; } = string.Empty;

    [JsonProperty("user_phone")]
    public string UserPhone { get; init; } = string.Empty;

    [JsonProperty("success_returl")]
    public string SuccessReturnUrl { get; init; } = string.Empty;

    [JsonProperty("failure_returl")]
    public string FailureReturnUrl { get; init; } = string.Empty;

    [JsonProperty("notify_url")]
    public string NotifyUrl { get; init; } = string.Empty;
}

internal sealed record MyPayServicePayload
{
    [JsonProperty("service_name")]
    public string ServiceName { get; init; } = "api";

    [JsonProperty("cmd")]
    public string Command { get; init; } = "api/orders";
}

internal sealed record MyPayCreateResponse
{
    [JsonProperty("code")]
    public string Code { get; init; } = string.Empty;

    [JsonProperty("msg")]
    public string Message { get; init; } = string.Empty;

    [JsonProperty("uid")]
    public string Uid { get; init; } = string.Empty;

    [JsonProperty("key")]
    public string Key { get; init; } = string.Empty;

    [JsonProperty("url")]
    public string Url { get; init; } = string.Empty;
}

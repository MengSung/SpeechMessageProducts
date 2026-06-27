using Newtonsoft.Json;

namespace SpeechMessage.Payments.Providers.MyPay;

/// <summary>
/// MyPay api/orders 加密 payload。
/// 這些型別保持 internal，避免 MyPay 欄位名稱外漏成通用 API 的一部分。
/// </summary>
internal sealed record MyPayCreatePayload
{
    [JsonProperty("store_uid")]
    public string StoreUid { get; init; } = string.Empty;

    [JsonProperty("order_id")]
    public string OrderId { get; init; } = string.Empty;

    [JsonProperty("cost")]
    public string Cost { get; init; } = string.Empty;

    [JsonProperty("items")]
    public IReadOnlyList<MyPayCreateItemPayload> Items { get; init; } = Array.Empty<MyPayCreateItemPayload>();

    [JsonProperty("user_id")]
    public string UserId { get; init; } = string.Empty;

    [JsonProperty("ip")]
    public string Ip { get; init; } = string.Empty;

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

internal sealed record MyPayCreateItemPayload
{
    [JsonProperty("id")]
    public string Id { get; init; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; init; } = string.Empty;

    [JsonProperty("cost")]
    public string Cost { get; init; } = string.Empty;

    [JsonProperty("amount")]
    public string Amount { get; init; } = "1";

    [JsonProperty("total")]
    public string Total { get; init; } = string.Empty;
}

internal sealed record MyPayServicePayload
{
    // MyPay service payload 也需要加密；目前建單固定呼叫 api/orders。
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

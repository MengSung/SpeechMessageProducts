// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments/Providers/MyPay/MyPayModels.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：record MyPayCreatePayload、record MyPayCreateItemPayload、record MyPayServicePayload、record MyPayCreateResponse
// 主要成員：StoreUid、OrderId、Cost、Items、UserId、Ip、Currency、ProductName、PaymentMethod、UserName
// 引用命名空間：Newtonsoft.Json
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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

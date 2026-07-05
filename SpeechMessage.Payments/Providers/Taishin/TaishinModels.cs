// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments/Providers/Taishin/TaishinModels.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class TaishinPaymentRequest、class TaishinPaymentParams、class TaishinCardholderMobilePhone、class TaishinApiResponse、class TaishinApiResponseParams
// 主要成員：Sender、Version、Mid、Tid、PayType、TxType、Params、Layout、OrderNo、Amt
// 引用命名空間：Newtonsoft.Json
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Newtonsoft.Json;

namespace SpeechMessage.Payments.Providers.Taishin;

/// <summary>
/// 台新 TSPG REST API 內部 DTO。
/// 這些欄位名稱是 provider contract，不作為 SpeechMessage.Payments 的公開模型。
/// </summary>
internal sealed class TaishinPaymentRequest
{
    [JsonProperty("sender")]
    public string Sender { get; set; } = "rest";

    [JsonProperty("ver")]
    public string Version { get; set; } = "1.0.0";

    [JsonProperty("mid")]
    public string Mid { get; set; } = string.Empty;

    [JsonProperty("tid")]
    public string Tid { get; set; } = string.Empty;

    [JsonProperty("pay_type")]
    public int PayType { get; set; } = 1;

    [JsonProperty("tx_type")]
    public int TxType { get; set; } = 1;

    [JsonProperty("params")]
    public TaishinPaymentParams Params { get; set; } = new();
}

internal sealed class TaishinPaymentParams
{
    [JsonProperty("layout")]
    public string Layout { get; set; } = "1";

    [JsonProperty("order_no")]
    public string OrderNo { get; set; } = string.Empty;

    [JsonProperty("amt")]
    public string Amt { get; set; } = string.Empty;

    [JsonProperty("cur")]
    public string Cur { get; set; } = "NTD";

    [JsonProperty("order_desc")]
    public string OrderDesc { get; set; } = string.Empty;

    [JsonProperty("capt_flag")]
    public string CaptFlag { get; set; } = "0";

    [JsonProperty("result_flag")]
    public string ResultFlag { get; set; } = "1";

    [JsonProperty("post_back_url")]
    public string PostBackUrl { get; set; } = string.Empty;

    [JsonProperty("result_url")]
    public string ResultUrl { get; set; } = string.Empty;

    [JsonProperty("cardholder_name")]
    public string CardholderName { get; set; } = string.Empty;

    [JsonProperty("cardholder_email")]
    public string CardholderEmail { get; set; } = string.Empty;

    [JsonProperty("cardholder_mobile_phone")]
    public TaishinCardholderMobilePhone? CardholderMobilePhone { get; set; }
}

internal sealed class TaishinCardholderMobilePhone
{
    [JsonProperty("country_code")]
    public string CountryCode { get; set; } = "886";

    [JsonProperty("phone_number")]
    public string PhoneNumber { get; set; } = string.Empty;
}

internal sealed class TaishinApiResponse
{
    [JsonProperty("ret_code")]
    public string RetCode { get; set; } = string.Empty;

    [JsonProperty("ret_msg")]
    public string RetMessage { get; set; } = string.Empty;

    [JsonProperty("order_no")]
    public string OrderNo { get; set; } = string.Empty;

    [JsonProperty("params")]
    public TaishinApiResponseParams? Params { get; set; }
}

internal sealed class TaishinApiResponseParams
{
    [JsonProperty("ret_code")]
    public string RetCode { get; set; } = string.Empty;

    [JsonProperty("ret_msg")]
    public string RetMessage { get; set; } = string.Empty;

    [JsonProperty("hpp_url")]
    public string PaymentPageUrl { get; set; } = string.Empty;

    [JsonProperty("transaction_id")]
    public string TransactionId { get; set; } = string.Empty;

    [JsonProperty("ORDERNO")]
    public string OrderNoUpper { get; set; } = string.Empty;

    [JsonProperty("order_no")]
    public string OrderNo { get; set; } = string.Empty;

    [JsonProperty("amt")]
    public string Amount { get; set; } = string.Empty;

    [JsonProperty("cur")]
    public string Currency { get; set; } = string.Empty;
}

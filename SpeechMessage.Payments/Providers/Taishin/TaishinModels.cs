/*
 * 檔案整體說明
 *
 * 目的與定位：本檔整合下列相關契約：TaishinPaymentRequest：台新 TSPG REST API 內部 DTO。 這些欄位名稱是 provider contract，不作為
 * SpeechMessage.Payments 的公開模型。
 *
 * 主要功能與流程：
 * 本檔宣告 TaishinPaymentRequest、TaishinPaymentParams、TaishinCardholderMobilePhone、TaishinApiResponse、
 * TaishinApiResponseParams。
 * 資料／選項欄位包含 TaishinPaymentRequest、Sender、Version、Mid、Tid、PayType、TxType、Params、TaishinPaymentParams、Layout、
 * OrderNo、Amt、Cur、OrderDesc、CaptFlag、ResultFlag，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Payments/Providers/Taishin/TaishinPaymentProvider.cs、
 * SpeechMessage.Payments/Providers/Taishin/TaishinRequestMapper.cs 追查目前工作樹中的合作／使用位置。
 */
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

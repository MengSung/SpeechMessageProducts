using Newtonsoft.Json;

namespace SpeechMessage.Payments.Providers.Taishin;

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

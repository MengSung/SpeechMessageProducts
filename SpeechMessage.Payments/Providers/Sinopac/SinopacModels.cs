// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments/Providers/Sinopac/SinopacModels.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class SinopacSignExcludeAttribute、enum SinopacApiService、interface ISinopacRequest、class SinopacNonceRequest、class SinopacNonceResponse、class SinopacWebApiMessage、class SinopacOrderCreateRequest、class SinopacOrderCreateAtmRequest
// 主要成員：ShopNo、Nonce、Version、APIService、Sign、Message、OrderNo、Amount、CurrencyID、PayType
// 引用命名空間：System.ComponentModel、System.Runtime.Serialization
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System.ComponentModel;
using System.Runtime.Serialization;

namespace SpeechMessage.Payments.Providers.Sinopac;

/// <summary>
/// 永豐 QPay provider 內部 DTO。
/// 這些型別刻意不公開給宿主產品，避免 provider 欄位再次成為產品層合約。
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class SinopacSignExcludeAttribute : Attribute
{
}

internal enum SinopacApiService
{
    OrderCreate = 1,
    OrderPayQuery = 5
}

internal interface ISinopacRequest
{
    string ShopNo { get; set; }
}

[DataContract]
internal sealed class SinopacNonceRequest
{
    public SinopacNonceRequest(string shopNo)
    {
        ShopNo = shopNo;
    }

    [DataMember]
    public string ShopNo { get; set; }
}

[DataContract]
internal sealed class SinopacNonceResponse
{
    [DataMember]
    public string Nonce { get; set; } = string.Empty;
}

[DataContract]
internal sealed class SinopacWebApiMessage
{
    [DataMember]
    public string Version { get; set; } = string.Empty;

    [DataMember]
    public string ShopNo { get; set; } = string.Empty;

    [DataMember]
    public string APIService { get; set; } = string.Empty;

    [DataMember]
    [SinopacSignExclude]
    public string Sign { get; set; } = string.Empty;

    [DataMember]
    public string Nonce { get; set; } = string.Empty;

    [DataMember]
    public string Message { get; set; } = string.Empty;
}

[DataContract]
internal sealed class SinopacOrderCreateRequest : ISinopacRequest
{
    [DataMember]
    public string ShopNo { get; set; } = string.Empty;

    [DataMember]
    public string OrderNo { get; set; } = string.Empty;

    [DataMember]
    public int Amount { get; set; }

    [DataMember]
    public string CurrencyID { get; set; } = "TWD";

    [DataMember]
    public string PayType { get; set; } = string.Empty;

    [DataMember]
    public SinopacOrderCreateAtmRequest? ATMParam { get; set; }

    [DataMember]
    public SinopacOrderCreateCardRequest? CardParam { get; set; }

    [DataMember]
    public string PrdtName { get; set; } = string.Empty;

    [DataMember]
    public string ReturnURL { get; set; } = string.Empty;

    [DataMember]
    public string BackendURL { get; set; } = string.Empty;

    [DataMember]
    public string Memo { get; set; } = string.Empty;

    [DataMember]
    public string Param1 { get; set; } = string.Empty;

    [DataMember]
    public string Param2 { get; set; } = string.Empty;

    [DataMember]
    public string Param3 { get; set; } = string.Empty;
}

[DataContract]
internal sealed class SinopacOrderCreateAtmRequest
{
    [DataMember]
    public string ExpireDate { get; set; } = string.Empty;
}

[DataContract]
internal sealed class SinopacOrderCreateCardRequest
{
    [DataMember]
    public string AutoBilling { get; set; } = string.Empty;

    [DataMember]
    public int? ExpBillingDays { get; set; }

    [DataMember]
    public int? ExpMinutes { get; set; }

    [DataMember]
    public string PayTypeSub { get; set; } = string.Empty;

    [DataMember]
    public string Staging { get; set; } = string.Empty;

    [DataMember]
    public int? DeductTotalNum { get; set; }

    [DataMember]
    public string PeriodType { get; set; } = string.Empty;

    [DataMember]
    public int? DeductFreq { get; set; }

    [DataMember]
    public string CCToken { get; set; } = string.Empty;
}

[DataContract]
internal sealed class SinopacOrderPayQueryRequest : ISinopacRequest
{
    [DataMember]
    public string ShopNo { get; set; } = string.Empty;

    [DataMember]
    public string PayToken { get; set; } = string.Empty;
}

[DataContract]
internal sealed class SinopacOrderCreateResponse
{
    [DataMember]
    [DefaultValue("")]
    public string OrderNo { get; set; } = string.Empty;

    [DataMember]
    [DefaultValue("")]
    public string ShopNo { get; set; } = string.Empty;

    [DataMember]
    [DefaultValue("")]
    public string TSNo { get; set; } = string.Empty;

    [DataMember]
    [DefaultValue("")]
    public string PayType { get; set; } = string.Empty;

    [DataMember]
    public int Amount { get; set; }

    [DataMember]
    [DefaultValue("")]
    public string Status { get; set; } = string.Empty;

    [DataMember]
    [DefaultValue("")]
    public string Description { get; set; } = string.Empty;

    [DataMember]
    [DefaultValue("")]
    public string Param1 { get; set; } = string.Empty;

    [DataMember]
    [DefaultValue("")]
    public string Param2 { get; set; } = string.Empty;

    [DataMember]
    [DefaultValue("")]
    public string Param3 { get; set; } = string.Empty;

    [DataMember]
    public SinopacOrderCreateAtmResponse? ATMParam { get; set; }

    [DataMember]
    public SinopacOrderCreateCardResponse? CardParam { get; set; }

    [DataMember]
    public SinopacOrderCreateMobileResponse? MobileParam { get; set; }
}

[DataContract]
internal sealed class SinopacOrderCreateAtmResponse
{
    [DataMember]
    public string AtmPayNo { get; set; } = string.Empty;

    [DataMember]
    public string WebAtmURL { get; set; } = string.Empty;

    [DataMember]
    public string OtpURL { get; set; } = string.Empty;
}

[DataContract]
internal sealed class SinopacOrderCreateCardResponse
{
    [DataMember]
    public string CardPayURL { get; set; } = string.Empty;
}

[DataContract]
internal sealed class SinopacOrderCreateMobileResponse
{
    [DataMember]
    public string MobilePayURL { get; set; } = string.Empty;
}

[DataContract]
internal sealed class SinopacOrderPayResponse
{
    [DataMember]
    [DefaultValue("")]
    public string ShopNo { get; set; } = string.Empty;

    [DataMember]
    [DefaultValue("")]
    public string PayToken { get; set; } = string.Empty;

    [DataMember]
    [DefaultValue("")]
    public string Date { get; set; } = string.Empty;

    [DataMember]
    [DefaultValue("")]
    public string Status { get; set; } = string.Empty;

    [DataMember]
    [DefaultValue("")]
    public string Description { get; set; } = string.Empty;

    [DataMember]
    public SinopacTransactionResult? TSResultContent { get; set; }
}

[DataContract]
internal sealed class SinopacTransactionResult
{
    [DataMember]
    [DefaultValue("")]
    public string APType { get; set; } = string.Empty;

    [DataMember]
    [DefaultValue("")]
    public string TSNo { get; set; } = string.Empty;

    [DataMember]
    [DefaultValue("")]
    public string OrderNo { get; set; } = string.Empty;

    [DataMember]
    [DefaultValue("")]
    public string ShopNo { get; set; } = string.Empty;

    [DataMember]
    [DefaultValue("")]
    public string PayType { get; set; } = string.Empty;

    [DataMember]
    [DefaultValue("")]
    public string Amount { get; set; } = string.Empty;

    [DataMember]
    [DefaultValue("")]
    public string Status { get; set; } = string.Empty;

    [DataMember]
    [DefaultValue("")]
    public string Description { get; set; } = string.Empty;

    [DataMember]
    [DefaultValue("")]
    public string Param1 { get; set; } = string.Empty;

    [DataMember]
    [DefaultValue("")]
    public string Param2 { get; set; } = string.Empty;

    [DataMember]
    [DefaultValue("")]
    public string Param3 { get; set; } = string.Empty;

    [DataMember]
    [DefaultValue("")]
    public string LeftCCNo { get; set; } = string.Empty;

    [DataMember]
    [DefaultValue("")]
    public string RightCCNo { get; set; } = string.Empty;

    [DataMember]
    [DefaultValue("")]
    public string CCExpDate { get; set; } = string.Empty;

    [DataMember]
    [DefaultValue("")]
    public string CCToken { get; set; } = string.Empty;
}

/*
 * 檔案整體說明
 *
 * 目的與定位：本檔整合下列相關契約：SinopacSignExcludeAttribute：永豐 QPay provider 內部 DTO。 這些型別刻意不公開給宿主產品，避免 provider 欄位再次成為產品層合約。
 *
 * 主要功能與流程：
 * 本檔宣告 SinopacSignExcludeAttribute、SinopacApiService、ISinopacRequest、SinopacNonceRequest、SinopacNonceResponse、
 * SinopacWebApiMessage、SinopacOrderCreateRequest、SinopacOrderCreateAtmRequest、SinopacOrderCreateCardRequest、
 * SinopacOrderPayQueryRequest、SinopacOrderCreateResponse、SinopacOrderCreateAtmResponse 等相關型別。
 * 資料／選項欄位包含 SinopacApiService、ISinopacRequest、SinopacNonceRequest、ShopNo、SinopacNonceResponse、Nonce、
 * SinopacWebApiMessage、Version、APIService、Sign、Message、OrderNo、Amount、CurrencyID、PayType、ATMParam，名稱、
 * 型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Payments.Tests/Providers/Sinopac/SinopacProviderTests.cs、
 * SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs、
 * SpeechMessage.Payments/Providers/Sinopac/SinopacRequestMapper.cs、
 * SpeechMessage.Payments/Providers/Sinopac/SinopacSigner.cs、
 * SpeechMessage.Payments/Providers/Sinopac/SinopacStatusMapper.cs 追查目前工作樹中的合作／使用位置。
 */
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

/*
 * 檔案整體說明
 *
 * 目的與定位：本檔整合永豐 QPay provider 內部 DTO 與簽章排除標記，涵蓋建單、付款查詢及撥款查詢。
 * 這些型別刻意不公開給宿主產品，避免 provider 欄位再次成為產品層合約。
 *
 * 主要功能與流程：
 * 本檔宣告 SinopacSignExcludeAttribute、SinopacApiService、ISinopacRequest、SinopacNonceRequest、SinopacNonceResponse、
 * SinopacWebApiMessage、SinopacOrderCreateRequest、SinopacOrderCreateAtmRequest、SinopacOrderCreateCardRequest、
 * SinopacOrderPayQueryRequest、SinopacAllotQueryRequest、SinopacAllotQueryResponse、SinopacAllotMain、SinopacAllotDetail、
 * SinopacOrderCreateResponse、SinopacOrderCreateAtmResponse 等相關型別。
 * 資料／選項欄位包含 SinopacApiService、ISinopacRequest、SinopacNonceRequest、ShopNo、SinopacNonceResponse、Nonce、
 * SinopacWebApiMessage、Version、APIService、Sign、Message、OrderNo、Amount、CurrencyID、PayType、AllotDateS、AllotDateE、ATMParam，名稱、
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

/// <summary>永豐 QPay API 服務的固定數字代碼；封裝欄位必須輸出代碼而非列舉名稱。</summary>
internal enum SinopacApiService
{
    /// <summary>建單操作在永豐 QPay 協定中的固定服務代碼。</summary>
    OrderCreate = 1,

    /// <summary>付款狀態查詢操作在永豐 QPay 協定中的固定服務代碼。</summary>
    OrderPayQuery = 5,

    /// <summary>撥款檔查詢操作在永豐 QPay 協定中的固定服務代碼。</summary>
    AllotQuery = 7
}

/// <summary>永豐 QPay request DTO 的內部標記，供封裝流程取得商店編號。</summary>
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

/// <summary>永豐 AllotQuery 請求本文，欄位名稱與日期格式須維持舊協定格式。</summary>
[DataContract]
internal sealed class SinopacAllotQueryRequest : ISinopacRequest
{
    /// <summary>永豐 profile 指定的商店編號；不得由撥款查詢 payload 覆寫。</summary>
    [DataMember]
    public string ShopNo { get; set; } = string.Empty;

    /// <summary>撥款查詢起日，依永豐協定使用 yyyyMMdd 格式。</summary>
    [DataMember]
    public string AllotDateS { get; set; } = string.Empty;

    /// <summary>撥款查詢迄日，依永豐協定使用 yyyyMMdd 格式。</summary>
    [DataMember]
    public string AllotDateE { get; set; } = string.Empty;

    /// <summary>撥款付款類型，限 C、M 或 A。</summary>
    [DataMember]
    public string PayType { get; set; } = string.Empty;
}

/// <summary>永豐 AllotQuery 解密後 DTO；內容只有在外層簽章驗證成功後才能供中立結果映射。</summary>
[DataContract]
internal sealed class SinopacAllotQueryResponse
{
    /// <summary>簽章驗證後的商店編號。</summary>
    [DataMember]
    [DefaultValue("")]
    public string ShopNo { get; set; } = string.Empty;

    /// <summary>永豐回報的訊息時間欄位；只作 provider response DTO，不轉成查詢日期。</summary>
    [DataMember]
    [DefaultValue("")]
    public string Date { get; set; } = string.Empty;

    /// <summary>永豐撥款查詢處理狀態；只有成功狀態才可發布批次。</summary>
    [DataMember]
    [DefaultValue("")]
    public string Status { get; set; } = string.Empty;

    /// <summary>永豐對撥款查詢的狀態說明；只有整體回應通過簽章驗證後才對外提供。</summary>
    [DataMember]
    [DefaultValue("")]
    public string Description { get; set; } = string.Empty;

    /// <summary>永豐回傳的撥款批次；null 或空清單代表沒有批次資料。</summary>
    [DataMember]
    public List<SinopacAllotMain>? Allot { get; set; } = new();
}

/// <summary>永豐撥款批次主檔，包含批次日期、金額摘要與訂單明細。</summary>
[DataContract]
internal sealed class SinopacAllotMain
{
    /// <summary>provider 撥款批次識別。</summary>
    [DataMember]
    public string AllotNo { get; set; } = string.Empty;

    /// <summary>provider 以 yyyyMMdd 整數回報的批次撥款日。</summary>
    [DataMember]
    public int AllotDate { get; set; }

    /// <summary>該批次的付款方式代碼。</summary>
    [DataMember]
    public string PayType { get; set; } = string.Empty;

    /// <summary>provider 回報的主檔撥款金額，單位為整數最小貨幣單位。</summary>
    [DataMember]
    public decimal PayAmount { get; set; }

    /// <summary>provider 回報的主檔退款金額，單位為整數最小貨幣單位。</summary>
    [DataMember]
    public decimal RefundAmount { get; set; }

    /// <summary>provider 回報的主檔手續費，單位為整數最小貨幣單位。</summary>
    [DataMember]
    public decimal FeeAmount { get; set; }

    /// <summary>provider 回報的外部撥款金額，單位為整數最小貨幣單位。</summary>
    [DataMember]
    public decimal ExtAllotAmount { get; set; }

    /// <summary>provider 回報的主檔實際撥款金額，單位為整數最小貨幣單位。</summary>
    [DataMember]
    public decimal AllotAmount { get; set; }

    /// <summary>provider 回報的收款帳戶識別；本產品中立結果不暴露此欄位。</summary>
    [DataMember]
    public string Account { get; set; } = string.Empty;

    /// <summary>批次內訂單明細；轉換成公開結果前仍須通過外層 response 簽章驗證。</summary>
    [DataMember]
    public List<SinopacAllotDetail> List { get; set; } = new();
}

/// <summary>永豐撥款批次的單筆訂單明細，金額值保留 decimal 以避免解析時損失精度。</summary>
[DataContract]
internal sealed class SinopacAllotDetail
{
    /// <summary>provider 交易流水識別；目前中立明細不需要此值。</summary>
    [DataMember]
    public string TSNO { get; set; } = string.Empty;

    /// <summary>provider 回報的訂單編號，供產品 reconciliation 規則比對。</summary>
    [DataMember]
    public string OrderNO { get; set; } = string.Empty;

    /// <summary>provider 回報的付款日期字串；撥款查詢結果使用主檔 AllotDate。</summary>
    [DataMember]
    public string PayDate { get; set; } = string.Empty;

    /// <summary>provider 回報的明細金額，單位為整數最小貨幣單位。</summary>
    [DataMember]
    public decimal Amount { get; set; }

    /// <summary>provider 回報的明細手續費，單位為整數最小貨幣單位。</summary>
    [DataMember]
    public decimal Fee { get; set; }

    /// <summary>provider 回報的交易類型代碼。</summary>
    [DataMember]
    public string TSType { get; set; } = string.Empty;
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

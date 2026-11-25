/*
 * 檔案整體說明
 *
 * 目的與定位：本檔整合永豐 QPay provider 內部 DTO 與簽章排除標記，涵蓋綁卡、建單、訂單維護、付款／定期查詢及撥款查詢。
 * 這些型別刻意不公開給宿主產品，避免 provider 欄位再次成為產品層合約。
 *
 * 主要功能與流程：
 * 本檔宣告 provider 內部協定識別、request／response DTO、nested transaction 與定期扣款資料列。
 * DTO 分別承載 OrderCreate、OrderPayQuery、CardBindCreate、OrderMaintain、RegularQuery 與 AllotQuery；
 * 欄位名稱與 DataMember 順序是 QPay wire contract，須與 V2.6 及 golden 測試保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 簽章只涵蓋 signer 能讀取的頂層純量欄位；巢狀 TSResultContent 不因外層簽章而取得完整性證明。
 * BindToken 只可在受信任 HTTPS 回應處理期間短暫存在，交出核心前須以 profile-bound AEAD 保護。
 * DTO 不持有網路資源或跨請求狀態；變更 wire 欄位須核對 V2.6 與 golden／假傳輸測試。
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

/// <summary>
/// 永豐 QPay API 服務的內部列舉識別；既有數值僅供程式內部保留，不能序列化為外層 APIService。
/// wire value 必須由 provider 明確對照為規格服務名稱。
/// </summary>
internal enum SinopacApiService
{
    /// <summary>建單操作的內部識別；外層 APIService wire value 為服務名稱 OrderCreate。</summary>
    OrderCreate = 1,

    /// <summary>付款狀態查詢的內部識別；外層 APIService wire value 為服務名稱 OrderPayQuery。</summary>
    OrderPayQuery = 5,

    /// <summary>撥款檔查詢的內部識別；外層 APIService wire value 為服務名稱 AllotQuery。</summary>
    AllotQuery = 7,

    /// <summary>建立綁卡付款頁的內部識別；外層 APIService wire value 為服務名稱 CardBindCreate。</summary>
    CardBindCreate = 8,

    /// <summary>取消授權、立即請款或停止定期扣款的內部識別；wire value 為 OrderMaintain。</summary>
    OrderMaintain = 9,

    /// <summary>定期扣款主單與扣款列查詢的內部識別；wire value 為 RegularQuery。</summary>
    RegularQuery = 10
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

    /// <summary>已由伺服器端 profile 綁定的銀行 BindNo。</summary>
    [DataMember]
    [DefaultValue("")]
    public string BindNo { get; set; } = string.Empty;

    /// <summary>只有 provider 在本次加密 OrderCreate 時短暫填入明文，呼叫完成後立即清空。</summary>
    [DataMember]
    [DefaultValue("")]
    public string BindToken { get; set; } = string.Empty;
}

[DataContract]
internal sealed class SinopacOrderPayQueryRequest : ISinopacRequest
{
    [DataMember]
    public string ShopNo { get; set; } = string.Empty;

    [DataMember]
    public string PayToken { get; set; } = string.Empty;
}

/// <summary>永豐 CardBindCreate request；NT$1 以整數最小單位傳送，且不建立請款。</summary>
[DataContract]
internal sealed class SinopacCardBindCreateRequest : ISinopacRequest
{
    /// <summary>只從本次 resolver 選出的 profile 取得，不接受產品 request 覆寫。</summary>
    [DataMember]
    public string ShopNo { get; set; } = string.Empty;

    /// <summary>由受信任產品服務為綁卡業務事件產生的唯一識別。</summary>
    [DataMember]
    public string BindNo { get; set; } = string.Empty;

    /// <summary>綁卡預先授權金額，以 TWD 最小單位表示；只建立授權，不執行請款。</summary>
    [DataMember]
    public int Amount { get; set; }

    /// <summary>綁卡授權使用的幣別識別，固定為 TWD。</summary>
    [DataMember]
    public string CurrencyID { get; set; } = "TWD";

    /// <summary>使用者完成銀行綁卡頁後的回跳網址，必須為 HTTPS。</summary>
    [DataMember]
    public string ReturnURL { get; set; } = string.Empty;
}

/// <summary>永豐 CardBindCreate 解密及外層簽章驗證後的結果。</summary>
[DataContract]
internal sealed class SinopacCardBindCreateResponse
{
    /// <summary>銀行回報的商店編號，必須與選取 profile 完全相同。</summary>
    [DataMember]
    public string ShopNo { get; set; } = string.Empty;

    /// <summary>銀行回報的綁卡編號，必須與本次 request 相同。</summary>
    [DataMember]
    public string BindNo { get; set; } = string.Empty;

    /// <summary>銀行提供的綁卡付款頁網址；只接受絕對 HTTPS URI。</summary>
    [DataMember]
    public string CardBindURL { get; set; } = string.Empty;

    /// <summary>銀行建立結果狀態。</summary>
    [DataMember]
    public string Status { get; set; } = string.Empty;

    /// <summary>銀行建立結果說明；不得直接寫入一般日誌。</summary>
    [DataMember]
    public string Description { get; set; } = string.Empty;
}

/// <summary>永豐 OrderMaintain request；Command 由核心 capability 固定映射，不接受呼叫端任意命令。</summary>
[DataContract]
internal sealed class SinopacOrderMaintainRequest : ISinopacRequest
{
    /// <summary>由本次 resolver 選出的 profile 指定。</summary>
    [DataMember]
    public string ShopNo { get; set; } = string.Empty;

    /// <summary>需維護的穩定銀行訂單識別。</summary>
    [DataMember]
    public string OrderNo { get; set; } = string.Empty;

    /// <summary>只允許 capability 產生的 C、P 或 E 命令。</summary>
    [DataMember]
    public string Command { get; set; } = string.Empty;
}

/// <summary>永豐 OrderMaintain 解密及外層簽章驗證後的結果。</summary>
[DataContract]
internal sealed class SinopacOrderMaintainResponse
{
    /// <summary>銀行回報的訂單識別，必須匹配 request。</summary>
    [DataMember]
    public string OrderNo { get; set; } = string.Empty;

    /// <summary>銀行回報的商店識別，必須匹配 profile。</summary>
    [DataMember]
    public string ShopNo { get; set; } = string.Empty;

    /// <summary>銀行回報的交易識別。</summary>
    [DataMember]
    public string TSNo { get; set; } = string.Empty;

    /// <summary>銀行回傳的維護命令，必須匹配本次請求。</summary>
    [DataMember]
    public string Command { get; set; } = string.Empty;

    /// <summary>永豐回報的時間欄位。</summary>
    [DataMember]
    public string Date { get; set; } = string.Empty;

    /// <summary>銀行維護操作狀態。</summary>
    [DataMember]
    public string Status { get; set; } = string.Empty;

    /// <summary>銀行維護操作說明；不直接進一般日誌。</summary>
    [DataMember]
    public string Description { get; set; } = string.Empty;
}

/// <summary>永豐 RegularQuery request，依 profile 商店查詢指定定期主單。</summary>
[DataContract]
internal sealed class SinopacRegularQueryRequest : ISinopacRequest
{
    /// <summary>由本次 resolver 選出的 profile 指定。</summary>
    [DataMember]
    public string ShopNo { get; set; } = string.Empty;

    /// <summary>伺服器端建立並保存的定期主單識別。</summary>
    [DataMember]
    public string MasterOrderNo { get; set; } = string.Empty;
}

/// <summary>永豐 RegularQuery 的頂層回應；子扣款只在完成所有穩定 ID 驗證後才映射為中立結果。</summary>
[DataContract]
internal sealed class SinopacRegularQueryResponse
{
    /// <summary>銀行回報的商店識別，必須匹配 profile。</summary>
    [DataMember]
    public string ShopNo { get; set; } = string.Empty;

    /// <summary>銀行回報的查詢時間欄位。</summary>
    [DataMember]
    public string Date { get; set; } = string.Empty;

    /// <summary>查詢 API 的處理狀態。</summary>
    [DataMember]
    public string Status { get; set; } = string.Empty;

    /// <summary>查詢 API 說明，不得直接寫入一般日誌。</summary>
    [DataMember]
    public string Description { get; set; } = string.Empty;

    /// <summary>定期主單與扣款明細；DTO 清單是本次 response 所有，不會由 provider 跨呼叫保存。</summary>
    [DataMember]
    public SinopacRegularQueryOrder? Order { get; set; }
}

/// <summary>永豐定期主單及其逐筆扣款候選資料。</summary>
[DataContract]
internal sealed class SinopacRegularQueryOrder
{
    /// <summary>銀行回報的定期主單識別，必須匹配查詢 request。</summary>
    [DataMember]
    public string MasterOrderNo { get; set; } = string.Empty;

    /// <summary>定期主單建立時間原始值。</summary>
    [DataMember]
    public string OrderTime { get; set; } = string.Empty;

    /// <summary>定期合約排定的扣款總期數。</summary>
    [DataMember]
    public int DeductTotalNum { get; set; }

    /// <summary>銀行回報的定期方案總額最小單位。</summary>
    [DataMember]
    public int Amount { get; set; }

    /// <summary>銀行回報的預期下次扣款日原始值。</summary>
    [DataMember]
    public string ExpectDeductDate { get; set; } = string.Empty;

    /// <summary>扣款間隔數值。</summary>
    [DataMember]
    public int FreqNum { get; set; }

    /// <summary>扣款間隔類型代碼。</summary>
    [DataMember]
    public string FreqType { get; set; } = string.Empty;

    /// <summary>目前已處理扣款次數。</summary>
    [DataMember]
    public int DeductNum { get; set; }

    /// <summary>銀行回報的定期方案狀態代碼。</summary>
    [DataMember]
    public string RegularStatus { get; set; } = string.Empty;

    /// <summary>銀行回報的停止扣款時間原始值。</summary>
    [DataMember]
    public string StopDeductTime { get; set; } = string.Empty;

    /// <summary>provider 對定期方案的附註；不得進入一般診斷輸出。</summary>
    [DataMember]
    public string Remarks { get; set; } = string.Empty;

    /// <summary>逐筆扣款候選；映射前需確認每筆 OrderNo 非空且在此集合唯一。</summary>
    [DataMember]
    public List<SinopacRegularQueryCharge> Info { get; set; } = new();
}

/// <summary>永豐 RegularQuery 的單筆扣款候選；OrderNo 是發布前必須驗證的穩定識別。</summary>
[DataContract]
internal sealed class SinopacRegularQueryCharge
{
    /// <summary>定期方案中的扣款序號。</summary>
    [DataMember]
    public int RegularNum { get; set; }

    /// <summary>該次扣款的穩定銀行訂單識別。</summary>
    [DataMember]
    public string OrderNo { get; set; } = string.Empty;

    /// <summary>銀行回報的扣款時間原始值，不在 provider 猜測時區。</summary>
    [DataMember]
    public string DeductTime { get; set; } = string.Empty;

    /// <summary>銀行回報的扣款狀態代碼，不直接宣告產品資料已入帳。</summary>
    [DataMember]
    public string DeductStatus { get; set; } = string.Empty;
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

    /// <summary>CardBindOut 的綁卡識別；僅在 HTTPS 回應經關聯檢查後用於本次結果封裝。</summary>
    [DataMember]
    [DefaultValue("")]
    public string BindNo { get; set; } = string.Empty;

    /// <summary>CardBindOut 的明文綁卡憑證；只允許存在於單次 provider 操作記憶體，不得映射或記錄。</summary>
    [DataMember]
    [DefaultValue("")]
    public string BindToken { get; set; } = string.Empty;
}

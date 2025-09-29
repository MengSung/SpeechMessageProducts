using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Newtonsoft.Json;

namespace ChurchReport.Tools
{
    #region TSPG 請求模型

    /// <summary>
    /// TSPG 付款請求模型 (支援 REST API v2.14 與舊版格式)
    /// </summary>
    public class TSPGPaymentRequest
    {
        #region REST API v2.14 結構

        /// <summary>
        /// 傳送端程式類型 (固定值: rest)
        /// </summary>
        [JsonProperty("sender")]
        public string Sender { get; set; } = "rest";

        /// <summary>
        /// 格式版本號 (固定值: 1.0.0)
        /// </summary>
        [JsonProperty("ver")]
        public string Ver { get; set; } = "1.0.0";

        /// <summary>
        /// 特店代號 (必填)
        /// </summary>
        [JsonProperty("mid")]
        [Required]
        [StringLength(15)]
        public string Mid { get; set; }

        /// <summary>
        /// 子特店代號 (非代收代付及大特店請勿傳入此參數)
        /// </summary>
        [JsonProperty("s_mid")]
        [StringLength(15)]
        public string S_Mid { get; set; }

        /// <summary>
        /// 端末代號 (必填)
        /// </summary>
        [JsonProperty("tid")]
        [Required]
        [StringLength(8)]
        public string Tid { get; set; }

        /// <summary>
        /// 付款類別 (1: 信用卡)
        /// </summary>
        [JsonProperty("pay_type")]
        [Required]
        public int PayType { get; set; } = 1;

        /// <summary>
        /// 交易類別 (1: 授權, 3: 請款, 4: 取消請款, 5: 退貨, 6: 取消退貨, 7: 查詢, 8: 取消授權)
        /// </summary>
        [JsonProperty("tx_type")]
        [Required]
        public int TxType { get; set; } = 1;

        /// <summary>
        /// 交易要求參數清單
        /// </summary>
        [JsonProperty("params")]
        [Required]
        public TSPGPaymentParams Params { get; set; } = new TSPGPaymentParams();

        #endregion
    }

    /// <summary>
    /// TSPG 付款參數模型
    /// </summary>
    public class TSPGPaymentParams
    {
        /// <summary>
        /// 客戶端版面類型 (1: 一般網頁, 2: 行動裝置網頁)
        /// </summary>
        [JsonProperty("layout")]
        [Required]
        [StringLength(1)]
        public string Layout { get; set; } = "1";

        /// <summary>
        /// 訂單號碼 (必填)
        /// </summary>
        [JsonProperty("order_no")]
        [Required]
        [StringLength(23)]
        public string OrderNo { get; set; }

        /// <summary>
        /// 交易金額 (包含兩位小數，如100代表1.00元)
        /// </summary>
        [JsonProperty("amt")]
        [Required]
        [StringLength(12)]
        public string Amt { get; set; }

        /// <summary>
        /// 幣別 (NTD: 新台幣)
        /// </summary>
        [JsonProperty("cur")]
        [Required]
        [StringLength(3)]
        public string Cur { get; set; } = "NTD";

        /// <summary>
        /// 訂單說明 (此欄允許中文，請以UTF-8編碼傳入)
        /// </summary>
        [JsonProperty("order_desc")]
        [Required]
        [StringLength(40)]
        public string OrderDesc { get; set; }

        /// <summary>
        /// 授權同步請款標記 (0: 不同步請款, 1: 同步請款)
        /// </summary>
        [JsonProperty("capt_flag")]
        [Required]
        [StringLength(1)]
        public string CaptFlag { get; set; } = "0";

        /// <summary>
        /// 回傳訊息標記 (0: 不查詢交易詳情, 1: 查詢交易詳情)
        /// </summary>
        [JsonProperty("result_flag")]
        [Required]
        [StringLength(1)]
        public string ResultFlag { get; set; } = "1";

        /// <summary>
        /// 指定接續網址 (必填)
        /// </summary>
        [JsonProperty("post_back_url")]
        [Required]
        [StringLength(255)]
        [Url]
        public string PostBackUrl { get; set; }

        /// <summary>
        /// 指定交易資料回傳網址，須為 https:// (必填)
        /// </summary>
        [JsonProperty("result_url")]
        [Required]
        [StringLength(255)]
        [Url]
        public string ResultUrl { get; set; }

        /// <summary>
        /// 機票號碼 (可選)
        /// </summary>
        [JsonProperty("ticket_no")]
        [StringLength(20)]
        public string TicketNo { get; set; }

        /// <summary>
        /// 卡號 (若使用HPP，則不必填值)
        /// </summary>
        [JsonProperty("pan")]
        [StringLength(19)]
        public string Pan { get; set; }

        /// <summary>
        /// 到期日 (YYMM)
        /// </summary>
        [JsonProperty("exp_date")]
        [StringLength(4)]
        public string ExpDate { get; set; }

        /// <summary>
        /// CVC2/CVV2
        /// </summary>
        [JsonProperty("cvv2")]
        [StringLength(3)]
        public string Cvv2 { get; set; }

        /// <summary>
        /// 分期期數 (若不帶此欄位，或欄位值為空值或空白，則表示不為分期交易)
        /// </summary>
        [JsonProperty("install_period")]
        [StringLength(2)]
        public string InstallPeriod { get; set; }

        /// <summary>
        /// 紅利交易標記 (1: 紅利交易)
        /// </summary>
        [JsonProperty("use_redeem")]
        [StringLength(1)]
        public string UseRedeem { get; set; }

        /// <summary>
        /// 綁卡類型 (01: 交易中綁卡, 02: 純綁卡交易)
        /// </summary>
        [JsonProperty("threeDS_mc")]
        [StringLength(2)]
        public string ThreeDSMc { get; set; }

        /// <summary>
        /// 綁卡類別 (04: 新增卡片, 05: 續綁卡片)
        /// </summary>
        [JsonProperty("threeDS_ra")]
        [StringLength(2)]
        public string ThreeDSRa { get; set; }

        /// <summary>
        /// 縣市群組代碼 (國旅卡專用欄位)
        /// </summary>
        [JsonProperty("city")]
        [StringLength(3)]
        public string City { get; set; }

        /// <summary>
        /// 啟程日 (MMddyyyy) (國旅卡專用欄位)
        /// </summary>
        [JsonProperty("start_date")]
        [StringLength(8)]
        public string StartDate { get; set; }

        /// <summary>
        /// 回程日 (MMddyyyy) (國旅卡專用欄位)
        /// </summary>
        [JsonProperty("end_date")]
        [StringLength(8)]
        public string EndDate { get; set; }

        /// <summary>
        /// 行動裝置身分驗證標記 (0: 不啟用, 1: 啟用)
        /// </summary>
        [JsonProperty("cbr_indicator_flag")]
        [StringLength(1)]
        public string CbrIndicatorFlag { get; set; }

        /// <summary>
        /// 身份證號 (首字母大寫)
        /// </summary>
        [JsonProperty("cust_id")]
        [StringLength(10)]
        public string CustId { get; set; }

        /// <summary>
        /// 生日 (MMddyyyy)
        /// </summary>
        [JsonProperty("b_day")]
        [StringLength(8)]
        public string BDay { get; set; }

        /// <summary>
        /// 手機號碼 (皆為數字)
        /// </summary>
        [JsonProperty("cell_phone_no")]
        [StringLength(10)]
        public string CellPhoneNo { get; set; }

        /// <summary>
        /// 居家電話 (皆為數字)
        /// </summary>
        [JsonProperty("home_tel_no")]
        [StringLength(12)]
        public string HomeTelNo { get; set; }

        /// <summary>
        /// 公司電話 (皆為數字)
        /// </summary>
        [JsonProperty("office_tel_no")]
        [StringLength(12)]
        public string OfficeTelNo { get; set; }

        /// <summary>
        /// 持卡人英文姓名 (限半形字，內容可包含「,」、「-」、「.」、最後一碼不得空白)
        /// </summary>
        [JsonProperty("cardholder_name")]
        [StringLength(45)]
        public string CardholderName { get; set; }

        /// <summary>
        /// 持卡人email (限半形字，最後一碼不得空白)
        /// </summary>
        [JsonProperty("cardholder_email")]
        [StringLength(254)]
        [EmailAddress]
        public string CardholderEmail { get; set; }

        /// <summary>
        /// 持卡人手機號碼
        /// </summary>
        [JsonProperty("cardholder_mobile_phone")]
        public TSPGCardholderMobilePhone CardholderMobilePhone { get; set; }
    }

    /// <summary>
    /// TSPG 持卡人手機號碼
    /// </summary>
    public class TSPGCardholderMobilePhone
    {
        /// <summary>
        /// 國碼 (皆為數字，台灣地區為886)
        /// </summary>
        [JsonProperty("country_code")]
        [StringLength(3)]
        public string CountryCode { get; set; } = "886";

        /// <summary>
        /// 號碼 (皆為數字)
        /// </summary>
        [JsonProperty("phone_number")]
        [StringLength(15)]
        public string PhoneNumber { get; set; }
    }

    /// <summary>
    /// TSPG 退款請求模型
    /// </summary>
    public class TSPGRefundRequest
    {
        /// <summary>
        /// 訂單編號 (必填)
        /// </summary>
        [Required]
        [StringLength(50)]
        public string OrderId { get; set; }

        /// <summary>
        /// 退款金額 (可選，不提供則全額退款)
        /// </summary>
        [Range(0.01, double.MaxValue)]
        public decimal? RefundAmount { get; set; }

        /// <summary>
        /// 退款原因
        /// </summary>
        [StringLength(200)]
        public string Reason { get; set; }

        /// <summary>
        /// 退款申請人
        /// </summary>
        [StringLength(50)]
        public string ApplicantName { get; set; }

        /// <summary>
        /// 申請時間
        /// </summary>
        public DateTime ApplyTime { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// TSPG 查詢請求模型
    /// </summary>
    public class TSPGQueryRequest
    {
        /// <summary>
        /// 訂單編號
        /// </summary>
        [StringLength(50)]
        public string OrderId { get; set; }

        /// <summary>
        /// 交易編號
        /// </summary>
        [StringLength(50)]
        public string TransactionId { get; set; }

        /// <summary>
        /// 查詢開始日期 (格式: yyyy-MM-dd)
        /// </summary>
        public string StartDate { get; set; }

        /// <summary>
        /// 查詢結束日期 (格式: yyyy-MM-dd)
        /// </summary>
        public string EndDate { get; set; }

        /// <summary>
        /// 付款狀態 (1: 成功, 0: 失敗, 空值: 全部)
        /// </summary>
        public int? PaymentStatus { get; set; }

        /// <summary>
        /// 付款方式篩選
        /// </summary>
        [StringLength(20)]
        public string PaymentType { get; set; }
    }

    #endregion

    #region TSPG 回應模型

    /// <summary>
    /// TSPG 基礎回應模型
    /// </summary>
    public class TSPGBaseResponse
    {
        /// <summary>
        /// 回應代碼 (0000: 成功, 其他: 失敗)
        /// </summary>
        public string Code { get; set; }

        /// <summary>
        /// 回應訊息
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// 是否成功
        /// </summary>
        public bool IsSuccess => Code == "0000";

        /// <summary>
        /// 處理時間
        /// </summary>
        public DateTime ProcessTime { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// TSPG 付款回應模型
    /// </summary>
    public class TSPGPaymentResponse : TSPGBaseResponse
    {
        /// <summary>
        /// 訂單編號
        /// </summary>
        public string OrderId { get; set; }

        /// <summary>
        /// 交易編號
        /// </summary>
        public string TransactionId { get; set; }

        /// <summary>
        /// 付款頁面網址
        /// </summary>
        public string PaymentUrl { get; set; }

        /// <summary>
        /// 付款金額
        /// </summary>
        public decimal Amount { get; set; }

        /// <summary>
        /// 付款方式
        /// </summary>
        public string PaymentType { get; set; }

        /// <summary>
        /// 付款截止時間
        /// </summary>
        public DateTime? ExpireTime { get; set; }

        /// <summary>
        /// ATM 轉帳資訊 (如果是 ATM 付款)
        /// </summary>
        public TSPGATMInfo ATMInfo { get; set; }
    }

    /// <summary>
    /// TSPG ATM 轉帳資訊
    /// </summary>
    public class TSPGATMInfo
    {
        /// <summary>
        /// 銀行代碼
        /// </summary>
        public string BankCode { get; set; }

        /// <summary>
        /// 銀行帳號
        /// </summary>
        public string BankAccount { get; set; }

        /// <summary>
        /// 轉帳截止日期
        /// </summary>
        public DateTime ExpireDate { get; set; }

        /// <summary>
        /// 轉帳金額
        /// </summary>
        public decimal Amount { get; set; }
    }

    /// <summary>
    /// TSPG 交易查詢回應
    /// </summary>
    public class TSPGTransactionResponse : TSPGBaseResponse
    {
        /// <summary>
        /// 交易清單
        /// </summary>
        public List<TSPGTransaction> Transactions { get; set; } = new List<TSPGTransaction>();

        /// <summary>
        /// 總筆數
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// 總金額
        /// </summary>
        public decimal TotalAmount { get; set; }
    }

    /// <summary>
    /// TSPG 交易記錄
    /// </summary>
    public class TSPGTransaction
    {
        /// <summary>
        /// 訂單編號
        /// </summary>
        public string OrderId { get; set; }

        /// <summary>
        /// 交易編號
        /// </summary>
        public string TransactionId { get; set; }

        /// <summary>
        /// 付款金額
        /// </summary>
        public decimal Amount { get; set; }

        /// <summary>
        /// 付款狀態 (SUCCESS: 成功, FAILED: 失敗, PENDING: 處理中)
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// 付款方式
        /// </summary>
        public string PayType { get; set; }

        /// <summary>
        /// 交易時間
        /// </summary>
        public DateTime TransactionDate { get; set; }

        /// <summary>
        /// 付款時間
        /// </summary>
        public DateTime? PaymentDate { get; set; }

        /// <summary>
        /// 商品名稱
        /// </summary>
        public string ProductName { get; set; }

        /// <summary>
        /// 使用者姓名
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// 使用者電子郵件
        /// </summary>
        public string UserEmail { get; set; }

        /// <summary>
        /// 手續費
        /// </summary>
        public decimal Fee { get; set; }

        /// <summary>
        /// 實收金額 (扣除手續費後)
        /// </summary>
        public decimal NetAmount { get; set; }

        /// <summary>
        /// 備註
        /// </summary>
        public string Memo { get; set; }

        /// <summary>
        /// 退款資訊
        /// </summary>
        public TSPGRefundInfo RefundInfo { get; set; }

        /// <summary>
        /// 信用卡資訊 (如果是信用卡付款)
        /// </summary>
        public TSPGCreditCardInfo CreditCardInfo { get; set; }
    }

    /// <summary>
    /// TSPG 退款資訊
    /// </summary>
    public class TSPGRefundInfo
    {
        /// <summary>
        /// 是否已退款
        /// </summary>
        public bool IsRefunded { get; set; }

        /// <summary>
        /// 退款金額
        /// </summary>
        public decimal RefundAmount { get; set; }

        /// <summary>
        /// 退款時間
        /// </summary>
        public DateTime? RefundDate { get; set; }

        /// <summary>
        /// 退款原因
        /// </summary>
        public string RefundReason { get; set; }

        /// <summary>
        /// 退款狀態
        /// </summary>
        public string RefundStatus { get; set; }
    }

    /// <summary>
    /// TSPG 信用卡資訊
    /// </summary>
    public class TSPGCreditCardInfo
    {
        /// <summary>
        /// 卡號前6碼
        /// </summary>
        public string CardNumberPrefix { get; set; }

        /// <summary>
        /// 卡號後4碼
        /// </summary>
        public string CardNumberSuffix { get; set; }

        /// <summary>
        /// 卡片類型
        /// </summary>
        public string CardType { get; set; }

        /// <summary>
        /// 發卡銀行
        /// </summary>
        public string IssuingBank { get; set; }

        /// <summary>
        /// 授權碼
        /// </summary>
        public string AuthCode { get; set; }

        /// <summary>
        /// 分期期數
        /// </summary>
        public int? InstallmentPeriods { get; set; }

        /// <summary>
        /// 每期金額
        /// </summary>
        public decimal? InstallmentAmount { get; set; }
    }

    /// <summary>
    /// TSPG 交易記錄查詢回應
    /// </summary>
    public class TSPGTransactionHistoryResponse : TSPGBaseResponse
    {
        /// <summary>
        /// 交易記錄清單
        /// </summary>
        public List<TSPGTransaction> Transactions { get; set; } = new List<TSPGTransaction>();

        /// <summary>
        /// 查詢日期範圍開始
        /// </summary>
        public string StartDate { get; set; }

        /// <summary>
        /// 查詢日期範圍結束
        /// </summary>
        public string EndDate { get; set; }

        /// <summary>
        /// 總筆數
        /// </summary>
        public int TotalCount => Transactions?.Count ?? 0;

        /// <summary>
        /// 成功交易筆數
        /// </summary>
        public int SuccessCount => Transactions?.Where(t => t.Status == "SUCCESS").Count() ?? 0;

        /// <summary>
        /// 失敗交易筆數
        /// </summary>
        public int FailedCount => Transactions?.Where(t => t.Status == "FAILED").Count() ?? 0;

        /// <summary>
        /// 總金額
        /// </summary>
        public decimal TotalAmount => Transactions?.Where(t => t.Status == "SUCCESS").Sum(t => t.Amount) ?? 0;
    }

    #endregion

    #region TSPG 通知模型

    /// <summary>
    /// TSPG 付款通知模型 (來自 TSPG 的回調通知)
    /// </summary>
    public class TSPGPaymentNotification
    {
        /// <summary>
        /// 商店 ID
        /// </summary>
        public string StoreUid { get; set; }

        /// <summary>
        /// 訂單編號
        /// </summary>
        public string OrderId { get; set; }

        /// <summary>
        /// 交易編號
        /// </summary>
        public string TransactionId { get; set; }

        /// <summary>
        /// 付款狀態 (1: 成功, 0: 失敗)
        /// </summary>
        public string State { get; set; }

        /// <summary>
        /// 付款金額
        /// </summary>
        public decimal Cost { get; set; }

        /// <summary>
        /// 實際付款金額
        /// </summary>
        public decimal ActualCost { get; set; }

        /// <summary>
        /// 幣別
        /// </summary>
        public string Currency { get; set; }

        /// <summary>
        /// 付款方式
        /// </summary>
        public string PayType { get; set; }

        /// <summary>
        /// 付款時間
        /// </summary>
        public DateTime PayTime { get; set; }

        /// <summary>
        /// 使用者姓名
        /// </summary>
        public string UserName { get; set; }

        /// <summary>
        /// 使用者電子郵件
        /// </summary>
        public string UserEmail { get; set; }

        /// <summary>
        /// 使用者電話
        /// </summary>
        public string UserPhone { get; set; }

        /// <summary>
        /// 回傳訊息
        /// </summary>
        public string ReturnMessage { get; set; }

        /// <summary>
        /// 檢查碼
        /// </summary>
        public string Hash { get; set; }

        /// <summary>
        /// 自訂參數
        /// </summary>
        public string Echo0 { get; set; }
        public string Echo1 { get; set; }
        public string Echo2 { get; set; }
        public string Echo3 { get; set; }
        public string Echo4 { get; set; }

        /// <summary>
        /// 信用卡相關資訊
        /// </summary>
        public string CardNo { get; set; }
        public string AuthCode { get; set; }
        public string CardType { get; set; }
        public string IssuingBank { get; set; }

        /// <summary>
        /// 是否付款成功
        /// </summary>
        public bool IsPaymentSuccess => State == "1";
    }

    #endregion

    #region TSPG 設定模型

    /// <summary>
    /// TSPG API 設定
    /// </summary>
    public class TSPGConfiguration
    {
        /// <summary>
        /// 商店 ID
        /// </summary>
        public string StoreId { get; set; }

        /// <summary>
        /// 商店金鑰
        /// </summary>
        public string StoreKey { get; set; }

        /// <summary>
        /// 商店 IV
        /// </summary>
        public string StoreIV { get; set; }

        /// <summary>
        /// API 基礎網址
        /// </summary>
        public string ApiBaseUrl { get; set; }

        /// <summary>
        /// 是否為測試模式
        /// </summary>
        public bool IsTestMode { get; set; }

        /// <summary>
        /// 連線逾時時間 (秒)
        /// </summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// 重試次數
        /// </summary>
        public int RetryCount { get; set; } = 3;

        /// <summary>
        /// 預設回傳網址
        /// </summary>
        public string DefaultReturnUrl { get; set; }

        /// <summary>
        /// 預設通知網址
        /// </summary>
        public string DefaultNotifyUrl { get; set; }

        /// <summary>
        /// 啟用日誌記錄
        /// </summary>
        public bool EnableLogging { get; set; } = true;
    }

    #endregion

    #region TSPG 錯誤模型

    /// <summary>
    /// TSPG API 錯誤
    /// </summary>
    public class TSPGApiError
    {
        /// <summary>
        /// 錯誤代碼
        /// </summary>
        public string ErrorCode { get; set; }

        /// <summary>
        /// 錯誤訊息
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// 詳細錯誤描述
        /// </summary>
        public string ErrorDetail { get; set; }

        /// <summary>
        /// 發生時間
        /// </summary>
        public DateTime OccurredAt { get; set; } = DateTime.Now;

        /// <summary>
        /// 相關的訂單編號
        /// </summary>
        public string OrderId { get; set; }

        /// <summary>
        /// 請求的 API 端點
        /// </summary>
        public string ApiEndpoint { get; set; }

        /// <summary>
        /// 例外堆疊追蹤
        /// </summary>
        public string StackTrace { get; set; }
    }

    #endregion

    #region TSPG REST API v2.14 回應模型

    /// <summary>
    /// TSPG REST API v2.14 回應模型
    /// </summary>
    public class TSPGApiResponse
    {
        /// <summary>
        /// 格式版本號
        /// </summary>
        [JsonProperty("ver")]
        public string Ver { get; set; }

        /// <summary>
        /// 特店代號
        /// </summary>
        [JsonProperty("mid")]
        public string Mid { get; set; }

        /// <summary>
        /// 子特店代號
        /// </summary>
        [JsonProperty("s_mid")]
        public string S_Mid { get; set; }

        /// <summary>
        /// 端末代號
        /// </summary>
        [JsonProperty("tid")]
        public string Tid { get; set; }

        /// <summary>
        /// 付款類別
        /// </summary>
        [JsonProperty("pay_type")]
        public int PayType { get; set; }

        /// <summary>
        /// 交易類別
        /// </summary>
        [JsonProperty("tx_type")]
        public int TxType { get; set; }

        /// <summary>
        /// 保留欄位，固定回 0
        /// </summary>
        [JsonProperty("ret_value")]
        public int RetValue { get; set; }

        /// <summary>
        /// 交易結果回應碼
        /// </summary>
        [JsonProperty("ret_code")]
        public string ret_code { get; set; }

        /// <summary>
        /// 回傳訊息
        /// </summary>
        [JsonProperty("ret_msg")]
        public string ret_msg { get; set; }

        /// <summary>
        /// 回應參數清單
        /// </summary>
        [JsonProperty("params")]
        public TSPGApiResponseParams Params { get; set; }
    }

    /// <summary>
    /// TSPG API 回應參數
    /// </summary>
    public class TSPGApiResponseParams
    {
        /// <summary>
        /// 付款網頁資訊
        /// </summary>
        [JsonProperty("hpp_url")]
        public string hpp_url { get; set; }

        /// <summary>
        /// 交易編號
        /// </summary>
        [JsonProperty("transaction_id")]
        public string transaction_id { get; set; }

        /// <summary>
        /// 訂單編號
        /// </summary>
        [JsonProperty("order_no")]
        public string order_no { get; set; }

        /// <summary>
        /// 交易金額
        /// </summary>
        [JsonProperty("amt")]
        public string amt { get; set; }

        /// <summary>
        /// 幣別
        /// </summary>
        [JsonProperty("cur")]
        public string cur { get; set; }
    }

    #endregion
}
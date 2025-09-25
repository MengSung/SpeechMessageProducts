using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace ChurchReport.Tools
{
    #region TSPG 請求模型

    /// <summary>
    /// TSPG 付款請求模型
    /// </summary>
    public class TSPGPaymentRequest
    {
        /// <summary>
        /// 訂單編號 (必填)
        /// </summary>
        [Required]
        [StringLength(50)]
        public string OrderId { get; set; }

        /// <summary>
        /// 付款金額 (必填)
        /// </summary>
        [Required]
        [Range(0.01, double.MaxValue)]
        public decimal Amount { get; set; }

        /// <summary>
        /// 商品名稱 (必填)
        /// </summary>
        [Required]
        [StringLength(60)]
        public string ProductName { get; set; }

        /// <summary>
        /// 付款完成後返回網址 (必填)
        /// </summary>
        [Required]
        [Url]
        [StringLength(255)]
        public string ReturnUrl { get; set; }

        /// <summary>
        /// 付款結果通知網址 (必填)
        /// </summary>
        [Required]
        [Url]
        [StringLength(255)]
        public string NotifyUrl { get; set; }

        /// <summary>
        /// 幣別 (預設 TWD)
        /// </summary>
        [StringLength(3)]
        public string Currency { get; set; } = "TWD";

        /// <summary>
        /// 付款方式 (credit: 信用卡, atm: ATM, all: 全部)
        /// </summary>
        [StringLength(20)]
        public string PaymentType { get; set; } = "credit";

        /// <summary>
        /// 使用者姓名 (可選)
        /// </summary>
        [StringLength(50)]
        public string UserName { get; set; }

        /// <summary>
        /// 使用者電子郵件 (可選)
        /// </summary>
        [EmailAddress]
        [StringLength(100)]
        public string UserEmail { get; set; }

        /// <summary>
        /// 使用者電話 (可選)
        /// </summary>
        [Phone]
        [StringLength(20)]
        public string UserPhone { get; set; }

        /// <summary>
        /// 自訂參數 1
        /// </summary>
        [StringLength(255)]
        public string Echo0 { get; set; }

        /// <summary>
        /// 自訂參數 2
        /// </summary>
        [StringLength(255)]
        public string Echo1 { get; set; }

        /// <summary>
        /// 自訂參數 3
        /// </summary>
        [StringLength(255)]
        public string Echo2 { get; set; }

        /// <summary>
        /// 自訂參數 4
        /// </summary>
        [StringLength(255)]
        public string Echo3 { get; set; }

        /// <summary>
        /// 自訂參數 5
        /// </summary>
        [StringLength(255)]
        public string Echo4 { get; set; }

        /// <summary>
        /// 付款截止時間 (可選，格式: yyyy-MM-dd HH:mm:ss)
        /// </summary>
        public DateTime? ExpireTime { get; set; }

        /// <summary>
        /// 是否啟用分期付款
        /// </summary>
        public bool EnableInstallment { get; set; } = false;

        /// <summary>
        /// 分期期數 (若啟用分期付款)
        /// </summary>
        [Range(3, 24)]
        public int? InstallmentPeriods { get; set; }

        /// <summary>
        /// 備註
        /// </summary>
        [StringLength(200)]
        public string Memo { get; set; }
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
}
using System;
using System.ComponentModel.DataAnnotations;

namespace ChurchReport.Tools
{
    #region TSPG 標準交易請求模型 (依據 TSPG.pdf 4.2 交易請求電文格式)

    /// <summary>
    /// TSPG 標準交易請求 - 依據 TSPG.pdf 4.2 交易請求電文格式
    /// </summary>
    public class TSPGStandardPaymentRequest
    {
        #region 必要欄位 (Required Fields)

        /// <summary>
        /// 特約商店商務代號 (必要)
        /// 長度: 15
        /// </summary>
        [Required]
        [StringLength(15)]
        public string store_uid { get; set; }

        /// <summary>
        /// 訂單編號 (必要)
        /// 長度: 1-50
        /// 說明: 不可重複，建議使用唯一值
        /// </summary>
        [Required]
        [StringLength(50, MinimumLength = 1)]
        public string order_id { get; set; }

        /// <summary>
        /// 交易金額 (必要)
        /// 格式: 整數，單位為分 (例如: 1000 = 10.00元)
        /// 範圍: 1 ~ 99999999
        /// </summary>
        [Required]
        [Range(1, 99999999)]
        public int cost { get; set; }

        /// <summary>
        /// 商品名稱 (必要)
        /// 長度: 1-60
        /// </summary>
        [Required]
        [StringLength(60, MinimumLength = 1)]
        public string product_name { get; set; }

        /// <summary>
        /// 付款完成後返回商店網址 (必要)
        /// 長度: 1-255
        /// 格式: 必須為有效的 URL
        /// </summary>
        [Required]
        [Url]
        [StringLength(255, MinimumLength = 1)]
        public string return_url { get; set; }

        /// <summary>
        /// 付款結果通知網址 (必要)
        /// 長度: 1-255
        /// 格式: 必須為有效的 URL
        /// </summary>
        [Required]
        [Url]
        [StringLength(255, MinimumLength = 1)]
        public string notify_url { get; set; }

        /// <summary>
        /// 檢查碼 (必要)
        /// 長度: 64
        /// 說明: SHA256 加密後的字串，由系統自動產生
        /// </summary>
        [Required]
        [StringLength(64)]
        public string hash { get; set; }

        #endregion

        #region 選用欄位 (Optional Fields)

        /// <summary>
        /// 付款方式 (選用)
        /// 預設值: credit
        /// 可選值: credit(信用卡), atm(ATM轉帳), all(開放所有付款方式)
        /// </summary>
        [StringLength(20)]
        public string pay_type { get; set; } = "credit";

        /// <summary>
        /// 幣別 (選用)
        /// 預設值: TWD
        /// 長度: 3
        /// </summary>
        [StringLength(3)]
        public string currency { get; set; } = "TWD";

        /// <summary>
        /// 消費者姓名 (選用)
        /// 長度: 1-50
        /// </summary>
        [StringLength(50)]
        public string user_name { get; set; }

        /// <summary>
        /// 消費者電子信箱 (選用)
        /// 長度: 1-100
        /// 格式: 必須為有效的 Email 格式
        /// </summary>
        [EmailAddress]
        [StringLength(100)]
        public string user_email { get; set; }

        /// <summary>
        /// 消費者電話 (選用)
        /// 長度: 1-20
        /// </summary>
        [StringLength(20)]
        public string user_phone { get; set; }

        /// <summary>
        /// 消費者 IP (選用)
        /// 長度: 1-45
        /// 說明: 支援 IPv4 和 IPv6
        /// </summary>
        [StringLength(45)]
        public string user_ip { get; set; }

        /// <summary>
        /// 語言設定 (選用)
        /// 預設值: zh-TW
        /// 可選值: zh-TW(繁體中文), en-US(英文), zh-CN(簡體中文)
        /// </summary>
        [StringLength(10)]
        public string language { get; set; } = "zh-TW";

        /// <summary>
        /// 自訂欄位 1 (選用)
        /// 長度: 0-255
        /// 說明: 會在交易完成通知時原值回傳
        /// </summary>
        [StringLength(255)]
        public string echo_0 { get; set; }

        /// <summary>
        /// 自訂欄位 2 (選用)
        /// 長度: 0-255
        /// </summary>
        [StringLength(255)]
        public string echo_1 { get; set; }

        /// <summary>
        /// 自訂欄位 3 (選用)
        /// 長度: 0-255
        /// </summary>
        [StringLength(255)]
        public string echo_2 { get; set; }

        /// <summary>
        /// 自訂欄位 4 (選用)
        /// 長度: 0-255
        /// </summary>
        [StringLength(255)]
        public string echo_3 { get; set; }

        /// <summary>
        /// 自訂欄位 5 (選用)
        /// 長度: 0-255
        /// </summary>
        [StringLength(255)]
        public string echo_4 { get; set; }

        #endregion

        #region 信用卡專用欄位 (Credit Card Specific Fields) - 依據 4.4 信用卡授權交易

        /// <summary>
        /// 信用卡交易類型 (選用)
        /// 預設值: auth
        /// 可選值: auth(授權), sale(授權+請款)
        /// </summary>
        [StringLength(10)]
        public string card_type { get; set; } = "auth";

        /// <summary>
        /// 是否啟用分期付款 (選用)
        /// 預設值: false
        /// 說明: true=啟用, false=不啟用
        /// </summary>
        public bool installment_enable { get; set; } = false;

        /// <summary>
        /// 分期期數 (選用)
        /// 範圍: 3-24
        /// 說明: 當 installment_enable=true 時使用
        /// </summary>
        [Range(3, 24)]
        public int? installment_periods { get; set; }

        /// <summary>
        /// 分期利率 (選用)
        /// 範圍: 0.00-99.99
        /// 單位: 百分比 (例如: 2.5 表示 2.5%)
        /// </summary>
        [Range(0.00, 99.99)]
        public decimal? installment_rate { get; set; }

        /// <summary>
        /// 是否強制使用 3D 驗證 (選用)
        /// 預設值: false
        /// </summary>
        public bool force_3d { get; set; } = false;

        /// <summary>
        /// 授權碼有效期限 (選用)
        /// 格式: YYYY-MM-DD HH:mm:ss
        /// 說明: 若不指定則使用系統預設值(通常為30分鐘)
        /// </summary>
        public DateTime? auth_expire_time { get; set; }

        #endregion

        #region ATM 專用欄位 (ATM Specific Fields)

        /// <summary>
        /// ATM 繳費期限 (選用)
        /// 格式: YYYY-MM-DD
        /// 說明: 不可早於當日，不可晚於30天後
        /// </summary>
        public DateTime? atm_expire_date { get; set; }

        /// <summary>
        /// ATM 銀行代碼 (選用)
        /// 長度: 3
        /// 說明: 指定特定銀行，空白則開放所有銀行
        /// </summary>
        [StringLength(3)]
        public string atm_bank_code { get; set; }

        #endregion

        #region 系統控制欄位 (System Control Fields)

        /// <summary>
        /// 是否為測試交易 (選用)
        /// 預設值: false
        /// 說明: true=測試模式, false=正式交易
        /// </summary>
        public bool is_test { get; set; } = false;

        /// <summary>
        /// 交易逾時時間 (選用)
        /// 單位: 秒
        /// 範圍: 60-1800 (1分鐘到30分鐘)
        /// 預設值: 600 (10分鐘)
        /// </summary>
        [Range(60, 1800)]
        public int timeout_seconds { get; set; } = 600;

        /// <summary>
        /// 版本號 (選用)
        /// 預設值: 1.0
        /// </summary>
        [StringLength(10)]
        public string version { get; set; } = "1.0";

        #endregion
    }

    #endregion

    #region TSPG 回應模型 (Response Models)

    /// <summary>
    /// TSPG 標準回應 - 付款頁面回應
    /// </summary>
    public class TSPGStandardResponse
    {
        /// <summary>
        /// 回應代碼
        /// 0000: 成功
        /// 其他: 錯誤代碼
        /// </summary>
        public string code { get; set; }

        /// <summary>
        /// 回應訊息
        /// </summary>
        public string msg { get; set; }

        /// <summary>
        /// 訂單編號 (成功時回傳原始訂單編號)
        /// </summary>
        public string uid { get; set; }

        /// <summary>
        /// 交易識別碼 (TSPG 內部使用)
        /// </summary>
        public string key { get; set; }

        /// <summary>
        /// 付款頁面網址 (成功時提供)
        /// </summary>
        public string url { get; set; }

        /// <summary>
        /// ATM 資訊 (當付款方式為 ATM 時提供)
        /// </summary>
        public TSPGATMResponse atm_info { get; set; }

        /// <summary>
        /// 是否成功
        /// </summary>
        public bool IsSuccess => code == "0000";
    }

    /// <summary>
    /// TSPG ATM 回應資訊
    /// </summary>
    public class TSPGATMResponse
    {
        /// <summary>
        /// 銀行代碼
        /// </summary>
        public string bank_code { get; set; }

        /// <summary>
        /// 銀行名稱
        /// </summary>
        public string bank_name { get; set; }

        /// <summary>
        /// 虛擬帳號
        /// </summary>
        public string virtual_account { get; set; }

        /// <summary>
        /// 繳費金額
        /// </summary>
        public int amount { get; set; }

        /// <summary>
        /// 繳費期限
        /// </summary>
        public DateTime expire_date { get; set; }
    }

    #endregion

    #region 轉換輔助類

    /// <summary>
    /// TSPG 請求轉換器 - 將現有的 TSPGPaymentRequest 轉換為標準格式
    /// </summary>
    public static class TSPGRequestConverter
    {
        /// <summary>
        /// 將 TSPGPaymentRequest 轉換為 TSPGStandardPaymentRequest
        /// </summary>
        /// <param name="source">來源請求</param>
        /// <param name="storeUid">商店代號</param>
        /// <returns>標準格式請求</returns>
        public static TSPGStandardPaymentRequest ConvertToStandardRequest(TSPGPaymentRequest source, string storeUid)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            return new TSPGStandardPaymentRequest
            {
                store_uid = storeUid,
                order_id = source.OrderId,
                cost = (int)(source.Amount * 100), // 轉換為分
                product_name = source.ProductName,
                return_url = source.ReturnUrl,
                notify_url = source.NotifyUrl,
                pay_type = source.PaymentType,
                currency = source.Currency,
                user_name = source.UserName,
                user_email = source.UserEmail,
                user_phone = source.UserPhone,
                echo_0 = source.Echo0,
                echo_1 = source.Echo1,
                echo_2 = source.Echo2,
                echo_3 = source.Echo3,
                echo_4 = source.Echo4,
                installment_enable = source.EnableInstallment,
                installment_periods = source.InstallmentPeriods,
                auth_expire_time = source.ExpireTime
            };
        }

        /// <summary>
        /// 將永豐金流 CreOrderReq 轉換為 TSPGStandardPaymentRequest
        /// </summary>
        /// <param name="source">永豐金流請求</param>
        /// <param name="storeUid">商店代號</param>
        /// <returns>標準格式請求</returns>
        public static TSPGStandardPaymentRequest ConvertFromCreOrderReq(QPay.Domain.CreOrderReq source, string storeUid)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            return new TSPGStandardPaymentRequest
            {
                store_uid = storeUid,
                order_id = source.OrderNo,
                cost = source.Amount * 100, // 轉換為分 (永豐是以元為單位)
                product_name = source.PrdtName ?? "商品",
                return_url = source.ReturnURL,
                notify_url = source.BackendURL,
                pay_type = source.PayType == "C" ? "credit" : "atm",
                currency = source.CurrencyID ?? "TWD",
                echo_0 = source.Param1,
                echo_1 = source.Param2,
                echo_2 = source.Param3,
                // 信用卡特定設定
                card_type = source.PayType == "C" ? "auth" : null,
                // ATM 特定設定
                atm_expire_date = (source.PayType == "A" && source.ATMParam != null && !string.IsNullOrEmpty(source.ATMParam.ExpireDate))
                    ? (DateTime?)DateTime.ParseExact(source.ATMParam.ExpireDate, "yyyyMMdd", null)
                    : null
            };
        }

        /// <summary>
        /// 將動態物件轉換為 TSPGStandardPaymentRequest
        /// </summary>
        /// <param name="customData">動態資料</param>
        /// <param name="storeUid">商店代號</param>
        /// <returns>標準格式請求</returns>
        public static TSPGStandardPaymentRequest ConvertFromDynamic(dynamic customData, string storeUid)
        {
            if (customData == null)
                throw new ArgumentNullException(nameof(customData));

            // 安全地轉換金額 (支援小數點轉換為分)
            decimal amount = 0;
            if (customData.cost != null)
            {
                if (decimal.TryParse(customData.cost.ToString(), out decimal parsedAmount))
                {
                    amount = parsedAmount;
                }
            }

            return new TSPGStandardPaymentRequest
            {
                store_uid = storeUid,
                order_id = customData?.order_id ?? Guid.NewGuid().ToString("N"),
                cost = (int)(amount * 100), // 轉換為分
                product_name = customData?.product_name ?? "商品",
                return_url = customData?.return_url ?? "",
                notify_url = customData?.notify_url ?? "",
                pay_type = customData?.pay_type ?? "credit",
                currency = customData?.currency ?? "TWD",
                user_name = customData?.user_name ?? "",
                user_email = customData?.user_email ?? "",
                user_phone = customData?.user_phone ?? "",
                user_ip = customData?.user_ip ?? "",
                echo_0 = customData?.echo_0?.ToString() ?? "",
                echo_1 = customData?.echo_1?.ToString() ?? "",
                echo_2 = customData?.echo_2?.ToString() ?? "",
                echo_3 = customData?.echo_3?.ToString() ?? "",
                echo_4 = customData?.echo_4?.ToString() ?? ""
            };
        }
    }

    #endregion
}
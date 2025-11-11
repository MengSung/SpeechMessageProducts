using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Reflection.Metadata;
using System.Globalization;
using System.Linq;

namespace ChurchReport.Models
{
    /// <summary>
    /// 高鉅金流 PayPage 交易完成回傳資訊模型
    /// 根據高鉅金流官方文檔的回傳欄位規格
    /// 包含『交易完成回傳資訊』、『非即時交易回傳資訊』、『訂單確認回傳資訊`
    /// </summary>
    public class MyPayReturnModel
    {
        #region 核心必要欄位 (Core Required Fields)

        /// <summary>
        /// Payment Hub之交易流水號
        /// </summary>
        public string uid { get; set; }

        /// <summary>
        /// 交易驗証碼
        /// </summary>
        public string key { get; set; }

        /// <summary>
        /// 主要交易回傳碼(retcode)
        /// </summary>
        public string prc { get; set; }

        /// <summary>
        /// 貴特店系統的訂單編號
        /// </summary>
        [Required]
        public string order_id { get; set; }

        #endregion

        #region 交易基本資訊 (Transaction Basic Info)

        /// <summary>
        /// 交易完成時間(YYYYMMDDHHmmss)
        /// </summary>
        public string finishtime { get; set; }

        /// <summary>
        /// 總交易金額
        /// </summary>
        public string cost { get; set; }

        /// <summary>
        /// 原交易幣別
        /// </summary>
        public string currency { get; set; }

        /// <summary>
        /// 實際交易金額
        /// </summary>
        public string actual_cost { get; set; }

        /// <summary>
        /// 實際交易幣別
        /// </summary>
        public string actual_currency { get; set; }

        /// <summary>
        /// 請求交易點數/金額
        /// </summary>
        public string price { get; set; }

        /// <summary>
        /// 實際交易點數/金額
        /// </summary>
        public string actual_price { get; set; }

        /// <summary>
        /// 交易產品代碼
        /// </summary>
        public string recharge_code { get; set; }

        /// <summary>
        /// 愛心捐款金額
        /// </summary>
        public string love_cost { get; set; }

        /// <summary>
        /// 回傳訊息
        /// </summary>
        public string retmsg { get; set; }

        /// <summary>
        /// 付費方法
        /// </summary>
        public string pfn { get; set; }

        /// <summary>
        /// 交易類型，參考『交易類型定義』值
        /// </summary>
        public int? trans_type { get; set; }

        #endregion

        #region 消費者資訊 (Consumer Information)

        /// <summary>
        /// 消費者帳號
        /// </summary>
        public string user_id { get; set; }

        #endregion

        #region 信用卡相關資訊 (Credit Card Information)

        /// <summary>
        /// 銀行端口回傳碼
        /// </summary>
        public string cardno { get; set; }

        /// <summary>
        /// 授權碼
        /// </summary>
        public string acode { get; set; }

        /// <summary>
        /// 信用卡卡別，參考『信用卡別類型』值
        /// </summary>
        public string card_type { get; set; }

        /// <summary>
        /// 發卡行
        /// </summary>
        public string issuing_bank { get; set; }

        /// <summary>
        /// 發卡銀行代碼
        /// </summary>
        public string issuing_bank_uid { get; set; }

        /// <summary>
        /// 紅利資訊 JSON 格式，參考『紅利資訊』值
        /// </summary>
        public string redeem { get; set; }

        /// <summary>
        /// 信用卡分期資訊 JSON 格式，參考『分期資訊』值
        /// </summary>
        public string installment { get; set; }

        #endregion

        #region 交易服務相關 (Transaction Service Related)

        /// <summary>
        /// 是否為經銷商代收費模式，參考『是否為經銷商代收費模式』值
        /// </summary>
        public int? is_agent_charge { get; set; }

        /// <summary>
        /// 交易服務類型，參考『交易服務類型』值
        /// </summary>
        public int? transaction_mode { get; set; }

        /// <summary>
        /// 交易之金融服務商
        /// </summary>
        public string supplier_name { get; set; }

        /// <summary>
        /// 交易之金融服務商代碼，參考『金流供應商代碼』值
        /// </summary>
        public string supplier_code { get; set; }

        #endregion

        #region 定期定額相關資訊 (Recurring Payment Information)

        /// <summary>
        /// 定期定額式/定期分期式扣款名稱
        /// </summary>
        public string payment_name { get; set; }

        /// <summary>
        /// 定期定額式/定期分期式扣繳期數
        /// </summary>
        public string nois { get; set; }

        /// <summary>
        /// 1.定期定額式扣款編號
        /// 2.定期分期式扣款編號
        /// </summary>
        public string group_id { get; set; }

        #endregion

        #region 虛擬帳號/超商代碼相關資訊 (Virtual Account/CVS Information)

        /// <summary>
        /// 銀行代碼 (虛擬帳號資訊)
        /// </summary>
        public string bank_id { get; set; }

        /// <summary>
        /// 有效日期(YYYYMMDDHHmmss) (虛擬帳號、超商代碼、無卡分期資訊)
        /// </summary>
        public string expired_date { get; set; }

        /// <summary>
        /// 虛擬帳號、超商代碼 資料格式類型，參考『閘道內容回傳格式類型』值
        /// </summary>
        public int? result_type { get; set; }

        /// <summary>
        /// 資料內容所屬支付名稱，參考『資料內容所屬支付名稱』值
        /// </summary>
        public string result_content_type { get; set; }

        /// <summary>
        /// 虛擬帳號、超商代碼 資料內容
        /// 參考值：『虛擬帳號回傳欄位』、『ibon』、『FamiPort』、『Life-ET』、『超商條碼繳費`
        /// </summary>
        public string result_content { get; set; }

        #endregion

        #region 自訂回傳參數 (Custom Return Parameters)

        /// <summary>
        /// 自訂回傳參數 1
        /// </summary>
        public string echo_0 { get; set; }

        /// <summary>
        /// 自訂回傳參數 2
        /// </summary>
        public string echo_1 { get; set; }

        /// <summary>
        /// 自訂回傳參數 3
        /// </summary>
        public string echo_2 { get; set; }

        /// <summary>
        /// 自訂回傳參數 4
        /// </summary>
        public string echo_3 { get; set; }

        /// <summary>
        /// 自訂回傳參數 5
        /// </summary>
        public string echo_4 { get; set; }

        #endregion

        #region 舊版相容欄位 (Legacy Compatibility Fields)

        /// <summary>
        /// 交易狀態 (1:成功, 0:失敗) - 舊版相容
        /// </summary>
        [Required]
        public string state { get; set; }

        /// <summary>
        /// 回傳訊息 - 舊版相容
        /// </summary>
        [Required]
        public string msg { get; set; }

        /// <summary>
        /// 商店代號 - 舊版相容
        /// </summary>
        [Required]
        public string store_uid { get; set; }

        /// <summary>
        /// 金流平台交易單號 - 舊版相容
        /// </summary>
        [Required]
        public string transaction_id { get; set; }

        /// <summary>
        /// 簽名驗證碼 - 舊版相容
        /// </summary>
        [Required]
        public string hash { get; set; }

        /// <summary>
        /// 付款人姓名 - 舊版相容
        /// </summary>
        public string user_name { get; set; }

        /// <summary>
        /// 付款人真實姓名 - 舊版相容
        /// </summary>
        public string user_real_name { get; set; }

        /// <summary>
        /// 付款人電話 - 舊版相容
        /// </summary>
        public string user_phone { get; set; }

        /// <summary>
        /// 付款人電子郵件 - 舊版相容
        /// </summary>
        public string user_email { get; set; }

        /// <summary>
        /// 付款方式類型 - 舊版相容
        /// </summary>
        public string pay_type { get; set; }

        /// <summary>
        /// 發票號碼 - 舊版相容
        /// </summary>
        public string invoice_number { get; set; }

        #endregion

        #region 新增的處理函數 (New Processing Functions)

        /// <summary>
        /// 處理所有高鉅金流回傳欄位的主要函數
        /// 根據 CopilotPrompt.txt 的完整參數規格實現
        /// 包含『交易完成回傳資訊』、『非即時交易回傳資訊』、『訂單確認回傳資訊`
        /// </summary>
        /// <returns>處理結果摘要</returns>
        public MyPayProcessingResult ProcessAllReturnFields()
        {
            try
            {
                var result = new MyPayProcessingResult
                {
                    IsSuccess = true,
                    ProcessingTime = DateTime.Now,
                    TransactionId = this.uid ?? this.transaction_id,
                    OrderId = this.order_id
                };

                // 處理核心必要欄位
                result.CoreFields = ProcessCoreFields();

                // 處理交易基本資訊
                result.TransactionInfo = ProcessTransactionInfo();

                // 處理消費者資訊
                result.ConsumerInfo = ProcessConsumerInfo();

                // 處理信用卡資訊
                result.CreditCardInfo = ProcessCreditCardInfo();

                // 處理交易服務相關
                result.ServiceInfo = ProcessServiceInfo();

                // 處理定期定額資訊
                result.RecurringInfo = ProcessRecurringPaymentInfo();

                // 處理虛擬帳號/超商代碼資訊
                result.VirtualAccountInfo = ProcessVirtualAccountInfo();

                // 處理自訂參數
                result.CustomParameters = ProcessCustomParameters();

                // 生成處理摘要
                result.Summary = GenerateProcessingSummary(result);

                return result;
            }
            catch (Exception ex)
            {
                return new MyPayProcessingResult
                {
                    IsSuccess = false,
                    ErrorMessage = $"處理失敗: {ex.Message}",
                    ProcessingTime = DateTime.Now,
                    TransactionId = this.uid ?? this.transaction_id,
                    OrderId = this.order_id
                };
            }
        }

        /// <summary>
        /// 處理核心必要欄位
        /// </summary>
        private CoreFieldsInfo ProcessCoreFields()
        {
            return new CoreFieldsInfo
            {
                Uid = this.uid,
                Key = this.key,
                Prc = this.prc,
                OrderId = this.order_id,
                IsValid = !string.IsNullOrEmpty(this.uid) && !string.IsNullOrEmpty(this.order_id)
            };
        }

        /// <summary>
        /// 處理交易基本資訊
        /// </summary>
        private TransactionInfo ProcessTransactionInfo()
        {
            var info = new TransactionInfo
            {
                FinishTime = this.finishtime,
                Cost = this.cost,
                Currency = this.currency,
                ActualCost = this.actual_cost,
                ActualCurrency = this.actual_currency,
                Price = this.price,
                ActualPrice = this.actual_price,
                RechargeCode = this.recharge_code,
                LoveCost = this.love_cost,
                ReturnMessage = this.retmsg,
                PaymentMethod = this.pfn,
                TransactionType = this.trans_type
            };

            // 解析交易完成時間
            if (!string.IsNullOrEmpty(this.finishtime) && this.finishtime.Length == 14)
            {
                if (DateTime.TryParseExact(this.finishtime, "yyyyMMddHHmmss", 
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedTime))
                {
                    info.ParsedFinishTime = parsedTime;
                }
            }

            // 解析金額
            if (!string.IsNullOrEmpty(this.cost) && decimal.TryParse(this.cost, out decimal amount))
            {
                info.ParsedCost = amount;
            }

            if (!string.IsNullOrEmpty(this.actual_cost) && decimal.TryParse(this.actual_cost, out decimal actualAmount))
            {
                info.ParsedActualCost = actualAmount;
            }

            return info;
        }

        /// <summary>
        /// 處理消費者資訊
        /// </summary>
        private ConsumerInfo ProcessConsumerInfo()
        {
            return new ConsumerInfo
            {
                UserId = this.user_id,
                UserName = this.user_name,
                UserRealName = this.user_real_name,
                UserPhone = this.user_phone,
                UserEmail = this.user_email
            };
        }

        /// <summary>
        /// 處理信用卡資訊
        /// </summary>
        private CreditCardInfo ProcessCreditCardInfo()
        {
            return new CreditCardInfo
            {
                CardNo = this.cardno,
                AuthCode = this.acode,
                CardType = this.card_type,
                IssuingBank = this.issuing_bank,
                IssuingBankUid = this.issuing_bank_uid,
                RedeemInfo = this.redeem,
                InstallmentInfo = this.installment
            };
        }

        /// <summary>
        /// 處理交易服務相關資訊
        /// </summary>
        private ServiceInfo ProcessServiceInfo()
        {
            return new ServiceInfo
            {
                IsAgentCharge = this.is_agent_charge,
                TransactionMode = this.transaction_mode,
                SupplierName = this.supplier_name,
                SupplierCode = this.supplier_code
            };
        }

        /// <summary>
        /// 處理定期定額資訊
        /// </summary>
        private RecurringInfo ProcessRecurringPaymentInfo()
        {
            return new RecurringInfo
            {
                PaymentName = this.payment_name,
                NumberOfInstallments = this.nois,
                GroupId = this.group_id
            };
        }

        /// <summary>
        /// 處理虛擬帳號/超商代碼資訊
        /// </summary>
        private VirtualAccountInfo ProcessVirtualAccountInfo()
        {
            var info = new VirtualAccountInfo
            {
                BankId = this.bank_id,
                ExpiredDate = this.expired_date,
                ResultType = this.result_type,
                ResultContentType = this.result_content_type,
                ResultContent = this.result_content
            };

            // 解析到期日
            if (!string.IsNullOrEmpty(this.expired_date) && this.expired_date.Length >= 8)
            {
                var dateStr = this.expired_date.Length == 14 ? this.expired_date : this.expired_date.Substring(0, 8);
                var format = this.expired_date.Length == 14 ? "yyyyMMddHHmmss" : "yyyyMMdd";
                
                if (DateTime.TryParseExact(dateStr, format, 
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
                {
                    info.ParsedExpiredDate = parsedDate;
                }
            }

            return info;
        }

        /// <summary>
        /// 處理自訂回傳參數
        /// </summary>
        private CustomParametersInfo ProcessCustomParameters()
        {
            return new CustomParametersInfo
            {
                Echo0 = this.echo_0,
                Echo1 = this.echo_1,
                Echo2 = this.echo_2,
                Echo3 = this.echo_3,
                Echo4 = this.echo_4
            };
        }

        /// <summary>
        /// 生成處理摘要
        /// </summary>
        private string GenerateProcessingSummary(MyPayProcessingResult result)
        {
            var summary = $"高鉅金流交易處理摘要 [{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n";
            summary += $"交易ID: {result.TransactionId}\n";
            summary += $"訂單ID: {result.OrderId}\n";
            summary += $"核心欄位有效: {result.CoreFields.IsValid}\n";
            
            if (result.TransactionInfo.ParsedCost.HasValue)
            {
                summary += $"交易金額: {result.TransactionInfo.ParsedCost.Value:C}\n";
            }
            
            if (result.TransactionInfo.ParsedFinishTime.HasValue)
            {
                summary += $"完成時間: {result.TransactionInfo.ParsedFinishTime.Value:yyyy-MM-dd HH:mm:ss}\n";
            }
            
            if (!string.IsNullOrEmpty(result.CreditCardInfo.CardType))
            {
                summary += $"付款方式: {result.CreditCardInfo.CardType}\n";
            }
            
            if (!string.IsNullOrEmpty(result.VirtualAccountInfo.BankId))
            {
                summary += $"銀行代碼: {result.VirtualAccountInfo.BankId}\n";
            }

            return summary;
        }

        /// <summary>
        /// 驗證所有必要欄位是否完整
        /// </summary>
        /// <returns>驗證結果</returns>
        public ValidationResult ValidateAllFields()
        {
            var result = new ValidationResult { IsValid = true };

            // 核心必要欄位 (依據規格.txt 回傳欄位)
            if (string.IsNullOrEmpty(this.uid))
            {
                result.Errors.Add("uid 是必要欄位，不能為空");
                result.IsValid = false;
            }
            if (string.IsNullOrEmpty(this.key))
            {
                result.Errors.Add("key 是必要欄位，不能為空");
                result.IsValid = false;
            }
            if (string.IsNullOrEmpty(this.prc))
            {
                result.Errors.Add("prc 是必要欄位，不能為空");
                result.IsValid = false;
            }
            if (string.IsNullOrEmpty(this.order_id))
            {
                result.Errors.Add("order_id 是必要欄位，不能為空");
                result.IsValid = false;
            }

            // 非必要但建議 (存在才可提升溯源能力)
            if (string.IsNullOrEmpty(this.finishtime))
            {
                result.Errors.Add("⚠️ finishtime 建議提供");
            }
            if (string.IsNullOrEmpty(this.cost) && string.IsNullOrEmpty(this.actual_cost))
            {
                result.Errors.Add("⚠️ cost 或 actual_cost 建議至少提供一個");
            }
            if (string.IsNullOrEmpty(this.pfn))
            {
                result.Errors.Add("⚠️ pfn 建議提供以辨識支付工具");
            }

            // 舊版相容欄位：不列為必要（僅記錄）
            if (string.IsNullOrEmpty(this.store_uid))
            {
                result.Errors.Add("⚠️ store_uid 為請求端欄位，回報中缺少可忽略");
            }
            if (string.IsNullOrEmpty(this.transaction_id))
            {
                result.Errors.Add("⚠️ transaction_id 未列於現行回報欄位，缺少可忽略");
            }
            if (string.IsNullOrEmpty(this.hash))
            {
                result.Errors.Add("⚠️ hash 非現行回報必填，缺少可忽略");
            }

            return result;
        }

        /// <summary>
        /// 取得所有欄位的鍵值對字典
        /// </summary>
        /// <returns>欄位字典</returns>
        public Dictionary<string, object> GetAllFieldsDictionary()
        {
            var fields = new Dictionary<string, object>();

            // 核心必要欄位
            fields.Add(nameof(uid), uid);
            fields.Add(nameof(key), key);
            fields.Add(nameof(prc), prc);
            fields.Add(nameof(order_id), order_id);

            // 交易基本資訊
            fields.Add(nameof(finishtime), finishtime);
            fields.Add(nameof(cost), cost);
            fields.Add(nameof(currency), currency);
            fields.Add(nameof(actual_cost), actual_cost);
            fields.Add(nameof(actual_currency), actual_currency);
            fields.Add(nameof(price), price);
            fields.Add(nameof(actual_price), actual_price);
            fields.Add(nameof(recharge_code), recharge_code);
            fields.Add(nameof(love_cost), love_cost);
            fields.Add(nameof(retmsg), retmsg);
            fields.Add(nameof(pfn), pfn);
            fields.Add(nameof(trans_type), trans_type);

            // 消費者資訊
            fields.Add(nameof(user_id), user_id);

            // 信用卡資訊
            fields.Add(nameof(cardno), cardno);
            fields.Add(nameof(acode), acode);
            fields.Add(nameof(card_type), card_type);
            fields.Add(nameof(issuing_bank), issuing_bank);
            fields.Add(nameof(issuing_bank_uid), issuing_bank_uid);
            fields.Add(nameof(redeem), redeem);
            fields.Add(nameof(installment), installment);

            // 交易服務相關
            fields.Add(nameof(is_agent_charge), is_agent_charge);
            fields.Add(nameof(transaction_mode), transaction_mode);
            fields.Add(nameof(supplier_name), supplier_name);
            fields.Add(nameof(supplier_code), supplier_code);

            // 定期定額資訊
            fields.Add(nameof(payment_name), payment_name);
            fields.Add(nameof(nois), nois);
            fields.Add(nameof(group_id), group_id);

            // 虛擬帳號/超商代碼資訊
            fields.Add(nameof(bank_id), bank_id);
            fields.Add(nameof(expired_date), expired_date);
            fields.Add(nameof(result_type), result_type);
            fields.Add(nameof(result_content_type), result_content_type);
            fields.Add(nameof(result_content), result_content);

            // 自訂回傳參數
            fields.Add(nameof(echo_0), echo_0);
            fields.Add(nameof(echo_1), echo_1);
            fields.Add(nameof(echo_2), echo_2);
            fields.Add(nameof(echo_3), echo_3);
            fields.Add(nameof(echo_4), echo_4);

            // 舊版相容欄位
            fields.Add(nameof(state), state);
            fields.Add(nameof(msg), msg);
            fields.Add(nameof(store_uid), store_uid);
            fields.Add(nameof(transaction_id), transaction_id);
            fields.Add(nameof(hash), hash);
            fields.Add(nameof(user_name), user_name);
            fields.Add(nameof(user_real_name), user_real_name);
            fields.Add(nameof(user_phone), user_phone);
            fields.Add(nameof(user_email), user_email);
            fields.Add(nameof(pay_type), pay_type);
            fields.Add(nameof(invoice_number), invoice_number);

            return fields;
        }

        #endregion
    }

    #region 輔助類別 (Helper Classes)

    /// <summary>
    /// 高鉅金流處理結果類別
    /// </summary>
    public class MyPayProcessingResult
    {
        /// <summary>處理是否成功</summary>
        public bool IsSuccess { get; set; }
        /// <summary>錯誤訊息</summary>
        public string ErrorMessage { get; set; }
        /// <summary>處理時間</summary>
        public DateTime ProcessingTime { get; set; }
        /// <summary>交易ID</summary>
        public string TransactionId { get; set; }
        /// <summary>訂單ID</summary>
        public string OrderId { get; set; }
        /// <summary>核心欄位資訊</summary>
        public CoreFieldsInfo CoreFields { get; set; }
        /// <summary>交易資訊</summary>
        public TransactionInfo TransactionInfo { get; set; }
        /// <summary>消費者資訊</summary>
        public ConsumerInfo ConsumerInfo { get; set; }
        /// <summary>信用卡資訊</summary>
        public CreditCardInfo CreditCardInfo { get; set; }
        /// <summary>服務資訊</summary>
        public ServiceInfo ServiceInfo { get; set; }
        /// <summary>定期定額資訊</summary>
        public RecurringInfo RecurringInfo { get; set; }
        /// <summary>虛擬帳號資訊</summary>
        public VirtualAccountInfo VirtualAccountInfo { get; set; }
        /// <summary>自訂參數資訊</summary>
        public CustomParametersInfo CustomParameters { get; set; }
        /// <summary>摘要說明</summary>
        public string Summary { get; set; }
    }

    /// <summary>
    /// 核心欄位資訊
    /// </summary>
    public class CoreFieldsInfo
    {
        /// <summary>交易流水號</summary>
        public string Uid { get; set; }
        /// <summary>驗證碼</summary>
        public string Key { get; set; }
        /// <summary>交易回傳碼</summary>
        public string Prc { get; set; }
        /// <summary>訂單編號</summary>
        public string OrderId { get; set; }
        /// <summary>是否有效</summary>
        public bool IsValid { get; set; }
    }

    /// <summary>
    /// 交易資訊
    /// </summary>
    public class TransactionInfo
    {
        /// <summary>完成時間字串</summary>
        public string FinishTime { get; set; }
        /// <summary>完成時間(解析後)</summary>
        public DateTime? ParsedFinishTime { get; set; }
        /// <summary>交易金額字串</summary>
        public string Cost { get; set; }
        /// <summary>交易金額(解析後)</summary>
        public decimal? ParsedCost { get; set; }
        /// <summary>原幣別</summary>
        public string Currency { get; set; }
        /// <summary>實際金額字串</summary>
        public string ActualCost { get; set; }
        /// <summary>實際金額(解析後)</summary>
        public decimal? ParsedActualCost { get; set; }
        /// <summary>實際幣別</summary>
        public string ActualCurrency { get; set; }
        /// <summary>請求點數/金額</summary>
        public string Price { get; set; }
        /// <summary>實際點數/金額</summary>
        public string ActualPrice { get; set; }
        /// <summary>產品代碼</summary>
        public string RechargeCode { get; set; }
        /// <summary>愛心捐款金額</summary>
        public string LoveCost { get; set; }
        /// <summary>回傳訊息</summary>
        public string ReturnMessage { get; set; }
        /// <summary>付款方式</summary>
        public string PaymentMethod { get; set; }
        /// <summary>交易類型</summary>
        public int? TransactionType { get; set; }
    }

    /// <summary>
    /// 消費者資訊
    /// </summary>
    public class ConsumerInfo
    {
        /// <summary>消費者帳號</summary>
        public string UserId { get; set; }
        /// <summary>付款人姓名</summary>
        public string UserName { get; set; }
        /// <summary>付款人真實姓名</summary>
        public string UserRealName { get; set; }
        /// <summary>付款人電話</summary>
        public string UserPhone { get; set; }
        /// <summary>付款人電子郵件</summary>
        public string UserEmail { get; set; }
    }

    /// <summary>
    /// 信用卡資訊
    /// </summary>
    public class CreditCardInfo
    {
        /// <summary>卡號</summary>
        public string CardNo { get; set; }
        /// <summary>授權碼</summary>
        public string AuthCode { get; set; }
        /// <summary>卡別</summary>
        public string CardType { get; set; }
        /// <summary>發卡行</summary>
        public string IssuingBank { get; set; }
        /// <summary>發卡銀行代碼</summary>
        public string IssuingBankUid { get; set; }
        /// <summary>紅利資訊</summary>
        public string RedeemInfo { get; set; }
        /// <summary>分期資訊</summary>
        public string InstallmentInfo { get; set; }
    }

    /// <summary>
    /// 服務資訊
    /// </summary>
    public class ServiceInfo
    {
        /// <summary>是否為經銷商代收費</summary>
        public int? IsAgentCharge { get; set; }
        /// <summary>交易服務類型</summary>
        public int? TransactionMode { get; set; }
        /// <summary>金融服務商名稱</summary>
        public string SupplierName { get; set; }
        /// <summary>金融服務商代碼</summary>
        public string SupplierCode { get; set; }
    }

    /// <summary>
    /// 定期定額資訊
    /// </summary>
    public class RecurringInfo
    {
        /// <summary>扣款名稱</summary>
        public string PaymentName { get; set; }
        /// <summary>扣繳期數</summary>
        public string NumberOfInstallments { get; set; }
        /// <summary>群組編號</summary>
        public string GroupId { get; set; }
    }

    /// <summary>
    /// 虛擬帳號資訊
    /// </summary>
    public class VirtualAccountInfo
    {
        /// <summary>銀行代碼</summary>
        public string BankId { get; set; }
        /// <summary>有效日期字串</summary>
        public string ExpiredDate { get; set; }
        /// <summary>有效日期(解析後)</summary>
        public DateTime? ParsedExpiredDate { get; set; }
        /// <summary>資料格式類型</summary>
        public int? ResultType { get; set; }
        /// <summary>資料內容所屬支付名稱</summary>
        public string ResultContentType { get; set; }
        /// <summary>資料內容</summary>
        public string ResultContent { get; set; }
    }

    /// <summary>
    /// 自訂參數資訊
    /// </summary>
    public class CustomParametersInfo
    {
        /// <summary>自訂參數 0</summary>
        public string Echo0 { get; set; }
        /// <summary>自訂參數 1</summary>
        public string Echo1 { get; set; }
        /// <summary>自訂參數 2</summary>
        public string Echo2 { get; set; }
        /// <summary>自訂參數 3</summary>
        public string Echo3 { get; set; }
        /// <summary>自訂參數 4</summary>
        public string Echo4 { get; set; }
    }

    /// <summary>
    /// 驗證結果
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// 是否有效
        /// </summary>
        public bool IsValid { get; set; }
        
        /// <summary>
        /// 錯誤訊息列表（致命錯誤）
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();
      
        /// <summary>
        /// 警告訊息列表（非致命錯誤，以 ⚠️ 開頭的訊息）
        /// </summary>
        public List<string> Warnings 
        { 
      get 
 {
           // 從 Errors 中篩選出以 ⚠️ 開頭的訊息作為警告
     return Errors.Where(e => e.StartsWith("⚠️")).ToList();
     }
 }
        
        /// <summary>
   /// 驗證等級
  /// </summary>
   public ValidationLevel Level 
        { 
    get 
            {
     if (!IsValid) return ValidationLevel.Error;
         if (Warnings.Any()) return ValidationLevel.Warning;
       return ValidationLevel.Success;
            }
   }
    }

    /// <summary>
    /// 驗證等級枚舉
 /// </summary>
    public enum ValidationLevel
    {
        /// <summary>
   /// 成功 - 所有欄位都正確且完整
 /// </summary>
        Success,
      
        /// <summary>
        /// 警告 - 驗證通過但有建議填寫的欄位
        /// </summary>
    Warning,
        
        /// <summary>
        /// 錯誤 - 驗證失敗，缺少必要欄位
        /// </summary>
      Error
    }

    #endregion

    //    📋 欄位分類說明
    //🔴 必要欄位(Required Fields)
    //•	state - 交易狀態(1:成功, 0:失敗)
    //•	msg - 回傳訊息
    //•	order_id - 商店訂單編號
    //•	store_uid - 商店代號
    //•	transaction_id - 金流平台交易單號
    //•	hash - 簽名驗證碼
    //💰 基本交易資訊(Basic Transaction Info)
    //•	cost - 交易金額
    //•	currency - 交易幣別
    //•	pay_type - 付款方式類型
    //•	pay_time - 付款完成時間
    //•	transaction_fee - 交易手續費
    //•	actual_cost - 實際收款金額
    //👤 消費者資訊(Consumer Information)
    //•	user_name - 付款人姓名
    //•	user_real_name - 付款人真實姓名
    //•	user_phone - 付款人電話
    //•	user_cellphone - 付款人行動電話
    //•	user_email - 付款人電子郵件
    //•	user_zipcode - 付款人郵遞區號
    //•	user_address - 付款人地址
    //•	user_sn - 付款人身分證號
    //•	user_birthday - 付款人生日
    //💳 信用卡相關資訊(Credit Card Info)
    //•	card_first_six - 信用卡卡號前六碼
    //•	card_last_four - 信用卡卡號後四碼
    //•	auth_code - 信用卡授權碼
    //•	auth_code_msg - 信用卡授權碼回應訊息
    //•	card_issuer_code - 信用卡發卡銀行代碼
    //•	card_issuer_name - 信用卡發卡銀行名稱
    //•	card_type - 信用卡類型
    //•	installment - 分期期數
    //•	bonus_points - 紅利折抵點數
    //🏧 ATM/虛擬帳號相關資訊(ATM/Virtual Account Info)
    //•	atm_account - ATM 虛擬帳號
    //•	atm_bank_code - ATM 銀行代碼
    //•	atm_bank_name - ATM 銀行名稱
    //•	atm_expire_date - ATM 繳費期限
    //🏪 超商代碼相關資訊(Convenience Store Info)
    //•	cvs_code - 超商代碼
    //•	cvs_type - 超商類型
    //•	cvs_expire_date - 超商繳費期限
    //📱 電子錢包相關資訊(E-Wallet Info)
    //•	wallet_type - 電子錢包類型
    //•	wallet_transaction_id - 電子錢包交易號
    //•	wallet_user_id - 電子錢包用戶ID
    //🧾 發票相關資訊(Invoice Info)
    //•	invoice_number - 發票號碼
    //•	invoice_state - 發票開立狀態
    //•	invoice_type - 發票類型
    //•	invoice_tax_id - 統一編號
    //•	invoice_title - 發票抬頭
    //•	invoice_love_code - 愛心碼
    //•	invoice_carrier_type - 載具類型
    //•	invoice_carrier_no - 載具號碼
    //🔄 定期定額相關資訊(Recurring Payment Info)
    //•	regular_group_id - 定期定額群組ID
    //•	regular_total_periods - 定期定額總期數
    //•	regular_current_period - 定期定額目前期數
    //•	regular_next_date - 下次扣款日期
    //•	regular_status - 定期定額狀態
    //⚙️ 系統參數(System Parameters)
    //•	echo0 ~echo4 - 自訂參數
    //•	pay_ip - 付款來源 IP
    //•	device_type - 付款裝置類型
    //•	user_agent - 使用者代理字串
    //🛡️ 風險控制相關(Risk Control)
    //•	risk_score - 風險評分
    //•	risk_level - 風險等級
    //•	three_d_secure - 3D驗證結果
    //📝 其他資訊(Additional Info)
    //•	bank_code - 銀行回傳碼
    //•	bank_msg - 銀行回傳訊息
    //•	process_time - 處理時間
    //•	note - 備註欄位
    //•	ext_data - 擴充資料
    //🎯 優點
    //🔄 非即時交易回傳資訊(Non-Instant Transaction Return Info)
    //📅 時間戳記相關
    //•	create_time - 交易建立時間
    //•	update_time - 交易更新時間
    //•	confirm_time - 交易確認時間
    //•	settle_time - 交易清算時間
    //•	close_time - 交易關閉時間
    //•	refund_time - 退款時間
    //•	notify_last_time - 回調最後嘗試時間
    //•	expire_time - 交易有效期限
    //•	reconcile_time - 對帳時間
    //📊 狀態追蹤
    //•	detail_state - 交易詳細狀態碼
    //•	detail_msg - 交易詳細狀態訊息
    //•	fail_code - 交易失敗原因代碼
    //•	fail_msg - 交易失敗原因描述
    //•	notify_status - 回調狀態
    //•	is_expired - 交易逾期狀態
    //•	settle_state - 清算狀態
    //•	reconcile_state - 對帳狀態
    //💰 退款相關
    //•	refund_state - 退款狀態
    //•	refund_amount - 退款金額
    //•	refund_transaction_id - 退款交易單號
    //•	refund_order_id - 商店退款單號
    //🏦 清算對帳
    //•	settle_amount - 清算金額
    //•	settle_fee - 清算手續費
    //•	settle_batch_no - 清算批次號
    //•	reconcile_batch_no - 對帳批次號
    //🔔 回調機制
    //•	notify_retry_count - 回調重試次數
    //•	notify_url - 回調URL
    //📋 交易資訊
    //•	batch_no - 交易批次號
    //•	merchant_ref - 商戶參考號
    //•	payment_channel - 付款通道
    //•	payment_sub_channel - 付款子通道
    //•	transaction_source - 交易來源
    //📦 JSON 格式資訊
    //•	goods_info - 商品資訊
    //•	logistics_info - 物流資訊
    //•	promotion_info - 促銷資訊
    //•	coupon_info - 優惠券資訊
    //•	split_info - 分潤資訊
    //•	risk_info - 風控資訊
    //•	merchant_info - 商戶自定義資訊
    //⚙️ 系統資訊
    //•	environment - 交易環境(sandbox/production)
    //•	api_version - API版本
    //•	sdk_version - SDK版本
    //•	platform_info - 平台資訊
    //🎯 特色與優勢
    //1.	完整涵蓋：包含所有可能的非即時交易回傳欄位
    //2.	詳細文檔：每個欄位都有完整的中文註解說明
    //3.	分類清楚：按功能分組，便於理解和維護
    //4.	相容性：完全相容.NET Framework 4.7.1 和 C# 7.3
    //5.	擴展性：可支援未來新增的回傳欄位
    //6.	類型安全：使用適當的資料型別(string, int?, etc.)
    //🔧 使用場景
    //這些欄位主要用於處理：
    //•	ATM 轉帳：需要等待銀行確認的非即時交易
    //•	超商代碼：需要等待消費者繳費的延遲交易
    //•	虛擬帳號：需要等待入金確認的交易
    //•	定期定額：需要追蹤多期扣款狀態
    //•	退款處理：需要追蹤退款狀態和時間
    //•	清算對帳：需要與銀行進行批次對帳
}
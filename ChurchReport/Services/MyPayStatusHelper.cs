using System;
using Microsoft.Extensions.Logging;

namespace ChurchReport.Services
{
    /// <summary>
    /// MyPay 狀態判斷與訊息轉換輔助服務
    /// 負責交易狀態判斷、錯誤訊息轉換、時間解析等功能
    /// </summary>
    public class MyPayStatusHelper
    {
        private readonly ILogger<MyPayStatusHelper> _logger;

        public MyPayStatusHelper(ILogger<MyPayStatusHelper> logger)
        {
            _logger = logger;
        }

        #region 交易狀態判斷

        /// <summary>
        /// ========================================
        /// 判斷交易是否成功
        /// ========================================
        /// 
        /// 【成功代碼說明】
        /// - 250: 付款成功（最常見的成功代碼）
        /// - 290: 交易成功但資訊不符
        /// - 600: 結帳完成
        /// </summary>
        public bool IsSuccessfulPaymentStatus(string prc)
        {
            if (string.IsNullOrWhiteSpace(prc)) return false;

            switch (prc)
            {
                case "250": // 付款成功
                case "290": // 交易成功但資訊不符
                case "600": // 結帳完成
                    return true;
                default:
                    return false;
            }
        }

        #endregion

        #region 錯誤訊息處理

        /// <summary>
        /// ========================================
        /// 建立失敗訊息文字
        /// ========================================
        /// </summary>
        public string BuildFailureMessage(string msg, string errorCode, string retCode)
        {
            var message = "付款失敗";

            if (!string.IsNullOrWhiteSpace(msg))
            {
                message = $"付款失敗：{msg}";
            }
            else if (!string.IsNullOrWhiteSpace(errorCode) || !string.IsNullOrWhiteSpace(retCode))
            {
                string friendly = GetFriendlyErrorMessage(errorCode, retCode);

                if (!string.IsNullOrWhiteSpace(friendly))
                {
                    message = $"付款失敗：{friendly}";
                }
                else
                {
                    message = $"付款失敗 (錯誤代碼: {errorCode ?? retCode})";
                }
            }
            else
            {
                message = "付款失敗，請稍後再試或聯繫教會辦公室。";
            }

            return message;
        }

        /// <summary>
        /// ========================================
        /// 取得友善的錯誤訊息
        /// ========================================
        /// </summary>
        public string GetFriendlyErrorMessage(string errorCode, string retCode)
        {
            string code = errorCode ?? retCode ?? "";

            switch (code.ToUpper())
            {
                case "CARD_DECLINED":
                case "51":
                    return "信用卡被拒絕，請確認卡片狀態或聯繫發卡銀行";

                case "INSUFFICIENT_FUNDS":
                case "05":
                    return "信用卡額度不足，請使用其他卡片或聯繫發卡銀行";

                case "EXPIRED_CARD":
                case "54":
                    return "信用卡已過期，請使用其他有效卡片";

                case "INVALID_CARD":
                case "14":
                    return "信用卡號碼錯誤，請檢查卡號是否正確";

                case "INVALID_CVV":
                case "CVV_ERROR":
                    return "安全碼(CVV)錯誤，請重新輸入";

                case "CARD_LOST_STOLEN":
                case "43":
                    return "此卡片已被列為遺失或被盜，請聯繫發卡銀行";

                case "TRANSACTION_NOT_PERMITTED":
                case "57":
                    return "此交易不被允許，請聯繫發卡銀行";

                case "EXCEEDED_LIMIT":
                case "61":
                    return "超過信用卡交易限額，請聯繫發卡銀行";

                case "TIMEOUT":
                case "NETWORK_ERROR":
                    return "連線逾時或網路錯誤，請稍後再試";

                case "SYSTEM_ERROR":
                case "96":
                    return "系統錯誤，請稍後再試或聯繫客服";

                case "CANCELLED":
                case "USER_CANCELLED":
                    return "交易已被取消";

                case "3D_SECURE_FAILED":
                case "3DS_FAILED":
                    return "3D驗證失敗，請重新進行驗證";

                default:
                    return null;
            }
        }

        #endregion

        #region 狀態訊息轉換

        /// <summary>
        /// ========================================
        /// 取得交易狀態訊息
        /// ========================================
        /// </summary>
        public string GetPaymentStatusMessage(string prc)
        {
            if (string.IsNullOrWhiteSpace(prc)) return "付款狀態未知";

            switch (prc)
            {
                case "100": return "資料錯誤 - MYPAYLINK收到資料，但是格式或資料錯誤";
                case "200": return "資料正確 - MYPAYLINK收到正確資料，會接續下一步交易";
                case "220": return "取消成功";
                case "230": return "退款成功";
                case "250": return "付款成功";
                case "290": return "交易成功但資訊不符";
                case "600": return "結帳完成";
                case "260": return "交易成功，尚未付款完成(超商代碼)";
                case "265": return "訂單綁定";
                case "270": return "交易成功，尚未付款完成(虛擬帳號)";
                case "275": return "交易成功，待審核(無卡分期)";
                case "280": return "交易成功，尚未付款完成(WebATM)";
                case "300": return "交易失敗";
                case "380": return "逾期交易";
                case "400": return "系統錯誤";
                case "A0001": return "交易待確認";
                case "A0002": return "放棄交易";
                case "B200": return "執行成功";
                case "B500": return "執行失敗";
                default: return $"未知狀態碼：{prc}";
            }
        }

        #endregion

        #region 時間與付款方式解析

        /// <summary>
        /// ========================================
        /// 解析完成時間字串
        /// ========================================
        /// </summary>
        public DateTime ParseFinishTime(string finishtime)
        {
            if (string.IsNullOrWhiteSpace(finishtime) || finishtime.Length != 14)
            {
                return DateTime.Now;
            }

            try
            {
                int year = int.Parse(finishtime.Substring(0, 4));
                int month = int.Parse(finishtime.Substring(4, 2));
                int day = int.Parse(finishtime.Substring(6, 2));
                int hour = int.Parse(finishtime.Substring(8, 2));
                int minute = int.Parse(finishtime.Substring(10, 2));
                int second = int.Parse(finishtime.Substring(12, 2));

                return new DateTime(year, month, day, hour, minute, second);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"ParseFinishTime:解析時間失敗 - FinishTime: {finishtime}");
                return DateTime.Now;
            }
        }

        /// <summary>
        /// ========================================
        /// 取得付款方式名稱
        /// ========================================
        /// </summary>
        public string GetPaymentMethodName(string pfn)
        {
            if (string.IsNullOrWhiteSpace(pfn)) return "未知支付工具";

            string k = pfn.ToUpper();

            switch (k)
            {
                case "1":
                case "CREDITCARD":
                    return "信用卡";

                case "6":
                case "E_COLLECTION":
                    return "虛擬帳號";

                case "3":
                case "CSTORECODE":
                    return "超商代碼";

                case "8":
                case "CREDITCARD_INSTALLMENT":
                    return "信用卡分期";

                default:
                    return $"支付工具 {pfn}";
            }
        }

        #endregion
    }
}

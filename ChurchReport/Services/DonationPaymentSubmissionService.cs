using System;
using ChurchReport.Models;

namespace ChurchReport.Services
{
    /// <summary>
    /// ChurchReport 奉獻付款送出流程的產品層判斷服務。
    ///
    /// 這個類別不呼叫銀行 provider，也不更新 CRM；它只負責兩件事：
    /// 1. 驗證奉獻付款表單是否足以建立付款。
    /// 2. 將 DonationPaymentProcessor 回傳的既有文字結果分類成奉獻頁 AJAX 需要的狀態。
    ///
    /// 抽出這個服務後，DonationPaymentManager 可以專注於協調 Controller 回應，
    /// 而不必在大型類別內散落一串 StartsWith / Contains 判斷。
    /// </summary>
    public sealed class DonationPaymentSubmissionService
    {
        /// <summary>
        /// 驗證奉獻表單。回傳空字串代表驗證通過；回傳文字代表要直接顯示給使用者的錯誤。
        /// </summary>
        public static string ValidateDonationForm(DonationPaymentFormModel donationModel)
        {
            if (donationModel == null)
            {
                return "未輸入奉獻金額";
            }

            if (donationModel.Category == "節期獻金" && string.IsNullOrWhiteSpace(donationModel.Others))
            {
                return "錯誤:沒有選擇節期!";
            }

            if (donationModel.Category == "特別奉獻" && string.IsNullOrWhiteSpace(donationModel.Others))
            {
                return "錯誤:沒有選擇特別奉獻的項目!";
            }

            return donationModel.Amount > 0 ? string.Empty : "未輸入奉獻金額";
        }

        /// <summary>
        /// 將既有建立付款結果文字轉成奉獻頁 AJAX 約定。
        /// 這裡保留舊前端期待的 status/message/DedicationResult/PayWay 形狀，避免重構破壞畫面。
        /// </summary>
        public static DonationPaymentSubmissionResult ClassifyCreatePaymentResult(string dedicationResult)
        {
            dedicationResult ??= string.Empty;

            if (dedicationResult.StartsWith("信用卡繳費失敗!", StringComparison.Ordinal) ||
                dedicationResult.StartsWith("信用卡定期定額建立失敗!", StringComparison.Ordinal) ||
                dedicationResult.StartsWith("行動支付付款失敗!", StringComparison.Ordinal) ||
                dedicationResult.StartsWith("LinePay付款失敗!", StringComparison.Ordinal))
            {
                return DonationPaymentSubmissionResult.Error(dedicationResult);
            }

            if (!dedicationResult.Contains("*** 請依照訊息付款 ***", StringComparison.Ordinal))
            {
                return DonationPaymentSubmissionResult.Success(
                    "正在處理您的奉獻中.....",
                    dedicationResult,
                    "信用卡");
            }

            return DonationPaymentSubmissionResult.Success(
                "正在處理您的奉獻中.....",
                dedicationResult,
                "虛擬帳號");
        }
    }

    /// <summary>
    /// 奉獻付款送出後給 MVC Action 包成 JSON 的穩定結果。
    /// 使用明確 DTO 取代匿名物件，可讓分類邏輯被單元測試直接驗證。
    /// </summary>
    public sealed record DonationPaymentSubmissionResult(
        string Status,
        string Message,
        string DedicationResult,
        string PayWay)
    {
        public static DonationPaymentSubmissionResult Error(string message)
        {
            return new DonationPaymentSubmissionResult("2", message, string.Empty, string.Empty);
        }

        public static DonationPaymentSubmissionResult Success(string message, string dedicationResult, string payWay)
        {
            return new DonationPaymentSubmissionResult("1", message, dedicationResult, payWay);
        }
    }
}

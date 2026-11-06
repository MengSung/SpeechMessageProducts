/*
 * 檔案整體說明
 *
 * 目的與定位：ChurchReport 奉獻付款送出流程的產品層判斷服務。 這個類別不呼叫銀行 provider，也不更新 CRM；它只負責兩件事： 1. 驗證奉獻付款表單是否足以建立付款。 2. 將 
 * DonationPaymentProcessor 回傳的既有文字結果分類成奉獻頁 AJAX 需要的狀態。 抽出這個服務後，DonationPaymentManager 可以專注於協調 Controller 回應， 
 * 而不必在大型類別內散落一串 StartsWith / Contains 判斷。
 *
 * 主要功能與流程：
 * 本檔宣告 DonationPaymentSubmissionService、DonationPaymentSubmissionResult。
 * - 驗證奉獻表單。回傳空字串代表驗證通過；回傳文字代表要直接顯示給使用者的錯誤。
 * - 將既有建立付款結果文字轉成奉獻頁 AJAX 約定。 這裡保留舊前端期待的 status/message/DedicationResult/PayWay 形狀，避免重構破壞畫面。
 * 主要操作入口包含 ValidateDonationForm、ClassifyCreatePaymentResult、DonationPaymentSubmissionResult、Error、Success。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using ChurchReport.Models;
using System.Linq;

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

            var lines = DonationLineItemNormalizer.Normalize(donationModel);
            if (lines.Count > DonationLineItemNormalizer.MaxLines)
                return $"一次最多只能奉獻 {DonationLineItemNormalizer.MaxLines} 個類別";
            if (lines.Count == 0)
                return "未輸入奉獻金額";
            if (string.Equals(donationModel.PayWay, "信用卡定期定額(每個月)", StringComparison.Ordinal) && lines.Count > 1)
                return "定期定額一次只能設定一個類別";
            if (string.Equals(donationModel.PayWay, "ATM轉帳/匯款", StringComparison.Ordinal) && lines.Count > 1)
                return "ATM 多類別付款尚未開放，請分開建立奉獻或改用信用卡";
            var duplicateCategory = lines
                .Where(x => !string.IsNullOrWhiteSpace(x.Category))
                .GroupBy(x => x.Category.Trim(), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateCategory != null)
                return $"奉獻類別不可重複：{duplicateCategory.Key}";
            foreach (var line in lines)
            {
                if (line.Amount < 0) return "奉獻金額不可為負數";
                if (line.Category == "節期獻金" && string.IsNullOrWhiteSpace(line.Others)) return "錯誤:沒有選擇節期!";
                if (line.Category == "特別奉獻" && string.IsNullOrWhiteSpace(line.Others)) return "錯誤:沒有選擇特別奉獻的項目!";
            }
            if (lines.All(x => x.Amount <= 0)) return "未輸入奉獻金額";
            return string.Empty;

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

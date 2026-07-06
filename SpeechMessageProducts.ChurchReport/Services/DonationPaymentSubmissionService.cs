// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/DonationPaymentSubmissionService.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class DonationPaymentSubmissionService、record DonationPaymentSubmissionResult
// 主要成員：ValidateDonationForm、ClassifyCreatePaymentResult、DonationPaymentSubmissionResult、Error、Success
// 引用命名空間：System、ChurchReport.Models
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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

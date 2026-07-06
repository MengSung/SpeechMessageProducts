// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/PaymentMessageBuilder.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class PaymentMessageBuilder
// 主要成員：BuildDedicationSuccessMessage、BuildDedicationFailureMessage、BuildCoursePaymentSuccessMessage、BuildCoursePaymentFailureMessage、BuildGeneralPaymentSuccessMessage、BuildGeneralPaymentFailureMessage
// 引用命名空間：System
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;

namespace ChurchReport.Services
{
    /// <summary>
    /// ChurchReport 付款 LINE 訊息建立服務
    /// 這裡負責把 provider-neutral 付款結果轉成 ChurchReport 使用者看得懂的文字，例如奉獻、課程繳費與一般繳費通知；訊息文案屬於產品流程，不屬於共用金流核心。
    /// </summary>
    public class PaymentMessageBuilder
    {
        #region 奉獻類型訊息建立

        /// <summary>
        /// ========================================
        /// 建立奉獻成功訊息
        /// ========================================
        ///
        /// 【功能說明】
        /// 根據奉獻類型和付款資訊生成完整的成功通知訊息
        ///
        /// 【訊息內容】
        /// - 感謝詞與問候語
        /// - 奉獻類別（十一奉獻、感恩奉獻等）
        /// - 訂單與交易編號
        /// - 付款金額與時間
        /// - 祝福語
        ///
        /// </summary>
        public string BuildDedicationSuccessMessage(
            string fullName,
            string orderId,
            string transactionId,
            decimal amount,
            string dedicationCategory,
            DateTime paymentTime)
        {
            var msg = $"【金流付款成功通知】{Environment.NewLine}{Environment.NewLine}";
            msg += $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}";
            msg += $"您的奉獻已成功完成，感謝您的支持！{Environment.NewLine}{Environment.NewLine}";
            msg += $"付款資訊：{Environment.NewLine}";
            msg += $"姓名：{fullName}{Environment.NewLine}";
            msg += $"奉獻類別：{dedicationCategory}{Environment.NewLine}";
            msg += $"訂單編號：{orderId}{Environment.NewLine}";

            if (!string.IsNullOrWhiteSpace(transactionId))
                msg += $"交易編號：{transactionId}{Environment.NewLine}";

            msg += $"付款金額：NT$ {amount:N0}{Environment.NewLine}";
            msg += $"付款時間：{paymentTime:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}";
            msg += $"付款方式：信用卡{Environment.NewLine}{Environment.NewLine}";
            msg += $"願上帝賜福與您！";

            return msg;
        }

        /// <summary>
        /// ========================================
        /// 建立奉獻失敗訊息
        /// ========================================
        /// </summary>
        public string BuildDedicationFailureMessage(
            string fullName,
            string orderId,
            string transactionId,
            decimal amount,
            string dedicationCategory,
            DateTime paymentTime,
            string statusMessage)
        {
            var msg = $"【金流付款失敗通知】{Environment.NewLine}{Environment.NewLine}";
            msg += $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}";
            msg += $"很抱歉，您的奉獻付款未能完成。{Environment.NewLine}{Environment.NewLine}";
            msg += $"失敗原因：{statusMessage}{Environment.NewLine}{Environment.NewLine}";
            msg += $"付款資訊：{Environment.NewLine}";
            msg += $"姓名：{fullName}{Environment.NewLine}";
            msg += $"奉獻類別：{dedicationCategory}{Environment.NewLine}";
            msg += $"訂單編號：{orderId}{Environment.NewLine}";

            if (!string.IsNullOrWhiteSpace(transactionId))
                msg += $"交易編號：{transactionId}{Environment.NewLine}";

            msg += $"應付金額：NT$ {amount:N0}{Environment.NewLine}";
            msg += $"嘗試時間：{paymentTime:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}{Environment.NewLine}";
            msg += $"您可以：{Environment.NewLine}";
            msg += $"1. 重新嘗試付款{Environment.NewLine}";
            msg += $"2. 更換其他信用卡{Environment.NewLine}";
            msg += $"3. 聯繫教會辦公室尋求協助{Environment.NewLine}{Environment.NewLine}";
            msg += $"如有任何問題，請隨時與我們聯繫。";

            return msg;
        }

        #endregion

        #region 課程繳費類型訊息建立

        /// <summary>
        /// ========================================
        /// 建立課程繳費成功訊息
        /// ========================================
        /// </summary>
        public string BuildCoursePaymentSuccessMessage(
            string fullName,
            string orderId,
            string transactionId,
            decimal amount,
            string courseName,
            string courseSchedule,
            string courseLocation,
            DateTime paymentTime)
        {
            var msg = $"【金流付款成功通知】{Environment.NewLine}{Environment.NewLine}";
            msg += $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}";
            msg += $"您的課程繳費已成功完成！{Environment.NewLine}{Environment.NewLine}";
            msg += $"課程資訊：{Environment.NewLine}";
            msg += $"姓名：{fullName}{Environment.NewLine}";
            msg += $"課程名稱：{courseName}{Environment.NewLine}";

            if (!string.IsNullOrWhiteSpace(courseSchedule))
                msg += $"上課時間：{courseSchedule}{Environment.NewLine}";

            if (!string.IsNullOrWhiteSpace(courseLocation))
                msg += $"上課地點：{courseLocation}{Environment.NewLine}";

            msg += $"{Environment.NewLine}付款資訊：{Environment.NewLine}";
            msg += $"訂單編號：{orderId}{Environment.NewLine}";

            if (!string.IsNullOrWhiteSpace(transactionId))
                msg += $"交易編號：{transactionId}{Environment.NewLine}";

            msg += $"繳費金額：NT$ {amount:N0}{Environment.NewLine}";
            msg += $"付款時間：{paymentTime:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}";
            msg += $"付款方式：信用卡{Environment.NewLine}{Environment.NewLine}";
            msg += $"期待在課程中與您相見！";

            return msg;
        }

        /// <summary>
        /// ========================================
        /// 建立課程繳費失敗訊息
        /// ========================================
        /// </summary>
        public string BuildCoursePaymentFailureMessage(
            string fullName,
            string orderId,
            string transactionId,
            decimal amount,
            string courseName,
            string courseSchedule,
            string courseLocation,
            DateTime paymentTime,
            string statusMessage)
        {
            var msg = $"【金流付款失敗通知】{Environment.NewLine}{Environment.NewLine}";
            msg += $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}";
            msg += $"很抱歉，您的課程繳費未能完成。{Environment.NewLine}{Environment.NewLine}";
            msg += $"失敗原因：{statusMessage}{Environment.NewLine}{Environment.NewLine}";
            msg += $"課程資訊：{Environment.NewLine}";
            msg += $"姓名：{fullName}{Environment.NewLine}";
            msg += $"課程名稱：{courseName}{Environment.NewLine}";

            if (!string.IsNullOrWhiteSpace(transactionId))
                msg += $"交易編號：{transactionId}{Environment.NewLine}";

            msg += $"應繳金額：NT$ {amount:N0}{Environment.NewLine}";
            msg += $"嘗試時間：{paymentTime:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}{Environment.NewLine}";
            msg += $"您可以：{Environment.NewLine}";
            msg += $"1. 重新嘗試付款{Environment.NewLine}";
            msg += $"2. 更換其他信用卡{Environment.NewLine}";
            msg += $"3. 聯繫教會辦公室尋求協助{Environment.NewLine}{Environment.NewLine}";
            msg += $"如有任何問題，請隨時與我們聯繫。";

            return msg;
        }

        #endregion

        #region 一般繳費類型訊息建立

        /// <summary>
        /// ========================================
        /// 建立一般繳費成功訊息
        /// ========================================
        /// </summary>
        public string BuildGeneralPaymentSuccessMessage(
            string fullName,
            string orderId,
            string transactionId,
            decimal amount,
            string itemName,
            DateTime paymentTime)
        {
            var msg = $"【金流付款成功通知】{Environment.NewLine}{Environment.NewLine}";
            msg += $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}";
            msg += $"您的付款已成功完成！{Environment.NewLine}{Environment.NewLine}";
            msg += $"付款資訊：{Environment.NewLine}";
            msg += $"姓名：{fullName}{Environment.NewLine}";
            msg += $"項目：{itemName}{Environment.NewLine}";
            msg += $"訂單編號：{orderId}{Environment.NewLine}";

            if (!string.IsNullOrWhiteSpace(transactionId))
                msg += $"交易編號：{transactionId}{Environment.NewLine}";

            msg += $"付款金額：NT$ {amount:N0}{Environment.NewLine}";
            msg += $"付款時間：{paymentTime:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}";
            msg += $"付款方式：信用卡{Environment.NewLine}{Environment.NewLine}";
            msg += $"感謝您的支持！";

            return msg;
        }

        /// <summary>
        /// ========================================
        /// 建立一般繳費失敗訊息
        /// ========================================
        /// </summary>
        public string BuildGeneralPaymentFailureMessage(
            string fullName,
            string orderId,
            string transactionId,
            decimal amount,
            string itemName,
            DateTime paymentTime,
            string statusMessage)
        {
            var msg = $"【金流付款失敗通知】{Environment.NewLine}{Environment.NewLine}";
            msg += $"親愛的 {fullName}，您好！{Environment.NewLine}{Environment.NewLine}";
            msg += $"很抱歉，您的付款未能完成。{Environment.NewLine}{Environment.NewLine}";
            msg += $"失敗原因：{statusMessage}{Environment.NewLine}{Environment.NewLine}";
            msg += $"付款資訊：{Environment.NewLine}";
            msg += $"姓名：{fullName}{Environment.NewLine}";
            msg += $"項目：{itemName}{Environment.NewLine}";
            msg += $"訂單編號：{orderId}{Environment.NewLine}";

            if (!string.IsNullOrWhiteSpace(transactionId))
                msg += $"交易編號：{transactionId}{Environment.NewLine}";

            msg += $"應付金額：NT$ {amount:N0}{Environment.NewLine}";
            msg += $"嘗試時間：{paymentTime:yyyy/MM/dd HH:mm:ss}{Environment.NewLine}{Environment.NewLine}";
            msg += $"您可以：{Environment.NewLine}";
            msg += $"1. 重新嘗試付款{Environment.NewLine}";
            msg += $"2. 更換其他信用卡{Environment.NewLine}";
            msg += $"3. 聯繫教會辦公室尋求協助{Environment.NewLine}{Environment.NewLine}";
            msg += $"如有任何問題，請隨時與我們聯繫。";

            return msg;
        }

        #endregion
    }
}

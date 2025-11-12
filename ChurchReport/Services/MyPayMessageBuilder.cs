using System;

namespace ChurchReport.Services
{
    /// <summary>
    /// MyPay LINE 訊息建立服務
    /// 負責生成各種類型的 LINE 通知訊息
    /// </summary>
    public class MyPayMessageBuilder
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

using ChurchReport.Models;
using Microsoft.Xrm.Sdk;
using System;
using System.Threading.Tasks;

namespace ChurchReport.WebServiceConnector
{
    /// <summary>
    /// 金流處理器 - 付款流程處理模組
    ///
    /// 【職責】
    /// - 信用卡付款處理
    /// - ATM 轉帳處理
    /// - 行動支付處理
    /// - LinePay 處理
    /// - 定期定額處理
    ///
    /// 【設計原則】
    /// - 策略模式：根據付款方式選擇處理策略
    /// - 模板方法：統一付款處理流程
    /// </summary>
    public partial class DonationPaymentProcessor
    {
        #region ===== 信用卡付款 =====

        /// <summary>
        /// 處理信用卡/銀聯卡付款
        /// </summary>
        private async Task<string> ProcessCreditCardPayment(Entity LineLoginContact, QpayModel QpayModel, string orderDate)
        {
            var feeId = CreateFee(LineLoginContact, QpayModel, false);
            var feeEntity = ToolUtility.RetrieveEntity("new_fee", feeId);

            // 判斷信用卡類型
            var payTypeSub = QpayModel.PayWay == "銀聯卡" ? "CUP" : "ONE";

            var createdCardOrder = await CreOrderCard(
                QpayModel.Amount,
                $"{QpayModel.Category}-{QpayModel.FullName}",
                orderDate,
                feeId.ToString(),
                "C", // 信用卡
                payTypeSub,
                "",
                0,
                "M",
                1,
                "收費單",
                LineLoginContact,
                QpayModel.SelectedCreditCard
            );

            if (createdCardOrder?.CardParam?.CardPayURL != null)
            {
                UpdateFee(ref feeEntity, createdCardOrder.OrderNo, "C" + orderDate, "", "");
                return createdCardOrder.CardParam.CardPayURL;
            }

            return $"信用卡繳費失敗! {createdCardOrder?.Description}";
        }

        #endregion

        #region ===== 定期定額付款 =====

        /// <summary>
        /// 處理信用卡定期定額扣款
        /// </summary>
        private async Task<string> ProcessRecurringPayment(Entity LineLoginContact, QpayModel QpayModel, string orderDate)
        {
            // 建立認獻單
            var dedicationBookingId = CreateDedicationBooking(LineLoginContact, QpayModel);
            var dedicationBookingEntity = ToolUtility.RetrieveEntity("new_dedication_booking", dedicationBookingId);

            var createdCardOrder = await CreOrderCard(
                QpayModel.Amount,
                $"{QpayModel.Category}-{QpayModel.FullName}",
                orderDate,
                dedicationBookingId.ToString(),
                "C",
                "REGULAR", // 定期定額
                "",
                TransferToDeductTotalNum(QpayModel.DeductTotalNumber),
                "M",
                1,
                "認獻單",
                LineLoginContact,
                QpayModel.SelectedCreditCard
            );

            if (createdCardOrder?.CardParam?.CardPayURL != null)
            {
                if (createdCardOrder.Status == "S")
                {
                    UpdateFee(ref dedicationBookingEntity, createdCardOrder.OrderNo, "C" + orderDate, "", "");
                    return createdCardOrder.CardParam.CardPayURL;
                }
                else
                {
                    UpdateFee(ref dedicationBookingEntity, createdCardOrder.Description, "C" + orderDate, "", "");
                    return $"信用卡繳費失敗! {createdCardOrder.Description}";
                }
            }
            else
            {
                // 認獻單狀態 = 啟動失敗
                ToolUtility.SetOptionSetAttribute(ref dedicationBookingEntity, "new_dedication_booking_status", 100000003);
                ToolUtility.SetEntityStringAttribute(ref dedicationBookingEntity, "new_explain", "建立永豐信用卡訂單時就失敗了");
                ToolUtility.UpdateEntity(dedicationBookingEntity);

                return $"信用卡定期定額建立失敗! {createdCardOrder?.Description}";
            }
        }

        #endregion

        #region ===== 行動支付 =====

        /// <summary>
        /// 處理行動支付
        /// </summary>
        private async Task<string> ProcessMobilePayment(Entity LineLoginContact, QpayModel QpayModel, string orderDate)
        {
            var feeId = CreateFee(LineLoginContact, QpayModel, false);
            var feeEntity = ToolUtility.RetrieveEntity("new_fee", feeId);

            var createdMobileOrder = await CreOrderCard(
                QpayModel.Amount,
                $"{QpayModel.Category}-{QpayModel.FullName}",
                orderDate,
                feeId.ToString(),
                "M", // 行動支付
                "ONE",
                "",
                0,
                "M",
                1,
                "收費單",
                LineLoginContact,
                QpayModel.SelectedCreditCard
            );

            if (createdMobileOrder?.MobileParam?.MobilePayURL != null)
            {
                UpdateFee(ref feeEntity, createdMobileOrder.OrderNo, "C" + orderDate, "", "");
                return createdMobileOrder.MobileParam.MobilePayURL;
            }

            UpdateFee(ref feeEntity, createdMobileOrder.Description, "C" + orderDate, "", "");
            return $"行動支付付款失敗! {createdMobileOrder?.Description}";
        }

        #endregion

        #region ===== LinePay =====

        /// <summary>
        /// 處理 LinePay 付款
        /// </summary>
        private async Task<string> ProcessLinePayPayment(Entity LineLoginContact, QpayModel QpayModel, string orderDate)
        {
            var feeId = CreateFee(LineLoginContact, QpayModel, false);
            var feeEntity = ToolUtility.RetrieveEntity("new_fee", feeId);

            var createdLinePayOrder = await CreOrderCard(
                QpayModel.Amount,
                $"{QpayModel.Category}-{QpayModel.FullName}",
                DateTime.Now.ToString("yyyyMMddhhmmssfff"),
                feeId.ToString(),
                "L", // LinePay
                "ONE",
                "",
                0,
                "M",
                1,
                "收費單",
                LineLoginContact,
                QpayModel.SelectedCreditCard
            );

            if (createdLinePayOrder?.MobileParam?.MobilePayURL != null)
            {
                UpdateFee(ref feeEntity, createdLinePayOrder.OrderNo, "C" + orderDate, "", "");
                return createdLinePayOrder.MobileParam.MobilePayURL;
            }

            UpdateFee(ref feeEntity, createdLinePayOrder.Description, "C" + orderDate, "", "");
            return $"LinePay付款失敗! {createdLinePayOrder?.Description}";
        }

        #endregion

        #region ===== ATM 轉帳 =====

        /// <summary>
        /// 處理 ATM 轉帳/匯款
        /// </summary>
        private async Task<string> ProcessAtmPayment(Entity LineLoginContact, QpayModel QpayModel, string orderDate)
        {
            var feeId = CreateFee(LineLoginContact, QpayModel, false);
            var feeEntity = ToolUtility.RetrieveEntity("new_fee", feeId);

            return await ProcessAtm(feeId, feeEntity, QpayModel, "C" + orderDate, "", LineLoginContact);
        }

        /// <summary>
        /// 處理 ATM 轉帳詳細流程
        /// </summary>
        public async Task<string> ProcessAtm(
            Guid aCreatedFeeId,
            Entity aFeeToUpdate,
            QpayModel QpayModel,
            string OrderId,
            string LineId,
            Entity LineLoginContact)
        {
            try
            {
                QpayModel.FullName = ToolUtility.GetEntityStringAttribute(ref LineLoginContact, "fullname");

                var createdAtmOrder = await CreateOrderATM(
                    QpayModel.Amount,
                    $"{QpayModel.Category}-{QpayModel.FullName}",
                    DateTime.Now.ToString("yyyyMMddhhmmssfff"),
                    aCreatedFeeId.ToString()
                );

                // 更新收費單
                UpdateFee(ref aFeeToUpdate, "", createdAtmOrder.OrderNo, OrderId, createdAtmOrder.ATMParam.AtmPayNo);

                // 建立 ATM 資訊
                var atmInfo = BuildAtmInfo(LineLoginContact, QpayModel, createdAtmOrder.ATMParam.AtmPayNo);

                // 發送 LINE 通知
                LineId = ResolveAtmNotificationLineId(LineId, LineLoginContact);
                var notificationWarning = await TrySendAtmPaymentInstructionsAsync(
                    LineId,
                    atmInfo.LineMessage,
                    LineLoginContact.Id);

                return atmInfo.HtmlMessage + notificationWarning;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"處理 ATM 轉帳失敗: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 建立 ATM 資訊訊息
        /// </summary>
        private (string LineMessage, string HtmlMessage) BuildAtmInfo(Entity contact, QpayModel model, string atmPayNo)
        {
            var fullName = ToolUtility.GetEntityStringAttribute(ref contact, "fullname");
            var expireDate = DateTime.Now.AddDays(10).ToLocalTime().ToShortDateString();

            var lineMessage =
                $"姓名 : {fullName}{Environment.NewLine}" +
                $"名稱 : {model.Category}{Environment.NewLine}" +
                $"金額 : {model.Amount}元{Environment.NewLine}" +
                $"付款到期日: {expireDate}{Environment.NewLine}" +
                $"*** 請依照訊息付款 ***{Environment.NewLine}" +
                $"銀行代碼 : 807 永豐商業銀行{Environment.NewLine}" +
                $"分行代號 : 021 台北分行{Environment.NewLine}" +
                $"帳號     : {atmPayNo}{Environment.NewLine}" +
                $"戶名     : 其他應付款-代收-網路收款";

            var htmlMessage = lineMessage.Replace(Environment.NewLine, "<br/>");

            return (lineMessage, htmlMessage);
        }

        private string ResolveAtmNotificationLineId(string lineId, Entity contact)
        {
            if (!string.IsNullOrWhiteSpace(lineId))
            {
                return lineId;
            }

            var contactLineId = ToolUtility.GetEntityStringAttribute(ref contact, "new_lineid");
            if (!string.IsNullOrWhiteSpace(contactLineId))
            {
                return contactLineId;
            }

            var backupLineId = ToolUtility.GetEntityStringAttribute(ref contact, "new_lineid_backup");
            if (!string.IsNullOrWhiteSpace(backupLineId))
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[DonationPaymentProcessor] ATM LINE notification uses new_lineid_backup. ContactId={contact.Id}");
                return backupLineId;
            }

            return string.Empty;
        }

        private async Task<string> TrySendAtmPaymentInstructionsAsync(
            string lineId,
            string lineMessage,
            Guid contactId)
        {
            if (string.IsNullOrWhiteSpace(lineId))
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[DonationPaymentProcessor] ATM LINE notification skipped because donor has no LINE id. ContactId={contactId}");
                return BuildAtmNotificationWarning("LINE 通知未送出：奉獻者尚未綁定 LINE，請保存本頁付款資訊。");
            }

            try
            {
                await SendAtmPaymentInstructionsAsync(lineId, lineMessage);
                return string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[DonationPaymentProcessor] ATM LINE notification failed. ContactId={contactId}, LineId={lineId}, Error={ex}");
                return BuildAtmNotificationWarning("LINE 通知未送出，請保存本頁付款資訊。");
            }
        }

        protected virtual async Task SendAtmPaymentInstructionsAsync(string lineId, string lineMessage)
        {
            await PushUtility.SendMessageOrThrowAsync(lineId, lineMessage);
        }

        private static string BuildAtmNotificationWarning(string message)
        {
            return $"{Environment.NewLine}<br/><br/><strong>{message}</strong>";
        }

        #endregion
    }
}

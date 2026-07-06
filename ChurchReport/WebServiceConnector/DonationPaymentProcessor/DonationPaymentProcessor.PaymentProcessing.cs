// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.PaymentProcessing.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class DonationPaymentProcessor
// 主要成員：ProcessCreditCardPayment、ProcessRecurringPayment、ProcessMobilePayment、ProcessLinePayPayment、ProcessAtmPayment、ProcessAtm、ResolveAtmNotificationLineIds、AddDistinctLineId、TrySendAtmPaymentInstructionsAsync、BuildAtmPaymentLineRetryKey
// 引用命名空間：ChurchReport.Models、Microsoft.Xrm.Sdk、System、System.Collections.Generic、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using ChurchReport.Models;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
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
        private async Task<string> ProcessCreditCardPayment(Entity LineLoginContact, DonationPaymentFormModel DonationPaymentFormModel, string orderDate)
        {
            var feeId = CreateFee(LineLoginContact, DonationPaymentFormModel, false);
            var feeEntity = ToolUtility.RetrieveEntity("new_fee", feeId);

            // 判斷信用卡類型
            var payTypeSub = DonationPaymentFormModel.PayWay == "銀聯卡" ? "CUP" : "ONE";

            var createdCardOrder = await CreOrderCard(
                DonationPaymentFormModel.Amount,
                $"{DonationPaymentFormModel.Category}-{DonationPaymentFormModel.FullName}",
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
                DonationPaymentFormModel.SelectedCreditCard
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
        private async Task<string> ProcessRecurringPayment(Entity LineLoginContact, DonationPaymentFormModel DonationPaymentFormModel, string orderDate)
        {
            // 建立認獻單
            var dedicationBookingId = CreateDedicationBooking(LineLoginContact, DonationPaymentFormModel);
            var dedicationBookingEntity = ToolUtility.RetrieveEntity("new_dedication_booking", dedicationBookingId);

            var createdCardOrder = await CreOrderCard(
                DonationPaymentFormModel.Amount,
                $"{DonationPaymentFormModel.Category}-{DonationPaymentFormModel.FullName}",
                orderDate,
                dedicationBookingId.ToString(),
                "C",
                "REGULAR", // 定期定額
                "",
                TransferToDeductTotalNum(DonationPaymentFormModel.DeductTotalNumber),
                "M",
                1,
                "認獻單",
                LineLoginContact,
                DonationPaymentFormModel.SelectedCreditCard
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
        private async Task<string> ProcessMobilePayment(Entity LineLoginContact, DonationPaymentFormModel DonationPaymentFormModel, string orderDate)
        {
            var feeId = CreateFee(LineLoginContact, DonationPaymentFormModel, false);
            var feeEntity = ToolUtility.RetrieveEntity("new_fee", feeId);

            var createdMobileOrder = await CreOrderCard(
                DonationPaymentFormModel.Amount,
                $"{DonationPaymentFormModel.Category}-{DonationPaymentFormModel.FullName}",
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
                DonationPaymentFormModel.SelectedCreditCard
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
        private async Task<string> ProcessLinePayPayment(Entity LineLoginContact, DonationPaymentFormModel DonationPaymentFormModel, string orderDate)
        {
            var feeId = CreateFee(LineLoginContact, DonationPaymentFormModel, false);
            var feeEntity = ToolUtility.RetrieveEntity("new_fee", feeId);

            var createdLinePayOrder = await CreOrderCard(
                DonationPaymentFormModel.Amount,
                $"{DonationPaymentFormModel.Category}-{DonationPaymentFormModel.FullName}",
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
                DonationPaymentFormModel.SelectedCreditCard
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
        private async Task<string> ProcessAtmPayment(Entity LineLoginContact, DonationPaymentFormModel DonationPaymentFormModel, string orderDate)
        {
            var feeId = CreateFee(LineLoginContact, DonationPaymentFormModel, false);
            var feeEntity = ToolUtility.RetrieveEntity("new_fee", feeId);

            return await ProcessAtm(feeId, feeEntity, DonationPaymentFormModel, "C" + orderDate, "", LineLoginContact);
        }

        /// <summary>
        /// 處理 ATM 轉帳詳細流程
        /// </summary>
        public async Task<string> ProcessAtm(
            Guid aCreatedFeeId,
            Entity aFeeToUpdate,
            DonationPaymentFormModel DonationPaymentFormModel,
            string OrderId,
            string LineId,
            Entity LineLoginContact)
        {
            try
            {
                DonationPaymentFormModel.FullName = ToolUtility.GetEntityStringAttribute(ref LineLoginContact, "fullname");

                var createdAtmOrder = await CreateOrderATM(
                    DonationPaymentFormModel.Amount,
                    $"{DonationPaymentFormModel.Category}-{DonationPaymentFormModel.FullName}",
                    DateTime.Now.ToString("yyyyMMddhhmmssfff"),
                    aCreatedFeeId.ToString()
                );

                if (createdAtmOrder?.ATMParam == null || string.IsNullOrWhiteSpace(createdAtmOrder.ATMParam.AtmPayNo))
                {
                    throw new InvalidOperationException("ATM order creation did not return a virtual account.");
                }
                UpdateFee(ref aFeeToUpdate, "", createdAtmOrder.OrderNo, OrderId, createdAtmOrder.ATMParam.AtmPayNo);

                // 建立 ATM 資訊
                var atmInfo = BuildAtmInfo(LineLoginContact, DonationPaymentFormModel, createdAtmOrder.ATMParam.AtmPayNo);

                // 發送 LINE 通知
                // ATM 虛擬帳號是付款必要資訊，因此不可只嘗試單一 LINE ID。
                // 若主要欄位 new_lineid 已失效，仍要改試綁定流程保留的 new_lineid_backup。
                var lineIds = ResolveAtmNotificationLineIds(LineId, LineLoginContact);
                var notificationResult = await TrySendAtmPaymentInstructionsAsync(
                    lineIds,
                    atmInfo.LineMessage,
                    BuildAtmPaymentLineRetryKey(aCreatedFeeId, createdAtmOrder.OrderNo, createdAtmOrder.ATMParam.AtmPayNo),
                    LineLoginContact.Id);

                return atmInfo.HtmlMessage + notificationResult;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"處理 ATM 轉帳失敗: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 建立 ATM 資訊訊息
        /// </summary>
        private (string LineMessage, string HtmlMessage) BuildAtmInfo(Entity contact, DonationPaymentFormModel model, string atmPayNo)
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

        private IReadOnlyList<string> ResolveAtmNotificationLineIds(string lineId, Entity contact)
        {
            var candidates = new List<string>();
            AddDistinctLineId(candidates, lineId);

            var contactLineId = ToolUtility.GetEntityStringAttribute(ref contact, "new_lineid");
            AddDistinctLineId(candidates, contactLineId);

            var backupLineId = ToolUtility.GetEntityStringAttribute(ref contact, "new_lineid_backup");
            if (!string.IsNullOrWhiteSpace(backupLineId) && !candidates.Contains(backupLineId.Trim()))
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[DonationPaymentProcessor] ATM LINE notification has backup LINE id candidate. ContactId={contact.Id}");
            }

            AddDistinctLineId(candidates, backupLineId);
            return candidates;
        }

        private static void AddDistinctLineId(List<string> candidates, string lineId)
        {
            if (string.IsNullOrWhiteSpace(lineId))
            {
                return;
            }

            var normalizedLineId = lineId.Trim();
            if (!candidates.Contains(normalizedLineId))
            {
                candidates.Add(normalizedLineId);
            }
        }

        private async Task<string> TrySendAtmPaymentInstructionsAsync(
            IReadOnlyList<string> lineIds,
            string lineMessage,
            string retryKey,
            Guid contactId)
        {
            // ATM 虛擬帳號是奉獻者完成付款的必要資訊；即使 LINE 沒有送出，
            // 頁面也必須明確告知使用者「付款資訊仍在畫面上」以及 LINE 失敗原因。
            if (lineIds == null || lineIds.Count == 0)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[DonationPaymentProcessor] ATM LINE notification skipped because donor has no LINE id. ContactId={contactId}");
                return BuildLineNotificationDisplayResult("發送失敗", "奉獻者尚未綁定 LINE，請保存本頁付款資訊。", false);
            }

            Exception lastException = null;
            for (var index = 0; index < lineIds.Count; index++)
            {
                var lineId = lineIds[index];
                if (string.IsNullOrWhiteSpace(lineId))
                {
                    continue;
                }

                try
                {
                    await SendAtmPaymentInstructionsAsync(lineId, lineMessage, retryKey);

                    if (index > 0)
                    {
                        System.Diagnostics.Trace.WriteLine(
                            $"[DonationPaymentProcessor] ATM LINE notification sent by fallback LINE id. ContactId={contactId}, AttemptIndex={index + 1}");
                    }

                    // 需求要求成功也要顯示給使用者；不可再回傳空字串，否則使用者無法判斷 LINE 是否送達。
                    return BuildLineNotificationDisplayResult("成功發送", "ATM/匯款付款資訊已成功發送 LINE。", true);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    System.Diagnostics.Trace.WriteLine(
                        $"[DonationPaymentProcessor] ATM LINE notification failed for candidate. ContactId={contactId}, AttemptIndex={index + 1}, HasMoreCandidates={index + 1 < lineIds.Count}, Error={ex}");
                }
            }

            System.Diagnostics.Trace.WriteLine(
                $"[DonationPaymentProcessor] ATM LINE notification failed for all LINE id candidates. ContactId={contactId}, CandidateCount={lineIds.Count}, LastError={lastException}");
            return BuildLineNotificationDisplayResult(
                "發送失敗",
                $"LINE 通知未送出，請保存本頁付款資訊。失敗原因：{FormatLineNotificationFailureReason(lastException)}",
                false);
        }

        private static string BuildAtmPaymentLineRetryKey(Guid feeId, string providerOrderNo, string atmPayNo)
        {
            if (feeId == Guid.Empty)
            {
                throw new ArgumentException("Fee id is required for ATM LINE retry key.", nameof(feeId));
            }

            if (string.IsNullOrWhiteSpace(atmPayNo))
            {
                throw new ArgumentException("ATM virtual account is required for ATM LINE retry key.", nameof(atmPayNo));
            }

            var normalizedProviderOrderNo = string.IsNullOrWhiteSpace(providerOrderNo)
                ? "no-provider-order"
                : providerOrderNo.Trim();

            // LINE 的 X-Line-Retry-Key 會進入 HTTP header。舊版使用
            // "churchreport:donation-atm:{feeId}:{orderNo}:{atmPayNo}" 這種產品語意字串，
            // 雖然可讀性高，但長度、冒號與 provider/order 資料都會增加被 LINE 或中介 proxy
            // 拒收的風險。這裡改成由穩定業務資料推導出的 UUID 字串：
            // - 同一筆 fee/order/ATM 虛擬帳號重送時得到同一個 retry key。
            // - header 內容固定為 UUID 格式，不含中文、冒號或個資。
            // - ChurchReport 仍保留 retry key 的業務來源；共用 LINE 模組只負責送出。
            return BuildDeterministicLineRetryKey(
                $"churchreport:donation-atm:{feeId:N}:{normalizedProviderOrderNo}:{atmPayNo.Trim()}");
        }

        protected virtual async Task SendAtmPaymentInstructionsAsync(string lineId, string lineMessage, string retryKey)
        {
            await PushUtility.SendReliableMessageAsync(lineId, lineMessage, retryKey);
        }

        private static string BuildLineNotificationDisplayResult(string status, string message, bool isSuccess)
        {
            var color = isSuccess ? "#198754" : "#dc3545";
            return $"{Environment.NewLine}<br/><br/><strong style=\"color:{color};\">LINE 發送結果：{status}</strong><br/><span>{message}</span>";
        }

        // LINE provider 或 HTTP client 的例外訊息會被串進 innerHTML 顯示；
        // 這裡統一轉成 HTML 安全文字，避免 provider 回傳內容破壞頁面或形成 XSS。
        private static string FormatLineNotificationFailureReason(Exception exception)
        {
            if (exception == null)
            {
                return "未知錯誤";
            }

            var message = exception.GetBaseException().Message;
            if (string.IsNullOrWhiteSpace(message))
            {
                return exception.GetType().Name;
            }

            return message;
        }

        #endregion
    }
}

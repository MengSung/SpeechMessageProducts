/*
 * 檔案整體說明
 *
 * 目的與定位：金流處理器 - 付款流程處理模組 【職責】 - 信用卡付款處理 - ATM 轉帳處理 - 行動支付處理 - LinePay 處理 - 定期定額處理 【設計原則】 - 策略模式：根據付款方式選擇處理策略 - 
 * 模板方法：統一付款處理流程
 *
 * 主要功能與流程：
 * 本檔宣告 DonationPaymentProcessor。
 * - 把同一次付款建立的所有收費單 Id 編成永豐 Param1（格式見 DonationFeeIdList）。 付款完成時 callback 依這份清單逐張把收費單標記已付款，實收金額＝各自的應收金額。
 * 主要操作入口包含 EncodeProviderFeeIds、ProcessCreditCardPayment、ProcessRecurringPayment、ProcessMobilePayment、
 * ProcessLinePayPayment、ProcessAtmPayment、ProcessAtm、ProcessAtmGroup；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/Contracts/TenancyCallSiteTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/DonationPaymentFormModelNamingTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorGatewayAdapterTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorKeyInNotificationTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorMoneyToChineseTests.cs 追查目前工作樹中的合作／使用位置。
 */
using ChurchReport.Models;
using ChurchReport.Services;
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
        private static readonly TimeSpan AtmLineNotificationDisplayTimeout = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// 把同一次付款建立的所有收費單 Id 編成永豐 Param1（格式見 <see cref="DonationFeeIdList"/>）。
        /// 付款完成時 callback 依這份清單逐張把收費單標記已付款，實收金額＝各自的應收金額。
        /// </summary>
        private static string EncodeProviderFeeIds(List<(Guid FeeId, Entity Fee, DonationLineItemInput Line)> fees)
        {
            var feeIds = new List<Guid>(fees.Count);
            foreach (var fee in fees)
            {
                feeIds.Add(fee.FeeId);
            }

            return DonationFeeIdList.Encode(feeIds);
        }

        #region ===== 信用卡付款 =====

        /// <summary>
        /// 處理信用卡/銀聯卡付款
        /// </summary>
        private async Task<string> ProcessCreditCardPayment(Entity LineLoginContact, DonationPaymentFormModel DonationPaymentFormModel, IReadOnlyList<DonationLineItemInput> lines, string orderDate)
        {
            var fees = CreateFeesForLines(LineLoginContact, DonationPaymentFormModel, lines);
            // 永豐 Param1 放入這次建立的所有收費單 Id，付款完成時 callback 依清單逐張入帳。
            var providerFeeIds = EncodeProviderFeeIds(fees);

            // 判斷信用卡類型
            var payTypeSub = DonationPaymentFormModel.PayWay == "銀聯卡" ? "CUP" : "ONE";

            var createdCardOrder = await CreOrderCard(
                DonationFeeGroupText.Total(lines),
                DonationFeeGroupText.ProviderProductName(lines, DonationPaymentFormModel.FullName),
                orderDate,
                providerFeeIds,
                "C", // 信用卡
                payTypeSub,
                "",
                0,
                "M",
                1,
                "收費單",
                LineLoginContact,
                DonationPaymentFormModel.SelectedCreditCard,
                DonationFeeGroupText.ProviderItems(lines)
            );

            if (createdCardOrder?.CardParam?.CardPayURL != null)
            {
                UpdateFees(fees, createdCardOrder.OrderNo, "C" + orderDate, "", "");
                return createdCardOrder.CardParam.CardPayURL;
            }

            return $"信用卡繳費失敗! {createdCardOrder?.Description}";
        }

        #endregion

        #region ===== 定期定額付款 =====

        /// <summary>
        /// 處理信用卡定期定額扣款
        /// </summary>
        private async Task<string> ProcessRecurringPayment(Entity LineLoginContact, DonationPaymentFormModel DonationPaymentFormModel, IReadOnlyList<DonationLineItemInput> lines, string orderDate)
        {
            if (lines.Count != 1) return "信用卡定期定額建立失敗! 定期定額一次只能設定一個類別";
            DonationPaymentFormModel = DonationPaymentFormModel.CloneForLine(lines[0]);
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
        private async Task<string> ProcessMobilePayment(Entity LineLoginContact, DonationPaymentFormModel DonationPaymentFormModel, IReadOnlyList<DonationLineItemInput> lines, string orderDate)
        {
            var fees = CreateFeesForLines(LineLoginContact, DonationPaymentFormModel, lines); var providerFeeIds = EncodeProviderFeeIds(fees);

            var createdMobileOrder = await CreOrderCard(
                DonationFeeGroupText.Total(lines), DonationFeeGroupText.ProviderProductName(lines, DonationPaymentFormModel.FullName),
                orderDate,
                providerFeeIds,
                "M", // 行動支付
                "ONE",
                "",
                0,
                "M",
                1,
                "收費單",
                LineLoginContact,
                DonationPaymentFormModel.SelectedCreditCard, DonationFeeGroupText.ProviderItems(lines)
            );

            if (createdMobileOrder?.MobileParam?.MobilePayURL != null)
            {
                UpdateFees(fees, createdMobileOrder.OrderNo, "C" + orderDate, "", "");
                return createdMobileOrder.MobileParam.MobilePayURL;
            }

            UpdateFees(fees, createdMobileOrder.Description, "C" + orderDate, "", "");
            return $"行動支付付款失敗! {createdMobileOrder?.Description}";
        }

        #endregion

        #region ===== LinePay =====

        /// <summary>
        /// 處理 LinePay 付款
        /// </summary>
        private async Task<string> ProcessLinePayPayment(Entity LineLoginContact, DonationPaymentFormModel DonationPaymentFormModel, IReadOnlyList<DonationLineItemInput> lines, string orderDate)
        {
            var fees = CreateFeesForLines(LineLoginContact, DonationPaymentFormModel, lines); var providerFeeIds = EncodeProviderFeeIds(fees);

            var createdLinePayOrder = await CreOrderCard(
                DonationFeeGroupText.Total(lines), DonationFeeGroupText.ProviderProductName(lines, DonationPaymentFormModel.FullName),
                DateTime.Now.ToString("yyyyMMddhhmmssfff"),
                providerFeeIds,
                "L", // LinePay
                "ONE",
                "",
                0,
                "M",
                1,
                "收費單",
                LineLoginContact,
                DonationPaymentFormModel.SelectedCreditCard, DonationFeeGroupText.ProviderItems(lines)
            );

            if (createdLinePayOrder?.MobileParam?.MobilePayURL != null)
            {
                UpdateFees(fees, createdLinePayOrder.OrderNo, "C" + orderDate, "", "");
                return createdLinePayOrder.MobileParam.MobilePayURL;
            }

            UpdateFees(fees, createdLinePayOrder.Description, "C" + orderDate, "", "");
            return $"LinePay付款失敗! {createdLinePayOrder?.Description}";
        }

        #endregion

        #region ===== ATM 轉帳 =====

        /// <summary>
        /// 處理 ATM 轉帳/匯款
        /// </summary>
        private async Task<string> ProcessAtmPayment(Entity LineLoginContact, DonationPaymentFormModel DonationPaymentFormModel, IReadOnlyList<DonationLineItemInput> lines, string orderDate)
        {
            var fees = CreateFeesForLines(LineLoginContact, DonationPaymentFormModel, lines);
            return await ProcessAtmGroup(fees, lines, DonationPaymentFormModel, "C" + orderDate, "", LineLoginContact);
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
            var line = new DonationLineItemInput { Category = DonationPaymentFormModel.Category, Amount = DonationPaymentFormModel.Amount, Others = DonationPaymentFormModel.Others };
            return await ProcessAtmGroup(new List<(Guid, Entity, DonationLineItemInput)> { (aCreatedFeeId, aFeeToUpdate, line) }, new[] { line }, DonationPaymentFormModel, OrderId, LineId, LineLoginContact);
        }

        private async Task<string> ProcessAtmGroup(List<(Guid FeeId, Entity Fee, DonationLineItemInput Line)> fees, IReadOnlyList<DonationLineItemInput> lines, DonationPaymentFormModel model, string orderId, string lineId, Entity contact)
        {
            try
            {
                model.FullName = ToolUtility.GetEntityStringAttribute(ref contact, "fullname");
                var createdAtmOrder = await CreateOrderATM(DonationFeeGroupText.Total(lines), DonationFeeGroupText.ProviderProductName(lines, model.FullName), DateTime.Now.ToString("yyyyMMddhhmmssfff"), EncodeProviderFeeIds(fees), DonationFeeGroupText.ProviderItems(lines));
                if (createdAtmOrder?.ATMParam == null || string.IsNullOrWhiteSpace(createdAtmOrder.ATMParam.AtmPayNo)) throw new InvalidOperationException("ATM order creation did not return a virtual account.");
                UpdateFees(fees, "", createdAtmOrder.OrderNo, orderId, createdAtmOrder.ATMParam.AtmPayNo);
                var atmInfo = DonationFeeGroupText.AtmInfo(model.FullName, lines, createdAtmOrder.ATMParam.AtmPayNo, DateTime.Now.AddDays(10).ToLocalTime().ToShortDateString());
                var lineIds = ResolveAtmNotificationLineIds(lineId, contact);
                var notificationResult = await TrySendAtmPaymentInstructionsAsync(lineIds, atmInfo.LineMessage, BuildAtmPaymentLineRetryKey(fees[0].FeeId, createdAtmOrder.OrderNo, createdAtmOrder.ATMParam.AtmPayNo), contact.Id);
                return atmInfo.HtmlMessage + notificationResult;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"處理 ATM 轉帳失敗: {ex.Message}", ex);
            }
            /*
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
            */
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
                    var sendTask = SendAtmPaymentInstructionsAsync(lineId, lineMessage, retryKey);
                    var timeoutTask = Task.Delay(AtmLineNotificationDisplayTimeout);
                    var completedTask = await Task.WhenAny(sendTask, timeoutTask);
                    if (completedTask == timeoutTask)
                    {
                        System.Diagnostics.Trace.WriteLine(
                            $"[DonationPaymentProcessor] ATM LINE notification timed out before display response. ContactId={contactId}, AttemptIndex={index + 1}, TimeoutMs={AtmLineNotificationDisplayTimeout.TotalMilliseconds}");
                        _ = sendTask.ContinueWith(
                            task =>
                            {
                                if (task.IsFaulted)
                                {
                                    System.Diagnostics.Trace.WriteLine(
                                        $"[DonationPaymentProcessor] ATM LINE notification background completion failed. ContactId={contactId}, AttemptIndex={index + 1}, Error={task.Exception}");
                                }
                            },
                            TaskContinuationOptions.ExecuteSynchronously);

                        return BuildLineNotificationDisplayResult(
                            "發送失敗",
                            "LINE API 逾時未回應，請保存本頁付款資訊。",
                            false);
                    }

                    await sendTask;

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

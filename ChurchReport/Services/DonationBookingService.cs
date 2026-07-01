using System;
using System.Threading.Tasks;
using ChurchReport.Models;
using ChurchReport.Payments;
using ChurchReport.Tools;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace;

namespace ChurchReport.Services
{
    /// <summary>
    /// ChurchReport 認獻單清單、狀態與取消流程服務。
    ///
    /// 認獻單是 ChurchReport 奉獻領域模型，並非通用金流核心概念。
    /// 因此這個服務留在 ChurchReport，集中管理 CRM new_dedication_booking 映射、
    /// 認獻單狀態文字、定期定額中止後的 CRM 備註更新，以及通知奉獻者的 LINE 訊息。
    ///
    /// 注意：這裡雖然會呼叫舊相容的 OrderMaintain，但它仍然是 ChurchReport 既有產品流程。
    /// 通用金流核心不應知道「認獻單」、「奉獻者 fullname」或 ChurchReport 的 LINE 通知格式。
    /// </summary>
    public sealed class DonationBookingService
    {
        private readonly ToolUtilityClass _utility;

        public DonationBookingService(ToolUtilityClass utility)
        {
            _utility = utility ?? throw new ArgumentNullException(nameof(utility));
        }

        public static string ConvertStatus(int optionSetValue)
        {
            return optionSetValue switch
            {
                100000000 => "尚未啟動",
                100000001 => "進行中",
                100000002 => "已結案",
                100000003 => "啟動失敗",
                100000004 => "已取消",
                _ => "尚未啟動"
            };
        }

        /// <summary>
        /// 從 CRM 查出指定奉獻者的認獻單，並填入奉獻付款表單模型。
        ///
        /// DonationPaymentManager 只需要知道「要載入認獻清單」；new_dedication_booking 的欄位名稱、
        /// 金額轉字串、日期轉本地時間、OptionSet 轉顯示文字等細節都集中在這裡。
        /// </summary>
        public void FillBookingList(DonationPaymentFormModel model, Entity contact)
        {
            ArgumentNullException.ThrowIfNull(model);
            ArgumentNullException.ThrowIfNull(contact);

            EntityCollection bookingEntities = _utility.RetrieveDedicationBookingByFetchXml(
                _utility.GetEntityStringAttribute(ref contact, "fullname"),
                contact.Id.ToString());

            model.DedicationBookingList.Clear();

            foreach (Entity bookingEntity in bookingEntities.Entities)
            {
                Entity retrievedBooking = _utility.RetrieveEntity("new_dedication_booking", bookingEntity.Id);
                model.DedicationBookingList.Add(MapBooking(retrievedBooking));
            }
        }

        /// <summary>
        /// 維持既有 UI 行為：若有認獻單，畫面預設選第一筆。
        /// </summary>
        public void SelectDefaultBooking(DonationPaymentFormModel model)
        {
            ArgumentNullException.ThrowIfNull(model);

            if (model.DedicationBookingList.Count > 0)
            {
                model.SelectedDedicationBooking = model.DedicationBookingList[0].EntityId;
            }
        }

        /// <summary>
        /// 中止一筆認獻單對應的定期定額授權，並把結果寫回 CRM 與 LINE。
        ///
        /// <paramref name="orderMaintainer"/> 由 manager 傳入，是為了把舊金流維護入口留在既有邊界；
        /// 本服務只負責 ChurchReport 產品流程，不直接依賴特定 provider toolkit。
        /// </summary>
        public async Task<string> DeleteBookingAsync(
            DonationPaymentFormModel model,
            Entity contact,
            DedicationBooking booking,
            Func<string, string, Task<OrderMaintain>> orderMaintainer,
            PushUtility pushUtility)
        {
            ArgumentNullException.ThrowIfNull(model);
            ArgumentNullException.ThrowIfNull(contact);
            ArgumentNullException.ThrowIfNull(booking);
            ArgumentNullException.ThrowIfNull(orderMaintainer);

            Entity bookingToDelete = _utility.RetrieveEntity("new_dedication_booking", new Guid(booking.EntityId));
            if (bookingToDelete == null)
            {
                return string.Empty;
            }

            string orderNo = _utility.GetEntityStringAttribute(bookingToDelete, "new_q_pay_card_order_no");
            OrderMaintain orderMaintain = await orderMaintainer(orderNo, "E");
            bool maintainSucceeded = orderMaintain != null && orderMaintain.Status == "S";
            string maintainDescription = orderMaintain?.Description ?? string.Empty;

            if (maintainSucceeded)
            {
                model.DedicationBookingList.Remove(booking);
                _utility.SetOptionSetAttribute(ref bookingToDelete, "new_dedication_booking_status", 100000004);
            }

            string resultMessage = BuildDeleteResultMessage(contact, booking, maintainSucceeded, maintainDescription);

            _utility.SetEntityStringAttribute(
                ref bookingToDelete,
                "new_explain",
                _utility.GetEntityStringAttribute(ref bookingToDelete, "new_explain") + Environment.NewLine + resultMessage);
            _utility.UpdateEntity(ref bookingToDelete);

            SendBookingResultMessage(contact, resultMessage, pushUtility);

            return maintainDescription;
        }

        private DedicationBooking MapBooking(Entity bookingEntity)
        {
            return new DedicationBooking
            {
                EntityId = bookingEntity.Id.ToString(),
                DedicationCategory = DonationFeeQueryService.ConvertCategory(bookingEntity),
                DedicationBookingStatus = ConvertStatus(_utility.GetOptionSetAttribute(bookingEntity, "new_dedication_booking_status")),
                AmountPerStage = Decimal.Truncate(_utility.GetEntityMoneyAttribute(ref bookingEntity, "new_amount_per_stage").Value).ToString(),
                TotalStages = _utility.GetEntityStringAttribute(ref bookingEntity, "new_total_stages"),
                DedicationAmount = Decimal.Truncate(_utility.GetEntityMoneyAttribute(ref bookingEntity, "new_dedication_amount").Value).ToString(),
                PaidPeriod = _utility.GetEntityStringAttribute(ref bookingEntity, "new_paid_period"),
                RollupPaidFee = Decimal.Truncate(_utility.GetEntityMoneyAttribute(ref bookingEntity, "new_rollup_paid_fee").Value).ToString(),
                StartDate = _utility.GetEntityDateTimeAttribute(ref bookingEntity, "new_dedication_start_date").ToLocalTime().ToShortDateString(),
                EndDate = _utility.GetEntityDateTimeAttribute(ref bookingEntity, "new_dedication_end_date").ToLocalTime().ToShortDateString()
            };
        }

        private string BuildDeleteResultMessage(
            Entity contact,
            DedicationBooking booking,
            bool maintainSucceeded,
            string maintainDescription)
        {
            string statusText = maintainSucceeded ? "中止定期定額成功!" : "中止定期定額失敗!";

            return _utility.GetEntityStringAttribute(contact, "fullname") + ":" + statusText + Environment.NewLine +
                "類別:" + booking.DedicationCategory + Environment.NewLine +
                "每期金額:" + booking.AmountPerStage + Environment.NewLine +
                "總期數:" + booking.TotalStages + Environment.NewLine +
                "應付總金額:" + booking.DedicationAmount + Environment.NewLine +
                "目前期數:" + booking.PaidPeriod + Environment.NewLine +
                "已付金額:" + booking.RollupPaidFee + Environment.NewLine +
                "開始日期:" + booking.StartDate + Environment.NewLine +
                "結束日期:" + booking.EndDate + Environment.NewLine +
                maintainDescription + Environment.NewLine +
                "--------------------------------------" + Environment.NewLine;
        }

        private void SendBookingResultMessage(Entity contact, string resultMessage, PushUtility pushUtility)
        {
            string contactLineId = _utility.GetEntityStringAttribute(contact, "new_lineid");
            if (contactLineId != string.Empty)
            {
                pushUtility.SendMessage(contactLineId, resultMessage);
            }
        }
    }
}

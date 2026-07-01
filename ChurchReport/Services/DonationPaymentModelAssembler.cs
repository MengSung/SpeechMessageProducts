using System;
using System.Collections.Generic;
using System.Linq;
using ChurchReport.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace;

namespace ChurchReport.Services
{
    /// <summary>
    /// ChurchReport 奉獻付款頁面模型組裝器。
    ///
    /// 這個服務負責把 CRM contact、CRM task 設定、OptionSet、信用卡清單與認獻清單組成
    /// DonationPaymentFormModel。它是「ChurchReport 畫面模型組裝」而不是「金流核心」：
    /// - 未來建設公司維修系統不會有奉獻類別、宣道支持奉獻、認獻單。
    /// - 協會會員系統或發票收款系統可能會有自己的付款表單，但不該繼承 ChurchReport 的 CRM task 名稱。
    ///
    /// 因此 DonationPaymentManager 只保留公開方法與目前 contact 狀態；詳細表單欄位填值集中在此處。
    /// </summary>
    public sealed class DonationPaymentModelAssembler
    {
        private readonly ToolUtilityClass _utility;
        private readonly DonationBookingService _bookingService;
        private readonly Action _processCreditCards;

        public DonationPaymentModelAssembler(
            ToolUtilityClass utility,
            DonationBookingService bookingService,
            Action processCreditCards)
        {
            _utility = utility ?? throw new ArgumentNullException(nameof(utility));
            _bookingService = bookingService ?? throw new ArgumentNullException(nameof(bookingService));
            _processCreditCards = processCreditCards ?? throw new ArgumentNullException(nameof(processCreditCards));
        }

        /// <summary>
        /// 建立奉獻付款頁面所需的完整表單模型。
        ///
        /// 這裡沿用原本 DonationPaymentManager.SetDonationPaymentModel 的填值順序，避免頁面初始狀態改變。
        /// 最後一定呼叫 EnsureFormDefaults，保護 CRM OptionSet 或 task 設定查詢失敗時的畫面基本可用性。
        /// </summary>
        public DonationPaymentFormModel Build(Entity contact, DonationPaymentFormModel model)
        {
            ArgumentNullException.ThrowIfNull(contact);
            ArgumentNullException.ThrowIfNull(model);

            FillDonorIdentity(contact, model);
            FillOtherCategories(model);
            FillSpecialCategories(model);
            FillCreditCards(model);
            FillDedicationBookings(contact, model);

            model.QueryStartDate = new DateTime(DateTime.Now.Year, 1, 1);
            model.QueryEndDate = DateTime.Now;
            model.IsAOfficeWorker = IsAccountingWorker(contact);
            model.DedicationCategoryList = LoadDedicationCategoryList();
            model.EnsureFormDefaults();

            return model;
        }

        private void FillDonorIdentity(Entity contact, DonationPaymentFormModel model)
        {
            model.FullName = _utility.GetEntityStringAttribute(ref contact, "fullname");
            model.DedicationNumber = _utility.GetEntityStringAttribute(ref contact, "pager");
            model.NationId = _utility.GetEntityStringAttribute(ref contact, "new_personal_id");
            model.Ntbt = _utility.GetEntityBoolAttribute(ref contact, "new_ntbt_ornot")
                ? "願意上傳國稅局"
                : "不願意上傳國稅局";

            model.Category = "十一奉獻";
            model.PayWay = "信用卡";
            model.DedicateLocation = "好牧人";
        }

        private void FillOtherCategories(DonationPaymentFormModel model)
        {
            model.OtherCategoryArray = ReadTaskDescriptionLines("宣道支持奉獻(請勿刪除)");
        }

        private void FillSpecialCategories(DonationPaymentFormModel model)
        {
            model.SpecialCategoryArray = new List<string>();

            foreach (string specialCategory in ReadTaskDescriptionLines("特別奉獻清單(不可刪除)"))
            {
                string specialCategoryText = DonationPaymentFormBuilder.ResolveSpecialCategory(specialCategory, DateTime.Now);
                if (specialCategoryText != string.Empty)
                {
                    model.SpecialCategoryArray.Add(specialCategoryText);
                }
            }
        }

        private List<string> ReadTaskDescriptionLines(string taskSubject)
        {
            EntityCollection taskCollection = _utility.RetrieveTaskByFetchXml(taskSubject);
            string description = string.Empty;

            if (taskCollection.Entities.Count > 0)
            {
                description = _utility.GetEntityStringAttribute(taskCollection.Entities[0], "description");
            }

            return description
                .Split(Environment.NewLine.ToCharArray())
                .ToList();
        }

        private void FillCreditCards(DonationPaymentFormModel model)
        {
            if (model.CreditCardList == null)
            {
                model.CreditCardList = new List<CreditCard>();
            }
            else
            {
                model.CreditCardList.Clear();
            }

            _processCreditCards();
        }

        private void FillDedicationBookings(Entity contact, DonationPaymentFormModel model)
        {
            if (model.DedicationBookingList == null)
            {
                model.DedicationBookingList = new List<DedicationBooking>();
            }
            else
            {
                model.DedicationBookingList.Clear();
            }

            // 認獻單查詢與 new_dedication_booking 映射由 DonationBookingService 擁有；
            // assembler 只負責把結果併入畫面模型。
            _bookingService.FillBookingList(model, contact);
            _bookingService.SelectDefaultBooking(model);
        }

        private bool IsAccountingWorker(Entity contact)
        {
            string jobTitle = _utility.GetEntityStringAttribute(ref contact, "new_church_jobtitle");
            return !string.IsNullOrEmpty(jobTitle) && jobTitle.Contains("會計");
        }

        private List<string> LoadDedicationCategoryList()
        {
            try
            {
                var optionSetService = new OptionSetMetadataService(
                    _utility.m_Crm2011OrganizationService,
                    null,
                    new MemoryCache(new MemoryCacheOptions()));

                var categoryMapping = optionSetService.GetOptionSetMapping("new_fee", "new_category");
                var categoryList = categoryMapping.Keys.ToList();

                System.Diagnostics.Debug.WriteLine(
                    $"[DonationPaymentModelAssembler] 成功取得 {categoryList.Count} 個奉獻類別");

                return categoryList;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[DonationPaymentModelAssembler] 動態取得奉獻類別失敗，使用備用清單: {ex.Message}");

                return new List<string>
                {
                    "主日奉獻",
                    "十一奉獻",
                    "感恩奉獻",
                    "建堂奉獻",
                    "宣教奉獻",
                    "愛心奉獻",
                    "特別奉獻"
                };
            }
        }
    }
}

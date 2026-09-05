// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/DonationPaymentModelAssembler.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class DonationPaymentModelAssembler
// 主要成員：Build、FillDonorIdentity、FillOtherCategories、FillSpecialCategories、ReadTaskDescriptionLines、FillCreditCards、FillDedicationBookings、IsAccountingWorker、LoadDedicationCategoryList
// 引用命名空間：System、System.Collections.Generic、System.Linq、ChurchReport.Models、Microsoft.Extensions.Caching.Memory、Microsoft.Xrm.Sdk、ToolUtilityNameSpace
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
                // 這個 metadata cache 只服務本次模型組裝，不能讓每次 LINE 登入都遺留一個
                // 永遠可達的 MemoryCache。using 讓 cache 及其 eviction 資源在查詢完成後立即釋放；
                // 不把任何 contact、token 或 scoped service 放進程序級快取。
                using var metadataCache = new MemoryCache(new MemoryCacheOptions());
                var optionSetService = new OptionSetMetadataService(
                    _utility.m_Crm2011OrganizationService,
                    null,
                    metadataCache);

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

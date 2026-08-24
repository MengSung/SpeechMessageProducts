// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Models/DonationPaymentFormModel.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：class DonationPaymentFormModel
// 主要成員：EnsureFormDefaults、NeedsDonorIdentityRestore、FullName、Mobile、DedicationNumber、NationId、DedicationDate、BankName、SerialNumber、LastSixDigit
// 引用命名空間：System、System.Collections.Generic、System.Linq、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    /// <summary>
    /// ChurchReport 奉獻付款頁面使用的「產品層表單狀態」。
    ///
    /// 這個類別只描述 ChurchReport 畫面與奉獻流程需要暫存的資料，例如奉獻者姓名、奉獻編號、
    /// 奉獻類別、付款方式、信用卡清單、定期定額清單、查詢日期等。它不是永豐 QPay 的協定模型，
    /// 也不是高鉅、台新或 LINE Pay 的 provider DTO。
    ///
    /// 重構前這個類別叫做 QpayModel，但那個名稱會讓人誤會整個奉獻付款流程都屬於永豐。
    /// 新名稱 DonationPaymentFormModel 明確表示：這是 ChurchReport 奉獻付款表單資料，
    /// 真正的銀行協定資料應該留在 SpeechMessage.Payments 的 provider 實作內。
    /// </summary>
    public class DonationPaymentFormModel
    {
        private const String DefaultCategory = "十一奉獻";
        private const String DefaultPayWay = "信用卡";

        private static readonly string[] s_defaultDedicationCategories =
        {
            "主日奉獻",
            "十一奉獻",
            "感恩奉獻",
            "建堂奉獻",
            "宣教奉獻",
            "愛心奉獻",
            "特別奉獻"
        };

        public DonationPaymentFormModel()
        {
            EnsureFormDefaults();
        }

        /// <summary>
        /// 確保奉獻頁面 render 時一定有可用的基本表單資料。
        /// DonationPaymentFormModel 目前會被 DonationPaymentManager 當作長生命週期狀態重複使用，
        /// 舊流程或 CRM 查詢失敗時可能把下拉選單清成 null 或空集合。
        /// Controller 在回傳 View 前呼叫此方法，可以讓畫面至少保有可操作的奉獻類別與付款方式，
        /// 再由 CRM/LINE 初始化流程補上更完整的個人資料與選項。
        /// </summary>
        public void EnsureFormDefaults()
        {
            if (DedicationCategoryList == null || DedicationCategoryList.Count == 0)
            {
                DedicationCategoryList = new List<String>(s_defaultDedicationCategories);
            }

            // 奉獻類別必須先跟實際可選清單對帳再寫回，順序不可調到清單初始化之前。
            Category = ResolveCategoryAgainstList(Category, DedicationCategoryList);

            if (String.IsNullOrWhiteSpace(PayWay))
            {
                PayWay = DefaultPayWay;
            }

            OtherCategoryArray ??= new List<String>();
            SpecialCategoryArray ??= new List<String>();
            CreditCardList ??= new List<CreditCard>();
            DedicationFeeList ??= new List<DedicationFee>();
            DedicationBookingList ??= new List<DedicationBooking>();
        }

        /// <summary>
        /// 把奉獻類別對帳回「畫面上真的選得到的清單」。
        ///
        /// 奉獻頁的 SelectBox DataSource 是 <see cref="DedicationCategoryList"/>，
        /// 而它來自各教會自己的 CRM new_fee.new_category OptionSet，用字不一定是「十一奉獻」
        /// （例如好牧人用「月定獻金」「禮拜獻金」）。若 Category 停在清單裡沒有的值，
        /// DevExtreme SelectBox 找不到對應項就會退回顯示 placeholder「選擇...」，
        /// 使用者必須自己下拉一次才能送出，這正是要防止的狀況。
        ///
        /// 對帳規則刻意分三層，避免改變既有教會的預設值：
        /// 1. 現有值若存在於清單中就原樣保留（含使用者已選好的類別）。
        /// 2. 否則優先採用 <see cref="DefaultCategory"/>，讓 OptionSet 有「十一奉獻」的教會行為不變。
        /// 3. 都對不上才退回清單第一個有效項目。
        ///
        /// 比對時忽略前後空白與大小寫，但回傳清單裡的「原始字串」，
        /// 因為 SelectBox 是用字串相等去比對 DataSource 項目的。
        /// </summary>
        private static String ResolveCategoryAgainstList(String category, List<String> categoryList)
        {
            String selected = FindCategoryInList(categoryList, category);
            if (selected != null)
            {
                return selected;
            }

            return FindCategoryInList(categoryList, DefaultCategory)
                ?? categoryList.FirstOrDefault(item => !String.IsNullOrWhiteSpace(item))
                ?? DefaultCategory;
        }

        /// <summary>
        /// 在奉獻類別清單中找出與指定文字相同的項目，找不到時回傳 null。
        /// </summary>
        private static String FindCategoryInList(List<String> categoryList, String category)
        {
            if (String.IsNullOrWhiteSpace(category))
            {
                return null;
            }

            return categoryList.FirstOrDefault(item =>
                !String.IsNullOrWhiteSpace(item)
                && String.Equals(item.Trim(), category.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 判斷奉獻頁是否缺少從 CRM contact 帶出的奉獻者識別資料。
        ///
        /// 這個方法刻意只檢查「姓名」與「奉獻編號」兩個畫面上必須顯示的欄位：
        /// - 下拉選單空白可以由 <see cref="EnsureFormDefaults"/> 補安全預設值。
        /// - 姓名、奉獻編號、信用卡清單則必須回到 CRM contact 重新初始化，不能用假資料補。
        ///
        /// Controller 透過這個判斷決定是否要用 Session 裡保存的 contact id
        /// 重新呼叫 DonationPaymentManager.SetDonationPaymentModel(...)。
        /// </summary>
        public bool NeedsDonorIdentityRestore()
        {
            return String.IsNullOrWhiteSpace(FullName)
                || String.IsNullOrWhiteSpace(DedicationNumber);
        }

        //public String LoginFullName { get; set; }                       //輸入人員姓名
        public String FullName { get; set; }                            //姓名
        public String Mobile { get; set; }                              //手機
        public String DedicationNumber { get; set; }                    //奉獻編號
        public String NationId { get; set; }                            //身分證字號
        public DateTime DedicationDate { get; set; } = DateTime.Now;    //奉獻日期
        public String BankName { get; set; }                            //銀行名稱
        public String SerialNumber { get; set; }                        //奉獻號碼
        public String LastSixDigit { get; set; }                        //帳戶後六碼
        public String Ntbt { get; set; }                                //是否上傳國稅局
        public String ChurchName { get; set; }                          //所屬教會

        /// <summary>
        /// ///////////////////////////////////////////////////////////////////////////
        /// </summary>
        public int Amount { get; set; }                                 //奉獻金額
        public String Category { get; set; }                            //奉獻類別
        public String PayWay { get; set; }                              //付款方式
        public String DeductTotalNumber { get; set; }                   //定期定額總期數
        public String Others { get; set; }                              //其他奉獻
        public String DedicateLocation { get; set; }                    //奉獻分堂
        public String Explain { get; set; }                             //備註
        public String WeeklyNote { get; set; }                          //週報專用備註

        public DateTime RecieptDate { get; set; } = DateTime.Now;       //收據日期
        public String ClickType { get; set; }                           //查詢或是上傳
        public String SelectedCreditCard { get; set; }                  //選取的信用卡
        public String SelectedContactId { get; set; }                   //從同名清單選取的 CRM Contact GUID，用於上傳時直接定位避免模糊查詢卡住

        // ✅ 新增：動態奉獻類別清單（從 Dynamics 365 OptionSet 取得）
        public List<String> DedicationCategoryList { get; set; } = new List<String>();

        public List<String> OtherCategoryArray { get; set; }
        public List<String> SpecialCategoryArray { get; set; }
        public List<CreditCard> CreditCardList { get; set; }
        public List<DedicationFee> DedicationFeeList { get; set; }
        public int TotalAmount { get; set; }                            //奉獻總金額

        public List<DedicationBooking> DedicationBookingList { get; set; }//認獻清單
        public String SelectedDedicationBooking { get; set; }             //選取的認獻

        public DateTime QueryStartDate { get; set; } = DateTime.Now;    //奉獻查詢開始日期
        public DateTime QueryEndDate { get; set; } = DateTime.Now;      //奉獻查詢結束日期

        public bool IsAOfficeWorker { get; set; } = false;              //是否符合輸入奉獻的行政人員

        public List<SameNameElement> SameNameList { get; set; } = new List<SameNameElement>();

    }
}

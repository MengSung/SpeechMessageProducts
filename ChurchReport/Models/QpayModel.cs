using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChurchReport.Models
{
    public class QpayModel
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

        public QpayModel()
        {
            EnsureFormDefaults();
        }

        /// <summary>
        /// 確保奉獻頁面 render 時一定有可用的基本表單資料。
        /// QpayModel 目前會被 DonationPaymentManager 當作長生命週期狀態重複使用，
        /// 舊流程或 CRM 查詢失敗時可能把下拉選單清成 null 或空集合。
        /// Controller 在回傳 View 前呼叫此方法，可以讓畫面至少保有可操作的奉獻類別與付款方式，
        /// 再由 CRM/LINE 初始化流程補上更完整的個人資料與選項。
        /// </summary>
        public void EnsureFormDefaults()
        {
            if (String.IsNullOrWhiteSpace(Category))
            {
                Category = DefaultCategory;
            }

            if (String.IsNullOrWhiteSpace(PayWay))
            {
                PayWay = DefaultPayWay;
            }

            if (DedicationCategoryList == null || DedicationCategoryList.Count == 0)
            {
                DedicationCategoryList = new List<String>(s_defaultDedicationCategories);
            }

            OtherCategoryArray ??= new List<String>();
            SpecialCategoryArray ??= new List<String>();
            CreditCardList ??= new List<CreditCard>();
            DedicationFeeList ??= new List<DedicationFee>();
            DedicationBookingList ??= new List<DedicationBooking>();
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

/*
 * 檔案整體說明
 *
 * 目的與定位：ChurchReport 奉獻付款頁面使用的「產品層表單狀態」。 這個類別只描述 ChurchReport 畫面與奉獻流程需要暫存的資料，例如奉獻者姓名、奉獻編號、 奉獻類別、付款方式、信用卡清單、定期定額清單、
 * 查詢日期等。它不是永豐 QPay 的協定模型， 也不是高鉅、台新或 LINE Pay 的 provider DTO。 重構前這個類別叫做 QpayModel，但那個名稱會讓人誤會整個奉獻付款流程都屬於永豐。 新名稱
 * DonationPaymentFormModel 明確表示：這是 ChurchReport 奉獻付款表單資料， 真正的銀行協定資料應該留在 SpeechMessage.Payments 的 provider 實作內。
 *
 * 主要功能與流程：
 * 本檔宣告 DonationPaymentFormModel 與 DonationOfferingCategoryOption。
 * 表單類別值為正式資料庫 Guid，名稱只供呈現；metadata 相容項目明確標為不可提交。
 * EnsureFormDefaults 只選擇當次教會清單中排序第一個可提交類別，不以硬編碼名稱回填；CloneForLine
 * 只把已驗證類別帶入該明細副本，原始表單與其他 request 不共享解析狀態。
 * - 判斷奉獻頁是否缺少從 CRM contact 帶出的奉獻者識別資料。 這個方法刻意只檢查「姓名」與「奉獻編號」兩個畫面上必須顯示的欄位： - 下拉選單空白可以由 EnsureFormDefaults 補安全預設值。 -
 * 姓名、奉獻編號、信用卡清單則必須回到 CRM contact 重新初始化，不能用假資料補。 Controller 透過這個判斷決定是否要用 Session 裡保存的 contact id 重新呼叫
 * DonationPaymentManager.SetDonationPaymentModel(...)。
 * - 多類別奉獻明細。空集合表示呼叫端仍使用相容的單一 Category／Amount／Others 欄位。 這個集合只屬於目前表單物件，禁止放入 static 或跨請求快取，以免會友資料互相洩漏。
 * 主要操作入口包含 EnsureFormDefaults、ResolveCategoryAgainstList、FindCategoryInList、NeedsDonorIdentityRestore、
 * CloneForLine。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 類別清單必須由伺服器登入 contact 的 account 範圍於每次組裝時重新投影，不能沿用其他使用者的清單。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/DedicationController.cs、
 * SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs、
 * SpeechMessageProducts.ChurchReport/Payments/DonationPaymentFormModelMapper.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SpeechMessageProducts.GoodShepherd.Core.Offerings;

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
        private const String DefaultPayWay = "信用卡";

        public DonationPaymentFormModel()
        {
            EnsureFormDefaults();
        }

        /// <summary>
        /// 確保表單集合有效，並把選取類別限制在本次伺服器投影的正式資料庫 ID。
        /// 類別清單可能來自本次已驗證 contact 的教會，但表單模型本身不是授權證明；
        /// Create 前仍須由服務端重新讀取教會清單並驗證 ID。空清單時保留空類別，不偽造預設項。
        /// </summary>
        public void EnsureFormDefaults()
        {
            DedicationCategoryList ??= new List<DonationOfferingCategoryOption>();

            Category = ResolveCategoryAgainstList(Category, DedicationCategoryList);

            if (String.IsNullOrWhiteSpace(PayWay))
            {
                PayWay = DefaultPayWay;
            }

            OtherCategoryArray ??= new List<String>();
            // Lines 是請求所屬的可變集合；若舊流程或 CRM 初始化曾將它設為 null，
            // 只補回新的空集合，絕不重用其他會友或其他請求的集合實例。
            Lines ??= new List<DonationLineItemInput>();
            SpecialCategoryArray ??= new List<String>();
            CreditCardList ??= new List<CreditCard>();
            DedicationFeeList ??= new List<DedicationFee>();
            DedicationBookingList ??= new List<DedicationBooking>();
        }

        /// <summary>
        /// 將目前類別對帳回本次伺服器提供的選項清單，只以 Guid 比對正式資料庫 ID。
        /// 不以顯示名稱、舊 OptionSet 標籤或呼叫端輸入推導類別身分。
        /// </summary>
        private static String ResolveCategoryAgainstList(
            String category,
            List<DonationOfferingCategoryOption> categoryList)
        {
            var selected = FindCategoryInList(categoryList, category);
            if (selected != null) return selected;
            return categoryList.FirstOrDefault(item => item.IsSelectable)?.Id ?? String.Empty;
        }

        /// <summary>在類別清單中依標準 D 格式 Guid 尋找正式且可提交的選項。</summary>
        private static String FindCategoryInList(
            List<DonationOfferingCategoryOption> categoryList,
            String category)
        {
            if (!Guid.TryParseExact(category, "D", out var id) || id == Guid.Empty) return null;
            return categoryList.FirstOrDefault(item => item.IsSelectable
                && Guid.TryParseExact(item.Id, "D", out var optionId)
                && optionId == id)?.Id;
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
        /// <summary>目前表單選取的奉獻類別正式資料庫 Guid，不是顯示名稱。</summary>
        public String Category { get; set; }
        /// <summary>付款流程本次驗證後的類別快照；只供內部建單使用，不作為表單輸入或跨請求狀態。</summary>
        internal OfferingCategory? ResolvedCategory { get; set; }
        public String PayWay { get; set; }                              //付款方式
        public String DeductTotalNumber { get; set; }                   //定期定額總期數
        public String Others { get; set; }                              //其他奉獻
        /// <summary>
        /// 多類別奉獻明細。空集合表示呼叫端仍使用相容的單一 Category／Amount／Others 欄位。
        /// 這個集合只屬於目前表單物件，禁止放入 static 或跨請求快取，以免會友資料互相洩漏。
        /// </summary>
        public List<DonationLineItemInput> Lines { get; set; } = new List<DonationLineItemInput>();

        /// <summary>
        /// 建立目前表單的淺層副本，並只替換指定明細的類別、金額與說明。
        /// 建單流程會為每一列呼叫此方法，再交給既有 SetFeeParameter；
        /// 因此可以沿用舊簽章，同時確保下一列不會讀到上一列被改寫的值。
        /// Lines 會重設成新的空集合，避免複製後再次被正規化成多筆而重複建單。
        /// </summary>
        public DonationPaymentFormModel CloneForLine(DonationLineItemInput line)
        {
            // 明細是建單必要輸入；null 代表程式呼叫錯誤，立即失敗比建立不完整收費單安全。
            if (line == null) throw new ArgumentNullException(nameof(line));
            // MemberwiseClone 不會修改原始表單；Lines 額外建立新集合，隔離複製物件的可變狀態。
            var clone = (DonationPaymentFormModel)MemberwiseClone();
            clone.Category = line.Category;
            clone.Amount = line.Amount;
            clone.Others = line.Others;
            clone.Lines = new List<DonationLineItemInput>();
            clone.ResolvedCategory = line.ResolvedCategory;
            // MemberwiseClone 對集合欄位只會複製參考；若直接沿用，任一列建單流程修改集合就會
            // 污染原始表單，甚至把一位會友的暫存資料帶到下一列或下一個請求。每個可變集合都
            // 建立新的容器，讓副本擁有自己的集合生命週期；集合元素本身是既有流程的資料快照，
            // 建單階段只讀取，不在此處註冊事件、配置 timer、開啟 stream 或保留外部資源。
            clone.DedicationCategoryList = DedicationCategoryList?
                .Select(item => item.Clone())
                .ToList() ?? new List<DonationOfferingCategoryOption>();
            clone.OtherCategoryArray = OtherCategoryArray?.ToList() ?? new List<String>();
            clone.SpecialCategoryArray = SpecialCategoryArray?.ToList() ?? new List<String>();
            clone.CreditCardList = CreditCardList?.ToList() ?? new List<CreditCard>();
            clone.DedicationFeeList = DedicationFeeList?.ToList() ?? new List<DedicationFee>();
            clone.DedicationBookingList = DedicationBookingList?.ToList() ?? new List<DedicationBooking>();
            clone.SameNameList = SameNameList?.ToList() ?? new List<SameNameElement>();
            return clone;
        }
        public String DedicateLocation { get; set; }                    //奉獻分堂
        public String Explain { get; set; }                             //備註
        public String WeeklyNote { get; set; }                          //週報專用備註

        public DateTime RecieptDate { get; set; } = DateTime.Now;       //收據日期
        public String ClickType { get; set; }                           //查詢或是上傳
        public String SelectedCreditCard { get; set; }                  //選取的信用卡
        public String SelectedContactId { get; set; }                   //從同名清單選取的 CRM Contact GUID，用於上傳時直接定位避免模糊查詢卡住

        /// <summary>本次教會範圍的最小 ID／名稱選項投影；不得放入跨請求快取。</summary>
        public List<DonationOfferingCategoryOption> DedicationCategoryList { get; set; } = new List<DonationOfferingCategoryOption>();

        public List<String> OtherCategoryArray { get; set; }
        public List<String> SpecialCategoryArray { get; set; }
        public List<CreditCard> CreditCardList { get; set; }
        public List<DedicationFee> DedicationFeeList { get; set; }
        public int TotalAmount { get; set; }                            //奉獻總金額

        public List<DedicationBooking> DedicationBookingList { get; set; }//認獻清單
        public String SelectedDedicationBooking { get; set; }             //選取的認獻

        public DateTime QueryStartDate { get; set; } = new DateTime(DateTime.Now.Year, 1, 1); //奉獻查詢開始日期
        public DateTime QueryEndDate { get; set; } = DateTime.Now;      //奉獻查詢結束日期

        public bool IsAOfficeWorker { get; set; } = false;              //是否符合輸入奉獻的行政人員

        public List<SameNameElement> SameNameList { get; set; } = new List<SameNameElement>();

    }

    /// <summary>奉獻表單的最小下拉選項投影，不暴露會計代碼或舊選項值。</summary>
    public sealed class DonationOfferingCategoryOption
    {
        /// <summary>
        /// 建立一個由伺服器讀取的選項投影；metadata 相容項目沒有正式 ID 且不可提交。
        /// </summary>
        /// <param name="id">奉獻類別正式 Guid；無正式類別的 metadata 項目使用空字串。</param>
        /// <param name="name">伺服器來源的顯示文字。</param>
        /// <param name="isSelectable">是否有正式 ID 並可供建立新記錄使用。</param>
        public DonationOfferingCategoryOption(string id, string name, bool isSelectable)
        {
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
            IsSelectable = isSelectable;
        }

        /// <summary>表單送出的正式資料庫 Guid；不可提交項目為空字串。</summary>
        public string Id { get; }

        /// <summary>供畫面呈現的類別名稱，不能作為身分或授權依據。</summary>
        public string Name { get; }

        /// <summary>沒有正式 lookup ID 的 metadata 相容項目為 false。</summary>
        public bool IsSelectable { get; }

        /// <summary>建立 DTO 獨立副本，避免複製後共用可變集合元素。</summary>
        public DonationOfferingCategoryOption Clone() => new(Id, Name, IsSelectable);
    }
}

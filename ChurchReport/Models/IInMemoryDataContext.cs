using ChurchReport.Tools;
using ChurchReport.ViewModel;

namespace ChurchReport.Models
{
    /// <summary>
    /// 記憶體資料上下文介面，用於支援 Dependency Injection 和單元測試。
    /// 此介面只描述 ChurchReport 應用程式內部的 session/context 管理，不屬於可重用金流核心。
    /// </summary>
    public interface IInMemoryDataContext
    {
        /// <summary>清單管理器。</summary>
        ListManager ListManager { get; }

        /// <summary>小組資料清單。</summary>
        SmallGroupDataList SmallGroupDataList { get; }

        /// <summary>週報資料。</summary>
        WeeklyReportData WeeklyReportData { get; }

        /// <summary>新人資料模型。</summary>
        NewPersonModel NewPersonModel { get; }

        /// <summary>個人資訊模型。</summary>
        PersonalInfomationModel PersonalInfomationModel { get; }

        /// <summary>幸福小組資料管理器。</summary>
        HappyGroupDataManager HappyGroupDataManager { get; }

        /// <summary>清單管理資料管理器。</summary>
        ListManagementDataManager ListManagementDataManager { get; }

        /// <summary>裝備資料管理器。</summary>
        EquipmentDataManager EquipmentDataManager { get; }

        /// <summary>繳費清單。</summary>
        FeeList FeeList { get; }

        /// <summary>LINE 綁定視圖模型。</summary>
        LineBindingViewModel LineBindingViewModel { get; }

        /// <summary>行事曆清單管理器。</summary>
        AppointmentsListManager AppointmentsListManager { get; }

        /// <summary>
        /// ChurchReport 奉獻付款 UI 狀態管理器。
        /// 新程式應使用此屬性；它負責產品流程、CRM 狀態與 LINE 通知，不屬於可重用金流核心。
        /// </summary>
        DonationPaymentManager DonationPaymentManager { get; }


        /// <summary>問卷管理器。</summary>
        PollManager PollManager { get; }

        /// <summary>工具類別實例。</summary>
        ToolUtilityNameSpace.ToolUtilityClass ToolUtilityClass { get; }

        /// <summary>設定小組資料。</summary>
        void SetupSmallGroupData(string fullName, string account, string password, System.DateTime selectDate, bool displayDateFlag);

        /// <summary>儲存變更。</summary>
        void SaveChanges();
    }
}

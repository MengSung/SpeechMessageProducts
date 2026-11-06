/*
 * 檔案整體說明
 *
 * 目的與定位：記憶體資料上下文介面，用於支援 Dependency Injection 和單元測試。 此介面只描述 ChurchReport 應用程式內部的 session/context 管理，不屬於可重用金流核心。
 *
 * 主要功能與流程：
 * 本檔宣告 IInMemoryDataContext。
 * - ChurchReport 奉獻付款 UI 狀態管理器。 新程式應使用此屬性；它負責產品流程、CRM 狀態與 LINE 通知，不屬於可重用金流核心。
 * 資料／選項欄位包含 IInMemoryDataContext，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 ChurchReport.MemberInfo.Tests/Images/ImageUploadCharacterizationTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/DedicationController.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs 追查目前工作樹中的合作／使用位置。
 */
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

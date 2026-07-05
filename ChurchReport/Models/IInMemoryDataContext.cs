// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Models/IInMemoryDataContext.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於資料模型或 ViewModel 層，註解重點在說明欄位語意、序列化/繫結用途與相容性限制。
// 主要型別：interface IInMemoryDataContext
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：ChurchReport.Tools、ChurchReport.ViewModel
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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

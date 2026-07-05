// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/WeeklyScheduleSettings.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：record WeeklyScheduleSettings、class WeeklyScheduleProvider
// 主要成員：GetFirstDayOfWeek、Initialize
// 引用命名空間：System
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;

namespace ChurchReport.Services
{
    /// <summary>
    /// 每週排程設定。
    /// 對應 appsettings.json 的 WeeklySchedule 區段，
    /// 用來描述系統判定「每週第一日」的規則。
    /// </summary>
    public record WeeklyScheduleSettings
    {
        /// <summary>
        /// 每週第一日的中文名稱。
        /// 預設值為「星期一」，用來維持既有系統行為的向後相容性。
        /// </summary>
        public string 每週的第一日 { get; init; } = "星期一";

        /// <summary>
        /// 取得設定對應的 <see cref="DayOfWeek"/>。
        /// </summary>
        /// <returns>設定值對應的 <see cref="DayOfWeek"/>；無效值時回退到星期一。</returns>
        public DayOfWeek GetFirstDayOfWeek()
        {
            return SundayCalculator.ParseChineseDayOfWeek(每週的第一日);
        }
    }

    /// <summary>
    /// 提供舊有非 DI 類別統一讀取每週第一日設定的入口。
    /// </summary>
    public static class WeeklyScheduleProvider
    {
        private static DayOfWeek _firstDayOfWeek = DayOfWeek.Monday;

        /// <summary>
        /// 在應用程式啟動時初始化每週第一日設定。
        /// </summary>
        /// <param name="firstDayOfWeek">應用程式實際採用的每週第一日。</param>
        public static void Initialize(DayOfWeek firstDayOfWeek)
        {
            _firstDayOfWeek = firstDayOfWeek;
        }

        /// <summary>
        /// 取得目前全系統共用的每週第一日設定。
        /// </summary>
        public static DayOfWeek FirstDayOfWeek => _firstDayOfWeek;
    }
}

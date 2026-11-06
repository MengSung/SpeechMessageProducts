/*
 * 檔案整體說明
 *
 * 目的與定位：每週排程設定。 對應 appsettings.json 的 WeeklySchedule 區段， 用來描述系統判定「每週第一日」的規則。
 *
 * 主要功能與流程：
 * 本檔宣告 WeeklyScheduleSettings、WeeklyScheduleProvider。
 * - 每週第一日的中文名稱。 預設值為「星期一」，用來維持既有系統行為的向後相容性。
 * 主要操作入口包含 GetFirstDayOfWeek、Initialize。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Models/ListManagementDataManager.cs、
 * SpeechMessageProducts.ChurchReport/Models/ListSmallGroupWeeklyReport.cs、
 * SpeechMessageProducts.ChurchReport/Startup.cs、SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs、
 * SpeechMessageProducts.ChurchReport/Tools/WeeklyReportProcessor.cs 追查目前工作樹中的合作／使用位置。
 */
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

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

using System;

namespace ChurchReport.Services
{
    /// <summary>
    /// 集中式主日日期計算服務。
    /// 主日永遠固定是星期日，但所屬週次必須依據設定的每週第一日來判定。
    /// </summary>
    public static class SundayCalculator
    {
        /// <summary>
        /// 將中文星期名稱解析成 <see cref="DayOfWeek"/>。
        /// 若設定值為空白、null 或不支援的內容，會回退到星期一，
        /// 以維持舊系統「每週從星期一開始」的既有行為。
        /// </summary>
        /// <param name="chineseDay">中文星期名稱，例如「星期日」、「星期一」、「星期六」。</param>
        /// <returns>對應的 <see cref="DayOfWeek"/> 列舉值。</returns>
        public static DayOfWeek ParseChineseDayOfWeek(string chineseDay)
        {
            return chineseDay switch
            {
                "星期日" => DayOfWeek.Sunday,
                "星期一" => DayOfWeek.Monday,
                "星期二" => DayOfWeek.Tuesday,
                "星期三" => DayOfWeek.Wednesday,
                "星期四" => DayOfWeek.Thursday,
                "星期五" => DayOfWeek.Friday,
                "星期六" => DayOfWeek.Saturday,
                _ => DayOfWeek.Monday
            };
        }

        /// <summary>
        /// 計算指定日期所屬週次的主日日期。
        /// 演算法會先回推出該日期落在哪一個週區間的起始日，
        /// 再找出該週區間內對應的星期日，並回傳純日期值。
        /// </summary>
        /// <param name="date">要判定所屬週次的任意日期。</param>
        /// <param name="firstDayOfWeek">每週第一日設定值。</param>
        /// <returns>該日期所屬週次的主日日期。</returns>
        public static DateTime CalculateSunday(DateTime date, DayOfWeek firstDayOfWeek)
        {
            // 先算出輸入日期距離該週起始日有幾天，
            // 這樣就能回推出同一週的 weekStart。
            int daysSinceWeekStart = ((int)date.DayOfWeek - (int)firstDayOfWeek + 7) % 7;

            // 僅保留日期部分，避免把原本的時間帶入後續主日計算。
            DateTime weekStart = date.Date.AddDays(-daysSinceWeekStart);

            // 星期日永遠是目標日，但在不同週起始規則下，
            // 它位於該週區間中的第幾天會不同。
            int daysToSunday = ((int)DayOfWeek.Sunday - (int)firstDayOfWeek + 7) % 7;

            return weekStart.AddDays(daysToSunday).Date;
        }

        /// <summary>
        /// 依指定主日與每週第一日，回推出該週區間的起始日。
        /// 例如：
        /// - 每週第一日為星期一時，主日 2026/03/08 對應的起始日是 2026/03/02。
        /// - 每週第一日為星期六時，主日 2026/03/08 對應的起始日是 2026/03/07。
        /// - 每週第一日為星期日時，主日 2026/03/08 對應的起始日就是 2026/03/08。
        /// </summary>
        /// <param name="sunday">已計算完成的主日日期。</param>
        /// <param name="firstDayOfWeek">每週第一日設定值。</param>
        /// <returns>該主日所屬週區間的起始日。</returns>
        public static DateTime CalculateWeekStart(DateTime sunday, DayOfWeek firstDayOfWeek)
        {
            // 先算出「星期日」在目前週規則下距離週起始日幾天，
            // 再由主日反推出該週的真正起始日。
            int daysFromWeekStartToSunday = ((int)DayOfWeek.Sunday - (int)firstDayOfWeek + 7) % 7;

            return sunday.Date.AddDays(-daysFromWeekStartToSunday);
        }

        /// <summary>
        /// 依指定主日與每週第一日，計算該週區間的結束日。
        /// 週區間固定為 7 天，因此結束日一定是起始日加 6 天。
        /// </summary>
        /// <param name="sunday">已計算完成的主日日期。</param>
        /// <param name="firstDayOfWeek">每週第一日設定值。</param>
        /// <returns>該主日所屬週區間的結束日。</returns>
        public static DateTime CalculateWeekEnd(DateTime sunday, DayOfWeek firstDayOfWeek)
        {
            DateTime weekStart = CalculateWeekStart(sunday, firstDayOfWeek);

            return weekStart.AddDays(6);
        }
    }
}

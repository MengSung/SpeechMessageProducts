/*
 * 檔案整體說明
 *
 * 目的與定位：P5-05 週報生成的純時間與正式識別規則，供活動與未來產品入口
 * 使用相同業務事件鍵；不以姓名、顯示名稱或暫存請求 token 識別正式記錄。
 * 主要功能與流程：明確 UTC 瞬間經 ChurchClock 轉成教會當地日期，星期日為週首，
 * 一次回傳本週與下週。週報 ID 由教會／名單／當地星期日計算；出席 ID 再納入
 * 週報與正式 Contact ID。回傳的 ID 必須直接作為資料庫主鍵，由交易 Create 的
 * 主鍵唯一約束保障跨程序唯一性；本檔單獨不能證明資料庫原子性。
 * 使用邊界與維護：呼叫端先以平台使用者權限讀取並驗證名單、會員與教會，
 * 本檔不授權、不查詢、不寫入，不持有 SDK、秘密、Entity、快取或背景工作。
 * 每次結果獨立且唯讀，沒有需排空或 Dispose 的資源；錯誤輸入在產生候選前拒絕。
 * 雙目標固定 UTC／教會與 ID 測試保護演算法，活動交易另驗原子失敗及重試。
 */
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Rules;

namespace SpeechMessageProducts.GoodShepherd.Core.WeeklyReports;

/// <summary>無跨呼叫狀態的週報時間及正式記錄識別規則。</summary>
public static class WeeklyReportGenerationRule
{
    /// <summary>從單一 UTC 瞬間取得教會的本週及下週星期日午夜，不讀取伺服器當地時間。</summary>
    /// <param name="utcNow">本次生成唯一讀取的 UTC 瞬間，Kind 必須為 Utc。</param>
    /// <param name="timeZone">由伺服器正式教會設定讀回的 IANA 時區。</param>
    /// <returns>當次獨立唯讀的兩個 Unspecified 日期。</returns>
    public static IReadOnlyList<DateTime> Weeks(DateTime utcNow, string timeZone)
    {
        var local = new ChurchClock().ToChurchTime(utcNow, timeZone).Date;
        var sunday = local.AddDays(-(int)local.DayOfWeek);
        return new ReadOnlyCollection<DateTime>(new[] { sunday, sunday.AddDays(7) });
    }

    /// <summary>產生伺服器持有的正式週報主鍵；呼叫端必須以該 ID 原子 Create，不能只查後隨機建立。</summary>
    /// <param name="churchId">已驗證正式教會 ID。</param>
    /// <param name="listId">已驗證正式名單 ID。</param>
    /// <param name="localSunday">教會當地星期日午夜，不能傳伺服器 Local 或 UTC 日期冒充。</param>
    /// <returns>分教會、名單及日期的固定資料庫主鍵。</returns>
    public static Guid ReportId(Guid churchId, Guid listId, DateTime localSunday)
    {
        if (churchId == Guid.Empty || listId == Guid.Empty || localSunday.Kind != DateTimeKind.Unspecified ||
            localSunday.TimeOfDay != TimeSpan.Zero || localSunday.DayOfWeek != DayOfWeek.Sunday)
            throw new ArgumentException("Invalid weekly report identity scope.");
        return DeterministicGuid.Create("WeeklyReport.Primary.v1", churchId, listId, localSunday);
    }

    /// <summary>同週報同正式會員只產生一個正式出席主鍵；不同同名會員不合併。</summary>
    /// <param name="reportId">該週報的正式資料庫 ID。</param>
    /// <param name="contactId">正式會員的資料庫 ID。</param>
    /// <returns>以資料庫 Create 唯一約束落實的出席列主鍵。</returns>
    public static Guid AttendanceId(Guid reportId, Guid contactId)
    {
        if (reportId == Guid.Empty || contactId == Guid.Empty) throw new ArgumentException("Invalid attendance identity scope.");
        return DeterministicGuid.Create("WeeklyReport.Attendance.Primary.v1", reportId, contactId);
    }
}

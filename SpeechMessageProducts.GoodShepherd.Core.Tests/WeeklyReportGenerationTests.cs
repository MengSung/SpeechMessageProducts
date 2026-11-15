/*
 * 檔案整體說明
 *
 * 目的與定位：以明確 UTC 與教會時區保護 P5-05 週報生成的週界線及正式記錄識別。
 * 本檔只使用合成教會、名單與會友 GUID，不呼叫 Dataverse、LINE 或本機秘密。
 * 主要功能與流程：先驗同一 UTC 瞬間跨教會的不同星期日，再驗同一業務事件的
 * 穩定 ID、不同名單／週次／會友的隔離，並注入既有重複週報識別與空值拒絕。
 * ID 測試只保護純演算法；跨程序唯一性與原子建立由活動外殼交易測試另外保護。
 * 使用邊界與維護：輸入與不可變輸出只由案例持有，不保存快取或可變共用狀態；
 * 沒有連線、背景工作、計時器或待排空資源。失敗須阻止後續建立，不可去重隱藏。
 */
using SpeechMessageProducts.GoodShepherd.Core.WeeklyReports;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests;

/// <summary>保護週報時間與正式 ID 的純值契約，不把離線斷言寫成資料庫驗收。</summary>
public sealed class WeeklyReportGenerationTests
{
    /// <summary>週日 UTC 邊界在台北與洛杉磯屬不同週；固定一個瞬間防止讀時鐘競態。</summary>
    [Fact]
    public void WeeklyReport_WeekBoundary_UsesChurchTimeZone()
    {
        var now = new DateTime(2026, 11, 15, 0, 30, 0, DateTimeKind.Utc);
        var taipei = WeeklyReportGenerationRule.Weeks(now, "Asia/Taipei");
        var losAngeles = WeeklyReportGenerationRule.Weeks(now, "America/Los_Angeles");
        Assert.Equal(new DateTime(2026, 11, 15), taipei[0]);
        Assert.Equal(new DateTime(2026, 11, 22), taipei[1]);
        Assert.Equal(new DateTime(2026, 11, 8), losAngeles[0]);
        Assert.Equal(DateTimeKind.Unspecified, taipei[0].Kind);
        Assert.Throws<ArgumentException>(() =>
            WeeklyReportGenerationRule.Weeks(DateTime.SpecifyKind(now, DateTimeKind.Unspecified), "Asia/Taipei"));
    }

    /// <summary>同名會員以正式 ID 保留；不同教會、名單與週次不可得到同一正式記錄 ID。</summary>
    [Fact]
    public void WeeklyReport_Generation_StablePrimaryIds_PartitionBusinessEvents()
    {
        var church = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var list = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var sunday = new DateTime(2026, 11, 15);
        var report = WeeklyReportGenerationRule.ReportId(church, list, sunday);
        Assert.Equal(report, WeeklyReportGenerationRule.ReportId(church, list, sunday));
        Assert.NotEqual(report, WeeklyReportGenerationRule.ReportId(Guid.NewGuid(), list, sunday));
        Assert.NotEqual(report, WeeklyReportGenerationRule.ReportId(church, Guid.NewGuid(), sunday));
        Assert.NotEqual(report, WeeklyReportGenerationRule.ReportId(church, list, sunday.AddDays(7)));
        var member = Guid.NewGuid();
        Assert.Equal(WeeklyReportGenerationRule.AttendanceId(report, member),
            WeeklyReportGenerationRule.AttendanceId(report, member));
        Assert.NotEqual(WeeklyReportGenerationRule.AttendanceId(report, member),
            WeeklyReportGenerationRule.AttendanceId(report, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => WeeklyReportGenerationRule.ReportId(Guid.Empty, list, sunday));
        Assert.Throws<ArgumentException>(() => WeeklyReportGenerationRule.ReportId(church, list, sunday.AddDays(1)));
    }
}

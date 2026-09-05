// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/SessionMonitorServiceTests.cs
// 所屬區塊：ChurchReport 產品層回歸測試，驗證 DEBUG Session 診斷服務的資源生命週期與記憶體上限。
// 檔案責任：固定程序級 Session 診斷索引不得因大量短命 Session 無界成長，也不得保留任何 Session 內容。
// 主要型別：SessionMonitorServiceTests。
// 主要成員：RecordSessionActivity_when_unique_session_volume_exceeds_capacity_keeps_a_hard_bound。
// 引用命名空間：ChurchReport.Services.Monitoring、FluentAssertions、Microsoft.Extensions.Logging.Abstractions、Xunit。
// 閱讀路徑：先讀測試名稱與 Arrange/Act/Assert，了解容量保護；再讀備註了解資源擁有權與故障注入。
// 維護重點：不可將此測試改為只驗證 timer 清理；壓力到來的五分鐘清理間隔內也必須維持硬上限。
// 行為保護：測試不建立 HTTP request、Session store、CRM 連線或背景工作；using 是唯一的服務與計時器釋放路徑。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
#if DEBUG
using ChurchReport.Services.Monitoring;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 驗證 DEBUG 專用的 <see cref="SessionMonitorService"/> 不會把不同 Session 的診斷紀錄無限制保留在程序記憶體。
/// </summary>
/// <remarks>
/// 監控器是 singleton，卻只允許保存統計所需的短命識別資料；它不保存使用者內容、認證、權杖或 profile。
/// 此測試以超過上限的一次性 Session 流量重現清理 timer 尚未觸發時的最壞情況，並在 <c>using</c> 結束時釋放
/// 服務擁有的兩個 timer 與其字典，避免測試本身殘留計時器、回呼或 Session 資料。
/// </remarks>
public sealed class SessionMonitorServiceTests
{
    /// <summary>
    /// 保護唯一 Session 數量超過容量時，程序級監控索引仍保持硬性記憶體上限的契約。
    /// </summary>
    /// <remarks>
    /// 故障注入連續送入 4,097 個不重複的 synthetic Session ID，刻意不等待五分鐘的週期清理。
    /// 決定性斷言是 <see cref="SessionStatistics.TotalTrackedSessions"/> 不得超過 4,096；若新增路徑只依賴
    /// timer 或不做容量淘汰，舊實作會回報 4,097 而失敗。測試不觀察或輸出任何實際使用者 Session ID。
    /// </remarks>
    [Fact]
    public void RecordSessionActivity_when_unique_session_volume_exceeds_capacity_keeps_a_hard_bound()
    {
        using var monitor = new SessionMonitorService(NullLogger<SessionMonitorService>.Instance);

        for (var index = 0; index <= 4_096; index++)
        {
            monitor.RecordSessionActivity($"synthetic-session-{index:D5}");
        }

        monitor.GetStatistics().TotalTrackedSessions.Should().BeLessOrEqualTo(4_096,
            "程序級 DEBUG 監控器絕不能在清理 timer 下一次觸發前無界保留不同 Session 的診斷索引");
    }
}
#endif

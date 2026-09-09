// ============================================================================
// 檔案路徑：ChurchReport.MemberInfo.Tests/Models/RowPublicationGuardTests.cs
// 測試責任：驗證所有產品可共用的資料列發布前 stable-ID 契約，避免把同名資料誤當成重複，
//           也避免同一權威資料列在實際 consumer collection 中被重複發布。
// 故障模型：以同名不同 ID、相同 ID、缺少 ID、null row 與超過容量上限的候選資料，
//           模擬慢網路重試、快取命中後活物件圖被意外寫入及組裝錯誤；測試只使用方法區域資料，
//           不建立 Session、HttpContext、CRM client、timer、背景工作或長生命週期 cache。
// 編碼要求：本檔案必須以 UTF-8 without BOM、CRLF、final CRLF 儲存。
// ============================================================================
using ChurchReport.Models;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Models;

/// <summary>
/// 驗證資料列交付給 API、Razor、Grid 或報表之前，必須依權威資料庫 ID 做 fail-closed 檢查。
/// </summary>
public sealed class RowPublicationGuardTests
{
    /// <summary>
    /// 保護合法同名資料不可被錯誤合併。
    /// 故障注入建立兩個姓名完全相同但 PresentRecordId 不同的成員；決勝斷言為兩列皆保留，
    /// 且回傳集合不是呼叫端稍後可藉由修改而污染的輸入集合。
    /// </summary>
    [Fact]
    public void ValidateDetachedRows_PreservesSameNameRowsWithDifferentStableIds()
    {
        var rows = new[]
        {
            new Member { PresentRecordId = "record-a", FullName = "王小明" },
            new Member { PresentRecordId = "record-b", FullName = "王小明" }
        };

        var result = RowPublicationGuard.ValidateDetachedRows(
            rows,
            row => row.PresentRecordId,
            "測試同名資料",
            maximumRowCount: 10);

        result.Should().HaveCount(2);
        result.Select(row => row.PresentRecordId).Should().ContainInOrder("record-a", "record-b");
    }

    /// <summary>
    /// 保護同一 consumer collection 的 exact duplicate stable ID 不會被靜默取第一筆或合併。
    /// 故障注入使用相同 PresentRecordId 但不同姓名，模擬同一權威記錄被重複 append；決勝斷言為
    /// guard 擲出可診斷例外，讓 API 在交給 DataSourceLoader 前停止發布。
    /// </summary>
    [Fact]
    public void ValidateDetachedRows_RejectsExactDuplicateStableId()
    {
        var rows = new[]
        {
            new Member { PresentRecordId = "record-a", FullName = "第一筆" },
            new Member { PresentRecordId = "record-a", FullName = "第二筆" }
        };

        var action = () => RowPublicationGuard.ValidateDetachedRows(
            rows,
            row => row.PresentRecordId,
            "測試重複資料",
            maximumRowCount: 10);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*測試重複資料*PresentRecordId*record-a*");
    }

    /// <summary>
    /// 保護正式資料不得用 null、空白或未完成的身份進入可渲染集合。
    /// 故障注入分別使用 null row 與空白 ID；決勝斷言為兩者都 fail closed，而不是自動產生
    /// Guid、counter 或時間字串掩蓋上游資料錯誤。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ValidateDetachedRows_RejectsNullOrMissingStableId(bool useNullRow)
    {
        var rows = useNullRow
            ? new Member[] { null! }
            : new[] { new Member { PresentRecordId = "  " } };

        var action = () => RowPublicationGuard.ValidateDetachedRows(
            rows,
            row => row.PresentRecordId,
            "測試缺少身份",
            maximumRowCount: 10);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*測試缺少身份*PresentRecordId*");
    }

    /// <summary>
    /// 保護集合容量是有界的，避免錯誤重試或失控組裝保留無限資料。
    /// 故障注入建立超過上限的候選；決勝斷言為在完成列舉前拒絕，不配置無界 queue 或 cache。
    /// </summary>
    [Fact]
    public void ValidateDetachedRows_RejectsRowsBeyondConfiguredLimit()
    {
        var rows = Enumerable.Range(1, 3)
            .Select(index => new Member { PresentRecordId = $"record-{index}" })
            .ToArray();

        var action = () => RowPublicationGuard.ValidateDetachedRows(
            rows,
            row => row.PresentRecordId,
            "測試容量上限",
            maximumRowCount: 2);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*測試容量上限*上限*");
    }
}

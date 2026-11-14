/*
檔案整體說明

目的與定位：定義 PluginHarness 的候選資料模型、三種分類值與固定 attendance 黃金資料，讓產生流程以 operation-local 物件組合後再發布。

主要功能與流程：專案結果只保存穩定名稱、數量、雜湊與固定診斷代碼，不保存原始碼、秘密、例外本文或外部服務回應。AttendanceGolden 建立正常、邊界及錯誤三筆合成情境，並驗證兩筆同顯示名稱記錄仍以不同穩定 ID 保留。

使用邊界與維護：模型是單次呼叫的純資料，沒有 static 可變集合、Session、credential、網路資源或背景工作。呼叫端必須先完成所有分類與驗證，候選未完成時不得交給序列化器；修改欄位時同步檢查秘密掃描、原子輸出與下游黃金契約。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LegacyGolden.PluginHarness;

/// <summary>舊專案的離線盤點分類；分類本身不代表實機 D365 或 LINE 行為已驗收。</summary>
internal enum ProjectClassification
{
    /// <summary>來源已通過安全掃描且 Roslyn 編譯成功，可形成離線候選。</summary>
    Generated,

    /// <summary>來源讀取、秘密掃描或 Roslyn 編譯失敗；不得把部分結果當成可執行黃金。</summary>
    CompileFailed,

    /// <summary>發現無法安全攔截的連外 API；拒絕執行與編譯，不建立網路替身假象。</summary>
    BlockedByNetworkApi
}

/// <summary>一個舊第一層專案的脫敏分類結果；不攜帶來源本文。</summary>
internal sealed class ProjectResult
{
    /// <summary>初始化結果所需的固定摘要欄位。</summary>
    internal ProjectResult(string name, ProjectClassification classification, int sourceFileCount, long sourceByteCount, string sourceSha256, IEnumerable<string> diagnostics)
    {
        Name = name;
        Classification = classification;
        SourceFileCount = sourceFileCount;
        SourceByteCount = sourceByteCount;
        SourceSha256 = sourceSha256;
        Diagnostics = diagnostics == null ? new List<string>() : diagnostics.Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToList();
    }

    /// <summary>舊專案資料夾或專案檔的穩定顯示名稱。</summary>
    internal string Name { get; }

    /// <summary>本專案的三態分類。</summary>
    internal ProjectClassification Classification { get; }

    /// <summary>讀取且納入編譯判斷的 C# 檔案數。</summary>
    internal int SourceFileCount { get; }

    /// <summary>讀取來源的位元組總量；超限時不會建立結果。</summary>
    internal long SourceByteCount { get; }

    /// <summary>排序後來源位元組的 SHA-256；不可由此還原來源內容。</summary>
    internal string SourceSha256 { get; }

    /// <summary>不含來源本文與例外訊息的固定診斷代碼。</summary>
    internal IReadOnlyList<string> Diagnostics { get; }
}

/// <summary>整次離線盤點的完整候選；只有所有專案結果與 attendance 驗證完成後才可序列化。</summary>
internal sealed class HarnessCandidate
{
    /// <summary>建立候選並保留獨立清單，避免 caller 後續修改內部集合。</summary>
    internal HarnessCandidate(string sourceRootName, IEnumerable<ProjectResult> projects, IEnumerable<AttendanceScenario> attendance)
    {
        SourceRootName = sourceRootName;
        Projects = projects.OrderBy(item => item.Name, StringComparer.Ordinal).ToList();
        Attendance = attendance.OrderBy(item => item.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>通過根目錄驗證後的固定根目錄名稱，不含主機路徑。</summary>
    internal string SourceRootName { get; }

    /// <summary>依專案名排序的完整分類結果。</summary>
    internal IReadOnlyList<ProjectResult> Projects { get; }

    /// <summary>至少三筆由工具固定建立的合成 attendance 情境。</summary>
    internal IReadOnlyList<AttendanceScenario> Attendance { get; }

    /// <summary>依分類名稱統計專案數量，供命令列摘要使用。</summary>
    internal int Count(string classification)
    {
        return Projects.Count(item => string.Equals(item.Classification.ToString(), classification, StringComparison.Ordinal));
    }
}

/// <summary>一筆離線 attendance 黃金情境；legacy、expected 與差異均為固定合成資料。</summary>
internal sealed class AttendanceScenario
{
    /// <summary>建立不帶個資或可用憑證的合成情境。</summary>
    internal AttendanceScenario(string id, AttendanceSnapshot legacy, AttendanceSnapshot expected, IEnumerable<string> declaredDifferences)
    {
        Id = id;
        Legacy = legacy;
        Expected = expected;
        DeclaredDifferences = declaredDifferences == null ? new List<string>() : declaredDifferences.ToList();
    }

    /// <summary>穩定情境 ID。</summary>
    internal string Id { get; }

    /// <summary>舊行為的脫敏摘要。</summary>
    internal AttendanceSnapshot Legacy { get; }

    /// <summary>新規則期望的脫敏摘要。</summary>
    internal AttendanceSnapshot Expected { get; }

    /// <summary>明確列出的刻意差異；不包含來源例外本文。</summary>
    internal IReadOnlyList<string> DeclaredDifferences { get; }
}

/// <summary>attendance 黃金的可序列化摘要；只含合成欄位。</summary>
internal sealed class AttendanceSnapshot
{
    /// <summary>建立一筆固定欄位的摘要。</summary>
    internal AttendanceSnapshot(string outcome, string date, bool churchLocalDate, IEnumerable<AttendanceRow> rows)
    {
        Outcome = outcome;
        Date = date;
        ChurchLocalDate = churchLocalDate;
        Rows = rows == null ? new List<AttendanceRow>() : rows.ToList();
    }

    /// <summary>固定結果分類。</summary>
    internal string Outcome { get; }

    /// <summary>合成日期；不代表實機事件時間。</summary>
    internal string Date { get; }

    /// <summary>是否採教會本地日期邊界。</summary>
    internal bool ChurchLocalDate { get; }

    /// <summary>保持穩定 ID 的出席列。</summary>
    internal IReadOnlyList<AttendanceRow> Rows { get; }
}

/// <summary>attendance 單列；同名資料必須靠穩定的 PresentRecordId 區分。</summary>
internal sealed class AttendanceRow
{
    /// <summary>建立合成出席列。</summary>
    internal AttendanceRow(string presentRecordId, string displayName, bool present)
    {
        PresentRecordId = presentRecordId;
        DisplayName = displayName;
        Present = present;
    }

    /// <summary>伺服器擁有的穩定列身分。</summary>
    internal string PresentRecordId { get; }

    /// <summary>僅供展示的合成名稱，不能作為 identity。</summary>
    internal string DisplayName { get; }

    /// <summary>合成出席值。</summary>
    internal bool Present { get; }
}

/// <summary>固定產生並驗證三筆 attendance 合成黃金；不讀取外部來源或執行網路。</summary>
internal static class AttendanceGolden
{
    private static readonly AttendanceRow[] EmptyRows = Array.Empty<AttendanceRow>();
    private static readonly AttendanceRow[] MissingIdRows = { new AttendanceRow("", "Synthetic Member", false) };
    private static readonly string[] NoDifferences = Array.Empty<string>();
    private static readonly string[] BoundaryDifferences = { "Uses church-local date boundary." };
    private static readonly string[] ErrorDifferences = { "Missing stable ID fails closed." };

    /// <summary>建立正常、邊界與錯誤三個情境，並執行同名穩定性檢查。</summary>
    internal static IReadOnlyList<AttendanceScenario> CreateAndValidate()
    {
        var sameNameRows = new[]
        {
            new AttendanceRow("present-001", "Synthetic Member", true),
            new AttendanceRow("present-002", "Synthetic Member", false)
        };
        var scenarios = new List<AttendanceScenario>
        {
            new AttendanceScenario("attendance-normal", new AttendanceSnapshot("published", "2025-01-15", true, sameNameRows),
                new AttendanceSnapshot("published", "2025-01-15", true, sameNameRows), NoDifferences),
            new AttendanceScenario("attendance-boundary", new AttendanceSnapshot("published", "2024-02-29", true, EmptyRows),
                new AttendanceSnapshot("published", "2024-02-29", true, EmptyRows), BoundaryDifferences),
            new AttendanceScenario("attendance-error", new AttendanceSnapshot("ignored", "2025-01-15", true, MissingIdRows),
                new AttendanceSnapshot("rejected", "2025-01-15", true, MissingIdRows), ErrorDifferences)
        };

        var normalRows = scenarios[0].Legacy.Rows;
        var ids = normalRows.Select(row => row.PresentRecordId).ToList();
        var names = normalRows.Select(row => row.DisplayName).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count != 2 || ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count || names.Count != 1)
        {
            throw new InvalidDataException("Attendance stable identity validation failed.");
        }

        return scenarios;
    }
}

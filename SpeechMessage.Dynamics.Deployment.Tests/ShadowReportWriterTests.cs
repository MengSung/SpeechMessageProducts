/*
 * 檔案整體說明
 * 目的與定位：保護 Deployment 影子報告的安全發布、比較成功條件與檔案所有權契約。
 * 主要功能與流程：使用合成比較結果、固定 UTC 時鐘與每案獨占的暫存目錄，驗證
 * 三方比較、new 無舊輪、未知輸入拒絕、秘密欄位不回顯及同日重複發布拒絕。
 * 使用邊界與維護：真實檔案系統測試會讀回位元組並獨占開啟，以證明 UTF-8 無 BOM、
 * CRLF 與 handle 已釋放；並行發布只容許一個 owner 成功。測試不建立 SDK 或 LINE，
 * 不代表實機已清理；唯一暫存根由 fixture Dispose 刪除，不接觸其他測試的資料。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SpeechMessage.Dynamics.Deployment.Shadow;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>離線驗證安全摘要；原始差異字串含合成秘密，用來抓出意外序列化。</summary>
public sealed class ShadowReportWriterTests
{
    /// <summary>零差異報告使用固定 UTC 日期，不加 BOM；完成後可獨占讀取且保留 CRLF。</summary>
    [Fact]
    public void Report_CompleteComparison_PublishesDurableSafeSummary()
    {
        using var files = new Files();
        var report = ShadowReportWriter.Write("attendance", "legacy-live", [Case()], files.Root, new Clock());
        Assert.True(report.Passed);
        Assert.Equal(Path.Combine(files.Root, "2026-11-14-attendance.md"), report.Path);
        using var stream = new FileStream(report.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        var bytes = copy.ToArray();
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        var text = new UTF8Encoding(false, true).GetString(bytes);
        Assert.EndsWith("\r\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", text.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("\r", text.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("basis=legacy-live", text, StringComparison.Ordinal);
        Assert.Contains("LegacyExecuted=true", text, StringComparison.Ordinal);
        Assert.Contains("BetweenRounds: Comparable=true; Differences=0", text, StringComparison.Ordinal);
        Assert.Contains("LegacyToExpected: Comparable=true; Differences=0", text, StringComparison.Ordinal);
        Assert.Contains("ShadowToExpected: Comparable=true; Differences=0", text, StringComparison.Ordinal);
        Assert.Contains("runner 成功返回", text, StringComparison.Ordinal);
        Assert.Contains("不代表實機驗收", text, StringComparison.Ordinal);
    }

    /// <summary>只要任一要求的比較不可比或有差異，報告仍可保存但 Passed 必為 false。</summary>
    /// <param name="comparison">注入故障的三方比較位置。</param>
    /// <param name="comparable">是否可比較；true 時注入一個含秘密的差異。</param>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void Report_FailedComparison_NeverLeaksRawDifference(int comparison, bool comparable)
    {
        using var files = new Files();
        var secret = "U0123456789abcdef0123456789abcdef";
        var fault = new ShadowComparison(comparable, comparable
            ? new[] { new ShadowDifference(99, $"fields.{secret}\ntrace-secret", "raw-value-secret") }
            : Array.Empty<ShadowDifference>());
        var parts = new[] { Equal(), Equal(), Equal() };
        parts[comparison] = fault;
        var report = ShadowReportWriter.Write("attendance", "legacy-live",
            [new ShadowCaseResult(1, parts[0], parts[1], parts[2])], files.Root, new Clock());
        Assert.False(report.Passed);
        var text = File.ReadAllText(report.Path);
        Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
        Assert.DoesNotContain("trace-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("raw-value-secret", text, StringComparison.Ordinal);
        Assert.Contains($"Differences={(comparable ? 1 : 0)}", text, StringComparison.Ordinal);
    }

    /// <summary>new 基準只要求新側比較；缺少舊輪不能使舊基準成功，也不能假報舊輪執行。</summary>
    /// <param name="basis">核定基準。</param>
    /// <param name="legacyExecuted">是否確實執行舊輪。</param>
    /// <param name="passed">預期結果。</param>
    [Theory]
    [InlineData("new", false, true)]
    [InlineData("new", true, false)]
    [InlineData("legacy-live", false, false)]
    public void Report_BasisControlsRequiredRounds(string basis, bool legacyExecuted, bool passed)
    {
        using var files = new Files();
        var unavailable = new ShadowComparison(false, Array.Empty<ShadowDifference>());
        var item = new ShadowCaseResult(1, unavailable, unavailable, Equal()) { LegacyExecuted = legacyExecuted };
        var report = ShadowReportWriter.Write("localization", basis, [item], files.Root, new Clock());
        Assert.Equal(passed, report.Passed);
        Assert.Contains($"LegacyExecuted={legacyExecuted.ToString().ToLowerInvariant()}", File.ReadAllText(report.Path), StringComparison.Ordinal);
    }

    /// <summary>拒絕路徑穿越、大小寫變體與非核定基準，且驗證失敗不建立輸出根。</summary>
    /// <param name="module">模組故障輸入。</param>
    /// <param name="basis">基準故障輸入。</param>
    [Theory]
    [InlineData("../attendance", "legacy-live")]
    [InlineData("ATTENDANCE", "legacy-live")]
    [InlineData("unknown", "legacy-live")]
    [InlineData("attendance", "secret-basis")]
    public void Report_InvalidVocabulary_RejectsBeforeCreatingDirectory(string module, string basis)
    {
        using var files = new Files();
        Assert.Throws<InvalidDataException>(() => ShadowReportWriter.Write(module, basis, [Case()], files.Root, new Clock()));
        Assert.False(Directory.Exists(files.Root));
    }

    /// <summary>每個 loader 核定模組均可發布，防止新增 writer 時白名單漏列合法模組。</summary>
    /// <param name="module">05A 與現有 loader 核定的固定代號。</param>
    [Theory]
    [InlineData("diagnostics")]
    [InlineData("localization")]
    [InlineData("weeklyreports")]
    [InlineData("attendance")]
    [InlineData("lessons")]
    [InlineData("appointments")]
    [InlineData("qrcodes")]
    [InlineData("care")]
    [InlineData("fees")]
    [InlineData("messaging")]
    public void Report_AllApprovedModules_Accepted(string module)
    {
        using var files = new Files();
        var report = ShadowReportWriter.Write(module, "legacy-live", [Case()], files.Root, new Clock());
        Assert.True(report.Passed);
        Assert.Equal(Path.Combine(files.Root, "2026-11-14-" + module + ".md"), report.Path);
    }

    /// <summary>空集合或重複情境序號不能被發布成成功報告，避免遺漏與重複案例。</summary>
    [Fact]
    public void Report_EmptyOrDuplicateCases_RejectsBeforePublication()
    {
        using var files = new Files();
        Assert.Throws<InvalidDataException>(() => ShadowReportWriter.Write(
            "attendance", "legacy-live", [], files.Root, new Clock()));
        Assert.Throws<InvalidDataException>(() => ShadowReportWriter.Write(
            "attendance", "legacy-live", [Case(), Case()], files.Root, new Clock()));
        Assert.False(Directory.Exists(files.Root));
    }

    /// <summary>情境與比較契約不完整必須在 I/O 前拒絕，不能把缺比較當成空集合成功。</summary>
    [Fact]
    public void Report_InvalidCaseShape_RejectsWithoutPartialFile()
    {
        using var files = new Files();
        IReadOnlyList<ShadowCaseResult>[] invalid =
        [
            [Case() with { Index = 0 }],
            [Case() with { Index = 7 }],
            [null!],
            [Case() with { BetweenRounds = null! }],
            [Case() with { LegacyToExpected = null! }],
            [Case() with { ShadowToExpected = new ShadowComparison(true, null!) }],
            Enumerable.Range(1, 7).Select(index => Case() with { Index = index }).ToArray()
        ];
        foreach (var cases in invalid)
            Assert.Throws<InvalidDataException>(() => ShadowReportWriter.Write(
                "attendance", "legacy-live", cases, files.Root, new Clock()));
        Assert.False(Directory.Exists(files.Root));
    }

    /// <summary>輸出根被其他檔案占用時必須傳出 I/O 失敗，不能刪除原檔或留住 handle。</summary>
    [Fact]
    public void Report_OutputDirectoryFailure_PreservesExistingFile()
    {
        using var files = new Files();
        Directory.CreateDirectory(files.Root);
        var blocked = Path.Combine(files.Root, "blocked");
        File.WriteAllText(blocked, "existing-owner", new UTF8Encoding(false));
        Assert.Throws<IOException>(() => ShadowReportWriter.Write(
            "attendance", "legacy-live", [Case()], blocked, new Clock()));
        Assert.Equal("existing-owner", File.ReadAllText(blocked));
        Assert.Single(Directory.GetFiles(files.Root));
    }

    /// <summary>同日同模組第二次寫入必須失敗且保留第一份位元組；不同模組可獨立發布。</summary>
    [Fact]
    public void Report_ExistingFile_IsNotOverwrittenOrDeleted()
    {
        using var files = new Files();
        var first = ShadowReportWriter.Write("attendance", "legacy-live", [Case()], files.Root, new Clock());
        var bytes = File.ReadAllBytes(first.Path);
        Assert.Throws<IOException>(() => ShadowReportWriter.Write(
            "attendance", "new", [Case() with { LegacyExecuted = false }], files.Root, new Clock()));
        Assert.Equal(bytes, File.ReadAllBytes(first.Path));
        var second = ShadowReportWriter.Write("care", "legacy-live", [Case()], files.Root, new Clock());
        Assert.NotEqual(first.Path, second.Path);
    }

    /// <summary>併發同路徑只有 CreateNew 的 owner 成功，失敗呼叫不得刪除成功者檔案。</summary>
    [Fact]
    public async Task Report_ConcurrentPublication_OneOwnerAndNoRetainedHandles()
    {
        using var files = new Files();
        var successes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            try
            {
                return ShadowReportWriter.Write("attendance", "legacy-live", [Case()], files.Root, new Clock()).Passed;
            }
            catch (IOException) { return false; }
        })));
        Assert.Single(successes.Where(value => value));
        var path = Assert.Single(Directory.GetFiles(files.Root));
        using var handle = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(handle.Length > 0);
    }

    /// <summary>產生沒有差異的完整比較；不持有外部資源或可變共享集合。</summary>
    private static ShadowComparison Equal() => new(true, Array.Empty<ShadowDifference>());

    /// <summary>合成單一情境結果，三側預設全可比且相同。</summary>
    private static ShadowCaseResult Case() => new(1, Equal(), Equal(), Equal());

    /// <summary>固定 UTC 日期供檔名驗證；不啟動計時器、不借用系統可變時鐘。</summary>
    private sealed class Clock : TimeProvider
    {
        /// <summary>回傳測試指定時間，跨機器執行仍得到相同報告檔名。</summary>
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);
    }

    /// <summary>每案唯一且起初不存在的輸出根；只在測試生命期保留報告。</summary>
    private sealed class Files : IDisposable
    {
        /// <summary>唯一資源所有權邊界；不與其他測試共享路徑。</summary>
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "shadow-report-" + Guid.NewGuid().ToString("N"));

        /// <summary>所有檔案 handle 先由測試 using 釋放，再刪除此 fixture 自建目錄。</summary>
        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}

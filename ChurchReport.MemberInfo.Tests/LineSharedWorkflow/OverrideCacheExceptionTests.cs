/*
 * 檔案整體說明
 *
 * 目的與定位：用真共用檔案 owner 驗覆寫 cache 的功能故障邊界；LINE 明確關閉，不把離線測試宣稱實機傳送。
 *
 * 主要功能與流程：
 * 本檔宣告 OverrideCacheExceptionTests、FailureSource。
 * - 冷失敗由 cache 傳真 Exception，外層再報同實例只能一筆；本文、租戶與個資不得進 Exception.log。
 * - 由 cache 真實終端邊界報同一例外，不在來源另行通知或轉字串。
 * 測試案例入口包含 Cache_failure_uses_shared_owner_and_preserves_original_without_sensitive_output、FailureSource、
 * ReadAsync。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using SpeechMessage.Localization;
using SpeechMessage.Tenancy;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.LineSharedWorkflow;

/// <summary>用真共用檔案 owner 驗覆寫 cache 的功能故障邊界；LINE 明確關閉，不把離線測試宣稱實機傳送。</summary>
public sealed class OverrideCacheExceptionTests
{
    /// <summary>冷失敗由 cache 傳真 Exception，外層再報同實例只能一筆；本文、租戶與個資不得進 Exception.log。</summary>
    [Fact]
    public async Task Cache_failure_uses_shared_owner_and_preserves_original_without_sensitive_output()
    {
        var directory = Path.Combine(Path.GetTempPath(), "p108-diagnostics-" + Guid.NewGuid().ToString("N"));
        var failure = new InvalidOperationException("private-p108-source-token");
        try
        {
            await using (var diagnostics = new ExceptionDiagnostics(directory,
                outputOptions: new ExceptionOutputOptions(writeExceptionLog: true, sendLine: false)))
            {
                await using var cache = new ChurchTextOverrideCache(new FailureSource(failure),
                    new ChurchTextOverrideValidator(new SystemTextTable(), "這是一個長度剛好十二字的教會"),
                    (exception, operation) => diagnostics.Report(exception, operation));
                var context = new TextContext(Guid.NewGuid(), "zh-TW", "zh-TW", "PrivateOrganization", "ChurchReport");
                var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => cache.LoadAsync(context,
                    new DateTime(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc)));
                Assert.Same(failure, actual);
                diagnostics.Report(actual, "OuterHandler");
                Assert.Equal(0, cache.ActiveLoadCount);
            }
            var record = Assert.Single(File.ReadAllLines(Path.Combine(directory, "Exception.log")));
            Assert.Contains("InvalidOperationException", record);
            Assert.Contains("ChurchTextOverrideCache.Load", record);
            Assert.DoesNotContain("private-p108-source-token", record);
            Assert.DoesNotContain("PrivateOrganization", record);
        }
        finally
        {
            // 唯一輸出為本案以 Guid 建立的暫存資料夾，無使用者路徑輸入；owner 已全部排空才刪除。
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>單案來源只傳原始合成故障，不建立任何連線／timer 或背景工作。</summary>
    private sealed class FailureSource(Exception failure) : IChurchTextOverrideSource
    {
        /// <summary>由 cache 真實終端邊界報同一例外，不在來源另行通知或轉字串。</summary>
        public Task<IReadOnlyList<ChurchTextOverride>> ReadAsync(TextContext context, CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyList<ChurchTextOverride>>(failure);
    }
}

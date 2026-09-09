using ChurchReport.Services;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.LineSharedWorkflow;

/// <summary>全域 legacy 綁定測試與其他案例隔離，避免同程序的兩個 owner 互搶收件路徑。</summary>
[CollectionDefinition("ExceptionReporting", DisableParallelization = true)]
public sealed class ExceptionReportingCollection { }

/// <summary>以真實檔案與假 sender 保護舊入口先落檔再發送，以及原始文字不外洩的契約。</summary>
[Collection("ExceptionReporting")]
public sealed class ChurchReportLineAdminNotificationServiceTests
{
    /// <summary>舊二／三參數入口統一 owner；驗證傳入的 error/category 不進 LINE／log。</summary>
    [Fact]
    public async Task Legacy_calls_use_shared_log_before_line_without_raw_error_text()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var messages = new List<string>();
        try
        {
            await using (var diagnostics = new ExceptionDiagnostics(directory))
            {
                using var registration = ExceptionReporting.Attach(diagnostics);
                diagnostics.StartNotifications((message, _) =>
                {
                    Assert.True(File.Exists(Path.Combine(directory, "Exception.log")));
                    messages.Add(message);
                    return Task.CompletedTask;
                });
                ChurchReportLineAdminNotificationService.NotifyDefaultError("Product", "private-A");
                ChurchReportLineAdminNotificationService.NotifyDefaultError("Product", "private-B", "private-C");
            }
            Assert.Equal(2, messages.Count);
            Assert.DoesNotContain("private-", string.Join("", messages));
            Assert.DoesNotContain("private-", File.ReadAllText(Path.Combine(directory, "Exception.log")));
            Assert.False(ExceptionReporting.IsActive);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}

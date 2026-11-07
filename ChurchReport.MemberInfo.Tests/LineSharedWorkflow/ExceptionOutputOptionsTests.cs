/*
 * 檔案整體說明
 *
 * 目的與定位：以真實檔案與假 sender 驗證啟動設定的四種輸出組合，避免停用後仍有 I/O 副作用。
 *
 * 主要功能與流程：
 * 本檔宣告 ExceptionOutputOptionsTests。
 * - 經 JSON 組態解析後輸出僅限所選目的地；雙開 sender 必須看得到相同紀錄且同例外不重複。
 * - 缺省維持兩者開啟；無效布林拒絕啟動，不把錯字解釋為靜默關閉。
 * - 只開 LINE 時 sender 故障與關機剩餘佇列只用固定 stderr，不能意外建立 Exception.log。
 * 測試案例入口包含 Selected_outputs_are_independent_and_both_write_before_sending、
 * Missing_options_default_on_and_invalid_boolean_is_rejected、
 * Line_only_failure_or_undrained_queue_never_creates_log。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using Microsoft.Extensions.Configuration;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.LineSharedWorkflow;

/// <summary>以真實檔案與假 sender 驗證啟動設定的四種輸出組合，避免停用後仍有 I/O 副作用。</summary>
public sealed class ExceptionOutputOptionsTests
{
    /// <summary>
    /// 既有部署可能仍帶有ToolUtility組件限定型別名稱，搬移後須由CLR解析成Hosting唯一型別。
    /// 以原四參數Report ABI實際呼叫停用輸出的owner，保護位置參數及預設值相容；await using
    /// 排空合成owner，不建立檔案或HTTP連線，不把此測試當作LINE實收驗收。
    /// </summary>
    [Fact]
    public async Task Forwarded_legacy_types_preserve_identity_and_report_abi()
    {
        foreach (var type in new[] { typeof(ExceptionDiagnostics), typeof(ExceptionOutputOptions), typeof(ExceptionReporting) })
        {
            var legacy = Type.GetType(type.FullName + ", ToolUtility", throwOnError: true);
            Assert.Same(type, legacy);
            Assert.Equal("SpeechMessage.Hosting", legacy!.Assembly.GetName().Name);
        }
        var report = typeof(ExceptionDiagnostics).GetMethod("Report",
            [typeof(Exception), typeof(string), typeof(CancellationToken), typeof(bool)]);
        Assert.NotNull(report);
        Assert.True(report.GetParameters()[2].IsOptional);
        Assert.True(report.GetParameters()[3].IsOptional);
        await using var owner = new ExceptionDiagnostics(Path.GetTempPath(),
            outputOptions: new ExceptionOutputOptions(false, false));
        Assert.Equal(false, report.Invoke(owner, [new Exception("not-recorded"), "Test.LegacyAbi", CancellationToken.None, true]));
    }

    /// <summary>經 JSON 組態解析後輸出僅限所選目的地；雙開 sender 必須看得到相同紀錄且同例外不重複。</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Selected_outputs_are_independent_and_both_write_before_sending(bool writeLog, bool sendLine)
    {
        using var json = new MemoryStream(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new { ExceptionNotifications = new { WriteExceptionLog = writeLog, SendLine = sendLine } }));
        var configuration = new ConfigurationBuilder().AddJsonStream(json).Build();
        using var configurationOwner = (IDisposable)configuration;
        var options = ExceptionOutputOptions.FromConfiguration(configuration);
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sent = new List<string>();
        try
        {
            await using (var diagnostics = new ExceptionDiagnostics(directory, outputOptions: options))
            {
                diagnostics.StartNotifications((message, _) =>
                {
                    Assert.Equal(writeLog, File.Exists(Path.Combine(directory, "Exception.log")));
                    if (writeLog) Assert.Contains(message, File.ReadAllText(Path.Combine(directory, "Exception.log")));
                    sent.Add(message);
                    return Task.CompletedTask;
                });
                var exception = new InvalidOperationException("private-secret");
                Assert.Equal(writeLog || sendLine, diagnostics.Report(exception, "Test.Output"));
                Assert.False(diagnostics.Report(exception, "Test.Outer"));
            }
            Assert.Equal(writeLog, Directory.Exists(directory));
            Assert.Equal(sendLine ? 1 : 0, sent.Count);
            if (writeLog) Assert.Single(File.ReadAllLines(Path.Combine(directory, "Exception.log")));
            Assert.DoesNotContain("private-secret", string.Join("", sent));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    /// <summary>缺省維持兩者開啟；無效布林拒絕啟動，不把錯字解釋為靜默關閉。</summary>
    [Fact]
    public void Missing_options_default_on_and_invalid_boolean_is_rejected()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        using var configurationOwner = (IDisposable)configuration;
        var options = ExceptionOutputOptions.FromConfiguration(configuration);
        Assert.True(options.WriteExceptionLog);
        Assert.True(options.SendLine);
        configuration["ExceptionNotifications:SendLine"] = "invalid";
        Assert.Throws<InvalidOperationException>(() => ExceptionOutputOptions.FromConfiguration(configuration));
        Assert.True(options.SendLine); // 啟動快照不追蹤後續設定變更。
    }

    /// <summary>只開 LINE 時 sender 故障與關機剩餘佇列只用固定 stderr，不能意外建立 Exception.log。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Line_only_failure_or_undrained_queue_never_creates_log(bool startSender)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sent = 0;
        await using (var diagnostics = new ExceptionDiagnostics(directory,
            outputOptions: new ExceptionOutputOptions(false, true)))
        {
            if (startSender) diagnostics.StartNotifications((_, _) =>
            {
                Interlocked.Increment(ref sent);
                throw new IOException("private-provider-error");
            });
            for (var i = 0; i < 70; i++) diagnostics.Report(new Exception(), "Test.Failure");
        }
        Assert.Equal(startSender, sent > 0);
        Assert.False(Directory.Exists(directory));
    }
}

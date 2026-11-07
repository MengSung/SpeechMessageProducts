/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 ChurchReport 舊公開型別 ChurchReport.Logging.ExceptionLoggerProvider
 * 移入 SpeechMessage.Hosting 後仍保留 CLR 完整名稱及借用 owner 的生命週期。既有
 * ExceptionPipelineTests 已保護等級篩選與敏感 formatter 行為；此檔聚焦移轉契約，
 * 不代表 D365、LINE 或正式主機驗收。
 *
 * 主要功能與流程：
 * 驗證舊 ChurchReport 組件可轉送至 Hosting，並以測試專用暫存目錄建立真實
 * ExceptionDiagnostics。呼叫 provider.Dispose 後仍由唯一 diagnostics owner 接受報告並
 * 寫入固定 operation，證明 provider 不會提前關閉或接管共享 owner。
 *
 * 使用邊界與維護：
 * 本檔不讀取 User Secrets、環境憑證、request、Session 或真實收件人；diagnostics 只寫入
 * 每次測試新建的本機目錄，並在離開作用域前 await DisposeAsync，再由測試 finally 刪除。
 * 決定性斷言為舊 assembly 的轉送結果與 Hosting 型別是同一 Type，且 provider.Dispose
 * 後 owner.Report 仍成功落檔。修改型別身分或 owner 所有權時，須重跑本檔及既有例外回歸。
 */
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ChurchReport.Logging;
using Microsoft.Extensions.Logging;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Logging;

/// <summary>
/// 保護 ChurchReport 舊 LoggerProvider CLR 名稱、共用 owner 借用方式及敏感 formatter 不執行的契約。
/// </summary>
public sealed class ExceptionLoggerProviderCompatibilityTests
{
    /// <summary>
    /// 舊 ChurchReport assembly 必須將相同完整型別名稱轉送至 Hosting，而非保留第二份產品實作。
    /// </summary>
    [Fact]
    public void Legacy_provider_type_resolves_to_Hosting_and_keeps_its_full_name()
    {
        var hostingType = typeof(ExceptionLoggerProvider);
        var churchReportAssembly = Assembly.Load("SpeechMessageProducts.ChurchReport");
        var legacyType = Type.GetType(
            "ChurchReport.Logging.ExceptionLoggerProvider, SpeechMessageProducts.ChurchReport",
            throwOnError: true);

        Assert.Equal("SpeechMessage.Hosting", hostingType.Assembly.GetName().Name);
        Assert.Equal("ChurchReport.Logging.ExceptionLoggerProvider", hostingType.FullName);
        Assert.Same(hostingType, legacyType);
        Assert.True(typeof(ILoggerProvider).IsAssignableFrom(hostingType));
        Assert.NotNull(hostingType.GetConstructor(new[] { typeof(ExceptionDiagnostics) }));
        Assert.Same(hostingType, churchReportAssembly.GetForwardedTypes()
            .Single(type => type.FullName == hostingType.FullName));
    }

    /// <summary>
    /// provider.Dispose 不得釋放由組合根借給 provider 的共享 diagnostics owner。
    /// </summary>
    [Fact]
    public async Task Provider_dispose_does_not_dispose_borrowed_diagnostics_owner()
    {
        var directory = Path.Combine(Path.GetTempPath(), "exception-provider-" + Guid.NewGuid().ToString("N"));
        var logPath = Path.Combine(directory, "Exception.log");

        try
        {
            await using (var diagnostics = new ExceptionDiagnostics(directory,
                outputOptions: new ExceptionOutputOptions(writeExceptionLog: true, sendLine: false)))
            {
                var provider = new ExceptionLoggerProvider(diagnostics);
                provider.Dispose();

                Assert.True(diagnostics.Report(new TimeoutException("owner-remains-open"),
                    "P2_01.ProviderOwnerProbe"));
            }

            var records = File.ReadAllLines(logPath);

            Assert.Single(records);
            Assert.Contains("P2_01.ProviderOwnerProbe", records[0]);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}

/*
 * 檔案整體說明
 *
 * 目的與定位：保護真實 ChurchReport 組合根的啟動順序：共用診斷 owner 建立後驗 ICU，成功才進 Startup／CRM／HTTP。 此為來源組合契約，另以真正 NLS 子程序驗證停止；本案不建立
 * Host、讀憑證或發送 LINE。
 *
 * 主要功能與流程：
 * 本檔宣告 ChurchClockStartupTests。
 * - 移除 ICU／生成語系守門、移到應用執行後或移出原例外 catch 都應失敗，不能只測獨立 helper。
 *   P2-01 改由 Hosting 組合接點註冊；實際 owner／adapter 身分與回復另由 HostingCompositionTests 驗證。
 * 測試案例入口包含 Main_ValidatesIcuInsideSharedFailureBoundary_BeforeRunApplication。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Contracts;

/// <summary>
/// 保護真實 ChurchReport 組合根的啟動順序：共用診斷 owner 建立後驗 ICU，成功才進 Startup／CRM／HTTP。
/// 此為來源組合契約，另以真正 NLS 子程序驗證停止；本案不建立 Host、讀憑證或發送 LINE。
/// </summary>
public sealed class ChurchClockStartupTests
{
    /// <summary>移除 ICU 呼叫、移到應用執行後或移出原例外 catch 都應失敗，不能只測獨立 helper。</summary>
    [Fact(DisplayName = "ChurchReport啟動必須先驗ICU再建立業務Host")]
    public void Main_ValidatesIcuInsideSharedFailureBoundary_BeforeRunApplication()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var remaining = 12; directory is not null && remaining > 0; remaining--, directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln"))) continue;
            var source = File.ReadAllText(Path.Combine(directory.FullName, "SpeechMessageProducts.ChurchReport", "Program.cs"));
            var owner = source.IndexOf("registration = RegisterHostingDiagnostics(builder.Services, diagnostics);", StringComparison.Ordinal);
            var guard = source.IndexOf("StartupGuards.ValidateIcuMode();", StringComparison.Ordinal);
            var policy = source.IndexOf("StartupGuards.ValidateLocalizationConfiguration(", StringComparison.Ordinal);
            var policyRegistration = source.IndexOf("builder.Services.AddSingleton(localizationOptions);", StringComparison.Ordinal);
            var run = source.IndexOf("RunApplication(builder, diagnostics, ref configuration);", StringComparison.Ordinal);
            var failure = source.IndexOf("catch (Exception exception)", StringComparison.Ordinal);
            Assert.True(owner >= 0 && guard > owner && policy > guard &&
                policyRegistration > policy && run > policyRegistration && failure > run);
            Assert.Contains("diagnostics.Report(exception, \"Program.Fatal\")", source, StringComparison.Ordinal);
            Assert.Contains("CleanupHostingResources(registration, diagnostics, sender, configuration, preservePrimaryFailure);", source, StringComparison.Ordinal);
            Assert.Contains("configuration = null;", source, StringComparison.Ordinal);
            return;
        }
        throw new InvalidOperationException("Repository root was not found for the startup source contract.");
    }
}

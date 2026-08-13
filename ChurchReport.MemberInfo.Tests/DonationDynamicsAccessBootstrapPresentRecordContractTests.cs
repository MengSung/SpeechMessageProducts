// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/DonationDynamicsAccessBootstrapPresentRecordContractTests.cs
// 用途：鎖定 P7.4 ORG-CALL-00026 bootstrap 的 Package02 base/sub gate、deployment ProfileAlias 驗證與
//       process-host ownership。測試不建立真實 typed client、Data8、Gateway、CRM 或 Session。
//
// 信任與生命週期：
// 1. 本檔只讀取 current worktree source 和 checked-in appsettings；讀取完成後檔案 handle 立即釋放，
//    不保存 profile、credential、endpoint、token、client 或 process host。
// 2. source contract 防止 disabled state 建立 provider、handler、pool、credential graph 或 outbound I/O；
//    它不啟用 flag、不建立 fixture，也不是 CE、traffic、P7.5/P8 或實機 lifecycle evidence。
// ============================================================================

using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 驗證個人出席紀錄 typed read 的 composition 唯一接受 deployment-owned base/sub gate 和 ProfileAlias。
/// client/host 的 reusable resource ownership 必須留在既有 Generic Host；本測試不注入或 Dispose 任何 resource。
/// </summary>
public sealed class DonationDynamicsAccessBootstrapPresentRecordContractTests
{
    /// <summary>
    /// 保護 gate predicate 的完整 fail-closed 組合。故障注入是缺少實作的 bootstrap source；決定性斷言是
    /// predicate 先檢查 Package02 base gate，再讀專屬 sub-gate，factory 在 predicate false 時立即回傳 null，
    /// 在 BindOptions、ProfileAlias 與 process host 前停止，讓 deployment rollback 不殘留 transport state。
    /// </summary>
    [Fact]
    public void Present_read_bootstrap_requires_base_and_sub_gate_before_profile_or_host_resolution()
    {
        var source = ReadBootstrapSource();
        var predicate = SliceMethod(
            source,
            "public static bool IsPackage02MemberInfoPresentReadEnabled(IConfiguration configuration)");
        var factory = SliceMethod(
            source,
            "public static IMemberInfoPresentRecordReadClient? TryCreatePackage02MemberInfoPresentReadClient(");

        predicate.Should().Contain("IsPackage02ContactProfileOperationsEnabled(configuration)");
        predicate.Should().Contain("DynamicsAccess:Package02MemberInfoPresentReadEnabled");
        factory.Should().Contain("if (!IsPackage02MemberInfoPresentReadEnabled(configuration))");
        factory.Should().Contain("return null;");
        factory.IndexOf("if (!IsPackage02MemberInfoPresentReadEnabled(configuration))", StringComparison.Ordinal)
            .Should().BeLessThan(factory.IndexOf("BindOptions(configuration)", StringComparison.Ordinal));
        factory.Should().Contain("EnsureNonEmptyProductProfile(productOptions, \"Package02 MemberInfo present read operations\")");
    }

    /// <summary>
    /// 保護 checked-in deployment settings 在 false state。故障注入是任一設定漏列或錯誤地改為 true；決定性斷言
    /// 是 production/development 都有 false gate，故本機候選不會因單純存在設定而建立 typed client、host、
    /// connection pool、credential graph 或 CE request。
    /// </summary>
    [Fact]
    public void Present_read_checked_in_settings_remain_disabled_by_default()
    {
        foreach (var fileName in new[] { "appsettings.json", "appsettings.Development.json" })
        {
            ReadChurchReportSource(fileName).Should().Contain("\"Package02MemberInfoPresentReadEnabled\": false");
        }
    }

    /// <summary>
    /// 擷取唯一 method body，避免 bootstrap 中其他 Package02/Package03 factory 的字串誤滿足 present-read contract。
    /// helper 只在單一 test invocation 保留 source 字串，沒有 static/cache/session/resource retention。
    /// </summary>
    private static string SliceMethod(string source, string marker)
    {
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterOrEqualTo(0);
        var bodyStart = source.IndexOf('{', start);
        bodyStart.Should().BeGreaterThan(start);
        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source[start..(index + 1)];
            }
        }

        throw new InvalidOperationException("Expected bootstrap method body was incomplete.");
    }

    /// <summary>讀取 bootstrap source；找不到 worktree root 時 fail closed，禁止掃描其他 checkout。</summary>
    private static string ReadBootstrapSource()
        => ReadChurchReportSource("Services", "DonationDynamicsAccessBootstrap.cs");

    /// <summary>
    /// 從目前 solution root 建立 ChurchReport source path。此 helper 不接受外部路徑、ProfileAlias 或秘密，
    /// File.ReadAllText 完成後釋放 handle，不延長 repository 或 deployment resource lifetime。
    /// </summary>
    private static string ReadChurchReportSource(string directoryOrFileName, string? fileName = null)
    {
        var applicationRoot = Path.Combine(FindRepositoryRoot(), "SpeechMessageProducts.ChurchReport");
        var path = fileName is null
            ? Path.Combine(applicationRoot, directoryOrFileName)
            : Path.Combine(applicationRoot, directoryOrFileName, fileName);
        return File.ReadAllText(path);
    }

    /// <summary>只接受目前同時含 solution 與 ChurchReport project 的 worktree root，避免 source evidence 跨樹混用。</summary>
    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")) &&
                Directory.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.ChurchReport")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("ChurchReport solution root was not found.");
    }
}

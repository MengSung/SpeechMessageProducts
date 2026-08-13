// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/Controllers/MemberInfoControllerPackage03CommitmentMetadataContractTests.cs
// 用途：在 P7.4 實作前先鎖定 MemberInfo 承諾類型 metadata 的獨立 Package03 gate、typed-only dispatch、
//       request cancellation 與 legacy false-gate coexistence source contract。
//
// 信任與生命週期：
// 1. 測試只在目前 worktree 讀取短生命週期 source 字串；不啟動 MVC、CRM、Data8、Gateway、Session、cache 或背景資源。
// 2. 它驗證 true branch 不得自動回落 legacy metadata；既有 legacy helper 只可位於 false-gate compatibility branch。
// 3. 此為本機 TDD regression guard，不構成 CE、traffic、parity、P7.5 或 P8 evidence。
// ============================================================================

using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Controllers;

/// <summary>
/// 保護 Package03 承諾 metadata consumer 的 controller 邊界。此測試刻意先於 production service 建立，
/// 以可執行契約要求獨立 sub-gate、固定 request-local typed snapshot 與取消傳遞，避免實作被既有 global
/// metadata cache 或任意 caller 路由需求牽引。測試本身不保存 profile、DTO、例外或檔案 handle。
/// </summary>
public sealed class MemberInfoControllerPackage03CommitmentMetadataContractTests
{
    /// <summary>
    /// 保護 metadata consumer 必須有獨立於圖片 route 的 base/sub-gate 與 checked-in false rollback state。
    /// 故障注入是只存在通用 Package03 gate 或設定缺少獨立 key；決定性斷言是 metadata helper 同時檢查
    /// base gate，兩份設定均保持 false，從而不會因圖片或其他 Package03 capability 設定而自動切流。
    /// </summary>
    [Fact]
    public void Commitment_metadata_requires_an_independent_package03_base_and_sub_gate()
    {
        var bootstrap = ReadSource("Services", "DonationDynamicsAccessBootstrap.cs");

        bootstrap.Should().Contain(
            "IsPackage03MemberInfoCommitmentMetadataReadEnabled(IConfiguration configuration)");
        bootstrap.Should().Contain("IsPackage03SpecialResourcesEnabled(configuration)");
        bootstrap.Should().Contain(
            "DynamicsAccess:Package03MemberInfoCommitmentMetadataReadEnabled");
        bootstrap.Should().Contain(
            "TryCreatePackage03MemberInfoCommitmentMetadataReadClient(");

        foreach (var fileName in new[] { "appsettings.json", "appsettings.Development.json" })
        {
            ReadSource(fileName).Should().Contain(
                "\"Package03MemberInfoCommitmentMetadataReadEnabled\": false");
        }
    }

    /// <summary>
    /// 保護三個實際 metadata consumer 在部署 gate 後共用 request-local typed snapshot。故障注入是 action
    /// 仍只具有同步 signature 或直接重新呼叫 legacy metadata provider；決定性斷言是每個 action 都將
    /// <see cref="Microsoft.AspNetCore.Http.HttpContext.RequestAborted"/> 送入同一 metadata coordinator，
    /// 且一般錯誤處理不吞掉取消。
    /// </summary>
    [Fact]
    public void Commitment_metadata_consumers_use_a_request_local_typed_snapshot_and_preserve_cancellation()
    {
        var source = ReadControllerSource();
        var search = SliceMethod(source, "public async Task<IActionResult> SearchDistrictTree(string search)");
        var group = SliceMethod(source, "public async Task<IActionResult> LoadGroupMembers(string listId, string search)");
        var ungrouped = SliceMethod(
            source,
            "public async Task<IActionResult> LoadUngroupedMembers(DataSourceLoadOptions loadOptions, string search)");

        foreach (var action in new[] { search, group, ungrouped })
        {
            action.Should().Contain("IConfiguration");
            action.Should().Contain(
                "DonationDynamicsAccessBootstrap.IsPackage03MemberInfoCommitmentMetadataReadEnabled(configuration)");
            action.Should().Contain("LoadCommitmentTypeOptionsAsync(");
            action.Should().Contain("HttpContext.RequestAborted");
            action.Should().Contain("catch (Exception ex) when (ex is not OperationCanceledException)");
        }

        var coordinator = SliceMethod(
            source,
            "private async Task<IReadOnlyList<MemberInfoCommitmentTypeOption>?> LoadCommitmentTypeOptionsAsync(");
        coordinator.Should().Contain(
            "DonationDynamicsAccessBootstrap.TryCreatePackage03MemberInfoCommitmentMetadataReadClient(configuration)");
        coordinator.Should().Contain("new Package03MemberInfoCommitmentMetadataReadService(");
        coordinator.Should().Contain("await metadataService.RetrieveAsync(cancellationToken)");
        coordinator.Should().NotContain("GetCommitmentTypeOptions(");
        coordinator.Should().NotContain("catch");
        coordinator.Should().NotContain("retry");
    }

    /// <summary>
    /// 保護 Package03 啟用路徑取得「結案」選項值的 fail-closed 邊界。測試先從三個 metadata consumer
    /// action 驗證同一份 request-local typed snapshot 被傳入 closed-status resolver，再檢查 resolver 的
    /// typed 分支只在該 immutable DTO 中作精確標籤比對，完全不觸及 legacy OptionSet service。故障注入是
    /// 缺少或重複「結案」標籤；實作必須讓其拋出而非改查 legacy metadata，決定性斷言是 typed branch 的
    /// 查找與 legacy lookup 在不同且不可重疊的分支。這可避免不同 profile/generation 的 metadata 在同一
    /// 回應混用，且不保存 snapshot、CRM service 或任何使用者狀態。
    /// </summary>
    [Fact]
    public void Commitment_metadata_typed_snapshot_resolves_closed_status_without_a_legacy_lookup()
    {
        var source = ReadControllerSource();
        var search = SliceMethod(source, "public async Task<IActionResult> SearchDistrictTree(string search)");
        var group = SliceMethod(source, "public async Task<IActionResult> LoadGroupMembers(string listId, string search)");
        var ungrouped = SliceMethod(
            source,
            "public async Task<IActionResult> LoadUngroupedMembers(DataSourceLoadOptions loadOptions, string search)");

        foreach (var action in new[] { search, group, ungrouped })
        {
            action.Should().Contain("GetRequiredClosedCustomerTypeValue(service, typedCommitmentOptions)");
        }

        var resolver = SliceMethod(source, "private int GetRequiredClosedCustomerTypeValue(");
        var legacyLookupStart = resolver.IndexOf("return GetSharedOptionSetService(service)", StringComparison.Ordinal);
        legacyLookupStart.Should().BeGreaterThan(0);
        var typedBranch = resolver[..legacyLookupStart];

        typedBranch.Should().Contain("IReadOnlyList<MemberInfoCommitmentTypeOption>? typedCommitmentOptions");
        typedBranch.Should().Contain("option.Label.Equals(\"結案\", StringComparison.Ordinal)");
        typedBranch.Should().Contain(".Single(");
        typedBranch.Should().NotContain("GetSharedOptionSetService");
    }

    /// <summary>
    /// 保護 true branch 不使用 legacy option text lookup 補救未知 typed metadata。故障注入是 row mapper
    /// 永遠呼叫 <c>ResolveOptionSetText</c>；決定性斷言是 mapper 接受可選 typed snapshot，只有 false
    /// compatibility branch 可保留 legacy fallback，避免同一 request 混用兩個 profile/generation 的 metadata。
    /// </summary>
    [Fact]
    public void Commitment_metadata_row_projection_does_not_fallback_to_legacy_when_the_typed_snapshot_is_present()
    {
        var mapper = SliceMethod(
            ReadControllerSource(),
            "private List<GroupMemberRowViewModel> BuildMemberRows(");

        mapper.Should().Contain("IReadOnlyList<MemberInfoCommitmentTypeOption>? typedCommitmentOptions");
        mapper.Should().Contain("typedCommitmentOptions ?? GetCommitmentTypeOptions(service)");
        mapper.Should().Contain("typedCommitmentOptions is null");
        mapper.Should().Contain("ResolveOptionSetText(optionService, contact, \"customertypecode\")");
    }

    /// <summary>
    /// 擷取單一完整 method，避免同檔其他 legacy helper 的字串誤通過 typed contract。此 helper 只存活於目前
    /// test call stack，沒有 static cache、檔案 watcher、Session、profile 或可釋放資源。
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

        throw new InvalidOperationException("Expected MemberInfo method body was incomplete.");
    }

    /// <summary>讀取目前 worktree 的 controller source；找不到可信 solution root 時 fail closed。</summary>
    private static string ReadControllerSource() => ReadSource("Controllers", "MemberInfoController.cs");

    /// <summary>
    /// 以 solution root 組成唯一 ChurchReport source path。File API 在讀取後立刻釋放 handle，
    /// 不接受環境變數、browser 或測試 caller 指定 repository/profile 路徑。
    /// </summary>
    private static string ReadSource(string directoryOrFileName, string? fileName = null)
    {
        var applicationRoot = Path.Combine(FindRepositoryRoot(), "SpeechMessageProducts.ChurchReport");
        var path = fileName is null
            ? Path.Combine(applicationRoot, directoryOrFileName)
            : Path.Combine(applicationRoot, directoryOrFileName, fileName);
        return File.ReadAllText(path);
    }

    /// <summary>只接受同時含 solution 與 ChurchReport 專案的 worktree root，避免跨 checkout source 混用。</summary>
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

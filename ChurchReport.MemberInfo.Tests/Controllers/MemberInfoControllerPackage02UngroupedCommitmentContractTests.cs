// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/Controllers/MemberInfoControllerPackage02UngroupedCommitmentContractTests.cs
// 用途：鎖定 P7.4 ORG-CALL-00024 在 MemberInfo 未分組頁面的 disabled gate、唯一 typed count dispatch、
//       cancellation 與 legacy capability coexistence 邊界。
//
// 信任與生命週期：
// 1. 測試只讀一次 UTF-8 source，結束後 File API 自動釋放 handle；不啟動 MVC、CRM、Gateway、Data8 或 Session。
// 2. 它區分「typed count fault 不可 fallback」與「empty count/metadata/paging 是別的 capability」，避免 source
//    contract 因字串出現在同檔而錯把合法 coexistence 當作不得存在的 legacy code。
// 3. 這是 local-only regression guard，不是 CE、traffic、P7.5 removal 或 P8 evidence。
// ============================================================================

using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Controllers;

/// <summary>
/// 驗證 `LoadUngroupedMembers` 對非空 commitment aggregate count 的唯一 typed boundary。它不測試或授權
/// 同一頁面其餘 legacy matrix row；那些 row 必須在自己的 child 具有 DTO、authorization、parity 與 rollback
/// evidence 後才可遷移。
/// </summary>
public sealed class MemberInfoControllerPackage02UngroupedCommitmentContractTests
{
    /// <summary>
    /// 保護 child gate 在使用者 Session／scope 與 typed composition 前先決定是否嘗試 typed count。決定性
    /// 斷言是新 sub-gate 依賴 Package02 base gate，checked-in settings 均為 false，controller async action
    /// 先取得 deployment configuration 與 gate，才呼叫 EnsureCorrectUserData 或建立 client。
    /// </summary>
    [Fact]
    public void Ungrouped_commitment_route_keeps_typed_sub_gate_disabled_and_before_user_or_client_work()
    {
        var controller = ReadControllerSource();
        var bootstrap = ReadSource("Services", "DonationDynamicsAccessBootstrap.cs");
        var action = SliceMethod(
            controller,
            "public async Task<IActionResult> LoadUngroupedMembers(DataSourceLoadOptions loadOptions, string search)");

        bootstrap.Should().Contain("IsPackage02UngroupedCommitmentReadEnabled(IConfiguration configuration)");
        bootstrap.Should().Contain("IsPackage02ContactProfileOperationsEnabled(configuration)");
        action.Should().Contain("DonationDynamicsAccessBootstrap.IsPackage02UngroupedCommitmentReadEnabled(configuration)");
        action.Should().Contain("EnsureCorrectUserData();");

        var gate = action.IndexOf("DonationDynamicsAccessBootstrap.IsPackage02UngroupedCommitmentReadEnabled(configuration)", StringComparison.Ordinal);
        var ensure = action.IndexOf("EnsureCorrectUserData();", StringComparison.Ordinal);

        gate.Should().BeGreaterOrEqualTo(0);
        ensure.Should().BeGreaterThan(gate);

        foreach (var fileName in new[] { "appsettings.json", "appsettings.Development.json" })
        {
            ReadSource(fileName).Should().Contain("\"Package02UngroupedCommitmentReadEnabled\": false");
        }
    }

    /// <summary>
    /// 保護 enabled path 對 ORG-CALL-00024 只使用 fixed-profile typed service 與 RequestAborted。故障注入
    /// 是 source 中仍存在合法 legacy empty count/metadata/page methods；決定性斷言是 typed branch 自身沒有
    /// catch、retry 或 `CountUngroupedCommitmentValues`，因此 typed fault 不會偷走舊 aggregate 當 fallback。
    /// </summary>
    [Fact]
    public void Ungrouped_commitment_typed_branch_has_no_legacy_aggregate_fallback_or_retry()
    {
        var controller = ReadControllerSource();
        var method = SliceMethod(
            controller,
            "private async Task<IReadOnlyDictionary<int, int>> LoadUngroupedCommitmentCountsAsync(");

        method.Should().Contain("DonationDynamicsAccessBootstrap.TryCreatePackage02UngroupedCommitmentReadClient(configuration)");
        method.Should().Contain("new Package02UngroupedCommitmentReadService(");
        method.Should().Contain("DonationDynamicsAccessBootstrap.BindOptions(configuration).ProfileAlias");
        method.Should().Contain("await countService.RetrieveAsync(search, cancellationToken)");
        method.Should().Contain("if (!useTypedUngroupedCommitmentCount)");
        method.Should().Contain("return CountUngroupedCommitmentValues(");

        var client = method.IndexOf("DonationDynamicsAccessBootstrap.TryCreatePackage02UngroupedCommitmentReadClient(configuration)", StringComparison.Ordinal);
        var typedBranch = method[client..];
        typedBranch.Should().NotContain("CountUngroupedCommitmentValues(");
        typedBranch.Should().NotContain("catch");
        typedBranch.Should().NotContain("retry");
        typedBranch.Should().NotContain("GetConnection(");
        typedBranch.Should().NotContain("IOrganizationService");
    }

    /// <summary>
    /// 保護 action 將 HTTP cancellation 原樣一路帶到 typed count，而一般錯誤 handler 不吞掉取消。決定性
    /// 斷言是 page loader 接收 `HttpContext.RequestAborted`，且 controller catch filter 排除
    /// `OperationCanceledException`；這使 executor/lease 的既有 cleanup owner 可以處理取消後的 transport。
    /// </summary>
    [Fact]
    public void Ungrouped_commitment_action_forwards_request_cancellation_without_turning_it_into_legacy_fallback()
    {
        var action = SliceMethod(
            ReadControllerSource(),
            "public async Task<IActionResult> LoadUngroupedMembers(DataSourceLoadOptions loadOptions, string search)");

        action.Should().Contain("HttpContext.RequestAborted");
        action.Should().Contain("catch (Exception ex) when (ex is not OperationCanceledException)");
        action.Should().NotContain("catch (OperationCanceledException)");
    }

    /// <summary>
    /// 保護啟用 typed non-empty count 時，頁面的 legacy empty count 與 segment retrieve 不可重用最多三分鐘的
    /// grouped-contact cache。故障注入是既有全域 church cache 仍存在；決定性斷言是 typed branch 要求新的
    /// request-local grouping snapshot，避免可快取的舊 membership 與 Data8 即時 aggregate 造成同頁 total/page
    /// segment 不一致。此檢查不把 snapshot 當成 browser input，也不新增 cache、retry 或 CE 操作。
    /// </summary>
    [Fact]
    public void Ungrouped_commitment_typed_branch_bypasses_the_legacy_grouped_contact_cache()
    {
        var controller = ReadControllerSource();
        var action = SliceMethod(
            controller,
            "public async Task<IActionResult> LoadUngroupedMembers(DataSourceLoadOptions loadOptions, string search)");
        var groupedIds = SliceMethod(
            controller,
            "private HashSet<Guid> GetChurchGroupedCurrentIds(");

        action.Should().Contain("bypassCache: useTypedUngroupedCommitmentCount && usesCommitmentSort");
        groupedIds.Should().Contain("bool bypassCache");
        groupedIds.Should().Contain("if (!bypassCache &&");
        groupedIds.Should().Contain("if (!bypassCache && memoryCache != null)");
    }

    /// <summary>
    /// 保護公開 MVC action 的文件描述路由、server-derived authorization、gate、cancellation、typed fault 與
    /// legacy connection 的唯一 owner。故障注入是只有 action 內部一般註解、卻缺少 API boundary XML 文件；
    /// 決定性斷言是 method attributes 前存在具體繁中契約，讓後續維護者不會把 local-only candidate 誤當切流。
    /// </summary>
    [Fact]
    public void Ungrouped_commitment_public_action_has_an_api_boundary_documentation_contract()
    {
        var source = ReadControllerSource();
        var action = source.IndexOf(
            "public async Task<IActionResult> LoadUngroupedMembers(DataSourceLoadOptions loadOptions, string search)",
            StringComparison.Ordinal);
        action.Should().BeGreaterThanOrEqualTo(0);
        var preceding = source[Math.Max(0, action - 2400)..action];

        preceding.Should().Contain("/// <summary>");
        preceding.Should().Contain("未分組會員");
        preceding.Should().Contain("取消");
        preceding.Should().Contain("legacy");
    }

    /// <summary>
    /// 擷取單一 method 的完整 brace 範圍，避免同檔其他 legacy action/method 的字串誤滿足 contract。這個
    /// helper 只使用 method-local indices 與 string，不持有檔案、Session、cache 或 mutable shared state。
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

    /// <summary>讀取唯一 controller source；發現不到 current worktree root 時 fail closed，不接受環境路徑覆寫。</summary>
    private static string ReadControllerSource()
        => ReadSource("Controllers", "MemberInfoController.cs");

    /// <summary>
    /// 由 solution root 組成 ChurchReport source path；最後參數允許直接讀 checked-in appsettings，仍不接受
    /// 外部路徑、profile 或機密。File API 在讀取完成後釋放 handle，因此測試不延長 repository resource lifetime。
    /// </summary>
    private static string ReadSource(string directoryOrFileName, string? fileName = null)
    {
        var applicationRoot = Path.Combine(FindRepositoryRoot(), "SpeechMessageProducts.ChurchReport");
        var path = fileName is null
            ? Path.Combine(applicationRoot, directoryOrFileName)
            : Path.Combine(applicationRoot, directoryOrFileName, fileName);
        return File.ReadAllText(path);
    }

    /// <summary>只接受同時含 solution 與 ChurchReport project 的目前 worktree root，避免跨 checkout source 讀取。</summary>
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

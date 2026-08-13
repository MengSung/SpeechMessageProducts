// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/Controllers/MemberInfoControllerPresentRecordContractTests.cs
// 用途：在不啟動 MVC、CRM、Gateway 或 Session 的情況下，保護 P7.4 ORG-CALL-00026 個人出席紀錄
//       typed read 的 deployment gate、server authorization、取消與 legacy compatibility boundary。
//
// 信任與生命週期：
// 1. 所有測試只讀取目前 worktree 的 UTF-8 source；File.ReadAllText 完成後立即釋放 handle，不保存
//    HttpContext、profile、client、token、Entity、credential、cache 或任何 transport resource。
// 2. 測試只驗證 local-only source contract；它不會建立 fixture、送出 CE request、切換 feature gate、
//    發起 traffic cutover，亦不是 P7.5/P8 或實機 rollback evidence。
// 3. 每個 assertion 將 typed branch 與同檔合法 legacy branch 分開檢查，避免字串出現在其他 action 時
//    使未授權的 fallback、取消吞沒或 SDK bridge 被錯誤放行。
// ============================================================================

using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Controllers;

/// <summary>
/// 驗證 MemberInfo 個人出席紀錄在 future gate=true 時只可使用 server-owned、DTO-only ProductClient path，
/// gate=false 時則完整保留既有 ToolUtility compatibility path。這些測試保護 routing authority 和 request
/// cancellation，而非測試或授權任何 CE 資料操作。
/// </summary>
public sealed class MemberInfoControllerPresentRecordContractTests
{
    /// <summary>
    /// 保護 present-record sub-gate 必須同時依賴 Package02 base gate，並在 Session hydration、browser locator
    /// parse、typed client/host/pool 或 outbound I/O 前決定。故障注入是目前不存在的 sub-gate；決定性斷言是
    /// controller 先讀 deployment configuration/gate，false branch 立即進入具名 legacy helper，兩份 checked-in
    /// settings 都保留 false，讓 rollback 不必接觸 request 或 transport 狀態。
    /// </summary>
    [Fact]
    public void Present_record_route_requires_a_disabled_base_and_sub_gate_before_user_or_typed_client_work()
    {
        var controller = ReadControllerSource();
        var bootstrap = ReadSource("Services", "DonationDynamicsAccessBootstrap.cs");
        var action = SliceMethod(
            controller,
            "public async Task<object> LoadContactPresentRecords(string contactId, DataSourceLoadOptions loadOptions)");

        bootstrap.Should().Contain("IsPackage02MemberInfoPresentReadEnabled(IConfiguration configuration)");
        bootstrap.Should().Contain("IsPackage02ContactProfileOperationsEnabled(configuration)");
        action.Should().Contain("DonationDynamicsAccessBootstrap.IsPackage02MemberInfoPresentReadEnabled(configuration)");
        action.Should().Contain("LoadContactPresentRecordsLegacy(contactId, loadOptions)");

        var gate = action.IndexOf(
            "DonationDynamicsAccessBootstrap.IsPackage02MemberInfoPresentReadEnabled(configuration)",
            StringComparison.Ordinal);
        gate.Should().BeGreaterOrEqualTo(0);

        var typedMethod = SliceMethod(
            controller,
            "private async Task<object> LoadContactPresentRecordsTypedAsync(");
        typedMethod.Should().Contain("EnsureCorrectUserData();");

        foreach (var fileName in new[] { "appsettings.json", "appsettings.Development.json" })
        {
            ReadSource(fileName).Should().Contain("\"Package02MemberInfoPresentReadEnabled\": false");
        }
    }

    /// <summary>
    /// 保護 enabled branch 在任何 typed dispatch 前完成既有 user/session 與 object authorization。故障注入是目前
    /// legacy action 直接持有 CRM SDK；決定性斷言是 true branch 只呼叫獨立 present-read factory/service、固定
    /// deployment profile 和 RequestAborted，沒有 ToolUtility、QueryExpression、GetConnection、retry 或 catch，
    /// 故 typed fault 不會偷偷落回 legacy CRM 或發佈 partial SDK data。
    /// </summary>
    [Fact]
    public void Present_record_typed_branch_authorizes_before_dispatch_and_has_no_sdk_fallback_or_retry()
    {
        var controller = ReadControllerSource();
        var method = SliceMethod(
            controller,
            "private async Task<object> LoadContactPresentRecordsTypedAsync(");

        method.Should().Contain("EnsureCorrectUserData();");
        method.Should().Contain("Guid.TryParse(contactId, out var contactGuid)");
        method.Should().Contain("CanViewContact(contactGuid)");
        method.Should().Contain("DonationDynamicsAccessBootstrap.TryCreatePackage02MemberInfoPresentReadClient(configuration)");
        method.Should().Contain("new Package02MemberInfoPresentRecordReadService(");
        method.Should().Contain("DonationDynamicsAccessBootstrap.BindOptions(configuration).ProfileAlias");
        method.Should().Contain("HttpContext.RequestAborted");

        var authorize = method.IndexOf("CanViewContact(contactGuid)", StringComparison.Ordinal);
        var dispatch = method.IndexOf(
            "DonationDynamicsAccessBootstrap.TryCreatePackage02MemberInfoPresentReadClient(configuration)",
            StringComparison.Ordinal);
        authorize.Should().BeGreaterOrEqualTo(0);
        dispatch.Should().BeGreaterThan(authorize);

        method.Should().NotContain("ToolUtility");
        method.Should().NotContain("QueryExpression");
        method.Should().NotContain("GetConnection(");
        method.Should().NotContain("IOrganizationService");
        method.Should().NotContain("catch");
        method.Should().NotContain("retry");
    }

    /// <summary>
    /// 保護公開 MVC action 對取消不建立一般錯誤 response。故障注入是 action catch-all 將任何例外交給
    /// <c>HandleError</c> 的既有模式；決定性斷言是公開 action 以 exception filter 排除所有
    /// <see cref="OperationCanceledException"/>，讓 ASP.NET Core 與下游 process-host/lease owner 繼續完成
    /// request-local cancellation cleanup，而不會留下跨 request 的 response、exception 或 transport state。
    /// </summary>
    [Fact]
    public void Present_record_public_action_preserves_cancellation_and_documents_the_boundary()
    {
        var source = ReadControllerSource();
        var action = SliceMethod(
            source,
            "public async Task<object> LoadContactPresentRecords(string contactId, DataSourceLoadOptions loadOptions)");
        var actionIndex = source.IndexOf(
            "public async Task<object> LoadContactPresentRecords(string contactId, DataSourceLoadOptions loadOptions)",
            StringComparison.Ordinal);
        var preceding = source[Math.Max(0, actionIndex - 2600)..actionIndex];

        action.Should().Contain("catch (Exception ex) when (ex is not OperationCanceledException)");
        action.Should().NotContain("catch (OperationCanceledException)");
        preceding.Should().Contain("/// <summary>");
        preceding.Should().Contain("個人出席紀錄");
        preceding.Should().Contain("取消");
        preceding.Should().Contain("legacy");
    }

    /// <summary>
    /// 擷取單一 method 的完整 brace 範圍，避免同檔其他 legacy action/method 的字串誤滿足 contract。此 helper
    /// 只保存本次測試字串，不保存檔案 handle、Session、cache、profile 或 mutable shared state。
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

    /// <summary>讀取唯一 controller source；找不到目前 worktree root 時 fail closed，禁止跨 checkout 掃描 source。</summary>
    private static string ReadControllerSource()
        => ReadSource("Controllers", "MemberInfoController.cs");

    /// <summary>
    /// 從目前 solution root 建立 ChurchReport source path。path 由 test assembly location 推導，沒有讀取外部輸入、
    /// profile、endpoint 或秘密；File API 在讀取完成後釋放 handle，因此不延長 repository resource lifetime。
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

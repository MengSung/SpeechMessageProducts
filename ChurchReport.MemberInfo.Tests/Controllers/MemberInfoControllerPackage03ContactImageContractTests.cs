// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/Controllers/MemberInfoControllerPackage03ContactImageContractTests.cs
// 用途：以 source contract 固定 P7.4 Package03 圖片新路由的 false-gate、雙層伺服器授權、DTO-only
//       dispatch、取消傳遞與 legacy route 隔離。
//
// 此測試不啟動 MVC/CRM/Connector。它讀取一次 UTF-8 source 並在方法結束後釋放檔案 handle，避免測試自己
// 保留 Session、圖片 bytes、cache 或外部資源。它不是 CE、cutover、P7.5 或 P8 證據。
// ============================================================================

using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Controllers;

/// <summary>
/// 鎖定獨立 Package03 image route 的安全邊界。由於既有精確目標授權 `CanViewContact(Guid)` 需要已解析
/// locator，本測試要求先驗證 server-side MemberInfo scope，之後才 parse，再進行精確 target authorization。
/// </summary>
public sealed class MemberInfoControllerPackage03ContactImageContractTests
{
    /// <summary>
    /// 保護 gate=false 是第一個 executable decision。決定性斷言是 gate 在 session scope、GUID parse、
    /// target authorization 和 typed client composition 前停止，且既有 legacy GetContactImage 仍存在。
    /// </summary>
    [Fact]
    public void Package03_image_route_short_circuits_before_scope_locator_or_client_when_disabled()
    {
        var source = ReadSource();
        var action = ReadAction(source);

        source.Should().Contain("public IActionResult GetContactImage(string contactId, int size = 80, bool fit = false)");
        action.Should().Contain("DonationDynamicsAccessBootstrap.IsPackage03SpecialResourcesEnabled(configuration)");
        action.Should().Contain("return NotFound();");
        action.Should().Contain("EnsureCorrectUserData();");
        action.Should().Contain("var access = GetAccess();");
        action.Should().Contain("Guid.TryParse(contactId, out var contactGuid)");
        action.Should().Contain("CanViewContact(contactGuid)");
        action.Should().Contain("DonationDynamicsAccessBootstrap.TryCreatePackage03SpecialResourceClient(configuration)");

        var gate = action.IndexOf("DonationDynamicsAccessBootstrap.IsPackage03SpecialResourcesEnabled(configuration)", StringComparison.Ordinal);
        var ensure = action.IndexOf("EnsureCorrectUserData();", StringComparison.Ordinal);
        var scope = action.IndexOf("var access = GetAccess();", StringComparison.Ordinal);
        var parse = action.IndexOf("Guid.TryParse(contactId, out var contactGuid)", StringComparison.Ordinal);
        var targetAuthorization = action.IndexOf("CanViewContact(contactGuid)", StringComparison.Ordinal);
        var client = action.IndexOf("DonationDynamicsAccessBootstrap.TryCreatePackage03SpecialResourceClient(configuration)", StringComparison.Ordinal);

        gate.Should().BeGreaterOrEqualTo(0);
        ensure.Should().BeGreaterThan(gate);
        scope.Should().BeGreaterThan(ensure);
        parse.Should().BeGreaterThan(scope);
        targetAuthorization.Should().BeGreaterThan(parse);
        client.Should().BeGreaterThan(targetAuthorization);
    }

    /// <summary>
    /// 保護新 route 僅走 typed image DTO，既不重用 legacy cache/CRM/redirect/avatar，也不把 typed fault 回退
    /// 至舊路徑。取消注入時必須保留原例外，避免把已中止 request 轉成普通 404 或建立第二次 I/O。
    /// </summary>
    [Fact]
    public void Package03_image_route_is_dto_only_no_cache_no_fallback_and_cancellation_safe()
    {
        var action = ReadAction(ReadSource());

        action.Should().Contain("await service.RetrieveAsync(contactGuid, HttpContext.RequestAborted)");
        action.Should().Contain("catch (Exception ex) when (ex is not OperationCanceledException)");
        action.Should().NotContain("catch (OperationCanceledException)");
        action.Should().NotContain("GetConnection(");
        action.Should().NotContain("IOrganizationService");
        action.Should().NotContain("IMemoryCache");
        action.Should().NotContain("GetDefaultImage(");
        action.Should().NotContain("Redirect(");
        action.Should().NotContain("Retrieve(");
        action.Should().NotContain("ToolUtility");
        action.Should().NotContain("GetContactImage(");
    }

    /// <summary>
    /// 保護 checked-in base/development 設定都維持獨立 Package03 gate=false。這是 rollback 的唯一正常
    /// 設定狀態；本 child 不得藉由改動 Package01/Package02 gate 偷偷啟用圖片流量。
    /// </summary>
    [Fact]
    public void Checked_in_configuration_keeps_package03_contact_image_gate_disabled()
    {
        foreach (var fileName in new[] { "appsettings.json", "appsettings.Development.json" })
        {
            var configuration = File.ReadAllText(Path.Combine(
                FindRepositoryRoot(),
                "SpeechMessageProducts.ChurchReport",
                fileName));

            configuration.Should().Contain("\"Package03SpecialResourcesEnabled\": false");
        }
    }

    /// <summary>
    /// 擷取單一 action 完整括號範圍，以免對同檔 legacy action 的字串誤判。字串只在目前測試期間持有，
    /// 不進 static cache 或跨測試集合。
    /// </summary>
    private static string ReadAction(string source)
    {
        const string marker = "public async Task<IActionResult> Package03ContactImage(string contactId)";
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterOrEqualTo(0);
        var bodyStart = source.IndexOf('{', start);
        bodyStart.Should().BeGreaterThan(start);
        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            switch (source[index])
            {
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return source[start..(index + 1)];
                    }

                    break;
            }
        }

        throw new InvalidOperationException("Package03ContactImage action has no complete method body.");
    }

    /// <summary>
    /// 從 test output 向上尋找 solution root，再以短生命週期 File API 讀取 source。讀取完成後沒有持久化
    /// handle、watcher、cache 或 mutable global，避免 contract test 影響另一使用者或下一次測試。
    /// </summary>
    private static string ReadSource()
        => File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "SpeechMessageProducts.ChurchReport",
            "Controllers",
            "MemberInfoController.cs"));

    /// <summary>以 solution file 為唯一判斷依據定位 worktree root，避免接受外部路徑作測試 authority。</summary>
    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("SpeechMessageProducts solution root was not found.");
    }
}

// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/Controllers/DedicationAuditControllerFeeAuditContractTests.cs
// 目的：鎖定奉獻稽核 AJAX fee endpoint 的授權、Package01 路由與錯誤隔離契約。
//
// 此測試只讀取目前 worktree 的 C# 原始碼並立即關閉檔案 handle；它不建立 MVC host、CRM
// 連線、Session、快取、feature flag 或背景工作。控制器相依既有大型產品流程，source contract
// 使我們仍能以可重複方式保護安全順序：server authorization 必須先於 caller GUID parse、
// target lookup 與任一 legacy/typed outbound dispatch。
// ============================================================================

using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Controllers;

/// <summary>
/// 驗證 <c>DedicationAuditController.GetFeesByContactId</c> 的 fail-closed 讀取邊界。
/// 此端點可接收 browser locator，但權限只由 request/session 已建立的 server login contact
/// 決定。測試不假裝 source assertion 是 CE 證據；它只防止本機實作退回 IDOR、session form
/// mutation 或在取消後輸出其他 request 的錯誤資料。
/// </summary>
public sealed class DedicationAuditControllerFeeAuditContractTests
{
    /// <summary>
    /// 驗證先取得並檢查伺服器登入快照，才可解析 browser contact GUID 或建立 manager/dispatch。
    /// 指定索引的 decisive assertions 可防止「合法 GUID 即可讀取任意費用」的 IDOR 回歸；
    /// login snapshot 缺失或不具會計職責必須在任何 target CRM retrieve 前回傳固定結果。
    /// </summary>
    [Fact]
    public void Fee_audit_authorizes_server_login_before_parsing_browser_target_or_dispatch()
    {
        var action = ReadAction();

        action.Should().Contain("EnsureCorrectUserData();");
        action.Should().Contain("DonationFeeAuditAccessResolver.CanAccessFeeAudit(loginContact)");
        action.Should().Contain("Guid.TryParse(id, out var contactId)");
        action.Should().Contain("message = FeeAuditAccessDeniedMessage");

        var authorization = action.IndexOf(
            "DonationFeeAuditAccessResolver.CanAccessFeeAudit(loginContact)",
            StringComparison.Ordinal);
        var parse = action.IndexOf("Guid.TryParse(id, out var contactId)", StringComparison.Ordinal);
        var manager = action.IndexOf("var feeManager = InMemoryContext.DonationPaymentManager;", StringComparison.Ordinal);

        authorization.Should().BeGreaterOrEqualTo(0);
        parse.Should().BeGreaterThan(authorization);
        manager.Should().BeGreaterThan(parse);
        action.Should().NotContain("RetrieveEntity(\"contact\"");
    }

    /// <summary>
    /// 驗證 feature gate 的 true/false 分支明確且互斥：關閉時維持舊 manager 呼叫，開啟時
    /// 只使用新的 request-local typed result，不能回填 DonationPaymentFormModel 或反向補查
    /// contact Entity。此為本機 rollback 邊界；測試不啟用旗標或修改任何部署設定。
    /// </summary>
    [Fact]
    public void Fee_audit_preserves_false_gate_legacy_route_and_true_gate_typed_route()
    {
        var action = ReadAction();

        action.Should().Contain("if (feeManager.IsPackage01FeeReadEnabled)");
        action.Should().Contain("await feeManager.RetrieveFeeAuditByContactAsync(");
        action.Should().Contain("DonationFeeQueryService.ToAjaxRows(auditResult.Fees)");
        action.Should().Contain("await feeManager.GetDedicationFeesByContactIdAsync(");
        action.Should().Contain("feeManager.m_DonationPaymentFormModel.TotalAmount");
        action.Should().NotContain("FillFromContactAsync");
        action.Should().NotContain("DonationPaymentFormModel model");
    }

    /// <summary>
    /// 已中止的 HTTP request 必須讓 <see cref="OperationCanceledException"/> 離開 action，
    /// 而一般錯誤只能回傳固定、去識別化訊息。這避免 cancellation 被轉成含內部 CRM/Gateway
    /// 詳情的 JSON，或使後續 request 看見先前例外；測試也禁止把 <c>e.Message</c> 送回瀏覽器。
    /// </summary>
    [Fact]
    public void Fee_audit_does_not_catch_cancellation_or_echo_raw_exception_details()
    {
        var action = ReadAction();

        action.Should().Contain("catch (Exception e) when (e is not OperationCanceledException)");
        action.Should().NotContain("catch (OperationCanceledException)");
        action.Should().NotContain("message = e.Message");
        action.Should().NotContain("Debug.WriteLine");
        action.Should().Contain("message = FeeAuditUnavailableMessage");
    }

    /// <summary>
    /// 讀取單一 action 的 source section；路徑由測試輸出向上找 solution root，故不依賴某個
    /// Windows 使用者或 worktree 名稱。<see cref="File.ReadAllText(string)"/> 在回傳前釋放檔案
    /// handle，本文字只在當前測試堆疊保存，沒有可跨測試重用的 mutable source cache。
    /// </summary>
    /// <returns>只包含 GetFeesByContactId 方法本體的目前 UTF-8 source。</returns>
    private static string ReadAction()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "SpeechMessageProducts.ChurchReport",
            "Controllers",
            "DedicationAuditController.cs"));
        const string marker = "public async Task<IActionResult> GetFeesByContactId(string id)";
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterOrEqualTo(0);
        var end = source.IndexOf("#endregion", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        return source[start..end];
    }

    /// <summary>
    /// 尋找包含 solution 的目前 worktree 根目錄。找不到就立即失敗，以免測試誤讀其他 checkout
    /// 而提供虛假的安全證據；迴圈不開啟網路或建立長壽命資源。
    /// </summary>
    /// <returns>目前 solution 根目錄。</returns>
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

        throw new DirectoryNotFoundException("找不到目前的 SpeechMessageProducts worktree。");
    }
}

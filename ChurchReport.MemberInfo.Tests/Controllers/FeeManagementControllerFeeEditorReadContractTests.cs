// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/Controllers/FeeManagementControllerFeeEditorReadContractTests.cs
// 目的：鎖定費用編輯器新唯讀 route 的 gate、server snapshot 授權與 no-legacy-dispatch 順序。
//
// 測試邊界：本檔只讀取 worktree 原始碼，讀取完畢即釋放檔案 handle；不建立 MVC host、
// Session、CRM、Gateway client、feature flag、cache、timer 或背景工作。decisive assertions 檢查
// source 中的安全順序，避免既有巨大 Controller 的可寫 FeeList 流程被新 DTO read endpoint 重用。
// ============================================================================

using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Controllers;

/// <summary>
/// 驗證 <c>FeeManagementController.GetFeeEditorRows</c> 是獨立、預設關閉的 DTO-only 邊界。
/// source contract 不宣稱 CE 或 traffic evidence；它只防止本機日後重構讓 false gate 提早讀取
/// session/FeeList、讓 browser GUID 先於 server snapshot 授權，或讓新 route 接上可寫 legacy editor。
/// </summary>
public sealed class FeeManagementControllerFeeEditorReadContractTests
{
    /// <summary>
    /// 兩個 deployment-owned gate 必須是 action 的第一個 executable decision。任何 gate=false 時，
    /// action 不能讀取目前登入者、FeeList、lesson snapshot、browser GUID 或 Package01 composition。
    /// 指定 source index 是 decisive assertion，避免日後將診斷或 legacy work 移到 gate 之前。
    /// </summary>
    [Fact]
    public void Fee_editor_read_dual_gate_short_circuits_before_session_snapshot_locator_or_client_work()
    {
        var action = ReadAction();

        action.Should().Contain("if (!DonationDynamicsAccessBootstrap.IsPackage01FeeEditorReadEnabled(_configuration))");
        action.Should().Contain("var (account, password) = CurrentLogin();");
        action.Should().Contain("var feeList = InMemoryContext.FeeList;");
        action.Should().Contain("Guid.TryParse(discipleLessonsId, out var discipleLessonId)");
        action.Should().Contain("DonationDynamicsAccessBootstrap.TryCreatePackage01Client(_configuration)");

        var gate = action.IndexOf("if (!DonationDynamicsAccessBootstrap.IsPackage01FeeEditorReadEnabled(_configuration))", StringComparison.Ordinal);
        var login = action.IndexOf("var (account, password) = CurrentLogin();", StringComparison.Ordinal);
        var feeList = action.IndexOf("var feeList = InMemoryContext.FeeList;", StringComparison.Ordinal);
        var parse = action.IndexOf("Guid.TryParse(discipleLessonsId, out var discipleLessonId)", StringComparison.Ordinal);
        var client = action.IndexOf("DonationDynamicsAccessBootstrap.TryCreatePackage01Client(_configuration)", StringComparison.Ordinal);

        gate.Should().BeGreaterOrEqualTo(0);
        login.Should().BeGreaterThan(gate);
        feeList.Should().BeGreaterThan(login);
        parse.Should().BeGreaterThan(feeList);
        client.Should().BeGreaterThan(parse);
    }

    /// <summary>
    /// 授權 target 必須來自已載入且 current-login-matched 的 server lesson snapshot，並在 parse
    /// 後再精確比對；browser locator 不得成為 Profile、owner 或 CRM query 的 authority。action 不能
    /// 自行建立 lesson list，否則 URL 呼叫會變成繞過授權的 legacy CRM load。
    /// </summary>
    [Fact]
    public void Fee_editor_read_authorizes_server_snapshot_before_dispatch_without_legacy_lesson_loading()
    {
        var action = ReadAction();

        action.Should().Contain("feeList.EnsureLoginScope(account, password);");
        action.Should().Contain("feeList.IsLessonListLoadedFor(account, password)");
        action.Should().Contain("FeeEditorLessonAccessResolver.TryCreateAuthorizedLessonIds(");
        action.Should().Contain("FeeEditorLessonAccessResolver.IsAuthorizedTarget(authorizedLessonIds, discipleLessonId)");
        action.Should().NotContain("EnsureLessonListLoaded(");
        action.Should().NotContain("SetupLessonList(");
        action.Should().NotContain("SetupPresentFeeList(");
        action.Should().NotContain("RetrieveEntity(");
        action.Should().NotContain("ToolUtility");

        var snapshot = action.IndexOf("FeeEditorLessonAccessResolver.TryCreateAuthorizedLessonIds(", StringComparison.Ordinal);
        var parse = action.IndexOf("Guid.TryParse(discipleLessonsId, out var discipleLessonId)", StringComparison.Ordinal);
        var dispatch = action.IndexOf("await service.RetrieveAsync(discipleLessonId, HttpContext.RequestAborted)", StringComparison.Ordinal);

        snapshot.Should().BeGreaterOrEqualTo(0);
        parse.Should().BeGreaterThan(snapshot);
        dispatch.Should().BeGreaterThan(parse);
    }

    /// <summary>
    /// 新 route 只能建立 `FeeEditorReadService` 的 immutable result，並將 request cancellation 原樣
    /// 傳遞。取消必須離開 action；一般錯誤只回傳固定訊息，不能 echo CRM/Gateway 例外。既有可寫
    /// FeeDataList、Update、Save 路徑一律不能出現在新 action，避免 read DTO 被重新接到 editor state。
    /// </summary>
    [Fact]
    public void Fee_editor_read_is_dto_only_cancellation_safe_and_disconnected_from_editable_paths()
    {
        var action = ReadAction();

        action.Should().Contain("new FeeEditorReadService(");
        action.Should().Contain("await service.RetrieveAsync(discipleLessonId, HttpContext.RequestAborted)");
        action.Should().Contain("catch (Exception ex) when (ex is not OperationCanceledException)");
        action.Should().NotContain("catch (OperationCanceledException)");
        action.Should().NotContain("message = ex.Message");
        action.Should().NotContain("FeeDataList");
        action.Should().NotContain("UpdateFeeData");
        action.Should().NotContain("SaveBatch");
        action.Should().NotContain("new Fee(");
    }

    /// <summary>
    /// checked-in appsettings 的 base Package01 gate 與此 capability gate 都必須維持 false。讀檔
    /// 僅作 configuration regression，不解析 secrets 或啟動組態；缺任一 false 值都表示本機 code
    /// 可能意外變成切流，因此測試立即失敗而不執行 CE request。
    /// </summary>
    [Fact]
    public void Checked_in_configuration_keeps_fee_editor_read_gates_disabled()
    {
        foreach (var fileName in new[] { "appsettings.json", "appsettings.Development.json" })
        {
            var configuration = File.ReadAllText(Path.Combine(
                FindRepositoryRoot(),
                "SpeechMessageProducts.ChurchReport",
                fileName));

            configuration.Should().Contain("\"Package01FeeReadsEnabled\": false");
            configuration.Should().Contain("\"Package01FeeEditorReadEnabled\": false");
        }
    }

    /// <summary>
    /// 只擷取目標 action source，讓 assertions 不會誤匹配既有 Fee／Present action 的 legacy APIs。
    /// <see cref="File.ReadAllText(string)"/> 完成後不保留 handle；source text 只存在目前測試 stack，
    /// 不會形成跨測試或跨使用者 cache。
    /// </summary>
    private static string ReadAction()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "SpeechMessageProducts.ChurchReport",
            "Controllers",
            "FeeManagementController.cs"));
        const string marker = "public async Task<IActionResult> GetFeeEditorRows(string discipleLessonsId)";
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

        throw new InvalidOperationException("找不到 GetFeeEditorRows 的方法結束大括號。");
    }

    /// <summary>
    /// 向上尋找目前 solution root，確保測試不讀取其他 worktree 而製造錯誤安全證據。找不到
    /// solution 立即失敗；此迴圈只讀取目錄 metadata，不開網路、程序或長期資源。
    /// </summary>
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

// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/Controllers/StorLessonControllerProductClientContractTests.cs
// 目的：保護兩個 stor-lesson 顯示端點在 Package01 開啟時的無 SDK 補查與取消傳遞契約。
//
// 測試邊界：控制器必須保留既有 CRM、Session 與 DevExtreme 相容性，直接建立控制器會需要
// 連線與完整登入環境。因此本測試只讀取目前 worktree 的原始碼，驗證確切的控制流程；
// File.ReadAllText 在讀取完成後立即關閉 handle，文字只活在單一測試方法中，不建立 CRM
// 連線、背景工作、快取或跨測試的使用者資料。
// ============================================================================

using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Controllers;

/// <summary>
/// 驗證 MemberInfo 與 Equipment 的 Package01 stor-lesson 顯示邊界。
/// 這些契約確保 feature gate 開啟後，控制器只把 server-owned request cancellation 與既有
/// contact 識別傳給 typed service；不得為補全姓名重新讀取 CRM Entity，也不得把可變資料
/// 留在跨 request 的欄位、快取或背景工作。
/// </summary>
public sealed class StorLessonControllerProductClientContractTests
{
    /// <summary>
    /// MemberInfo 的 Package01 分支必須在呼叫 typed service 前保留 full name 為 null，並把
    /// HttpContext.RequestAborted 傳遞到底層。反向條件下才允許 legacy CRM contact read，
    /// 因此切換旗標不會在新路徑暗中延長 SDK session 或繞回舊資料來源。
    /// </summary>
    [Fact]
    public void MemberInfo_package01_stor_lesson_path_skips_contact_retrieve_and_forwards_request_cancellation()
    {
        var source = ReadSource("MemberInfoController.cs");

        source.Should().Contain("public async Task<object> LoadContactStorLessons(");
        source.Should().Contain("string? fullName = null;");
        source.Should().Contain("if (!queryService.IsPackage01Enabled)");
        source.Should().Contain("service.Retrieve(\"contact\", contactGuid, new ColumnSet(\"fullname\"))");
        source.Should().Contain("await queryService.GetByContactAsync(");
        source.Should().Contain("fullName,");
        source.Should().Contain("HttpContext.RequestAborted");
    }

    /// <summary>
    /// Equipment 的 Package01 分支不得將由既有成員清單取得的姓名傳給 typed read，也必須
    /// 原樣轉交目前 HTTP request 的取消 token。條件運算子使 gate=false 繼續使用 legacy
    /// fullname 語意，gate=true 則只用受限 DTO query，避免不同使用者或 profile 的顯示名稱
    /// 藉由 SDK 補查、同步等待或共用狀態混入結果。
    /// </summary>
    [Fact]
    public void Equipment_package01_stor_lesson_path_uses_null_name_and_forwards_request_cancellation()
    {
        var source = ReadSource("EquipmentController.cs");

        source.Should().Contain("public async Task<object> LoadEquipmentStorLessons(");
        source.Should().Contain("await queryService.GetByContactAsync(");
        source.Should().Contain("queryService.IsPackage01Enabled ? null : member.FullName,");
        source.Should().Contain("member.ContactId,");
        source.Should().Contain("HttpContext.RequestAborted");
    }

    /// <summary>
    /// 已取消的 HTTP request 不得被泛用例外處理轉成錯誤回應或診斷資料。兩個非同步 action
    /// 都必須只在其 <see cref="HttpContext.RequestAborted"/> 已取消時重新擲出
    /// <see cref="OperationCanceledException"/>；這讓 ASP.NET Core 結束原 request，而不會把
    /// 已中止的 ProductClient work、例外或使用者資料保留到 HandleError 的後續流程。
    /// </summary>
    [Fact]
    public void Stor_lesson_actions_rethrow_request_cancellation_before_generic_error_handling()
    {
        var memberInfoSource = ReadSource("MemberInfoController.cs");
        var equipmentSource = ReadSource("EquipmentController.cs");
        const string cancellationGuard =
            "catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)";

        memberInfoSource.Should().Contain(cancellationGuard);
        equipmentSource.Should().Contain(cancellationGuard);
    }

    /// <summary>
    /// 讀取指定控制器的目前原始碼。Repository root 由測試輸出目錄向上探索，不使用硬編碼
    /// 使用者路徑；<see cref="File.ReadAllText(string)"/> 的短生命週期檔案 handle 由 BCL 在
    /// 回傳前關閉，故 helper 不保留 session、檔案串流或可變 source cache。
    /// </summary>
    /// <param name="fileName">要驗證且位於 ChurchReport Controllers 目錄的檔名。</param>
    /// <returns>目前 worktree 中可供此測試使用的 UTF-8 原始碼文字。</returns>
    private static string ReadSource(string fileName)
        => File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "SpeechMessageProducts.ChurchReport",
            "Controllers",
            fileName));

    /// <summary>
    /// 從測試輸出目錄向上尋找 solution 根目錄，讓 CI、不同 Windows 使用者與 worktree
    /// 位置都使用相同的相對路徑。找不到時立即拋出例外，避免誤讀其他 repository 的原始碼
    /// 而產生錯誤安全感；迴圈只保存短生命週期 DirectoryInfo，不配置背景資源。
    /// </summary>
    /// <returns>含 <c>SpeechMessageProducts.sln</c> 的目前 worktree 根目錄。</returns>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "Could not locate SpeechMessageProducts.sln from test output directory.");
    }
}

// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/Controllers/MemberInfoControllerPackage03FullContactImageContractTests.cs
// 用途：在 P7.4 完整會友照片顯示路由實作前，鎖定部署閘門、授權順序、DTO 邊界與三種安全輸出分支。
//
// 本檔案只讀取已簽入的 C# 與設定來源；不建立 CRM 連線、不配置 Connector、不保留 HttpContext、
// Session、照片位元組或任何跨測試可變資料。每個測試的檔案控制代碼僅存活於同步讀取期間，
// 讓 RED 驗證本身不引入跨使用者或資源生命週期風險。
// ============================================================================

using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Controllers;

/// <summary>
/// 驗證 <c>/MemberInfo/Package03FullContactImage</c> 的未來安全邊界。
///
/// 此類別保護的契約是：部署擁有的 base/sub gate 必須先於任何使用者工作、瀏覽器 locator
/// 解析與 typed client 組合而拒絕請求；啟用後必須依序建立伺服器端 MemberInfo 範圍、解析
/// locator、授權目標 contact，最後才進行固定 profile/workload 的 typed dispatch。測試故意
/// 以來源契約先行失敗，避免在新路由開放前回退到 ToolUtility、CRM SDK、快取或重試路徑，
/// 而使其他登入者、設定檔或要求的照片資料互相洩漏。
/// </summary>
public sealed class MemberInfoControllerPackage03FullContactImageContractTests
{
    /// <summary>
    /// 保護 deployment rollback 契約：base gate 或完整顯示 sub-gate 任一為 false 時，路由必須
    /// 回傳 404，且不能先 hydrate Session、取得 MemberInfo scope、解析 caller locator、檢查
    /// target 或建立 typed client。故障注入為檢查不存在或順序錯誤的來源符號；決定性斷言是
    /// gate 位於所有會觸及 request/transport 狀態的動作之前。
    /// </summary>
    [Fact]
    public void Full_contact_image_route_short_circuits_base_and_sub_gates_before_scope_parse_or_client()
    {
        var controller = ReadControllerSource();
        var bootstrap = ReadSource("Services", "DonationDynamicsAccessBootstrap.cs");
        var action = SliceMethod(
            controller,
            "public async Task<IActionResult> Package03FullContactImage(string contactId, int size = 80, bool fit = false)");

        bootstrap.Should().Contain("IsPackage03MemberInfoFullContactImageReadEnabled(IConfiguration configuration)");
        bootstrap.Should().Contain("if (!IsPackage03SpecialResourcesEnabled(configuration))");
        bootstrap.Should().Contain("DynamicsAccess:Package03MemberInfoFullContactImageReadEnabled");

        action.Should().Contain("DonationDynamicsAccessBootstrap.IsPackage03MemberInfoFullContactImageReadEnabled(configuration)");
        action.Should().Contain("return NotFound();");
        action.Should().Contain("EnsureCorrectUserData();");
        action.Should().Contain("var access = GetAccess();");
        action.Should().Contain("Guid.TryParse(contactId, out var contactGuid)");
        action.Should().Contain("CanViewContact(contactGuid)");
        action.Should().Contain("DonationDynamicsAccessBootstrap.TryCreatePackage03MemberInfoFullContactImageReadClient(configuration)");

        var gate = action.IndexOf(
            "DonationDynamicsAccessBootstrap.IsPackage03MemberInfoFullContactImageReadEnabled(configuration)",
            StringComparison.Ordinal);
        var ensureScope = action.IndexOf("EnsureCorrectUserData();", StringComparison.Ordinal);
        var accessScope = action.IndexOf("var access = GetAccess();", StringComparison.Ordinal);
        var parse = action.IndexOf("Guid.TryParse(contactId, out var contactGuid)", StringComparison.Ordinal);
        var targetAuthorization = action.IndexOf("CanViewContact(contactGuid)", StringComparison.Ordinal);
        var typedClient = action.IndexOf(
            "DonationDynamicsAccessBootstrap.TryCreatePackage03MemberInfoFullContactImageReadClient(configuration)",
            StringComparison.Ordinal);

        gate.Should().BeGreaterOrEqualTo(0);
        ensureScope.Should().BeGreaterThan(gate);
        accessScope.Should().BeGreaterThan(ensureScope);
        parse.Should().BeGreaterThan(accessScope);
        targetAuthorization.Should().BeGreaterThan(parse);
        typedClient.Should().BeGreaterThan(targetAuthorization);
    }

    /// <summary>
    /// 保護 checked-in 預設封閉契約：正式與開發設定都必須明確保留完整顯示 sub-gate 為 false，
    /// 即使 Package03 base gate 日後被設定為 true 也不可意外開放新端點。故障注入為逐一讀取
    /// 兩份部署設定；決定性斷言是各檔案都有同名且值為 false 的設定，提供可預期的無 I/O
    /// rollback 位置。
    /// </summary>
    [Fact]
    public void Checked_in_configuration_keeps_full_contact_image_sub_gate_disabled()
    {
        foreach (var fileName in new[] { "appsettings.json", "appsettings.Development.json" })
        {
            var configuration = ReadSource(fileName);

            configuration.Contains(
                    "\"Package03MemberInfoFullContactImageReadEnabled\": false",
                    StringComparison.Ordinal)
                .Should()
                .BeTrue("checked-in deployment settings must keep the sub-gate disabled");
        }
    }

    /// <summary>
    /// 保護啟用後的資料隔離與資源所有權契約：控制器僅能在 scope、GUID locator 與目標授權成功
    /// 後，將 RequestAborted 交給專用 typed service；不得在此分支使用 ToolUtility、CRM SDK、
    /// IMemoryCache、重試或 legacy fallback。故障注入為掃描 action 本體；決定性斷言是允許
    /// 的 dispatch 順序及所有禁止符號均不存在，防止不確定 transport/快取狀態被其他 request
    /// 重用。
    /// </summary>
    [Fact]
    public void Full_contact_image_enabled_path_authorizes_before_typed_dispatch_without_legacy_or_cache_dependencies()
    {
        var action = SliceMethod(
            ReadControllerSource(),
            "public async Task<IActionResult> Package03FullContactImage(string contactId, int size = 80, bool fit = false)");

        action.Should().Contain("EnsureCorrectUserData();");
        action.Should().Contain("var access = GetAccess();");
        action.Should().Contain("Guid.TryParse(contactId, out var contactGuid)");
        action.Should().Contain("CanViewContact(contactGuid)");
        action.Should().Contain("DonationDynamicsAccessBootstrap.TryCreatePackage03MemberInfoFullContactImageReadClient(configuration)");
        action.Should().Contain("HttpContext.RequestAborted");

        action.Should().NotContain("ToolUtility");
        action.Should().NotContain("IOrganizationService");
        action.Should().NotContain("Entity");
        action.Should().NotContain("QueryExpression");
        action.Should().NotContain("IMemoryCache");
        action.Should().NotContain("retry");
        action.Should().NotContain("fallback");
        action.Should().NotContain("GetConnection(");
        action.Should().NotContain("Retrieve(");
        action.Should().NotContain("GetContactImage(");
    }

    /// <summary>
    /// 保護閉合 display union 的輸出契約：圖片 branch 只能輸出已驗證的本機 image bytes，LINE
    /// branch 只能以已驗證 URL redirect，avatar branch 只能由既有性別 SVG 產生。故障注入為
    /// 要求三個 discriminator 與對應 MVC 結果同時出現在新 action；決定性斷言避免一個 branch
    /// 攜帶另一 branch 的資料，或把 CRM/LINE 原始回應直接發布給瀏覽器。
    /// </summary>
    [Fact]
    public void Full_contact_image_route_maps_only_image_redirect_and_avatar_display_branches()
    {
        var action = SliceMethod(
            ReadControllerSource(),
            "public async Task<IActionResult> Package03FullContactImage(string contactId, int size = 80, bool fit = false)");

        action.Should().Contain("Package03MemberInfoFullContactImageReadResultKind.Image");
        action.Should().Contain("Package03MemberInfoFullContactImageReadResultKind.LineRedirect");
        action.Should().Contain("Package03MemberInfoFullContactImageReadResultKind.DefaultAvatar");
        action.Should().Contain("File(");
        action.Should().Contain("Redirect(");
        action.Should().Contain("DefaultAvatarSvg.ForGender(");
        action.Should().Contain("CreateThumbnailIfNeeded(");
        action.Should().Contain("CreateFitThumbnail(");
        action.Should().Contain("ApplyImageResponseCacheHeaders(");
    }

    /// <summary>
    /// 以 brace depth 擷取單一 action，避免同檔舊照片路由中的 legacy CRM 符號影響新邊界判定。
    /// 來源字串只在目前測試方法內保存，方法回傳後不保留 request、使用者、連線或圖片資料。
    /// </summary>
    /// <param name="source">UTF-8 控制器原始碼。</param>
    /// <param name="marker">欲擷取 public action 的完整宣告。</param>
    /// <returns>從 action 宣告至成對結尾大括號的獨立來源片段。</returns>
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

        throw new InvalidOperationException("Package03FullContactImage action has no complete method body.");
    }

    /// <summary>
    /// 讀取 ChurchReport 來源檔案以驗證部署與 MVC 邊界。<see cref="File.ReadAllText(string)"/>
    /// 在讀取完成後釋放檔案控制代碼，且不快取任何內容，避免 test host 將設定或來源狀態跨案例保留。
    /// </summary>
    /// <param name="directoryOrFileName">專案根目錄下的檔案名稱或子目錄名稱。</param>
    /// <param name="fileName">子目錄下的檔案名稱；根目錄檔案時為 null。</param>
    /// <returns>目前 worktree 的完整文字內容。</returns>
    private static string ReadSource(string directoryOrFileName, string? fileName = null)
    {
        var applicationRoot = Path.Combine(FindRepositoryRoot(), "SpeechMessageProducts.ChurchReport");
        var path = fileName is null
            ? Path.Combine(applicationRoot, directoryOrFileName)
            : Path.Combine(applicationRoot, directoryOrFileName, fileName);
        return File.ReadAllText(path);
    }

    /// <summary>
    /// 讀取唯一 MemberInfo 控制器來源，讓所有 action 範圍檢查都從相同 checkout 取得資料。
    /// </summary>
    /// <returns>MemberInfoController 的完整 UTF-8 來源。</returns>
    private static string ReadControllerSource()
        => ReadSource("Controllers", "MemberInfoController.cs");

    /// <summary>
    /// 從測試輸出目錄向上尋找 solution root，避免硬編碼開發者路徑或把環境特定資料寫入共享狀態。
    /// 找不到 root 時直接失敗，以免在錯誤 checkout 對非目標來源產生看似成功的安全結論。
    /// </summary>
    /// <returns>包含 solution 與 ChurchReport 專案的 worktree 根目錄。</returns>
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

        throw new DirectoryNotFoundException("SpeechMessageProducts solution root was not found.");
    }
}

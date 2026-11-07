/*
 * 檔案整體說明
 *
 * 目的與定位：以合成相依圖及實際方案保護 R2／R3／R4／R9；每個反例只違反指定邊界，
 * 避免以其他建置失敗冒充架構拒絕。測試不讀任何秘密、不連資料庫；實際專案僅進行有界
 * MSBuild／來源評估，子程序、串流與來源快照由讀取器及當次案例釋放。
 *
 * 主要功能與流程：
 * 本檔宣告 ArchitectureRuleTests。
 * - 雙目標專案不能指向只有 net10 的產品，即使當次建置選到 net10 也不豁免整體雙目標契約。
 * - Extensions、ASP.NET 與 Dataverse 直接套件不得進入共用雙目標；以各套件正反例保護辨識。
 * - ToolUtility.Core 的 Dataverse 例外只限 net10 SDK 型別來源，不能擴大為 net462 或任意 Extensions 豁免。
 * - 沙箱只引用可提供 net462 的雙目標產品；net48 宿主不等於合格部署產品。
 * - R2 以公開型別宣告檢查主機／Plugin 邊界；R3 以精確來源檔白名單保護相容
 *   組織別名；R9 檢查 HttpClient 所在專案及 Plugin 的禁止參考。
 * 測試案例入口包含 DualTarget_ReferenceToNet10Only_IsRejected、DualTarget_HostPackage_IsRejected、
 * ToolCore_DataverseException_IsTargetSpecific、Sandbox_ReferenceToNet48Product_IsRejected、
 * Sandbox_HigherFramework_IsRejected、HostToHost_IsRejected、
 * ReverseDependency_ToolReference_AndUnknownProject_AreRejected、Net10SharedLayer_ToDualCore_IsAllowed；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SpeechMessage.Architecture.Tests;

/// <summary>
/// 以合成相依圖及實際方案保護 R2／R3／R4／R9；每個反例只違反指定邊界，避免以其他建置失敗冒充架構拒絕。
/// 測試不讀任何秘密、不連資料庫；實際專案僅進行有界的 MSBuild／來源評估，子程序、串流與來源快照由讀取器釋放。
/// </summary>
public sealed class ArchitectureRuleTests
{
    private static readonly IReadOnlyList<string> SystemDrawingPackages = new[] { "System.Drawing.Common" };

    /// <summary>雙目標專案不能指向只有 net10 的產品，即使當次建置選到 net10 也不豁免整體雙目標契約。</summary>
    [Fact]
    public void DualTarget_ReferenceToNet10Only_IsRejected()
    {
        var graph = Graph(Product("SpeechMessage.Localization", "net462", "net462;net10.0", "ToolUtility"), Product("ToolUtility", "net10.0", "net10.0"));
        Assert.Contains(ArchitectureRules.Validate(graph), x => x.Rule == "DualTargetReference");
    }

    /// <summary>Extensions、ASP.NET 與 Dataverse 直接套件不得進入共用雙目標；以各套件正反例保護辨識。</summary>
    /// <param name="package">刻意注入的 host-only 套件。</param>
    [Theory]
    [InlineData("Microsoft.Extensions.Logging")]
    [InlineData("Microsoft.AspNetCore.Http")]
    [InlineData("Microsoft.PowerPlatform.Dataverse.Client")]
    public void DualTarget_HostPackage_IsRejected(string package)
    {
        var project = Product("SpeechMessage.Localization", "net462", "net462;net10.0") with { Packages = new[] { package } };
        Assert.Contains(ArchitectureRules.Validate(Graph(project)), x => x.Rule == "DualTargetPackage");
        Assert.Empty(ArchitectureRules.Validate(Graph(project with { Packages = Array.Empty<string>() })));
    }

    /// <summary>ToolUtility.Core 的 Dataverse 例外只限 net10 SDK 型別來源，不能擴大為 net462 或任意 Extensions 豁免。</summary>
    [Fact]
    public void ToolCore_DataverseException_IsTargetSpecific()
    {
        var project = Product("ToolUtility.Core", "net10.0", "net462;net10.0") with { Packages = new[] { "Microsoft.PowerPlatform.Dataverse.Client" } };
        Assert.Empty(ArchitectureRules.Validate(Graph(project)));
        Assert.Contains(ArchitectureRules.Validate(Graph(project with { Target = "net462" })), x => x.Rule == "DualTargetPackage");
    }

    /// <summary>沙箱只引用可提供 net462 的雙目標產品；net48 宿主不等於合格部署產品。</summary>
    [Fact]
    public void Sandbox_ReferenceToNet48Product_IsRejected()
    {
        var plugin = Product("SpeechMessage.Dynamics.Plugins", "net462", "net462", "SpeechMessage.Localization");
        Assert.Contains(ArchitectureRules.Validate(Graph(plugin, Product("SpeechMessage.Localization", "net48", "net48;net10.0"))), x => x.Rule == "SandboxReference");
        Assert.Empty(ArchitectureRules.Validate(Graph(plugin, Product("SpeechMessage.Localization", "net462", "net462;net10.0"))));
    }

    /// <summary>Plugin 自身高於 v4.6.2 就必須拒絕，不得只檢查被參考專案。</summary>
    [Fact]
    public void Sandbox_HigherFramework_IsRejected()
    {
        Assert.Contains(ArchitectureRules.Validate(Graph(Product("SpeechMessage.Dynamics.Plugins", "net48", "net48"))), x => x.Rule == "SandboxTarget");
    }

    /// <summary>主機之間不共享 product assembly；故意讓 ChurchReport 指向另一主機應立即拒絕。</summary>
    [Fact]
    public void HostToHost_IsRejected()
    {
        Assert.Contains(ArchitectureRules.Validate(Graph(Product("SpeechMessageProducts.ChurchReport", "net10.0", "net10.0", "SpeechMessageProducts.LineWebhook"), Product("SpeechMessageProducts.LineWebhook", "net10.0", "net10.0"))), x => x.Rule == "HostReference");
    }

    /// <summary>低層反向依賴、工具被產品引用與未分類專案都不可靜默放行。</summary>
    [Fact]
    public void ReverseDependency_ToolReference_AndUnknownProject_AreRejected()
    {
        Assert.Contains(ArchitectureRules.Validate(Graph(Product("Line.Messaging", "net10.0", "net10.0", "LineMessagingProcessor"), Product("LineMessagingProcessor", "net10.0", "net10.0"))), x => x.Rule == "LayerDirection");
        Assert.Contains(ArchitectureRules.Validate(Graph(Product("ToolUtility", "net10.0", "net10.0", "SpeechMessage.Dynamics.Deployment"), Product("SpeechMessage.Dynamics.Deployment", "net10.0", "net10.0"))), x => x.Rule == "DeploymentIsolation");
        Assert.Contains(ArchitectureRules.Validate(Graph(Product("Unclassified.Product", "net10.0", "net10.0"))), x => x.Rule == "UnknownProject");
    }

    /// <summary>net10 共用層往雙目標核心為合法方向；層級圖不能因文件的視覺排列誤擋此情境。</summary>
    [Fact]
    public void Net10SharedLayer_ToDualCore_IsAllowed()
    {
        Assert.Empty(ArchitectureRules.Validate(Graph(Product("SpeechMessage.Tenancy", "net10.0", "net10.0", "SpeechMessage.Localization"), Product("SpeechMessage.Localization", "net462", "net462;net10.0"), Product("SpeechMessage.Localization", "net10.0", "net462;net10.0"))));
    }

    /// <summary>
    /// R2 只允許主機外殼公開 Program、Registration 或 Endpoints 類型；一般公開服務即使
    /// 名稱看似主機功能也必須被拒絕，避免以命名慣例繞過外殼邊界。
    /// </summary>
    [Fact]
    public void R2_HostPublicTypeOutsideShell_IsRejected()
    {
        var violations = ArchitectureRules.ValidateSource(Graph(
            Product("SpeechMessageProducts.LineWebhook", "net10.0", "net10.0") with
            {
                Sources = new[]
                {
                    Source("Endpoints.cs", "public sealed class HealthEndpoints { }"),
                    Source("Service.cs", "public sealed class MutableRequestService { }")
                }
            }));

        Assert.Contains(violations, item => item.Rule == "R2HostPublicType" && item.Dependency == "MutableRequestService");
        Assert.DoesNotContain(violations, item => item.Dependency == "HealthEndpoints");
    }

    /// <summary>
    /// R2 的 Plugin 邊界允許實際公開 IPlugin／CodeActivity 入口，但不允許公開可被外部
    /// 直接建立的 helper 類別；測試使用合成來源，不載入 Dataverse 或產品組件。
    /// </summary>
    [Fact]
    public void R2_PluginPublicHelperOutsideEntryPoint_IsRejected()
    {
        var violations = ArchitectureRules.ValidateSource(Graph(
            Product("SpeechMessage.Dynamics.Plugins", "net462", "net462") with
            {
                Sources = new[]
                {
                    Source("Plugin.cs", "public sealed class ValidPlugin : IPlugin { }"),
                    Source("Helper.cs", "public sealed class PublicPluginHelper { }")
                }
            }));

        Assert.Contains(violations, item => item.Rule == "R2PluginPublicType" && item.Dependency == "PublicPluginHelper");
        Assert.DoesNotContain(violations, item => item.Dependency == "ValidPlugin");
    }

    /// <summary>
    /// R3 只接受精確檔案／專案／字面值白名單；相同組織字面值出現在另一個來源時不得
    /// 因為既有相容案例而整體放行。
    /// </summary>
    [Fact]
    public void R3_OrganizationLiteralRequiresExactCompatibilityAllowlist()
    {
        var rejected = ArchitectureRules.ValidateSource(Graph(
            Product("SpeechMessage.Hosting", "net10.0", "net10.0") with
            {
                Sources = new[] { Source("Resolver.cs", "var alias = \"Jesus\";") }
            }));
        Assert.Contains(rejected, item => item.Rule == "R3OrganizationLiteral" && item.Dependency == "Jesus");

        var allowed = ArchitectureRules.ValidateSource(Graph(
            Product("SpeechMessage.Tenancy", "net10.0", "net10.0") with
            {
                Sources = new[] { Source("ConfigurationLineChannelResolver.cs", "var alias = \"Jesus\";") }
            }));
        Assert.DoesNotContain(allowed, item => item.Rule == "R3OrganizationLiteral");
    }

    /// <summary>
    /// R9 只允許指定傳輸／主機專案使用 HttpClient，Plugin 另須拒絕 Payments 與
    /// System.Drawing 參考；每個斷言都對應一個可造成邊界退化的實際修改。
    /// </summary>
    [Fact]
    public void R9_TransportAndPluginReferencesAreBounded()
    {
        var violations = ArchitectureRules.ValidateSource(Graph(
            Product("SpeechMessage.Dynamics.Plugins", "net462", "net462",
                "SpeechMessage.Payments") with
            {
                Packages = SystemDrawingPackages,
                Sources = new[] { Source("Plugin.cs", "using System.Net.Http; public sealed class Plugin { private HttpClient client; }") }
            }));

        Assert.Contains(violations, item => item.Rule == "R9HttpClientProject");
        Assert.Contains(violations, item => item.Rule == "R9PluginPaymentsReference");
        Assert.Contains(violations, item => item.Rule == "R9PluginSystemDrawingReference");
    }

    /// <summary>
    /// 以真實方案逐目標來源快照執行 R2／R3／R9。P2 目前只允許已知的 ChurchReport
    /// 舊主機薄殼搬移債務出現在結果；Hosting、Plugin、Core 與相容白名單不得新增
    /// 未記錄的違規。這是過渡期診斷，不把舊產品債務誤標為整包通過。
    /// </summary>
    [Fact]
    public void Solution_SourceArchitecture_ReportsOnlyDocumentedChurchReportDebt()
    {
        var violations = ArchitectureRules.ValidateSource(ArchitectureProjectReader.ReadSolution());

        Assert.NotEmpty(violations);
        Assert.All(violations, item => Assert.Equal("SpeechMessageProducts.ChurchReport", item.Project));
        Assert.Contains(violations, item => item.Rule == "R2HostPublicType");
        Assert.DoesNotContain(violations, item => item.Rule is "R3OrganizationLiteral" or "R9HttpClientProject" or
            "R9PluginPaymentsReference" or "R9PluginSystemDrawingReference");
    }

    /// <summary>
    /// P2通知owner真正抽出後，Hosting是Tenancy之上的net10基礎而非ToolUtility的消費者。
    /// 正例允許舊工具委派Hosting，反例仍拒絕Hosting反向參考ToolUtility，防止未來形成循環。
    /// 合成圖僅含本案例值，不載入產品或保留任何程序資源。
    /// </summary>
    [Fact]
    public void Hosting_foundation_allows_legacy_consumers_but_rejects_reverse_reference()
    {
        Assert.Empty(ArchitectureRules.Validate(Graph(
            Product("ToolUtility", "net10.0", "net10.0", "SpeechMessage.Hosting"),
            Product("SpeechMessage.Hosting", "net10.0", "net10.0", "SpeechMessage.Tenancy"),
            Product("SpeechMessage.Tenancy", "net10.0", "net10.0"))));
        Assert.Contains(ArchitectureRules.Validate(Graph(
            Product("SpeechMessage.Hosting", "net10.0", "net10.0", "ToolUtility"),
            Product("ToolUtility", "net10.0", "net10.0"))), item => item.Rule == "LayerDirection");
    }

    /// <summary>評估後每目標的參考各自檢查，net10 合法例外不能掩蓋 net462 的條件套件。</summary>
    [Fact]
    public void ConditionalReferences_AreCheckedForEachEvaluatedTarget()
    {
        var valid = Product("ToolUtility.Core", "net10.0", "net462;net10.0") with { Packages = new[] { "Microsoft.PowerPlatform.Dataverse.Client" } };
        var invalid = valid with { Target = "net462" };
        Assert.Contains(ArchitectureRules.Validate(Graph(valid, invalid)), x => x.Rule == "DualTargetPackage" && x.Target == "net462");
    }

    /// <summary>實際方案逐 csproj 與每目標評估；新增未知產品或違規參考須讓方案測試紅燈，而非只測合成字串。</summary>
    [Fact]
    public void Solution_AllEvaluatedTargets_ObeyR4()
    {
        var projects = ArchitectureProjectReader.ReadSolution();
        Assert.NotEmpty(projects);
        Assert.Empty(ArchitectureRules.Validate(projects));
    }

    /// <summary>建立單一不可變的合成評估節點，陣列僅屬此案例，不持有 request 或 SDK 型別。</summary>
    private static ArchitectureProject Product(string name, string target, string targets, params string[] references) =>
        new(name, target, targets.Split(';'), references, Array.Empty<string>(), false);

    /// <summary>建立不持有檔案、程序或使用者資料的合成來源快照。</summary>
    private static ArchitectureSource Source(string path, string text) => new(path, text);

    /// <summary>將案例節點組成 operation-local 圖，不跨案例快取。</summary>
    private static ArchitectureProject[] Graph(params ArchitectureProject[] projects) => projects;
}

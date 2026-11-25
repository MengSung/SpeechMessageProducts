/*
 * 檔案整體說明
 *
 * 目的與定位：以合成相依圖及實際方案保護 R2／R3／R4／R9 與 SharedToProduct；
 * 每個反例只違反指定邊界，
 * 避免以其他建置失敗冒充架構拒絕。測試不讀任何秘密、不連資料庫；實際專案僅進行有界
 * MSBuild／來源評估，子程序、串流與來源快照由讀取器及當次案例釋放。
 *
 * 主要功能與流程：
 * 本檔宣告 ArchitectureRuleTests。
 * - P3-01 黃金工具精確分類並保護反向參考與沙箱隔離，未知或相似名稱仍拒絕。
 * - P5-02 僅 PluginHarness 的 net48 節點取得目標例外；其他工具與相似名稱仍檢查 ProductTarget。
 * - P2-05共用核心向Localization取字合法，反向仍拒絕；雙目標專案不能指向只有 net10 的產品，即使當次建置選到 net10 也不豁免整體雙目標契約。
 * - F1 依既有 DAG 分辨產品、工具與共用層；未知專案仍視為共用來源並 fail closed。
 *   合成反例保護分類，真實方案圖驗證共用層的產品相依已移至產品 Hosting。
 * - Extensions、ASP.NET 與 Dataverse 直接套件不得進入共用雙目標；以各套件正反例保護辨識。
 * - P6-06 以合成圖注入九個舊 ASP.NET Core 套件，保護直接及還原相依的拒絕，
 *   並以真實 ChurchReport 的 Debug／Release 評估確認不再使用這些套件；不啟動網站或外部連線。
 * - ToolUtility.Core 的 Dataverse 例外只限 net10 SDK 型別來源，不能擴大為 net462 或任意 Extensions 豁免。
 * - 沙箱只引用可提供 net462 的雙目標產品；net48 宿主不等於合格部署產品。
 * - P6-07以四個合成案例保護ChurchReport語意簽章閉包與實證例外，LineWebhook規則不變；b段另以清冊resolved且空與雙組態零違規防回歸。
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
using System.IO;
using System.Text.Json;
using Xunit;

namespace SpeechMessage.Architecture.Tests;

/// <summary>
/// 以合成相依圖及實際方案保護 R2／R3／R4／R9；每個反例只違反指定邊界，避免以其他建置失敗冒充架構拒絕。
/// 測試不讀任何秘密、不連資料庫；實際專案僅進行有界的 MSBuild／來源評估，子程序、串流與來源快照由讀取器釋放。
/// </summary>
public sealed class ArchitectureRuleTests
{
    private static readonly IReadOnlyList<string> SystemDrawingPackages = new[] { "System.Drawing.Common" };

    /// <summary>
    /// 將已由共用框架提供的舊套件逐一注入主機的直接參考與還原相依，
    /// 決定性斷言均回報相同套件的 LegacyAspNetCorePackage，避免傳遞相依繞過守門。
    /// 合成圖只含當次案例純值，不載入組件、不持有連線或跨案例可變資源。
    /// </summary>
    /// <param name="package">06A 明列、必須拒絕的精確套件 ID。</param>
    [Theory]
    [InlineData("Microsoft.AspNetCore")]
    [InlineData("Microsoft.AspNetCore.Authentication.Cookies")]
    [InlineData("Microsoft.AspNetCore.Mvc")]
    [InlineData("Microsoft.AspNetCore.Routing")]
    [InlineData("Microsoft.AspNetCore.Server.IIS")]
    [InlineData("Microsoft.AspNetCore.Server.IISIntegration")]
    [InlineData("Microsoft.AspNetCore.Server.Kestrel")]
    [InlineData("Microsoft.AspNetCore.Session")]
    [InlineData("Microsoft.AspNetCore.StaticFiles")]
    public void SyntheticHost_AspNetCore2xPackage_IsReported(string package)
    {
        var host = Product("SpeechMessageProducts.ChurchReport", "net10.0", "net10.0");
        Assert.Contains(ArchitectureRules.Validate(Graph(host with { Packages = new[] { package } })),
            item => item.Rule == "LegacyAspNetCorePackage" && item.Dependency == package);
        Assert.Contains(ArchitectureRules.Validate(Graph(host with { RestoredDependencies = new[] { package } })),
            item => item.Rule == "LegacyAspNetCorePackage" && item.Dependency == package);
    }

    /// <summary>
    /// 以實際方案評估 ChurchReport 的直接及已還原套件，保護兩個組態都不重新帶入舊框架套件。
    /// 讀取器擁有有界 MSBuild 子程序及來源快照並於完成後釋放；本測試不讀秘密、不連 D365。
    /// </summary>
    /// <param name="configuration">分別評估 Debug 與 Release，不以單一組態代替。</param>
    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public void ChurchReport_ResolvedPackages_ExcludeAspNetCore2xPackageIds(string configuration)
    {
        var projects = ArchitectureProjectReader.ReadSolution(configuration);
        Assert.Contains(projects, project => project.Name == "SpeechMessageProducts.ChurchReport");
        Assert.DoesNotContain(ArchitectureRules.Validate(projects),
            item => item.Project == "SpeechMessageProducts.ChurchReport" && item.Rule == "LegacyAspNetCorePackage");
    }

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

    /// <summary>
    /// GoodShepherd.Core 在 net10 只為正式 SDK 型別提供 Dataverse 套件；net462 仍使用
    /// CRM SDK，例外必須精確綁定產品與目標，不能擴大成雙目標共用套件豁免。
    /// </summary>
    [Fact]
    public void GoodShepherdCore_DataverseException_IsTargetSpecific()
    {
        var project = Product("SpeechMessageProducts.GoodShepherd.Core", "net10.0", "net462;net10.0") with
        {
            Packages = new[] { "Microsoft.PowerPlatform.Dataverse.Client" }
        };
        Assert.Empty(ArchitectureRules.Validate(Graph(project)));
        Assert.Contains(ArchitectureRules.Validate(Graph(project with { Target = "net462" })),
            x => x.Rule == "DualTargetPackage");
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

    /// <summary>
    /// P3-01 黃金工具只能以精確名稱取得工具層級；正例向下使用 SDK，反例保護產品反向參考、
    /// 工具直接依賴沙箱與相似名稱冒充。所有節點為案例內純值，不載入來源或建立外部資源。
    /// </summary>
    [Fact]
    public void LegacyGolden_ExactToolLayer_PreservesIsolationAndUnknownRejection()
    {
        var tool = Product("LegacyGolden", "net10.0", "net10.0", "Line.Messaging");
        var sdk = Product("Line.Messaging", "net10.0", "net462;net10.0");
        Assert.Empty(ArchitectureRules.Validate(Graph(tool, sdk)));
        Assert.Contains(ArchitectureRules.Validate(Graph(
            Product("SpeechMessageProducts.LineWebhook", "net10.0", "net10.0", tool.Name), tool, sdk)),
            item => item.Rule == "DeploymentIsolation" && item.Dependency == tool.Name);
        Assert.Contains(ArchitectureRules.Validate(Graph(
            Product("LegacyGolden", "net10.0", "net10.0", "SpeechMessage.Dynamics.Plugins"),
            Product("SpeechMessage.Dynamics.Plugins", "net462", "net462"))),
            item => item.Rule == "DeploymentIsolation");
        Assert.Contains(ArchitectureRules.Validate(Graph(Product("LegacyGolden.Other", "net10.0", "net10.0"))),
            item => item.Rule == "UnknownProject");
    }

    /// <summary>
    /// 精確 PluginHarness 只額外允許 net48，既有 net462／net10 目標維持合法。
    /// 合成節點不執行舊程式；空違規清單證明例外不靠其他規則失敗掩蓋。
    /// </summary>
    /// <param name="target">允許的確切目標框架。</param>
    [Theory]
    [InlineData("net48")]
    [InlineData("net462")]
    [InlineData("net10.0")]
    public void ProductTarget_ExactPluginHarness_AllowsOnlyApprovedTargets(string target)
    {
        Assert.Empty(ArchitectureRules.Validate(Graph(Product("PluginHarness", target, target))));
    }

    /// <summary>
    /// 對工具、一般產品及相似名稱注入未核准框架，必須回報具精確專案與目標的 ProductTarget。
    /// 此案例只建立短生命週期純值，確保 IsTool 不再成為任意框架的整體豁免。
    /// </summary>
    /// <param name="name">精確工具、產品或冒充名稱。</param>
    /// <param name="target">不在該專案核准範圍內的目標。</param>
    [Theory]
    [InlineData("LegacyGolden", "net48")]
    [InlineData("LegacyGolden", "net8.0")]
    [InlineData("SpeechMessage.Dynamics.Deployment", "net48")]
    [InlineData("SpeechMessage.Dynamics.Deployment", "net8.0")]
    [InlineData("PluginHarness", "net8.0")]
    [InlineData("PluginHarness", "net481")]
    [InlineData("PluginHarness.Other", "net48")]
    [InlineData("SpeechMessage.Hosting", "net48")]
    public void ProductTarget_ToolClassification_DoesNotExemptOtherTargets(string name, string target)
    {
        Assert.Contains(ArchitectureRules.Validate(Graph(Product(name, target, target))),
            item => item.Rule == "ProductTarget" && item.Project == name &&
                item.Target == target && item.Dependency == target);
    }

    /// <summary>
    /// P2-05允許共用核心向最底層Localization取字；兩個目標都須合法，反向依賴仍必須被拒絕。
    /// 合成圖只持有案例值，不建立產品Host、I/O或可跨案例保留的資源；不以豁免整個核心層放行。
    /// </summary>
    /// <param name="target">分別保護兩個正式產品目標。</param>
    [Theory]
    [InlineData("net462")]
    [InlineData("net10.0")]
    public void Core_LocalizationDependency_IsAllowedButReverseIsRejected(string target)
    {
        Assert.Empty(ArchitectureRules.Validate(Graph(
            Product("LineMessagingProcessor.Core", target, "net462;net10.0", "SpeechMessage.Localization"),
            Product("SpeechMessage.Localization", target, "net462;net10.0"))));
        Assert.Contains(ArchitectureRules.Validate(Graph(
            Product("SpeechMessage.Localization", target, "net462;net10.0", "LineMessagingProcessor.Core"),
            Product("LineMessagingProcessor.Core", target, "net462;net10.0"))),
            item => item.Rule == "LayerDirection" && item.Project == "SpeechMessage.Localization");
    }

    /// <summary>
    /// PROP-004的兩個包裝投影只取得原來源的精確層級；相似名稱仍拒絕分類。
    /// 合成圖同時證明Plugin／雙目標不可反向參考單目標投影，不建立I/O或共享狀態。
    /// </summary>
    [Fact]
    public void PackagingSubsets_ExactLayers_DoNotRelaxSandboxOrDualReferences()
    {
        var line = Product("Line.Messaging.PluginSubset", "net462", "net462");
        var tool = Product("ToolUtility.Core.PluginSubset", "net462", "net462", "LineMessagingProcessor.Core");
        var core = Product("LineMessagingProcessor.Core", "net462", "net462;net10.0");
        Assert.Empty(ArchitectureRules.Validate(Graph(line, tool, core)));
        Assert.Contains(ArchitectureRules.Validate(Graph(core with { References = new[] { line.Name } }, line)), item => item.Rule == "DualTargetReference");
        Assert.Contains(ArchitectureRules.Validate(Graph(Product("SpeechMessage.Dynamics.Plugins", "net462", "net462", tool.Name), tool, core)), item => item.Rule == "SandboxReference");
        Assert.Contains(ArchitectureRules.Validate(Graph(Product("Line.Messaging.PluginSubset.Other", "net462", "net462"))), item => item.Rule == "UnknownProject");
        Assert.Contains(ArchitectureRules.Validate(Graph(line with { References = new[] { tool.Name } }, tool, core)), item => item.Rule == "LayerDirection");
    }

    /// <summary>
    /// 工作樹可位於磁碟根目錄，前綴正規化不可多加分隔符而拒絕所有本地資產。
    /// 使用現有來源只讀驗路徑，同時拒絕根外的相似前綴；不建立或刪除任何檔案。
    /// </summary>
    [Fact]
    public void LocalPath_DriveRoot_AcceptsOwnedFilesButRejectsOutsidePrefix()
    {
        var root = ArchitectureProjectReader.FindRoot();
        var source = Path.Combine(root, "SpeechMessage.Hosting.Tests/Architecture/ArchitectureRuleTests.cs");
        ArchitectureProjectReader.ValidateLocalPath(Path.GetPathRoot(root)!, source);
        Assert.Throws<InvalidDataException>(() =>
            ArchitectureProjectReader.ValidateLocalPath(Path.Combine(root, "not-owner"), source));
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
    /// 真正MVC基底、控制器建構介面與其回傳模型構成公開簽章閉包；泛型及陣列亦須追蹤。
    /// 合成來源不執行產品，故意讓介面方法回傳模型，禁止只放行控制器而漏掉必要契約。
    /// </summary>
    [Fact]
    public void R2_SyntheticHost_ControllerSignatureType_IsAllowed()
    {
        var project = Product("SpeechMessageProducts.ChurchReport", "net10.0", "net10.0") with
        {
            Sources = new[] { Source("Host.cs", """
                public class Startup { }
                public class Controller : Microsoft.AspNetCore.Mvc.ControllerBase
                {
                    public Controller(IService service) { }
                }
                public interface IService { System.Collections.Generic.IReadOnlyList<Model[]> Read(); }
                public sealed class Model { public string Name { get; set; } }
                """) }
        };
        Assert.Empty(ArchitectureRules.ValidateSource(Graph(project)));
    }

    /// <summary>未進入公開簽章閉包的服務與假Controller名稱均拒絕，不能依命名或註解放行；只使用當案純來源。</summary>
    [Fact]
    public void R2_SyntheticHost_PublicNonSignatureType_IsViolation()
    {
        var project = Product("SpeechMessageProducts.ChurchReport", "net10.0", "net10.0") with
        {
            Sources = new[] { Source("Unused.cs", "public class UnusedService {} public class PretendController {}") }
        };
        var violations = ArchitectureRules.ValidateSource(Graph(project));
        Assert.Equal("PretendController|UnusedService", string.Join("|", violations.Select(item => item.Dependency)
            .OrderBy(value => value, StringComparer.Ordinal)));
        Assert.All(violations, item => Assert.Equal("R2HostPublicType", item.Rule));
    }

    /// <summary>
    /// 明列例外只有同時含精確身分、失敗理由與重現證據才允許；缺任一理由或證據即拒絕，
    /// 防止以空清單欄位掩蓋公開型別。所有值合成，不讀寫正式例外檔。
    /// </summary>
    [Fact]
    public void R2_SyntheticHost_ExplicitException_RequiresReasonAndEvidence()
    {
        var project = Product("SpeechMessageProducts.ChurchReport", "net10.0", "net10.0") with
        {
            Sources = new[] { Source("RuntimeModel.cs", "public class RuntimeModel {}") },
            ChurchReportExceptions = new[] { new ArchitectureR2Exception("RuntimeModel", "synthetic runtime refusal", "Synthetic_RuntimeProbe") }
        };
        Assert.Empty(ArchitectureRules.ValidateSource(Graph(project)));
        foreach (var exception in new[]
        {
            new ArchitectureR2Exception("RuntimeModel", "", "Synthetic_RuntimeProbe"),
            new ArchitectureR2Exception("RuntimeModel", "synthetic runtime refusal", "")
        })
            Assert.Throws<InvalidDataException>(() => ArchitectureRules.ValidateSource(Graph(project with
            { ChurchReportExceptions = new[] { exception } })));
    }

    /// <summary>LineWebhook仍只接受Program、Registration、Endpoints；不因ChurchReport的MVC規則而擴權或取得例外。</summary>
    [Fact]
    public void R2_LineWebhook_RuleUnchanged()
    {
        var project = Product("SpeechMessageProducts.LineWebhook", "net10.0", "net10.0") with
        {
            Sources = new[] { Source("Host.cs", """
                public class Program {} public class HostRegistration {} public class HealthEndpoints {}
                public class Startup {} public class Api : Microsoft.AspNetCore.Mvc.ControllerBase {}
                """) }
        };
        var violations = ArchitectureRules.ValidateSource(Graph(project));
        Assert.Equal("Api|Startup", string.Join("|", violations.Select(item => item.Dependency)
            .OrderBy(value => value, StringComparer.Ordinal)));
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
                    Source("Plugin.cs", "using Microsoft.Xrm.Sdk; public sealed class ValidPlugin : IPlugin { }"),
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
                Sources = new[] { Source("SpeechMessage.Tenancy/ConfigurationLineChannelResolver.cs", "var alias = \"Jesus\";") }
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
    /// 組織字串以 C# 解碼後的實際值檢查；Unicode 跳脫不可繞過 R3。這些來源不執行，
    /// 每案例只持有自己的文字與違規陣列，不建立任何產品或外部資源。
    /// </summary>
    /// <param name="expression">不同語法但具有相同受限制內容的字串。</param>
    [Theory]
    [InlineData("\"\\u004Aesus\"")]
    [InlineData("@\"Sunny\"")]
    [InlineData("\"\"\"Ymllc\"\"\"")]
    [InlineData("$\"Jesus\"")]
    public void R3_DecodedStringValues_CannotBypassRule(string expression)
    {
        var project = Product("SpeechMessage.Hosting", "net10.0", "net10.0") with
        {
            Sources = new[] { Source("Candidate.cs", "class Candidate { string alias = " + expression + "; }") }
        };
        Assert.Contains(ArchitectureRules.ValidateSource(Graph(project)), item => item.Rule == "R3OrganizationLiteral");
    }

    /// <summary>
    /// 遷移白名單必須使用方案根的精確路徑；同名檔、子目錄或前綴拼接都不可取得相容例外。
    /// 此反例直接保護白名單比對，沒有修改真實 resolver 或讀取設定。
    /// </summary>
    /// <param name="path">刻意偽裝成合法來源的不同路徑。</param>
    [Theory]
    [InlineData("ConfigurationLineChannelResolver.cs")]
    [InlineData("Fake/SpeechMessage.Tenancy/ConfigurationLineChannelResolver.cs")]
    public void R3_AllowlistRejectsPathImpersonation(string path)
    {
        var project = Product("SpeechMessage.Tenancy", "net10.0", "net10.0") with
        {
            Sources = new[] { Source(path, "class Candidate { string alias = \"Jesus\"; }") }
        };
        Assert.Contains(ArchitectureRules.ValidateSource(Graph(project)), item => item.Rule == "R3OrganizationLiteral");
    }

    /// <summary>Plugin 介面名稱只包含 IPlugin 並不代表真正 SDK 入口；本案例不得被字串包含判斷放行。</summary>
    [Fact]
    public void R2_SimilarlyNamedPluginContract_IsRejected()
    {
        var project = Product("SpeechMessage.Dynamics.Plugins", "net462", "net462") with
        {
            Sources = new[] { Source("Fake.cs", "interface IPluginPretender {} public class Fake : IPluginPretender {}") }
        };
        Assert.Contains(ArchitectureRules.ValidateSource(Graph(project)), item => item.Rule == "R2PluginPublicType" && item.Dependency == "Fake");
    }

    /// <summary>內部容器的 public 巢狀類型不構成對外公開 API，不能因單行正規式而誤報主機違規。</summary>
    [Fact]
    public void R2_InternalContainer_DoesNotExposeNestedType()
    {
        var project = Product("SpeechMessageProducts.LineWebhook", "net10.0", "net10.0") with
        {
            Sources = new[] { Source("Internal.cs", "internal class Container { public class Detail {} }") }
        };
        Assert.Empty(ArchitectureRules.ValidateSource(Graph(project)));
    }

    /// <summary>
    /// 原始多行字串內看似公開類別或 HttpClient 的文字不是程式碼；真正外層只有合法外殼。
    /// 語法樹必須區別字串 token，避免修復 false negative 後又產生 false positive。
    /// </summary>
    [Fact]
    public void R2_R9_RawStringContent_IsNotExecutableCode()
    {
        var project = Product("SpeechMessage.Dynamics.Plugins", "net462", "net462") with
        {
            Sources = new[] { Source("Text.cs", "internal class Text { string value = \"\"\"a \" public class Fake {} HttpClient \" z\"\"\"; }") }
        };
        Assert.Empty(ArchitectureRules.ValidateSource(Graph(project)));
    }

    /// <summary>禁止參考獨立於來源存在；移空 Compile 清單不能繞過 Plugin 的 Payments 禁令。</summary>
    [Fact]
    public void R9_EmptyCompileList_DoesNotHideForbiddenReference()
    {
        var project = Product("SpeechMessage.Dynamics.Plugins", "net462", "net462", "SpeechMessage.Payments");
        Assert.Contains(ArchitectureRules.ValidateSource(Graph(project)), item => item.Rule == "R9PluginPaymentsReference");
    }

    /// <summary>Payments 若日後加入 Framework 目標也不得取得 HttpClient 雙目標例外，例外僅限兩個指定傳輸核心。</summary>
    [Fact]
    public void R9_PaymentsDualTarget_IsNotTransportException()
    {
        var project = Product("SpeechMessage.Payments", "net462", "net462;net10.0") with
        {
            Sources = new[] { Source("Client.cs", "using System.Net.Http; internal class Client { HttpClient client; }") }
        };
        Assert.Contains(ArchitectureRules.ValidateSource(Graph(project)), item => item.Rule == "R9HttpClientProject");
    }

    /// <summary>
    /// P5-02 的 net48 PluginHarness 只建立記錄用 HttpClient 替身；它不是部署產品，
    /// 允許此精確節點不代表其他 net48 工具或任何產品可直接持有傳輸 client。
    /// </summary>
    [Fact]
    public void R9_PluginHarnessNet48_RecordingTransportIsAllowed()
    {
        var project = Product("PluginHarness", "net48", "net48") with
        {
            Sources = new[] { Source("Transport.cs", "using System.Net.Http; internal class Transport { HttpClient client; }") }
        };
        Assert.DoesNotContain(ArchitectureRules.ValidateSource(Graph(project)),
            item => item.Rule == "R9HttpClientProject");
    }

    /// <summary>
    /// 公開 Plugin 的介面別名、間接繼承及 Workflow 基底均依真正 SDK metadata 辨識。
    /// 正反例只在 Roslyn 中編譯來源模型，不執行入口；同命名空間的偽造介面仍須拒絕。
    /// </summary>
    [Fact]
    public void R2_SdkAliasesAndInheritedEntries_AreBoundToRealContracts()
    {
        var project = Product("SpeechMessage.Dynamics.Plugins", "net462", "net462") with
        {
            Sources = new[]
            {
                Source("Entries.cs", """
                    using Contract = Microsoft.Xrm.Sdk.IPlugin;
                    public abstract class BasePlugin : Contract { public abstract void Execute(System.IServiceProvider provider); }
                    public sealed class DerivedPlugin : BasePlugin { public override void Execute(System.IServiceProvider provider) {} }
                    public abstract class WorkflowEntry : System.Activities.CodeActivity { }
                    """)
            }
        };
        Assert.Empty(ArchitectureRules.ValidateSource(Graph(project)));
        var fake = project with
        {
            Sources = new[]
            {
                Source("Fake.cs", "namespace Microsoft.Xrm.Sdk { public interface IPlugin {} } public class Fake : Microsoft.Xrm.Sdk.IPlugin {}")
            }
        };
        Assert.Contains(ArchitectureRules.ValidateSource(Graph(fake)), item => item.Rule == "R2PluginPublicType" && item.Dependency == "Fake");
    }

    /// <summary>HttpClient 的別名及完整型別名稱不可繞過，內部同名型別則不是 framework 傳輸；測試不建立 client。</summary>
    [Fact]
    public void R9_HttpClientAlias_IsRejectedButLocalNamesArePreserved()
    {
        var project = Product("ToolUtility.Core", "net462", "net462;net10.0") with
        {
            Sources = new[] { Source("Alias.cs", "using Web = System.Net.Http.HttpClient; internal class Client { Web value; }") }
        };
        Assert.Contains(ArchitectureRules.ValidateSource(Graph(project)), item => item.Rule == "R9HttpClientProject");
        Assert.Empty(ArchitectureRules.ValidateSource(Graph(project with
        {
            Sources = new[] { Source("Local.cs", "internal class HttpClient {} internal class Local { HttpClient value; }") }
        })));
    }

    /// <summary>R9 必須沿目標相依圖檢查普通／傳遞參考，不能只看 Plugin 本身套件或來源；循環亦須有界結束。</summary>
    [Fact]
    public void R9_TransitiveAndAssemblyReferences_CannotBypassEmptySourceCheck()
    {
        var plugin = Product("SpeechMessage.Dynamics.Plugins", "net462", "net462", "ToolUtility.Core");
        var core = Product("ToolUtility.Core", "net462", "net462;net10.0", "SpeechMessage.Dynamics.Plugins") with
        {
            AssemblyReferences = new[] { "System.Drawing" },
            RestoredDependencies = new[] { "SpeechMessage.Payments" }
        };
        var violations = ArchitectureRules.ValidateSource(Graph(plugin, core));
        Assert.Single(violations, item => item.Rule == "R9PluginPaymentsReference");
        Assert.Single(violations, item => item.Rule == "R9PluginSystemDrawingReference");
    }

    /// <summary>
    /// 逐組態以同一份方案快照驗證完整參考圖與來源均零違規；舊清冊只保留已解決的空紀錄，
    /// 不再接受任何清冊豁免。反例是新增任意架構違規、回填清冊或將狀態退回unresolved，任一即失敗。
    /// 來源、metadata與子程序由當次讀取器有界持有並釋放，不執行產品或連線外部服務。
    /// </summary>
    /// <param name="configuration">分別驗證的實際建置組態。</param>
    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public void Solution_EvaluatedGraphAndSource_MatchContractsAndUnresolvedInventory(string configuration)
    {
        var projects = ArchitectureProjectReader.ReadSolution(configuration);
        Assert.NotEmpty(projects);
        Assert.Empty(ArchitectureRules.Validate(projects));
        Assert.Empty(ArchitectureRules.ValidateSource(projects));
        var path = Path.Combine(ArchitectureProjectReader.FindRoot(),
            "SpeechMessage.Hosting.Tests/Architecture/unresolved-r2-inventory.json");
        using var inventory = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("resolved", inventory.RootElement.GetProperty("status").GetString());
        Assert.Empty(inventory.RootElement.GetProperty("entries").EnumerateArray());
    }

    /// <summary>清冊的狀態與空陣列同時防回歸，任一新項目或unresolved都必須拒絕；只讀受版控檔並立即釋放JSON。</summary>
    [Fact]
    public void R2_Inventory_IsEmptyAndResolved()
    {
        var path = Path.Combine(ArchitectureProjectReader.FindRoot(),
            "SpeechMessage.Hosting.Tests/Architecture/unresolved-r2-inventory.json");
        using var inventory = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("resolved", inventory.RootElement.GetProperty("status").GetString());
        Assert.Empty(inventory.RootElement.GetProperty("entries").EnumerateArray());
    }

    /// <summary>
    /// 逐組態以真正Roslyn語意規則檢查ChurchReport所有對外可見型別；一般公開服務即使有舊清冊也不得放行。
    /// 錯誤列出不合格完整型別名稱；讀取器擁有有界子程序與metadata，測試不啟動Host或外部連線。
    /// </summary>
    /// <param name="configuration">Debug與Release分別驗證，不能互相替代。</param>
    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public void R2_ChurchReport_PublicTypes_AreOnlyEntryControllerOrSignatureRequired(string configuration)
    {
        var projects = ArchitectureProjectReader.ReadSolution(configuration);
        var host = Assert.Single(projects, project => project.Name == "SpeechMessageProducts.ChurchReport");
        Assert.NotEmpty(host.Sources);
        var violations = ArchitectureRules.ValidateSource(projects)
            .Where(item => item.Project == host.Name && item.Rule == "R2HostPublicType").ToArray();
        Assert.True(violations.Length == 0, "Unexpected public host types: "
            + string.Join(", ", violations.Select(item => item.Dependency)));
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

    /// <summary>
    /// P4-02 允許付款核心向下使用 Localization 的純值 Money 契約；以合成圖直接斷言沒有架構違規，
    /// 不載入產品或建立 I/O、網路及跨案例狀態。
    /// </summary>
    [Fact]
    public void Payments_ToLocalization_IsAllowed()
    {
        var violations = ArchitectureRules.Validate(Graph(
            Product("SpeechMessage.Payments", "net10.0", "net10.0", "SpeechMessage.Localization"),
            Product("SpeechMessage.Localization", "net10.0", "net462;net10.0")));

        Assert.Empty(violations);
    }

    /// <summary>
    /// P4-02 不放寬相依方向；Localization 反向參考付款核心必須由同一層級規則拒絕。
    /// 合成圖只在本案例生命週期內使用，決定性斷言確認違規節點與 LayerDirection 規則。
    /// </summary>
    [Fact]
    public void Localization_ToPayments_IsRejected()
    {
        var violations = ArchitectureRules.Validate(Graph(
            Product("SpeechMessage.Localization", "net10.0", "net462;net10.0", "SpeechMessage.Payments"),
            Product("SpeechMessage.Payments", "net10.0", "net462;net10.0")));

        Assert.Contains(violations, item =>
            item.Rule == "LayerDirection" &&
            item.Project == "SpeechMessage.Localization" &&
            item.Dependency == "SpeechMessage.Payments");
    }

    /// <summary>
    /// F1 的依賴方向契約禁止共用專案直接參考產品組件；已分類的產品與部署工具不會
    /// 被誤認為共用層，測試專案仍可驗證產品。合成圖只持有本案例純值，不建立外部資源。
    /// </summary>
    [Fact]
    public void SharedProject_ReferenceToProduct_IsRejected()
    {
        var product = Product("SpeechMessageProducts.GoodShepherd.Core", "net10.0", "net10.0");
        var shared = Product("LineMessagingProcessor.AspNetCore", "net10.0", "net10.0", product.Name);
        var productHosting = Product("SpeechMessageProducts.GoodShepherd.Hosting", "net10.0", "net10.0", product.Name);
        var test = Product("LineMessagingProcessor.AspNetCore.Tests", "net10.0", "net10.0", product.Name) with
        {
            IsTest = true
        };

        Assert.Contains(ArchitectureRules.Validate(Graph(shared, product)), item =>
            item.Rule == "SharedToProduct" && item.Project == shared.Name && item.Dependency == product.Name);
        Assert.DoesNotContain(ArchitectureRules.Validate(Graph(productHosting, product)), item =>
            item.Rule == "SharedToProduct");
        Assert.DoesNotContain(ArchitectureRules.Validate(Graph(test, product)), item =>
            item.Rule == "SharedToProduct");
    }

    /// <summary>
    /// 依既有 DAG 精確排除 Dynamics Plugin／Workflow 產品元件及 Deployment 工具；
    /// 未知產品名稱即使採產品命名也仍為未知共用來源，參考已知產品時必須同時報
    /// SharedToProduct 與 UnknownProject。所有節點為案例區域純值，不載入或執行組件。
    /// </summary>
    [Fact]
    public void SharedToProduct_UsesDagClassificationAndRejectsUnknownSources()
    {
        var core = Product("SpeechMessageProducts.GoodShepherd.Core", "net462", "net462;net10.0");
        var plugin = Product("SpeechMessage.Dynamics.Plugins", "net462", "net462", core.Name);
        var workflow = Product("SpeechMessage.Dynamics.Workflows", "net462", "net462", core.Name);
        var deployment = Product("SpeechMessage.Dynamics.Deployment", "net10.0", "net10.0", core.Name);
        var unknown = Product("SpeechMessageProducts.Unclassified", "net10.0", "net10.0", core.Name);

        Assert.DoesNotContain(ArchitectureRules.Validate(Graph(plugin, core)), item =>
            item.Rule == "SharedToProduct");
        Assert.DoesNotContain(ArchitectureRules.Validate(Graph(workflow, core)), item =>
            item.Rule == "SharedToProduct");
        Assert.DoesNotContain(ArchitectureRules.Validate(Graph(deployment, core)), item =>
            item.Rule == "SharedToProduct");
        Assert.Contains(ArchitectureRules.Validate(Graph(unknown, core)), item =>
            item.Rule == "SharedToProduct" && item.Project == unknown.Name && item.Dependency == core.Name);
        Assert.Contains(ArchitectureRules.Validate(Graph(unknown, core)), item =>
            item.Rule == "UnknownProject" && item.Project == unknown.Name);
    }

    /// <summary>
    /// F1 以真正 Release 方案評估出的 ProjectReference 驗證非產品共用層沒有依賴產品層，
    /// 並確認同一規則引擎未回報違規。診斷會列出精確的共用專案與產品邊；MSBuild
    /// 評估及來源快照由讀取器有界建立並釋放，測試不載入產品、不讀秘密或連線服務。
    /// </summary>
    [Fact]
    public void Solution_SharedProjects_DoNotReferenceProducts()
    {
        var projects = ArchitectureProjectReader.ReadSolution("Release");
        var invalidEdges = ArchitectureRules.Validate(projects)
            .Where(item => item.Rule == "SharedToProduct")
            .Select(item => $"{item.Project} -> {item.Dependency}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(edge => edge, StringComparer.Ordinal)
            .ToArray();

        Assert.True(invalidEdges.Length == 0,
            "Shared projects must not reference products: " + string.Join(", ", invalidEdges));
    }

    /// <summary>建立單一不可變的合成評估節點，陣列僅屬此案例，不持有 request 或 SDK 型別。</summary>
    private static ArchitectureProject Product(string name, string target, string targets, params string[] references) =>
        new(name, target, targets.Split(';'), references, Array.Empty<string>(), false)
        {
            ContractMetadataPaths = name is "SpeechMessage.Dynamics.Plugins" or "SpeechMessage.Dynamics.Workflows" ?
                ArchitectureProjectReader.ReadPluginContractMetadata() : Array.Empty<string>()
        };

    /// <summary>建立不持有檔案、程序或使用者資料的合成來源快照。</summary>
    private static ArchitectureSource Source(string path, string text) => new(path, text);

    /// <summary>將案例節點組成 operation-local 圖，不跨案例快取。</summary>
    private static ArchitectureProject[] Graph(params ArchitectureProject[] projects) => projects;
}

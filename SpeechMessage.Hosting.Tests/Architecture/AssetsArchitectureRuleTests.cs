/*
 * 檔案整體說明
 *
 * 目的與定位：在線 A 的共用架構測試宿主驗證 SpeechMessage.Assets 精確歸入第 50 層，
 * 讓線 B 的 F7 公開圖片模組沿既有 Scheduling 層序被產品組合根消費；不實作或啟動圖片產品。
 * 主要流程：以每案獨立的 ArchitectureProject 純值圖呼叫真正 ArchitectureRules，
 * 正例驗向下使用 Hosting／Localization 及產品向下組合；反例注入反向、同儕、產品、
 * 近似名稱、未標測試及框架錯配，逐項核對診斷數量、規則與完整參考身分。
 * 公開邊界：另以合成來源交給真正 Roslyn 掃描，確認共用公開介面不授予主機一般服務公開豁免。
 * 資源與隔離：不讀線 B 原始碼、秘密、真實組織或方案圖，不建 SDK、網路、排程或背景工作。
 * 圖與來源僅存活當次測試；語意掃描的唯讀 runtime metadata 由 ArchitectureRules 同步 Dispose。
 * 驗證方式：只定向執行此類別及既有 Scheduling／F3 合成案例；離線通過不代表 F7 或實機驗收。
 */
using System;
using Xunit;

namespace SpeechMessage.Architecture.Tests;

/// <summary>
/// 以真實規則及獨立合成輸入保護 Assets 的精確共用層分類；
/// 失敗必須來自對外可見的診斷，不靠反射讀私有 Layer 或比對實作文字。
/// </summary>
public sealed class AssetsArchitectureRuleTests
{
    /// <summary>唯一核定的產品無關圖片模組名稱；近似名稱不由此常數推導為合法。</summary>
    private const string AssetsName = "SpeechMessage.Assets";

    /// <summary>
    /// 漏登 Assets 或錯放到基礎層會讓合法向下參考產生診斷；
    /// 空結果同時確認沒有新增產品、主機或未知專案身分，案例不建立任何外部依賴。
    /// </summary>
    [Fact]
    public void Assets_HostingAndLocalizationDependencies_AreAllowed()
    {
        Assert.Empty(ArchitectureRules.Validate(new[]
        {
            Project(AssetsName, "net10.0", "net10.0", "SpeechMessage.Hosting", "SpeechMessage.Localization"),
            Project("SpeechMessage.Hosting", "net10.0", "net10.0"),
            Project("SpeechMessage.Localization", "net10.0", "net462;net10.0")
        }));
    }

    /// <summary>
    /// 高層產品組合根應可使用共用 Assets；漏登或誤設更高層會使空違規斷言失敗。
    /// 每列只建立兩個純值節點，不讀取產品或線 B 專案。
    /// </summary>
    /// <param name="consumer">已登錄的產品 Hosting 或主機完整名稱。</param>
    [Theory]
    [InlineData("SpeechMessageProducts.GoodShepherd.Hosting")]
    [InlineData("SpeechMessageProducts.ChurchReport")]
    [InlineData("SpeechMessageProducts.LineWebhook")]
    [InlineData("SpeechMessageProducts.GoodShepherd2.Web")]
    public void Assets_ProductConsumers_AreAllowed(string consumer)
    {
        Assert.Empty(ArchitectureRules.Validate(new[]
        {
            Project(consumer, "net10.0", "net10.0", AssetsName),
            Project(AssetsName, "net10.0", "net10.0")
        }));
    }

    /// <summary>
    /// Hosting 及雙目標 Localization 不得反向引用 Assets；
    /// 完整診斷集合排除未知專案失敗掩蓋 LayerDirection，雙目標另須拒絕不相容參考。
    /// </summary>
    /// <param name="foundation">注入反向邊的已分類基礎專案。</param>
    /// <param name="target">該節點本次評估的目標。</param>
    /// <param name="targets">該基礎專案的完整宣告目標。</param>
    [Theory]
    [InlineData("SpeechMessage.Hosting", "net10.0", "net10.0")]
    [InlineData("SpeechMessage.Localization", "net10.0", "net462;net10.0")]
    [InlineData("SpeechMessage.Localization", "net462", "net462;net10.0")]
    public void Assets_FoundationReverseReferences_AreRejected(string foundation, string target, string targets)
    {
        var violations = ArchitectureRules.Validate(new[]
        {
            Project(foundation, target, targets, AssetsName),
            Project(AssetsName, "net10.0", "net10.0")
        });

        Assert.Equal(foundation == "SpeechMessage.Localization" ? 2 : 1, violations.Count);
        Assert.All(violations, item =>
        {
            Assert.Equal(foundation, item.Project);
            Assert.Equal(target, item.Target);
            Assert.Equal(AssetsName, item.Dependency);
        });
        Assert.Contains(violations, item => item.Rule == "LayerDirection");
        Assert.Equal(foundation == "SpeechMessage.Localization",
            violations.Any(item => item.Rule == "DualTargetReference"));
    }

    /// <summary>
    /// Assets 與 Scheduling 及所有既有 adapter 同層，任一方向都應只產生 LayerDirection；
    /// 這會抓出誤放到相鄰層或新增同儕豁免，不以未知名稱拒絕冒充正確層序。
    /// </summary>
    /// <param name="peer">既有第 50 層同儕的完整名稱。</param>
    /// <param name="reverse">是否將邊反轉為同儕引用 Assets。</param>
    [Theory]
    [InlineData("SpeechMessage.Scheduling", false)]
    [InlineData("SpeechMessage.Scheduling", true)]
    [InlineData("SpeechMessage.Access", false)]
    [InlineData("SpeechMessage.Access", true)]
    [InlineData("LineMessagingProcessor.AspNetCore", false)]
    [InlineData("LineMessagingProcessor.AspNetCore", true)]
    [InlineData("SpeechMessage.Payments.AspNetCore", false)]
    [InlineData("SpeechMessage.Payments.AspNetCore", true)]
    [InlineData("SpeechMessage.Liff.AspNetCore", false)]
    [InlineData("SpeechMessage.Liff.AspNetCore", true)]
    public void Assets_PeerReferences_AreRejected(string peer, bool reverse)
    {
        var source = reverse ? peer : AssetsName;
        var dependency = reverse ? AssetsName : peer;
        var violation = Assert.Single(ArchitectureRules.Validate(new[]
        {
            Project(source, "net10.0", "net10.0", dependency),
            Project(dependency, "net10.0", "net10.0")
        }));

        Assert.Equal("LayerDirection", violation.Rule);
        Assert.Equal(source, violation.Project);
        Assert.Equal("net10.0", violation.Target);
        Assert.Equal(dependency, violation.Dependency);
    }

    /// <summary>
    /// 共用 Assets 不得取得產品依賴，即使產品核心位於下層也須獨立觸發 SharedToProduct；
    /// 高層產品另須觸發 LayerDirection，精確數量防止漏登或誤分產品身分掩蓋規則。
    /// </summary>
    /// <param name="product">刻意注入的既有產品專案。</param>
    /// <param name="targets">保留該產品的單目標或雙目標形狀。</param>
    /// <param name="upward">產品是否位於共用模組之上。</param>
    [Theory]
    [InlineData("SpeechMessageProducts.GoodShepherd.Core", "net462;net10.0", false)]
    [InlineData("SpeechMessageProducts.GoodShepherd.Hosting", "net10.0", true)]
    [InlineData("SpeechMessageProducts.ChurchReport", "net10.0", true)]
    [InlineData("SpeechMessageProducts.LineWebhook", "net10.0", true)]
    [InlineData("SpeechMessageProducts.GoodShepherd2.Web", "net10.0", true)]
    public void Assets_ReferenceToProduct_IsRejected(string product, string targets, bool upward)
    {
        var violations = ArchitectureRules.Validate(new[]
        {
            Project(AssetsName, "net10.0", "net10.0", product),
            Project(product, "net10.0", targets)
        });

        Assert.Equal(upward ? 2 : 1, violations.Count);
        Assert.All(violations, item =>
        {
            Assert.Equal(AssetsName, item.Project);
            Assert.Equal(product, item.Dependency);
            Assert.Equal("net10.0", item.Target);
        });
        Assert.Contains(violations, item => item.Rule == "SharedToProduct");
        Assert.Equal(upward, violations.Any(item => item.Rule == "LayerDirection"));
    }

    /// <summary>
    /// 注入近似名稱、大小寫變體及沒有測試標記的 Tests 後綴，
    /// 必須同時保留 UnknownProject 與 SharedToProduct；不能用前綴放寬精確白名單。
    /// </summary>
    /// <param name="name">不在核定分類內的合成名稱。</param>
    [Theory]
    [InlineData("SpeechMessage.Assets.Other")]
    [InlineData("SpeechMessage.AssetsPretender")]
    [InlineData("SpeechMessage.access")]
    [InlineData("Pretend.SpeechMessage.Assets")]
    [InlineData("SpeechMessage.Assets.Tests")]
    [InlineData("Unclassified.Assets")]
    public void Assets_UnknownOrSimilarNames_AreRejected(string name)
    {
        const string product = "SpeechMessageProducts.GoodShepherd.Core";
        var violations = ArchitectureRules.Validate(new[]
        {
            Project(name, "net10.0", "net10.0", product),
            Project(product, "net10.0", "net462;net10.0")
        });

        Assert.Equal(2, violations.Count);
        Assert.All(violations, item => Assert.Equal(name, item.Project));
        Assert.Contains(violations, item => item.Rule == "UnknownProject" && item.Dependency == name);
        Assert.Contains(violations, item => item.Rule == "SharedToProduct" && item.Dependency == product);
    }

    /// <summary>
    /// 真正 IsTest 節點可測產品，取消同一節點的標記則恢復未知及產品依賴拒絕；
    /// 不以 Tests 名稱或 Assets 分類新增隱性測試豁免，資料均為案例區域純值。
    /// </summary>
    /// <param name="name">測試後綴及任意名稱兩種未分類節點。</param>
    [Theory]
    [InlineData("SpeechMessage.Assets.Tests")]
    [InlineData("Unclassified.AssetsProbe")]
    public void Assets_TestClassification_RequiresExplicitTestMarker(string name)
    {
        var product = Project("SpeechMessageProducts.GoodShepherd.Core", "net10.0", "net462;net10.0");
        var access = Project(AssetsName, "net10.0", "net10.0");
        var test = Project(name, "net10.0", "net10.0", AssetsName, product.Name) with { IsTest = true };

        Assert.Empty(ArchitectureRules.Validate(new[] { test, access, product }));
        var violations = ArchitectureRules.Validate(new[] { test with { IsTest = false }, access, product });
        Assert.Equal(2, violations.Count);
        Assert.Contains(violations, item => item.Rule == "UnknownProject" && item.Project == name);
        Assert.Contains(violations, item => item.Rule == "SharedToProduct" && item.Project == name && item.Dependency == product.Name);
    }

    /// <summary>
    /// 雙目標核心在兩個實際目標都不能取得只有 net10 的 Assets；
    /// 必須精確回報 DualTargetReference 及 LayerDirection，不能因 net10 評估而放行。
    /// </summary>
    /// <param name="target">雙目標核心當次被評估的目標。</param>
    [Theory]
    [InlineData("net462")]
    [InlineData("net10.0")]
    public void Assets_DualTargetConsumers_AreRejected(string target)
    {
        var violations = ArchitectureRules.Validate(new[]
        {
            Project("ToolUtility.Core", target, "net462;net10.0", AssetsName),
            Project(AssetsName, "net10.0", "net10.0")
        });

        Assert.Equal(2, violations.Count);
        Assert.All(violations, item =>
        {
            Assert.Equal("ToolUtility.Core", item.Project);
            Assert.Equal(target, item.Target);
            Assert.Equal(AssetsName, item.Dependency);
        });
        Assert.Contains(violations, item => item.Rule == "DualTargetReference");
        Assert.Contains(violations, item => item.Rule == "LayerDirection");
    }

    /// <summary>
    /// 精確 Assets 分類不豁免原有框架白名單；注入 net48 或 net8 應只有 ProductTarget，
    /// 不以未知專案失敗代替目標規則，也不編譯或執行這些合成框架。
    /// </summary>
    /// <param name="target">既有產品守門未核准的框架。</param>
    [Theory]
    [InlineData("net48")]
    [InlineData("net8.0")]
    public void Assets_UnapprovedTargets_AreRejected(string target)
    {
        var violation = Assert.Single(ArchitectureRules.Validate(new[] { Project(AssetsName, target, target) }));

        Assert.Equal("ProductTarget", violation.Rule);
        Assert.Equal(AssetsName, violation.Project);
        Assert.Equal(target, violation.Target);
        Assert.Equal(target, violation.Dependency);
    }

    /// <summary>
    /// Assets 可提供共用公開介面，引用它的主機內一般公開服務仍須由 R2 拒絕；
    /// 合成同命名空間服務確認分類不會擴張公開面，metadata 由真正掃描器同步清理。
    /// </summary>
    /// <param name="host">沿用原有公開外殼限制的精確主機。</param>
    [Theory]
    [InlineData("SpeechMessageProducts.ChurchReport")]
    [InlineData("SpeechMessageProducts.LineWebhook")]
    [InlineData("SpeechMessageProducts.GoodShepherd2.Web")]
    public void Assets_PublicContracts_DoNotExemptHostTypes(string host)
    {
        var access = Project(AssetsName, "net10.0", "net10.0") with
        {
            Sources = new[] { new ArchitectureSource("Contract.cs", "namespace SpeechMessage.Assets; public interface IPublicAssetStore {}") }
        };
        Assert.Empty(ArchitectureRules.ValidateSource(new[] { access }));
        var project = Project(host, "net10.0", "net10.0", AssetsName) with
        {
            Sources = new[] { new ArchitectureSource("Policy.cs", "namespace SpeechMessage.Assets; public sealed class PublicAssetStore {}") }
        };
        var violation = Assert.Single(ArchitectureRules.ValidateSource(new[] { project, access }));

        Assert.Equal("R2HostPublicType", violation.Rule);
        Assert.Equal(host, violation.Project);
        Assert.Equal("SpeechMessage.Assets.PublicAssetStore", violation.Dependency);
    }

    /// <summary>
    /// 建立當次案例擁有的純值圖節點，保留完整目標集合及參考身分；
    /// 不讀線 B 專案、不載入產品 DLL，也不保存可跨測試變動的狀態。
    /// </summary>
    /// <param name="name">完整且區分大小寫的合成專案名稱。</param>
    /// <param name="target">本節點評估目標。</param>
    /// <param name="targets">以分號分隔的完整宣告目標。</param>
    /// <param name="references">精確的直接專案參考。</param>
    /// <returns>只由本次測試持有的不可變描述。</returns>
    private static ArchitectureProject Project(string name, string target, string targets, params string[] references) =>
        new(name, target, targets.Split(';'), references, Array.Empty<string>(), false);
}

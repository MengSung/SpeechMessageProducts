using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace SpeechMessage.Architecture.Tests;

/// <summary>
/// 使用真實 csproj XML 的故意違規範例驗證 R4，不把合成模型當成 MSBuild 解析證據。
/// 範例不還原、不建置、不載入 DLL；每個案例擁有自己的暫存目錄，子程序退出後確定刪除。
/// </summary>
public sealed class ArchitectureXmlTests
{
    /// <summary>每條正式規則都有能被實際 XML 評估偵測的反例，移除違規參考後則必須通過。</summary>
    /// <param name="source">違規來源專案。</param><param name="targets">來源宣告目標。</param>
    /// <param name="dependency">參考專案。</param><param name="dependencyTargets">相依宣告目標。</param><param name="rule">必須攔截的規則。</param>
    [Theory]
    [InlineData("SpeechMessage.Localization", "net462;net10.0", "ToolUtility", "net10.0", "DualTargetReference")]
    [InlineData("SpeechMessage.Dynamics.Plugins", "net462", "SpeechMessage.Localization", "net48;net10.0", "SandboxReference")]
    [InlineData("Line.Messaging", "net10.0", "LineMessagingProcessor", "net10.0", "LayerDirection")]
    [InlineData("SpeechMessageProducts.ChurchReport", "net10.0", "SpeechMessageProducts.LineWebhook", "net10.0", "HostReference")]
    public void InvalidCsprojReference_IsRejected(string source, string targets, string dependency, string dependencyTargets, string rule)
    {
        using var fixture = new ProjectFixture();
        fixture.Add(source, Project(targets, $"<ProjectReference Include=\"{dependency}.csproj\" />"));
        fixture.Add(dependency, Project(dependencyTargets, string.Empty));
        var evaluated = fixture.Read();
        Assert.NotEmpty(evaluated);
        Assert.Contains(ArchitectureRules.Validate(evaluated), item => item.Rule == rule);
        fixture.Replace(source, Project(targets, string.Empty));
        var repaired = fixture.Read().Where(item => item.Name == source).ToArray();
        Assert.NotEmpty(repaired);
        Assert.Empty(ArchitectureRules.Validate(repaired));
    }

    /// <summary>條件式套件只在 net462 啟用仍必須拒絕；net10 例外不能掩蓋另一個 inner build。</summary>
    /// <param name="package">三種不可進入雙目標純核心的主機套件。</param>
    [Theory]
    [InlineData("Microsoft.Extensions.Logging")]
    [InlineData("Microsoft.AspNetCore.Http")]
    [InlineData("Microsoft.PowerPlatform.Dataverse.Client")]
    public void InvalidCsprojConditionalPackage_IsRejected(string package)
    {
        using var fixture = new ProjectFixture();
        var reference = $"<PackageReference Include=\"{package}\" Condition=\"'$(TargetFramework)' == 'net462'\" />";
        fixture.Add("SpeechMessage.Localization", Project("net462;net10.0", reference));
        var nodes = fixture.Read();
        Assert.Equal(2, nodes.Count);
        Assert.Contains(ArchitectureRules.Validate(nodes), item => item.Rule == "DualTargetPackage" && item.Target == "net462");
        Assert.Empty(ArchitectureRules.Validate(nodes.Where(item => item.Target == "net10.0").ToArray()));
    }

    /// <summary>沙箱自身 net48 的 XML 不得冒充 net462 產品；測試宿主的豁免不會套到產品名稱。</summary>
    [Fact]
    public void InvalidCsprojSandboxTarget_IsRejected()
    {
        using var fixture = new ProjectFixture();
        fixture.Add("SpeechMessage.Dynamics.Workflows", Project("net48", string.Empty));
        Assert.Contains(ArchitectureRules.Validate(fixture.Read()), item => item.Rule == "SandboxTarget");
        fixture.Replace("SpeechMessage.Dynamics.Workflows", Project("net462", string.Empty));
        Assert.Empty(ArchitectureRules.Validate(fixture.Read()));
    }

    /// <summary>即使 csproj 宣告雙目標，NuGet 若實際選出 net48 產品也必須拒絕；正常 net462 與套件節點不誤擋。</summary>
    [Fact]
    public void RestoredAssets_Net48Product_IsRejected()
    {
        using var invalid = JsonDocument.Parse("{\"Core/1.0.0\":{\"type\":\"project\",\"framework\":\".NETFramework,Version=v4.8\"}}");
        Assert.Equal("Core", Assert.Single(ArchitectureRules.ValidateFrameworkAssets(invalid.RootElement)));
        using var valid = JsonDocument.Parse("{\"Core/1.0.0\":{\"type\":\"project\",\"framework\":\".NETFramework,Version=v4.6.2\"},\"Package/1.0\":{\"type\":\"package\"}}");
        Assert.Empty(ArchitectureRules.ValidateFrameworkAssets(valid.RootElement));
    }

    /// <summary>只組成受信任固定範例；Condition 由真正 MSBuild 解讀，沒有自製條件語言。</summary>
    private static string Project(string targets, string items) =>
        $"<Project><PropertyGroup><TargetFrameworks>{targets}</TargetFrameworks><IsTestProject>false</IsTestProject></PropertyGroup><ItemGroup>{items}</ItemGroup></Project>";

    /// <summary>單一案例擁有其唯一暫存根目錄及固定清單，沒有 timer、網路、秘密或共享可變資源。</summary>
    private sealed class ProjectFixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "speechmessage-r4-" + Guid.NewGuid().ToString("N"));
        private readonly List<string> files = new();

        /// <summary>建構即建立本案例專屬目錄，所有後續檔案只能位於此目錄直接子層。</summary>
        internal ProjectFixture() => Directory.CreateDirectory(root);

        /// <summary>新增具明確專案名稱的 XML；不執行任意 target 或使用者字串。</summary>
        internal void Add(string name, string xml)
        {
            var path = Path.Combine(root, name + ".csproj");
            File.WriteAllText(path, xml, new UTF8Encoding(false));
            files.Add(path);
        }

        /// <summary>只替換已由本案例建立的單一範例，讓同條規則同時有正反例。</summary>
        internal void Replace(string name, string xml) => File.WriteAllText(files.Single(path => Path.GetFileNameWithoutExtension(path) == name), xml, new UTF8Encoding(false));

        /// <summary>以完整工作範圍及顯式清單評估，不掃描系統其他專案。</summary>
        internal IReadOnlyList<ArchitectureProject> Read() => ArchitectureProjectReader.ReadProjects(root, files);

        /// <summary>MSBuild 只評估、不產生 obj；逐一刪除自己建立的檔後刪空目錄，不遞迴刪除任意路徑。</summary>
        public void Dispose()
        {
            foreach (var path in files) File.Delete(path);
            Directory.Delete(root);
        }
    }
}

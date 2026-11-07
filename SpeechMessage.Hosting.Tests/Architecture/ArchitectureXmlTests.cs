/*
 * 檔案整體說明
 *
 * 目的與定位：使用真實 csproj XML 的故意違規範例驗證 R4，不把合成模型當成 MSBuild 解析證據。 範例不還原、不建置、不載入 DLL；每個案例擁有自己的暫存目錄，子程序退出後確定刪除。
 *
 * 主要功能與流程：
 * 本檔宣告 ArchitectureXmlTests、ProjectFixture。
 * - 每條正式規則都有能被實際 XML 評估偵測的反例，移除違規參考後則必須通過。
 * - 條件式套件只在 net462 啟用仍必須拒絕；net10 例外不能掩蓋另一個 inner build。
 * - 沙箱自身 net48 的 XML 不得冒充 net462 產品；測試宿主的豁免不會套到產品名稱。
 * - 沒有沙箱消費者的雙目標產品也不能把 net48 當產品目標，防止只靠被參考關係才攔截的死角。
 * 測試案例入口包含 InvalidCsprojReference_IsRejected、InvalidCsprojConditionalPackage_IsRejected、
 * InvalidCsprojSandboxTarget_IsRejected、InvalidCsprojUnreferencedProductTarget_IsRejected、
 * RestoredAssets_Net48Product_IsRejected、Project、Add、Replace；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
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

    /// <summary>沒有沙箱消費者的雙目標產品也不能把 net48 當產品目標，防止只靠被參考關係才攔截的死角。</summary>
    [Fact]
    public void InvalidCsprojUnreferencedProductTarget_IsRejected()
    {
        using var fixture = new ProjectFixture();
        fixture.Add("SpeechMessage.Localization", Project("net48;net10.0", string.Empty));
        Assert.Contains(ArchitectureRules.Validate(fixture.Read()), item => item.Rule == "ProductTarget" && item.Target == "net48");
        fixture.Replace("SpeechMessage.Localization", Project("net462;net10.0", string.Empty));
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

    /// <summary>
    /// 真正 MSBuild 評估應依 Debug／Release 分別解析 Compile、普通組件及條件常數。
    /// 兩檔來源由本案例建立並確定刪除；不執行建置、載入產品或連接外部系統。
    /// </summary>
    [Fact]
    public void ConfigurationSpecificSourceAndAssembly_AreEvaluatedIndependently()
    {
        using var fixture = new ProjectFixture();
        fixture.AddSource("Debug.cs", "internal class DebugOnly {}");
        fixture.AddSource("Release.cs", "internal class ReleaseOnly {}");
        fixture.Add("ToolUtility.Core", """
            <Project><PropertyGroup><TargetFramework>net462</TargetFramework><IsTestProject>false</IsTestProject>
            <DefineConstants Condition="'$(Configuration)' == 'Debug'">DEBUG;TRACE</DefineConstants></PropertyGroup>
            <ItemGroup><Compile Include="Debug.cs" Condition="'$(Configuration)' == 'Debug'" />
            <Compile Include="Release.cs" Condition="'$(Configuration)' == 'Release'" />
            <Reference Include="System.Drawing, Version=4.0.0.0" Condition="'$(Configuration)' == 'Debug'" /></ItemGroup></Project>
            """);
        var debug = Assert.Single(fixture.Read("Debug"));
        var release = Assert.Single(fixture.Read("Release"));
        Assert.Equal("Debug.cs", Assert.Single(debug.Sources).Path);
        Assert.Equal("Release.cs", Assert.Single(release.Sources).Path);
        Assert.Contains("System.Drawing", debug.AssemblyReferences);
        Assert.Empty(release.AssemblyReferences);
        Assert.Contains("DEBUG", debug.PreprocessorSymbols);
        Assert.Empty(release.PreprocessorSymbols);
        Assert.Equal("Debug", debug.Configuration);
    }

    /// <summary>來源超過十六 MiB 時必須在解析與字串配置前拒絕；稀疏檔由 fixture 獨占，退出確定刪除。</summary>
    [Fact]
    public void OversizedSource_IsRejectedBeforeParsing()
    {
        using var fixture = new ProjectFixture();
        fixture.AddSource("Large.cs", string.Empty);
        fixture.ResizeSource("Large.cs", 16L * 1024 * 1024 + 1);
        fixture.Add("ToolUtility.Core", Project("net462", "<Compile Include=\"Large.cs\" />"));
        var error = Assert.Throws<InvalidDataException>(() => fixture.Read());
        Assert.Contains("size budget", error.Message, StringComparison.Ordinal);
    }

    /// <summary>不合法 UTF-8 不得以 replacement fallback 進入語法樹；錯誤由 strict decoder 決定性拒絕。</summary>
    [Fact]
    public void InvalidUtf8Source_IsRejectedWithoutReplacementFallback()
    {
        using var fixture = new ProjectFixture();
        fixture.AddSource("Invalid.cs", string.Empty);
        fixture.WriteSourceBytes("Invalid.cs", new byte[] { 0xc3, 0x28 });
        fixture.Add("ToolUtility.Core", Project("net462", "<Compile Include=\"Invalid.cs\" />"));
        Assert.Throws<DecoderFallbackException>(() => fixture.Read());
    }

    /// <summary>
    /// Framework SDK 自帶的隱含可用組件不代表產品宣告或產物實際依賴；明確偽標
    /// IsImplicitlyDefined 的普通 Reference 仍必須被看見，不能靠 caller metadata 解除 R9。
    /// </summary>
    [Fact]
    public void SdkImplicitReferenceAvailability_DoesNotHideExplicitDrawingDependency()
    {
        using var fixture = new ProjectFixture();
        fixture.Add("SpeechMessage.Dynamics.Plugins", """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
            <TargetFramework>net462</TargetFramework><IsTestProject>false</IsTestProject>
            </PropertyGroup></Project>
            """);
        Assert.DoesNotContain("System.Drawing", Assert.Single(fixture.Read()).AssemblyReferences);
        fixture.Replace("SpeechMessage.Dynamics.Plugins", Project("net462",
            "<Reference Include=\"System.Drawing\"><IsImplicitlyDefined>true</IsImplicitlyDefined></Reference>"));
        Assert.Contains("System.Drawing", Assert.Single(fixture.Read()).AssemblyReferences);
    }

    /// <summary>
    /// SDK 的 Using 項目最終會產生 global using；只讀 Compile 評估仍須把這份契約交給
    /// 語意分析，不能因尚未執行產生 target 而漏掉以短名稱宣告的 HttpClient。
    /// </summary>
    [Fact]
    public void EvaluatedGlobalUsing_CannotHideHttpClientUsage()
    {
        using var fixture = new ProjectFixture();
        fixture.AddSource("Transport.cs", "internal class Transport { HttpClient client; }");
        fixture.Add("ToolUtility.Core", Project("net462;net10.0",
            "<Compile Include=\"Transport.cs\" /><Using Include=\"System.Net.Http\" />"));
        Assert.Contains(ArchitectureRules.ValidateSource(fixture.Read()),
            item => item.Rule == "R9HttpClientProject");
    }

    /// <summary>
    /// 直接檢查真正已建置 Plugin 的 AssemblyRef，補足 SDK 隱含參考不等於輸出相依的差別。
    /// 只用 PE metadata 讀取，不載入或執行 DLL；檔案與 PEReader 均由案例確定釋放。
    /// </summary>
    /// <param name="configuration">已建置產物的組態；兩種組態都不得引用禁止組件。</param>
    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public void BuiltPlugin_HasNoForbiddenAssemblyReferences(string configuration)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SpeechMessageProducts.sln")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var path = Path.Combine(root.FullName, "SpeechMessage.Dynamics.Plugins/bin", configuration,
            "net462/SpeechMessage.Dynamics.Plugins.dll");
        Assert.True(File.Exists(path), "Build the solution before checking sandbox artifacts.");
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var names = reader.AssemblyReferences.Select(handle =>
            reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain(names, name => name == "System.Drawing" ||
            name.StartsWith("System.Drawing.", StringComparison.Ordinal) ||
            name == "SpeechMessage.Payments" || name.StartsWith("SpeechMessage.Payments.", StringComparison.Ordinal));
    }

    /// <summary>只組成受信任固定範例；Condition 由真正 MSBuild 解讀，沒有自製條件語言。</summary>
    private static string Project(string targets, string items) =>
        $"<Project><PropertyGroup><TargetFrameworks>{targets}</TargetFrameworks><IsTestProject>false</IsTestProject></PropertyGroup><ItemGroup>{items}</ItemGroup></Project>";

    /// <summary>單一案例擁有其唯一暫存根目錄及固定清單，沒有 timer、網路、秘密或共享可變資源。</summary>
    private sealed class ProjectFixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "speechmessage-r4-" + Guid.NewGuid().ToString("N"));
        private readonly List<string> files = new();
        private readonly List<string> sourceFiles = new();

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

        /// <summary>建立案例直接子層來源，另列清理清單，不把來源檔冒充 csproj 傳給 MSBuild。</summary>
        internal void AddSource(string name, string text)
        {
            var path = Path.Combine(root, name);
            File.WriteAllText(path, text, new UTF8Encoding(false));
            sourceFiles.Add(path);
        }

        /// <summary>只擴充本案例已建立的來源以注入大小故障；串流於返回前 Dispose。</summary>
        internal void ResizeSource(string name, long length)
        {
            using var stream = File.OpenWrite(sourceFiles.Single(path => Path.GetFileName(path) == name));
            stream.SetLength(length);
        }

        /// <summary>僅覆寫案例擁有的來源，注入不合法 UTF-8；不修改 repo 或其他測試檔案。</summary>
        internal void WriteSourceBytes(string name, byte[] bytes) =>
            File.WriteAllBytes(sourceFiles.Single(path => Path.GetFileName(path) == name), bytes);

        /// <summary>以完整工作範圍及顯式清單評估，不掃描系統其他專案。</summary>
        internal IReadOnlyList<ArchitectureProject> Read(string configuration = "Release") =>
            ArchitectureProjectReader.ReadProjects(root, files, configuration: configuration);

        /// <summary>MSBuild 只評估、不產生 obj；逐一刪除自己建立的檔後刪空目錄，不遞迴刪除任意路徑。</summary>
        public void Dispose()
        {
            foreach (var path in files.Concat(sourceFiles)) File.Delete(path);
            Directory.Delete(root);
        }
    }
}

/*
 * 檔案整體說明
 *
 * 目的與定位：保護 F3 平台 Plugin 的獨立打包 target 與 PE 入口驗證，
 * 並確認舊 Plugin／Workflow 打包規則仍由原有測試覆蓋且不被此檔放寬。
 * 主要流程：讀取真實 MSBuild 專案與獨立 merge targets，核對精確五個合併輸入、
 * 明確 net462 入口及不含 GoodShepherd.Core 的依賴閉包；以一次性簽章 key
 * 編譯合成 SDK 與 Plugin PE，呼叫 MetadataReader 檢查器進行離線驗證。
 * 故障注入與決定性斷言：未知／近似組件身分、缺失或額外 IPlugin、錯框架、
 * 未簽章、未允許參考、System.Drawing 與 facade 均須由對應既有檢查拒絕。
 * 資源與隔離：每個 Fixture 獨占暫存目錄及隨機測試金鑰，PE 不載入執行；
 * Dispose 關閉所有編譯／讀取資源後刪除目錄，不使用正式簽章金鑰或外部服務。
 */
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Xunit;

namespace SpeechMessage.Tools.PluginPackaging.Tests;

/// <summary>驗證平台部署 DLL 的獨立 target 及精確入口；合成 PE 僅供離線中繼資料測試。</summary>
public sealed class PlatformPackagingContractTests
{
    private const string PlatformAssembly = "SpeechMessage.Dynamics.Platform.Plugins";
    private const string SlotRequestType =
        "SpeechMessage.Dynamics.Platform.Plugins.ChannelPool.SlotRequestPlugin";
    private const string ChannelCreatedType =
        "SpeechMessage.Dynamics.Platform.Plugins.ChannelPool.ChannelCreatedPlugin";

    /// <summary>獨立 target 只合併明列主組件與核心閉包，不能污染舊七輸入 target 或攜入產品Core。</summary>
    [Fact]
    public void PlatformMerge_UsesExactFiveInputCoreClosure()
    {
        var root = FindRepositoryRoot();
        var targetPath = Path.Combine(root, "build", "PlatformPluginMerge.targets");
        Assert.True(File.Exists(targetPath), "F3 必須以獨立 targets 接入，不能改寫舊 PluginMerge.targets。");
        var targets = XDocument.Load(targetPath);
        var inputNames = targets.Descendants("_SpeechMessagePlatformMergeInput")
            .Select(element => (string?)element.Attribute("Include")).ToArray();

        Assert.Equal(5, inputNames.Length);
        Assert.All(inputNames, input => Assert.DoesNotContain("*", input!, StringComparison.Ordinal));
        Assert.Equal("$(TargetPath)", inputNames[0]);
        Assert.Contains(inputNames, value => value == "$(TargetDir)LineMessagingProcessor.Core.dll");
        Assert.Contains(inputNames, value => value == "$(TargetDir)SpeechMessage.Localization.dll");
        Assert.Contains(inputNames, value => value == "$(TargetDir)Newtonsoft.Json.dll");
        Assert.Contains(inputNames, value => value ==
            "$(MSBuildThisFileDirectory)Line.Messaging.PluginSubset/bin/$(Configuration)/net462/Line.Messaging.dll");
        Assert.DoesNotContain(inputNames, value => value!.Contains("GoodShepherd.Core", StringComparison.Ordinal));
        Assert.DoesNotContain(inputNames, value => value!.Contains("ToolUtility", StringComparison.Ordinal));
        Assert.Contains(targets.Descendants("Target"), target =>
            ((string?)target.Attribute("Condition"))?.Contains("'$(SpeechMessagePackagePlugin)' == 'true'",
                StringComparison.Ordinal) == true);
        Assert.Contains(targets.Descendants("Exec"), task =>
            ((string?)task.Attribute("Command"))?.Contains("Line.Messaging.PluginSubset.csproj",
                StringComparison.Ordinal) == true);
        Assert.Contains(targets.Descendants("ILRepack"), merge =>
            (string?)merge.Attribute("Internalize") == "true");
        Assert.Contains(targets.Descendants("ILRepack"), merge =>
            ((string?)merge.Attribute("OutputFile"))?.Contains("artifacts/plugins", StringComparison.Ordinal) == true);
        var deleteOutput = targets.Descendants("Delete").SingleOrDefault(element =>
            ((string?)element.Attribute("Files"))?.Contains("PlatformPluginOutput", StringComparison.Ordinal) == true);
        var mergeTarget = targets.Descendants("Target").Single();
        Assert.NotNull(deleteOutput);
        Assert.True(mergeTarget.Elements().ToList().IndexOf(deleteOutput!) <
            mergeTarget.Elements().ToList().IndexOf(targets.Descendants("ILRepack").Single()));

        var project = XDocument.Load(Path.Combine(root, "SpeechMessage.Dynamics.Platform.Plugins",
            "SpeechMessage.Dynamics.Platform.Plugins.csproj"));
        Assert.Contains(project.Descendants("ILRepackTargetsFile"), element =>
            element.Value.Contains("PlatformPluginMerge.targets", StringComparison.Ordinal));
        Assert.Contains(project.Descendants("ProjectReference"), element =>
            ((string?)element.Attribute("Include"))?.Contains("LineMessagingProcessor.Core", StringComparison.Ordinal) == true);
        Assert.Contains(project.Descendants("ProjectReference"), element =>
            ((string?)element.Attribute("Include"))?.Contains("SpeechMessage.Localization", StringComparison.Ordinal) == true);
        Assert.Contains(project.Descendants("PackageReference"), element =>
            (string?)element.Attribute("Include") == "Newtonsoft.Json");
        Assert.Contains(project.Descendants("PackageReference"), element =>
            (string?)element.Attribute("Include") == "System.Text.Json");
        Assert.DoesNotContain(project.Descendants("ProjectReference"), element =>
            ((string?)element.Attribute("Include"))?.Contains("GoodShepherd.Core", StringComparison.Ordinal) == true);
    }

    /// <summary>僅精確平台組件名且恰有兩個公開 IPlugin 型別通過所有 PE 安全檢查。</summary>
    [Fact]
    public void Inspect_PlatformAssembly_ExactTwoPluginTypesPass()
    {
        using var fixture = new Fixture();
        var path = fixture.Emit();

        using var output = new StringWriter();
        var exitCode = Program.Run(new[] { path }, output);
        Assert.True(exitCode == 0, output.ToString());

        using var json = JsonDocument.Parse(output.ToString());
        var checks = json.RootElement.GetProperty("checks");
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(checks.GetProperty("platformPlugin").GetBoolean());
        Assert.True(checks.GetProperty("assemblyIdentity").GetBoolean());
        Assert.True(checks.GetProperty("allowedReferences").GetBoolean());
        Assert.True(checks.GetProperty("targetFramework").GetBoolean());
        Assert.True(checks.GetProperty("strongName").GetBoolean());
        Assert.True(checks.GetProperty("noDrawing").GetBoolean());
        Assert.True(checks.GetProperty("noNetStandardFacade").GetBoolean());
        Assert.DoesNotContain(fixture.Root, output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>精確入口不接受少列、多列、相似組件簡名或非 IPlugin 的同名公開類別。</summary>
    [Theory]
    [InlineData("missing-slot-request", "platformPlugin")]
    [InlineData("missing-channel-created", "platformPlugin")]
    [InlineData("extra-plugin", "platformPlugin")]
    [InlineData("extra-public-type", "platformPlugin")]
    [InlineData("lookalike-assembly", "assemblyIdentity")]
    [InlineData("non-plugin-entry", "platformPlugin")]
    [InlineData("local-fake-plugin-interface", "platformPlugin")]
    public void Inspect_PlatformIdentityOrEntrySetMismatch_IsRejected(string fault, string failedCheck)
    {
        using var fixture = new Fixture();
        var path = fixture.Emit(fault);

        using var output = new StringWriter();
        Assert.Equal(1, Program.Run(new[] { path }, output));

        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(json.RootElement.GetProperty("checks").TryGetProperty(failedCheck, out var check),
            $"The checker must report the failed '{failedCheck}' contract explicitly.");
        Assert.False(check.GetBoolean());
        Assert.DoesNotContain(fixture.Root, output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>平台組件自身仍須通過原有簽章、框架、相依、facade 與繪圖守門，不得因新身分略過。</summary>
    [Theory]
    [InlineData("unsigned", "strongName")]
    [InlineData("net48", "targetFramework")]
    [InlineData("unknown-reference", "allowedReferences")]
    [InlineData("workflow-reference", "allowedReferences")]
    [InlineData("drawing-reference", "noDrawing")]
    [InlineData("facade-reference", "noNetStandardFacade")]
    public void Inspect_PlatformFaults_PreserveExistingGuards(string fault, string failedCheck)
    {
        using var fixture = new Fixture();
        var path = fixture.Emit(fault);

        using var output = new StringWriter();
        Assert.Equal(1, Program.Run(new[] { path }, output));

        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(json.RootElement.GetProperty("checks").TryGetProperty(failedCheck, out var check),
            $"The checker must report the failed '{failedCheck}' contract explicitly.");
        Assert.False(check.GetBoolean());
        if (fault != "unsigned" && fault != "net48")
            Assert.True(json.RootElement.GetProperty("checks").GetProperty("platformPlugin").GetBoolean());
        Assert.DoesNotContain(fixture.Root, output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>定位目前 checkout 的方案根；測試不依賴固定使用者路徑或上層其他 worktree。</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    /// <summary>唯一擁有合成 PE、SDK 型別與簽章測試 key 的案例；不讀取正式 key 或載入產物。</summary>
    private sealed class Fixture : IDisposable
    {
        /// <summary>以隨機識別隔離每個測試案例的本機目錄。</summary>
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "f3-plugin-package-" + Guid.NewGuid().ToString("N"));

        /// <summary>每案例產生暫存強式名稱 key；只供 Roslyn 合成樣本，禁止部署或離開目錄。</summary>
        private readonly string keyPath;

        /// <summary>建立唯一資料夾與臨時 key；CSP 為非持久且在匯出後立即釋放。</summary>
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            keyPath = Path.Combine(Root, "fixture.snk");
            using var rsa = new RSACryptoServiceProvider(2048) { PersistKeyInCsp = false };
            File.WriteAllBytes(keyPath, rsa.ExportCspBlob(true));
        }

        /// <summary>
        /// 使用 net462 mscorlib、合成 Xrm SDK 身分及個案指定故障輸出惰性 PE；
        /// 編譯錯誤屬測試失敗，不會被當作 metadata checker 的成功拒絕。
        /// </summary>
        /// <param name="fault">本案例固定故障標籤，空字串表示正向控制組。</param>
        /// <returns>未載入或執行的組件絕對路徑。</returns>
        public string Emit(string fault = "")
        {
            var framework = fault == "net48" ? "4.8" : "4.6.2";
            var assemblyName = fault == "lookalike-assembly"
                ? PlatformAssembly + ".Other" : PlatformAssembly;
            var mscorlib = MetadataReference.CreateFromFile(
                Path.Combine(AppContext.BaseDirectory, "Framework", "mscorlib.dll"));
            var sdkPath = Path.Combine(Root, "Microsoft.Xrm.Sdk.dll");
            var sdk = CSharpCompilation.Create("Microsoft.Xrm.Sdk",
                new[] { CSharpSyntaxTree.ParseText(
                    "namespace Microsoft.Xrm.Sdk { public interface IPlugin { void Execute(System.IServiceProvider provider); } }") },
                new[] { mscorlib },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            EmitOrThrow(sdk, sdkPath);

            var source = "[assembly:System.Runtime.Versioning.TargetFramework(\".NETFramework,Version=v" +
                framework + "\")] ";
            if (fault == "local-fake-plugin-interface")
                source += "namespace Microsoft.Xrm.Sdk { public interface IPlugin { void Execute(System.IServiceProvider provider); } } ";
            source += "namespace SpeechMessage.Dynamics.Platform.Plugins.ChannelPool { ";
            if (fault != "missing-slot-request")
                source += fault == "non-plugin-entry"
                    ? "public sealed class SlotRequestPlugin { } "
                    : "public sealed class SlotRequestPlugin : Microsoft.Xrm.Sdk.IPlugin { public void Execute(System.IServiceProvider provider) {} } ";
            if (fault != "missing-channel-created")
                source += "public sealed class ChannelCreatedPlugin : Microsoft.Xrm.Sdk.IPlugin { public void Execute(System.IServiceProvider provider) {} } ";
            if (fault == "extra-plugin")
                source += "public sealed class ExtraPlugin : Microsoft.Xrm.Sdk.IPlugin { public void Execute(System.IServiceProvider provider) {} } ";
            if (fault == "extra-public-type")
                source += "public sealed class PublicHelper { } ";
            source += "}";
            var references = new[] { mscorlib, MetadataReference.CreateFromFile(sdkPath) }.AsEnumerable();
            var extraReference = fault switch
            {
                "unknown-reference" => "Unapproved.Dependency",
                "workflow-reference" => "Microsoft.Xrm.Sdk.Workflow",
                "drawing-reference" => "System.Drawing",
                "facade-reference" => "System.Runtime",
                _ => null
            };
            if (extraReference is not null)
            {
                var dependencyPath = Path.Combine(Root, extraReference + ".dll");
                var dependency = CSharpCompilation.Create(extraReference,
                    new[] { CSharpSyntaxTree.ParseText("public class RequiredType { }") },
                    new[] { mscorlib },
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                EmitOrThrow(dependency, dependencyPath);
                references = references.Append(MetadataReference.CreateFromFile(dependencyPath));
                source += "internal class DependencyOwner : RequiredType { }";
            }

            var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
            if (fault != "unsigned")
                options = options.WithCryptoKeyFile(keyPath).WithStrongNameProvider(new DesktopStrongNameProvider());
            var compilation = CSharpCompilation.Create(assemblyName,
                new[] { CSharpSyntaxTree.ParseText(source) }, references,
                options.WithAssemblyIdentityComparer(DesktopAssemblyIdentityComparer.Default));
            var assemblyPath = Path.Combine(Root, "platform-fixture.dll");
            EmitOrThrow(compilation, assemblyPath);
            return assemblyPath;
        }

        /// <summary>要求 Roslyn 寫出有效樣本；編譯診斷只回報合成來源錯誤，不遮蔽檢查器結果。</summary>
        /// <param name="compilation">本次合成的單一組件。</param>
        /// <param name="path">Fixture 目錄內固定輸出位置。</param>
        private static void EmitOrThrow(CSharpCompilation compilation, string path)
        {
            EmitResult result = compilation.Emit(path);
            Assert.True(result.Success, string.Join(Environment.NewLine,
                result.Diagnostics.Select(diagnostic => diagnostic.ToString())));
        }

        /// <summary>確認使用者輸出已釋放，並移除僅由本 Fixture 建立的簽章 key 與 PE。</summary>
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

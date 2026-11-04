using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SpeechMessage.Tools.PluginPackaging;
using Xunit;

namespace SpeechMessage.Tools.PluginPackaging.Tests;

/// <summary>
/// 使用 Roslyn 及一次性測試金鑰編譯惰性 PE，驗證真實 MetadataReader 與確定性關檔。
/// 樣本不載入執行，不讀正式金鑰或外部服務；各案例的輸出、金鑰與資料夾完全隔離。
/// </summary>
public sealed class AssemblyInspectionTests
{
    /// <summary>逐項注入錯誤框架、未簽章、未公開型別及禁用相依，正樣本必須通過全部檢查。</summary>
    /// <param name="fault">固定故障代碼，不接受外部輸入。</param>
    /// <param name="expected">預期接受或拒絕。</param>
    [Theory(DisplayName = "檢查器拒絕不符合沙箱契約的中繼資料")]
    [InlineData("valid", true)]
    [InlineData("unsigned", false)]
    [InlineData("framework", false)]
    [InlineData("no-framework", false)]
    [InlineData("private", false)]
    [InlineData("reference", false)]
    [InlineData("drawing", false)]
    public void Inspect_MetadataFaults_ReturnsContractResult(string fault, bool expected)
    {
        using var fixture = new Fixture();
        var path = fixture.Emit(fault);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        Assert.Equal(expected ? 0 : 1, Program.Run(new[] { path }, output));
        using var result = JsonDocument.Parse(output.ToString());
        Assert.Equal(expected, result.RootElement.GetProperty("ok").GetBoolean());
        // 不等程序結束或 GC：返回後立即以獨佔讀寫開啟，證明 PE reader／stream 已確定性釋放。
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(exclusive.Length > 0);
    }

    /// <summary>損毀、過大、缺檔與缺參數皆 fail closed，輸出固定原因且不能包含原始本機路徑。</summary>
    /// <param name="fault">只供案例選擇的錯誤種類。</param>
    /// <param name="reason">固定安全拒絕原因。</param>
    [Theory(DisplayName = "不完整輸入以固定原因拒絕且不洩漏路徑")]
    [InlineData("arguments", "InvalidArguments")]
    [InlineData("missing", "InvalidImage")]
    [InlineData("invalid", "InvalidImage")]
    [InlineData("oversize", "SizeLimit")]
    public void Inspect_InvalidInput_FailsClosedWithoutPath(string fault, string reason)
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, "synthetic-sensitive-path.dll");
        if (fault == "invalid") File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
        if (fault == "oversize")
        {
            using var stream = File.Create(path);
            stream.SetLength(16 * 1024 * 1024);
        }
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var args = fault == "arguments" ? Array.Empty<string>() : new[] { path };
        Assert.Equal(1, Program.Run(args, output));
        using var result = JsonDocument.Parse(output.ToString());
        Assert.Equal(reason, result.RootElement.GetProperty("reason").GetString());
        Assert.DoesNotContain(fixture.Root, output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// 保護 ILRepack 的每一個輸入都不得宣告高於伺服器核定的 v4.6.2；
    /// 以離線合成 PE 注入 v4.6.2、v4.5、v4.8 與非 Framework 屬性，斷言只接受不高於上限的明確 .NET Framework 目標。
    /// </summary>
    [Theory(DisplayName = "合併輸入僅接受不高於 Framework 4.6.2 的明確目標")]
    [InlineData("net462", true)]
    [InlineData("net45", true)]
    [InlineData("net48", false)]
    [InlineData("framework", false)]
    [InlineData("no-framework", false)]
    public void ValidateMergeInputFramework_EnforcesNet462Ceiling(string target, bool expected)
    {
        using var fixture = new Fixture();
        var path = fixture.Emit(target);
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        var exitCode = Program.Run(new[] { "--validate-net462-input", path }, output);

        Assert.Equal(expected ? 0 : 1, exitCode);
        using var result = JsonDocument.Parse(output.ToString());
        Assert.Equal(expected, result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(expected, result.RootElement.GetProperty("checks").GetProperty("targetFramework").GetBoolean());
        var expectedFramework = target switch
        {
            "net462" => ".NETFramework,Version=v4.6.2",
            "net45" => ".NETFramework,Version=v4.5",
            "net48" => ".NETFramework,Version=v4.8",
            "framework" => ".NETCoreApp,Version=v10.0",
            _ => null
        };
        Assert.Equal(expectedFramework, result.RootElement.GetProperty("frameworkName").GetString());
        Assert.DoesNotContain(fixture.Root, output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// 合成真實的 facade 名稱參考，重現 System.* 前綴把它誤判為安全相依的缺口；
    /// 合併前與部署 DLL 檢查兩個入口都必須拒絕，普通 Framework 名稱則維持可用。
    /// 每次建立獨立惰性 PE，不載入執行；返回後獨佔重開確認讀取資源已釋放。
    /// </summary>
    /// <param name="dependencyName">由案例固定的組件簡名，不從外部設定取得。</param>
    /// <param name="expected">人工依 net462 facade 目錄分類的預期結果。</param>
    [Theory(DisplayName = "兩個檢查入口拒絕 facade 參考並保留合法 Framework 參考")]
    [InlineData("System.Runtime", false)]
    [InlineData("System.Collections", false)]
    [InlineData("System.Runtime.Extensions", false)]
    [InlineData("System.ServiceModel.Primitives", false)]
    [InlineData("System.runtime", false)]
    [InlineData("netstandard", false)]
    [InlineData("System.Net.Http", true)]
    [InlineData("System.Runtime.CustomLibrary", true)]
    public void Inspect_FacadeReferences_BothEntryPointsReject(string dependencyName, bool expected)
    {
        using var fixture = new Fixture();
        var path = fixture.Emit("facade-reference", dependencyName);
        foreach (var args in new[] { new[] { path }, new[] { "--validate-net462-input", path } })
        {
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            Assert.Equal(expected ? 0 : 1, Program.Run(args, output));
            using var result = JsonDocument.Parse(output.ToString());
            Assert.Equal(expected, result.RootElement.GetProperty("ok").GetBoolean());
            Assert.True(result.RootElement.GetProperty("checks").GetProperty("targetFramework").GetBoolean());
            Assert.Equal(expected, result.RootElement.GetProperty("checks").GetProperty("noNetStandardFacade").GetBoolean());
            Assert.DoesNotContain(fixture.Root, output.ToString(), StringComparison.Ordinal);
        }
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(exclusive.Length > 0);
    }

    /// <summary>
    /// 以 facade 自身的組件身分注入合併輸入，避免只檢查其相依名稱而允許直接合併 facade；
    /// 即使它宣告 v4.6.2 且已簽章，兩個入口仍須以獨立 facade 欄位拒絕。
    /// </summary>
    /// <param name="assemblyName">固定的 facade 組件身分。</param>
    [Theory(DisplayName = "facade 本身即使宣告正確框架也不得合併")]
    [InlineData("System.Runtime")]
    [InlineData("System.Collections")]
    [InlineData("netstandard")]
    public void Inspect_FacadeAssemblyIdentity_BothEntryPointsReject(string assemblyName)
    {
        using var fixture = new Fixture();
        var path = fixture.Emit("valid", assemblyName: assemblyName);
        foreach (var args in new[] { new[] { path }, new[] { "--validate-net462-input", path } })
        {
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            Assert.Equal(1, Program.Run(args, output));
            using var result = JsonDocument.Parse(output.ToString());
            Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
            Assert.True(result.RootElement.GetProperty("checks").GetProperty("targetFramework").GetBoolean());
            Assert.False(result.RootElement.GetProperty("checks").GetProperty("noNetStandardFacade").GetBoolean());
        }
    }

    /// <summary>獨佔暫存目錄及測試金鑰的唯一 owner；只產生惰性 DLL，Dispose 刪除所有測試資源。</summary>
    private sealed class Fixture : IDisposable
    {
        /// <summary>以隨機識別切分測試邊界，不從待測檔或外部資料推算清理目標。</summary>
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "speechmessage-inspection-" + Guid.NewGuid().ToString("N"));
        /// <summary>僅在此 fixture 有效的測試金鑰路徑。</summary>
        private readonly string key;
        /// <summary>一次性產生測試 RSA key，匯出後立即釋放 CSP，不碰正式環境變數。</summary>
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            key = Path.Combine(Root, "test.snk");
            using var rsa = new RSACryptoServiceProvider(2048) { PersistKeyInCsp = false };
            File.WriteAllBytes(key, rsa.ExportCspBlob(true));
        }
        /// <summary>以 net462 參考組件離線編譯指定故障；編譯失敗是測試錯誤，不當作檢查器成功拒絕。</summary>
        /// <param name="fault">固定測試故障種類。</param>
        /// <param name="dependencyName">可選的合成相依名稱，只用來注入 facade 參考。</param>
        /// <param name="assemblyName">可選的合成組件身分，用來驗證 facade 本身也必須拒絕。</param>
        /// <returns>尚未被載入的測試 DLL 路徑。</returns>
        public string Emit(string fault, string? dependencyName = null, string assemblyName = "InspectionFixture")
        {
            var reference = MetadataReference.CreateFromFile(Path.Combine(AppContext.BaseDirectory, "Framework", "mscorlib.dll"));
            var framework = fault == "no-framework" ? "" :
                "[assembly: System.Runtime.Versioning.TargetFramework(\"" + GetFrameworkName(fault) + "\")]";
            var source = framework + "namespace SpeechMessage.Dynamics.Plugins.Diagnostics { " +
                (fault == "private" ? "internal" : "public") + " sealed class DiagnosticsPingPlugin { } }";
            var references = new[] { reference }.AsEnumerable();
            if (fault is "reference" or "drawing" or "facade-reference")
            {
                var name = fault == "facade-reference"
                    ? dependencyName ?? throw new ArgumentException("A fixture dependency name is required.", nameof(dependencyName))
                    : fault == "reference" ? "UnexpectedDependency" : "System.Drawing";
                var dependency = Path.Combine(Root, name + ".dll");
                var dependencyCompilation = CSharpCompilation.Create(name, new[] { CSharpSyntaxTree.ParseText("public class RequiredType { }") },
                    references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                Assert.True(dependencyCompilation.Emit(dependency).Success);
                references = references.Append(MetadataReference.CreateFromFile(dependency));
                source += "public class DependencyOwner : RequiredType { }";
            }
            var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
            if (fault != "unsigned") options = options.WithCryptoKeyFile(key).WithStrongNameProvider(new DesktopStrongNameProvider());
            var compilation = CSharpCompilation.Create(assemblyName, new[] { CSharpSyntaxTree.ParseText(source) }, references, options);
            var path = Path.Combine(Root, "fixture.dll");
            var result = compilation.Emit(path);
            Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(value => value.ToString())));
            return path;
        }

        /// <summary>依固定測試案例產生框架屬性，分別覆蓋低於、等於、超過上限及非 Framework 情境。</summary>
        /// <param name="fault">測試內部控制的合成故障標籤。</param>
        /// <returns>編入惰性 PE 的框架名稱。</returns>
        private static string GetFrameworkName(string fault) => fault switch
        {
            "framework" => ".NETCoreApp,Version=v10.0",
            "net45" => ".NETFramework,Version=v4.5",
            "net48" => ".NETFramework,Version=v4.8",
            _ => ".NETFramework,Version=v4.6.2"
        };

        /// <summary>Roslyn 已完成並釋放檔案後，在已宣告的獨佔根目錄內刪除一次性 key 與 PE。</summary>
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

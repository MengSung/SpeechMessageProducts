/*
檔案整體說明

目的與定位：保護 P5-02 離線來源切片的安全閉包，確保只有指定舊入口之外、可證明未使用的型別與普通方法可移除；不是舊方案或實機黃金驗收。
主要功能與流程：以記憶體中的合成 IPlugin／CodeActivity 原始碼建立 Roslyn compilation，再對切片重新繫結並交給既有全量 CompileSources guard；未使用 WebRequest helper 應消失，可達初始化、建構、介面／虛擬派送、委派、屬性及屬性標記中的危險呼叫必須留下並被拒絕。
使用邊界與維護：不讀取舊原始檔、token 或部署設定，不載入或執行合成組件、不建立外部連線。每筆測試獨占 compilation 與來源，metadata 僅讀目前受信任框架／SDK；compiler 結果以 using 清零輸出。故障注入涵蓋反射、dynamic 與未解析呼叫，另核對移除未使用型別後 namespace 宣告仍合法，決定性斷言為符號存在性、重新編譯錯誤及既有 guard 的固定狀態；沒有跨測試可變快取或背景資源。
*/
using System;
using System.IO;
using System.Linq;
using System.Text;
using LegacyGolden.PluginHarness;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace SpeechMessage.Dynamics.Plugins.Tests.Harness;

/// <summary>
/// 驗證來源切片與既有安全守門之間的契約。所有危險 API 都只存在於合成文字，
/// 測試僅執行 Roslyn 與 guard，不反射載入、建立或執行任何舊／合成入口。
/// </summary>
public sealed class ReachableSourceSubsetTests
{
    private const string Header = "using System; using Microsoft.Xrm.Sdk; ";
    private const string Danger = "System.Net.WebRequest.Create(\"https://example.invalid\");";
    private static readonly string[] SampleRoots = { "Sample" };

    /// <summary>
    /// 注入完全未使用型別及已使用工具型別的未使用 CRM／網路方法；兩者均可移除，
    /// 安全方法與入口仍存在，重新編譯及原有全量 guard 必須成功。
    /// </summary>
    [Fact]
    public void Subset_UnusedDangerousHelpersAreRemoved_BeforeUnchangedGuard()
    {
        var compilation = Slice(Plugin("Tools.Safe();")
            + "public static class Tools { public static void Safe() {} public static void Unused() { " + Danger + " } }"
            + "public static class UnusedPaymentHelper { public static void Connect() { " + Danger + " } }");
        Assert.Null(compilation.GetTypeByMetadataName("UnusedPaymentHelper"));
        var tools = compilation.GetTypeByMetadataName("Tools");
        Assert.NotNull(tools);
        Assert.Empty(tools!.GetMembers("Unused"));
        Assert.Single(tools.GetMembers("Safe"));
        AssertCompiles(compilation);
        AssertGuardStatus(compilation, "Generated");
    }

    /// <summary>指定入口的私人未呼叫方法也是根；不可藉一般方法裁剪隱藏入口內危險 API。</summary>
    [Fact]
    public void Subset_AllEntryMembersRemainRoots()
    {
        var compilation = Slice(Plugin(string.Empty, "private void Hidden() { " + Danger + " }"));
        Assert.Single(compilation.GetTypeByMetadataName("Sample")!.GetMembers("Hidden"));
        AssertGuardStatus(compilation, "BlockedByNetworkApi");
    }

    /// <summary>
    /// 相依型別一經使用，其 static／instance 欄位初始化、屬性初始化、全部建構子、
    /// base／this 初始化器均須傳播；即使當下選到另一建構子也不得裁掉副作用。
    /// </summary>
    [Theory]
    [InlineData("public static object Value = Hazard.Get();")]
    [InlineData("private object value = Hazard.Get();")]
    [InlineData("public object Value { get; } = Hazard.Get();")]
    [InlineData("static Tools() { Hazard.Go(); }")]
    [InlineData("public Tools() { Hazard.Go(); }")]
    [InlineData("public Tools() {} public Tools(int value) : this() { Hazard.Go(); }")]
    public void Subset_ReferencedTypeInitializationAndConstructorsRetainHazard(string member)
    {
        var compilation = Slice(Plugin("Tools.Safe();")
            + "public class Tools { public static void Safe() {} " + member + " }" + Hazard());
        AssertHazardRetained(compilation);
    }

    /// <summary>根入口繼承來源基底時，隱式 base 建構與基底普通方法仍受閉包及守門保護。</summary>
    [Fact]
    public void Subset_BaseAndThisConstructorChainsRemainReachable()
    {
        var source = Header + "public sealed class Sample : Parent, IPlugin { public void Execute(IServiceProvider p) { new Child(); } }"
            + "public class Parent { public Parent() { Hazard.Go(); } }"
            + "public class Child : Parent { public Child() : this(1) {} public Child(int value) : base() {} }" + Hazard();
        AssertHazardRetained(Slice(source));
    }

    /// <summary>介面無 body 仍須追蹤所有來源同名實作，包含私人顯式實作的危險方法。</summary>
    [Theory]
    [InlineData("public void Go() { Hazard.Go(); }")]
    [InlineData("void IRunner.Go() { Hazard.Go(); }")]
    public void Subset_InterfaceDispatchRetainsSourceImplementations(string implementation)
    {
        var source = Plugin("IRunner runner = Factory.Make(); runner.Go();")
            + "public interface IRunner { void Go(); }"
            + "public static class Factory { public static IRunner Make() => new Runner(); }"
            + "public sealed class Runner : IRunner { " + implementation + " }" + Hazard();
        AssertHazardRetained(Slice(source));
    }

    /// <summary>透過來源或框架介面呼叫，不能只留下靜態介面 symbol 而漏掉實作。</summary>
    [Fact]
    public void Subset_MetadataInterfaceDispatchRetainsImplementation()
    {
        AssertHazardRetained(Slice(Plugin("IDisposable value = new Runner(); value.Dispose();")
            + "public sealed class Runner : IDisposable { public void Dispose() { Hazard.Go(); } }" + Hazard()));
    }

    /// <summary>虛擬方法由工廠回傳基底參考；override 危險實作與其相依必須留下。</summary>
    [Fact]
    public void Subset_VirtualIndirectDispatchRetainsOverride()
    {
        AssertHazardRetained(Slice(Plugin("Factory.Make().Go();")
            + "public class Parent { public virtual void Go() {} }"
            + "public sealed class Child : Parent { public override void Go() { Hazard.Go(); } }"
            + "public static class Factory { public static Parent Make() => new Child(); }" + Hazard()));
    }

    /// <summary>方法群組與 lambda 的目標都須傳播，不能只追蹤直接 invocation。</summary>
    [Theory]
    [InlineData("Action action = Hazard.Go; action();")]
    [InlineData("Action action = () => Hazard.Go(); action();")]
    [InlineData("Action action = new Action(Hazard.Go); action();")]
    public void Subset_DelegateAndLambdaTargetsRemainReachable(string body)
    {
        AssertHazardRetained(Slice(Plugin(body) + Hazard()));
    }

    /// <summary>屬性、事件、運算子與自訂 attribute 建構都不能因普通方法裁剪而丟失相依。</summary>
    [Theory]
    [InlineData("public static object Value => Hazard.Get();")]
    [InlineData("public static event Action Changed { add { Hazard.Go(); } remove { Hazard.Go(); } }")]
    [InlineData("public static Tools operator +(Tools left, Tools right) { Hazard.Go(); return left; }")]
    [InlineData("[Marker] public static void Callback() {}")]
    public void Subset_PropertiesEventsOperatorsAndAttributedCallbacksRemainReachable(string member)
    {
        var source = Plugin("Tools.Safe();")
            + "public class Tools { public static void Safe() {} " + member + " }"
            + "public sealed class MarkerAttribute : Attribute { public MarkerAttribute() { Hazard.Go(); } }" + Hazard();
        AssertHazardRetained(Slice(source));
    }

    /// <summary>foreach 的模式方法是編譯器隱含呼叫，並非原始碼直接方法參考；危險 MoveNext 必須留下。</summary>
    [Fact]
    public void Subset_ForeachPatternMethodsRemainReachable()
    {
        AssertHazardRetained(Slice(Plugin("foreach (var value in new Sequence()) {}")
            + "public class Sequence { public Cursor GetEnumerator() => new Cursor(); }"
            + "public class Cursor { public int Current => 0; public bool MoveNext() { Hazard.Go(); return false; } }" + Hazard()));
    }

    /// <summary>來源分散於 partial 宣告與巢狀型別時，根必須涵蓋每個宣告及其危險相依。</summary>
    [Fact]
    public void Subset_PartialAndNestedEntryMembersRemainRoots()
    {
        var compilation = Create(
            Header + "public partial class Sample : IPlugin { public void Execute(IServiceProvider p) {} }",
            "public partial class Sample { private class Nested { private void Hidden() { Hazard.Go(); } } }" + Hazard());
        AssertHazardRetained(ReachableSourceSubset.Create(compilation, SampleRoots));
    }

    /// <summary>以真正 CodeActivity metadata 驗證根識別，活動 protected Execute 及未呼叫成員都保留。</summary>
    [Fact]
    public void Subset_PublicCodeActivityIsAcceptedAsRoot()
    {
        var compilation = Slice("public sealed class Sample : System.Activities.CodeActivity {"
            + " protected override void Execute(System.Activities.CodeActivityContext context) {}"
            + " private void Hidden() { Hazard.Go(); } }" + Hazard());
        AssertHazardRetained(compilation);
    }

    /// <summary>
    /// 反射、dynamic 及無法解析的可達呼叫皆使切片退回原 compilation；
    /// 其他未使用危險 helper 不得消失，原 guard／compiler 繼續決定拒絕。
    /// </summary>
    [Theory]
    [InlineData("typeof(Sample).GetMethods();")]
    [InlineData("dynamic value = p; value.Unknown();")]
    [InlineData("Missing.Run();")]
    public void Subset_UnprovableReachabilityRetainsCompleteInput(string body)
    {
        var compilation = Create(Plugin(body) + Hazard());
        var result = ReachableSourceSubset.Create(compilation, SampleRoots);
        Assert.Same(compilation, result);
        Assert.NotNull(result.GetTypeByMetadataName("Hazard"));
        AssertGuardStatus(result, "BlockedByNetworkApi");
    }

    /// <summary>空根、錯誤名稱與非入口型別不能把來源裁成空組件並繞過安全 guard。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("Missing")]
    [InlineData("Hazard")]
    public void Subset_InvalidRootsNeverProduceEmptyTrustedCompilation(string root)
    {
        var compilation = Create(Plugin(string.Empty) + Hazard());
        Assert.Same(compilation, ReachableSourceSubset.Create(compilation,
            root.Length == 0 ? Array.Empty<string>() : new[] { root }));
    }

    /// <summary>移除未使用型別後仍保留 namespace 宣告；既有 using 可合法繫結，不應把空 namespace 誤判為缺失。</summary>
    [Fact]
    public void Subset_UnusedNamespaceShellRetainsValidUsingDirective()
    {
        var source = "using UnusedNamespace; " + Plugin(string.Empty)
            + "namespace UnusedNamespace { public class Removed {} }";
        var compilation = Slice(source);
        Assert.Null(compilation.GetTypeByMetadataName("UnusedNamespace.Removed"));
        AssertCompiles(compilation);
        AssertGuardStatus(compilation, "Generated");
    }

    /// <summary>兩次切片獨占閉包狀態；前一次根與危險來源不能污染下一次安全來源。</summary>
    [Fact]
    public void Subset_IndependentCompilationsNeverShareReachabilityState()
    {
        AssertHazardRetained(Slice(Plugin("Hazard.Go();") + Hazard()));
        var safe = Slice(Plugin(string.Empty) + Hazard());
        Assert.Null(safe.GetTypeByMetadataName("Hazard"));
        AssertCompiles(safe);
        AssertGuardStatus(safe, "Generated");
    }

    /// <summary>建立明確非網路執行的合成入口，額外成員僅插入同一個根型別。</summary>
    private static string Plugin(string body, string members = "") =>
        Header + "public sealed class Sample : IPlugin { public void Execute(IServiceProvider p) { " + body + " } " + members + " }";

    /// <summary>統一危險相依，不輸入真 token、LINE ID 或可連線目的地。</summary>
    private static string Hazard() =>
        "public static class Hazard { public static void Go() { " + Danger + " } public static object Get() { " + Danger + " return null; } }";

    /// <summary>只切指定根；輸出仍是待 guard 與 emit 的 compilation，沒有舊程式執行。</summary>
    private static CSharpCompilation Slice(string source) =>
        ReachableSourceSubset.Create(Create(source), SampleRoots);

    /// <summary>
    /// 建立單次合成 compilation，metadata 由受信任目前框架與 CRM SDK 的唯讀影像提供；
    /// 不開啟舊 DLL，不以 Roslyn 長期映射檔案，不保存來源或 semantic model 的 static 快取。
    /// </summary>
    private static CSharpCompilation Create(params string[] sources)
    {
        var framework = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Reference Assemblies", "Microsoft", "Framework", ".NETFramework", "v4.8");
        var paths = new[] { typeof(object).Assembly.Location, typeof(Enumerable).Assembly.Location,
            typeof(System.Net.WebRequest).Assembly.Location, typeof(IPlugin).Assembly.Location,
            Path.Combine(framework, "System.Activities.dll"), Path.Combine(framework, "System.Runtime.Serialization.dll") };
        var references = paths.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromImage(File.ReadAllBytes(path)));
        return CSharpCompilation.Create("ReachableSubsetFixture",
            sources.Select((source, index) => CSharpSyntaxTree.ParseText(source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest), "Fixture" + index + ".cs", Encoding.UTF8)),
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>有缺成員或符號繫結錯誤就失敗，錯誤輸出僅限固定診斷代碼。</summary>
    private static void AssertCompiles(CSharpCompilation compilation) =>
        Assert.Empty(compilation.GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error).Select(item => item.Id));

    /// <summary>斷言危險方法及其呼叫未被刪除，且既有 guard 仍在 emit 前拒絕網路 API。</summary>
    private static void AssertHazardRetained(CSharpCompilation compilation)
    {
        var hazard = compilation.GetTypeByMetadataName("Hazard");
        Assert.NotNull(hazard);
        Assert.True(hazard!.GetMembers("Go").Length != 0 || hazard.GetMembers("Get").Length != 0);
        AssertCompiles(compilation);
        AssertGuardStatus(compilation, "BlockedByNetworkApi");
    }

    /// <summary>
    /// 把切片樹交給原有合成來源全量安全管線，避免測試依賴放寬 guard；
    /// 結果 owner 於離開此方法時釋放位元組，任何來源均不執行。
    /// </summary>
    private static void AssertGuardStatus(CSharpCompilation compilation, string expected)
    {
        using var result = LegacyProjectCompiler.CompileSources(compilation.SyntaxTrees.Select(tree => tree.GetText().ToString()));
        Assert.Equal(expected, result.Status);
    }
}

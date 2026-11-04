# CCG reviewer Task: integration-p0-review-net462-10-fix

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P0.Worktree

## Request
# 第 10 份：net462 改版 High 修正獨立複審

單模型（使用者指定 ClaudeOnly）。只讀，不修改任何檔案，不連線 D365／LINE，不讀 User Secrets、appsettings、金鑰／授權或其他歷史輸出。
本次是 20261104-190256-integration-p0-review-net462-10-reviewer 的獨立修正複審，不宣稱初審涵蓋此修正。
初審 reviewMode=claude-only、dualModelComplete=false、quotaBlocked=false，Critical=0、High=1。
High：原 allowedReferences 允許所有 System.*，無法拒絕 System.Runtime 等 facade。Info：version.Major>=0 冗餘僅記錄，不擴大改動。

## 修正與需要核對
- 原 allowedReferences、strongName、net462、noDrawing、診斷型別與大小檢查完整保留；新增獨立 noNetStandardFacade，且納入整體 ok 與 exit code。
- 合併前 input 驗證與最終 DLL 檢查共用同一 HasNoFrameworkFacade；拒絕自身身分與所有 assembly references。固定 net462 ReferenceAssemblies 1.0.3 的 Facades 目錄53個名稱，加 netstandard，共54個 FrozenSet；精確簡名、OrdinalIgnoreCase，無磁碟或呼叫者設定，不使用開放式 System.* 拒絕規則，合法 System.Net.Http 保留。
- 11 個新合成 PE 案例修正前都功能紅燈（包括缺失欄位），無編譯／環境失敗；修正後全套27/27。案例覆蓋 Runtime、Collections、Runtime.Extensions、ServiceModel.Primitives、大小寫、netstandard、合法 Net.Http、僅前綴相似名稱、自身 facade 身分，兩入口都執行。每案例獨立暫存與一次性測試key，未載入PE，不讀正式key；流讀取返回後可獨佔重開。
- PS assembly/build 契約亦檢查新欄位；打包契約6/6已再通過，完整assembly10樣本與完整00§8正在由Codex重跑，不交由審查模型執行。
- 文件修正原先錯稱 AnalyzerProbe 目標net462的敘述：實際探針保留 net48;net10.0，它是測試工具；產品net462以參考組件編譯和實際載入測試驗證。此次未縮小既有探測或放寬規則。

檢查最小修正是否確實關閉High，是否有誤拒真實框架參考／漏掉入口、資源洩漏、未記錄的功能變更、測試假綠燈。這次無新增套件或產品連線程式修改。若認為仍有High，請提供具體可重現情境、檔案方法與最小修法；不要把本機無法證明實機註冊列為程式High（它是使用者已接受、仍待實機確認的邊界）。
以繁體中文輸出 Critical/High/Medium/Low/Info，明確說是否有未處理Critical/High。只補讀下列列出的檔案及build/PluginMerge.targets；其餘範圍可依初審已核對結論，不重新發起全repo掃描。

## 修正差異
```diff
diff --git a/.trellis/spec/backend/plugin-packaging-contract.md b/.trellis/spec/backend/plugin-packaging-contract.md
index ffdcdd4c..de7e9e63 100644
--- a/.trellis/spec/backend/plugin-packaging-contract.md
+++ b/.trellis/spec/backend/plugin-packaging-contract.md
@@ -40,7 +40,14 @@ It emits one JSON result per DLL and fails the whole run if any DLL fails.
   each declare a .NET Framework target no higher than v4.6.2; the merged
   deployment assembly must declare v4.6.2. The metadata checker reads PE
   metadata without loading the files. Do not merge .NET Standard facades,
-  including `System.Runtime`.
+  including `System.Runtime`. The common PE checker rejects both facade assembly
+  identities and facade references before merging and when validating the final
+  DLL, through a separate `checks.noNetStandardFacade` field. The bounded,
+  immutable name set consists of the Facades directory names from the fixed
+  net462 ReferenceAssemblies 1.0.3 package plus `netstandard`; compare exact
+  assembly simple names case-insensitively, without blocking all `System.*`
+  references or consulting the runtime machine. Keep the original allowed-reference
+  and other checks independently active.
 - `net48` may remain a test-host target, but tests that reference product
   projects must resolve and verify their actual `net462` product outputs.
 - Read the formal key only from process environment
@@ -76,6 +83,7 @@ It emits one JSON result per DLL and fails the whole run if any DLL fails.
 | Wrong/missing TargetFrameworkAttribute | targetFramework=false, exit 1 |
 | Reference outside mscorlib, System, System.*, Xrm SDK/Proxy | allowedReferences=false, exit 1 |
 | System.Drawing or a child reference | noDrawing=false, exit 1 |
+| Framework facade identity or reference, including System.Runtime, System.Collections or netstandard | noNetStandardFacade=false, exit 1 in both entry points |
 | Missing/private diagnostic type | diagnosticsPlugin=false, exit 1 |
 | Length at least 16 MiB, corrupt PE, unavailable input | Fail closed with fixed reason, no original path/message |
 | Mixed directory containing valid and invalid DLLs | Inspect every DLL; aggregate exit 1 |
diff --git a/.trellis/spec/backend/strict-analyzer-contract.md b/.trellis/spec/backend/strict-analyzer-contract.md
index 7285353f..750c75d5 100644
--- a/.trellis/spec/backend/strict-analyzer-contract.md
+++ b/.trellis/spec/backend/strict-analyzer-contract.md
@@ -52,7 +52,8 @@ the intentionally invalid probe builds themselves must fail.
 - Use property documentation IDs for CultureInfo default-culture properties.
   Accessor method IDs did not match real assignments in version 5.6.0. The
   property ban intentionally rejects both reads and writes of global defaults.
-- IsExternalInit and NotNullWhenAttribute are linked only for net48. Their
+- IsExternalInit and NotNullWhenAttribute are linked only for .NET Framework
+  targets: net462 products and retained net48 test hosts. Their
   source files are shared; they introduce no state or runtime dependency.
 - Probe projects never execute. Per-run plugin fixtures are deleted after their
   build process drains; the cleanup target must remain beneath the owned root.
diff --git a/tools/PluginPackaging.Tests/AssemblyInspectionTests.cs b/tools/PluginPackaging.Tests/AssemblyInspectionTests.cs
index e60f36b5..8d34ade6 100644
--- a/tools/PluginPackaging.Tests/AssemblyInspectionTests.cs
+++ b/tools/PluginPackaging.Tests/AssemblyInspectionTests.cs
@@ -101,6 +101,64 @@ public sealed class AssemblyInspectionTests
         Assert.DoesNotContain(fixture.Root, output.ToString(), StringComparison.Ordinal);
     }
 
+    /// <summary>
+    /// 合成真實的 facade 名稱參考，重現 System.* 前綴把它誤判為安全相依的缺口；
+    /// 合併前與部署 DLL 檢查兩個入口都必須拒絕，普通 Framework 名稱則維持可用。
+    /// 每次建立獨立惰性 PE，不載入執行；返回後獨佔重開確認讀取資源已釋放。
+    /// </summary>
+    /// <param name="dependencyName">由案例固定的組件簡名，不從外部設定取得。</param>
+    /// <param name="expected">人工依 net462 facade 目錄分類的預期結果。</param>
+    [Theory(DisplayName = "兩個檢查入口拒絕 facade 參考並保留合法 Framework 參考")]
+    [InlineData("System.Runtime", false)]
+    [InlineData("System.Collections", false)]
+    [InlineData("System.Runtime.Extensions", false)]
+    [InlineData("System.ServiceModel.Primitives", false)]
+    [InlineData("System.runtime", false)]
+    [InlineData("netstandard", false)]
+    [InlineData("System.Net.Http", true)]
+    [InlineData("System.Runtime.CustomLibrary", true)]
+    public void Inspect_FacadeReferences_BothEntryPointsReject(string dependencyName, bool expected)
+    {
+        using var fixture = new Fixture();
+        var path = fixture.Emit("facade-reference", dependencyName);
+        foreach (var args in new[] { new[] { path }, new[] { "--validate-net462-input", path } })
+        {
+            using var output = new StringWriter(CultureInfo.InvariantCulture);
+            Assert.Equal(expected ? 0 : 1, Program.Run(args, output));
+            using var result = JsonDocument.Parse(output.ToString());
+            Assert.Equal(expected, result.RootElement.GetProperty("ok").GetBoolean());
+            Assert.True(result.RootElement.GetProperty("checks").GetProperty("targetFramework").GetBoolean());
+            Assert.Equal(expected, result.RootElement.GetProperty("checks").GetProperty("noNetStandardFacade").GetBoolean());
+            Assert.DoesNotContain(fixture.Root, output.ToString(), StringComparison.Ordinal);
+        }
+        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
+        Assert.True(exclusive.Length > 0);
+    }
+
+    /// <summary>
+    /// 以 facade 自身的組件身分注入合併輸入，避免只檢查其相依名稱而允許直接合併 facade；
+    /// 即使它宣告 v4.6.2 且已簽章，兩個入口仍須以獨立 facade 欄位拒絕。
+    /// </summary>
+    /// <param name="assemblyName">固定的 facade 組件身分。</param>
+    [Theory(DisplayName = "facade 本身即使宣告正確框架也不得合併")]
+    [InlineData("System.Runtime")]
+    [InlineData("System.Collections")]
+    [InlineData("netstandard")]
+    public void Inspect_FacadeAssemblyIdentity_BothEntryPointsReject(string assemblyName)
+    {
+        using var fixture = new Fixture();
+        var path = fixture.Emit("valid", assemblyName: assemblyName);
+        foreach (var args in new[] { new[] { path }, new[] { "--validate-net462-input", path } })
+        {
+            using var output = new StringWriter(CultureInfo.InvariantCulture);
+            Assert.Equal(1, Program.Run(args, output));
+            using var result = JsonDocument.Parse(output.ToString());
+            Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
+            Assert.True(result.RootElement.GetProperty("checks").GetProperty("targetFramework").GetBoolean());
+            Assert.False(result.RootElement.GetProperty("checks").GetProperty("noNetStandardFacade").GetBoolean());
+        }
+    }
+
     /// <summary>獨佔暫存目錄及測試金鑰的唯一 owner；只產生惰性 DLL，Dispose 刪除所有測試資源。</summary>
     private sealed class Fixture : IDisposable
     {
@@ -118,8 +176,10 @@ public sealed class AssemblyInspectionTests
         }
         /// <summary>以 net462 參考組件離線編譯指定故障；編譯失敗是測試錯誤，不當作檢查器成功拒絕。</summary>
         /// <param name="fault">固定測試故障種類。</param>
+        /// <param name="dependencyName">可選的合成相依名稱，只用來注入 facade 參考。</param>
+        /// <param name="assemblyName">可選的合成組件身分，用來驗證 facade 本身也必須拒絕。</param>
         /// <returns>尚未被載入的測試 DLL 路徑。</returns>
-        public string Emit(string fault)
+        public string Emit(string fault, string? dependencyName = null, string assemblyName = "InspectionFixture")
         {
             var reference = MetadataReference.CreateFromFile(Path.Combine(AppContext.BaseDirectory, "Framework", "mscorlib.dll"));
             var framework = fault == "no-framework" ? "" :
@@ -127,9 +187,11 @@ public sealed class AssemblyInspectionTests
             var source = framework + "namespace SpeechMessage.Dynamics.Plugins.Diagnostics { " +
                 (fault == "private" ? "internal" : "public") + " sealed class DiagnosticsPingPlugin { } }";
             var references = new[] { reference }.AsEnumerable();
-            if (fault is "reference" or "drawing")
+            if (fault is "reference" or "drawing" or "facade-reference")
             {
-                var name = fault == "reference" ? "UnexpectedDependency" : "System.Drawing";
+                var name = fault == "facade-reference"
+                    ? dependencyName ?? throw new ArgumentException("A fixture dependency name is required.", nameof(dependencyName))
+                    : fault == "reference" ? "UnexpectedDependency" : "System.Drawing";
                 var dependency = Path.Combine(Root, name + ".dll");
                 var dependencyCompilation = CSharpCompilation.Create(name, new[] { CSharpSyntaxTree.ParseText("public class RequiredType { }") },
                     references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
@@ -139,7 +201,7 @@ public sealed class AssemblyInspectionTests
             }
             var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
             if (fault != "unsigned") options = options.WithCryptoKeyFile(key).WithStrongNameProvider(new DesktopStrongNameProvider());
-            var compilation = CSharpCompilation.Create("InspectionFixture", new[] { CSharpSyntaxTree.ParseText(source) }, references, options);
+            var compilation = CSharpCompilation.Create(assemblyName, new[] { CSharpSyntaxTree.ParseText(source) }, references, options);
             var path = Path.Combine(Root, "fixture.dll");
             var result = compilation.Emit(path);
             Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(value => value.ToString())));
diff --git a/tools/PluginPackaging/Program.cs b/tools/PluginPackaging/Program.cs
index 94929bbc..fd8cfaf0 100644
--- a/tools/PluginPackaging/Program.cs
+++ b/tools/PluginPackaging/Program.cs
@@ -1,4 +1,5 @@
 using System;
+using System.Collections.Frozen;
 using System.IO;
 using System.Linq;
 using System.Reflection;
@@ -17,6 +18,70 @@ internal static class Program
     /// <summary>部署沙箱目前核定的最高組件目標框架；更高版本即使本機可執行也不得送入合併。</summary>
     private static readonly Version MaximumMergeInputFramework = new(4, 6, 2);
 
+    /// <summary>
+    /// 以固定 Microsoft.NETFramework.ReferenceAssemblies.net462 1.0.3 的 Facades 清單加 netstandard
+    /// 區分相容轉送組件，避免原有 System.* 允許規則讓 facade 參考假通過。
+    /// 集合在程序啟動後不可變且大小固定，不讀取執行主機／呼叫者的目錄或設定，不保存任何操作狀態。
+    /// 組件身分依 CLR 簡名以 OrdinalIgnoreCase 比對；只拒絕完整名稱，不誤拒合法的 System.Net.Http 等框架參考。
+    /// </summary>
+    private static readonly FrozenSet<string> FrameworkFacadeNames = new[]
+    {
+        "System.Collections",
+        "System.Collections.Concurrent",
+        "System.ComponentModel",
+        "System.ComponentModel.Annotations",
+        "System.ComponentModel.EventBasedAsync",
+        "System.Diagnostics.Contracts",
+        "System.Diagnostics.Debug",
+        "System.Diagnostics.Tools",
+        "System.Diagnostics.Tracing",
+        "System.Dynamic.Runtime",
+        "System.Globalization",
+        "System.IO",
+        "System.Linq",
+        "System.Linq.Expressions",
+        "System.Linq.Parallel",
+        "System.Linq.Queryable",
+        "System.Net.NetworkInformation",
+        "System.Net.Primitives",
+        "System.Net.Requests",
+        "System.Net.WebHeaderCollection",
+        "System.ObjectModel",
+        "System.Reflection",
+        "System.Reflection.Emit",
+        "System.Reflection.Emit.ILGeneration",
+        "System.Reflection.Emit.Lightweight",
+        "System.Reflection.Extensions",
+        "System.Reflection.Primitives",
+        "System.Resources.ResourceManager",
+        "System.Runtime",
+        "System.Runtime.Extensions",
+        "System.Runtime.Handles",
+        "System.Runtime.InteropServices",
+        "System.Runtime.InteropServices.WindowsRuntime",
+        "System.Runtime.Numerics",
+        "System.Runtime.Serialization.Json",
+        "System.Runtime.Serialization.Primitives",
+        "System.Runtime.Serialization.Xml",
+        "System.Security.Principal",
+        "System.ServiceModel.Duplex",
+        "System.ServiceModel.Http",
+        "System.ServiceModel.NetTcp",
+        "System.ServiceModel.Primitives",
+        "System.ServiceModel.Security",
+        "System.Text.Encoding",
+        "System.Text.Encoding.Extensions",
+        "System.Text.RegularExpressions",
+        "System.Threading",
+        "System.Threading.Tasks",
+        "System.Threading.Tasks.Parallel",
+        "System.Threading.Timer",
+        "System.Xml.ReaderWriter",
+        "System.Xml.XDocument",
+        "System.Xml.XmlSerializer",
+        "netstandard",
+    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
+
     /// <summary>輸出固定 JSON 檢查結果；未完成檢查時不得以結束碼零假稱通過。</summary>
     /// <param name="args">受檢 DLL 的本機路徑，不含憑證或連線資訊。</param>
     /// <returns>零表示所有契約通過，一表示驗證拒絕。</returns>
@@ -60,11 +125,12 @@ internal static class Program
                     string.Equals(metadata.GetString(type.Namespace), "SpeechMessage.Dynamics.Plugins.Diagnostics", StringComparison.Ordinal) &&
                     string.Equals(metadata.GetString(type.Name), "DiagnosticsPingPlugin", StringComparison.Ordinal);
             });
-            var ok = strongName && targetFramework && allowed && noDrawing && plugin;
+            var noNetStandardFacade = HasNoFrameworkFacade(metadata, assembly);
+            var ok = strongName && targetFramework && allowed && noDrawing && plugin && noNetStandardFacade;
             output.WriteLine(JsonSerializer.Serialize(new
             {
                 ok,
-                checks = new { readable = true, strongName, targetFramework, allowedReferences = allowed, noDrawing, diagnosticsPlugin = plugin, size = true },
+                checks = new { readable = true, strongName, targetFramework, allowedReferences = allowed, noNetStandardFacade, noDrawing, diagnosticsPlugin = plugin, size = true },
                 assemblyVersion = assembly.Version.ToString(),
                 references,
                 sizeBytes = stream.Length
@@ -108,15 +174,16 @@ internal static class Program
     }
 
     /// <summary>
-    /// 在 ILRepack 前逐一驗證輸入組件宣告了不高於 v4.6.2 的 .NET Framework 目標。
+    /// 在 ILRepack 前逐一驗證輸入組件宣告了不高於 v4.6.2 的 .NET Framework 目標，且沒有 facade 身分或參考。
     /// 僅輸出固定 JSON，不回顯路徑；讀取仍由單一 PE reader 操作擁有並在返回時釋放。
     /// </summary>
     /// <param name="path">本次合併的單一輸入組件路徑。</param>
     /// <param name="output">呼叫端擁有的操作專屬輸出。</param>
-    /// <returns>只有 PE 可讀且框架版本符合上限時回傳零。</returns>
+    /// <returns>只有 PE 可讀、框架版本符合上限且未含 facade 時回傳零。</returns>
     private static int ValidateMergeInputFramework(string path, TextWriter output)
     {
         string? frameworkName;
+        bool noNetStandardFacade;
         try
         {
             using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
@@ -125,7 +192,9 @@ internal static class Program
             if (!pe.HasMetadata) return Reject("InvalidImage", output);
             var metadata = pe.GetMetadataReader();
             if (!metadata.IsAssembly) return Reject("InvalidImage", output);
-            frameworkName = ReadFrameworkName(metadata, metadata.GetAssemblyDefinition());
+            var assembly = metadata.GetAssemblyDefinition();
+            frameworkName = ReadFrameworkName(metadata, assembly);
+            noNetStandardFacade = HasNoFrameworkFacade(metadata, assembly);
         }
         catch (Exception exception) when (exception is BadImageFormatException or IOException or UnauthorizedAccessException or ArgumentException)
         {
@@ -136,14 +205,26 @@ internal static class Program
         var valid = IsFrameworkAtOrBelowLimit(frameworkName);
         output.WriteLine(JsonSerializer.Serialize(new
         {
-            ok = valid,
-            checks = new { targetFramework = valid },
+            ok = valid && noNetStandardFacade,
+            checks = new { targetFramework = valid, noNetStandardFacade },
             frameworkName,
             maximumTargetFramework = ".NETFramework,Version=v4.6.2"
         }));
-        return valid ? 0 : 1;
+        return valid && noNetStandardFacade ? 0 : 1;
     }
 
+    /// <summary>
+    /// 共用於部署 DLL 與合併前輸入檢查：組件自身及其全部參考都不得是固定清單中的 facade。
+    /// 只讀同一操作擁有的 MetadataReader，不載入組件、不追蹤磁碟相依，也不持有 reader 或參考名稱快照。
+    /// </summary>
+    /// <param name="metadata">由呼叫端 PE reader 控制生命週期的唯讀中繼資料。</param>
+    /// <param name="assembly">已確認為組件的定義。</param>
+    /// <returns>自身與所有 assembly references 都不是 facade 時才為 true。</returns>
+    private static bool HasNoFrameworkFacade(MetadataReader metadata, AssemblyDefinition assembly)
+        => !FrameworkFacadeNames.Contains(metadata.GetString(assembly.Name)) &&
+            metadata.AssemblyReferences.All(handle =>
+                !FrameworkFacadeNames.Contains(metadata.GetString(metadata.GetAssemblyReference(handle).Name)));
+
     /// <summary>只接受明確 .NET Framework 版本，並以數值版本比較拒絕高於 v4.6.2 的輸入。</summary>
     /// <param name="frameworkName">PE 中繼資料的 TargetFrameworkAttribute 值。</param>
     /// <returns>可解析且不高於核定上限時為 true。</returns>
@@ -166,7 +247,7 @@ internal static class Program
         {
             ok = false,
             reason,
-            checks = new { readable = false, strongName = false, targetFramework = false, allowedReferences = false, noDrawing = false, diagnosticsPlugin = false, size = false }
+            checks = new { readable = false, strongName = false, targetFramework = false, allowedReferences = false, noNetStandardFacade = false, noDrawing = false, diagnosticsPlugin = false, size = false }
         }));
         return 1;
     }
diff --git a/tools/PluginPackaging/Tests/Test-PluginAssembly.Tests.ps1 b/tools/PluginPackaging/Tests/Test-PluginAssembly.Tests.ps1
index 473b7d12..e939b19a 100644
--- a/tools/PluginPackaging/Tests/Test-PluginAssembly.Tests.ps1
+++ b/tools/PluginPackaging/Tests/Test-PluginAssembly.Tests.ps1
@@ -88,7 +88,7 @@ namespace SpeechMessage.Dynamics.Plugins.Diagnostics
         if ($sample -eq 'signed') {
             $passed = $code -eq 0 -and $result.ok -eq $true -and $result.checks.strongName -eq $true -and
                 $result.checks.targetFramework -eq $true -and $result.checks.allowedReferences -eq $true -and
-                $result.checks.noDrawing -eq $true -and $result.checks.diagnosticsPlugin -eq $true -and $result.checks.size -eq $true
+                $result.checks.noNetStandardFacade -eq $true -and $result.checks.noDrawing -eq $true -and $result.checks.diagnosticsPlugin -eq $true -and $result.checks.size -eq $true
         } else {
             $passed = $code -eq 1 -and $result.ok -eq $false -and $result.checks.$failedCheck -eq $false
         }
diff --git a/tools/PluginPackaging/Tests/Test-PluginBuild.Tests.ps1 b/tools/PluginPackaging/Tests/Test-PluginBuild.Tests.ps1
index 8fed9fdb..f5820b7e 100644
--- a/tools/PluginPackaging/Tests/Test-PluginBuild.Tests.ps1
+++ b/tools/PluginPackaging/Tests/Test-PluginBuild.Tests.ps1
@@ -77,7 +77,7 @@ try {
                 $inspectCode = $LASTEXITCODE
                 $jsonLine = $inspection | Where-Object { $_.ToString().StartsWith('{', [StringComparison]::Ordinal) } | Select-Object -Last 1
                 $result = $jsonLine.ToString() | ConvertFrom-Json
-                $passed = $inspectCode -eq 1 -and $result.checks.strongName -eq $false -and $result.checks.targetFramework -eq $true
+                $passed = $inspectCode -eq 1 -and $result.checks.strongName -eq $false -and $result.checks.targetFramework -eq $true -and $result.checks.noNetStandardFacade -eq $true
                 if (-not $passed) { $failureDetail = 'unsigned-debug-assembly-contract-failed' }
             }
         } else {
@@ -89,7 +89,7 @@ try {
                 $jsonLine = $inspection | Where-Object { $_.ToString().StartsWith('{', [StringComparison]::Ordinal) } | Select-Object -Last 1
                 $result = $jsonLine.ToString() | ConvertFrom-Json
                 $references = @($result.references | ForEach-Object { $_.ToString() })
-                $passed = $inspectCode -eq 0 -and $result.checks.targetFramework -eq $true -and
+                $passed = $inspectCode -eq 0 -and $result.checks.targetFramework -eq $true -and $result.checks.noNetStandardFacade -eq $true -and
                     $result.assemblyVersion -eq '1.0.0.0' -and
                     $references -notcontains 'System.Runtime' -and $references -notcontains 'netstandard'
                 if (-not $passed) { $failureDetail = 'release-assembly-check-failed' }
diff --git a/文件/規劃設計書/實作計畫-3.3/01_階段0_準備守門與可行性驗證.md b/文件/規劃設計書/實作計畫-3.3/01_階段0_準備守門與可行性驗證.md
index 55654d19..b0a82c09 100644
--- a/文件/規劃設計書/實作計畫-3.3/01_階段0_準備守門與可行性驗證.md
+++ b/文件/規劃設計書/實作計畫-3.3/01_階段0_準備守門與可行性驗證.md
@@ -182,7 +182,7 @@
 
 **先寫的測試**
 
-新增 `tools\RepoQuality\AnalyzerProbe\`：一個 `SpeechMessageStrictCode=true` 的雙目標探測專案（net462;net10.0，用來驗證目前沙箱產品目標與主機目標的分析器及語言補丁），**不加入 .sln**，故意使用 `DateTime.Now`。CI 建置它時「必須失敗」，用來證明禁用 API 有作用。腳本 `Test-AnalyzerProbe.ps1` 斷言兩個目標建置失敗、且錯誤代碼是 `RS0030`。仍有明確 net48 測試目標的守門維持獨立，不得以該測試目標代替 net462 產品驗證。
+新增 `tools\RepoQuality\AnalyzerProbe\`：一個 `SpeechMessageStrictCode=true` 的雙目標測試探測專案（維持 net48;net10.0；net48 是 .NET Framework 測試探針，不是沙箱部署產品，產品目標另由 net462 參考組件建置與測試守門驗證），**不加入 .sln**，故意使用 `DateTime.Now`。CI 建置它時「必須失敗」，用來證明禁用 API 有作用。腳本 `Test-AnalyzerProbe.ps1` 斷言兩個目標建置失敗、且錯誤代碼是 `RS0030`。仍有明確 net48 測試目標的守門維持獨立，不得以該測試目標代替 net462 產品驗證。
 
 保留原有 13 項檢查，新增缺 XML 註解必須有 CS1591、只設 StrictCode 未設文件開關必須被 SMQ0001 擋下；設定回歸要檢查實際 DocumentationFile 非空，並以正常有文件的分支建置、斷言 bin 輸出資料夾存在本次產生的 XML，不能只檢查 GenerateDocumentationFile=true。
 

```

## 完整來源 tools/PluginPackaging/Program.cs
```
using System;
using System.Collections.Frozen;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace SpeechMessage.Tools.PluginPackaging;

/// <summary>
/// 由 PowerShell 啟動的離線中繼資料檢查器；只讀 PE，不載入或執行受檢 DLL。
/// 每次程序只處理指定的檔案，串流與讀取器由同一操作確定性釋放，不保留到 static。
/// </summary>
internal static class Program
{
    /// <summary>部署沙箱目前核定的最高組件目標框架；更高版本即使本機可執行也不得送入合併。</summary>
    private static readonly Version MaximumMergeInputFramework = new(4, 6, 2);

    /// <summary>
    /// 以固定 Microsoft.NETFramework.ReferenceAssemblies.net462 1.0.3 的 Facades 清單加 netstandard
    /// 區分相容轉送組件，避免原有 System.* 允許規則讓 facade 參考假通過。
    /// 集合在程序啟動後不可變且大小固定，不讀取執行主機／呼叫者的目錄或設定，不保存任何操作狀態。
    /// 組件身分依 CLR 簡名以 OrdinalIgnoreCase 比對；只拒絕完整名稱，不誤拒合法的 System.Net.Http 等框架參考。
    /// </summary>
    private static readonly FrozenSet<string> FrameworkFacadeNames = new[]
    {
        "System.Collections",
        "System.Collections.Concurrent",
        "System.ComponentModel",
        "System.ComponentModel.Annotations",
        "System.ComponentModel.EventBasedAsync",
        "System.Diagnostics.Contracts",
        "System.Diagnostics.Debug",
        "System.Diagnostics.Tools",
        "System.Diagnostics.Tracing",
        "System.Dynamic.Runtime",
        "System.Globalization",
        "System.IO",
        "System.Linq",
        "System.Linq.Expressions",
        "System.Linq.Parallel",
        "System.Linq.Queryable",
        "System.Net.NetworkInformation",
        "System.Net.Primitives",
        "System.Net.Requests",
        "System.Net.WebHeaderCollection",
        "System.ObjectModel",
        "System.Reflection",
        "System.Reflection.Emit",
        "System.Reflection.Emit.ILGeneration",
        "System.Reflection.Emit.Lightweight",
        "System.Reflection.Extensions",
        "System.Reflection.Primitives",
        "System.Resources.ResourceManager",
        "System.Runtime",
        "System.Runtime.Extensions",
        "System.Runtime.Handles",
        "System.Runtime.InteropServices",
        "System.Runtime.InteropServices.WindowsRuntime",
        "System.Runtime.Numerics",
        "System.Runtime.Serialization.Json",
        "System.Runtime.Serialization.Primitives",
        "System.Runtime.Serialization.Xml",
        "System.Security.Principal",
        "System.ServiceModel.Duplex",
        "System.ServiceModel.Http",
        "System.ServiceModel.NetTcp",
        "System.ServiceModel.Primitives",
        "System.ServiceModel.Security",
        "System.Text.Encoding",
        "System.Text.Encoding.Extensions",
        "System.Text.RegularExpressions",
        "System.Threading",
        "System.Threading.Tasks",
        "System.Threading.Tasks.Parallel",
        "System.Threading.Timer",
        "System.Xml.ReaderWriter",
        "System.Xml.XDocument",
        "System.Xml.XmlSerializer",
        "netstandard",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>輸出固定 JSON 檢查結果；未完成檢查時不得以結束碼零假稱通過。</summary>
    /// <param name="args">受檢 DLL 的本機路徑，不含憑證或連線資訊。</param>
    /// <returns>零表示所有契約通過，一表示驗證拒絕。</returns>
    public static int Main(string[] args)
        => Run(args, Console.Out);

    /// <summary>由呼叫者擁有輸出 writer，讓並行離線驗證不必改全域 Console 或共享診斷緩衝。</summary>
    /// <param name="args">單一本機 DLL 路徑。</param>
    /// <param name="output">僅存活於此次操作的輸出目的地，本工具不負責關閉。</param>
    /// <returns>所有中繼資料契約通過才回傳零。</returns>
    internal static int Run(string[] args, TextWriter output)
    {
        if (args.Length == 2 && string.Equals(args[0], "--validate-net462-input", StringComparison.Ordinal))
            return ValidateMergeInputFramework(args[1], output);
        if (args.Length != 1) return Reject("InvalidArguments", output);
        try
        {
            // 每個受檢檔案最多十六 MiB；先以檔案長度拒絕超界，再建立唯讀 PE reader。
            using var stream = new FileStream(args[0], FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length >= 16 * 1024 * 1024) return Reject("SizeLimit", output);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) return Reject("InvalidImage", output);
            var metadata = pe.GetMetadataReader();
            if (!metadata.IsAssembly) return Reject("InvalidImage", output);
            var assembly = metadata.GetAssemblyDefinition();
            var publicKey = metadata.GetBlobBytes(assembly.PublicKey);
            var identity = new AssemblyName("InspectionOnly");
            identity.SetPublicKey(publicKey);
            var strongName = publicKey.Length > 0 && identity.GetPublicKeyToken() is { Length: > 0 } &&
                (pe.PEHeaders.CorHeader!.Flags & CorFlags.StrongNameSigned) != 0;
            var targetFramework = ReadFramework(metadata, assembly);
            var references = metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name)).ToArray();
            var allowed = references.All(name => name is "mscorlib" or "System" or "Microsoft.Xrm.Sdk" or "Microsoft.Crm.Sdk.Proxy" ||
                name.StartsWith("System.", StringComparison.Ordinal));
            var noDrawing = references.All(name => !string.Equals(name, "System.Drawing", StringComparison.Ordinal) &&
                !name.StartsWith("System.Drawing.", StringComparison.Ordinal));
            var plugin = metadata.TypeDefinitions.Any(handle =>
            {
                var type = metadata.GetTypeDefinition(handle);
                return (type.Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public &&
                    string.Equals(metadata.GetString(type.Namespace), "SpeechMessage.Dynamics.Plugins.Diagnostics", StringComparison.Ordinal) &&
                    string.Equals(metadata.GetString(type.Name), "DiagnosticsPingPlugin", StringComparison.Ordinal);
            });
            var noNetStandardFacade = HasNoFrameworkFacade(metadata, assembly);
            var ok = strongName && targetFramework && allowed && noDrawing && plugin && noNetStandardFacade;
            output.WriteLine(JsonSerializer.Serialize(new
            {
                ok,
                checks = new { readable = true, strongName, targetFramework, allowedReferences = allowed, noNetStandardFacade, noDrawing, diagnosticsPlugin = plugin, size = true },
                assemblyVersion = assembly.Version.ToString(),
                references,
                sizeBytes = stream.Length
            }));
            return ok ? 0 : 1;
        }
        catch (Exception exception) when (exception is BadImageFormatException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            // 損壞影像、不可讀檔與不合法中繼資料都是預期輸入拒絕；不輸出原始路徑或例外訊息。
            return Reject("InvalidImage", output);
        }
    }

    /// <summary>只解析 assembly 的框架屬性 blob，不反射載入待測型別或執行屬性建構式。</summary>
    /// <param name="metadata">當次 PE reader 擁有的唯讀中繼資料。</param>
    /// <param name="assembly">已確認為組件的定義。</param>
    /// <returns>只有明確宣告 .NET Framework 4.6.2 的正式部署組件才通過。</returns>
    private static bool ReadFramework(MetadataReader metadata, AssemblyDefinition assembly)
        => string.Equals(ReadFrameworkName(metadata, assembly), ".NETFramework,Version=v4.6.2", StringComparison.Ordinal);

    /// <summary>只解析 assembly 的框架屬性字串，不載入待測型別或執行使用者程式碼。</summary>
    /// <param name="metadata">當次 PE reader 擁有的唯讀中繼資料。</param>
    /// <param name="assembly">已確認為組件的定義。</param>
    /// <returns>明確的框架名稱；缺少屬性或格式錯誤時回傳 null。</returns>
    private static string? ReadFrameworkName(MetadataReader metadata, AssemblyDefinition assembly)
    {
        foreach (var handle in assembly.GetCustomAttributes())
        {
            var attribute = metadata.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
            var constructor = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (constructor.Parent.Kind != HandleKind.TypeReference) continue;
            var type = metadata.GetTypeReference((TypeReferenceHandle)constructor.Parent);
            if (!string.Equals(metadata.GetString(type.Namespace), "System.Runtime.Versioning", StringComparison.Ordinal) ||
                !string.Equals(metadata.GetString(type.Name), "TargetFrameworkAttribute", StringComparison.Ordinal)) continue;
            var blob = metadata.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() != 1) return null;
            return blob.ReadSerializedString();
        }
        return null;
    }

    /// <summary>
    /// 在 ILRepack 前逐一驗證輸入組件宣告了不高於 v4.6.2 的 .NET Framework 目標，且沒有 facade 身分或參考。
    /// 僅輸出固定 JSON，不回顯路徑；讀取仍由單一 PE reader 操作擁有並在返回時釋放。
    /// </summary>
    /// <param name="path">本次合併的單一輸入組件路徑。</param>
    /// <param name="output">呼叫端擁有的操作專屬輸出。</param>
    /// <returns>只有 PE 可讀、框架版本符合上限且未含 facade 時回傳零。</returns>
    private static int ValidateMergeInputFramework(string path, TextWriter output)
    {
        string? frameworkName;
        bool noNetStandardFacade;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length >= 16 * 1024 * 1024) return Reject("SizeLimit", output);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) return Reject("InvalidImage", output);
            var metadata = pe.GetMetadataReader();
            if (!metadata.IsAssembly) return Reject("InvalidImage", output);
            var assembly = metadata.GetAssemblyDefinition();
            frameworkName = ReadFrameworkName(metadata, assembly);
            noNetStandardFacade = HasNoFrameworkFacade(metadata, assembly);
        }
        catch (Exception exception) when (exception is BadImageFormatException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            // 預期的本機 PE 輸入錯誤以固定原因拒絕，不洩漏原路徑或例外文字。
            return Reject("InvalidImage", output);
        }

        var valid = IsFrameworkAtOrBelowLimit(frameworkName);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            ok = valid && noNetStandardFacade,
            checks = new { targetFramework = valid, noNetStandardFacade },
            frameworkName,
            maximumTargetFramework = ".NETFramework,Version=v4.6.2"
        }));
        return valid && noNetStandardFacade ? 0 : 1;
    }

    /// <summary>
    /// 共用於部署 DLL 與合併前輸入檢查：組件自身及其全部參考都不得是固定清單中的 facade。
    /// 只讀同一操作擁有的 MetadataReader，不載入組件、不追蹤磁碟相依，也不持有 reader 或參考名稱快照。
    /// </summary>
    /// <param name="metadata">由呼叫端 PE reader 控制生命週期的唯讀中繼資料。</param>
    /// <param name="assembly">已確認為組件的定義。</param>
    /// <returns>自身與所有 assembly references 都不是 facade 時才為 true。</returns>
    private static bool HasNoFrameworkFacade(MetadataReader metadata, AssemblyDefinition assembly)
        => !FrameworkFacadeNames.Contains(metadata.GetString(assembly.Name)) &&
            metadata.AssemblyReferences.All(handle =>
                !FrameworkFacadeNames.Contains(metadata.GetString(metadata.GetAssemblyReference(handle).Name)));

    /// <summary>只接受明確 .NET Framework 版本，並以數值版本比較拒絕高於 v4.6.2 的輸入。</summary>
    /// <param name="frameworkName">PE 中繼資料的 TargetFrameworkAttribute 值。</param>
    /// <returns>可解析且不高於核定上限時為 true。</returns>
    private static bool IsFrameworkAtOrBelowLimit(string? frameworkName)
    {
        const string prefix = ".NETFramework,Version=v";
        if (frameworkName is null || !frameworkName.StartsWith(prefix, StringComparison.Ordinal) ||
            !Version.TryParse(frameworkName.AsSpan(prefix.Length), out var version))
            return false;
        return version.Major >= 0 && version <= MaximumMergeInputFramework;
    }

    /// <summary>驗證尚未完整成功時一律失敗；固定代碼與布林欄位不包含檔案原文或簽章金鑰資訊。</summary>
    /// <param name="reason">本工具擁有的固定原因代碼。</param>
    /// <param name="output">呼叫端擁有的操作專屬輸出。</param>
    /// <returns>供 PowerShell／CI 判定的拒絕結束碼。</returns>
    private static int Reject(string reason, TextWriter output)
    {
        output.WriteLine(JsonSerializer.Serialize(new
        {
            ok = false,
            reason,
            checks = new { readable = false, strongName = false, targetFramework = false, allowedReferences = false, noNetStandardFacade = false, noDrawing = false, diagnosticsPlugin = false, size = false }
        }));
        return 1;
    }
}

```

## 完整來源 tools/PluginPackaging.Tests/AssemblyInspectionTests.cs
```
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

```

## 完整來源 build/PluginMerge.targets
```
<Project>
  <!-- 套件預設會覆寫 bin 並併入所有相依；此自訂入口只產生獨立的沙箱部署組件。
       只有明確指定 SpeechMessagePackagePlugin=true 才打包；一般建置不讀金鑰、不建立 artifacts。
       正式金鑰只能讀取程序環境變數，不複製內容、不設後備、不由 MSBuild 同名屬性冒充。 -->
  <Target Name="PackageSpeechMessagePlugin" AfterTargets="Build" Condition="'$(SpeechMessagePackagePlugin)' == 'true' And '$(DesignTimeBuild)' != 'true'">
    <PropertyGroup>
      <_SpeechMessagePluginKeyFile>$([System.Environment]::GetEnvironmentVariable('SPEECHMESSAGE_PLUGIN_KEYFILE'))</_SpeechMessagePluginKeyFile>
      <SpeechMessagePluginOutputDirectory Condition="'$(SpeechMessagePluginOutputDirectory)' == ''">$(MSBuildThisFileDirectory)../artifacts/plugins</SpeechMessagePluginOutputDirectory>
      <_SpeechMessagePluginOutput>$(SpeechMessagePluginOutputDirectory)/$(TargetFileName)</_SpeechMessagePluginOutput>
    </PropertyGroup>
    <Error Condition="'$(_SpeechMessagePluginKeyFile)' == '' And '$(Configuration)' == 'Release'"
           Code="SMP0001" Text="Release 外掛打包必須設定 SPEECHMESSAGE_PLUGIN_KEYFILE；請由使用者準備 repo 外的正式強式名稱金鑰。" />
    <Error Condition="'$(_SpeechMessagePluginKeyFile)' != '' And !Exists('$(_SpeechMessagePluginKeyFile)')"
           Code="SMP0003" Text="SPEECHMESSAGE_PLUGIN_KEYFILE 指定的金鑰不存在；禁止改用其他金鑰或產生正式金鑰。" />
    <Warning Condition="'$(_SpeechMessagePluginKeyFile)' == '' And '$(Configuration)' != 'Release'"
             Code="SMP0002" Text="未設定 SPEECHMESSAGE_PLUGIN_KEYFILE；本次 Debug 只產生未簽章診斷組件，不可部署。" />
    <ItemGroup>
      <!-- 第一個輸入是唯一公開外殼；其餘型別全部內部化，SDK 仍由 D365 沙箱提供。 -->
      <_SpeechMessageMergeInput Include="$(TargetPath)" />
      <_SpeechMessageMergeInput Include="$(TargetDir)SpeechMessage.Localization.dll" />
      <_SpeechMessageMergeInput Include="$(TargetDir)LineMessagingProcessor.Core.dll" />
      <_SpeechMessageMergeInput Include="$(TargetDir)Newtonsoft.Json.dll" />
    </ItemGroup>
    <!-- 每個被合併輸入都必須先證明目標不高於伺服器核定的 v4.6.2；
         檢查器以 PE metadata 工作，不載入或執行組件。使用 dotnet build 的標準 restore/build 流程，
         並強制還原，避免依賴方案或其他工具先前留下的 assets；此工具沒有方案相依，不用靜態圖還原。 -->
    <Exec Command="dotnet build &quot;$(MSBuildThisFileDirectory)../tools/PluginPackaging/PluginPackaging.csproj&quot; --configuration Release --framework net10.0 --nologo -p:RestoreUseStaticGraphEvaluation=false -p:RestoreForce=true"
          WorkingDirectory="$(MSBuildThisFileDirectory)../tools/PluginPackaging" />
    <Exec Command="dotnet &quot;$(MSBuildThisFileDirectory)../tools/PluginPackaging/bin/Release/net10.0/PluginPackaging.dll&quot; --validate-net462-input &quot;%(_SpeechMessageMergeInput.Identity)&quot;"
          WorkingDirectory="$(MSBuildThisFileDirectory).." />
    <MakeDir Directories="$(SpeechMessagePluginOutputDirectory)" />
    <ILRepack InputAssemblies="@(_SpeechMessageMergeInput)"
              OutputFile="$(_SpeechMessagePluginOutput)"
              TargetKind="Dll"
              Internalize="true"
              KeyFile="$(_SpeechMessagePluginKeyFile)"
              LibraryPath="$(TargetDir);$(TargetFrameworkDirectory)"
              DebugInfo="false"
              Parallel="true" />
  </Target>
</Project>

```


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.

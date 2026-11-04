# CCG reviewer Task: integration-p0-review-net462-10

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P0.Worktree

## Request
# 第 10 份：net462 改版獨立審查

使用者 2026-11-04 指定 ClaudeOnly；回報名稱必須為「單模型（使用者指定 ClaudeOnly）」。這是新的審查，先前 01～09 不涵蓋這些修改。

## 範圍與限制

基底 dce8f961，目前 HEAD d3c2f4c3 加本包的未提交變更。保留途中使用者提交 160553b9、d3c2f4c3，不改寫歷史。

只做唯讀審查。不得修改檔案、連線 D365／LINE、讀取或輸出任何 User Secrets、appsettings、金鑰、授權、憑證或歷史日誌。不要重新跑會連線的 CLI；可直接依本包的來源與本機結果審查。排除歷次 .ccg 輸出、wp-evidence、research、生成檔、二進位與進度頁。若需補讀只限下列產品／測試／建置／規範檔；不要擴大 repo 搜尋。

## 核定與重點

1. Plugins net462；Core／Localization net462;net10.0；測試可 net48 但實際載入 net462。Framework 分支不可因 NET48 不再定義而走錯；用 ReferenceAssemblies 編譯，分析器與 NuGetAudit 維持嚴格。

2. 合併只含 Plugins、Localization、Core、Newtonsoft.Json。每個輸入 Framework <=4.6.2，結果為 v4.6.2；net48 反向樣本必須拒絕。保持簽章、AssemblyVersion=1.0.0.0、allowedReferences、noDrawing、診斷型別、大小守門；不得合併 netstandard／System.Runtime facade。

3. 跨使用者／跨教會隔離、單一有界資源擁有者與 Dispose、不外洩機密、設計 10.5／10.9 例外分工、Framework 與 net10 行為一致，守門不得假綠燈。

4. 原 TargetFrameworkVersionValidator／FormatException 僅支持高度可信根因推論；net462 是否接受仍待使用者實機註冊。mscorlib FrameworkFileVersion=4.8.x 不代表 Plugin TargetFramework。不得要求改伺服器或用 Plugin Registration Tool。

5. 核對包裝工具以一般 dotnet build 強制還原且停用 nested static graph；測試不能把任意建置失敗當功能紅燈。文件的新 plan 預期僅 CreateAssembly/CreateType/CreateStep；其他操作停止回報。

## 已實測（均離線，並非 D365 驗收）

Release 全方案 build 0 errors/0 warnings；13 個測試目標共1121/1121含 ChurchReport596；Debug tracer7/7；I18n108/108；PluginPackaging16/16；ToolUtility63/63（方案外舊專案仍有原 warning）；套件弱點清單無弱點；編碼514檔零問題；重複碼26專案879檔零新增；I18n verify通過；套件39專案149引用零失敗及路徑5/5；分析器26檢查零失敗且RS0030；assembly樣本10/10（含拒net48）；build樣本6/6；正式簽章打包check全true、AssemblyVersion1.0.0.0/FileVersion1.0.0.37、size723456、10 references、無facade。四輸入v4.6.2/v4.6.2/v4.6.2/v4.5。所有net462套件資產均Framework，未解析netstandard2.0；詳見報告第8節。

## 輸出要求

以繁體中文列出 Critical / High / Medium / Low / Info，每個發現給檔案、行或方法、具體失敗情境與最小修正；沒有就明確寫無。區分可重現問題、已核定變更、無法離線證明的限制。最後說明是否有未處理 Critical/High。不讀取或回顯任何原始例外／設定或秘密。

## 審查檔案
- .trellis/spec/backend/deployment-contract.md
- .trellis/spec/backend/index.md
- .trellis/spec/backend/plugin-packaging-contract.md
- Directory.Build.targets
- LineMessagingProcessor.Core/LineMessagingProcessor.Core.csproj
- LineMessagingProcessor.Core/LinePushClient.cs
- LineMessagingProcessor.Core/LinePushRequest.cs
- LineMessagingProcessor.Tests/LinePushClientTests.cs
- SpeechMessage.Dynamics.Deployment.Tests/DeploymentContractTests.cs
- SpeechMessage.Dynamics.Deployment.Tests/SpeechMessage.Dynamics.Deployment.Tests.csproj
- SpeechMessage.Dynamics.Deployment/Contracts.cs
- SpeechMessage.Dynamics.Plugins.Tests/DiagnosticsPingPluginTests.cs
- SpeechMessage.Dynamics.Plugins/SpeechMessage.Dynamics.Plugins.csproj
- SpeechMessage.Localization.Tests/SystemTextCatalogTests.cs
- SpeechMessage.Localization/ISystemTextCatalog.cs
- SpeechMessage.Localization/SpeechMessage.Localization.csproj
- SpeechMessage.Localization/SystemTextTable.cs
- build/PluginMerge.targets
- src-shared/Polyfills/IsExternalInit.cs
- src-shared/Polyfills/NotNullWhenAttribute.cs
- tools/PluginPackaging.Tests/AssemblyInspectionTests.cs
- tools/PluginPackaging/Program.cs
- tools/PluginPackaging/Tests/Test-PluginAssembly.Tests.ps1
- tools/PluginPackaging/Tests/Test-PluginBuild.Tests.ps1
- 文件/規劃設計書/實作計畫-3.3/00_總覽與執行規則.md
- 文件/規劃設計書/實作計畫-3.3/01_階段0_準備守門與可行性驗證.md
- 文件/規劃設計書/實作計畫-3.3/02_階段1_基礎層與國際化地基.md
- 文件/規劃設計書/實作計畫-3.3/03_階段2-4_共用送訊_Webhook_金流.md
- 文件/規劃設計書/實作計畫-3.3/04_階段5-8_Plugin重建_切換_收尾.md
- 文件/規劃設計書/實作計畫-3.3/90_附錄_規格與範本.md
- 文件/規劃設計書/實作計畫-3.3/驗收/P0-13_部署失敗調查報告_供Claude複核.md
- 文件/規劃設計書/實作計畫-3.3/驗收/P0_Plugin骨架佈署步驟.md

## 本次差異
```diff
diff --git a/.trellis/spec/backend/deployment-contract.md b/.trellis/spec/backend/deployment-contract.md
index 0ec95e6f6..3570a00e4 100644
--- a/.trellis/spec/backend/deployment-contract.md
+++ b/.trellis/spec/backend/deployment-contract.md
@@ -56,15 +56,22 @@ minutes and deletes the task in finally. Caller cancellation is not notified.
 ## Offline verification
 
 Keep all original 17 test methods and every subsequently added case; 17 is the original contract list, not the current suite total. L1 uses operation-local in-memory SDK services;
-L2 emits an unsigned net48 PE into the test's unique temporary folder and reads
-metadata without Assembly.Load. Dispose readers, timers, locks and temp fixtures.
+L2 emits an unsigned net462 PE into the test's unique temporary folder and reads
+metadata without Assembly.Load. A test runner may itself target net48, but the
+generated deployment fixture must exercise the net462 Plugin metadata contract.
+Dispose readers, timers, locks and temp fixtures.
 CI includes the Deployment project and a separate >=80% coverage report.
 Actual WCF/WS-Trust connectivity and D365 sandbox behavior remain P0-13 user
 acceptance, not a claim made by the offline suite.
 
 ## Minimal P0-13 acceptance (user approved 2026-11-04)
 
-P0-13 is user-operated trace-only sandbox acceptance. Only OrganizationUrl,
+P0-13 is user-operated trace-only sandbox acceptance. The Plugin deployment
+assembly targets .NET Framework 4.6.2 per the user's 2026-11-04 D21
+authorization; successful registration on the target D365 host remains to be
+confirmed by the user. The smoke field `FrameworkFileVersion=4.8.x` describes
+the server's loaded `mscorlib` runtime file, not the Plugin target framework.
+Only OrganizationUrl,
 Username and Password under the Deployment organization User Secrets are needed;
 no test LINE identity or token is required. Plugins and Deployment have no direct
 or transitive ImageSharp dependency, so these project builds need no Six Labors
diff --git a/.trellis/spec/backend/index.md b/.trellis/spec/backend/index.md
index 6b2b38d63..97c7d8d19 100644
--- a/.trellis/spec/backend/index.md
+++ b/.trellis/spec/backend/index.md
@@ -20,10 +20,10 @@ This directory contains guidelines for backend development. Fill in each file wi
 | [Quality Guidelines](./quality-guidelines.md) | Code standards, forbidden patterns | To fill |
 | [CCG Review Summary Contract](./ccg-review-summary-contract.md) | Requested-backend success, explicit review mode, dual completion and independent probe diagnostics | Established |
 | [Central Package Management](./central-package-management.md) | Actual project scope, snapshot opt-out, XML-aware version guard, and restore validation | Established |
-| [Strict Analyzer Contract](./strict-analyzer-contract.md) | Late opt-in evaluation, exact diagnostic probes, legacy isolation, and net48 polyfills | Established |
+| [Strict Analyzer Contract](./strict-analyzer-contract.md) | Late opt-in evaluation, exact diagnostic probes, legacy isolation, and .NET Framework polyfills | Established |
 | [Solution CI Contract](./solution-ci-contract.md) | Offline Windows checks, per-target coverage, optional signing and temp-key cleanup | Established |
 | [Deployment CLI Contract](./deployment-contract.md) | User Secrets-only isolated commands, metadata plans, safe diagnostics and offline verification | Established |
-| [Plugin Packaging Contract](./plugin-packaging-contract.md) | Separate four-input merge, process-environment signing, offline metadata checks and sandbox exception ownership | Established |
+| [Plugin Packaging Contract](./plugin-packaging-contract.md) | Separate four-input merge targeting .NET Framework 4.6.2, process-environment signing, offline metadata checks and sandbox exception ownership | Established |
 | [Text Encoding Contract](./text-encoding-contract.md) | Tracked-file boundaries, byte/token checks, lossless conversion, and safe output ownership | Established |
 | [Development Configuration Safety](./development-configuration-safety.md) | Development-only providers, baseline token rejection, test organization guard and Production compatibility | Established |
 | [Duplicate Source Ratchet](./duplicate-source-ratchet.md) | Solution-scoped content/type duplicates, baseline identity validation and no parent-worktree traversal | Established |
diff --git a/.trellis/spec/backend/plugin-packaging-contract.md b/.trellis/spec/backend/plugin-packaging-contract.md
index 442f1433f..ffdcdd4c0 100644
--- a/.trellis/spec/backend/plugin-packaging-contract.md
+++ b/.trellis/spec/backend/plugin-packaging-contract.md
@@ -2,8 +2,10 @@
 
 ## 1. Scope / Trigger
 
-P0-10 builds the net48 DiagnosticsPingPlugin shell and a separate deployment DLL.
-All verification is offline. No package test or build connects to D365 or LINE.
+P0-10 originally built the net48 DiagnosticsPingPlugin shell. Per the user's
+2026-11-04 D21 authorization, the deployment Plugin and every input merged into
+it now target .NET Framework 4.6.2 (`net462`). The separate deployment DLL is
+validated offline. No package test or build connects to D365 or LINE.
 
 ## 2. Signatures
 
@@ -28,6 +30,19 @@ It emits one JSON result per DLL and fails the whole run if any DLL fails.
 - Merge exactly the primary DLL, Localization, Core, and Newtonsoft.Json.
   Internalize dependencies, leave Xrm SDK external, and write only the dedicated
   `artifacts/plugins` deployment output. Never overwrite the original test DLL.
+- The standalone .NET 10 metadata validator must be built with the ordinary
+  `dotnet build` restore/build flow and a forced restore when invoked by the
+  packaging target. Disable static-graph restore for this nested tool build so
+  it cannot accidentally depend on another project's `obj/project.assets.json`
+  or intermediate-path calculation. Do not invoke nested `MSBuild` targets as
+  `Restore;Build` from the active solution build.
+- The primary Plugin, Localization, Core, and Newtonsoft.Json merge inputs must
+  each declare a .NET Framework target no higher than v4.6.2; the merged
+  deployment assembly must declare v4.6.2. The metadata checker reads PE
+  metadata without loading the files. Do not merge .NET Standard facades,
+  including `System.Runtime`.
+- `net48` may remain a test-host target, but tests that reference product
+  projects must resolve and verify their actual `net462` product outputs.
 - Read the formal key only from process environment
   `SPEECHMESSAGE_PLUGIN_KEYFILE`. Do not read, copy, print, or record its contents.
   Never generate a replacement formal key.
@@ -55,7 +70,8 @@ It emits one JSON result per DLL and fails the whole run if any DLL fails.
 
 | Input / condition | Required result |
 | --- | --- |
-| Signed net48 public DiagnosticsPingPlugin, allowed references, under 16 MiB | JSON ok=true, exit 0 |
+| Signed net462 public DiagnosticsPingPlugin, allowed references, under 16 MiB | JSON ok=true, exit 0 |
+| Signed net48 public DiagnosticsPingPlugin | targetFramework=false, exit 1 |
 | No public key/token or no StrongNameSigned flag | strongName=false, exit 1 |
 | Wrong/missing TargetFrameworkAttribute | targetFramework=false, exit 1 |
 | Reference outside mscorlib, System, System.*, Xrm SDK/Proxy | allowedReferences=false, exit 1 |
@@ -81,6 +97,8 @@ Keep ordinary Debug/Release-without-key builds, opt-in missing/invalid-key
 failures, and signed/unsigned samples and independently inject framework, dependency,
 visibility, drawing, size, image and mixed-directory faults. Temporary test
 keys belong to a unique fixture, are never formal keys, and must be deleted.
+The signed positive fixture targets net462; a signed net48 fixture is a
+required negative case so the previous target cannot return unnoticed.
 The in-process helper suite must also prove immediate exclusive file reopening;
 its writer is operation-owned, avoiding global Console replacement in tests.
 
diff --git a/Directory.Build.targets b/Directory.Build.targets
index 8b1fc8d0e..15e86ca59 100644
--- a/Directory.Build.targets
+++ b/Directory.Build.targets
@@ -45,9 +45,9 @@
     <AdditionalFiles Include="$(MSBuildThisFileDirectory)build/Plugin/BannedSymbols.txt" />
   </ItemGroup>
 
-  <!-- 補丁只補 net48 缺少的編譯器中繼資料，不在 net10 重複宣告框架型別。
+  <!-- 補丁只補 .NET Framework 缺少的編譯器中繼資料，不在 net10 重複宣告框架型別。
        使用連結檔確保所有雙目標程式庫共用一份定義，不建立額外狀態或組件相依。 -->
-  <ItemGroup Condition="'$(SpeechMessageStrictCode)' == 'true' And '$(TargetFramework)' == 'net48'">
+  <ItemGroup Condition="'$(SpeechMessageStrictCode)' == 'true' And ('$(TargetFramework)' == 'net48' Or '$(TargetFramework)' == 'net462')">
     <Compile Include="$(MSBuildThisFileDirectory)src-shared/Polyfills/IsExternalInit.cs" Link="Polyfills/IsExternalInit.cs" />
     <Compile Include="$(MSBuildThisFileDirectory)src-shared/Polyfills/NotNullWhenAttribute.cs" Link="Polyfills/NotNullWhenAttribute.cs" />
   </ItemGroup>
diff --git a/LineMessagingProcessor.Core/LineMessagingProcessor.Core.csproj b/LineMessagingProcessor.Core/LineMessagingProcessor.Core.csproj
index 24aed0004..c9f08fcab 100644
--- a/LineMessagingProcessor.Core/LineMessagingProcessor.Core.csproj
+++ b/LineMessagingProcessor.Core/LineMessagingProcessor.Core.csproj
@@ -1,6 +1,6 @@
 <Project Sdk="Microsoft.NET.Sdk">
   <PropertyGroup>
-    <TargetFrameworks>net48;net10.0</TargetFrameworks>
+    <TargetFrameworks>net462;net10.0</TargetFrameworks>
     <SpeechMessageStrictCode>true</SpeechMessageStrictCode>
     <!-- SDK 在 targets 之前決定 XML 文件輸出；嚴格開關與文件開關必須同在專案本體。 -->
     <GenerateDocumentationFile>true</GenerateDocumentationFile>
@@ -10,7 +10,7 @@
     <InternalsVisibleTo Include="LineMessagingProcessor.Tests" />
     <InternalsVisibleTo Include="SpeechMessage.Dynamics.Plugins.Tests" />
   </ItemGroup>
-  <ItemGroup Condition="'$(TargetFramework)' == 'net48'">
+  <ItemGroup Condition="'$(TargetFramework)' == 'net462'">
     <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="all" />
     <Reference Include="System.Net.Http" />
   </ItemGroup>
diff --git a/LineMessagingProcessor.Core/LinePushClient.cs b/LineMessagingProcessor.Core/LinePushClient.cs
index 8b454a4f5..c2c51dcf6 100644
--- a/LineMessagingProcessor.Core/LinePushClient.cs
+++ b/LineMessagingProcessor.Core/LinePushClient.cs
@@ -12,7 +12,7 @@ namespace LineMessagingProcessor.Core;
 
 /// <summary>
 /// 固定 LINE push 端點的單次傳輸實作，沒有重試佇列、背景工作或管理者 notifier。
-/// net48 的程序級共用 HttpClient 不含使用者狀態，net10 的 HttpClient 由主機擁有；
+/// .NET Framework 的程序級共用 HttpClient 不含使用者狀態，net10 的 HttpClient 由主機擁有；
 /// 每個操作獨佔 HTTP 訊息、十秒逾時與取消連結，結束前按 using 次序釋放。
 /// </summary>
 public sealed class LinePushClient : ILinePushClient
@@ -24,7 +24,7 @@ public sealed class LinePushClient : ILinePushClient
     /// <summary>固定十秒的 Core 內部期限，不讓未驗證輸入延長資源生命週期。</summary>
     private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
 
-#if NET48
+#if NETFRAMEWORK
     /// <summary>
     /// 沙箱 AppDomain／程序是此唯一客戶端與連線池的 owner，卸載時一併回收。
     /// 它不使用 Cookie、預設認證或自動轉址，沒有 per-user 狀態；每個傳輸取消與內容另有 using owner。
@@ -37,7 +37,7 @@ public sealed class LinePushClient : ILinePushClient
 
     /// <summary>連線由呼叫端擁有；必須使用無 Cookie、無預設憑證且不自動轉址的管線。</summary>
     /// <param name="httpClient">可重用且無教會／使用者狀態的客戶端；Core 不 Dispose 它。</param>
-#if NET48
+#if NETFRAMEWORK
     internal LinePushClient(HttpClient httpClient) : this(httpClient, StartTimeout) { }
 #else
     public LinePushClient(HttpClient httpClient) : this(httpClient, StartTimeout) { }
@@ -59,7 +59,7 @@ public sealed class LinePushClient : ILinePushClient
     /// <returns>不持有 request／response／credential 的結果。</returns>
     public async Task<LinePushResult> PushAsync(LineChannelCredential credential, LinePushRequest request, CancellationToken ct)
     {
-#if NET48
+#if NETFRAMEWORK
         if (credential is null) throw new ArgumentNullException(nameof(credential));
         if (request is null) throw new ArgumentNullException(nameof(request));
 #else
@@ -106,7 +106,7 @@ public sealed class LinePushClient : ILinePushClient
         catch (HttpRequestException exception)
         {
             // 本層沒有收到 HTTP 回應，原始實例交給最終邊界；不能在可恢復重試階段重複告警。
-            // net48 不會像 net10 自動把同時發生的 caller 取消正規化，因此在此明確優先辨識。
+            // .NET Framework 不會像 net10 自動把同時發生的 caller 取消正規化，因此在此明確優先辨識。
             ct.ThrowIfCancellationRequested();
             return new LinePushResult(LinePushOutcome.Retryable, null, null, exception);
         }
@@ -117,8 +117,11 @@ public sealed class LinePushClient : ILinePushClient
     /// <returns>尚未與其他操作共用的新來源。</returns>
     private static CancellationTokenSource StartTimeout(TimeSpan duration) => new(duration);
 
-#if NET48
-    /// <summary>只在 AppDomain 建立一次無狀態池；TLS 依 OS／Framework 4.8 預設，不改程序全域 TLS。</summary>
+#if NETFRAMEWORK
+    /// <summary>
+    /// 只在 AppDomain 建立一次無狀態連線池，不覆寫程序級 TLS 設定。
+    /// TLS 仍受沙箱 .NET Framework 目標相容開關與作業系統設定影響；net462 的 LINE 推播須在 P2-04 前置另行驗收。
+    /// </summary>
     /// <returns>由程序擁有、沒有 Cookie 或使用者憑證的客戶端。</returns>
     private static HttpClient CreateSharedClient()
     {
diff --git a/LineMessagingProcessor.Core/LinePushRequest.cs b/LineMessagingProcessor.Core/LinePushRequest.cs
index f3e215cc0..5ecc85c70 100644
--- a/LineMessagingProcessor.Core/LinePushRequest.cs
+++ b/LineMessagingProcessor.Core/LinePushRequest.cs
@@ -23,7 +23,7 @@ public sealed class LinePushRequest
             if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                 throw new ArgumentException("A valid single-recipient identifier is required.", nameof(userId));
         }
-#if NET48
+#if NETFRAMEWORK
         if (messages is null) throw new ArgumentNullException(nameof(messages));
 #else
         ArgumentNullException.ThrowIfNull(messages);
diff --git a/LineMessagingProcessor.Tests/LinePushClientTests.cs b/LineMessagingProcessor.Tests/LinePushClientTests.cs
index 17569e86d..9f1584097 100644
--- a/LineMessagingProcessor.Tests/LinePushClientTests.cs
+++ b/LineMessagingProcessor.Tests/LinePushClientTests.cs
@@ -27,18 +27,25 @@ public sealed class LinePushClientTests
     private static readonly Guid RetryKey = new("11111111-2222-3333-4444-555555555555");
 
     /// <summary>
-    /// 保護 Plugin 使用的 Core 必須來自 net462 編譯輸出，並確認該輸出包含 .NET Framework 專用的無參數客戶端。
-    /// 測試透過組件參考的實際載入中繼資料斷言，不以測試專案的目標框架推定產品分支已正確選取。
+    /// 保護雙目標參考依測試宿主選出正確產品輸出：net48 宿主必須載入 Plugin 使用的 net462
+    /// 及 Framework 專用無參數客戶端；net10 宿主則載入 net10 與注入 HttpClient 的公開建構式。
+    /// 測試檢查實際載入組件和建構式，不能只由測試專案的目標框架推定產品分支。
     /// </summary>
-    [Fact(DisplayName = "Plugin Core 專案參考選取 net462 Framework 實作")]
-    public void Assembly_TargetFramework_AndFrameworkClientBranch_AreNet462()
+    [Fact(DisplayName = "Plugin Core 專案參考選取宿主對應產品實作")]
+    public void Assembly_TargetFramework_AndClientBranch_MatchHost()
     {
         var assembly = typeof(LinePushClient).Assembly;
         var attribute = assembly.GetCustomAttributes(typeof(TargetFrameworkAttribute), false)
             .Cast<TargetFrameworkAttribute>().Single();
 
+#if NETFRAMEWORK
         Assert.Equal(".NETFramework,Version=v4.6.2", attribute.FrameworkName);
         Assert.NotNull(typeof(LinePushClient).GetConstructor(Type.EmptyTypes));
+#else
+        Assert.Equal(".NETCoreApp,Version=v10.0", attribute.FrameworkName);
+        Assert.Null(typeof(LinePushClient).GetConstructor(Type.EmptyTypes));
+        Assert.NotNull(typeof(LinePushClient).GetConstructor(new[] { typeof(HttpClient) }));
+#endif
     }
 
     /// <summary>成功回應注入固定 ID；斷言 URL、方法、兩個逐請求標頭及共用標頭沒有被污染。</summary>
diff --git a/SpeechMessage.Dynamics.Deployment.Tests/DeploymentContractTests.cs b/SpeechMessage.Dynamics.Deployment.Tests/DeploymentContractTests.cs
index 22b09d205..32f0e014d 100644
--- a/SpeechMessage.Dynamics.Deployment.Tests/DeploymentContractTests.cs
+++ b/SpeechMessage.Dynamics.Deployment.Tests/DeploymentContractTests.cs
@@ -1031,11 +1031,11 @@ internal sealed class Fixture : IDisposable
         ["name"] = Image.Name, ["version"] = version, ["publickeytoken"] = token, ["content"] = Image.Content,
         ["isolationmode"] = new OptionSetValue(2), ["sourcetype"] = new OptionSetValue(0)
     });
-    /// <summary>離線產生沒有金鑰的最小 net48 樣本，僅供 MetadataReader 測試。</summary>
+    /// <summary>離線產生沒有金鑰的最小 net462 樣本，僅供 MetadataReader 測試。</summary>
     /// <param name="extra">是否注入未註冊型別。</param><returns>不載入執行的 PE 路徑。</returns>
     public string Emit(bool extra = false)
     {
-        var source = "[assembly:System.Runtime.Versioning.TargetFramework(\".NETFramework,Version=v4.8\")] " +
+        var source = "[assembly:System.Runtime.Versioning.TargetFramework(\".NETFramework,Version=v4.6.2\")] " +
             "namespace Microsoft.Xrm.Sdk { public interface IPlugin { } } namespace SpeechMessage.Dynamics.Plugins { public abstract class PluginShellBase : Microsoft.Xrm.Sdk.IPlugin { } } " +
             "namespace SpeechMessage.Dynamics.Plugins.Diagnostics { public sealed class DiagnosticsPingPlugin : SpeechMessage.Dynamics.Plugins.PluginShellBase { } " +
             (extra ? "public sealed class UnregisteredPlugin : Microsoft.Xrm.Sdk.IPlugin { }" : "") + " }";
diff --git a/SpeechMessage.Dynamics.Deployment.Tests/SpeechMessage.Dynamics.Deployment.Tests.csproj b/SpeechMessage.Dynamics.Deployment.Tests/SpeechMessage.Dynamics.Deployment.Tests.csproj
index b7e146290..f6f9920fd 100644
--- a/SpeechMessage.Dynamics.Deployment.Tests/SpeechMessage.Dynamics.Deployment.Tests.csproj
+++ b/SpeechMessage.Dynamics.Deployment.Tests/SpeechMessage.Dynamics.Deployment.Tests.csproj
@@ -15,7 +15,7 @@
     <PackageReference Include="xunit" />
     <PackageReference Include="xunit.runner.visualstudio" PrivateAssets="all" />
     <PackageReference Include="coverlet.collector" PrivateAssets="all" />
-    <!-- 只用 net48 參考編譯器產生惰性測試 PE，不參考或執行正式 Plugin。 -->
-    <Content Include="$(NuGetPackageRoot)microsoft.netframework.referenceassemblies.net48/1.0.3/build/.NETFramework/v4.8/mscorlib.dll" Link="Framework/mscorlib.dll" CopyToOutputDirectory="PreserveNewest" />
+    <!-- 只用 net462 參考編譯器產生惰性測試 PE，不參考或執行正式 Plugin。 -->
+    <Content Include="$(NuGetPackageRoot)microsoft.netframework.referenceassemblies.net462/1.0.3/build/.NETFramework/v4.6.2/mscorlib.dll" Link="Framework/mscorlib.dll" CopyToOutputDirectory="PreserveNewest" />
   </ItemGroup>
 </Project>
diff --git a/SpeechMessage.Dynamics.Deployment/Contracts.cs b/SpeechMessage.Dynamics.Deployment/Contracts.cs
index c60562ee7..11fab5c60 100644
--- a/SpeechMessage.Dynamics.Deployment/Contracts.cs
+++ b/SpeechMessage.Dynamics.Deployment/Contracts.cs
@@ -50,7 +50,7 @@ internal sealed record StepRegistration
 }
 
 /// <summary>
-/// 從有界 PE 讀出的單次組件描述。保留 DLL bytes 只供本次 apply，從不載入或執行 net48 程式碼，
+/// 從有界 PE 讀出的單次組件描述。保留 DLL bytes 只供本次 apply，從不載入或執行待驗證組件的程式碼，
 /// 也不將此物件放入 static、快取或跨命令狀態。
 /// </summary>
 internal sealed record AssemblyImage(string Name, string Version, string PublicKeyToken, string Content, string Sha256, IReadOnlyList<string> PluginTypes);
diff --git a/SpeechMessage.Dynamics.Plugins.Tests/DiagnosticsPingPluginTests.cs b/SpeechMessage.Dynamics.Plugins.Tests/DiagnosticsPingPluginTests.cs
index d78c3543c..5d1220c34 100644
--- a/SpeechMessage.Dynamics.Plugins.Tests/DiagnosticsPingPluginTests.cs
+++ b/SpeechMessage.Dynamics.Plugins.Tests/DiagnosticsPingPluginTests.cs
@@ -28,7 +28,7 @@ public sealed class DiagnosticsPingPluginTests
     private const string UserId = "U11111111111111111111111111111111";
 
     /// <summary>
-    /// 保護部署外殼只能以伺服器已接受的 .NET Framework 4.6.2 為目標；
+    /// 保護部署外殼只能以使用者依 D21 核定的 .NET Framework 4.6.2 為目標；
     /// 直接讀取測試實際載入的 Plugin 組件屬性，避免只看專案設定或測試專案本身而產生假綠燈。
     /// </summary>
     [Fact(DisplayName = "Plugin 參考的正式組件目標為 Framework 4.6.2")]
diff --git a/SpeechMessage.Dynamics.Plugins/SpeechMessage.Dynamics.Plugins.csproj b/SpeechMessage.Dynamics.Plugins/SpeechMessage.Dynamics.Plugins.csproj
index 052cc3656..1bd36963a 100644
--- a/SpeechMessage.Dynamics.Plugins/SpeechMessage.Dynamics.Plugins.csproj
+++ b/SpeechMessage.Dynamics.Plugins/SpeechMessage.Dynamics.Plugins.csproj
@@ -1,6 +1,6 @@
 <Project Sdk="Microsoft.NET.Sdk">
   <PropertyGroup>
-    <TargetFramework>net48</TargetFramework>
+    <TargetFramework>net462</TargetFramework>
     <SpeechMessageStrictCode>true</SpeechMessageStrictCode>
     <!-- 嚴格開關與 XML 文件開關必須同在專案本體，避免 SDK 載入時點造成假綠燈。 -->
     <GenerateDocumentationFile>true</GenerateDocumentationFile>
diff --git a/SpeechMessage.Localization.Tests/SystemTextCatalogTests.cs b/SpeechMessage.Localization.Tests/SystemTextCatalogTests.cs
index 8f04928a3..d3e6679ff 100644
--- a/SpeechMessage.Localization.Tests/SystemTextCatalogTests.cs
+++ b/SpeechMessage.Localization.Tests/SystemTextCatalogTests.cs
@@ -15,16 +15,20 @@ namespace SpeechMessage.Localization.Tests;
 public sealed class SystemTextCatalogTests
 {
     /// <summary>
-    /// 保護 Plugin 取得的 Localization 專案輸出為 net462，而非舊 net48 或僅適用主機的 net10。
-    /// 讀取實際載入組件上的 TargetFrameworkAttribute，避免建置設定與引用資產不一致時誤報通過。
+    /// 保護雙目標參考依測試宿主選出正確產品輸出：net48 宿主必須載入供 Plugin 使用的 net462，
+    /// net10 宿主則載入 net10。讀取實際組件屬性，避免把測試專案 TFM 誤當成產品輸出 TFM。
     /// </summary>
-    [Fact(DisplayName = "Localization 專案參考選取 Framework 4.6.2 輸出")]
-    public void Assembly_TargetFramework_IsNet462()
+    [Fact(DisplayName = "Localization 專案參考選取宿主對應產品輸出")]
+    public void Assembly_TargetFramework_MatchesNearestProductAsset()
     {
         var attribute = typeof(SystemTextTable).Assembly.GetCustomAttributes(typeof(TargetFrameworkAttribute), false)
             .Cast<TargetFrameworkAttribute>().Single();
 
+#if NETFRAMEWORK
         Assert.Equal(".NETFramework,Version=v4.6.2", attribute.FrameworkName);
+#else
+        Assert.Equal(".NETCoreApp,Version=v10.0", attribute.FrameworkName);
+#endif
     }
 
     /// <summary>六個固定資源都須由真實生成表返回原文，包含 P0-10 診斷鍵，不可以替身字典略過產生器。</summary>
diff --git a/SpeechMessage.Localization/ISystemTextCatalog.cs b/SpeechMessage.Localization/ISystemTextCatalog.cs
index 7909f12bf..488006a06 100644
--- a/SpeechMessage.Localization/ISystemTextCatalog.cs
+++ b/SpeechMessage.Localization/ISystemTextCatalog.cs
@@ -1,7 +1,7 @@
 namespace SpeechMessage.Localization;
 
 /// <summary>
-/// 僅解析編譯時系統文字的共用介面，net48 與 net10 使用同一份契約。
+/// 僅解析編譯時系統文字的共用介面，net462 與 net10 使用同一份契約。
 /// 不讀取教會覆寫、成員語言、身分或連線；選擇語系不代表通過任何授權。
 /// </summary>
 public interface ISystemTextCatalog
diff --git a/SpeechMessage.Localization/SpeechMessage.Localization.csproj b/SpeechMessage.Localization/SpeechMessage.Localization.csproj
index d9595e7d5..b97faeb4c 100644
--- a/SpeechMessage.Localization/SpeechMessage.Localization.csproj
+++ b/SpeechMessage.Localization/SpeechMessage.Localization.csproj
@@ -1,11 +1,11 @@
 <Project Sdk="Microsoft.NET.Sdk">
   <PropertyGroup>
-    <TargetFrameworks>net48;net10.0</TargetFrameworks>
+    <TargetFrameworks>net462;net10.0</TargetFrameworks>
     <SpeechMessageStrictCode>true</SpeechMessageStrictCode>
     <!-- SDK 在匯入 targets 前決定文件產出，必須於本體啟用以通過 SMQ0001／CS1591 守門。 -->
     <GenerateDocumentationFile>true</GenerateDocumentationFile>
   </PropertyGroup>
-  <ItemGroup Condition="'$(TargetFramework)' == 'net48'">
+  <ItemGroup Condition="'$(TargetFramework)' == 'net462'">
     <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="all" />
   </ItemGroup>
   <ItemGroup>
diff --git a/SpeechMessage.Localization/SystemTextTable.cs b/SpeechMessage.Localization/SystemTextTable.cs
index 67f29a50b..72f6ab0e8 100644
--- a/SpeechMessage.Localization/SystemTextTable.cs
+++ b/SpeechMessage.Localization/SystemTextTable.cs
@@ -93,7 +93,7 @@ public sealed class SystemTextTable : ISystemTextCatalog
     /// <returns>沿用相同比較器的不可變字典。</returns>
     private static FrozenDictionary<TKey, TValue> Seal<TKey, TValue>(Dictionary<TKey, TValue> candidate) where TKey : notnull => candidate.ToFrozenDictionary(candidate.Comparer);
 #else
-    /// <summary>net48 以 ReadOnlyDictionary 封裝單一擁有者的候選，底層字典不再暴露或修改。</summary>
+    /// <summary>net462 以 ReadOnlyDictionary 封裝單一擁有者的候選，底層字典不再暴露或修改。</summary>
     /// <typeparam name="TKey">不可為 null 的穩定鍵型別。</typeparam>
     /// <typeparam name="TValue">不可變文字或已封裝的內層快照。</typeparam>
     /// <param name="candidate">本建構式獨佔且已完成的候選。</param>
diff --git a/build/PluginMerge.targets b/build/PluginMerge.targets
index 4a7a162bc..f0bdc6471 100644
--- a/build/PluginMerge.targets
+++ b/build/PluginMerge.targets
@@ -21,6 +21,13 @@
       <_SpeechMessageMergeInput Include="$(TargetDir)LineMessagingProcessor.Core.dll" />
       <_SpeechMessageMergeInput Include="$(TargetDir)Newtonsoft.Json.dll" />
     </ItemGroup>
+    <!-- 每個被合併輸入都必須先證明目標不高於伺服器核定的 v4.6.2；
+         檢查器以 PE metadata 工作，不載入或執行組件。使用 dotnet build 的標準 restore/build 流程，
+         並強制還原，避免依賴方案或其他工具先前留下的 assets；此工具沒有方案相依，不用靜態圖還原。 -->
+    <Exec Command="dotnet build &quot;$(MSBuildThisFileDirectory)../tools/PluginPackaging/PluginPackaging.csproj&quot; --configuration Release --framework net10.0 --nologo -p:RestoreUseStaticGraphEvaluation=false -p:RestoreForce=true"
+          WorkingDirectory="$(MSBuildThisFileDirectory)../tools/PluginPackaging" />
+    <Exec Command="dotnet &quot;$(MSBuildThisFileDirectory)../tools/PluginPackaging/bin/Release/net10.0/PluginPackaging.dll&quot; --validate-net462-input &quot;%(_SpeechMessageMergeInput.Identity)&quot;"
+          WorkingDirectory="$(MSBuildThisFileDirectory).." />
     <MakeDir Directories="$(SpeechMessagePluginOutputDirectory)" />
     <ILRepack InputAssemblies="@(_SpeechMessageMergeInput)"
               OutputFile="$(_SpeechMessagePluginOutput)"
diff --git a/src-shared/Polyfills/IsExternalInit.cs b/src-shared/Polyfills/IsExternalInit.cs
index 0fe5fec48..c0d13a6c0 100644
--- a/src-shared/Polyfills/IsExternalInit.cs
+++ b/src-shared/Polyfills/IsExternalInit.cs
@@ -1,8 +1,8 @@
 namespace System.Runtime.CompilerServices
 {
     /// <summary>
-    /// 提供 net48 缺少的唯初始化成員中繼資料標記，讓共用原始碼能使用 init 與 record。
-    /// 此定義僅由嚴格模式的 net48 專案連結編譯，不覆蓋 net10 的框架型別。
+    /// 提供 .NET Framework 目標缺少的唯初始化成員中繼資料標記，讓共用原始碼能使用 init 與 record。
+    /// 此定義僅由嚴格模式的 net462 與保留的 net48 測試目標連結編譯，不覆蓋 net10 的框架型別。
     /// 標記沒有執行期狀態、配置或資源生命週期，不保存任何使用者或教會資料。
     /// </summary>
     internal static class IsExternalInit
diff --git a/src-shared/Polyfills/NotNullWhenAttribute.cs b/src-shared/Polyfills/NotNullWhenAttribute.cs
index 2af0bd117..ea9dd696d 100644
--- a/src-shared/Polyfills/NotNullWhenAttribute.cs
+++ b/src-shared/Polyfills/NotNullWhenAttribute.cs
@@ -1,9 +1,9 @@
 namespace System.Diagnostics.CodeAnalysis
 {
     /// <summary>
-    /// 為 net48 提供條件式非空值契約，讓雙目標 API 可向編譯器描述 Try 模式的輸出狀態。
+    /// 為 .NET Framework 目標提供條件式非空值契約，讓雙目標 API 可向編譯器描述 Try 模式的輸出狀態。
     /// 此屬性只描述中繼資料，不在執行期驗證輸入，也不得用來略過真正的資料檢查。
-    /// 僅 net48 連結編譯，避免與 net10 內建型別重複；每個實例只有一個不可變布林值。
+    /// 只在 net462 與保留的 net48 測試目標連結編譯，避免與 net10 內建型別重複；每個實例只有一個不可變布林值。
     /// </summary>
     [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
     internal sealed class NotNullWhenAttribute : Attribute
diff --git a/tools/PluginPackaging.Tests/AssemblyInspectionTests.cs b/tools/PluginPackaging.Tests/AssemblyInspectionTests.cs
index 24ed77bd5..e60f36b5f 100644
--- a/tools/PluginPackaging.Tests/AssemblyInspectionTests.cs
+++ b/tools/PluginPackaging.Tests/AssemblyInspectionTests.cs
@@ -89,6 +89,15 @@ public sealed class AssemblyInspectionTests
         using var result = JsonDocument.Parse(output.ToString());
         Assert.Equal(expected, result.RootElement.GetProperty("ok").GetBoolean());
         Assert.Equal(expected, result.RootElement.GetProperty("checks").GetProperty("targetFramework").GetBoolean());
+        var expectedFramework = target switch
+        {
+            "net462" => ".NETFramework,Version=v4.6.2",
+            "net45" => ".NETFramework,Version=v4.5",
+            "net48" => ".NETFramework,Version=v4.8",
+            "framework" => ".NETCoreApp,Version=v10.0",
+            _ => null
+        };
+        Assert.Equal(expectedFramework, result.RootElement.GetProperty("frameworkName").GetString());
         Assert.DoesNotContain(fixture.Root, output.ToString(), StringComparison.Ordinal);
     }
 
diff --git a/tools/PluginPackaging/Program.cs b/tools/PluginPackaging/Program.cs
index 3d1afc886..94929bbc0 100644
--- a/tools/PluginPackaging/Program.cs
+++ b/tools/PluginPackaging/Program.cs
@@ -116,7 +116,7 @@ internal static class Program
     /// <returns>只有 PE 可讀且框架版本符合上限時回傳零。</returns>
     private static int ValidateMergeInputFramework(string path, TextWriter output)
     {
-        var valid = false;
+        string? frameworkName;
         try
         {
             using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
@@ -125,7 +125,7 @@ internal static class Program
             if (!pe.HasMetadata) return Reject("InvalidImage", output);
             var metadata = pe.GetMetadataReader();
             if (!metadata.IsAssembly) return Reject("InvalidImage", output);
-            valid = IsFrameworkAtOrBelowLimit(ReadFrameworkName(metadata, metadata.GetAssemblyDefinition()));
+            frameworkName = ReadFrameworkName(metadata, metadata.GetAssemblyDefinition());
         }
         catch (Exception exception) when (exception is BadImageFormatException or IOException or UnauthorizedAccessException or ArgumentException)
         {
@@ -133,10 +133,13 @@ internal static class Program
             return Reject("InvalidImage", output);
         }
 
+        var valid = IsFrameworkAtOrBelowLimit(frameworkName);
         output.WriteLine(JsonSerializer.Serialize(new
         {
             ok = valid,
-            checks = new { targetFramework = valid }
+            checks = new { targetFramework = valid },
+            frameworkName,
+            maximumTargetFramework = ".NETFramework,Version=v4.6.2"
         }));
         return valid ? 0 : 1;
     }
diff --git a/tools/PluginPackaging/Tests/Test-PluginBuild.Tests.ps1 b/tools/PluginPackaging/Tests/Test-PluginBuild.Tests.ps1
index 941856934..8fed9fdbf 100644
--- a/tools/PluginPackaging/Tests/Test-PluginBuild.Tests.ps1
+++ b/tools/PluginPackaging/Tests/Test-PluginBuild.Tests.ps1
@@ -14,10 +14,19 @@ if (-not $UseEphemeralTestKey -and [string]::IsNullOrWhiteSpace($originalKey)) {
 $repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
 $projectPath = Join-Path $repoRoot 'SpeechMessage.Dynamics.Plugins/SpeechMessage.Dynamics.Plugins.csproj'
 $checker = Join-Path $PSScriptRoot '../Test-PluginAssembly.ps1'
+$mergeTargetsPath = Join-Path $repoRoot 'build/PluginMerge.targets'
+$toolProjectRoot = Join-Path $repoRoot 'tools/PluginPackaging'
+$toolAssets = Join-Path $toolProjectRoot 'obj/project.assets.json'
+[xml]$mergeTargets = [IO.File]::ReadAllText($mergeTargetsPath)
+$toolBuildTarget = $mergeTargets.SelectSingleNode("/Project/Target[@Name='PackageSpeechMessagePlugin']/Exec[contains(@Command, 'dotnet build') and contains(@Command, 'RestoreUseStaticGraphEvaluation=false') and contains(@Command, 'RestoreForce=true')]")
+if ($null -eq $toolBuildTarget) {
+    throw 'Plugin packaging must force a standard validator restore during build with static-graph restore disabled.'
+}
 $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
 $fixtureRoot = [IO.Path]::GetFullPath((Join-Path $tempRoot ('speechmessage-build-' + [Guid]::NewGuid().ToString('N'))))
 $null = New-Item -ItemType Directory -Path $fixtureRoot
 $failures = 0
+Write-Output 'PASS validator-standard-build-forces-restore'
 try {
     $signingKey = $originalKey
     if ($UseEphemeralTestKey) {
@@ -36,39 +45,72 @@ try {
         # 未要求打包的案例不得進入合併目標；即使沒有金鑰，也不能建立 artifacts 目錄。
         $packageArguments = @()
         if (-not $sample.EndsWith('-no-package', [StringComparison]::Ordinal)) { $packageArguments = @('-p:SpeechMessagePackagePlugin=true') }
-        $output = @(& dotnet build $projectPath -c $configuration @packageArguments --no-restore "-p:SpeechMessagePluginOutputDirectory=$outputDirectory" -p:SpeechMessageBuildNumber=37 --nologo -v minimal 2>&1)
+        $output = @(& dotnet build $projectPath -c $configuration @packageArguments --no-restore `
+            "-p:SpeechMessagePluginOutputDirectory=$outputDirectory" `
+            -p:SpeechMessageBuildNumber=37 --nologo -v minimal 2>&1)
         $code = $LASTEXITCODE
         $assemblyPath = Join-Path $outputDirectory 'SpeechMessage.Dynamics.Plugins.dll'
         $text = $output -join "`n"
+        $assemblyExists = Test-Path -LiteralPath $assemblyPath
+        $toolAssetsPresent = $null
+        $failureDetail = $null
         if ($sample.EndsWith('-no-package', [StringComparison]::Ordinal)) {
             $passed = $code -eq 0 -and -not (Test-Path -LiteralPath $outputDirectory) -and
                 -not $text.Contains('SMP0001') -and -not $text.Contains('SMP0002') -and -not $text.Contains('SMP0003')
+            if (-not $passed) { $failureDetail = 'ordinary-build-packaging-contract-failed' }
         } elseif ($sample -eq 'release-invalid') {
             $passed = $code -ne 0 -and $text.Contains('SMP0003') -and -not (Test-Path -LiteralPath $outputDirectory)
+            if (-not $passed) { $failureDetail = 'invalid-key-rejection-contract-failed' }
         } elseif ($sample -eq 'release-unset') {
             $passed = $code -ne 0 -and $text.Contains('SMP0001') -and -not (Test-Path -LiteralPath $assemblyPath)
+            if (-not $passed) { $failureDetail = 'missing-key-rejection-contract-failed' }
         } elseif ($sample -eq 'debug-unset') {
             $passed = $code -eq 0 -and $text.Contains('SMP0002') -and (Test-Path -LiteralPath $assemblyPath)
+            $toolAssetsPresent = Test-Path -LiteralPath $toolAssets
+            if (-not $passed) { $failureDetail = 'debug-build-or-warning-contract-failed' }
+            if ($passed -and -not $toolAssetsPresent) {
+                $passed = $false
+                $failureDetail = 'tool-restore-assets-missing'
+            }
             if ($passed) {
                 $inspection = @(& powershell -NoProfile -File $checker -AssemblyPath $assemblyPath 2>&1)
                 $inspectCode = $LASTEXITCODE
                 $jsonLine = $inspection | Where-Object { $_.ToString().StartsWith('{', [StringComparison]::Ordinal) } | Select-Object -Last 1
                 $result = $jsonLine.ToString() | ConvertFrom-Json
                 $passed = $inspectCode -eq 1 -and $result.checks.strongName -eq $false -and $result.checks.targetFramework -eq $true
+                if (-not $passed) { $failureDetail = 'unsigned-debug-assembly-contract-failed' }
             }
         } else {
             $passed = $code -eq 0 -and (Test-Path -LiteralPath $assemblyPath)
+            if (-not $passed) { $failureDetail = 'release-build-or-output-missing' }
             if ($passed) {
-                $null = & powershell -NoProfile -File $checker -AssemblyPath $assemblyPath
-                $passed = $LASTEXITCODE -eq 0
+                $inspection = @(& powershell -NoProfile -File $checker -AssemblyPath $assemblyPath 2>&1)
+                $inspectCode = $LASTEXITCODE
+                $jsonLine = $inspection | Where-Object { $_.ToString().StartsWith('{', [StringComparison]::Ordinal) } | Select-Object -Last 1
+                $result = $jsonLine.ToString() | ConvertFrom-Json
+                $references = @($result.references | ForEach-Object { $_.ToString() })
+                $passed = $inspectCode -eq 0 -and $result.checks.targetFramework -eq $true -and
+                    $result.assemblyVersion -eq '1.0.0.0' -and
+                    $references -notcontains 'System.Runtime' -and $references -notcontains 'netstandard'
+                if (-not $passed) { $failureDetail = 'release-assembly-check-failed' }
                 # 版本資源的唯讀查詢不載入執行 DLL；建置序號不改 AssemblyVersion 的穩定身分。
                 $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($assemblyPath)
                 $passed = $passed -and $version.FileVersion -eq '1.0.0.37' -and
                     $version.ProductVersion.StartsWith('1.0.0-p0.37', [StringComparison]::Ordinal)
+                if (-not $passed -and $null -eq $failureDetail) { $failureDetail = 'release-version-resource-mismatch' }
             }
         }
         if ($passed) { Write-Output ('PASS ' + $sample) }
-        else { $failures++; Write-Output ('FAIL ' + $sample + ': packaging contract not satisfied') }
+        else {
+            $failures++
+            $errorCodes = [Regex]::Matches($text, '\b[A-Z]{2,}[0-9]{4}\b') |
+                ForEach-Object { $_.Value } | Sort-Object -Unique
+            $codeSummary = if ($errorCodes.Count -gt 0) { ' codes=' + ($errorCodes -join ',') } else { '' }
+            $detailSummary = if ($null -ne $failureDetail) { ' reason=' + $failureDetail } else { '' }
+            $stateSummary = ' exit=' + $code + ' assembly=' + $assemblyExists.ToString().ToLowerInvariant()
+            if ($null -ne $toolAssetsPresent) { $stateSummary += ' restoreAssets=' + $toolAssetsPresent.ToString().ToLowerInvariant() }
+            Write-Output ('FAIL ' + $sample + ': packaging contract not satisfied' + $codeSummary + $detailSummary + $stateSummary)
+        }
     }
 } finally {
     [Environment]::SetEnvironmentVariable('SPEECHMESSAGE_PLUGIN_KEYFILE', $originalKey)
diff --git a/文件/規劃設計書/實作計畫-3.3/00_總覽與執行規則.md b/文件/規劃設計書/實作計畫-3.3/00_總覽與執行規則.md
index daff2d231..86e31554a 100644
--- a/文件/規劃設計書/實作計畫-3.3/00_總覽與執行規則.md
+++ b/文件/規劃設計書/實作計畫-3.3/00_總覽與執行規則.md
@@ -25,7 +25,7 @@
 |---|---|---|
 | A1 | 保留 Plugin／Workflow | D365 端用 `IPlugin`、`CodeActivity`；不改用 D365 Webhook |
 | A2 | 36 個 DLL → 2 個 | 只有 `SpeechMessage.Dynamics.Plugins`、`SpeechMessage.Dynamics.Workflows` 兩個 D365 組件 |
-| A3 | 規則集中在 GoodShepherd.Core（雙目標） | 業務規則寫一次，`net48;net10.0` 兩個目標都要編得過、測得過 |
+| A3 | 規則集中在 GoodShepherd.Core（雙目標） | 業務規則寫一次，`net462;net10.0` 兩個目標都要編得過、測得過 |
 | A4 | Plugin 直接推播 | 非同步步驟、每次 1 人、Retry Key＝記錄或活動 ID、同教會檢查、Token 加密；只有宮格圖、QR 圖片、永豐撥款查詢走 `new_outboundtask` |
 | D15 | D365 9.1 | — |
 | D16 | LINE 去重放平台 SQL；對外待辦放 D365 | 見階段 3、附錄 A |
@@ -33,7 +33,7 @@
 | D18 | LineWebhook 放 DMZ | 需能連內網 D365 |
 | D19 | 入站 LINE 訊息仍寫 letter 當紀錄 | 不再當觸發 |
 | D20 | 生日、年資維持 D365 遞迴流程 | 活動改呼叫共用規則 |
-| D21 | Plugin 目標 .NET Framework 4.8 | `TargetFramework=net48` |
+| D21 | D365 沙箱組件目標 .NET Framework 4.6.2（使用者 2026-11-04 核定，取代先前 net48 決定） | `SpeechMessage.Dynamics.Plugins` 使用 `net462`；會進沙箱的 Plugin／Workflow 與其 Framework 相依組件均須提供不高於 v4.6.2 的輸入。現場 goodshepherdback 的 ObjectModel 9.1.49.21825 在 `TargetFrameworkVersionValidator` 驗證 net48 組件時發生 `FormatException`；結合 Microsoft KB 4345239，net48 目標不相容是高度可信推論，但格式化例外的具體成因及 net462 是否可註冊仍待使用者重驗。4.8 沙箱支援未公布適用於現場元件版本的最低更新；取得相容性更新前不評估升回 net48 |
 | D22 | D365 佈署改用佈署專案 | `SpeechMessage.Dynamics.Deployment` 依 repo 清單執行；Plugin Registration Tool 只留作查看與除錯；原 `tools\D365Provisioning`、`tools\ShadowCompare`、`tools\D365StepSwitch` 併入它 |
 | D23 | 開發測試組織 goodshepherdback | 唯一的開發測試組織；正式組織的複本，比照正式個資保護；原則上使用者回報清理完成前不連線。使用者 2026-11-04 追加核定：P0-13 trace 由使用者執行時，免除清空舊 LINE token 與會友 `new_lineid` 的前置，舊步驟／流程仍停用、待處理舊作業仍取消；Codex 仍不連線 D365／LINE |
 | D24 | 對測試組織的權限分工 | Codex 只執行不寫入的指令（`export-metadata`、`plan`、`verify`、`export`、`shadow`）；`apply`、`smoke`、`switch`、`rollback`、`ui-import` 由使用者執行 |
@@ -55,12 +55,12 @@
 
 ### 與 v3.2 Word 的一處技術修正（v3.3 Word 已更正）
 
-v3.2 Word 10.9 節寫「Token 以 AES-GCM 加密」。但 .NET Framework 4.8 **沒有** `AesGcm` 類別，Plugin 端無法解密。v3.3 Word 已改為下列做法，也已改成「一個 Channel 可由多間教會共用、由教會指向 Channel」（附錄 A.3）。
+v3.2 Word 10.9 節寫「Token 以 AES-GCM 加密」。但 .NET Framework 4.6.2 **沒有** `AesGcm` 類別，Plugin 端無法解密。v3.3 Word 已改為下列做法，也已改成「一個 Channel 可由多間教會共用、由教會指向 Channel」（附錄 A.3）。
 
 本計畫改用以下做法：
 - **演算法：** AES-256-CBC＋HMAC-SHA256，先加密再驗證（Encrypt-then-MAC）。
 - **封包格式：** 版本化，例如 `v1:`（規格見附錄 A.5）。
-- **跨框架：** 4.8 與 .NET 10 都能用同一份程式。
+- **跨框架：** 4.6.2 與 .NET 10 都能用同一份程式。
 - **安全性：** 同等。
 
 實作時以本計畫為準。
@@ -75,12 +75,12 @@ v3.2 Word 10.9 節寫「Token 以 AES-GCM 加密」。但 .NET Framework 4.8 **
 |---|---|---|---|
 | 執行位置 | `SpeechMessageProducts.ChurchReport` | 擴充 | net10.0 |
 | 執行位置 | `SpeechMessageProducts.LineWebhook` | 新增（階段 3） | net10.0 |
-| 執行位置 | `SpeechMessage.Dynamics.Plugins` | 新增（階段 0 骨架） | net48 |
-| 執行位置 | `SpeechMessage.Dynamics.Workflows` | 新增（階段 5） | net48 |
-| 產品領域 | `SpeechMessageProducts.GoodShepherd.Core` | 新增（階段 2） | net48;net10.0 |
-| 共用核心 | `ToolUtility.Core` | 新增（階段 1） | net48;net10.0 |
-| 共用核心 | `SpeechMessage.Localization` | 新增（階段 0 骨架） | net48;net10.0 |
-| 共用核心 | `LineMessagingProcessor.Core` | 新增（階段 0 骨架） | net48;net10.0 |
+| 執行位置 | `SpeechMessage.Dynamics.Plugins` | 新增（階段 0 骨架） | net462 |
+| 執行位置 | `SpeechMessage.Dynamics.Workflows` | 新增（階段 5） | net462 |
+| 產品領域 | `SpeechMessageProducts.GoodShepherd.Core` | 新增（階段 2） | net462;net10.0 |
+| 共用核心 | `ToolUtility.Core` | 新增（階段 1） | net462;net10.0 |
+| 共用核心 | `SpeechMessage.Localization` | 新增（階段 0 骨架） | net462;net10.0 |
+| 共用核心 | `LineMessagingProcessor.Core` | 新增（階段 0 骨架） | net462;net10.0 |
 | 共用層 | `LineMessagingProcessor.AspNetCore` | 擴充 | net10.0 |
 | 共用層 | `SpeechMessage.Hosting` | 新增（階段 2） | net10.0 |
 | 共用層 | `SpeechMessage.Payments.AspNetCore` | 擴充 | net10.0 |
@@ -91,7 +91,7 @@ v3.2 Word 10.9 節寫「Token 以 AES-GCM 加密」。但 .NET Framework 4.8 **
 | 共用層 | `SpeechMessage.Payments` | 擴充 | net10.0 |
 | 共用層 | `ToolUtility` | 擴充 | net10.0 |
 | 共用層 | `SpeechMessage.Tenancy` | 新增（階段 1） | net10.0 |
-| 基礎 | `Line.Messaging` | 擴充為雙目標（階段 1） | net48;net10.0 |
+| 基礎 | `Line.Messaging` | 擴充為雙目標（階段 1） | net462;net10.0 |
 | 基礎 | `PowerPlatform.Dataverse.Client` | 沿用 | net10.0 |
 | 工具 | `SpeechMessage.Dynamics.Deployment` | 新增（階段 0 最小版，階段 1、5、7 擴充） | net10.0（主控台，不被任何專案參考） |
 
@@ -119,6 +119,8 @@ v3.2 Word 10.9 節寫「Token 以 AES-GCM 加密」。但 .NET Framework 4.8 **
 
 **例外：** `LineMessagingProcessor.Core` 的測試放在 `LineMessagingProcessor.Tests`，該專案改為雙目標。
 
+測試執行器可維持 `net48`，但任何指向產品專案的測試都必須確認實際引用／載入的是 `net462` 輸出；不可因測試目標為 net48，就把產品組件重新編譯或誤選成 net48。產品組件的目標框架與測試執行器框架是不同契約。
+
 ### 3.3 不進 .sln 的工具
 
 工具放在 `tools\` 底下，各自有 csproj 或 ps1，由 CI 呼叫：
@@ -133,7 +135,7 @@ v3.2 的 `tools\D365Provisioning`、`tools\ShadowCompare`、`tools\D365StepSwitc
 ### 3.4 相依規則（架構測試要自動檢查）
 
 ```
-執行位置   ChurchReport  LineWebhook  │  Plugins  Workflows（net48）
+執行位置   ChurchReport  LineWebhook  │  Plugins  Workflows（net462）
              └────────────┬───────────┴─────┘
 產品領域        GoodShepherd.Core（雙目標）
 共用核心        ToolUtility.Core　Localization　LineMessagingProcessor.Core（雙目標）
@@ -141,11 +143,11 @@ v3.2 的 `tools\D365Provisioning`、`tools\ShadowCompare`、`tools\D365StepSwitc
 基礎            Line.Messaging（雙目標）　PowerPlatform.Dataverse.Client（net10.0）
 
 工具（net10.0）  SpeechMessage.Dynamics.Deployment：可參考 ToolUtility 的連線與 PowerPlatform.Dataverse.Client；
-                 只讀取建置產出的 Plugins.dll／Workflows.dll 檔案，不以 ProjectReference 參考 net48 專案；不被任何專案參考
+                 只讀取建置產出的 Plugins.dll／Workflows.dll 檔案，不以 ProjectReference 參考沙箱目標專案；不被任何專案參考
 ```
 
 - **只能往下參考。** 主機之間、Plugin 專案之間不互相參考。
-- **Plugin 專案只能參考雙目標專案。** 架構測試要擋下參考 net10.0 專案的情況。
+- **Plugin／Workflow 專案只能參考含 net462 目標的雙目標專案。** 架構測試要擋下參考 net10.0 專案的情況；所有進入 D365 沙箱的合併輸入都必須明確宣告不高於 v4.6.2 的目標。
 - **雙目標專案不參考任何只有 net10.0 的專案**，也不參考 `Microsoft.Extensions.*`、ASP.NET Core、`ServiceClient`。
 - **`HttpClient` 只能出現在** `LineMessagingProcessor.Core`、`Line.Messaging`，以及 net10.0 的主機與共用層。
 
@@ -197,7 +199,7 @@ v3.2 的 `tools\D365Provisioning`、`tools\ShadowCompare`、`tools\D365StepSwitc
   1. 保護的契約；
   2. 注入的失敗或錯誤情境；
   3. 決定性的斷言。
-- **雙目標專案的測試在 `net48` 與 `net10.0` 各跑一次**，結果必須相同。
+- **雙目標產品專案的測試須覆蓋 `net462` 與 `net10.0` 兩個產品輸出**，結果必須符合相同契約；測試執行器可留在 `net48`，但必須由測試確認引用的 Framework 產品組件確為 net462。
 - **時間：** 測試不得依賴系統時間、時區、文化設定或網路。時間一律用假的時鐘（`FakeChurchClock`／`TimeProvider` 的假實作）。
 - **跨教會隔離：** 每個有快取、佇列、靜態資料或背景工作的元件，都要有 A／B 教會並行測試，並斷言資源計數回到基線（AGENTS.md「跨使用者隔離」）。
 - **黃金檔：**
@@ -302,7 +304,7 @@ dotnet run --project .\tools\I18nCheck -- verify
 
 - 後三個工具在階段 0 建立；建立前的 WP 仍須跑 Release 建置、Release 測試與 Debug tracer 測試這三行。
 - P0-05 提交後，所有後續 WP 都必須執行 `Test-TextEncoding.ps1 -Scope Changed -Base 73b6e6cf`；P0-06 起再加 `Find-DuplicateCode.ps1`，P0-07 起再加 `dotnet run --project tools/I18nCheck -- verify`。
-- 階段 1 以後，另加架構測試與 net48 Plugin 打包檢查（各階段檔案會說明）。
+- 階段 1 以後，另加架構測試與 net462 Plugin 打包檢查（各階段檔案會說明）。
 - P0-12 起，佈署專案的清單與 DLL 一致性測試包含在 `dotnet test` 之內（不連 D365）；任何 WP 都不得為了驗證而連線 D365，連線只依 D24 的權限分工進行。
 
 ---
diff --git a/文件/規劃設計書/實作計畫-3.3/01_階段0_準備守門與可行性驗證.md b/文件/規劃設計書/實作計畫-3.3/01_階段0_準備守門與可行性驗證.md
index 620866f14..55654d19d 100644
--- a/文件/規劃設計書/實作計畫-3.3/01_階段0_準備守門與可行性驗證.md
+++ b/文件/規劃設計書/實作計畫-3.3/01_階段0_準備守門與可行性驗證.md
@@ -5,7 +5,7 @@
 - 目標有三個：
   1. 之後每一步都能被自動驗證（套件集中管理、CI、編碼、重複檔、中文字串棘輪）；
   2. **用一個「走得通的骨架」證明整個 Plugin 版做法在 D365 9.1 上可行。**
-     證明的範圍：雙目標程式庫 → net48 Plugin → ILRepack 合併 → 新金鑰簽章 → **由佈署專案**以 Sandbox 註冊（不用 Plugin Registration Tool，D22）→ 沙箱內取字典、推播 LINE；
+     證明的範圍：雙目標程式庫 → net462 Plugin → ILRepack 合併 → 新金鑰簽章 → **由佈署專案**以 Sandbox 註冊（不用 Plugin Registration Tool，D22）→ 沙箱內取字典、推播 LINE；
   3. 開發環境安全：Development 不可能用到正式 LINE token、只能連開發測試組織 goodshepherdback（P0-SEC、P0-ENV，D23、D27）。
 
 > 3.3 版的變更：新增 P0-SEC、P0-ENV；原 P0-12「使用者用 Plugin Registration Tool 註冊」改為 P0-12「佈署專案最小版」（Codex 寫程式）與 P0-13「使用者以佈署專案在 goodshepherdback 驗收」。階段審查改在 P0-12 完成後、P0-13 之前。
@@ -55,7 +55,7 @@
 | P0-07 | I18nCheck 第一版：中文字串棘輪＋resx 產生器 | P0-03 |
 | P0-08 | 骨架：`SpeechMessage.Localization`（雙目標） | P0-04、P0-07 |
 | P0-09 | 骨架：`LineMessagingProcessor.Core`（雙目標，單人推播） | P0-04 |
-| P0-10 | 骨架：`SpeechMessage.Dynamics.Plugins`（net48）＋ILRepack＋簽章＋打包檢查 | P0-08、P0-09 |
+| P0-10 | 骨架：`SpeechMessage.Dynamics.Plugins`（net462）＋ILRepack＋簽章＋打包檢查 | P0-08、P0-09 |
 | P0-11 | 方案層級 CI | P0-03～P0-10 |
 | P0-12 | 佈署專案最小版 `SpeechMessage.Dynamics.Deployment`（L1／L2 測試，不連 D365） | P0-10、P0-11 |
 | P0-12A | ImageSharp 3.2.0 與上傳圖片格式／尺寸防護 | P0-12 |
@@ -121,7 +121,7 @@
    - **統一到 13.0.4：** `Newtonsoft.Json`。
    - **其他有效套件維持現有版本。** ChurchReport 的 `Microsoft.AspNetCore.* 2.2.x` 都在 XML 註解內，維持停用，不新增、不啟用、不加入中央版本表。有效的 `Microsoft.AspNetCore.Mvc.NewtonsoftJson 10.0.0` 維持原版本。
    - **為後續階段預先加入：**
-     - `Microsoft.NETFramework.ReferenceAssemblies`：讓任何機器都能建置 net48；
+     - `Microsoft.NETFramework.ReferenceAssemblies`：讓任何機器都能以指定版本的 .NET Framework 參考組件建置，沙箱產品採 v4.6.2；
      - `Microsoft.CrmSdk.CoreAssemblies 9.0.2.56`；
      - `Microsoft.CrmSdk.Workflow`：取與 CoreAssemblies 相同的 9.0.2.x，若不存在取 9.0.2 系列最新版；
      - `Microsoft.PowerPlatform.Dataverse.Client 1.1.32`；
@@ -169,20 +169,20 @@
    - 加上 `BannedApiAnalyzers` 的參考，以及 `AdditionalFiles Include="$(MSBuildThisFileDirectory)BannedSymbols.txt"`。
    - `CA1307`、`CA1309`、`CA1310` 設為錯誤：字串比較必須明確指定比較方式。
    - 嚴格測試專案的 CA1707 例外由 Directory.Build.targets 統一處理（使用者 2026-11-03 核定），其他規則不變。
-   - 嚴格模式明確設定 AnalysisLevel，net48 與 net10 規則一致，以 net48 探測守門（使用者 2026-11-03 核定）：層級空白時補 latest，並檢查兩目標 EffectiveAnalysisLevel 非空；僅 Recommended 字串不足以證明規則載入。
+   - 嚴格模式明確設定 AnalysisLevel，net462 與 net10 規則一致，以 net462 探測守門（使用者 2026-11-03 核定後依 2026-11-04 D21 調整目標）：層級空白時補 latest，並檢查兩目標 EffectiveAnalysisLevel 非空；僅 Recommended 字串不足以證明規則載入。仍以 net48 執行的測試專案另確認其分析器設定。
    - 加上繁體中文註解說明為何只對新專案啟用：舊專案一啟用會產生大量既有警告。
 2. 新增 `BannedSymbols.txt`，內容依附錄 E.1，例如：
    - `P:System.DateTime.Now`、`P:System.DateTime.Today`、`M:System.DateTime.ToLocalTime`、`P:System.TimeZoneInfo.Local`；
    - `P:System.Globalization.CultureInfo.DefaultThreadCurrentCulture`（屬性識別碼，原因見附錄 E.1）；
    - Plugin 專案另外禁用 `System.Drawing` 命名空間。
-3. 雙目標專案需要的語言補丁，放在 `src-shared\Polyfills\`，用 `<Compile Include>` 連結到需要的專案，只在 `net48` 編譯：
-   - `IsExternalInit`（讓 `init`／`record` 在 net48 可用）；
+3. 雙目標專案需要的語言補丁，放在 `src-shared\Polyfills\`，用 `<Compile Include>` 連結到需要的專案，只在 .NET Framework 目標（產品 net462，以及仍需驗證的 net48 測試目標）編譯：
+   - `IsExternalInit`（讓 `init`／`record` 在 .NET Framework 目標可用）；
    - `NotNullWhenAttribute` 等 nullable 標註屬性；
    - 每個檔案都要有繁體中文 XML 註解。
 
 **先寫的測試**
 
-新增 `tools\RepoQuality\AnalyzerProbe\`：一個 `SpeechMessageStrictCode=true` 的雙目標探測專案（net48;net10.0，用來同時編譯驗證語言補丁），**不加入 .sln**，故意使用 `DateTime.Now`。CI 建置它時「必須失敗」，用來證明禁用 API 有作用。腳本 `Test-AnalyzerProbe.ps1` 斷言建置失敗、且錯誤代碼是 `RS0030`。
+新增 `tools\RepoQuality\AnalyzerProbe\`：一個 `SpeechMessageStrictCode=true` 的雙目標探測專案（net462;net10.0，用來驗證目前沙箱產品目標與主機目標的分析器及語言補丁），**不加入 .sln**，故意使用 `DateTime.Now`。CI 建置它時「必須失敗」，用來證明禁用 API 有作用。腳本 `Test-AnalyzerProbe.ps1` 斷言兩個目標建置失敗、且錯誤代碼是 `RS0030`。仍有明確 net48 測試目標的守門維持獨立，不得以該測試目標代替 net462 產品驗證。
 
 保留原有 13 項檢查，新增缺 XML 註解必須有 CS1591、只設 StrictCode 未設文件開關必須被 SMQ0001 擋下；設定回歸要檢查實際 DocumentationFile 非空，並以正常有文件的分支建置、斷言 bin 輸出資料夾存在本次產生的 XML，不能只檢查 GenerateDocumentationFile=true。
 
@@ -349,9 +349,9 @@
 
 **新增**
 - `SpeechMessage.Localization\SpeechMessage.Localization.csproj`：
-  - `TargetFrameworks=net48;net10.0`，`SpeechMessageStrictCode=true`；
+  - `TargetFrameworks=net462;net10.0`，`SpeechMessageStrictCode=true`；
   - 不參考任何其他專案；
-  - net48 參考 `Microsoft.NETFramework.ReferenceAssemblies`。
+  - net462 參考 `Microsoft.NETFramework.ReferenceAssemblies`。
 - `SpeechMessage.Localization.Tests`：雙目標，加入 .sln。
 
 **本階段範圍**（完整 API 在階段 1；介面見附錄 C.1）
@@ -359,7 +359,7 @@
 | 型別 | 本階段的行為 |
 |---|---|
 | `TextKey`（readonly struct） | key 格式驗證：`^[A-Z][A-Za-z0-9]*(\.[A-Z][A-Za-z0-9]*)+$` |
-| `SystemTextTable` | 以產生的資料建立「每個語系一份」的唯讀表。net10 用 `FrozenDictionary`，net48 用 `ReadOnlyDictionary`，兩邊共用同一個介面。建立時預先展開後備：每個語系表都包含 zh-TW 的全部 key |
+| `SystemTextTable` | 以產生的資料建立「每個語系一份」的唯讀表。net10 用 `FrozenDictionary`，net462 用 `ReadOnlyDictionary`，兩邊共用同一個介面。建立時預先展開後備：每個語系表都包含 zh-TW 的全部 key |
 | `ISystemTextCatalog.GetText(string culture, TextKey key)`；依使用者 2026-11-03 核定（CA1716） | 找不到語系就用 zh-TW；找不到 key 就丟 `KeyNotFoundException`（系統字典缺 key 是程式錯誤，CI 會先擋） |
 | `Resources\SharedText.resx` | 先放 3 個 key：`Common.Diagnostics.Ping`＝「連線測試成功」、`Common.Diagnostics.Culture`＝「目前語系：{culture}」、`Line.Sender.Template`＝「{churchName}小幫手」 |
 | `Resources\SharedText.g.cs` | 由 `I18nCheck generate` 產生並提交，內容含 zh-TW 與 qps-ploc |
@@ -382,7 +382,7 @@
 ## P0-09　骨架：`LineMessagingProcessor.Core`（雙目標，單人推播）
 
 **新增** `LineMessagingProcessor.Core\LineMessagingProcessor.Core.csproj`
-- `TargetFrameworks=net48;net10.0`，`SpeechMessageStrictCode=true`；
+- `TargetFrameworks=net462;net10.0`，`SpeechMessageStrictCode=true`；
 - 參考 `Newtonsoft.Json`；
 - 不參考 `Line.Messaging`：該專案在階段 1 才改成雙目標，本階段先用最小的請求模型。
 
@@ -394,8 +394,8 @@
 | `ILinePushClient.PushAsync(LineChannelCredential credential, LinePushRequest request, CancellationToken ct)` | POST `https://api.line.me/v2/bot/message/push`；標頭 `Authorization: Bearer {token}`、`X-Line-Retry-Key: {RetryKey:D}`；逾時 10 秒；回傳 `LinePushResult`（成功、狀態碼、LINE 的 `x-line-request-id`、錯誤分類） |
 | 錯誤分類 | 2xx＝成功；409（同 Retry Key 已接受）＝視為成功並標記 `AlreadyAccepted`；429、5xx、逾時＝`Retryable`；400、401、403、404＝`Permanent` |
 | `LineChannelCredential` | 只包 Token 字串。`ToString()` 回傳 `"LineChannelCredential(***)"`，絕不輸出 Token |
-| `HttpClient` 生命週期 | net10：由呼叫端傳入 `HttpClient`（主機用 `IHttpClientFactory`）。net48：`LinePushClient` 內部持有一個 `static readonly HttpClient`，原因是 Plugin 沙箱會重用程序，不能每次 new，也沒有 DI。它不含任何使用者或教會狀態，Token 只放在每個請求的標頭，不放 `DefaultRequestHeaders`。逾時與取消一律用每個請求自己的 `CancellationTokenSource`，用完 Dispose。這些都要寫進繁中註解 |
-| TLS | net48 依 OS 預設（目標 4.8 已是 `SystemDefault`）；不得寫死 `ServicePointManager.SecurityProtocol` |
+| `HttpClient` 生命週期 | net10：由呼叫端傳入 `HttpClient`（主機用 `IHttpClientFactory`）。.NET Framework：`LinePushClient` 內部持有一個 `static readonly HttpClient`，原因是 Plugin 沙箱會重用程序，不能每次 new，也沒有 DI。它不含任何使用者或教會狀態，Token 只放在每個請求的標頭，不放 `DefaultRequestHeaders`。逾時與取消一律用每個請求自己的 `CancellationTokenSource`，用完 Dispose。這些都要寫進繁中註解 |
+| TLS | net462 依 .NET Framework／作業系統相容設定；不得寫死 `ServicePointManager.SecurityProtocol`。LINE 實際推播的 TLS 行為移至 P2-04 前置驗收，不以 trace 驗收代替 |
 
 **例外回報與安全契約（依使用者 2026-11-03 核定，出處設計 10.5／10.9、計畫 04 §5.4）：**
 - Core 是雙目標程式庫，不是最終失敗邊界；不參考 ToolUtility、不呼叫 notifier，不新增 callback 或 no-op 預設。
@@ -432,10 +432,10 @@
 
 ---
 
-## P0-10　骨架：`SpeechMessage.Dynamics.Plugins`（net48）＋ILRepack＋簽章＋打包檢查
+## P0-10　骨架：`SpeechMessage.Dynamics.Plugins`（net462）＋ILRepack＋簽章＋打包檢查
 
 **新增** `SpeechMessage.Dynamics.Plugins\SpeechMessage.Dynamics.Plugins.csproj`
-- `TargetFramework=net48`，csproj 本體同時設定 `SpeechMessageStrictCode=true` 與 `GenerateDocumentationFile=true`（文件載入時點契約見 P0-04）；
+- `TargetFramework=net462`，csproj 本體同時設定 `SpeechMessageStrictCode=true` 與 `GenerateDocumentationFile=true`（文件載入時點契約見 P0-04）；使用 `Microsoft.NETFramework.ReferenceAssemblies` 以 4.6.2 參考組件編譯；
 - 參考 `Microsoft.CrmSdk.CoreAssemblies`，設為 `PrivateAssets=all`，**不併入**；
 - 專案參考 `SpeechMessage.Localization`、`LineMessagingProcessor.Core`。
 
@@ -449,7 +449,7 @@
   2. 追蹤記錄寫入：
      - `P0-PING OK`；
      - 組件完整名稱與版本；
-     - `typeof(object).Assembly` 的檔案版本，用來判斷 .NET Framework 是 4.8；
+     - `typeof(object).Assembly` 的檔案版本，用來觀察伺服器 mscorlib 執行環境；追蹤顯示 `FrameworkFileVersion=4.8.x` 代表伺服器 runtime 檔案版本，**不代表 Plugin 的 TargetFramework**（本次 Plugin 目標為 net462）；
      - `Environment.Version`；
      - `Common.Diagnostics.Ping` 在 zh-TW 與 qps-ploc 的取字結果；
      - 觸發的實體名稱與訊息名稱。
@@ -485,7 +485,7 @@
 
 檢查 `artifacts\plugins\*.dll`：
 1. 有強式名稱（PublicKeyToken 不為空）；
-2. `TargetFrameworkAttribute` 是 `.NETFramework,Version=v4.8`；
+2. `TargetFrameworkAttribute` 是 `.NETFramework,Version=v4.6.2`；並以反向測試證明 `.NETFramework,Version=v4.8` 樣本會被拒絕；
 3. 參考的組件只允許 `mscorlib`、`System`、`System.*`、`Microsoft.Xrm.Sdk`、`Microsoft.Crm.Sdk.Proxy`、`System.Runtime.Serialization`；
 4. 不含 `System.Drawing` 參考；
 5. 公開型別中至少有 `DiagnosticsPingPlugin`；
@@ -516,7 +516,7 @@
 
 1. `actions/checkout@v4`，`fetch-depth: 0`：編碼檢查要和基底比對。
 2. `actions/setup-dotnet@v4`，讀取 `global.json`。
-3. `dotnet restore` → `dotnet build -c Release`：含 net48，靠 ReferenceAssemblies 套件。
+3. `dotnet restore` → `dotnet build -c Release`：含 net462，靠 ReferenceAssemblies 套件；測試執行器仍可為 net48，但必須確認引用的是 net462 產品輸出。
 4. `dotnet test -c Release --no-build --collect:"XPlat Code Coverage"`。
    - 同時執行 `dotnet test ToolUtility.Dataverse.Tests -c Debug --filter "FullyQualifiedName~FileToolUtilityTracerTests"`，守住 Big5 時間格式與檔案 handle 確定釋放契約。
 5. 對新專案檢查覆蓋率 ≥ 80%；用 `coverlet` 結果加 `tools/RepoQuality/Test-Coverage.ps1` 判斷，不依賴外部服務。各模組、各目標框架分別收集到新的獨立資料夾，缺少或重複報告必須失敗；工具測試專案亦納入，不以另一目標或相依報告替代。
@@ -617,16 +617,18 @@
 2. **建置：** Release 明確指定 `SpeechMessagePackagePlugin=true` 打包，`Test-PluginAssembly.ps1` 通過，建置 Deployment。
 3. **plan：** 第一次 plan 前，清單即設為 trace、secureConfig 空白、`enabledByOrganization.goodshepherdback=true`。工具支援建立時直接啟用；新安裝只列 CreateAssembly／CreateType／CreateStep，已存在時核對預期差異。
 4. **apply → verify：** 套用剛審核的 plan，一輪完成註冊與啟用；trace 的 verify 必須為 0。清單、DLL 或組織現況變更就重新 plan。
-5. **smoke trace：** 執行 `p0-ping-trace`，人工確認追蹤含 `P0-PING OK`、Assembly `1.0.0.0`、mscorlib `FrameworkFileVersion=4.8.x`、`zh-TW=連線測試成功`、`qps-ploc=［連線測試成功ǂǂǂ］`。階段 0～4 的 Plugin 失敗從系統作業及 Plugin 追蹤記錄查看。
+5. **smoke trace：** 執行 `p0-ping-trace`，人工確認追蹤含 `P0-PING OK`、Assembly `1.0.0.0`、mscorlib `FrameworkFileVersion=4.8.x`、`zh-TW=連線測試成功`、`qps-ploc=［連線測試成功ǂǂǂ］`。`FrameworkFileVersion=4.8.x` 是伺服器 mscorlib 執行環境檔案版本，不是 Plugin TargetFramework；本次 Plugin 目標為 net462。階段 0～4 的 Plugin 失敗從系統作業及 Plugin 追蹤記錄查看。
 6. **收尾：** trace 不變，清單設回停用 → 新 plan → apply → verify=0；追蹤改回「例外狀況」，確認 smoke 資料已清除。中途失敗也須完成停用及清理。
 
 **判斷（使用者 2026-11-04 核定）：**
 
+前次 net48 apply 在伺服器 `TargetFrameworkVersionValidator.ValidateInternal()` 的 `String.Format` 呼叫中發生 `FormatException`。Microsoft KB 4345239 說明該驗證器會拒絕不支援的 Plugin 目標框架；另一個公開案例也記錄相同格式化多載並以 net462 解決。故「net48 目標不相容、格式化例外遮住原拒絕訊息」屬高度可信推論，仍未證明現場 net462 可註冊，也未證明例外格式化失敗的確切成因。細節與限制見 `驗收\P0-13_部署失敗調查報告_供Claude複核.md`。
+
 | 結果 | 後續 |
 |---|---|
 | 第 0～6 步由使用者回報全部成功 | 最小驗收通過，再依下方完整判準結束階段 0 |
-| 第 3、4 步失敗（連線、權限、沙箱拒絕、簽章） | 回到 P0-12 或 P0-10 定位；連線方式不可行就回報，不改回手動註冊 |
-| 第 5 步顯示不是 4.8 | 依 D21 評估改 4.6.2，回報使用者決定 |
+| 第 3、4 步失敗（連線、權限、沙箱拒絕、簽章） | 回到 P0-12 或 P0-10 定位；若 net462 組件仍在目標框架驗證階段出現同一個 `FormatException`／`0x80040216`，表示本次框架推論未解決故障，停止並另行調查；連線方式不可行就回報，不改回手動註冊 |
+| 第 5 步未出現預期的 `FrameworkFileVersion=4.8.x` | 這是伺服器 mscorlib 執行環境檔案版本的觀察，不代表 Plugin TargetFramework；請核對伺服器環境與追蹤資料，不要因此把 Plugin 改回 net48 |
 | 第 5 步 smoke 逾時、沒有追蹤 | 請使用者確認伺服器沙箱處理服務、非同步服務及追蹤設定 |
 | 收尾無法停用或清理 | 回報，由使用者處理，不再建立測試資料 |
 
diff --git a/文件/規劃設計書/實作計畫-3.3/02_階段1_基礎層與國際化地基.md b/文件/規劃設計書/實作計畫-3.3/02_階段1_基礎層與國際化地基.md
index d033c2211..0ef5b08ad 100644
--- a/文件/規劃設計書/實作計畫-3.3/02_階段1_基礎層與國際化地基.md
+++ b/文件/規劃設計書/實作計畫-3.3/02_階段1_基礎層與國際化地基.md
@@ -24,7 +24,7 @@
 | P1-03 | `Line.Messaging` 改雙目標＋Sender、Background、webhookEventId、redelivery | P1-02 |
 | P1-04 | 拆出 `ToolUtility.Core`（雙目標）＋補 4 個方法＋product 參數化 | P1-02 |
 | P1-05 | `SpeechMessage.Localization` 完整版 | 階段 0 |
-| P1-06 | 教會時鐘 `IChurchClock`（含 4.8 的 IANA→Windows 對照） | P1-05 |
+| P1-06 | 教會時鐘 `IChurchClock`（含 .NET Framework 的 IANA→Windows 對照） | P1-05 |
 | P1-07 | `Money` 與格式化 | P1-05 |
 | P1-08 | 教會覆寫快照快取（`ChurchTextOverrideCache`，放在 Tenancy） | P1-05、P1-04、P1-10 |
 | P1-09 | I18nCheck 第二版：完整 resx 檢查、長度上限、en 完成度報告 | P1-05 |
@@ -58,9 +58,9 @@
 **新增** `tests-shared\ArchitectureTests\`，以原始碼連結方式放進 `SpeechMessage.Localization.Tests`（net10 目標）。階段 2 再搬到 `SpeechMessage.Hosting.Tests`。
 
 **做法**：讀取 .sln 內每個 csproj（XML），檢查以下規則：
-- 雙目標專案（`TargetFrameworks` 含 `net48`）只能參考雙目標專案；不能參考 `Microsoft.Extensions.*`、`Microsoft.AspNetCore.*`、`Microsoft.PowerPlatform.Dataverse.Client`。
+- 雙目標產品專案（`TargetFrameworks` 含 `net462`）只能參考同時提供 `net462` 的雙目標專案；不能參考 `Microsoft.Extensions.*`、`Microsoft.AspNetCore.*`、`Microsoft.PowerPlatform.Dataverse.Client`。
   - 例外：`ToolUtility.Core` 的 net10 目標可以參考 `Microsoft.PowerPlatform.Dataverse.Client`，只為了取得 `Microsoft.Xrm.Sdk` 型別。
-- net48 Plugin 專案只能參考雙目標專案。
+- net462 Plugin／Workflow 專案只能參考含 net462 目標的雙目標專案；沙箱組件及合併輸入不可高於 v4.6.2（使用者依 D21 於 2026-11-04 核定）。測試執行器可用 net48，但須驗證它引用的是 net462 產品輸出；net48 不得作為部署產品目標。
 - 層級規則依 00 第 3.4 節：每個專案標一個層級，參考只能往下。
 - 主機（ChurchReport、LineWebhook）之間不互相參考。
 
@@ -81,8 +81,8 @@
    - 型別：Text、Flex、Template、Image、QuickReply、RichMenu 請求；
    - 黃金檔由目前的 net10 版本產生；
    - 改雙目標後，兩個目標都必須產生**逐字相同**的 JSON。
-2. **改雙目標：** `TargetFrameworks=net48;net10.0`。
-   - net48 缺的 API 用 `src-shared\Polyfills` 或 `#if NET48` 處理；
+2. **改雙目標：** `TargetFrameworks=net462;net10.0`。
+   - .NET Framework 缺的 API 用 `src-shared\Polyfills` 或 `#if NETFRAMEWORK` 處理；避免只檢查 `NET48`，因 net462 不會定義該符號；
    - **若需要 `#if` 的檔案超過 30 個，停止並回報**，可能改為「只把 Plugin 需要的訊息型別抽成小專案」。
 3. **補型別：**
    - `Messages\Sender\Sender.cs`：參考 `.其他解決方案\Dynamics365Line\Line.Messaging\Messages\Sender\Sender.cs`，舊檔的 XML 註解是從 Block Style 複製的錯誤內容，要重寫。
@@ -92,7 +92,7 @@
    - Webhook 模型補 `WebhookEventId`、`DeliveryContext.IsRedelivery`。
 4. **驗簽：**
    - 既有 `Webhooks\WebhookRequestMessageHelper.VerifySignature` 吃 `HttpRequestMessage`，保留；
-   - 新增與傳輸無關的 `LineSignature.Verify(byte[] body, string signatureHeader, string channelSecret)`：HMAC-SHA256 加固定時間比較，net48 自行實作固定時間比較；
+   - 新增與傳輸無關的 `LineSignature.Verify(byte[] body, string signatureHeader, string channelSecret)`：HMAC-SHA256 加固定時間比較，net462 自行實作固定時間比較；
    - ASP.NET Core 端在階段 3 使用。
 
 **先寫的測試**
@@ -106,7 +106,7 @@
 
 ## P1-04　拆出 `ToolUtility.Core`（雙目標）
 
-**目標：** 把「只依賴 `IOrganizationService` 與 `Microsoft.Xrm.Sdk`」的資料操作，從 `ToolUtility`（net10）拆到 `ToolUtility.Core`（net48;net10.0）。Plugin 與網站共用同一份。
+**目標：** 把「只依賴 `IOrganizationService` 與 `Microsoft.Xrm.Sdk`」的資料操作，從 `ToolUtility`（net10）拆到 `ToolUtility.Core`（net462;net10.0）。Plugin 與網站共用同一份。
 
 **步驟**
 
@@ -119,7 +119,7 @@
      - **不可搬的條件：** 使用 `Microsoft.Extensions.*`、`ServiceClient`、`HttpClient`、`IMemoryCache`、檔案系統、`ILogger`，或任何 net10 專屬 API。
    - 盤點結果寫成 `wp-reports\P1-04-inventory.md`。
 2. **新增專案：** `ToolUtility.Core\ToolUtility.Core.csproj`，雙目標、`SpeechMessageStrictCode=true`。
-   - **SDK 參考：** net48 用 `Microsoft.CrmSdk.CoreAssemblies`，net10 用 `Microsoft.PowerPlatform.Dataverse.Client`；兩者都設 `PrivateAssets=all`。
+   - **SDK 參考：** net462 用 `Microsoft.CrmSdk.CoreAssemblies`，net10 用 `Microsoft.PowerPlatform.Dataverse.Client`；兩者都設 `PrivateAssets=all`。
    - **命名空間：** 維持 `ToolUtilityNameSpace`，ChurchReport 的呼叫在原始碼層級不用改。
 3. **搬移：**
    - 用 `git mv` 保留歷史，一次搬一個資料夾；
@@ -193,11 +193,11 @@
   - `TodayInChurch(string ianaTimeZoneId)`；
   - `ToUtc(LocalDateTimeOfChurch local)`。
 - **net10：** 直接用 `TimeZoneInfo.FindSystemTimeZoneById(iana)`。主機啟動時檢查 ICU 模式，不是 ICU 就拒絕啟動（設計附錄 E.5）。
-- **net48：**
+- **net462（D365 沙箱使用的 .NET Framework 目標）：**
   - 以內建對照表把 IANA 換成 Windows 時區 ID；
   - 對照表 `IanaWindowsZones.g.cs` 由 `tools\I18nCheck generate-timezones --cldr <windowsZones.xml>` 產生；
   - CLDR 版本號寫在檔頭註解，原始 XML 存在 `tools\I18nCheck\data\windowsZones.xml`。
-- **時間來源：** 以 `TimeProvider` 抽象。net48 沒有 `TimeProvider`，自行定義 `IClock`，兩個目標共用。
+- **時間來源：** 以 `TimeProvider` 抽象。net462 沒有 `TimeProvider`，自行定義 `IClock`，兩個目標共用。
 
 **先寫的測試**（兩個目標都跑，結果必須相同）
 - `Taipei_NoDst_Plus8`
@@ -205,7 +205,7 @@
 - `LosAngeles_DstStart_2027_03_14`
 - `Birthday_DateOnly_NeverShiftsDay`
 - `UnknownIana_Throws`
-- `Net48Mapping_CoversAllSupportedChurchTimeZones`：Asia/Taipei、Asia/Hong_Kong、Asia/Singapore、Asia/Kuala_Lumpur、Europe/London、America/Los_Angeles、America/New_York、Australia/Sydney。
+- `NetFrameworkMapping_CoversAllSupportedChurchTimeZones`：在 net462 產品目標及 net10.0 目標測試 Asia/Taipei、Asia/Hong_Kong、Asia/Singapore、Asia/Kuala_Lumpur、Europe/London、America/Los_Angeles、America/New_York、Australia/Sydney。測試執行器若為 net48，仍須確認載入的產品組件是 net462。
 
 ---
 
diff --git a/文件/規劃設計書/實作計畫-3.3/03_階段2-4_共用送訊_Webhook_金流.md b/文件/規劃設計書/實作計畫-3.3/03_階段2-4_共用送訊_Webhook_金流.md
index 61e07e9ec..480f74d57 100644
--- a/文件/規劃設計書/實作計畫-3.3/03_階段2-4_共用送訊_Webhook_金流.md
+++ b/文件/規劃設計書/實作計畫-3.3/03_階段2-4_共用送訊_Webhook_金流.md
@@ -8,7 +8,7 @@
 
 - 估時：10～13 天（國際化＋2～3 天）。
 - 目標：
-  - LINE 送訊只剩一份。單人推播放在 `LineMessagingProcessor.Core`（雙目標），Plugin 可以直接推播；
+  - LINE 送訊只剩一份。單人推播放在 `LineMessagingProcessor.Core`（`net462;net10.0`），Plugin 可以直接推播；依使用者 2026-11-04 核定的 D21，所有部署至 D365 沙箱的 Plugin／Workflow 均以 net462 為目標；net462 沙箱的 TLS／推播實機驗收仍須依下方 P2-04 前置完成；
   - `new_linechannel` 加密存放 Token；
   - 對外待辦只處理宮格圖、QR 圖片、撥款查詢三種，可由 D365 建立、由 .NET 10 執行。
 - 前提（使用者 2026-11-04 核定）：沙箱對 `api.line.me` 的推播驗收已移出 P0-13，改在本檔「P2-04 前置驗收」完成；未通過不得開始 P2-04 以後的推播工作。
@@ -91,7 +91,7 @@
 - 只能對白名單組織執行。
 
 **先寫的測試：**
-- `RoundTrip_Net48AndNet10_Compatible`：net48 加密、net10 解密，反之亦然；兩個目標共用固定測試向量檔。
+- `RoundTrip_Net462AndNet10_Compatible`：net462 加密、net10 解密，反之亦然；兩個目標共用固定測試向量檔。
 - `Tampered_Ciphertext_Throws`、`Tampered_Mac_Throws`
 - `WrongKey_Throws`、`UnknownVersion_Throws`
 - `Exception_NeverContainsSecret`
diff --git a/文件/規劃設計書/實作計畫-3.3/04_階段5-8_Plugin重建_切換_收尾.md b/文件/規劃設計書/實作計畫-3.3/04_階段5-8_Plugin重建_切換_收尾.md
index ca4f437c3..2d91598ae 100644
--- a/文件/規劃設計書/實作計畫-3.3/04_階段5-8_Plugin重建_切換_收尾.md
+++ b/文件/規劃設計書/實作計畫-3.3/04_階段5-8_Plugin重建_切換_收尾.md
@@ -6,6 +6,8 @@
 
 # 階段 5　Plugin／Workflow 重建（36 個 DLL → 2 個）
 
+**框架核定（使用者 2026-11-04）：** 所有實際部署至 D365 沙箱的 Plugin 與 Workflow 組件一律以 `.NET Framework 4.6.2`（`net462`）為目標；`LineMessagingProcessor.Core`、`SpeechMessage.Localization` 維持 `net462;net10.0` 雙目標。測試執行器或離線舊程式 harness 可在明確標示下保留 `net48`，但不得讓 `net48` 產品組件進入部署包。goodshepherdback 曾拒絕 net48；net462 是否可註冊仍須由使用者驗收。伺服器 `mscorlib` 的 `FrameworkFileVersion=4.8.x` 是執行環境版本，不代表 Plugin 的目標框架。
+
 - **估時：** 20～27 天（國際化＋5～7 天）。
 - **目標：** 舊外掛與工作流程活動的功能，全部由 `SpeechMessage.Dynamics.Plugins`、`SpeechMessage.Dynamics.Workflows` 承接。
   - 規則全部在 `GoodShepherd.Core`；
@@ -16,7 +18,7 @@
 
 ### P5-01　外殼基礎與測試替身
 
-**`SpeechMessage.Dynamics.Plugins\Infrastructure\`**（net48；Workflows 專案以原始碼連結共用）
+**`SpeechMessage.Dynamics.Plugins\Infrastructure\`**（net462；Workflows 專案以原始碼連結共用）
 
 | 型別 | 職責 |
 |---|---|
@@ -43,7 +45,7 @@
 
 ### P5-02　舊程式黃金資料產生器 `tools\LegacyGolden\PluginHarness`
 
-- **專案設定：** net48 console，不進 .sln。
+- **專案設定：** net48 console，不進 .sln；只作離線舊程式黃金資料測試，不部署至 D365 沙箱。
 - **編譯舊原始碼：** 把舊 Plugin 原始碼以 `<Compile Include>` 編進 harness。
   - 原始碼先用 `Convert-TextEncoding.ps1` 轉成 UTF-8，放在暫存資料夾；**不修改** `.其他解決方案` 原檔；
   - 改用 `Microsoft.CrmSdk.CoreAssemblies 9.0.2` 編譯，舊程式原本用的是 Xrm.Sdk 8.2。
@@ -69,7 +71,7 @@
 
 **使用者 2026-11-04 核定：** 原 P0-13 第 8、9 步移至階段 5。先以骨架完成下列驗收，再佈署第一個正式 Plugin；現在不補改 P0 Plugin。所有連線／寫入／smoke 由使用者執行，唯一組織 goodshepherdback。
 
-1. **先補自身 FileVersion 追蹤：** 以 TDD 讓 Plugin 追蹤輸出自身組件的 FileVersion，不能拿固定 AssemblyVersion 或 mscorlib FrameworkFileVersion 代替。測試確認數值確實來自 Plugin 組件、無秘密；經該階段守門及必要審查後才交使用者驗收。
+1. **先補自身 FileVersion 追蹤：** 以 TDD 讓 Plugin 追蹤輸出自身組件的 FileVersion，不能拿固定 AssemblyVersion 或伺服器 `mscorlib` 的 FrameworkFileVersion（目前觀察為 4.8.x）代替。兩者分別是 Plugin 組件檔案版本與伺服器執行環境版本；測試確認數值確實來自 Plugin 組件、無秘密；經該階段守門及必要審查後才交使用者驗收。
 2. **保存第一版：** 清理與 trace 驗收前提仍成立；以 trace、secureConfig 空白、enabled=true 註冊骨架，verify=0、smoke trace 通過。於 repo 外保存第一版已合併 DLL 與清單，記下原步驟資料庫 ID、追蹤中的 Plugin FileVersion；不複製金鑰。
 3. **只更新 FileVersion：** AssemblyVersion 固定 `1.0.0.0`，沿用相同名稱、簽章及步驟；只遞增 SpeechMessageBuildNumber，Release 明確打包並通過 Test-PluginAssembly。plan 必須只有 UpdateAssembly，不能重建步驟。apply 前保存該次自動產生的 export.json（是本次更新的 apply 目錄，不是初次安裝前的空備份）。verify=0、smoke trace 後人工確認原步驟 ID 不變且沙箱追蹤顯示新的 Plugin FileVersion。
 4. **回復第一版：** 以 `rollback --org goodshepherdback --from <本次更新apply前的export.json>` 還原；rollback 只還原組件內容與既有步驟啟用狀態，不還原 unsecure／secure 設定或秘密，因此全程維持相同 trace 設定。將本機比較用 DLL 換回保存的第一版，再 verify=0、smoke trace，確認步驟 ID 不變及追蹤回到第一版 Plugin FileVersion。
@@ -136,14 +138,14 @@
 
 ## 5.5 Workflows 專案
 
-- **專案設定：** `SpeechMessage.Dynamics.Workflows`，net48，參考 `Microsoft.CrmSdk.Workflow`，打包、簽章、ILRepack 與 Plugins 專案相同。
+- **專案設定：** `SpeechMessage.Dynamics.Workflows`，net462，參考 `Microsoft.CrmSdk.Workflow`，打包、簽章、ILRepack 與 Plugins 專案相同。
 - **活動屬性：**
   - 每個 `CodeActivity` 的 InputArgument／OutputArgument 與舊活動同名、同型別；
   - 群組名稱「音訊科技」（WorkflowActivityGroupName）；
   - 顯示名稱寫在註冊清單 `plugins.registration.json`。
 - **改參數的規則：** 活動要改參數時，新增一個新類別（例如 `WeeklyReportGenerationActivityV2`），不改舊類別（設計 10.8 節）。
 
-**先寫的測試：** 以 `WorkflowInvoker` 執行活動（net48 可用）。
+**先寫的測試：** 以 net48 測試執行器透過 `WorkflowInvoker` 執行 net462 活動；net48 僅是測試宿主，不是部署組件目標。
 - `WorkflowInvoker` 需要的 `IWorkflowContext`、`IOrganizationServiceFactory` 以擴充提供；
 - 斷言輸入輸出參數與規則呼叫。
 
diff --git a/文件/規劃設計書/實作計畫-3.3/90_附錄_規格與範本.md b/文件/規劃設計書/實作計畫-3.3/90_附錄_規格與範本.md
index 7e47540ff..8c3cd5407 100644
--- a/文件/規劃設計書/實作計畫-3.3/90_附錄_規格與範本.md
+++ b/文件/規劃設計書/實作計畫-3.3/90_附錄_規格與範本.md
@@ -16,7 +16,7 @@
 |---|---|---|---|
 | `new_defaultlanguage` | 預設語言 | 單行文字（16） | BCP 47，預設 `zh-TW`；由 `ChurchLocaleChangedPlugin` 同步驗證 |
 | `new_enabledlanguages` | 啟用語系 | 單行文字（200） | 逗號分隔，例如 `zh-TW`；只接受已開放的語系，`en` 在開放前會被拒絕 |
-| `new_timezone` | 時區 | 單行文字（64） | IANA，例如 `Asia/Taipei`；必須同時有 .NET 10 與 4.8 對照 |
+| `new_timezone` | 時區 | 單行文字（64） | IANA，例如 `Asia/Taipei`；必須同時有 net462 與 .NET 10 對照 |
 | `new_countrycode` | 國家 | 單行文字（2） | ISO 3166-1 alpha-2，例如 `TW` |
 | `new_textchangedon` | 教會改字時間 | 日期時間（使用者當地，存 UTC） | 由 `LocalizedTextChangedPlugin` 更新 |
 | `new_linechannelid` | LINE Channel | 查閱 → `new_linechannel` | 多間教會可指向同一 Channel |
@@ -66,7 +66,7 @@ v1:{base64(iv 16 bytes)}:{base64(ciphertext)}:{base64(mac 32 bytes)}
 - **加密：** AES-256-CBC、PKCS7，每次產生新的隨機 IV（`RandomNumberGenerator`）。
 - **MAC 計算：** `HMAC-SHA256(macKey, ASCII("v1") || iv || ciphertext)`。
 - **解密順序：** 先驗 MAC（固定時間比較），通過才解密。
-- **固定時間比較：** net10 用 `CryptographicOperations.FixedTimeEquals`；net48 自行實作「逐位元組 XOR 累加、長度不同也跑完」。兩個目標共用同一組測試向量。
+- **固定時間比較：** net10 用 `CryptographicOperations.FixedTimeEquals`；net462 自行實作「逐位元組 XOR 累加、長度不同也跑完」。兩個目標共用同一組測試向量。
 - **金鑰輪替：** 封包版本號保留給未來的 `v2`。輪替時以工具重新加密全部 Channel。
 
 ### A.6　`new_localizedtext`（新實體，組織擁有）
diff --git a/文件/規劃設計書/實作計畫-3.3/驗收/P0-13_部署失敗調查報告_供Claude複核.md b/文件/規劃設計書/實作計畫-3.3/驗收/P0-13_部署失敗調查報告_供Claude複核.md
index d9bcbab52..ba5d9eeef 100644
--- a/文件/規劃設計書/實作計畫-3.3/驗收/P0-13_部署失敗調查報告_供Claude複核.md
+++ b/文件/規劃設計書/實作計畫-3.3/驗收/P0-13_部署失敗調查報告_供Claude複核.md
@@ -8,15 +8,15 @@ P0-13 是 goodshepherdback 的最小 trace 骨架驗收：註冊／啟用 Diagno
 
 目前第二次 apply 仍失敗。plan 成功不代表已註冊；verify、smoke、追蹤復原與停用收尾尚未完成，P0-13／階段 0 未通過。
 
-使用者已核定先離線試做 .NET Framework 4.6.2 相容候選；截至本報告初稿尚未執行試做，不能宣稱候選能建置、通過檢查或註冊。
+本報告初稿時，使用者先核定隔離離線試做 .NET Framework 4.6.2。其後使用者於 2026-11-04 依 D21 核定將 net462 改為正式產品目標，詳見第 8 節；本地驗證不能代替 D365 註冊結果。
 
 ## 2. 已確認的本機與使用者操作證據
 
-- 既定 D21：Plugin 專案的 TargetFramework=net48；Core 與 Localization 為 net48;net10.0。
+- 發生失敗時的既定 D21：Plugin 專案 TargetFramework=net48；Core 與 Localization 為 net48;net10.0。使用者於 2026-11-04 另核定正式改用 net462／net462;net10.0，取代舊決定。
 - Plugin 原 Release 已明確打包成功：四個輸入合併、強式名稱簽章，AssemblyVersion=1.0.0.0，大小 723456 bytes。
 - 原 Test-PluginAssembly.ps1 回報 ok=true；readable、strongName、targetFramework、allowedReferences、noDrawing、diagnosticsPlugin、size 均為 true。
 - 部署 DLL 的 SHA-256：70D18A13830116F2DFB967AA3DD25A659DF221949955B18C6F92BB6324776B24。
-- 原檢查器只讀 PE／Metadata，不載入或執行 Plugin；其 targetFramework 契約固定為 net48。這是本機政策檢查，不是 D365 相容性證明。
+- 失敗當時的檢查器只讀 PE／Metadata、不載入或執行 Plugin，且只接受 net48。net462 改版已將檢查器契約改為只接受 v4.6.2；任何本機中繼資料檢查都不是 D365 相容性證明。
 - 使用者已停用舊自訂步驟／流程、取消 3 筆待處理舊作業，並把 Plugin 追蹤設為「全部」。舊 LINE token 與會友 LINE ID 依使用者另行核定保留；不宣稱資料已清除。
 - 清單唯一 Plugin：DiagnosticsPingPlugin，Create／task／PostOperation／Asynchronous／rank 1；mode=trace，secureConfig 空白，goodshepherdback=true。
 - 最新經審核 plan：2026-11-04T06:14:02.6994698+00:00（台灣時間 14:14），只列 CreateAssembly、CreateType、CreateStep，沒有秘密佔位。
@@ -65,24 +65,28 @@ PluginAssemblyServiceInternal.ValidateAssemblyMetadata
 
 ## 4. 可以下的結論與仍未知事項
 
-**已定位：** 本次事件顯示 Plugin 組件建立流程，在 D365 的目標框架驗證器內發生格式化例外；不能將這筆錯誤當成 DiagnosticsPingPlugin.Execute 的業務邏輯失敗。
+**高度可信的根因推論：** 本次事件 18176 發生於 Plugin 組件建立的目標框架驗證器，呼叫鏈包含 `TargetFrameworkVersionValidator.ValidateInternal()`、`String.Format` 與 `PluginValidatorBase.Validate()`。Microsoft KB 4345239 記錄同一驗證器在 Plugin 目標框架不相容時拒絕組件，並以兩個格式參數說明所需／目前框架；因此最符合現有證據的解釋是伺服器拒絕 net48 目標，而格式化拒絕訊息時另發生 `FormatException`，外層只留下通用錯誤碼。這仍是推論，不能寫成已由伺服器原始拒絕訊息證實，也不能將事件當成 `DiagnosticsPingPlugin.Execute` 的業務邏輯失敗。
 
 **尚未證實：**
 
-1. 驗證器正在處理哪一個具體條件，及其原本欲回報的錯誤。
-2. 該 D365 元件版本實際接受的 Plugin 目標框架上限、相關設定或所需支援更新。
-3. 是否有本地化資源格式問題遮蔽另一個錯誤。String.Format 堆疊只支持調查這項假設，尚無資源內容或程式碼證據。
-4. 改成 net462 是否能成功註冊；尚未做實機比較，離線試做也不能證明這一點。
-5. 伺服器上沙箱程序實際載入的 .NET 版本。
+1. 原本拒絕的精確條件與伺服器預期回傳的拒絕訊息；本次沒有保存該訊息內容。
+2. `Microsoft.Crm.ObjectModel.dll` 9.1.49.21825 對應哪個 4.8 沙箱支援更新。Microsoft 地端文件列出 4.6.2～4.8，並註明 4.8 沙箱支援已加入，但沒有公布適用的最低伺服器組建；不能僅憑檔案版本斷言現場缺少哪個更新。
+3. `FormatException` 是資源格式、錯誤格式字串或其他格式化輸入造成；事件證據目前只保存堆疊方法名稱，未保留或核對 `String.Format` 的具體多載。
+4. 改為 net462 是否能在該主機註冊；離線建置、簽章和 PE 檢查都不能證明這一點。
+5. 沙箱程序實際載入的 .NET Framework 執行環境及部分信任限制。
 
 不能寫成「伺服器沒有 .NET 4.8」或「D365 9.1 一律只支援 4.6.2」。目前 Microsoft 地端文件列出 4.6.2～4.8，但相關段落沒有最低伺服器更新，不能直接套用到上述特定 DLL 版本。
 
 官方參考：
+- [Microsoft KB 4345239：Assembly version error importing Dynamics 365 solution](https://learn.microsoft.com/en-us/troubleshoot/power-platform/dataverse/working-with-solutions/assembly-version-error-importing-dynamics-365-solution)（描述不相容框架的驗證呼叫鏈與兩參數拒絕訊息；該文章不是現場 9.1.49.21825 的組建對照表）
 - https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/developer/visual-studio-dot-net-framework?view=op-9-1
 - https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/developer/supported-extensions?view=op-9-1
 - https://learn.microsoft.com/en-us/power-apps/developer/data-platform/reference/web-service-error-codes
+- [社群案例：String.Format 雙參數多載與 Plugin 註冊 FormatException](https://nishantrana.me/2020/11/11/system-formatexception-input-string-was-not-in-a-correct-format-exception-while-registering-plugin-in-dynamics-365/)（不同伺服器版本的佐證案例，不能證明本次環境的版本門檻）
 
-## 5. 已核定的離線試驗
+## 5. 已核定的離線試驗（歷史紀錄，已由第 8 節正式決定取代）
+
+本節記錄使用者最初只允許隔離試做時的限制。2026-11-04 正式 net462 核定已取代「保留正式 net48、只在副本試做」要求；目前操作以第 8 節為準。
 
 1. 保留正式產品專案的 net48 與原部署 DLL；在隔離暫存副本對 Plugin／Core／Localization 試做 net462。
 2. 保留既有程式、測試與套件版本，跑相關既有測試；不放寬分析器、弱點稽核或原組件檢查器。
@@ -115,6 +119,44 @@ PluginAssemblyServiceInternal.ValidateAssemblyMetadata
 
 使用者手動提交 714465fd 已保留；其中清單 true 已在 HEAD。驗收結束須另以新提交保存 false，不能改寫該提交。所有後續變更都要同步原驗收步驟文件。
 
-## 8. 離線試做結果
+## 8. net462 正式遷移與離線驗證
+
+**使用者核定（2026-11-04）：** 依 D21 退路正式採用 .NET Framework 4.6.2，而非只做隔離試做。Plugin 目標改為 `net462`；LineMessagingProcessor.Core 與 SpeechMessage.Localization 改為 `net462;net10.0`；測試執行器可維持 `net48`，但必須驗證引用的是 `net462` 產品輸出。日後會部署到 D365 沙箱的 Plugin／Workflow 一律以 `net462` 為目標。這項核定取代先前 §5「只在隔離副本試做、正式目標仍為 net48」的限制。
+
+**根因狀態：** 事件 18176 位於 `TargetFrameworkVersionValidator.ValidateInternal()`，使 net48 組件框架不相容成為高度可信假設；尚未證實伺服器接受 net462。現場 `Microsoft.Crm.ObjectModel.dll` 檔案版本 `9.1.49.21825` 與 `mscorlib.dll` 檔案版本 `4.8.4795.0` 都保留為觀察證據，不代表特定更新已提供 4.8 Plugin 沙箱支援。smoke 的 `FrameworkFileVersion=4.8.x` 驗證的是伺服器載入的 mscorlib 執行環境，與 Plugin 組件宣告的 net462 目標不同。
+
+**net462 套件資產盤點（2026-11-04）：** 三個產品專案已還原為 net462。Plugin 的全部套件 compile/runtime 資產如下；ILRepack、分析器與 ReferenceAssemblies 僅作建置用途，不是 ILRepack 合併輸入：
+
+| 套件 | Compile 資產 | Runtime 資產 | 說明 |
+|---|---|---|---|
+| ILRepack.Lib.MSBuild.Task 2.0.48 | 無 | 無 | 建置工具 |
+| Microsoft.CodeAnalysis.BannedApiAnalyzers 5.6.0 | 無 | 無 | 分析器 |
+| Microsoft.NETFramework.ReferenceAssemblies 1.0.3 | 無 | 無 | 參考組件建置套件 |
+| Microsoft.NETFramework.ReferenceAssemblies.net462 1.0.3 | 無 | 無 | 4.6.2 參考組件，不使用本機 4.8 API 編譯 |
+| Microsoft.Bcl.AsyncInterfaces 6.0.0 | `lib/net461` | `lib/net461` | .NET Framework 相容資產 |
+| Microsoft.CrmSdk.CoreAssemblies 9.0.2.56 | `lib/net462` | `lib/net462` | Xrm SDK 外部參考，不合併 |
+| Newtonsoft.Json 13.0.4 | `lib/net45` | `lib/net45` | 四個合併輸入之一；v4.5 低於 v4.6.2 上限 |
+| System.Buffers 4.5.1 | `ref/net45` | `lib/net461` | Framework 資產 |
+| System.Memory 4.5.4 | `lib/net461` | `lib/net461` | Framework 資產 |
+| System.Numerics.Vectors 4.5.0 | `ref/net46` | `lib/net46` | Framework 資產 |
+| System.Runtime.CompilerServices.Unsafe 6.0.0 | `lib/net461` | `lib/net461` | Framework 資產 |
+| System.ServiceModel.Http 4.10.3 | `ref/net461` | `lib/net461` | Framework 資產 |
+| System.ServiceModel.Primitives 4.10.3 | `ref/net461` | `lib/net461` | Framework 資產 |
+| System.Text.Encodings.Web 6.0.1 | `lib/net461` | `lib/net461` | Framework 資產 |
+| System.Text.Json 6.0.11 | `lib/net461` | `lib/net461` | Framework 資產 |
+| System.Threading.Tasks.Extensions 4.5.4 | `lib/net461` | `lib/net461` | Framework 資產 |
+| System.ValueTuple 4.5.0 | `ref/net461` | `lib/net461` | Framework 資產 |
+
+Core 的套件為 BannedApiAnalyzers 5.6.0、ReferenceAssemblies 1.0.3、ReferenceAssemblies.net462 1.0.3（均無 compile/runtime 資產）及 Newtonsoft.Json 13.0.4（compile/runtime 均為 lib/net45）。Localization 僅有上述三個無 compile/runtime 資產的建置套件。兩個產品專案由 Plugin 的 `ProjectReference` 解析至 net462 輸出。上述 net462 目標未解析到 `netstandard2.0` compile/runtime 資產。Metadata 檢查器確認四個實際合併輸入的 `TargetFrameworkAttribute` 為 Plugins `v4.6.2`、Localization `v4.6.2`、Core `v4.6.2`、Newtonsoft.Json `v4.5`；合併後部署 DLL 為 `v4.6.2`。簽章 DLL 檢查器結果為 `ok=true`、AssemblyVersion `1.0.0.0`、FileVersion `1.0.0.37`、大小 723456 bytes、10 個 assembly references，且沒有 `System.Runtime` 或 `netstandard` facade 參考。外部 Xrm SDK 由 D365 提供；這些離線檢查不證明伺服器註冊或沙箱執行相容。
+
+**離線驗證結果（2026-11-04）：**
+
+- Release 全方案建置成功，0 錯誤、0 警告；Release 全方案測試 1121／1121 通過，涵蓋 net462 相依產品的 net48 測試執行器、net10.0 目標及 ChurchReport 596／596。
+- 方案外 `ToolUtility.Tests` 63／63 通過，建置出現該舊測試專案既有的 NU1701、nullable 與重複 using 警告；PluginPackaging.Tests 16／16、Plugin 組件樣本 10／10、Plugin 打包契約樣本 6／6 通過。
+- 正式金鑰只以存在性檢查確認後，Release net462 簽章打包與組件檢查通過；輸出留在被 `.gitignore` 排除的 `artifacts/plugins/net462-p0-13-20261104` 專屬目錄，未覆寫舊版 `artifacts/plugins/SpeechMessage.Dynamics.Plugins.dll`。
+- Debug tracer 7／7 通過；其觸發的 ChurchReport Debug 建置輸出 ImageSharp 授權提示，不影響本次 Debug 測試結果。Changed 編碼檢查 514 檔、0 問題；重複碼掃描 26 專案／879 檔、0 新增；I18n verify 通過；套件版本守門 39 專案／149 引用、0 失敗，隔離路徑測試 5／5；分析器探測 26 項、0 失敗，預期由 `RS0030` 阻擋違規；全方案易受攻擊套件查詢未列出弱點。
+- 本機 PluginPackaging 與 ToolUtility 測試沒有連線 D365／LINE。這些結果只證明離線建置、打包契約與測試，不等於 net462 已被現場 D365 接受。
+
+**根因判斷界線：** Microsoft KB 4345239 記載不受支援的 Plugin 目標框架會由 `TargetFrameworkVersionValidator.ValidateInternal()` 拒絕，並展示相同驗證呼叫鏈；這支持「net48 目標可能遭拒」的高度可信推論，但該 KB 的錯誤是正常的框架不符訊息，沒有證明本次伺服器 `String.Format` 本身如何失敗，也沒有證明現場 `9.1.49.21825` 的 4.8 沙箱支援門檻。官方地端文件列出 net462 至 net48，並說明 4.8 沙箱支援已加入、net462 仍受支援，但未列現場組建對應的最低更新。是否接受 net462 仍須由使用者重跑註冊確認。
 
-待執行；完成後更新本節。原有本機綠燈、文件審查結果及部署失敗紀錄，不是本次 net462 成功的證據。
+**實機驗證狀態：** Codex 不執行 D365／LINE 操作。離線驗證與審查完成後，由使用者以新 net462 DLL 重新建置、重新 plan 並審核，再依序 apply、verify=0、smoke 和停用收尾。舊 net48 plan 綁定舊組件雜湊，不得重用；新 plan 預期仍只有 CreateAssembly／CreateType／CreateStep。直到使用者回報註冊、trace smoke 與收尾成功，不能宣稱 net462 被伺服器接受或 P0-13 完成。
diff --git a/文件/規劃設計書/實作計畫-3.3/驗收/P0_Plugin骨架佈署步驟.md b/文件/規劃設計書/實作計畫-3.3/驗收/P0_Plugin骨架佈署步驟.md
index d98d91683..05dc5eb0d 100644
--- a/文件/規劃設計書/實作計畫-3.3/驗收/P0_Plugin骨架佈署步驟.md
+++ b/文件/規劃設計書/實作計畫-3.3/驗收/P0_Plugin骨架佈署步驟.md
@@ -9,7 +9,7 @@
 >
 > **使用者 2026-11-04 追加核定：** 本次 P0-13 trace 驗收保留資料中的舊 LINE token 與會友 `new_lineid`，免除這兩項清理前置，不再等待清空才繼續。這記錄為使用者核定保留資料，不能宣稱資料已清理；舊 Plugin／工作流程維持停用。此例外限本次 trace 驗收，後續階段另依該階段規則執行。
 
-本次只確認：以佈署專案註冊、啟用 net48 Plugin，在沙箱看到版本與雙語文字，最後停用並清理。
+本次只確認：以佈署專案註冊、啟用 net462 Plugin，在沙箱看到版本與雙語文字，最後停用並清理。
 不需要測試 LINE channel，也不需要 Six Labors 授權：Plugins 與 Deployment 的直接、間接相依均無 ImageSharp。
 推播移至 03 的「P2-04 前置驗收」；Plugin 自身 FileVersion 追蹤、更新與回復移至 04 階段 5，現在不改 Plugin。
 
@@ -101,7 +101,9 @@ JSON 中的反斜線必須寫**兩條** `\\`，解析後才是一條；只寫一
 
 **已完成的唯讀查核：** Codex 經使用者核定直接查看虛擬機 GUI 的上述事件，以及 `Microsoft.Crm.ObjectModel.dll` 的詳細資料，版本為 `9.1.49.21825`。續查 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\mscorlib.dll` 的詳細資料，檔案與產品版本均為 **4.8.4795.0**；這證明該檔案存在，不能直接當成沙箱程序實際載入版本或第 5 步 smoke 的驗收結果。本次沒有產品程式修改，也沒有執行新的 plan／apply。
 
-**下一小步（使用者 2026-11-04 追加核定）：** 先離線試做 .NET Framework 4.6.2 相容候選，完成相關測試與打包檢查；只在隔離副本試做，產品預設 `TargetFramework=net48`、原部署 DLL 與原守門保留，不部署、不修改伺服器、不繞過驗證。已見 4.8 核心檔案，不把失敗歸因為未安裝 .NET 4.8；net462 能否註冊仍未證實。上述事件已由 Codex 查看並記錄，不需要使用者重找。試做尚未執行，結果將同步本文件及[供 Claude 複核的調查報告](./P0-13_部署失敗調查報告_供Claude複核.md)。
+**目前處置（使用者 2026-11-04 核定）：** 依 D21 正式採用 .NET Framework 4.6.2（`net462`）：Plugin 目標為 `net462`；Core 與 Localization 為 `net462;net10.0`；測試執行器可維持 `net48`，但必須確認引用的是 `net462` 產品輸出。使用者最新核定取代前一版「保留正式 net48、只做隔離試做」的決定。Codex 已完成 net462 離線建置、套件／框架檢查、簽章打包、測試與 00 §8；目前只待第 10 份 ClaudeOnly 審查。審查通過並提交後，使用者再依第 2～6 步重新驗收。Codex 不連線 D365／LINE、不執行 plan／apply／verify／smoke。
+
+現場 `Microsoft.Crm.ObjectModel.dll` 檔案版本為 `9.1.49.21825`，事件指出 `TargetFrameworkVersionValidator.ValidateInternal()` 內的 `String.Format` 發生 `FormatException`。Microsoft KB 4345239 描述此驗證器會拒絕不相容的 Plugin 目標框架；公開案例也記錄相同格式化多載並以 net462 解決。因此「net48 目標遭拒，格式化例外遮住原訊息」是高度可信推論，但原拒絕訊息與格式化失敗的具體原因未取得。官方地端文件列出 4.6.2～4.8，未指出 4.8 沙箱支援的最低伺服器組建，不能只憑現場 DLL 版本宣稱缺少哪個更新。`mscorlib.dll` 檔案版本 `4.8.4795.0` 與 smoke 欄位 `FrameworkFileVersion=4.8.x` 描述伺服器執行環境，不代表 Plugin 的 `TargetFramework`；smoke 仍預期看到 `4.8.x`。只有使用者重新註冊 net462 組件的結果，才能確認該 D365 主機是否接受它。更多依據與限制見[部署失敗調查報告](P0-13_部署失敗調查報告_供Claude複核.md)。
 
 兩個相同的數字碼沒有指出失敗的是哪一項建立操作，也不能證明沒有部分寫入。後續若需重試，先重新 plan 並審核目前狀態，不重用失敗的 plan，也不要在 apply 後重新以「最新目錄」改寫 `$plan`。
 
@@ -129,10 +131,12 @@ powershell -NoProfile -File .\tools\PluginPackaging\Test-PluginAssembly.ps1 -Ass
 dotnet build .\SpeechMessage.Dynamics.Deployment -c Release
 ```
 
-**預期看到什麼：** 建置成功，已簽章 DLL 通過檢查，AssemblyVersion 固定 `1.0.0.0`，目標為 net48。
+**預期看到什麼：** 建置成功，已簽章 DLL 通過檢查，AssemblyVersion 固定 `1.0.0.0`，Plugin 與合併輸入符合核定的 `net462` 目標契約；Deployment Release 建置成功。這次使用者重新打包會更新部署工具預設讀取的 artifacts/plugins/SpeechMessage.Dynamics.Plugins.dll；先前 net48 plan 不可重用。
 
 **使用者執行結果（2026-11-04）：** Plugin Release 打包成功，0 錯誤、0 警告；組件檢查 `ok=true`，各項檢查均為 true，AssemblyVersion=`1.0.0.0`，大小 723456 bytes。Deployment Release 建置成功，0 錯誤、1 個 `CS1572` 警告（`Line.Messaging/LineMessagingClient.cs:840` 的 `chatId` XML 註解）；此處只記錄使用者提供的輸出，不將該警告宣稱為已驗證的既有基準。
 
+**Codex 離線驗證（2026-11-04）：** net462 正式簽章打包及組件檢查成功，0 錯誤；AssemblyVersion=`1.0.0.0`、FileVersion=`1.0.0.37`、大小 723456 bytes，各項檢查均為 true。產物位置為 `artifacts/plugins/net462-p0-13-20261104/SpeechMessage.Dynamics.Plugins.dll`。Release 全方案建置 0 警告、0 錯誤；細項與套件資產記錄於[部署失敗調查報告第 8 節](P0-13_部署失敗調查報告_供Claude複核.md)。尚未連線 D365；net462 能否註冊待使用者重驗。
+
 **不對時怎麼辦：** 回報失敗命令與錯誤碼；不產生替代金鑰、不跳過簽章檢查，也不為此提供 Six Labors 授權。
 
 ## 3. 一次規劃：trace 模式、建立時就啟用
@@ -159,6 +163,8 @@ plan 成功後，**立刻執行取得 `$plan` 與開啟 plan.md 的兩行，在
 
 **重新 plan 核對結果（2026-11-04）：** 使用者回報 UTC `2026-11-04T06:14:02.6994698+00:00`（台灣時間 14:14）的新計畫，目標仍為 goodshepherdback，只有同一骨架的 CreateAssembly／CreateType／CreateStep，秘密佔位為無；組織指紋、清單與組件雜湊均與首次一致。新 Plan SHA-256 為 `C7C599656278D8E1A6BFB06894D5EC7D16BA182427FA6BBE55C9749D4B84DDDA`，核對符合預期；使用者已執行這份新 plan 的 apply，但仍失敗（見第 4 步），不能將 plan 成功當成註冊成功。
 
+**net462 重新驗收規則（使用者 2026-11-04 核定）：** 舊計畫綁定 net48 組件雜湊，不能重用。待本機 net462 Release 打包檢查通過並由使用者取得新部署 DLL 後，重新執行第 3 步 plan，立即取得該次 `$plan` 並審核 `plan.md`。預期仍只有 CreateAssembly／CreateType／CreateStep；如果出現其他操作或新 plan 的內容與 trace 骨架不符，停止，不要 apply。
+
 **不對時怎麼辦：** 出現其他組織、步驟、push 模式或未預期更新就不套用，回報操作種類。不得手改 plan hash；清單或 DLL 改過就重新 plan。
 
 ## 4. 套用並確認 verify=0
@@ -183,7 +189,7 @@ dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -
 - 本次補修另經自癒入口分析與審查；兩次皆為 `reviewMode=dual`、`dualModelComplete=true`、`quotaBlocked=false`，沒有未處理的 Critical／High。這是本次補修的新審查，不冒稱由先前階段 0 審查涵蓋。
 - 本機證據位於 `artifacts/p013-apply-fault-tests/`；全方案各測試組的完整摘要保留於 `validation/release-test.log`。這些結果不等於 apply、verify 或 smoke 已通過。
 
-**目前先不重試：** 已取得目標框架驗證器的具體故障位置；使用者已核定先離線試做 net462，正式採用與部署尚未核定，先完成隔離候選的測試與檢查。後續若核定部署，必須重新 plan／審核。失敗時仍只回報數字行與固定失敗摘要，不貼 Exception.log 全文、追蹤全文、帳密或任何設定內容。
+**net462 變更後的下一步：** 先完成全方案 Release、測試、打包及守門，再以自癒入口執行第 10 份 ClaudeOnly 審查。審查通過並提交後，使用者依第 2～6 步重新建置、產生並審核新 plan、apply、verify=0、smoke 與停用收尾；不得重用舊 net48 plan。若新的 net462 apply 仍在目標框架驗證階段出現相同 `FormatException`／`0x80040216`，停止並回報，表示本次框架推論未解決問題；不改回 Plugin Registration Tool、不修改伺服器。所有失敗回報仍只保留安全的數字錯誤碼與固定摘要，不貼 Exception.log 全文、追蹤全文、帳密或任何設定內容。
 
 **不對時怎麼辦：** 連線、權限、簽章或沙箱錯誤就停下回報並做第 6 步，不改回手動註冊。trace 的 verify=1 或 3 都未通過，不能略過。
 
@@ -209,7 +215,7 @@ dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -
 
 | 結果 | 處理 |
 | --- | --- |
-| FrameworkFileVersion 不是 4.8.x | 依 D21 回報，由使用者決定是否評估改 4.6.2，不自行降低框架 |
+| FrameworkFileVersion 不是 4.8.x | 這是伺服器 mscorlib 執行環境欄位，不是 Plugin 目標；記錄固定資訊並回報，不據此修改已核定的 net462 目標 |
 | smoke 逾時或沒有追蹤 | 請使用者確認沙箱處理服務、非同步服務已啟動及追蹤設定 |
 | 其他內容不符或清理失敗 | 記下固定錯誤型別／代碼並回報，不貼追蹤全文或資料內容 |
 
@@ -253,7 +259,7 @@ git status --short -- .\d365\registration\plugins.registration.json
 | 第 0 步前置與準備 | 使用者已回報舊步驟／流程停用、3 筆作業取消、金鑰存在及 User Secrets 儲存；舊 token 與會友 LINE ID 核定保留，免除本次清理前置 |
 | 建置與打包檢查 | 已由使用者執行通過；Deployment 有 1 個 CS1572 警告，詳第 2 步 |
 | Plugin 追蹤設定 | 使用者已設為全部並儲存；收尾復原尚未完成 |
-| 一輪 plan／apply／verify=0 | 第二份 plan 的 apply 仍失敗：兩層 `0x80040216`；Codex 經使用者核定查得事件 18176，故障位於目標框架驗證器；伺服器元件 `9.1.49.21825`、核心檔案 `4.8.4795.0`；net462 離線試做已核定，正式採用與部署未核定，verify 未執行 |
+| 一輪 plan／apply／verify=0 | 舊 net48 組件的 apply 失敗：兩層 `0x80040216`；事件 18176 指向目標框架驗證器，根因仍待驗證。使用者已核定正式改為 net462；離線驗證完成後須重新 plan，verify 尚未執行 |
 | trace smoke 與五項人工追蹤 | 未驗證 |
 | 停用、收尾 verify=0、追蹤復原及資料清除 | 未驗證 |
 

```


## 完整來源：tools/PluginPackaging/Tests/Test-PluginAssembly.Tests.ps1
```
<#
多個樣本各自建置於本次暫存資料夾：簽章完整的 net462 樣本必須通過，net48 樣本必須被拒絕。
一次性 RSA 測試金鑰不讀正式環境變數，不輸出內容，完成或失敗均釋放並刪除整個專屬資料夾。
樣本只供 PE 解析，絕不載入執行；斷言結果內容而非只看結束碼，避免任意失敗造成假綠燈。
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixtureRoot = [IO.Path]::GetFullPath((Join-Path $tempRoot ('speechmessage-packaging-' + [Guid]::NewGuid().ToString('N'))))
$null = New-Item -ItemType Directory -Path $fixtureRoot
$utf8 = New-Object Text.UTF8Encoding($false)
$failures = 0
try {
    $keyPath = Join-Path $fixtureRoot 'fixture.snk'
    $rsa = New-Object Security.Cryptography.RSACryptoServiceProvider(2048)
    try {
        $rsa.PersistKeyInCsp = $false
        [IO.File]::WriteAllBytes($keyPath, $rsa.ExportCspBlob($true))
    } finally { $rsa.Dispose() }
    $escapedKey = [Security.SecurityElement]::Escape($keyPath)
    $project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net462</TargetFramework>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <SignAssembly>true</SignAssembly>
    <AssemblyOriginatorKeyFile>$escapedKey</AssemblyOriginatorKeyFile>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all" />
  </ItemGroup>
</Project>
"@
    $source = @'
namespace SpeechMessage.Dynamics.Plugins.Diagnostics
{
    /// <summary>Inert metadata-only fixture. No code is executed.</summary>
    public sealed class DiagnosticsPingPlugin { }
}
'@
    $projectPath = Join-Path $fixtureRoot 'Fixture.csproj'
    [IO.File]::WriteAllText($projectPath, ($project -replace "`r?`n", "`r`n") + "`r`n", $utf8)
    [IO.File]::WriteAllText((Join-Path $fixtureRoot 'Fixture.cs'), ($source -replace "`r?`n", "`r`n") + "`r`n", $utf8)
    $samples = @('signed', 'unsigned', 'wrong-framework', 'legacy-net48', 'missing-plugin', 'unexpected-reference', 'drawing', 'oversize', 'invalid-image')
    foreach ($sample in $samples) {
        $signed = $sample -ne 'unsigned'
        $sampleOutput = Join-Path $fixtureRoot $sample
        $sampleProject = $project
        $sampleSource = $source
        $failedCheck = switch ($sample) {
            'unsigned' { 'strongName' }
            'wrong-framework' { 'targetFramework' }
            'legacy-net48' { 'targetFramework' }
            'missing-plugin' { 'diagnosticsPlugin' }
            'unexpected-reference' { 'allowedReferences' }
            'drawing' { 'noDrawing' }
            'oversize' { 'size' }
            'invalid-image' { 'readable' }
        }
        if ($sample -eq 'wrong-framework') { $sampleProject = $sampleProject.Replace('<TargetFramework>net462</TargetFramework>', '<TargetFramework>net10.0</TargetFramework>') }
        if ($sample -eq 'legacy-net48') { $sampleProject = $sampleProject.Replace('<TargetFramework>net462</TargetFramework>', '<TargetFramework>net48</TargetFramework>') }
        if ($sample -eq 'missing-plugin') { $sampleSource = $sampleSource.Replace('DiagnosticsPingPlugin', 'OtherType') }
        if ($sample -eq 'unexpected-reference') {
            $sampleProject = $sampleProject.Replace('<ItemGroup>', '<ItemGroup><Reference Include="Microsoft.CSharp" />')
            $sampleSource = $sampleSource.Replace('public sealed class DiagnosticsPingPlugin { }', 'public sealed class DiagnosticsPingPlugin { /// <summary>Inert dynamic metadata dependency.</summary>' + "`r`n" + 'public static object ReadValue(dynamic value) { return value.Value; } }')
        }
        if ($sample -eq 'drawing') {
            $sampleProject = $sampleProject.Replace('<ItemGroup>', '<ItemGroup><Reference Include="System.Drawing" />')
            $sampleSource = $sampleSource.Replace('public sealed class DiagnosticsPingPlugin { }', 'public sealed class DiagnosticsPingPlugin { /// <summary>Inert forbidden GDI metadata.</summary>' + "`r`n" + 'public static System.Drawing.Color ReadColor() { return System.Drawing.Color.Red; } }')
        }
        [IO.File]::WriteAllText($projectPath, ($sampleProject -replace "`r?`n", "`r`n") + "`r`n", $utf8)
        [IO.File]::WriteAllText((Join-Path $fixtureRoot 'Fixture.cs'), ($sampleSource -replace "`r?`n", "`r`n") + "`r`n", $utf8)
        & dotnet build $projectPath -c Release -p:SignAssembly=$signed -o $sampleOutput --nologo -v quiet
        if ($LASTEXITCODE -ne 0) { throw 'Fixture build failed; this is not a functional red result.' }
        $assemblyPath = Join-Path $sampleOutput 'Fixture.dll'
        if ($sample -eq 'oversize') {
            $sizedStream = [IO.File]::OpenWrite($assemblyPath)
            try { $sizedStream.SetLength(16 * 1024 * 1024) } finally { $sizedStream.Dispose() }
        }
        if ($sample -eq 'invalid-image') { [IO.File]::WriteAllBytes($assemblyPath, [byte[]]@(1, 2, 3, 4)) }
        $checker = Join-Path $PSScriptRoot '../Test-PluginAssembly.ps1'
        $output = @(& powershell -NoProfile -File $checker -AssemblyPath $assemblyPath 2>&1)
        $code = $LASTEXITCODE
        $jsonLine = $output | Where-Object { $_.ToString().StartsWith('{', [StringComparison]::Ordinal) } | Select-Object -Last 1
        if ($null -eq $jsonLine) { throw 'Checker did not return structured evidence.' }
        $result = $jsonLine.ToString() | ConvertFrom-Json
        if ($sample -eq 'signed') {
            $passed = $code -eq 0 -and $result.ok -eq $true -and $result.checks.strongName -eq $true -and
                $result.checks.targetFramework -eq $true -and $result.checks.allowedReferences -eq $true -and
                $result.checks.noDrawing -eq $true -and $result.checks.diagnosticsPlugin -eq $true -and $result.checks.size -eq $true
        } else {
            $passed = $code -eq 1 -and $result.ok -eq $false -and $result.checks.$failedCheck -eq $false
        }
        if ($passed) { Write-Output ('PASS ' + $sample) }
        else { $failures++; Write-Output ('FAIL ' + $sample + ': contract not satisfied') }
    }
    # 多檔結果必須逐檔輸出，且任何一個未簽章都讓整體失敗；不能只驗最後一個 DLL。
    Copy-Item -LiteralPath (Join-Path $fixtureRoot 'signed/Fixture.dll') -Destination (Join-Path $fixtureRoot 'a-signed.dll')
    Copy-Item -LiteralPath (Join-Path $fixtureRoot 'unsigned/Fixture.dll') -Destination (Join-Path $fixtureRoot 'z-unsigned.dll')
    $output = @(& powershell -NoProfile -File $checker -AssemblyPath (Join-Path $fixtureRoot '*.dll') 2>&1)
    $code = $LASTEXITCODE
    $results = @($output | Where-Object { $_.ToString().StartsWith('{', [StringComparison]::Ordinal) } | ForEach-Object { $_.ToString() | ConvertFrom-Json })
    $passed = $code -eq 1 -and $results.Count -eq 2 -and @($results | Where-Object { $_.ok }).Count -eq 1
    if ($passed) { Write-Output 'PASS mixed-directory' } else { $failures++; Write-Output 'FAIL mixed-directory: every DLL must be inspected' }
} finally {
    # 先解析絕對目標並核對專屬暫存邊界，再以同一個 PowerShell 直接移除，禁止跨殼層組命令。
    $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
    if (-not $resolvedFixture.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        -not ([IO.Path]::GetFileName($resolvedFixture)).StartsWith('speechmessage-packaging-', [StringComparison]::Ordinal)) {
        throw 'Refused cleanup outside the owned fixture directory.'
    }
    Remove-Item -LiteralPath $resolvedFixture -Force -Recurse
}
Write-Output ('Plugin packaging samples: total=' + ($samples.Count + 1) + ' failed=' + $failures)
if ($failures -gt 0) { exit 1 }
exit 0

```


## 完整來源：tools/PluginPackaging/Program.cs
```
using System;
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
            var ok = strongName && targetFramework && allowed && noDrawing && plugin;
            output.WriteLine(JsonSerializer.Serialize(new
            {
                ok,
                checks = new { readable = true, strongName, targetFramework, allowedReferences = allowed, noDrawing, diagnosticsPlugin = plugin, size = true },
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
    /// 在 ILRepack 前逐一驗證輸入組件宣告了不高於 v4.6.2 的 .NET Framework 目標。
    /// 僅輸出固定 JSON，不回顯路徑；讀取仍由單一 PE reader 操作擁有並在返回時釋放。
    /// </summary>
    /// <param name="path">本次合併的單一輸入組件路徑。</param>
    /// <param name="output">呼叫端擁有的操作專屬輸出。</param>
    /// <returns>只有 PE 可讀且框架版本符合上限時回傳零。</returns>
    private static int ValidateMergeInputFramework(string path, TextWriter output)
    {
        string? frameworkName;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length >= 16 * 1024 * 1024) return Reject("SizeLimit", output);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) return Reject("InvalidImage", output);
            var metadata = pe.GetMetadataReader();
            if (!metadata.IsAssembly) return Reject("InvalidImage", output);
            frameworkName = ReadFrameworkName(metadata, metadata.GetAssemblyDefinition());
        }
        catch (Exception exception) when (exception is BadImageFormatException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            // 預期的本機 PE 輸入錯誤以固定原因拒絕，不洩漏原路徑或例外文字。
            return Reject("InvalidImage", output);
        }

        var valid = IsFrameworkAtOrBelowLimit(frameworkName);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            ok = valid,
            checks = new { targetFramework = valid },
            frameworkName,
            maximumTargetFramework = ".NETFramework,Version=v4.6.2"
        }));
        return valid ? 0 : 1;
    }

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
            checks = new { readable = false, strongName = false, targetFramework = false, allowedReferences = false, noDrawing = false, diagnosticsPlugin = false, size = false }
        }));
        return 1;
    }
}

```


## 完整來源：tools/PluginPackaging.Tests/AssemblyInspectionTests.cs
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
        /// <returns>尚未被載入的測試 DLL 路徑。</returns>
        public string Emit(string fault)
        {
            var reference = MetadataReference.CreateFromFile(Path.Combine(AppContext.BaseDirectory, "Framework", "mscorlib.dll"));
            var framework = fault == "no-framework" ? "" :
                "[assembly: System.Runtime.Versioning.TargetFramework(\"" + GetFrameworkName(fault) + "\")]";
            var source = framework + "namespace SpeechMessage.Dynamics.Plugins.Diagnostics { " +
                (fault == "private" ? "internal" : "public") + " sealed class DiagnosticsPingPlugin { } }";
            var references = new[] { reference }.AsEnumerable();
            if (fault is "reference" or "drawing")
            {
                var name = fault == "reference" ? "UnexpectedDependency" : "System.Drawing";
                var dependency = Path.Combine(Root, name + ".dll");
                var dependencyCompilation = CSharpCompilation.Create(name, new[] { CSharpSyntaxTree.ParseText("public class RequiredType { }") },
                    references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                Assert.True(dependencyCompilation.Emit(dependency).Success);
                references = references.Append(MetadataReference.CreateFromFile(dependency));
                source += "public class DependencyOwner : RequiredType { }";
            }
            var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
            if (fault != "unsigned") options = options.WithCryptoKeyFile(key).WithStrongNameProvider(new DesktopStrongNameProvider());
            var compilation = CSharpCompilation.Create("InspectionFixture", new[] { CSharpSyntaxTree.ParseText(source) }, references, options);
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


## 完整來源：SpeechMessage.Dynamics.Plugins.Tests/DiagnosticsPingPluginTests.cs
```
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using LineMessagingProcessor.Core;
using Microsoft.Xrm.Sdk;
using Moq;
using SpeechMessage.Dynamics.Plugins.Diagnostics;
using Newtonsoft.Json.Linq;
using Xunit;

namespace SpeechMessage.Dynamics.Plugins.Tests;

/// <summary>
/// 完全離線驗證外掛邊界；以 Moq 提供 SDK 服務、假 handler 注入 HTTP 狀態及原始例外。
/// 每案例獨佔 provider、追蹤集合與 HttpClient，確認不輸出機密、不吞錯、傳輸及服務參考不跨執行保留。
/// </summary>
public sealed class DiagnosticsPingPluginTests
{
    /// <summary>只在測試中使用的合成憑證，不對應任何 LINE channel。</summary>
    private const string Token = "synthetic-plugin-token";
    /// <summary>符合單人推播格式的合成收件者，不對應真實聯絡人。</summary>
    private const string UserId = "U11111111111111111111111111111111";

    /// <summary>
    /// 保護部署外殼只能以使用者依 D21 核定的 .NET Framework 4.6.2 為目標；
    /// 直接讀取測試實際載入的 Plugin 組件屬性，避免只看專案設定或測試專案本身而產生假綠燈。
    /// </summary>
    [Fact(DisplayName = "Plugin 參考的正式組件目標為 Framework 4.6.2")]
    public void Assembly_TargetFramework_IsNet462()
    {
        var attribute = typeof(DiagnosticsPingPlugin).Assembly.GetCustomAttributes(typeof(TargetFrameworkAttribute), false)
            .Cast<TargetFrameworkAttribute>().Single();

        Assert.Equal(".NETFramework,Version=v4.6.2", attribute.FrameworkName);
    }

    /// <summary>trace 模式不得送 HTTP；斷言標記、組件、執行環境、觸發事件及兩語系實際取字結果。</summary>
    [Fact(DisplayName = "追蹤模式寫入診斷與雙語文字")]
    public void Execute_TraceMode_WritesPingAndBothCultures()
    {
        using var fixture = new Fixture();
        fixture.Create("trace").Execute(fixture.Provider.Object);
        var trace = string.Join("\n", fixture.Traces);
        Assert.Contains("P0-PING OK", trace, StringComparison.Ordinal);
        Assert.Contains("SpeechMessage.Dynamics.Plugins, Version=1.0.0.0", trace, StringComparison.Ordinal);
        Assert.Contains("FrameworkFileVersion=", trace, StringComparison.Ordinal);
        Assert.Contains("EnvironmentVersion=", trace, StringComparison.Ordinal);
        Assert.Contains("連線測試成功", trace, StringComparison.Ordinal);
        Assert.Contains("［連線測試成功ǂǂǂ］", trace, StringComparison.Ordinal);
        Assert.Contains("account", trace, StringComparison.Ordinal);
        Assert.Contains("Update", trace, StringComparison.Ordinal);
        Assert.Equal(0, fixture.Handler.Calls);
    }

    /// <summary>固定 UTC 時鐘與實體 ID，斷言真實 Core HTTP 標頭及單則推播內容，不驗證替身自行設定的結果。</summary>
    [Fact(DisplayName = "推播重試鍵來自主實體識別")]
    public void Execute_PushMode_UsesPrimaryEntityIdAsRetryKey()
    {
        using var fixture = new Fixture();
        fixture.Create().Execute(fixture.Provider.Object);
        Assert.Equal(fixture.EntityId.ToString("D", CultureInfo.InvariantCulture), fixture.Handler.RetryKey);
        Assert.Contains("P0 推播測試 2026-11-03T00:00:00.0000000+00:00", fixture.Handler.Body!, StringComparison.Ordinal);
        Assert.Equal(1, fixture.Handler.Calls);
        Assert.True(fixture.Handler.ContentDisposed);
    }

    /// <summary>以合成 token／userId 送訊，追蹤只允許分類、安全狀態與請求診斷鍵，不能夾帶憑證或收件者。</summary>
    [Theory(DisplayName = "推播追蹤永不包含憑證與收件者")]
    [InlineData(200)]
    [InlineData(401)]
    [InlineData(429)]
    public void Execute_PushMode_TraceNeverContainsToken(int status)
    {
        using var fixture = new Fixture(status);
        var error = Record.Exception(() => fixture.Create().Execute(fixture.Provider.Object));
        var trace = string.Join("\n", fixture.Traces);
        Assert.DoesNotContain(Token, trace, StringComparison.Ordinal);
        Assert.DoesNotContain(UserId, trace, StringComparison.Ordinal);
        Assert.DoesNotContain("P0 推播測試", trace, StringComparison.Ordinal);
        if (error is not null) Assert.DoesNotContain(Token, error.Message, StringComparison.Ordinal);
        Assert.Contains(status.ToString(CultureInfo.InvariantCulture), trace, StringComparison.Ordinal);
    }

    /// <summary>以 429／5xx 注入可重試結果，必須追蹤分類並以 SDK 例外退出；訊息只有通用文字與事件鍵。</summary>
    [Theory(DisplayName = "可重試 HTTP 故障交系統作業處理")]
    [InlineData(429)]
    [InlineData(500)]
    public void Execute_Retryable_ThrowsInvalidPluginExecutionException(int status)
    {
        using var fixture = new Fixture(status);
        var error = Assert.Throws<InvalidPluginExecutionException>(() => fixture.Create().Execute(fixture.Provider.Object));
        Assert.Contains(fixture.EntityId.ToString("D", CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
        Assert.Null(error.InnerException);
        Assert.Contains("Retryable", string.Join("\n", fixture.Traces), StringComparison.Ordinal);
    }

    /// <summary>以 401 注入永久失敗，外掛只追蹤分類且不拋出，避免非同步作業盲目重試。</summary>
    [Fact(DisplayName = "永久失敗只追蹤不拋出")]
    public void Execute_Permanent_DoesNotThrow()
    {
        using var fixture = new Fixture(401);
        fixture.Create().Execute(fixture.Provider.Object);
        Assert.Contains("Permanent", string.Join("\n", fixture.Traces), StringComparison.Ordinal);
        Assert.Equal(1, fixture.Handler.Calls);
    }

    /// <summary>以重入深度二注入遞迴事件，斷言沒有追蹤與 HTTP，且不讀取實體內容。</summary>
    [Fact(DisplayName = "深度大於一直接返回")]
    public void Execute_DepthGreaterThanOne_DoesNothing()
    {
        using var fixture = new Fixture();
        fixture.Context.SetupGet(value => value.Depth).Returns(2);
        fixture.Create().Execute(fixture.Provider.Object);
        Assert.Empty(fixture.Traces);
        Assert.Equal(0, fixture.Handler.Calls);
        fixture.Context.VerifyGet(value => value.PrimaryEntityId, Times.Never);
    }

    /// <summary>注入損毀 JSON／無效模式語系；拒絕訊息必須為繁中固定文字，不能回顯輸入或 JSON 解析例外。</summary>
    [Theory(DisplayName = "不合法註冊設定以安全繁中訊息拒絕")]
    [InlineData("{synthetic-secret")]
    [InlineData("null")]
    [InlineData("{\"mode\":\"invalid\",\"culture\":\"zh-TW\"}")]
    [InlineData("{\"mode\":\"trace\",\"culture\":\"invalid\"}")]
    public void Ctor_InvalidUnsecureJson_ThrowsWithChineseMessage(string config)
    {
        var error = Assert.Throws<InvalidPluginExecutionException>(() => new DiagnosticsPingPlugin(config, Token));
        Assert.Contains("設定", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic", error.Message, StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }

    /// <summary>Core 的無回應故障保留原始 HttpRequestException；外掛必須以同一實例作 InnerException，不輸出訊息。</summary>
    [Fact(DisplayName = "可重試傳輸例外保留原始內層例外")]
    public void Execute_Retryable_PreservesTransportExceptionAsInner()
    {
        using var fixture = new Fixture();
        var original = new HttpRequestException("synthetic-sensitive-network-detail");
        fixture.Handler.Reply = (_, _) => throw original;
        var error = Assert.Throws<InvalidPluginExecutionException>(() => fixture.Create().Execute(fixture.Provider.Object));
        Assert.Same(original, error.InnerException);
        var trace = string.Join("\n", fixture.Traces);
        Assert.Contains(typeof(HttpRequestException).FullName!, trace, StringComparison.Ordinal);
        Assert.DoesNotContain(original.Message, trace + error.Message, StringComparison.Ordinal);
    }

    /// <summary>非預期程式故障先追蹤型別再以 throw 重拋；同一例外必須離開外掛，訊息不可進追蹤。</summary>
    [Fact(DisplayName = "非預期例外追蹤後保持原始實例重拋")]
    public void Execute_UnexpectedException_TracesAndRethrows()
    {
        using var fixture = new Fixture();
        var original = new InvalidOperationException("synthetic-sensitive-programming-detail");
        fixture.Handler.Reply = (_, _) => throw original;
        Assert.Same(original, Assert.Throws<InvalidOperationException>(() => fixture.Create().Execute(fixture.Provider.Object)));
        var trace = string.Join("\n", fixture.Traces);
        Assert.Contains(typeof(InvalidOperationException).FullName!, trace, StringComparison.Ordinal);
        Assert.DoesNotContain(original.Message, trace, StringComparison.Ordinal);
    }

    /// <summary>注入重複鍵、錯誤型別、過長設定與非法收件者，固定拒絕且不保留敏感輸入作內層例外。</summary>
    /// <param name="fault">固定故障選擇，不接受外部設定。</param>
    [Theory(DisplayName = "診斷設定邊界拒絕不合法值")]
    [InlineData("empty")]
    [InlineData("long")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("wrong-type")]
    [InlineData("user")]
    [InlineData("token")]
    public void Ctor_ConfigurationBoundary_FailsClosed(string fault)
    {
        var config = "{\"mode\":\"push\",\"culture\":\"zh-TW\",\"lineUserId\":\"" + UserId + "\"}";
        config = fault switch
        {
            "empty" => "",
            "long" => new string('x', 4097),
            "duplicate" => "{\"mode\":\"push\",\"mode\":\"trace\",\"culture\":\"zh-TW\"}",
            "unknown" => "{\"mode\":\"trace\",\"culture\":\"zh-TW\",\"other\":\"synthetic-sensitive\"}",
            "wrong-type" => "{\"mode\":2,\"culture\":\"zh-TW\"}",
            "user" => config.Replace(UserId, "synthetic-sensitive-user"),
            _ => config
        };
        var error = Assert.Throws<InvalidPluginExecutionException>(() => new DiagnosticsPingPlugin(config, fault == "token" ? "" : Token));
        Assert.Equal("診斷外掛設定格式錯誤。", error.Message);
        Assert.Null(error.InnerException);
    }

    /// <summary>
    /// 同一無狀態 HTTP client 供 A／B 外掛重用，各自執行二十次；真正 Core 必須每次取到正確憑證、收件者及事件鍵。
    /// 兩路並行且每次更換 provider，所有回應內容在 Execute 返回前釋放，停止後活躍數回到零。
    /// </summary>
    [Fact(DisplayName = "重用外掛與共用傳輸維持步驟隔離並排空資源")]
    public async Task Execute_ConcurrentRegistrationReuse_IsolatedAndDrained()
    {
        using var handler = new IsolationHandler();
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var client = new LinePushClient(http);
        var pluginA = new DiagnosticsPingPlugin("{\"mode\":\"push\",\"culture\":\"zh-TW\",\"lineUserId\":\"" + UserId + "\"}", Token,
            client, () => new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero));
        var pluginB = new DiagnosticsPingPlugin("{\"mode\":\"push\",\"culture\":\"qps-ploc\",\"lineUserId\":\"U22222222222222222222222222222222\"}",
            "synthetic-plugin-token-B", client, () => new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero));
        await Task.WhenAll(Task.Run(() => Run(pluginA, "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")),
            Task.Run(() => Run(pluginB, "bbbbbbbb-bbbb-cccc-dddd-eeeeeeeeeeee")));
        Assert.Equal(40, handler.Calls);
        Assert.Equal(0, handler.Active);
        Assert.Equal(0, handler.Mismatches);

        static void Run(DiagnosticsPingPlugin plugin, string id)
        {
            for (var index = 0; index < 20; index++)
            {
                using var fixture = new Fixture();
                fixture.Context.SetupGet(value => value.PrimaryEntityId).Returns(Guid.Parse(id));
                plugin.Execute(fixture.Provider.Object);
                Assert.DoesNotContain("synthetic-plugin-token", string.Join("\n", fixture.Traces), StringComparison.Ordinal);
            }
        }
    }

    /// <summary>會丟棄 Post 的同步內容代表不能回到原執行緒；外掛須在有界等待內完成且釋放回應。</summary>
    [Fact(DisplayName = "同步沙箱內容不會攔住非同步推播完成")]
    public void Execute_SynchronizationContext_CompletesAndDisposes()
    {
        using var fixture = new Fixture();
        using var content = new TrackedContent();
        fixture.Handler.Reply = async (_, _) =>
        {
            await Task.Yield();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        };
        var previous = SynchronizationContext.Current;
        var context = new RejectingContext();
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            fixture.Create().Execute(fixture.Provider.Object);
            Assert.Equal(1, fixture.Handler.Calls);
            Assert.Equal(0, context.Posts);
            Assert.True(content.Disposed);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    /// <summary>被驗證的沙箱同步內容，任何回到呼叫執行緒的排程都直接拒絕，不建立背景執行緒。</summary>
    private sealed class RejectingContext : SynchronizationContext
    {
        /// <summary>排程次數應維持零，避免靠等待逾時判斷死結。</summary>
        public int Posts { get; private set; }
        /// <summary>故障注入不執行 callback，若產品捕獲此外部內容便立刻失敗。</summary>
        /// <param name="d">不應被捕獲的回呼。</param>
        /// <param name="state">不應滯留的呼叫狀態。</param>
        public override void Post(SendOrPostCallback d, object? state)
        {
            Posts++;
            throw new InvalidOperationException("Sandbox synchronization context was captured.");
        }
    }

    /// <summary>共用連線池的無狀態替身；只保留有界計數，不保存 request、憑證或訊息原文。</summary>
    private sealed class IsolationHandler : HttpMessageHandler
    {
        /// <summary>以原子操作計算傳輸次數。</summary>
        private int calls;
        /// <summary>已建立但尚未 Dispose 的回應內容數量。</summary>
        private int active;
        /// <summary>隔離契約失敗次數，不保存失敗原文。</summary>
        private int mismatches;
        /// <summary>停止後的總操作數。</summary>
        public int Calls => calls;
        /// <summary>停止後必須為零的資源數。</summary>
        public int Active => active;
        /// <summary>停止後必須為零的配對錯誤數。</summary>
        public int Mismatches => mismatches;
        /// <summary>以真實 Core 組出的標頭及 JSON 檢查 A／B 配對，每次建立獨立的回應內容。</summary>
        /// <param name="request">只在這次 await 期間存活，不存入欄位。</param>
        /// <param name="cancellationToken">由 Core 擁有的十秒取消鏈。</param>
        /// <returns>交回 Core using owner 的獨立回應。</returns>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            var body = JObject.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false));
            var tokenA = string.Equals(request.Headers.Authorization!.Parameter, Token, StringComparison.Ordinal);
            var expectedId = tokenA ? UserId : "U22222222222222222222222222222222";
            var expectedKey = tokenA ? "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee" : "bbbbbbbb-bbbb-cccc-dddd-eeeeeeeeeeee";
            if (!string.Equals((string?)body["to"], expectedId, StringComparison.Ordinal) ||
                !string.Equals(request.Headers.GetValues("X-Line-Retry-Key").Single(), expectedKey, StringComparison.Ordinal))
                Interlocked.Increment(ref mismatches);
            Interlocked.Increment(ref active);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new DrainContent(() => Interlocked.Decrement(ref active))
            };
        }
    }

    /// <summary>由 Core 唯一擁有的回應內容；Dispose 只回報一次並解除回呼，不能滯留測試 owner。</summary>
    private sealed class DrainContent : StringContent
    {
        /// <summary>最多存活至這次回應 Dispose 的資源釋放回呼。</summary>
        private Action? release;
        /// <summary>建立單次回應資源計數。</summary>
        /// <param name="release">測試 owner 的有界計數回呼。</param>
        public DrainContent(Action release) : base("{}") => this.release = release;
        /// <summary>原子解除回呼避免重複扣數，再交基底釋放串流。</summary>
        /// <param name="disposing">是否由 owner 明確釋放。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing) Interlocked.Exchange(ref release, null)?.Invoke();
            base.Dispose(disposing);
        }
    }

    /// <summary>單案例的 SDK／HTTP owner；不建立真實組織服務或任何網路 fallback。</summary>
    private sealed class Fixture : IDisposable
    {
        /// <summary>固定合成業務事件鍵，每個 fixture 各自持有且不來自真實 D365。</summary>
        public Guid EntityId { get; } = new("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        /// <summary>追蹤資料僅限此案例；Dispose 清空，不跨測試共享。</summary>
        public List<string> Traces { get; } = new();
        /// <summary>Moq SDK 服務提供者，只提供本案例的執行內容與追蹤服務。</summary>
        public Mock<IServiceProvider> Provider { get; } = new(MockBehavior.Strict);
        /// <summary>合成執行內容，讓深度與 RetryKey 契約可被決定性斷言。</summary>
        public Mock<IPluginExecutionContext> Context { get; } = new(MockBehavior.Strict);
        /// <summary>可注入故障的離線傳輸，所有回應資源由 Core 釋放。</summary>
        public Handler Handler { get; }
        /// <summary>此 fixture 的唯一連線池 owner，結束時 Dispose。</summary>
        private readonly HttpClient http;
        /// <summary>建立最小 SDK 情境；追蹤格式使用明確 invariant culture，不依賴主機文化。</summary>
        public Fixture(int status = 200)
        {
            Context.SetupGet(value => value.Depth).Returns(1);
            Context.SetupGet(value => value.PrimaryEntityId).Returns(EntityId);
            Context.SetupGet(value => value.PrimaryEntityName).Returns("account");
            Context.SetupGet(value => value.MessageName).Returns("Update");
            var tracing = new Mock<ITracingService>(MockBehavior.Strict);
            tracing.Setup(value => value.Trace(It.IsAny<string>(), It.IsAny<object[]>()))
                .Callback<string, object[]>((format, args) => Traces.Add(string.Format(CultureInfo.InvariantCulture, format, args)));
            Provider.Setup(value => value.GetService(typeof(ITracingService))).Returns(tracing.Object);
            Provider.Setup(value => value.GetService(typeof(IPluginExecutionContext))).Returns(Context.Object);
            Handler = new Handler(status);
            http = new HttpClient(Handler) { Timeout = Timeout.InfiniteTimeSpan };
        }
        /// <summary>使用真實 Core 配合假 handler；時間固定，不連線或等待系統時間。</summary>
        /// <param name="mode">trace 或 push，依案例選定。</param>
        /// <returns>只在此案例存活的外掛實例。</returns>
        public DiagnosticsPingPlugin Create(string mode = "push") =>
            new("{\"mode\":\"" + mode + "\",\"culture\":\"zh-TW\",\"lineUserId\":\"" + UserId + "\"}", Token,
                new LinePushClient(http), () => new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero));
        /// <summary>由單一 owner 關閉 HttpClient／handler 並清除合成追蹤資料，沒有 fire-and-forget。</summary>
        public void Dispose() { http.Dispose(); Traces.Clear(); }
    }

    /// <summary>擷取單次 HTTP 的合成資料與內容生命週期；不得落到預設網路 handler。</summary>
    private sealed class Handler : HttpMessageHandler
    {
        /// <summary>故障注入委派只存活於此測試 fixture。</summary>
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Reply { get; set; }
        /// <summary>送出次數，保護深度守門及沒有內部重試。</summary>
        public int Calls { get; private set; }
        /// <summary>當次事件鍵快照，不保留完整 request。</summary>
        public string? RetryKey { get; private set; }
        /// <summary>合成訊息快照，Dispose 清除，不寫入證據。</summary>
        public string? Body { get; private set; }
        /// <summary>追蹤回應是否由 Core 明確釋放，與 GC 無關。</summary>
        public bool ContentDisposed => content?.Disposed == true;
        /// <summary>僅持有合成回應內容供決定性釋放斷言。</summary>
        private readonly TrackedContent content = new();
        /// <summary>配置固定狀態回應，不提供真實端點或驗證機密。</summary>
        public Handler(int status) => Reply = (_, _) =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)status) { Content = content };
            response.Headers.Add("x-line-request-id", "11111111-2222-3333-4444-555555555555");
            return Task.FromResult(response);
        };
        /// <summary>先複製必要合成資料再交給故障注入，回應由 Core 的 using owner 接管。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            RetryKey = string.Join("", request.Headers.GetValues("X-Line-Retry-Key"));
            Body = await request.Content!.ReadAsStringAsync().ConfigureAwait(false);
            return await Reply(request, cancellationToken).ConfigureAwait(false);
        }
        /// <summary>測試 owner 結束時釋放未送出的內容，並丟棄合成快照。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing) { content.Dispose(); Body = RetryKey = null; }
            base.Dispose(disposing);
        }
    }

    /// <summary>單次回應內容的明確 Dispose 證據，不以程序結束代替清理。</summary>
    private sealed class TrackedContent : StringContent
    {
        /// <summary>內容是否已被擁有者確定性釋放。</summary>
        public bool Disposed { get; private set; }
        /// <summary>固定合成內容不含任何真實資料。</summary>
        public TrackedContent() : base("{}") { }
        /// <summary>釋放前留下布林計數，再交基底清理串流。</summary>
        protected override void Dispose(bool disposing) { if (disposing) Disposed = true; base.Dispose(disposing); }
    }
}

```


## 完整來源：Directory.Build.targets
```
<Project>
  <!-- 嚴格規則只套用到明確啟用的新專案；舊專案同時開啟會引入大量既有警告。
       SDK 在 csproj 本體之前讀取 Directory.Build.props，因此必須在 targets 才判斷
       SpeechMessageStrictCode，不能靠命令列覆寫讓測試假通過。 -->
  <PropertyGroup Condition="'$(SpeechMessageStrictCode)' == 'true'">
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <AnalysisMode>Recommended</AnalysisMode>
    <!-- net48 預設只稽核直接相依；嚴格專案須在還原時稽核全部相依，與 net10 保持一致。 -->
    <NuGetAuditMode>all</NuGetAuditMode>
    <!-- SDK 只為 .NETCoreApp 自動選分析層級；net48 空值會讓 Recommended 規則集不載入。
         使用者 2026-11-03 核定：嚴格模式只在空值時明確採 latest，使雙目標確實同規則；
         net10 原有層級不變，舊專案不啟用嚴格模式也不受影響。 -->
    <AnalysisLevel Condition="'$(AnalysisLevel)' == ''">latest</AnalysisLevel>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <WarningsAsErrors>$(WarningsAsErrors);CA1307;CA1309;CA1310;RS0030</WarningsAsErrors>
  </PropertyGroup>

  <!-- 使用者 2026-11-03 核定的永久規則，適用本計畫所有階段：
       只有嚴格模式、明確測試標記、專案名稱以 .Tests 結尾三者同時成立，
       才允許計畫要求的底線測試命名；僅排除 CA1707，其他分析器與警告視為錯誤不變。
       必須集中在此設定，不得在個別測試專案重複加入其他 NoWarn。 -->
  <PropertyGroup Condition="'$(SpeechMessageStrictCode)' == 'true' And '$(IsTestProject)' == 'true' And $([System.String]::Copy('$(MSBuildProjectName)').EndsWith('.Tests', System.StringComparison.Ordinal))">
    <NoWarn>$(NoWarn);CA1707</NoWarn>
  </PropertyGroup>

  <!-- SDK 在匯入本檔之前已依文件開關決定 DocumentationFile 與輸出複製行為。
       因此 GenerateDocumentationFile 必須在 csproj 本體與嚴格模式一同啟用，
       不能在此補設開關或改寫 SDK 私有屬性；編譯前拒絕空的實際文件路徑，避免假綠燈。 -->
  <Target Name="ValidateSpeechMessageDocumentation" BeforeTargets="CoreCompile" Condition="'$(SpeechMessageStrictCode)' == 'true'">
    <Error Condition="'$(DocumentationFile)' == ''" Code="SMQ0001" Text="嚴格專案必須在 csproj 本體同時設定 SpeechMessageStrictCode=true 與 GenerateDocumentationFile=true；目前 DocumentationFile 為空，無法驗證 XML 文件。" />
  </Target>

  <ItemGroup Condition="'$(SpeechMessageStrictCode)' == 'true'">
    <PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" PrivateAssets="all" />
    <AdditionalFiles Include="$(MSBuildThisFileDirectory)BannedSymbols.txt" />
    <!-- WarningsAsErrors 不會自行啟用預設停用的規則，另以專用全域設定確保分析器真的執行。 -->
    <GlobalAnalyzerConfigFiles Include="$(MSBuildThisFileDirectory)build/StrictCode.globalconfig" />
  </ItemGroup>

  <!-- Plugin 與 Workflow 都在 D365 Sandbox 執行，額外拒絕 GDI；一般主機不受此限制。
       分析器依 BannedSymbols.txt 檔名辨識清單，故 Plugin 清單保留同名並置於獨立目錄。 -->
  <ItemGroup Condition="'$(SpeechMessageStrictCode)' == 'true' And ('$(MSBuildProjectName)' == 'SpeechMessage.Dynamics.Plugins' Or '$(MSBuildProjectName)' == 'SpeechMessage.Dynamics.Workflows')">
    <AdditionalFiles Include="$(MSBuildThisFileDirectory)build/Plugin/BannedSymbols.txt" />
  </ItemGroup>

  <!-- 補丁只補 .NET Framework 缺少的編譯器中繼資料，不在 net10 重複宣告框架型別。
       使用連結檔確保所有雙目標程式庫共用一份定義，不建立額外狀態或組件相依。 -->
  <ItemGroup Condition="'$(SpeechMessageStrictCode)' == 'true' And ('$(TargetFramework)' == 'net48' Or '$(TargetFramework)' == 'net462')">
    <Compile Include="$(MSBuildThisFileDirectory)src-shared/Polyfills/IsExternalInit.cs" Link="Polyfills/IsExternalInit.cs" />
    <Compile Include="$(MSBuildThisFileDirectory)src-shared/Polyfills/NotNullWhenAttribute.cs" Link="Polyfills/NotNullWhenAttribute.cs" />
  </ItemGroup>
</Project>

```


## 完整來源：build/PluginMerge.targets
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


## 完整來源：LineMessagingProcessor.Core/LinePushClient.cs
```
using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace LineMessagingProcessor.Core;

/// <summary>
/// 固定 LINE push 端點的單次傳輸實作，沒有重試佇列、背景工作或管理者 notifier。
/// .NET Framework 的程序級共用 HttpClient 不含使用者狀態，net10 的 HttpClient 由主機擁有；
/// 每個操作獨佔 HTTP 訊息、十秒逾時與取消連結，結束前按 using 次序釋放。
/// </summary>
public sealed class LinePushClient : ILinePushClient
{
    /// <summary>外部或沙箱程序擁有的無狀態連線池，不保存任何當次憑證或結果。</summary>
    private readonly HttpClient _httpClient;
    /// <summary>建立操作專屬逾時來源；委派本身不捕獲使用者、教會或 request 狀態。</summary>
    private readonly Func<TimeSpan, CancellationTokenSource> _createTimeout;
    /// <summary>固定十秒的 Core 內部期限，不讓未驗證輸入延長資源生命週期。</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

#if NETFRAMEWORK
    /// <summary>
    /// 沙箱 AppDomain／程序是此唯一客戶端與連線池的 owner，卸載時一併回收。
    /// 它不使用 Cookie、預設認證或自動轉址，沒有 per-user 狀態；每個傳輸取消與內容另有 using owner。
    /// </summary>
    private static readonly HttpClient SharedClient = CreateSharedClient();

    /// <summary>使用沙箱程序生命週期內的無狀態客戶端，不為每次 Plugin 執行建立連線池。</summary>
    public LinePushClient() : this(SharedClient) { }
#endif

    /// <summary>連線由呼叫端擁有；必須使用無 Cookie、無預設憑證且不自動轉址的管線。</summary>
    /// <param name="httpClient">可重用且無教會／使用者狀態的客戶端；Core 不 Dispose 它。</param>
#if NETFRAMEWORK
    internal LinePushClient(HttpClient httpClient) : this(httpClient, StartTimeout) { }
#else
    public LinePushClient(HttpClient httpClient) : this(httpClient, StartTimeout) { }
#endif

    /// <summary>統一雙目標的計時來源所有權；來源必須逐次建立，回傳後由單一操作 Dispose。</summary>
    /// <param name="httpClient">外部擁有的無狀態客戶端。</param>
    /// <param name="createTimeout">依給定期限建立獨立來源的排程器，不得回傳共用或快取來源。</param>
    internal LinePushClient(HttpClient httpClient, Func<TimeSpan, CancellationTokenSource> createTimeout)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _createTimeout = createTimeout ?? throw new ArgumentNullException(nameof(createTimeout));
    }

    /// <summary>以逐請求標頭與快照內容推播；只轉換已核定的傳輸失敗，其他例外往外拋。</summary>
    /// <param name="credential">當次頻道憑證。</param>
    /// <param name="request">當次不可變請求。</param>
    /// <param name="ct">明確 caller 取消；只在尚未收到 HTTP 回應時生效，回應抵達後以狀態碼分類結果。</param>
    /// <returns>不持有 request／response／credential 的結果。</returns>
    public async Task<LinePushResult> PushAsync(LineChannelCredential credential, LinePushRequest request, CancellationToken ct)
    {
#if NETFRAMEWORK
        if (credential is null) throw new ArgumentNullException(nameof(credential));
        if (request is null) throw new ArgumentNullException(nameof(request));
#else
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(request);
#endif
        ct.ThrowIfCancellationRequested();

        using var timeout = _createTimeout(RequestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.line.me/v2/bot/message/push");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        message.Headers.Add("X-Line-Retry-Key", request.RetryKey.ToString("D", CultureInfo.InvariantCulture));
        var body = JsonConvert.SerializeObject(new
        {
            to = request.UserId,
            messages = request.Messages.Select(text => new { type = "text", text }).ToArray()
        });
        message.Content = new StringContent(body, Encoding.UTF8, "application/json");

        try
        {
            // 只需要標頭；不緩衝、不讀取也不保留錯誤本文，回傳前釋放回應及內容串流。
            using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, linked.Token)
                .ConfigureAwait(false);
            var status = (int)response.StatusCode;
            var outcome = status switch
            {
                >= 200 and <= 299 => LinePushOutcome.Success,
                409 => LinePushOutcome.AlreadyAccepted,
                429 or (>= 500 and <= 599) => LinePushOutcome.Retryable,
                _ => LinePushOutcome.Permanent
            };
            var requestId = response.Headers.TryGetValues("x-line-request-id", out var values) ? values.FirstOrDefault() : null;
            return new LinePushResult(outcome, response.StatusCode, requestId, null);
        }
        catch (OperationCanceledException exception)
        {
            // caller 取消只在沒有收到 HTTP 回應時中止操作；其他取消來源都是無回應的傳輸失敗。
            // 一旦 SendAsync 已回傳 response，狀態碼結果優先，避免丟失服務端已知的接受／拒絕資訊。
            ct.ThrowIfCancellationRequested();
            return new LinePushResult(LinePushOutcome.Retryable, null, null, exception);
        }
        catch (HttpRequestException exception)
        {
            // 本層沒有收到 HTTP 回應，原始實例交給最終邊界；不能在可恢復重試階段重複告警。
            // .NET Framework 不會像 net10 自動把同時發生的 caller 取消正規化，因此在此明確優先辨識。
            ct.ThrowIfCancellationRequested();
            return new LinePushResult(LinePushOutcome.Retryable, null, null, exception);
        }
    }

    /// <summary>跨框架共用的正式排程器，建立具固定期限的獨立來源，由呼叫的 using 確定性釋放。</summary>
    /// <param name="duration">Core 固定的十秒期限。</param>
    /// <returns>尚未與其他操作共用的新來源。</returns>
    private static CancellationTokenSource StartTimeout(TimeSpan duration) => new(duration);

#if NETFRAMEWORK
    /// <summary>
    /// 只在 AppDomain 建立一次無狀態連線池，不覆寫程序級 TLS 設定。
    /// TLS 仍受沙箱 .NET Framework 目標相容開關與作業系統設定影響；net462 的 LINE 推播須在 P2-04 前置另行驗收。
    /// </summary>
    /// <returns>由程序擁有、沒有 Cookie 或使用者憑證的客戶端。</returns>
    private static HttpClient CreateSharedClient()
    {
        var handler = new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false, UseDefaultCredentials = false };
        return new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }
#endif
}

```


## 完整來源：LineMessagingProcessor.Core/LinePushRequest.cs
```
using System;
using System.Collections.Generic;

namespace LineMessagingProcessor.Core;

/// <summary>
/// 僅容許一名收件者與最多五則 text 的操作快照，不能表示群播或廣播。
/// 建構時複製並驗證全部訊息再發布，來源集合後續修改不得影響已建立請求。
/// </summary>
public sealed class LinePushRequest
{
    /// <summary>在任何 HTTP 操作之前完成單人 ID、訊息界限與事件重試鍵驗證。</summary>
    /// <param name="userId">U 加三十二個小寫十六進位字元，須完整符合、不能附加換行。</param>
    /// <param name="messages">一到五則非空文字，每則最多五千個 UTF-16 碼元。</param>
    /// <param name="retryKey">同一業務事件重試必須沿用的非空識別碼。</param>
    public LinePushRequest(string userId, IReadOnlyList<string> messages, Guid retryKey)
    {
        if (userId is null || userId.Length != 33 || userId[0] != 'U')
            throw new ArgumentException("A valid single-recipient identifier is required.", nameof(userId));
        for (var index = 1; index < userId.Length; index++)
        {
            var character = userId[index];
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                throw new ArgumentException("A valid single-recipient identifier is required.", nameof(userId));
        }
#if NETFRAMEWORK
        if (messages is null) throw new ArgumentNullException(nameof(messages));
#else
        ArgumentNullException.ThrowIfNull(messages);
#endif
        var count = messages.Count;
        if (count is < 1 or > 5)
            throw new ArgumentException("One through five text messages are required.", nameof(messages));
        if (retryKey == Guid.Empty)
            throw new ArgumentException("A nonempty event retry key is required.", nameof(retryKey));

        // 先完成有界候選再發布；不得保存 caller 清單，或逐步暴露尚未驗證完的內容。
        var snapshot = new string[count];
        for (var index = 0; index < count; index++)
        {
            var text = messages[index];
            if (string.IsNullOrEmpty(text) || text.Length > 5000)
                throw new ArgumentException("Each text must contain one through 5000 UTF-16 code units.", nameof(messages));
            snapshot[index] = text;
        }
        UserId = userId;
        RetryKey = retryKey;
        Messages = Array.AsReadOnly(snapshot);
    }

    /// <summary>伺服器已驗證的單一收件 ID，僅用於當次 HTTP 內容，不可當成授權依據。</summary>
    public string UserId { get; }

    /// <summary>呼叫專屬且不可變的完整訊息集合，不把外部可變清單直接發布。</summary>
    public IReadOnlyList<string> Messages { get; }

    /// <summary>由業務事件衍生的重試鍵；Core 不重新產生或在失敗時替換。</summary>
    public Guid RetryKey { get; }

    /// <summary>診斷只能顯示型別及遮罩，不帶 userId 或訊息內容。</summary>
    /// <returns>固定遮罩。</returns>
    public override string ToString() => "LinePushRequest(***)";
}

```


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.

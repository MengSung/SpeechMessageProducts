ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: integration-p0-stage0-resume-05a

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P0.Worktree

## Request
# 階段0 第5批 第1/2份：SpeechMessage.Dynamics.Deployment

你是唯讀外部審查者。本輪為「單模型（使用者指定 ClaudeOnly）」，不是雙模型或配額降級。只審查，不改檔、不提交、不執行產品／工具／測試，不建立任何網路或 D365／LINE 連線。
審查重點：跨使用者／跨教會隔離、資源釋放、機密外洩、例外責任（設計10.5／10.9）、net48/net10行為一致、守門假綠燈。所有真實連線驗收仍由使用者負責。
機密欄位已遮罩；遮罩值不可用來推斷實際設定正確性。不得讀取 appsettings*、User Secrets、環境變數、Logs、金鑰／授權檔，或以 git show 取未遮罩版本。只以所附安全差異審查；如需上下文，可只查明確的非機密程式來源。不要開啟 .ccg/dual-model-runs 的其他歷史輸出或 research 原始紀錄。
輸出只列可驗證的 Critical／High／Medium／Low，包含檔案、行號、觸發條件、具體影響及最小修法；測試不足須說明可重現的缺口，不把純猜測當發現。沒有問題請明寫。不得聲稱實跑測試。
已核定例外：嚴格 .Tests 測試專案僅 CA1707 放寬；未實作 API 的 CA1716 可改語意近名；Core 不通知，Plugin 僅追蹤，最終告警在 net10 重試用盡／階段5；正式金鑰只程序環境且不讀內容；本階段不存在業務 Rejected。小組頁面由使用者確認恢復，圖表未逐項驗證，之前審查兩次為逾時。

範圍：73b6e6cf409daa7559fd3c11d482e3efc542c0d7..ae29cb40aa5c395f83c169068d7ca40e7094c509
安全差異 SHA256：1277646b09a38d8075dc1de02fc766fb5a8677cba0829986939377cbbc78bb20
同批其他分份須一併審查後才算完成，本份不可代表全批。
專項：User Secrets-only credentials、CLI 不收密碼、每個指令獨立連線 Dispose、不經共用池；組織白名單、乾跑／apply 明確邊界、冪等計畫與執行順序、部分故障回滾、寫入鎖、DLL 元資料檢查；輸出不得包含原始連線例外訊息。僅做靜態審查，不連線。

```diff
diff --git a/.trellis/spec/backend/deployment-contract.md b/.trellis/spec/backend/deployment-contract.md
new file mode 100644
index 00000000..d4f994f8
--- /dev/null
+++ b/.trellis/spec/backend/deployment-contract.md
@@ -0,0 +1,45 @@
+# Deployment CLI Contract
+
+## Scope and connection boundary
+
+P0-12 is an offline implementation and test package. No D365 or LINE connection
+is permitted until the user reports goodshepherdback cleanup complete. Afterwards,
+Codex may only run the read-only commands authorized by D24; apply, rollback and
+smoke remain user operations. Credentials come only from the Deployment User
+Secrets provider; Windows Credential Manager is a future option. Do not modify
+ToolUtility or accept passwords on the command line.
+
+The connector validates the sole allowed HTTPS host and Organization.svc path,
+then uses CrmConnectionService.CreateOnPremiseClient directly, without a shared
+pool. One command owns one OrganizationLease. Dispose releases the client and
+clears retained references, including on failure or caller cancellation.
+
+## Metadata, output and mutation
+
+The registration manifest contains templates only. Plan and Markdown never
+resolve secrets. Export contains managed assembly content and metadata summaries;
+settings use presence bits or hashes. Never export trace text, token or userId.
+Report real exceptions through the shared diagnostic owner, which writes only
+safe type/stack metadata; never print raw exception messages or CLI values.
+
+Plan hashes cover manifest, DLL and current organization fingerprint. Apply
+revalidates these, exports and flushes the current state before writing. Stable
+step identities use type/message/entity/stage/mode/rank, and duplicate database
+IDs fail closed. Existing step configuration changes update the original ID;
+trace/push transitions retain idempotency, parse secrets only for writes, and
+remove obsolete secure configuration records. Plan does not compare secret
+values and therefore is not a credential-rotation tool.
+
+A bounded OS file-handle lease serializes same-organization write commands on
+this machine; release it after the connection is disposed. Smoke only creates
+its own marked task, polls its correlated async/trace metadata for up to three
+minutes and deletes the task in finally. Caller cancellation is not notified.
+
+## Offline verification
+
+Keep all original 17 test methods. L1 uses operation-local in-memory SDK services;
+L2 emits an unsigned net48 PE into the test's unique temporary folder and reads
+metadata without Assembly.Load. Dispose readers, timers, locks and temp fixtures.
+CI includes the Deployment project and a separate >=80% coverage report.
+Actual WCF/WS-Trust connectivity and D365 sandbox behavior remain P0-13 user
+acceptance, not a claim made by the offline suite.

diff --git a/SpeechMessage.Dynamics.Deployment.Tests/DeploymentContractTests.cs b/SpeechMessage.Dynamics.Deployment.Tests/DeploymentContractTests.cs
new file mode 100644
index 00000000..89f3eb2e
--- /dev/null
+++ b/SpeechMessage.Dynamics.Deployment.Tests/DeploymentContractTests.cs
@@ -0,0 +1,1056 @@
+using System;
+using System.Collections.Generic;
+using System.Globalization;
+using System.IO;
+using System.Linq;
+using System.Reflection;
+using System.Security.Cryptography;
+using System.Threading;
+using System.Threading.Tasks;
+using Microsoft.CodeAnalysis;
+using Microsoft.CodeAnalysis.CSharp;
+using Microsoft.Extensions.Configuration;
+using Microsoft.Xrm.Sdk;
+using Microsoft.Xrm.Sdk.Query;
+using ToolUtilityNameSpace.Diagnostics;
+using Xunit;
+
+namespace SpeechMessage.Dynamics.Deployment.Tests;
+
+/// <summary>
+/// 用記憶體組織及獨立暫存檔保護 P0-12 的十七項原始契約。
+/// 測試刻意注入版本／token 衝突、組織變動、竄改、連線機密與 smoke 失敗；
+/// 斷言實際 SDK 寫入、備份順序及 finally 清理，不呼叫 D365、LINE 或正式金鑰。
+/// </summary>
+public sealed class DeploymentContractTests
+{
+    /// <summary>保留 plan 操作的固定順序，避免每個案例重複配置相同期待陣列。</summary>
+    private static readonly string[] ExpectedPlanOperations = { "CreateAssembly", "CreateType", "CreateStep" };
+
+    /// <summary>只供 CLI 拒絕案例使用的合成密碼參數範本；呼叫時複製以維持案例隔離。</summary>
+    private static readonly string[] PasswordArgumentTemplate =
+        { "plan", "--org", Fixture.Organization, "--password", Fixture.SecretValue };
+
+    /// <summary>只供白名單拒絕案例使用的合成參數範本；每次呼叫複製避免共用可變陣列。</summary>
+    private static readonly string[] NonAllowlistedOrganizationArguments =
+        { "plan", "--org", "production" };
+
+    /// <summary>CLI 說明案例的固定輸入，避免每次執行測試重複配置參數陣列。</summary>
+    private static readonly string[] HelpArguments = { "--help" };
+
+    /// <summary>未知命令案例的固定輸入，避免重複配置相同參數陣列。</summary>
+    private static readonly string[] UnknownCommandArguments = { "unsupported" };
+
+    /// <summary>空組織須規劃組件、類型與步驟；規劃本身完全不寫入。</summary>
+    [Fact(DisplayName = "空組織產生三層註冊計畫且不寫入")]
+    public void Plan_EmptyOrganization_CreatesAssemblyTypeAndStep()
+    {
+        using var f = new Fixture();
+        var plan = f.Plan();
+        Assert.Equal(ExpectedPlanOperations, plan.Operations.Select(x => x.Kind));
+        Assert.Empty(f.Service.Writes);
+        Assert.Equal(TimeSpan.Zero, plan.CreatedUtc.Offset);
+        Assert.Equal(64, plan.Sha256.Length);
+    }
+
+    /// <summary>第一次套用後再規劃同清單不得新增或重建步驟。</summary>
+    [Fact(DisplayName = "相同清單重跑沒有變更")]
+    public void Plan_SameState_NoOperations()
+    {
+        using var f = new Fixture();
+        f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
+        Assert.Empty(f.Plan().Operations);
+        Assert.Single(f.Service.Rows.Where(x => x.LogicalName == "sdkmessageprocessingstep"));
+    }
+
+    /// <summary>
+    /// 已註冊的追蹤步驟切成推播、再切回追蹤時，必須更新原資料庫 ID 而非重建。
+    /// 用會計數的假 secrets 驗證 plan 不解析秘密，apply 才解析，完成後同清單沒有差異，
+    /// 且停用推播會移除不再使用的 secure 記錄；輸出不保留解析後內容。
+    /// </summary>
+    [Fact(DisplayName = "既有步驟可切換追蹤與推播而不重建或洩漏秘密")]
+    public void Apply_ConfigurationChanged_UpdatesSameStepAndRemainsIdempotent()
+    {
+        using var f = new Fixture();
+        using var push = new Fixture(push: true);
+        f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
+        var stepId = Assert.Single(f.Service.Rows.Where(x => x.LogicalName == "sdkmessageprocessingstep")).Id;
+        var plan = f.Engine.Plan(push.Manifest, f.Image, Fixture.Organization);
+        Assert.Equal("UpdateStepConfiguration", Assert.Single(plan.Operations).Kind);
+        Assert.Equal(0, f.SecretReads);
+        f.Engine.Apply(plan, push.Manifest, f.Image);
+        var step = Assert.Single(f.Service.Rows.Where(x => x.LogicalName == "sdkmessageprocessingstep"));
+        Assert.Equal(stepId, step.Id);
+        Assert.Contains("\"mode\":\"push\"", step.GetAttributeValue<string>("configuration"), StringComparison.Ordinal);
+        Assert.Equal(2, f.SecretReads);
+        Assert.Empty(f.Engine.Plan(push.Manifest, f.Image, Fixture.Organization).Operations);
+        Assert.Single(f.Service.Rows.Where(x => x.LogicalName == "sdkmessageprocessingstepsecureconfig"));
+        f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
+        Assert.Equal(stepId, Assert.Single(f.Service.Rows.Where(x => x.LogicalName == "sdkmessageprocessingstep")).Id);
+        Assert.Empty(f.Service.Rows.Where(x => x.LogicalName == "sdkmessageprocessingstepsecureconfig"));
+        Assert.Empty(f.Plan().Operations);
+        Assert.DoesNotContain(Fixture.SecretValue, File.ReadAllText(Path.Combine(f.Root, "export.json")), StringComparison.Ordinal);
+    }
+
+    /// <summary>既有組件主次版本不同，須在任何寫入之前拒絕。</summary>
+    [Fact(DisplayName = "主次版本衝突停止")]
+    public void Plan_AssemblyMajorMinorChanged_Stops()
+    {
+        using var f = new Fixture(); f.SeedAssembly("2.0.0.0");
+        Assert.Throws<InvalidDataException>(() => f.Plan());
+        Assert.Empty(f.Service.Writes);
+    }
+
+    /// <summary>同名但不同公鑰身份不能被視為一般內容更新。</summary>
+    [Fact(DisplayName = "公鑰身份改變停止")]
+    public void Plan_AssemblyPublicKeyTokenChanged_Stops()
+    {
+        using var f = new Fixture(); f.SeedAssembly(token: "fedcba9876543210");
+        Assert.Throws<InvalidDataException>(() => f.Plan());
+        Assert.Empty(f.Service.Writes);
+    }
+
+    /// <summary>以會拋出的 secret reader 證明 plan 及 Markdown 根本不解析秘密。</summary>
+    [Fact(DisplayName = "計畫與 Markdown 保留佔位且不讀秘密")]
+    public void Plan_SecretPlaceholders_NeverResolvedIntoPlanOrMarkdown()
+    {
+        using var f = new Fixture(push: true);
+        var plan = f.Plan();
+        var markdown = DeploymentEngine.Markdown(plan);
+        Assert.Contains("${secret:", markdown, StringComparison.Ordinal);
+        Assert.DoesNotContain(Fixture.SecretValue, markdown, StringComparison.Ordinal);
+        Assert.Equal(0, f.SecretReads);
+    }
+
+    /// <summary>規劃後組織出現新的組件，apply 必須拒絕且沒有寫入。</summary>
+    [Fact(DisplayName = "組織現況已變拒絕套用")]
+    public void Apply_OrganizationChangedSincePlan_Refuses()
+    {
+        using var f = new Fixture(); var plan = f.Plan(); f.SeedAssembly();
+        Assert.Throws<InvalidDataException>(() => f.Engine.Apply(plan, f.Manifest, f.Image));
+        Assert.Empty(f.Service.Writes);
+    }
+
+    /// <summary>任意操作清單被修改卻未重算 hash，寫入前就拒絕。</summary>
+    [Fact(DisplayName = "計畫遭竄改拒絕套用")]
+    public void Apply_TamperedPlanHash_Refuses()
+    {
+        using var f = new Fixture(); var plan = f.Plan() with { Operations = Array.Empty<DeploymentOperation>() };
+        Assert.Throws<InvalidDataException>(() => f.Engine.Apply(plan, f.Manifest, f.Image));
+        Assert.Empty(f.Service.Writes);
+    }
+
+    /// <summary>第一個 SDK 寫入時檢查可獨佔讀取的 export，證明備份已落盤與關檔。</summary>
+    [Fact(DisplayName = "任何寫入前先完成備份")]
+    public void Apply_ExportsCurrentStateBeforeAnyWrite()
+    {
+        using var f = new Fixture();
+        f.Service.BeforeWrite = () => {
+            var file = Assert.Single(Directory.GetFiles(f.Root, "export.json", SearchOption.AllDirectories));
+            using var stream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
+            Assert.True(stream.Length > 0);
+        };
+        var backup = f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
+        Assert.True(File.Exists(backup));
+        Assert.NotEmpty(f.Service.Writes);
+    }
+
+    /// <summary>缺少組織啟用值一律停用；只有明確 true 才啟用，兩者皆保護。</summary>
+    /// <param name="enabled">該組織是否明確核定啟用。</param>
+    [Theory(DisplayName = "新步驟預設停用，明確啟用才開啟")]
+    [InlineData(false)] [InlineData(true)]
+    public void Apply_NewStep_DefaultDisabledUnlessManifestEnables(bool enabled)
+    {
+        using var f = new Fixture(enabled: enabled);
+        f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
+        var step = Assert.Single(f.Service.Rows.Where(x => x.LogicalName == "sdkmessageprocessingstep"));
+        Assert.Equal(enabled ? 0 : 1, step.GetAttributeValue<OptionSetValue>("statecode").Value);
+    }
+
+    /// <summary>更新組件內容及停用狀態後，rollback 要還原兩者而非只改版本字串。</summary>
+    [Fact(DisplayName = "回復備份恢復組件內容及步驟啟用狀態")]
+    public void Rollback_RestoresExportedStepStateAndAssemblyContent()
+    {
+        using var f = new Fixture(enabled: true);
+        f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
+        var changedImage = f.Image with { Content = Convert.ToBase64String(new byte[] { 9, 8, 7 }), Sha256 = Convert.ToHexString(SHA256.HashData(new byte[] { 9, 8, 7 })) };
+        var changed = f.Manifest with { Steps = new[] { f.Manifest.Steps[0] with { EnabledByOrganization = new Dictionary<string, bool>(StringComparer.Ordinal) } } };
+        var plan = f.Engine.Plan(changed, changedImage, Fixture.Organization);
+        var backup = f.Engine.Apply(plan, changed, changedImage);
+        f.Engine.Rollback(backup, f.Manifest, Fixture.Organization);
+        var assembly = Assert.Single(f.Service.Rows.Where(x => x.LogicalName == "pluginassembly"));
+        var step = Assert.Single(f.Service.Rows.Where(x => x.LogicalName == "sdkmessageprocessingstep"));
+        Assert.Equal(f.Image.Content, assembly.GetAttributeValue<string>("content"));
+        Assert.Equal(0, step.GetAttributeValue<OptionSetValue>("statecode").Value);
+    }
+
+    /// <summary>非白名單在 connector 被呼叫前拒絕，避免連到其他組織。</summary>
+    [Fact(DisplayName = "非白名單組織在連線前拒絕")]
+    public void Org_NotInAllowlist_Refused()
+    {
+        var connector = new ThrowingConnector(); using var output = new StringWriter(CultureInfo.InvariantCulture);
+        Assert.NotEqual(0, DeploymentCli.Run((string[])NonAllowlistedOrganizationArguments.Clone(), connector, output, _ => { }));
+        Assert.Equal(0, connector.Calls);
+    }
+
+    /// <summary>密碼選項即使語法正常也必須拒絕，不連線、不回顯值。</summary>
+    [Fact(DisplayName = "命令列密碼參數拒絕且不回顯")]
+    public void Cli_PasswordArgument_Rejected()
+    {
+        var connector = new ThrowingConnector(); using var output = new StringWriter(CultureInfo.InvariantCulture);
+        Assert.NotEqual(0, DeploymentCli.Run((string[])PasswordArgumentTemplate.Clone(), connector, output, _ => { }));
+        Assert.Equal(0, connector.Calls);
+        Assert.DoesNotContain(Fixture.SecretValue, output.ToString(), StringComparison.Ordinal);
+    }
+
+    /// <summary>互斥租約保護同組織寫入；持有第一把鎖時注入第二次取得，並斷言釋放後可重入。</summary>
+    [Fact(DisplayName = "同組織寫入互斥且釋放後可再次取得")]
+    public void OrganizationWriteLock_ExclusiveLeaseCanBeReacquiredAfterDispose()
+    {
+        using var f = new Fixture();
+        var first = OrganizationWriteLock.Acquire(f.Root, TimeSpan.Zero);
+
+        Assert.Throws<TimeoutException>(() => OrganizationWriteLock.Acquire(f.Root, TimeSpan.Zero));
+
+        first.Dispose();
+        first.Dispose();
+        using var second = OrganizationWriteLock.Acquire(f.Root, TimeSpan.Zero);
+        Assert.NotNull(second);
+    }
+
+    /// <summary>help 與未知命令不建立連線；以會計數的失敗 connector 證明 parser 先行拒絕。</summary>
+    [Fact(DisplayName = "說明與無效命令不建立連線")]
+    public void Cli_HelpAndUnknownCommand_DoNotConnect()
+    {
+        var connector = new ThrowingConnector();
+        using var output = new StringWriter(CultureInfo.InvariantCulture);
+
+        Assert.Equal(0, DeploymentCli.Run(HelpArguments, connector, output, _ => { }));
+        Assert.NotEqual(0, DeploymentCli.Run(UnknownCommandArguments, connector, output, _ => { }));
+
+        Assert.Equal(0, connector.Calls);
+    }
+
+    /// <summary>路徑穿越輸入在 connector 呼叫前拒絕；假 connector 計數是未建立連線的決定性證據。</summary>
+    [Fact(DisplayName = "Plan 路徑逸出組織輸出根時先拒絕")]
+    public void Cli_ApplyPathOutsideOrganizationRoot_RefusesBeforeConnect()
+    {
+        using var f = new Fixture();
+        var connector = new ThrowingConnector();
+        using var output = new StringWriter(CultureInfo.InvariantCulture);
+        var outsidePath = Path.Combine(f.Root, "outside-plan.json");
+
+        var exitCode = DeploymentCli.Run(
+            new[] { "apply", "--org", Fixture.Organization, "--plan", outsidePath },
+            connector,
+            output,
+            _ => { },
+            artifactRoot: f.Root);
+
+        Assert.Equal(2, exitCode);
+        Assert.Equal(0, connector.Calls);
+        Assert.DoesNotContain(outsidePath, output.ToString(), StringComparison.Ordinal);
+    }
+
+    /// <summary>持有同組織寫入鎖時 CLI 不得建立連線；立即逾時是故障注入，連線計數是判定斷言。</summary>
+    [Fact(DisplayName = "寫入命令先取得互斥鎖再建立連線")]
+    public void Cli_WriteCommand_WaitsForOrganizationLockBeforeConnect()
+    {
+        using var f = new Fixture();
+        var organizationRoot = Path.Combine(f.Root, Fixture.Organization);
+        using var heldLock = OrganizationWriteLock.Acquire(organizationRoot, TimeSpan.Zero);
+        var connector = new ThrowingConnector();
+        using var output = new StringWriter(CultureInfo.InvariantCulture);
+
+        var exitCode = DeploymentCli.Run(
+            new[] { "smoke", "--org", Fixture.Organization, "--scenario", "p0-ping-trace" },
+            connector,
+            output,
+            _ => { },
+            artifactRoot: f.Root,
+            writeLockTimeout: TimeSpan.Zero);
+
+        Assert.NotEqual(0, exitCode);
+        Assert.Equal(0, connector.Calls);
+        Assert.Contains(nameof(TimeoutException), output.ToString(), StringComparison.Ordinal);
+    }
+
+    /// <summary>
+    /// 假服務在建立 smoke task 後同步觸發 caller 取消；斷言 CLI 不告警、清除 task 並釋放連線。
+    /// 反射只用來讓此案例先在缺少取消入口時以明確契約失敗；執行時仍呼叫正式 CLI 方法。
+    /// </summary>
+    [Fact(DisplayName = "CLI 取消 smoke 時清理資料且不送例外告警")]
+    public void Cli_SmokeCallerCancellation_CleansUpWithoutReporting()
+    {
+        var run = typeof(DeploymentCli).GetMethods(BindingFlags.Public | BindingFlags.Static)
+            .SingleOrDefault(method =>
+                method.Name == nameof(DeploymentCli.Run) &&
+                method.GetParameters().LastOrDefault()?.ParameterType == typeof(CancellationToken));
+        Assert.NotNull(run);
+
+        using var f = new Fixture();
+        using var cancellation = new CancellationTokenSource();
+        f.Service.Trace = "P0-PING OK zh-TW=ready qps-ploc=ready";
+        f.Service.OnTaskCreated = cancellation.Cancel;
+        var connector = new TrackingConnector(f.Service);
+        using var output = new StringWriter(CultureInfo.InvariantCulture);
+        var reportCount = 0;
+
+        var exitCode = (int)run!.Invoke(null, new object?[]
+        {
+            new[] { "smoke", "--org", Fixture.Organization, "--scenario", "p0-ping-trace" },
+            connector,
+            output,
+            (Action<Exception>)(_ => reportCount++),
+            f.Root,
+            null,
+            TimeSpan.Zero,
+            cancellation.Token
+        })!;
+
+        Assert.Equal(130, exitCode);
+        Assert.Equal(0, reportCount);
+        Assert.Equal(1, connector.DisposeCount);
+        Assert.Contains("Command cancelled.", output.ToString(), StringComparison.Ordinal);
+        Assert.DoesNotContain(Fixture.SecretValue, output.ToString(), StringComparison.Ordinal);
+        Assert.Empty(f.Service.Rows.Where(row => row.LogicalName == "task"));
+        Assert.Single(f.Service.Writes.Where(write => write == "Delete:task"));
+        Assert.Equal(0, f.Clock.ActiveTimers);
+    }
+
+    /// <summary>同組織互斥鎖等待期間收到 caller 取消時，必須立即停止且尚未建立連線。</summary>
+    [Fact(DisplayName = "等待寫入鎖時取消 smoke 不建立連線")]
+    public async Task Cli_SmokeCancellationWhileWaitingForLock_StopsBeforeConnect()
+    {
+        using var f = new Fixture();
+        var organizationRoot = Path.Combine(f.Root, Fixture.Organization);
+        using var heldLock = OrganizationWriteLock.Acquire(organizationRoot, TimeSpan.Zero);
+        using var cancellation = new CancellationTokenSource();
+        var connector = new ThrowingConnector();
+        using var output = new StringWriter(CultureInfo.InvariantCulture);
+        var reportCount = 0;
+        var cancelTask = Task.Run(async () =>
+        {
+            await Task.Delay(TimeSpan.FromMilliseconds(10));
+            cancellation.Cancel();
+        });
+
+        var exitCode = DeploymentCli.Run(
+            new[] { "smoke", "--org", Fixture.Organization, "--scenario", "p0-ping-trace" },
+            connector,
+            output,
+            _ => reportCount++,
+            artifactRoot: f.Root,
+            writeLockTimeout: TimeSpan.FromMilliseconds(100),
+            cancellationToken: cancellation.Token);
+        await cancelTask;
+
+        Assert.Equal(130, exitCode);
+        Assert.Equal(0, connector.Calls);
+        Assert.Equal(0, reportCount);
+        Assert.Contains("Command cancelled.", output.ToString(), StringComparison.Ordinal);
+    }
+
+    /// <summary>額外設定的組織別名會在連線前遭拒，避免 User Secrets 擴大唯一核准邊界。</summary>
+    [Fact(DisplayName = "部署設定不得加入其他允許組織")]
+    public void Connector_AdditionalAllowedOrganization_IsRejected()
+    {
+        var configuration = CreateConnectorConfiguration(additionalOrganization: "production");
+        using var configurationOwner = (IDisposable)configuration;
+
+        Assert.Throws<InvalidOperationException>(
+            () => new UserSecretsOrganizationConnector(configuration));
+    }
+
+    /// <summary>
+    /// 合法假 User Secrets 必須逐欄傳給建線邊界，且命令租約結束後釋放唯一假 client；
+    /// 工廠只建立記憶體服務，案例不會嘗試連到設定中的 Organization.svc。
+    /// </summary>
+    [Fact(DisplayName = "合法 User Secrets 建立單次 client 並由租約釋放")]
+    public void Connector_ValidSecrets_CreatesAndDisposesOneClient()
+    {
+        var configuration = CreateConnectorConfiguration();
+        using var configurationOwner = (IDisposable)configuration;
+        var service = new MemoryOrganization();
+        var sawExpectedEndpoint = false;
+        var sawExpectedCredentials = false;
+        var connector = new UserSecretsOrganizationConnector(
+            configuration,
+            (organizationUrl, username, password) =>
+            {
+                sawExpectedEndpoint =
+                    organizationUrl == "https://goodshepherdback.speechmessage.com.tw/XRMServices/2011/Organization.svc";
+                sawExpectedCredentials =
+                    username == "synthetic-user" && password == Fixture.SecretValue;
+                return service;
+            });
+
+        var lease = connector.Connect(Fixture.Organization);
+
+        Assert.Same(service, lease.Service);
+        Assert.True(sawExpectedEndpoint);
+        Assert.True(sawExpectedCredentials);
+        lease.Dispose();
+        lease.Dispose();
+        Assert.Equal(1, service.DisposeCount);
+        Assert.Throws<ObjectDisposedException>(() => lease.Service);
+    }
+
+    /// <summary>端點主機或路徑不符時使用固定錯誤，且在建立地端 client 前拒絕。</summary>
+    [Theory(DisplayName = "部署端點必須精確符合唯一 Organization.svc")]
+    [InlineData("https://example.invalid/XRMServices/2011/Organization.svc")]
+    [InlineData("http://goodshepherdback.speechmessage.com.tw/XRMServices/2011/Organization.svc")]
+    [InlineData("https://goodshepherdback.speechmessage.com.tw/")]
+    [InlineData("https://goodshepherdback.speechmessage.com.tw/XRMServices/2011/Organization.svc?mode=test")]
+    public void Connector_InvalidOrganizationUrl_IsRejected(string organizationUrl)
+    {
+        var configuration = CreateConnectorConfiguration(organizationUrl: organizationUrl);
+        using var configurationOwner = (IDisposable)configuration;
+        var connector = new UserSecretsOrganizationConnector(configuration);
+
+        var exception = Assert.Throws<InvalidOperationException>(
+            () => connector.Connect(Fixture.Organization));
+
+        Assert.DoesNotContain(Fixture.SecretValue, exception.Message, StringComparison.Ordinal);
+    }
+
+    /// <summary>缺少任一必要 credential 時固定拒絕；注入假設定並確認錯誤不回顯其值。</summary>
+    /// <param name="missingCredential">本次故障注入要移除的設定鍵。</param>
+    [Theory(DisplayName = "部署連線缺少必要 User Secrets 時拒絕")]
+    [InlineData("Username")]
+    [InlineData("Password")]
+    public void Connector_MissingCredential_IsRejected(string missingCredential)
+    {
+        var configuration = CreateConnectorConfiguration(missingCredential: missingCredential);
+        using var configurationOwner = (IDisposable)configuration;
+        var connector = new UserSecretsOrganizationConnector(configuration);
+
+        var exception = Assert.Throws<InvalidOperationException>(
+            () => connector.Connect(Fixture.Organization));
+
+        Assert.DoesNotContain(Fixture.SecretValue, exception.Message, StringComparison.Ordinal);
+    }
+
+    /// <summary>清單 JSON 損壞時不把原始內容寫入錯誤；檔案只在案例私有暫存根中建立。</summary>
+    [Fact(DisplayName = "註冊清單錯誤不回顯原始內容")]
+    public void ManifestLoader_MalformedJson_DoesNotEchoFileContent()
+    {
+        using var f = new Fixture();
+        var directory = Path.Combine(f.Root, "d365", "registration");
+        Directory.CreateDirectory(directory);
+        File.WriteAllText(Path.Combine(directory, "plugins.registration.json"),
+            "malformed-" + Fixture.SecretValue);
+
+        var exception = Assert.Throws<InvalidDataException>(
+            () => RegistrationManifestLoader.Load(f.Root));
+
+        Assert.DoesNotContain(Fixture.SecretValue, exception.Message, StringComparison.Ordinal);
+    }
+
+    /// <summary>PE 掃描拒絕空檔與非 .NET 中繼資料，並確定性釋放各自的輸入檔案。</summary>
+    [Fact(DisplayName = "組件掃描拒絕空檔與非 PE 內容")]
+    public void AssemblyCatalog_EmptyAndNonPeFiles_AreRejected()
+    {
+        using var f = new Fixture();
+        var emptyPath = Path.Combine(f.Root, "empty.dll");
+        var textPath = Path.Combine(f.Root, "text.dll");
+        File.WriteAllBytes(emptyPath, Array.Empty<byte>());
+        File.WriteAllText(textPath, "synthetic-not-a-pe");
+
+        Assert.Throws<InvalidDataException>(() => AssemblyCatalog.Read(emptyPath, f.Manifest));
+        Assert.Throws<BadImageFormatException>(() => AssemblyCatalog.Read(textPath, f.Manifest));
+        using var exclusive = new FileStream(textPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
+    }
+
+    /// <summary>清單的組件名稱必須與 PE metadata 完全相同，錯誤身份不得進入部署摘要。</summary>
+    [Fact(DisplayName = "組件名稱與清單不符時拒絕")]
+    public void AssemblyCatalog_AssemblyNameMismatch_IsRejected()
+    {
+        using var f = new Fixture();
+        var path = f.Emit();
+        var manifest = f.Manifest with { AssemblyName = "Different.Assembly" };
+
+        Assert.Throws<InvalidDataException>(() => AssemblyCatalog.Read(path, manifest));
+        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
+    }
+
+    /// <summary>注入不符合診斷內容的 trace，驗收失敗仍要刪掉唯一測試 task。</summary>
+    [Fact(DisplayName = "smoke 斷言失敗仍刪除測試資料")]
+    public async Task Smoke_AssertionFails_StillDeletesTestRecord()
+    {
+        using var f = new Fixture(); f.Service.Trace = "incorrect trace";
+        Assert.False(await f.Engine.SmokeAsync("p0-ping-trace", CancellationToken.None));
+        Assert.Empty(f.Service.Rows.Where(x => x.LogicalName == "task"));
+        Assert.Single(f.Service.Writes.Where(x => x == "Delete:task"));
+    }
+
+    /// <summary>用假時鐘推進三分鐘，不真的等待；逾時失敗後 task 與 timer 均須釋放。</summary>
+    [Fact(DisplayName = "smoke 逾時仍刪除並回傳失敗")]
+    public async Task Smoke_Timeout_DeletesTestRecordAndReturnsFailure()
+    {
+        using var f = new Fixture(); f.Service.Trace = null;
+        Assert.False(await f.Engine.SmokeAsync("p0-ping-trace", CancellationToken.None));
+        Assert.True(f.Service.TraceQueryCount > 1);
+        Assert.Equal(2, f.Clock.DelayCount);
+        Assert.Empty(f.Service.Rows.Where(x => x.LogicalName == "task"));
+        Assert.Single(f.Service.Writes.Where(x => x == "Delete:task"));
+        Assert.Equal(0, f.Clock.ActiveTimers);
+    }
+
+    /// <summary>追蹤在第二次輪詢才出現時仍須成功，並於結果確認後刪除唯一測試 task。</summary>
+    [Fact(DisplayName = "smoke 等待延遲追蹤並在成功後刪除測試資料")]
+    public async Task Smoke_TraceArrivesAfterPolling_ReturnsSuccessAndDeletesTestRecord()
+    {
+        using var f = new Fixture();
+        f.Service.Trace = "P0-PING OK zh-TW=ready qps-ploc=ready";
+        f.Service.TraceAvailableAfterPoll = 2;
+
+        Assert.True(await f.Engine.SmokeAsync("p0-ping-trace", CancellationToken.None));
+
+        Assert.Equal(2, f.Service.TraceQueryCount);
+        Assert.Single(f.Service.Writes.Where(x => x == "Delete:task"));
+        Assert.Empty(f.Service.Rows.Where(x => x.LogicalName == "task"));
+        Assert.Equal(0, f.Clock.ActiveTimers);
+    }
+
+    /// <summary>連線例外含假密碼時，CLI 輸出與共用 Exception.log 都不得寫出原始訊息。</summary>
+    [Fact(DisplayName = "連線錯誤所有輸出不含秘密且保留例外身份")]
+    public void Logs_NeverContainSecretValues()
+    {
+        var run = typeof(Program).GetMethod("RunCommand", BindingFlags.NonPublic | BindingFlags.Static);
+        Assert.NotNull(run);
+
+        using var f = new Fixture();
+        var connector = new ThrowingConnector();
+        var configuration = CreateConnectorConfiguration();
+        using var configurationOwner = (IDisposable)configuration;
+        using var output = new StringWriter(CultureInfo.InvariantCulture);
+
+        var exitCode = (int)run!.Invoke(null, new object?[]
+        {
+            new[] { "plan", "--org", Fixture.Organization },
+            connector,
+            output,
+            configuration,
+            Path.Combine(f.Root, "Logs"),
+            CancellationToken.None
+        })!;
+        var emittedFiles = Directory.EnumerateFiles(f.Root, "*", SearchOption.AllDirectories)
+            .Select(File.ReadAllText)
+            .Append(output.ToString());
+        var combinedOutput = string.Join(Environment.NewLine, emittedFiles);
+
+        Assert.NotEqual(0, exitCode);
+        Assert.Equal(1, connector.Calls);
+        Assert.False(ExceptionReporting.IsActive);
+        Assert.True(File.Exists(Path.Combine(f.Root, "Logs", "Exception.log")));
+        Assert.Contains(nameof(InvalidOperationException), combinedOutput, StringComparison.Ordinal);
+        Assert.DoesNotContain(Fixture.SecretValue, combinedOutput, StringComparison.Ordinal);
+        Assert.Contains(nameof(InvalidOperationException), output.ToString(), StringComparison.Ordinal);
+    }
+
+    /// <summary>成功命令只建立一份服務租約，結束後立即釋放且不把連線留給下一個命令。</summary>
+    [Fact(DisplayName = "命令完成後釋放唯一連線租約")]
+    public void Cli_ExportCommand_DisposesCommandLease()
+    {
+        using var f = new Fixture();
+        var connector = new TrackingConnector(f.Service);
+        using var output = new StringWriter(CultureInfo.InvariantCulture);
+
+        var exitCode = DeploymentCli.Run(
+            new[] { "export", "--org", Fixture.Organization },
+            connector,
+            output,
+            _ => { },
+            artifactRoot: f.Root);
+
+        Assert.Equal(0, exitCode);
+        Assert.Equal(1, connector.ConnectCount);
+        Assert.Equal(1, connector.DisposeCount);
+        Assert.Single(Directory.GetFiles(f.Root, "export.json", SearchOption.AllDirectories));
+    }
+
+    /// <summary>連線後查詢失敗仍釋放租約；輸出遮罩假密碼並把原例外交給共用回報入口。</summary>
+    [Fact(DisplayName = "命令失敗時釋放連線且遮罩例外訊息")]
+    public void Cli_ExportFailure_DisposesLeaseAndRedactsException()
+    {
+        using var f = new Fixture();
+        var failure = new InvalidOperationException("synthetic connection detail " + Fixture.SecretValue);
+        f.Service.RetrieveFailure = failure;
+        var connector = new TrackingConnector(f.Service);
+        using var output = new StringWriter(CultureInfo.InvariantCulture);
+        Exception? reported = null;
+
+        var exitCode = DeploymentCli.Run(
+            new[] { "export", "--org", Fixture.Organization },
+            connector,
+            output,
+            exception => reported = exception,
+            artifactRoot: f.Root);
+
+        Assert.NotEqual(0, exitCode);
+        Assert.Same(failure, reported);
+        Assert.Equal(1, connector.ConnectCount);
+        Assert.Equal(1, connector.DisposeCount);
+        Assert.Contains(nameof(InvalidOperationException), output.ToString(), StringComparison.Ordinal);
+        Assert.Contains("Sensitive details are not shown.", output.ToString(), StringComparison.Ordinal);
+        Assert.DoesNotContain("Details were sent", output.ToString(), StringComparison.Ordinal);
+        Assert.DoesNotContain(Fixture.SecretValue, output.ToString(), StringComparison.Ordinal);
+    }
+
+    /// <summary>plan 不讀取佔位；apply 才把假值送入假服務，備份與輸出仍不得保存該值。</summary>
+    [Fact(DisplayName = "佔位秘密只在套用時解析且不落入輸出")]
+    public void Apply_SecretPlaceholdersResolvedOnlyForWrites()
+    {
+        using var f = new Fixture(push: true);
+        var plan = f.Plan();
+
+        Assert.Equal(0, f.SecretReads);
+        Assert.DoesNotContain(Fixture.SecretValue, DeploymentEngine.Markdown(plan), StringComparison.Ordinal);
+
+        f.Engine.Apply(plan, f.Manifest, f.Image);
+
+        Assert.Equal(2, f.SecretReads);
+        var step = Assert.Single(f.Service.Rows.Where(x => x.LogicalName == "sdkmessageprocessingstep"));
+        var secureConfigurationReference = Assert.IsType<EntityReference>(step["secureconfigid"]);
+        var secureConfiguration = Assert.Single(f.Service.Rows.Where(
+            x => x.LogicalName == "sdkmessageprocessingstepsecureconfig" &&
+                 x.Id == secureConfigurationReference.Id));
+        Assert.Equal(Fixture.SecretValue, secureConfiguration.GetAttributeValue<string>("secureconfig"));
+        var exportedJson = File.ReadAllText(Path.Combine(f.Root, "export.json"));
+        Assert.DoesNotContain(Fixture.SecretValue, exportedJson, StringComparison.Ordinal);
+    }
+
+    /// <summary>匯出集合若收到相同資料庫 ID 的衝突步驟，必須拒絕而不能以列順序挑一筆。</summary>
+    [Fact(DisplayName = "匯出遇到重複步驟資料庫 ID 時拒絕")]
+    public void Export_DuplicateStepIds_FailsClosed()
+    {
+        using var f = new Fixture();
+        var assemblyId = Guid.NewGuid();
+        var typeId = Guid.NewGuid();
+        var message = Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "sdkmessage"));
+        var filter = Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "sdkmessagefilter"));
+        f.Service.Seed(new Entity("pluginassembly", assemblyId)
+        {
+            ["name"] = f.Image.Name,
+            ["version"] = f.Image.Version,
+            ["publickeytoken"] = f.Image.PublicKeyToken,
+            ["content"] = f.Image.Content,
+            ["isolationmode"] = new OptionSetValue(2),
+            ["sourcetype"] = new OptionSetValue(0)
+        });
+        f.Service.Seed(new Entity("plugintype", typeId)
+        {
+            ["name"] = "DiagnosticsPingPlugin",
+            ["typename"] = f.Manifest.Steps[0].Type,
+            ["pluginassemblyid"] = new EntityReference("pluginassembly", assemblyId)
+        });
+        var duplicateId = Guid.NewGuid();
+        f.Service.Seed(CreateStep(duplicateId, typeId, message, filter, "first"));
+        f.Service.Seed(CreateStep(duplicateId, typeId, message, filter, "conflicting"));
+
+        Assert.Throws<InvalidDataException>(() =>
+            f.Engine.WriteExport(Fixture.Organization, Path.Combine(f.Root, "duplicate-export.json")));
+    }
+
+    /// <summary>DLL 有清單未列出的公開 IPlugin 時不能只驗證清單中型別存在而假通過。</summary>
+    [Fact(DisplayName = "DLL 中每個外掛都必須有註冊")]
+    public void Manifest_EveryPluginTypeInBuiltDll_IsRegistered()
+    {
+        using var f = new Fixture();
+        var path = f.Emit(extra: true);
+        Assert.Throws<InvalidDataException>(() => AssemblyCatalog.Read(path, f.Manifest));
+    }
+
+    /// <summary>清單列了 DLL 中不存在的類型時 L2 必須拒絕；不載入執行樣本。</summary>
+    [Fact(DisplayName = "清單類型不存在 DLL 時拒絕")]
+    public void Manifest_TypeNotInBuiltDll_Fails()
+    {
+        using var f = new Fixture(); var path = f.Emit();
+        var manifest = f.Manifest with { Steps = new[] { f.Manifest.Steps[0] with { Type = "Missing.Plugin" } } };
+        Assert.Throws<InvalidDataException>(() => AssemblyCatalog.Read(path, manifest));
+    }
+
+    /// <summary>辨識繼承 PluginShellBase 的公開外掛，避免正式 DLL 的間接 IPlugin 實作漏列。</summary>
+    [Fact(DisplayName = "PE 掃描辨識 PluginShellBase 子類別")]
+    public void Manifest_PluginShellSubclass_IsRecognized()
+    {
+        using var f = new Fixture();
+        var path = f.Emit();
+
+        var image = AssemblyCatalog.Read(path, f.Manifest);
+
+        Assert.Equal(f.Manifest.Steps[0].Type, Assert.Single(image.PluginTypes));
+    }
+
+    /// <summary>
+    /// 建立只含合成 connector 設定的記憶體組態；所有憑證樣本均為假值，
+    /// 不讀取 User Secrets、環境變數或任何 D365 組織。
+    /// </summary>
+    /// <param name="organizationUrl">待驗證的 Organization.svc URL。</param>
+    /// <param name="additionalOrganization">可選的白名單越界案例。</param>
+    /// <param name="missingCredential">可選的必要設定移除案例。</param>
+    /// <returns>由案例持有並 Dispose 的記憶體組態根。</returns>
+    private static IConfigurationRoot CreateConnectorConfiguration(
+        string organizationUrl = "https://goodshepherdback.speechmessage.com.tw/XRMServices/2011/Organization.svc",
+        string? additionalOrganization = null,
+        string? missingCredential = null)
+    {
+        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
+        {
+            ["Deployment:AllowedOrganizations:0"] = Fixture.Organization,
+            ["Deployment:Organizations:goodshepherdback:OrganizationUrl"] = organizationUrl,
+            ["Deployment:Organizations:goodshepherdback:Username"] = "synthetic-user",
+            ["Deployment:Organizations:goodshepherdback:Password"] = Fixture.SecretValue
+        };
+        if (additionalOrganization is not null)
+        {
+            values["Deployment:AllowedOrganizations:1"] = additionalOrganization;
+        }
+
+        if (missingCredential is not null)
+        {
+            values.Remove("Deployment:Organizations:goodshepherdback:" + missingCredential);
+        }
+
+        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
+    }
+
+    /// <summary>只持有假密碼的失敗工廠；不建立任何服務或背景資源。</summary>
+    private sealed class ThrowingConnector : IOrganizationConnector
+    {
+        /// <summary>呼叫次數供拒絕必須發生在連線前的決定性斷言。</summary>
+        public int Calls { get; private set; }
+        /// <summary>故障注入的原始實例，故意含假機密。</summary>
+        public InvalidOperationException Failure { get; } = new("connection failed: " + Fixture.SecretValue);
+        /// <summary>每次都拋固定合成例外，完全沒有網路行為。</summary>
+        /// <param name="organization">不回顯的測試組織。</param><returns>此故障案例不產生租約。</returns>
+        public OrganizationLease Connect(string organization) { Calls++; throw Failure; }
+    }
+
+    /// <summary>以記憶體服務故障注入命令生命週期，記錄每次連線與租約釋放。</summary>
+    private sealed class TrackingConnector(IOrganizationService service) : IOrganizationConnector
+    {
+        /// <summary>證明每個 CLI 命令只建立一條連線。</summary>
+        public int ConnectCount { get; private set; }
+        /// <summary>證明命令成功或失敗後都會釋放其租約。</summary>
+        public int DisposeCount { get; private set; }
+
+        /// <summary>每次建立獨立租約，沒有網路、共用池或跨案例狀態。</summary>
+        /// <param name="organization">已通過白名單的測試組織。</param>
+        /// <returns>只供當次命令持有的記憶體服務租約。</returns>
+        public OrganizationLease Connect(string organization)
+        {
+            ConnectCount++;
+            return new OrganizationLease(service, () => DisposeCount++);
+        }
+    }
+
+    /// <summary>建立指定身份的 Plugin 步驟，供重複 ID 故障注入使用。</summary>
+    /// <param name="stepId">兩列刻意共用的資料庫 ID。</param>
+    /// <param name="typeId">所屬 Plugin 型別 ID。</param>
+    /// <param name="message">Create 訊息 metadata。</param>
+    /// <param name="filter">task 訊息篩選器 metadata。</param>
+    /// <param name="configuration">用以區分衝突列的合成設定。</param>
+    /// <returns>本案例專用的假步驟列。</returns>
+    private static Entity CreateStep(Guid stepId, Guid typeId, Entity message, Entity filter, string configuration) =>
+        new("sdkmessageprocessingstep", stepId)
+        {
+            ["eventhandler"] = new EntityReference("plugintype", typeId),
+            ["sdkmessageid"] = message.ToEntityReference(),
+            ["sdkmessagefilterid"] = filter.ToEntityReference(),
+            ["stage"] = new OptionSetValue(40),
+            ["mode"] = new OptionSetValue(1),
+            ["rank"] = 1,
+            ["configuration"] = configuration,
+            ["statecode"] = new OptionSetValue(1),
+            ["statuscode"] = new OptionSetValue(2)
+        };
+}
+
+/// <summary>每案例獨占暫存、假服務與時鐘，Dispose 確定性清除；不碰使用者 secrets 或正式 key。</summary>
+internal sealed class Fixture : IDisposable
+{
+    /// <summary>唯一核定的組織邊界。</summary>
+    public const string Organization = "goodshepherdback";
+    /// <summary>只供機密掃描與故障注入的假值。</summary>
+    public const string SecretValue = "SYNTHETIC-SECRET-NOT-A-CREDENTIAL";
+    /// <summary>唯一測試根，不從外部輸入組成清理目標。</summary>
+    public string Root { get; } = Path.Combine(Path.GetTempPath(), "speechmessage-deployment-" + Guid.NewGuid().ToString("N"));
+    /// <summary>不含業務資料的記憶體組織。</summary>
+    public MemoryOrganization Service { get; } = new();
+    /// <summary>不依賴牆上時鐘的有限等待來源。</summary>
+    public AdvancingClock Clock { get; } = new();
+    /// <summary>固定清單候選，不含實際 token 或 userId。</summary>
+    public RegistrationManifest Manifest { get; }
+    /// <summary>以小型合成內容保護內容雜湊與還原，不作正式部署。</summary>
+    public AssemblyImage Image { get; } = new("SpeechMessage.Dynamics.Plugins", "1.0.0.0", "0123456789abcdef", "AQID", Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 })), new[] { "SpeechMessage.Dynamics.Plugins.Diagnostics.DiagnosticsPingPlugin" });
+    /// <summary>本案例待測核心，使用相同假服務。</summary>
+    public DeploymentEngine Engine { get; }
+    /// <summary>證明唯讀計畫沒有解析秘密的計數。</summary>
+    public int SecretReads { get; private set; }
+    /// <summary>建立各案例隔離資源；推播也只使用佔位與假 secret。</summary>
+    /// <param name="push">是否測試推播設定佔位。</param><param name="enabled">是否明確啟用測試步驟。</param>
+    public Fixture(bool push = false, bool enabled = false)
+    {
+        Directory.CreateDirectory(Root);
+        var step = new StepRegistration {
+            UnsecureConfig = push ? "{\"mode\":\"push\",\"culture\":\"zh-TW\",\"lineUserId\":\"${secret:TestLineUserId}\"}" : "{\"mode\":\"trace\",\"culture\":\"zh-TW\"}",
+            SecureConfig = push ? "${secret:TestChannelToken}" : "",
+            EnabledByOrganization = enabled ? new Dictionary<string, bool>(StringComparer.Ordinal) { [Organization] = true } : new Dictionary<string, bool>(StringComparer.Ordinal)
+        };
+        Manifest = new RegistrationManifest { Steps = new[] { step } };
+        Engine = new DeploymentEngine(Service, Root, Clock, _ => { SecretReads++; return SecretValue; });
+        Service.Seed(new Entity("sdkmessage", Guid.NewGuid()) { ["name"] = "Create" });
+        Service.Seed(new Entity("sdkmessagefilter", Guid.NewGuid()) { ["primaryobjecttypecode"] = "task", ["sdkmessageid"] = Service.Rows[0].ToEntityReference() });
+    }
+    /// <summary>對目前假組織規劃，測試無網路副作用。</summary>
+    /// <returns>含固定時鐘的計畫。</returns>
+    public DeploymentPlan Plan() => Engine.Plan(Manifest, Image, Organization);
+    /// <summary>注入既有組件，以版本或公鑰衝突測試拒絕。</summary>
+    /// <param name="version">合成版本。</param><param name="token">合成公鑰身份。</param>
+    public void SeedAssembly(string version = "1.0.0.0", string token = "0123456789abcdef") => Service.Seed(new Entity("pluginassembly", Guid.NewGuid()) {
+        ["name"] = Image.Name, ["version"] = version, ["publickeytoken"] = token, ["content"] = Image.Content,
+        ["isolationmode"] = new OptionSetValue(2), ["sourcetype"] = new OptionSetValue(0)
+    });
+    /// <summary>離線產生沒有金鑰的最小 net48 樣本，僅供 MetadataReader 測試。</summary>
+    /// <param name="extra">是否注入未註冊型別。</param><returns>不載入執行的 PE 路徑。</returns>
+    public string Emit(bool extra = false)
+    {
+        var source = "[assembly:System.Runtime.Versioning.TargetFramework(\".NETFramework,Version=v4.8\")] " +
+            "namespace Microsoft.Xrm.Sdk { public interface IPlugin { } } namespace SpeechMessage.Dynamics.Plugins { public abstract class PluginShellBase : Microsoft.Xrm.Sdk.IPlugin { } } " +
+            "namespace SpeechMessage.Dynamics.Plugins.Diagnostics { public sealed class DiagnosticsPingPlugin : SpeechMessage.Dynamics.Plugins.PluginShellBase { } " +
+            (extra ? "public sealed class UnregisteredPlugin : Microsoft.Xrm.Sdk.IPlugin { }" : "") + " }";
+        var compilation = CSharpCompilation.Create(Image.Name, new[] { CSharpSyntaxTree.ParseText(source) },
+            new[] { MetadataReference.CreateFromFile(Path.Combine(AppContext.BaseDirectory, "Framework", "mscorlib.dll")) }, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
+        var path = Path.Combine(Root, "sample.dll"); var result = compilation.Emit(path);
+        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(x => x.ToString())));
+        return path;
+    }
+    /// <summary>待測程式已釋放 reader、stream、timer 後立即刪除案例自己的唯一根。</summary>
+    public void Dispose() => Directory.Delete(Root, recursive: true);
+}
+
+/// <summary>無外部資源的 SDK 假組織，僅模擬 P0 中繼資料與測試 task；不把它當作產品實作。</summary>
+internal sealed class MemoryOrganization : IOrganizationService, IDisposable
+{
+    /// <summary>案例自己的候選列，不跨 fixture 共用。</summary>
+    public List<Entity> Rows { get; } = new();
+    /// <summary>用於驗證寫入先後順序的固定事件，不包含欄位值。</summary>
+    public List<string> Writes { get; } = new();
+    /// <summary>寫入前檢查 export 的故障注入點。</summary>
+    public Action? BeforeWrite { get; set; }
+    /// <summary>只供 smoke 對應記錄的合成追蹤內容；null 表示尚無回應。</summary>
+    public string? Trace { get; set; }
+    /// <summary>設定第幾次 plugintracelog 查詢才可看見合成追蹤。</summary>
+    public int TraceAvailableAfterPoll { get; set; } = 1;
+    /// <summary>記錄 smoke 追蹤查詢次數，證明案例真的輪詢而非立即失敗。</summary>
+    public int TraceQueryCount { get; private set; }
+    /// <summary>故障注入：查詢時回傳同一例外，供 CLI 驗證遮罩與租約清理。</summary>
+    public Exception? RetrieveFailure { get; set; }
+    /// <summary>smoke task 與其關聯記錄建立後觸發 caller 取消的測試掛鉤。</summary>
+    public Action? OnTaskCreated { get; set; }
+    /// <summary>假 client 的 deterministic release 計數；用於確認連線租約只釋放一次。</summary>
+    public int DisposeCount { get; private set; }
+    /// <summary>只在安排階段注入列，不計入產品 SDK 寫入。</summary>
+    /// <param name="entity">只屬本案例的假列。</param>
+    public void Seed(Entity entity) => Rows.Add(entity);
+    /// <summary>模擬資料庫生成 ID，並記錄唯一身份；task 帶動自己專用的非同步作業。</summary>
+    /// <param name="entity">候選列。</param><returns>資料庫身份。</returns>
+    public Guid Create(Entity entity)
+    {
+        Record("Create:" + entity.LogicalName);
+        var copy = Copy(entity); if (copy.Id == Guid.Empty) copy.Id = Guid.NewGuid(); Rows.Add(copy);
+        if (entity.LogicalName == "task")
+        {
+            var correlation = Guid.NewGuid();
+            Seed(new Entity("asyncoperation", Guid.NewGuid()) { ["regardingobjectid"] = copy.ToEntityReference(), ["correlationid"] = correlation });
+            if (Trace is not null)
+            {
+                Seed(new Entity("plugintracelog", Guid.NewGuid()) { ["correlationid"] = correlation, ["typename"] = new StepRegistration().Type, ["messageblock"] = Trace });
+            }
+
+            OnTaskCreated?.Invoke();
+        }
+        return copy.Id;
+    }
+    /// <summary>只更新同一實體的資料庫身份，不按顯示名稱合併。</summary>
+    /// <param name="entity">包含 ID 的更新。</param>
+    public void Update(Entity entity)
+    {
+        Record("Update:" + entity.LogicalName); var row = Rows.Single(x => x.LogicalName == entity.LogicalName && x.Id == entity.Id);
+        foreach (var pair in entity.Attributes) row[pair.Key] = pair.Value;
+    }
+    /// <summary>刪除精確身份，用以驗證 smoke finally 的所有出口。</summary>
+    /// <param name="entityName">固定邏輯名。</param><param name="id">本案例的資料庫 ID。</param>
+    public void Delete(string entityName, Guid id) { Record("Delete:" + entityName); Rows.RemoveAll(x => x.LogicalName == entityName && x.Id == id); }
+    /// <summary>以 detached 副本回傳，避免測試以共用可變參考掩蓋發布缺陷。</summary>
+    /// <param name="entityName">邏輯名。</param><param name="id">資料庫 ID。</param><param name="columnSet">要求欄位。</param><returns>獨立副本。</returns>
+    public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => Copy(Rows.Single(x => x.LogicalName == entityName && x.Id == id));
+    /// <summary>只支援測試實際需要的 QueryExpression，未支援格式直接失敗。</summary>
+    /// <param name="query">含隔離條件的結構查詢。</param><returns>獨立結果列集合。</returns>
+    public EntityCollection RetrieveMultiple(QueryBase query)
+    {
+        if (RetrieveFailure is not null) throw RetrieveFailure;
+        var expression = Assert.IsType<QueryExpression>(query);
+        if (expression.EntityName == "plugintracelog")
+        {
+            TraceQueryCount++;
+            if (TraceQueryCount < TraceAvailableAfterPoll)
+            {
+                return new EntityCollection();
+            }
+        }
+
+        var rows = Rows.Where(x => x.LogicalName == expression.EntityName && Matches(x, expression.Criteria)).Select(Copy).ToList();
+        return new EntityCollection(rows);
+    }
+    /// <summary>模擬狀態切換；未定義訊息拒絕，避免假的成功。</summary>
+    /// <param name="request">SDK 訊息。</param><returns>合成回應。</returns>
+    public OrganizationResponse Execute(OrganizationRequest request)
+    {
+        if (request.RequestName == "SetState") {
+            var reference = (EntityReference)request["EntityMoniker"];
+            Update(new Entity(reference.LogicalName, reference.Id) { ["statecode"] = request["State"], ["statuscode"] = request["Status"] });
+            return new OrganizationResponse();
+        }
+        throw new NotSupportedException("Unsupported synthetic request.");
+    }
+    /// <summary>本包不使用關聯操作，意外使用立即失敗。</summary>
+    /// <param name="entityName">邏輯名。</param><param name="entityId">ID。</param><param name="relationship">關聯。</param><param name="relatedEntities">目標。</param>
+    public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
+    /// <summary>本包不使用解除關聯，意外使用立即失敗。</summary>
+    /// <param name="entityName">邏輯名。</param><param name="entityId">ID。</param><param name="relationship">關聯。</param><param name="relatedEntities">目標。</param>
+    public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
+    /// <summary>假服務沒有 OS handle；計數只用來斷言單次連線租約確實執行 release。</summary>
+    public void Dispose() => DisposeCount++;
+    /// <summary>只記固定事件，不記秘密或業務資料。</summary>
+    /// <param name="name">編譯期種類。</param>
+    private void Record(string name) { BeforeWrite?.Invoke(); Writes.Add(name); }
+    /// <summary>建立欄位層級獨立候選，不把可變 SDK 選項或參照交回呼叫者。</summary>
+    /// <param name="entity">來源列。</param><returns>detached 副本。</returns>
+    private static Entity Copy(Entity entity)
+    {
+        var copy = new Entity(entity.LogicalName, entity.Id);
+        foreach (var pair in entity.Attributes) copy[pair.Key] = pair.Value switch { EntityReference r => new EntityReference(r.LogicalName, r.Id), OptionSetValue o => new OptionSetValue(o.Value), _ => pair.Value };
+        return copy;
+    }
+
+    /// <summary>只依精確欄位及 ID 判斷 AND／OR，未知比較不可假通過。</summary>
+    /// <param name="row">候選列。</param><param name="filter">查詢條件。</param><returns>是否符合。</returns>
+    private static bool Matches(Entity row, FilterExpression filter)
+    {
+        var values = filter.Conditions.Select(c => {
+            object? actual = row.Contains(c.AttributeName) ? row[c.AttributeName] : c.AttributeName == row.LogicalName + "id" ? row.Id : null;
+            if (actual is EntityReference reference) actual = reference.Id;
+            if (actual is OptionSetValue option) actual = option.Value;
+            return c.Operator switch { ConditionOperator.Equal => Equals(actual, c.Values[0]), ConditionOperator.In => c.Values.Contains(actual), _ => throw new NotSupportedException() };
+        }).Concat(filter.Filters.Select(x => Matches(row, x)));
+        return filter.FilterOperator == LogicalOperator.And ? values.All(x => x) : values.Any(x => x);
+    }
+}
+
+/// <summary>
+/// 單案例隔離的 UTC／單調假時鐘。短輪詢等待會快轉虛擬時間，長逾時計時器則保留至期限；
+/// 因此測試能在不等待三分鐘的情況下驗證真正輪詢、逾時取消及 timer 釋放。
+/// </summary>
+internal sealed class AdvancingClock : TimeProvider
+{
+    /// <summary>只供本 fixture 的 UTC 時點。</summary>
+    private DateTimeOffset now = new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
+    /// <summary>由本案例建立且尚未釋放的 timer；集合不跨 fixture 共用。</summary>
+    private readonly List<OwnedTimer> timers = new();
+    /// <summary>確認所有 delay 的取消登記與 timer 都釋放。</summary>
+    public int ActiveTimers { get; private set; }
+    /// <summary>記錄虛擬輪詢次數，避免 timeout 案例在第一次查詢前就被取消。</summary>
+    public int DelayCount { get; private set; }
+    /// <summary>回傳固定起點加上假延遲。</summary><returns>UTC 時點。</returns>
+    public override DateTimeOffset GetUtcNow() => now;
+    /// <summary>單調時間使用同一假時間，不讀硬體計時器。</summary><returns>tick 數。</returns>
+    public override long GetTimestamp() => now.Ticks;
+    /// <summary>固定 tick 頻率配合單調時間。</summary>
+    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
+    /// <summary>長期限先排程；短輪詢等待快轉兩分鐘並執行所有已到期的一次性回呼。</summary>
+    /// <param name="callback">延遲的單次完成回呼。</param><param name="state">延遲 owner 的狀態。</param><param name="dueTime">模擬延遲。</param><param name="period">不得重複。</param><returns>由等待者釋放的 timer。</returns>
+    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
+    {
+        var timer = new OwnedTimer(this, callback, state, dueTime);
+        timers.Add(timer);
+        ActiveTimers++;
+        if (dueTime <= TimeSpan.FromSeconds(1))
+        {
+            DelayCount++;
+            now += TimeSpan.FromMinutes(2);
+            foreach (var scheduled in timers.ToArray())
+            {
+                scheduled.FireIfDue(now);
+            }
+        }
+
+        return timer;
+    }
+
+    /// <summary>從案例 owner 移除已釋放 timer，讓生命週期斷言精確回到基線。</summary>
+    /// <param name="timer">被釋放的單次 timer。</param>
+    private void Release(OwnedTimer timer)
+    {
+        if (timers.Remove(timer))
+        {
+            ActiveTimers--;
+        }
+    }
+
+    /// <summary>單次假 timer 的唯一 owner，釋放後計數回到基線。</summary>
+    private sealed class OwnedTimer(AdvancingClock owner) : ITimer
+    {
+        /// <summary>期限到達時交由 BCL 延遲／取消來源的 callback。</summary>
+        private TimerCallback? callback;
+        /// <summary>只在 timer 活躍期間保留的 callback 狀態。</summary>
+        private object? state;
+        /// <summary>虛擬到期時間；觸發後設為 MaxValue，避免重複回呼。</summary>
+        private DateTimeOffset dueAt;
+        /// <summary>防止同步／非同步重複 Dispose 減少兩次計數。</summary>
+        private bool disposed;
+        /// <summary>建立一次性計時器；無限期限不會觸發 callback。</summary>
+        /// <param name="timerOwner">本案例的唯一 timer 集合 owner。</param>
+        /// <param name="timerCallback">期限到達時呼叫的委派。</param>
+        /// <param name="timerState">傳回 callback 的狀態。</param>
+        /// <param name="timerDueTime">虛擬到期時間。</param>
+        public OwnedTimer(AdvancingClock timerOwner, TimerCallback timerCallback, object? timerState, TimeSpan timerDueTime) : this(timerOwner)
+        {
+            callback = timerCallback;
+            state = timerState;
+            dueAt = timerDueTime == Timeout.InfiniteTimeSpan
+                ? DateTimeOffset.MaxValue
+                : timerOwner.GetUtcNow() + timerDueTime;
+        }
+
+        /// <summary>虛擬時間到達期限時執行一次回呼；timer 仍由原呼叫端負責 Dispose。</summary>
+        /// <param name="utcNow">目前的虛擬 UTC 時點。</param>
+        public void FireIfDue(DateTimeOffset utcNow)
+        {
+            if (disposed || dueAt > utcNow || callback is null)
+            {
+                return;
+            }
+
+            dueAt = DateTimeOffset.MaxValue;
+            callback(state);
+        }
+
+        /// <summary>測試只使用單次延遲，不接受改期。</summary>
+        /// <param name="dueTime">不使用。</param><param name="period">不使用。</param><returns>不支援改期。</returns>
+        public bool Change(TimeSpan dueTime, TimeSpan period) => false;
+        /// <summary>解除 timer owner，只釋放一次。</summary>
+        public void Dispose()
+        {
+            if (disposed) return;
+            disposed = true;
+            callback = null;
+            state = null;
+            owner.Release(this);
+        }
+        /// <summary>非同步釋放與同步使用相同路徑。</summary><returns>已完成。</returns>
+        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
+    }
+}

diff --git a/SpeechMessage.Dynamics.Deployment.Tests/SpeechMessage.Dynamics.Deployment.Tests.csproj b/SpeechMessage.Dynamics.Deployment.Tests/SpeechMessage.Dynamics.Deployment.Tests.csproj
new file mode 100644
index 00000000..b7e14629
--- /dev/null
+++ b/SpeechMessage.Dynamics.Deployment.Tests/SpeechMessage.Dynamics.Deployment.Tests.csproj
@@ -0,0 +1,21 @@
+<Project Sdk="Microsoft.NET.Sdk">
+  <PropertyGroup>
+    <TargetFramework>net10.0</TargetFramework>
+    <!-- 文件開關先於 targets 計算；測試的 CA1707 例外由根 targets 統一處理。 -->
+    <SpeechMessageStrictCode>true</SpeechMessageStrictCode>
+    <GenerateDocumentationFile>true</GenerateDocumentationFile>
+    <IsTestProject>true</IsTestProject>
+    <IsPackable>false</IsPackable>
+  </PropertyGroup>
+  <ItemGroup>
+    <ProjectReference Include="../SpeechMessage.Dynamics.Deployment/SpeechMessage.Dynamics.Deployment.csproj" />
+    <PackageReference Include="Microsoft.NET.Test.Sdk" />
+    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" />
+    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="all" />
+    <PackageReference Include="xunit" />
+    <PackageReference Include="xunit.runner.visualstudio" PrivateAssets="all" />
+    <PackageReference Include="coverlet.collector" PrivateAssets="all" />
+    <!-- 只用 net48 參考編譯器產生惰性測試 PE，不參考或執行正式 Plugin。 -->
+    <Content Include="$(NuGetPackageRoot)microsoft.netframework.referenceassemblies.net48/1.0.3/build/.NETFramework/v4.8/mscorlib.dll" Link="Framework/mscorlib.dll" CopyToOutputDirectory="PreserveNewest" />
+  </ItemGroup>
+</Project>

diff --git a/SpeechMessage.Dynamics.Deployment/AssemblyCatalog.cs b/SpeechMessage.Dynamics.Deployment/AssemblyCatalog.cs
new file mode 100644
index 00000000..7d3bb6d7
--- /dev/null
+++ b/SpeechMessage.Dynamics.Deployment/AssemblyCatalog.cs
@@ -0,0 +1,268 @@
+using System;
+using System.Collections.Generic;
+using System.IO;
+using System.Linq;
+using System.Reflection;
+using System.Reflection.Metadata;
+using System.Reflection.PortableExecutable;
+using System.Security.Cryptography;
+
+namespace SpeechMessage.Dynamics.Deployment;
+
+/// <summary>
+/// 以唯讀 PE 中繼資料檢查 Plugin 組件，不載入、反射執行或建立待部署程式碼。
+/// 每次只讀一個有大小上限的 DLL，並於方法結束釋放 PE reader 與檔案控制代碼。
+/// </summary>
+internal static class AssemblyCatalog
+{
+    private const long MaximumAssemblyBytes = 16L * 1024 * 1024;
+    private const string PluginInterfaceName = "Microsoft.Xrm.Sdk.IPlugin";
+
+    /// <summary>
+    /// 驗證 DLL 的組件身份、雜湊與全部公開 Plugin 型別，並回傳供一次 plan/apply 使用的描述。
+    /// 不把原始檔案路徑或 DLL 位元組寫入診斷訊息。
+    /// </summary>
+    /// <param name="path">本機建置輸出或測試生成的 PE 檔案。</param>
+    /// <param name="manifest">只含結構資料與秘密佔位的註冊清單。</param>
+    /// <returns>包含組件身份、內容雜湊與 Plugin 型別的 detached 描述。</returns>
+    /// <exception cref="ArgumentException">路徑空白或清單為 null。</exception>
+    /// <exception cref="InvalidDataException">檔案不是有效且相符的 Plugin PE，或缺少/多出 Plugin 型別。</exception>
+    public static AssemblyImage Read(string path, RegistrationManifest manifest)
+    {
+        ArgumentException.ThrowIfNullOrWhiteSpace(path);
+        ArgumentNullException.ThrowIfNull(manifest);
+        if (string.IsNullOrWhiteSpace(manifest.AssemblyName))
+        {
+            throw new InvalidDataException("註冊清單缺少組件名稱。");
+        }
+
+        byte[] content;
+        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
+        {
+            if (stream.Length <= 0 || stream.Length >= MaximumAssemblyBytes)
+            {
+                throw new InvalidDataException("Plugin 組件為空或超過允許大小。");
+            }
+
+            content = new byte[checked((int)stream.Length)];
+            stream.ReadExactly(content);
+            if (stream.ReadByte() != -1)
+            {
+                throw new InvalidDataException("Plugin 組件在讀取期間超過允許大小。");
+            }
+        }
+
+        using var metadataStream = new MemoryStream(content, writable: false);
+        using var peReader = new PEReader(metadataStream);
+        if (!peReader.HasMetadata)
+        {
+            throw new InvalidDataException("Plugin 組件沒有可讀取的中繼資料。");
+        }
+
+        var reader = peReader.GetMetadataReader();
+        if (!reader.IsAssembly)
+        {
+            throw new InvalidDataException("指定 PE 不是 .NET 組件。");
+        }
+
+        var assembly = reader.GetAssemblyDefinition();
+        var name = reader.GetString(assembly.Name);
+        if (!string.Equals(name, manifest.AssemblyName, StringComparison.Ordinal))
+        {
+            throw new InvalidDataException("Plugin 組件名稱與註冊清單不符。");
+        }
+
+        var pluginTypes = ReadPublicPluginTypes(reader);
+        var declaredTypes = manifest.Steps
+            .Select(step => step.Type)
+            .Distinct(StringComparer.Ordinal)
+            .OrderBy(typeName => typeName, StringComparer.Ordinal)
+            .ToArray();
+        var actualTypes = pluginTypes.OrderBy(typeName => typeName, StringComparer.Ordinal).ToArray();
+        if (declaredTypes.Length != manifest.Steps.Count ||
+            !declaredTypes.SequenceEqual(actualTypes, StringComparer.Ordinal))
+        {
+            throw new InvalidDataException("註冊清單與 DLL 的 Plugin 型別集合不一致。");
+        }
+
+        var publicKeyToken = ReadPublicKeyToken(reader, assembly);
+        var sha256 = Convert.ToHexString(SHA256.HashData(content));
+
+        return new AssemblyImage(
+            name,
+            assembly.Version.ToString(),
+            publicKeyToken,
+            Convert.ToBase64String(content),
+            sha256,
+            Array.AsReadOnly(actualTypes));
+    }
+
+    /// <summary>
+    /// 只回傳可註冊的公開、非 abstract 類別；介面與 abstract base class 不會被當作步驟候選。
+    /// </summary>
+    /// <param name="reader">由已驗證 PE 建立的唯讀中繼資料 reader。</param>
+    /// <returns>排序前的完整型別名稱集合，呼叫端負責以 ordinal 順序固定化。</returns>
+    private static List<string> ReadPublicPluginTypes(MetadataReader reader)
+    {
+        var result = new List<string>();
+        foreach (var handle in reader.TypeDefinitions)
+        {
+            var definition = reader.GetTypeDefinition(handle);
+            if (!IsPublicType(reader, handle) ||
+                (definition.Attributes & TypeAttributes.Interface) != 0 ||
+                (definition.Attributes & TypeAttributes.Abstract) != 0 ||
+                !ImplementsPluginInterface(reader, definition))
+            {
+                continue;
+            }
+
+            result.Add(GetFullTypeName(reader, handle));
+        }
+
+        return result;
+    }
+
+    /// <summary>直接辨識實作清單中的 IPlugin 介面，不使用 Assembly.Load 或執行 DLL 程式碼。</summary>
+    /// <param name="reader">型別中繼資料來源。</param>
+    /// <param name="definition">待檢查類別定義。</param>
+    /// <returns>類別明確實作 Microsoft.Xrm.Sdk.IPlugin 時為 true。</returns>
+    private static bool ImplementsPluginInterface(MetadataReader reader, TypeDefinition definition)
+    {
+        foreach (var implementationHandle in definition.GetInterfaceImplementations())
+        {
+            var implementation = reader.GetInterfaceImplementation(implementationHandle);
+            if (GetTypeName(reader, implementation.Interface) == PluginInterfaceName)
+            {
+                return true;
+            }
+        }
+
+        return ImplementsPluginInterface(reader, definition.BaseType, new HashSet<TypeDefinitionHandle>());
+    }
+
+    /// <summary>沿同一組件的基底類別追蹤 IPlugin，涵蓋繼承 PluginShellBase 的實際外掛。</summary>
+    /// <param name="reader">唯讀 metadata reader。</param>
+    /// <param name="baseType">目前類別的基底型別 handle。</param>
+    /// <param name="visited">防止惡意或毀損 PE 形成循環。</param>
+    /// <returns>基底鏈宣告或繼承 IPlugin 時為 true。</returns>
+    private static bool ImplementsPluginInterface(
+        MetadataReader reader,
+        EntityHandle baseType,
+        HashSet<TypeDefinitionHandle> visited)
+    {
+        if (baseType.Kind == HandleKind.TypeReference)
+        {
+            return GetTypeName(reader, baseType) == PluginInterfaceName;
+        }
+
+        if (baseType.Kind != HandleKind.TypeDefinition)
+        {
+            return false;
+        }
+
+        var handle = (TypeDefinitionHandle)baseType;
+        if (!visited.Add(handle))
+        {
+            return false;
+        }
+
+        var definition = reader.GetTypeDefinition(handle);
+        foreach (var implementationHandle in definition.GetInterfaceImplementations())
+        {
+            var implementation = reader.GetInterfaceImplementation(implementationHandle);
+            if (GetTypeName(reader, implementation.Interface) == PluginInterfaceName)
+            {
+                return true;
+            }
+        }
+
+        return ImplementsPluginInterface(reader, definition.BaseType, visited);
+    }
+
+    /// <summary>取得 TypeDefinition 或 TypeReference 的完整名稱；不解析外部組件或載入型別。</summary>
+    /// <param name="reader">型別中繼資料來源。</param>
+    /// <param name="handle">介面型別的中繼資料 handle。</param>
+    /// <returns>命名空間與型別名稱。</returns>
+    private static string GetTypeName(MetadataReader reader, EntityHandle handle)
+    {
+        return handle.Kind switch
+        {
+            HandleKind.TypeDefinition => GetFullTypeName(reader, (TypeDefinitionHandle)handle),
+            HandleKind.TypeReference => GetFullTypeName(reader, (TypeReferenceHandle)handle),
+            _ => string.Empty
+        };
+    }
+
+    /// <summary>組合 TypeDefinition 的巢狀名稱，不依賴執行期反射。</summary>
+    /// <param name="reader">型別中繼資料來源。</param>
+    /// <param name="handle">型別定義。</param>
+    /// <returns>與 CLR 完整型別名稱相符的名稱。</returns>
+    private static string GetFullTypeName(MetadataReader reader, TypeDefinitionHandle handle)
+    {
+        var definition = reader.GetTypeDefinition(handle);
+        var name = reader.GetString(definition.Name);
+        var declaringType = definition.GetDeclaringType();
+        if (!declaringType.IsNil)
+        {
+            return GetFullTypeName(reader, declaringType) + "+" + name;
+        }
+
+        var namespaceName = reader.GetString(definition.Namespace);
+        return namespaceName.Length == 0 ? name : namespaceName + "." + name;
+    }
+
+    /// <summary>組合 TypeReference 名稱以精確比對 SDK 的 IPlugin 身份。</summary>
+    /// <param name="reader">型別中繼資料來源。</param>
+    /// <param name="handle">外部型別參考。</param>
+    /// <returns>命名空間與型別名稱。</returns>
+    private static string GetFullTypeName(MetadataReader reader, TypeReferenceHandle handle)
+    {
+        var reference = reader.GetTypeReference(handle);
+        var name = reader.GetString(reference.Name);
+        var namespaceName = reader.GetString(reference.Namespace);
+        return namespaceName.Length == 0 ? name : namespaceName + "." + name;
+    }
+
+    /// <summary>支援公開頂層類別與其公開巢狀類別，且所有外層型別也必須公開。</summary>
+    /// <param name="reader">型別中繼資料來源。</param>
+    /// <param name="handle">待檢查型別。</param>
+    /// <returns>型別可由 Plugin Registration API 以完整名稱解析時為 true。</returns>
+    private static bool IsPublicType(MetadataReader reader, TypeDefinitionHandle handle)
+    {
+        var definition = reader.GetTypeDefinition(handle);
+        var visibility = definition.Attributes & TypeAttributes.VisibilityMask;
+        var declaringType = definition.GetDeclaringType();
+        return declaringType.IsNil
+            ? visibility == TypeAttributes.Public
+            : visibility == TypeAttributes.NestedPublic && IsPublicType(reader, declaringType);
+    }
+
+    /// <summary>透過 CLR 強式名稱 API 取得相容 PublicKeyToken；內容完整性另以 SHA-256 驗證。</summary>
+    /// <param name="reader">組件中繼資料來源。</param>
+    /// <param name="assembly">組件定義。</param>
+    /// <returns>小寫十六進位 token；unsigned 測試樣本回傳空字串。</returns>
+    private static string ReadPublicKeyToken(MetadataReader reader, AssemblyDefinition assembly)
+    {
+        if (assembly.PublicKey.IsNil)
+        {
+            return string.Empty;
+        }
+
+        var publicKey = reader.GetBlobBytes(assembly.PublicKey);
+        if (publicKey.Length == 0)
+        {
+            return string.Empty;
+        }
+
+        try
+        {
+            var identity = new AssemblyName();
+            identity.SetPublicKey(publicKey);
+            return Convert.ToHexString(identity.GetPublicKeyToken() ?? Array.Empty<byte>()).ToLowerInvariant();
+        }
+        catch (ArgumentException)
+        {
+            throw new InvalidDataException("Plugin 組件的強式名稱公鑰格式無效。");
+        }
+    }
+}

diff --git a/SpeechMessage.Dynamics.Deployment/Contracts.cs b/SpeechMessage.Dynamics.Deployment/Contracts.cs
new file mode 100644
index 00000000..c60562ee
--- /dev/null
+++ b/SpeechMessage.Dynamics.Deployment/Contracts.cs
@@ -0,0 +1,134 @@
+using System;
+using System.Collections.Generic;
+using System.Threading;
+using Microsoft.Xrm.Sdk;
+
+namespace SpeechMessage.Dynamics.Deployment;
+
+/// <summary>
+/// 單次讀取的註冊清單。檔案可進版控，因此設定欄位只允許固定結構與秘密佔位；
+/// 真實 token、收件者識別或帳密不得進入此模型。
+/// </summary>
+internal sealed record RegistrationManifest
+{
+    /// <summary>清單格式版本；不支援的值必須在任何組織讀寫前拒絕。</summary>
+    public int SchemaVersion { get; init; } = 1;
+    /// <summary>受此工具管理的唯一 Plugin 組件簡名，不接受路徑或任意組件。</summary>
+    public string AssemblyName { get; init; } = "SpeechMessage.Dynamics.Plugins";
+    /// <summary>步驟設定；每個穩定身份在驗證唯一後才可參與計畫。</summary>
+    public IReadOnlyList<StepRegistration> Steps { get; init; } = Array.Empty<StepRegistration>();
+}
+
+/// <summary>
+/// 一個受管理步驟的不可變範本。身份欄位決定唯一步驟，設定欄位只保留固定值或
+/// `${secret:name}` 佔位；僅 apply 寫入瞬間才解析秘密。
+/// </summary>
+internal sealed record StepRegistration
+{
+    /// <summary>Plugin 完整型別名稱，必須在待部署 PE 中實作 IPlugin。</summary>
+    public string Type { get; init; } = "SpeechMessage.Dynamics.Plugins.Diagnostics.DiagnosticsPingPlugin";
+    /// <summary>P0 骨架只允許 Create 訊息。</summary>
+    public string Message { get; init; } = "Create";
+    /// <summary>P0 骨架只允許 task 實體，避免觸碰會友或業務資料。</summary>
+    public string Entity { get; init; } = "task";
+    /// <summary>註冊階段；此骨架只允許 PostOperation。</summary>
+    public string Stage { get; init; } = "PostOperation";
+    /// <summary>執行模式；此骨架只允許 Asynchronous。</summary>
+    public string Mode { get; init; } = "Asynchronous";
+    /// <summary>穩定身份的一部分；不可依清單列位置推導。</summary>
+    public int Rank { get; init; } = 1;
+    /// <summary>Create 的篩選欄位；P0 必須為空，非空時拒絕而不靜默忽略。</summary>
+    public IReadOnlyList<string> FilteringAttributes { get; init; } = Array.Empty<string>();
+    /// <summary>前後映像；P0 不使用，非空時拒絕而不靜默遺漏。</summary>
+    public IReadOnlyList<string> Images { get; init; } = Array.Empty<string>();
+    /// <summary>只含固定模式、文化與秘密佔位的 JSON；不得含實際 userId。</summary>
+    public string UnsecureConfig { get; init; } = "{\"mode\":\"trace\",\"culture\":\"zh-TW\"}";
+    /// <summary>測試 Channel Token 佔位；trace 模式為空，從不序列化實際值。</summary>
+    public string SecureConfig { get; init; } = "";
+    /// <summary>每組織明確啟用設定；缺少或 false 一律維持停用。</summary>
+    public IReadOnlyDictionary<string, bool> EnabledByOrganization { get; init; } = new Dictionary<string, bool>(StringComparer.Ordinal);
+}
+
+/// <summary>
+/// 從有界 PE 讀出的單次組件描述。保留 DLL bytes 只供本次 apply，從不載入或執行 net48 程式碼，
+/// 也不將此物件放入 static、快取或跨命令狀態。
+/// </summary>
+internal sealed record AssemblyImage(string Name, string Version, string PublicKeyToken, string Content, string Sha256, IReadOnlyList<string> PluginTypes);
+
+/// <summary>計畫操作只存固定動詞與非敏感結構鍵，不保存設定值或憑證。</summary>
+internal sealed record DeploymentOperation(string Kind, string Key);
+
+/// <summary>
+/// 可人工審核的不可變計畫。SHA-256 涵蓋所有有效欄位，apply 前另驗雜湊、來源清單、
+/// DLL 與組織指紋；秘密只以名稱佔位呈現。
+/// </summary>
+internal sealed record DeploymentPlan(
+    string Organization,
+    DateTimeOffset CreatedUtc,
+    string Fingerprint,
+    string ManifestHash,
+    string AssemblyHash,
+    IReadOnlyList<DeploymentOperation> Operations,
+    string Sha256)
+{
+    /// <summary>拒絕不支援的 plan 格式，避免舊格式被當成完整核准內容。</summary>
+    public int SchemaVersion { get; init; } = 1;
+
+    /// <summary>供人工確認哪些 User Secrets 名稱會在寫入時解析，不含秘密值。</summary>
+    public IReadOnlyList<string> SecretPlaceholders { get; init; } = Array.Empty<string>();
+}
+
+/// <summary>
+/// 單一命令的連線建立邊界。測試以記憶體服務實作，正式實作每次建立新 client，
+/// 不得回傳靜態 client 或共用使用者／組織狀態的連線池。
+/// </summary>
+internal interface IOrganizationConnector
+{
+    /// <summary>只為已通過白名單驗證的組織建立本命令獨占服務。</summary>
+    /// <param name="organization">由命令解析器核准的固定組織名稱。</param>
+    /// <returns>命令結束時由唯一 owner Dispose 的連線租約。</returns>
+    OrganizationLease Connect(string organization);
+}
+
+/// <summary>
+/// 將服務與唯一釋放委派綁在一次命令生命週期內。Dispose 後立即解除服務與 delegate 參考，
+/// 不允許後續命令重用此 lease，避免連線、credential 或故障狀態跨命令殘留。
+/// </summary>
+internal sealed class OrganizationLease : IDisposable
+{
+    private IOrganizationService? service;
+    private Action? dispose;
+
+    /// <summary>本指令獨占的 Dataverse 服務；租約釋放後不再保留可用服務參考。</summary>
+    public IOrganizationService Service =>
+        Volatile.Read(ref service) ?? throw new ObjectDisposedException(nameof(OrganizationLease));
+
+    /// <summary>建立租約並明確指定唯一釋放動作。</summary>
+    /// <param name="service">只屬於本次命令的服務實例。</param>
+    /// <param name="dispose">負責釋放服務及其連線資源的委派。</param>
+    /// <exception cref="ArgumentNullException">服務或釋放委派為 null。</exception>
+    public OrganizationLease(IOrganizationService service, Action dispose)
+    {
+        this.service = service ?? throw new ArgumentNullException(nameof(service));
+        this.dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
+    }
+
+    /// <summary>冪等釋放連線並解除服務與 delegate 參考；釋放後不能再次取得此連線。</summary>
+    public void Dispose()
+    {
+        var release = Interlocked.Exchange(ref dispose, null);
+        if (release is null)
+        {
+            return;
+        }
+
+        try
+        {
+            release();
+        }
+        finally
+        {
+            Interlocked.Exchange(ref service, null);
+        }
+    }
+}

diff --git a/SpeechMessage.Dynamics.Deployment/DeploymentCli.cs b/SpeechMessage.Dynamics.Deployment/DeploymentCli.cs
new file mode 100644
index 00000000..9c18c411
--- /dev/null
+++ b/SpeechMessage.Dynamics.Deployment/DeploymentCli.cs
@@ -0,0 +1,379 @@
+using System;
+using System.Globalization;
+using System.IO;
+using System.Text.Json;
+using System.Threading;
+using System.Threading.Tasks;
+
+namespace SpeechMessage.Dynamics.Deployment;
+
+/// <summary>
+/// 以固定命令表執行一次佈署操作。此 parser 不回顯參數值、不接受密碼或任意輸出路徑；
+/// 每個有效命令最多取得一個 OrganizationLease，並在同步或非同步結束時立即釋放。
+/// </summary>
+internal static class DeploymentCli
+{
+    private const string AllowedOrganization = "goodshepherdback";
+    private const string OrganizationOption = "--org";
+    private const string PlanOption = "--plan";
+    private const string FromOption = "--from";
+    private const string ScenarioOption = "--scenario";
+    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
+
+    /// <summary>
+    /// 解析並執行單一命令。輸出目錄固定位於 artifacts/d365，測試可注入獨立暫存根；
+    /// connector 與共用錯誤回報皆為注入邊界，離線測試不會建立真實連線。
+    /// </summary>
+    /// <param name="args">命令列參數；不會記錄或回顯未知參數內容。</param>
+    /// <param name="connector">每次 Connect 建立獨立服務的組織 connector。</param>
+    /// <param name="output">安全摘要輸出 writer。</param>
+    /// <param name="report">接收原始例外的共用診斷回報委派。</param>
+    /// <param name="artifactRoot">僅供離線測試隔離輸出的根目錄；產品執行使用 repo 的 artifacts/d365。</param>
+    /// <param name="secretReader">只在 apply 寫入設定時讀取 User Secrets 的委派；不保留或輸出秘密值。</param>
+    /// <param name="writeLockTimeout">寫入命令等待同組織互斥鎖的上限；預設五分鐘。</param>
+    /// <param name="cancellationToken">由命令呼叫端提供的取消訊號；正常取消不送例外告警。</param>
+    /// <returns>成功為 0、caller 取消為 130，拒絕或失敗為其他非零值。</returns>
+    public static int Run(
+        string[] args,
+        IOrganizationConnector connector,
+        TextWriter output,
+        Action<Exception> report,
+        string? artifactRoot = null,
+        Func<string, string>? secretReader = null,
+        TimeSpan? writeLockTimeout = null,
+        CancellationToken cancellationToken = default)
+    {
+        ArgumentNullException.ThrowIfNull(args);
+        ArgumentNullException.ThrowIfNull(connector);
+        ArgumentNullException.ThrowIfNull(output);
+        ArgumentNullException.ThrowIfNull(report);
+
+        if (args.Length == 0 || (args.Length == 1 && args[0] is "--help" or "-h"))
+        {
+            WriteUsage(output);
+            return 0;
+        }
+
+        if (!TryParse(args, out var command))
+        {
+            output.WriteLine("Arguments are invalid or not supported.");
+            return 2;
+        }
+
+        if (!string.Equals(command.Organization, AllowedOrganization, StringComparison.OrdinalIgnoreCase))
+        {
+            output.WriteLine("Target organization is not in the P0 allowlist.");
+            return 2;
+        }
+
+        if (cancellationToken.IsCancellationRequested)
+        {
+            output.WriteLine("Command cancelled.");
+            return 130;
+        }
+
+        var repositoryRoot = FindRepositoryRoot();
+        var effectiveArtifactRoot = Path.GetFullPath(artifactRoot ??
+            Path.Combine(repositoryRoot, "artifacts", "d365"));
+        var organizationArtifactRoot = Path.GetFullPath(Path.Combine(effectiveArtifactRoot, AllowedOrganization));
+        try
+        {
+            if (command.InputPath is not null)
+            {
+                if (!IsContainedPath(organizationArtifactRoot, command.InputPath, out var fullInputPath))
+                {
+                    output.WriteLine("Plan/export path must be inside this organization's artifacts directory.");
+                    return 2;
+                }
+
+                command = command with { InputPath = fullInputPath };
+            }
+
+            using var writeLock = command.Name is "apply" or "rollback" or "smoke"
+                ? OrganizationWriteLock.Acquire(
+                    organizationArtifactRoot,
+                    writeLockTimeout ?? TimeSpan.FromMinutes(5),
+                    cancellationToken)
+                : null;
+            cancellationToken.ThrowIfCancellationRequested();
+            using var lease = connector.Connect(command.Organization);
+            var operationRoot = CreateOperationDirectory(organizationArtifactRoot);
+            var manifest = RegistrationManifestLoader.Load(repositoryRoot);
+            var assemblyPath = Path.Combine(repositoryRoot, "artifacts", "plugins", "SpeechMessage.Dynamics.Plugins.dll");
+            var engine = new DeploymentEngine(lease.Service, operationRoot, TimeProvider.System,
+                name => ResolveRequiredSecret(command.Organization, name, secretReader));
+
+            switch (command.Name)
+            {
+                case "plan":
+                {
+                    var image = AssemblyCatalog.Read(assemblyPath, manifest);
+                    var plan = engine.Plan(manifest, image, command.Organization);
+                    WritePlan(operationRoot, plan);
+                    output.WriteLine("Plan created.");
+                    return 0;
+                }
+                case "apply":
+                {
+                    var inputPath = command.InputPath!;
+                    var plan = ReadPlan(inputPath);
+                    var image = AssemblyCatalog.Read(assemblyPath, manifest);
+                    var export = engine.Apply(plan, manifest, image);
+                    output.WriteLine("Plan applied. Current state export: " + Path.GetFileName(export));
+                    return 0;
+                }
+                case "verify":
+                {
+                    var image = AssemblyCatalog.Read(assemblyPath, manifest);
+                    var plan = engine.Plan(manifest, image, command.Organization);
+                    if (plan.Operations.Count != 0)
+                    {
+                        output.WriteLine("Deployment state differs from the registration manifest.");
+                        return 1;
+                    }
+
+                    output.WriteLine("Deployment state matches the registration manifest.");
+                    return 0;
+                }
+                case "export":
+                {
+                    var path = Path.Combine(operationRoot, "export.json");
+                    engine.WriteExport(command.Organization, path);
+                    output.WriteLine("Metadata export created.");
+                    return 0;
+                }
+                case "rollback":
+                    engine.Rollback(command.InputPath!, manifest, command.Organization);
+                    output.WriteLine("Rollback completed.");
+                    return 0;
+                case "smoke":
+                {
+                    var succeeded = engine.SmokeAsync(command.Scenario!, cancellationToken)
+                        .GetAwaiter().GetResult();
+                    output.WriteLine(succeeded ? "Smoke scenario passed." : "Smoke scenario failed.");
+                    return succeeded ? 0 : 1;
+                }
+                default:
+                    output.WriteLine("Command is not supported.");
+                    return 2;
+            }
+        }
+        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
+        {
+            output.WriteLine("Command cancelled.");
+            return 130;
+        }
+        catch (Exception exception)
+        {
+            try
+            {
+                report(exception);
+            }
+            catch
+            {
+                WriteFixedError(output);
+            }
+
+            output.WriteLine("Command failed (" + exception.GetType().Name + "). Sensitive details are not shown.");
+            return 1;
+        }
+    }
+
+    /// <summary>解析唯一命令、允許選項與其值；未知參數的實際內容不會進入回應。</summary>
+    /// <param name="args">原始命令列參數。</param>
+    /// <param name="command">驗證後固定命令資料。</param>
+    /// <returns>語法、值及命令互斥條件皆正確時為 true。</returns>
+    private static bool TryParse(string[] args, out ParsedCommand command)
+    {
+        command = default;
+        var name = args[0];
+        if (name is not ("plan" or "apply" or "verify" or "export" or "rollback" or "smoke"))
+        {
+            return false;
+        }
+
+        string? organization = null;
+        string? inputPath = null;
+        string? scenario = null;
+        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
+        for (var index = 1; index < args.Length; index++)
+        {
+            var option = args[index];
+            if (option is not ("--org" or "--plan" or "--from" or "--scenario") ||
+                !seen.Add(option) ||
+                index + 1 >= args.Length ||
+                string.IsNullOrWhiteSpace(args[index + 1]) ||
+                args[index + 1].StartsWith("--", StringComparison.Ordinal))
+            {
+                return false;
+            }
+
+            var value = args[++index];
+            switch (option)
+            {
+                case OrganizationOption:
+                    organization = value;
+                    break;
+                case PlanOption:
+                    inputPath = value;
+                    break;
+                case FromOption:
+                    inputPath = value;
+                    break;
+                case ScenarioOption:
+                    scenario = value;
+                    break;
+            }
+        }
+
+        var valid = name switch
+        {
+            "plan" or "verify" or "export" => organization is not null && inputPath is null && scenario is null,
+            "apply" => organization is not null && HasExactlyTwoOptions(seen, OrganizationOption, PlanOption) && inputPath is not null && scenario is null,
+            "rollback" => organization is not null && HasExactlyTwoOptions(seen, OrganizationOption, FromOption) && inputPath is not null && scenario is null,
+            "smoke" => organization is not null && HasExactlyTwoOptions(seen, OrganizationOption, ScenarioOption) && scenario is not null && inputPath is null,
+            _ => false
+        };
+        if (!valid)
+        {
+            return false;
+        }
+
+        command = new ParsedCommand(name, organization!, inputPath, scenario);
+        return true;
+    }
+
+    /// <summary>以固定選項常值驗證集合，避免建立每次解析都配置的暫存陣列。</summary>
+    /// <param name="seen">本次命令已出現的選項集合。</param>
+    /// <param name="first">命令所需的第一個選項。</param>
+    /// <param name="second">命令所需的第二個選項。</param>
+    /// <returns>集合恰好包含兩個預期選項時為 true。</returns>
+    private static bool HasExactlyTwoOptions(System.Collections.Generic.HashSet<string> seen, string first, string second) =>
+        seen.Count == 2 && seen.Contains(first) && seen.Contains(second);
+
+    /// <summary>確認輸入 plan/export 完整位於指定組織的 artifacts 子樹，不接受路徑穿越。</summary>
+    /// <param name="root">本組織輸出根。</param>
+    /// <param name="path">命令列輸入的檔案位置。</param>
+    /// <param name="fullPath">正規化後的絕對位置。</param>
+    /// <returns>正規化路徑是根目錄內的檔案時為 true。</returns>
+    private static bool IsContainedPath(string root, string path, out string fullPath)
+    {
+        fullPath = Path.GetFullPath(path);
+        var relative = Path.GetRelativePath(root, fullPath);
+        return !Path.IsPathRooted(relative) &&
+            !string.Equals(relative, ".", StringComparison.Ordinal) &&
+            !relative.Equals("..", StringComparison.Ordinal) &&
+            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
+            !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
+    }
+
+    /// <summary>建立唯一 UTC 操作目錄；同時執行的命令取得不同目錄且不互相覆寫。</summary>
+    /// <param name="organizationRoot">白名單組織的 artifacts 根目錄。</param>
+    /// <returns>此命令獨占的輸出目錄。</returns>
+    private static string CreateOperationDirectory(string organizationRoot)
+    {
+        Directory.CreateDirectory(organizationRoot);
+        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture);
+        var path = Path.Combine(organizationRoot, timestamp + "-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
+        Directory.CreateDirectory(path);
+        return path;
+    }
+
+    /// <summary>讀取 schema 1 plan，將解析細節統一轉為固定錯誤而不回顯檔案內容。</summary>
+    /// <param name="path">已經過 artifacts 根目錄驗證的 JSON 路徑。</param>
+    /// <returns>待由引擎重新驗證雜湊與即時組織指紋的 plan。</returns>
+    private static DeploymentPlan ReadPlan(string path)
+    {
+        try
+        {
+            var file = new FileInfo(path);
+            if (!file.Exists || file.Length <= 0 || file.Length > 4 * 1024 * 1024)
+            {
+                throw new InvalidDataException("Plan 不存在、為空或超過允許大小。");
+            }
+
+            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
+            return JsonSerializer.Deserialize<DeploymentPlan>(stream, JsonOptions)
+                ?? throw new InvalidDataException("Plan 格式無效。");
+        }
+        catch (JsonException)
+        {
+            throw new InvalidDataException("Plan 格式無效。");
+        }
+    }
+
+    /// <summary>以 UTF-8 無 BOM 寫入 plan JSON 與固定 Markdown 摘要。</summary>
+    /// <param name="directory">本次 plan 專用輸出目錄。</param>
+    /// <param name="plan">只含摘要、hash 與秘密佔位名稱的 plan。</param>
+    private static void WritePlan(string directory, DeploymentPlan plan)
+    {
+        File.WriteAllText(Path.Combine(directory, "plan.json"),
+            JsonSerializer.Serialize(plan, JsonOptions), new System.Text.UTF8Encoding(false));
+        File.WriteAllText(Path.Combine(directory, "plan.md"),
+            DeploymentEngine.Markdown(plan), new System.Text.UTF8Encoding(false));
+    }
+
+    /// <summary>只在 apply 寫入設定時解析 placeholder；錯誤不包含要求的 secret 名稱或值。</summary>
+    /// <param name="organization">經白名單固定為 goodshepherdback 的組織別名。</param>
+    /// <param name="name">清單佔位符名稱。</param>
+    /// <param name="secretReader">由目前程序組態提供的 User Secrets 讀取委派。</param>
+    /// <returns>存在的 User Secrets 值。</returns>
+    private static string ResolveRequiredSecret(
+        string organization,
+        string name,
+        Func<string, string>? secretReader)
+    {
+        if (secretReader is null)
+        {
+            throw new InvalidOperationException("部署 User Secrets 缺少必要的安全設定。");
+        }
+
+        var value = secretReader(name);
+        return string.IsNullOrWhiteSpace(value)
+            ? throw new InvalidOperationException("部署 User Secrets 缺少必要的安全設定。")
+            : value;
+    }
+
+    /// <summary>從目前工作目錄向上找唯一 SpeechMessageProducts.sln 根目錄。</summary>
+    /// <returns>方案根目錄的絕對路徑。</returns>
+    private static string FindRepositoryRoot()
+    {
+        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
+        while (current is not null)
+        {
+            if (File.Exists(Path.Combine(current.FullName, "SpeechMessageProducts.sln")))
+            {
+                return current.FullName;
+            }
+
+            current = current.Parent;
+        }
+
+        throw new DirectoryNotFoundException("無法找到 SpeechMessageProducts.sln。");
+    }
+
+    /// <summary>輸出不帶動態路徑、參數或例外內容的固定錯誤，供回報回呼本身故障時使用。</summary>
+    /// <param name="output">命令摘要 writer。</param>
+    private static void WriteFixedError(TextWriter output)
+    {
+        try
+        {
+            output.WriteLine("Command failed. Diagnostic reporting was unavailable.");
+        }
+        catch
+        {
+        }
+    }
+
+    /// <summary>只列出命令與參數名稱，不含帳密、路徑或 secrets 值。</summary>
+    /// <param name="output">命令摘要 writer。</param>
+    private static void WriteUsage(TextWriter output)
+    {
+        output.WriteLine("Commands: plan --org <name> | apply --org <name> --plan <file> | verify --org <name>");
+        output.WriteLine("          export --org <name> | rollback --org <name> --from <file>");
+        output.WriteLine("          smoke --org <name> --scenario <p0-ping-trace|p0-ping-push>");
+    }
+
+    /// <summary>只包含已驗證的命令名稱與參數；不保留其他命令列文字。</summary>
+    private readonly record struct ParsedCommand(string Name, string Organization, string? InputPath, string? Scenario);
+}

diff --git a/SpeechMessage.Dynamics.Deployment/DeploymentConfiguration.cs b/SpeechMessage.Dynamics.Deployment/DeploymentConfiguration.cs
new file mode 100644
index 00000000..013d91c1
--- /dev/null
+++ b/SpeechMessage.Dynamics.Deployment/DeploymentConfiguration.cs
@@ -0,0 +1,23 @@
+using System;
+using System.Reflection;
+using Microsoft.Extensions.Configuration;
+
+namespace SpeechMessage.Dynamics.Deployment;
+
+/// <summary>
+/// 建立部署 CLI 唯一的組態來源。密碼與其他憑證只由此程序的 User Secrets provider 供應；
+/// 本型別不保存單次連線、解析後憑證或跨命令的 mutable 狀態。
+/// </summary>
+internal static class DeploymentConfiguration
+{
+    /// <summary>以固定的部署工具組件識別載入選用 User Secrets，缺少 secrets 檔仍可使用離線指令。</summary>
+    /// <param name="assembly">含固定 UserSecretsId 的部署工具組件。</param>
+    /// <returns>只含 User Secrets provider 的唯讀組態根。</returns>
+    public static IConfigurationRoot Create(Assembly assembly)
+    {
+        ArgumentNullException.ThrowIfNull(assembly);
+        return new ConfigurationBuilder()
+            .AddUserSecrets(assembly, optional: true)
+            .Build();
+    }
+}
```


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.
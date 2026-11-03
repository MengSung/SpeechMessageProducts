ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: integration-p0-stage0-resume-06

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P0.Worktree

## Request
# 階段0 第6批：Development 防護 P0-SEC／P0-ENV

你是唯讀外部審查者。本輪為「單模型（使用者指定 ClaudeOnly）」，不是雙模型或配額降級。只審查，不改檔、不提交、不執行產品／工具／測試，不建立任何網路或 D365／LINE 連線。
審查重點：跨使用者／跨教會隔離、資源釋放、機密外洩、例外責任（設計10.5／10.9）、net48/net10行為一致、守門假綠燈。所有真實連線驗收仍由使用者負責。
機密欄位已遮罩；遮罩值不可用來推斷實際設定正確性。不得讀取 appsettings*、User Secrets、環境變數、Logs、金鑰／授權檔，或以 git show 取未遮罩版本。只以所附安全差異審查；如需上下文，可只查明確的非機密程式來源。不要開啟 .ccg/dual-model-runs 的其他歷史輸出或 research 原始紀錄。
輸出只列可驗證的 Critical／High／Medium／Low，包含檔案、行號、觸發條件、具體影響及最小修法；測試不足須說明可重現的缺口，不把純猜測當發現。沒有問題請明寫。不得聲稱實跑測試。
已核定例外：嚴格 .Tests 測試專案僅 CA1707 放寬；未實作 API 的 CA1716 可改語意近名；Core 不通知，Plugin 僅追蹤，最終告警在 net10 重試用盡／階段5；正式金鑰只程序環境且不讀內容；本階段不存在業務 Rejected。小組頁面由使用者確認恢復，圖表未逐項驗證，之前審查兩次為逾時。

範圍：73b6e6cf409daa7559fd3c11d482e3efc542c0d7..ae29cb40aa5c395f83c169068d7ca40e7094c509
安全差異 SHA256：7b353856af4a01e9d46a8b2a9e4a71523436c7b1a0b31b6c3a753a1c1674bb77
同批其他分份須一併審查後才算完成，本份不可代表全批。
專項：環境先 DOTNET_ENVIRONMENT 再 ASPNETCORE_ENVIRONMENT，預設 Production；Development overlay 與 User Secrets 順序；基底正式 LINE token 被清空、DevTest token 只 secrets；goodshepherdback 完整 https Organization.svc 與 Package01FeeReads；使用者明確允許 Development 檔存帳密，不可把這項核定當成未授權變更。

```diff
diff --git a/.trellis/spec/backend/development-configuration-safety.md b/.trellis/spec/backend/development-configuration-safety.md
new file mode 100644
index 00000000..d891be5c
--- /dev/null
+++ b/.trellis/spec/backend/development-configuration-safety.md
@@ -0,0 +1,31 @@
+# ChurchReport Development Configuration Safety
+
+The active implementation plan is `文件/規劃設計書/實作計畫-3.3/`. Version 3.2 and completed WP reports are historical snapshots.
+
+- Resolve DOTNET_ENVIRONMENT before ASPNETCORE_ENVIRONMENT, then default to Production. Development matching is ordinal case-insensitive. Never change process environment or current directory in unit tests.
+- `ChurchReportDevelopmentConfiguration` appends optional Development JSON and optional host User Secrets only in Development. Other environments add zero providers and preserve every existing key and value. Preserve each legacy base JSON optional/reload setting and static/Lazy ownership.
+- The shared LineMessagingProcessor default path can append Development JSON but cannot reference ChurchReport or read its secrets. Keep the original environment-variable provider precedence.
+- Validate the merged host configuration before constructing exception LINE senders or CRM services. Reject reused baseline tokens, unsafe default aliases, CRM outside goodshepherdback, and enabled Embedded endpoints outside its host. Failures contain key names and fixed reasons, never values.
+- Do not retain baseline configuration or secrets in new static state. Dispose baseline streams and roots within the guard; new providers do not enable reload watchers. Existing deployment snapshots never contain request/user/tenant mutable state.
+- Tests use synthetic tokens and isolated temporary directories. Passing the test assembly without a UserSecretsId prevents real secrets access. Production compatibility tests assert both provider count and the complete key/value sequence; source architecture tests cover all 11+1 read points and startup ordering.
+- D365 and LINE connections are prohibited before the user confirms test-organization cleanup. After cleanup, Codex can execute only the D24 read-only commands. Write commands remain user-operated.
+
+## User-approved ChurchReport credential storage exception
+
+依使用者 2026-11-03 核定，Development 檔可存放帳密；User Secrets 仍可覆寫。
+This supersedes the former P0-ENV requirement that Domain, Username and Password
+be blank. Keep user commit 01f3c03e and the unchanged provider order: base JSON,
+Development JSON, then User Secrets. Test only credential-key presence; replace
+values with synthetic data before writing fixtures and use boolean assertions
+so failures never disclose real values. Exercise the actual User Secrets
+provider with a unique fixture identity and deterministically delete its files.
+
+The Development organization remains goodshepherdback. Its ServerUrl must use
+HTTPS, the exact goodshepherdback.speechmessage.com.tw host, and the complete
+/XRMServices/2011/Organization.svc path. Preserve all existing LINE-token
+masking, DevTest-secret-only and Package01FeeReads guards.
+
+This exception applies only to ChurchReport settings. Deployment credentials
+remain User-Secrets-only in P0-12; command-line passwords are forbidden.
+Base appsettings.json cleanup remains in P2/P4. Never copy credential values
+into reports, evidence, test output or commit messages.

diff --git a/ChurchReport.MemberInfo.Tests/Contracts/DevelopmentOrganizationSettingsTests.cs b/ChurchReport.MemberInfo.Tests/Contracts/DevelopmentOrganizationSettingsTests.cs
new file mode 100644
index 00000000..dd598a17
--- /dev/null
+++ b/ChurchReport.MemberInfo.Tests/Contracts/DevelopmentOrganizationSettingsTests.cs
@@ -0,0 +1,217 @@
+using ChurchReport.Configuration;
+using FluentAssertions;
+using Microsoft.Extensions.Configuration;
+using Microsoft.Extensions.Configuration.UserSecrets;
+using System.Reflection;
+using System.Reflection.Emit;
+using System.Text.Json.Nodes;
+using Xunit;
+
+namespace ChurchReport.MemberInfo.Tests.Contracts;
+
+/// <summary>
+/// 以實際 Development 檔的鍵與非機密網址驗證測試組織、設定優先順序與傳輸路徑防護；不建立 Host 或外部 client。
+/// 複製到測試目錄前先將帳密值換成固定假值，避免真實憑證落入暫存證據；斷言只回報布林結果。
+/// 每案獨立擁有組態根、假設定與假 User Secrets，先釋放組態，再於 Dispose 清除所有自建檔案。
+/// </summary>
+public sealed class DevelopmentOrganizationSettingsTests
+{
+    /// <summary>基底仍指向舊組織時，本案例應失敗；Development 合併後組織與 URI 主機必須精確符合唯一測試目標。</summary>
+    [Fact]
+    public void Development_CrmOrganizationAndServer_AreGoodshepherdback()
+    {
+        using var settings = LoadDevelopment();
+        var configuration = settings.Configuration;
+        configuration["CrmConnection:Organization"].Should().Be("goodshepherdback");
+        Uri.TryCreate(configuration["CrmConnection:ServerUrl"], UriKind.Absolute, out var uri).Should().BeTrue();
+        uri!.Host.Should().Be("goodshepherdback.speechmessage.com.tw");
+    }
+
+    /// <summary>
+    /// 捕捉先前僅驗主機而漏掉服務路徑的回歸；對實際 Development 的非機密網址驗證 HTTPS、精確主機及 Organization.svc 路徑。
+    /// 網址錯誤時只輸出失敗的布林條件，不建立連線，也不將設定值放進錯誤訊息。
+    /// </summary>
+    [Fact]
+    public void Development_CrmServerUrl_UsesHttpsAndFullOrganizationServicePath()
+    {
+        using var settings = LoadDevelopment();
+        Uri.TryCreate(settings.Configuration["CrmConnection:ServerUrl"], UriKind.Absolute, out var uri).Should().BeTrue();
+        string.Equals(uri!.Scheme, "https", StringComparison.Ordinal).Should().BeTrue();
+        string.Equals(uri.Host, "goodshepherdback.speechmessage.com.tw", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
+        string.Equals(uri.AbsolutePath, "/XRMServices/2011/Organization.svc", StringComparison.Ordinal).Should().BeTrue();
+    }
+
+    /// <summary>
+    /// 用唯一識別碼的假 User Secrets 驗證實際 Development 擴充方法的 provider 順序；若 secrets 被漏載或遭 JSON 蓋回，本案失敗。
+    /// 所有帳密均為固定假資料，比較先轉成布林，不讀既有使用者 secrets；組態釋放後才清除專有 secrets 目錄。
+    /// </summary>
+    [Fact]
+    public void Development_UserSecrets_OverrideDevelopmentCredentialKeys()
+    {
+        using var secrets = new SecretsScope();
+        using var settings = new SettingsScope(ProductRoot(), secrets.Assembly);
+        foreach (var key in new[] { "Domain", "Username", "Password" })
+            string.Equals(settings.Configuration["CrmConnection:" + key], "SECRETS-FAKE", StringComparison.Ordinal)
+                .Should().BeTrue("User Secrets 必須在 Development JSON 之後載入");
+    }
+
+    /// <summary>
+    /// 依使用者 2026-11-03 核定，Development 可以存放帳密，故只檢查三個設定鍵存在，不限制值是否空白。
+    /// 若設定鍵被刪除，本案必須失敗；不比對或輸出真實值，其他組織、LINE 與 Web API 防護由原案例繼續守門。
+    /// </summary>
+    [Fact]
+    public void Development_Credentials_KeysExistWithoutRestrictingStorage()
+    {
+        using var settings = LoadDevelopment();
+        var configuration = settings.Configuration;
+        foreach (var key in new[] { "Domain", "Username", "Password" })
+            configuration.GetSection("CrmConnection").GetChildren().Any(section =>
+                string.Equals(section.Key, key, StringComparison.Ordinal)).Should().BeTrue("只確認設定鍵存在，不檢查帳密實值");
+    }
+
+    /// <summary>未停用前旗標為 true，必須失敗；ADFS 尚未驗證時只允許保留關閉的 Web API 路徑。</summary>
+    [Fact]
+    public void Development_EmbeddedFeeReads_AreDisabledBeforeAdfsValidation()
+    {
+        using var settings = LoadDevelopment();
+        var configuration = settings.Configuration;
+        configuration.GetValue<bool>("DynamicsAccess:Package01FeeReadsEnabled").Should().BeFalse();
+    }
+
+    /// <summary>LINE 別名與 token 遮蔽延續 P0-SEC；本包新增的 QPAY 組織未覆寫時會失敗，避免付款路徑指回舊組織。</summary>
+    [Fact]
+    public void Development_LineAliasesAndQpayOrganization_StayWithinTestBoundary()
+    {
+        using var settings = LoadDevelopment();
+        var configuration = settings.Configuration;
+        configuration["LineMessaging:DefaultOrganization"].Should().Be("DevTest");
+        string.IsNullOrEmpty(configuration["LineMessaging:Jesus:ChannelAccessToken"]).Should().BeTrue();
+        string.IsNullOrEmpty(configuration["LineMessaging:JesusBack:ChannelAccessToken"]).Should().BeTrue();
+        configuration["QPAY_ORGANIZATION"].Should().Be("goodshepherdback");
+    }
+
+    /// <summary>P0-SEC 後舊 CRM 設定預期被擋；本包完成後同一份實際檔案合併結果必須通過該守門，且不需 secrets 或網路。</summary>
+    [Fact]
+    public void Development_ActualMergedSettings_PassExistingSafetyGuard()
+    {
+        using var settings = LoadDevelopment();
+        var configuration = settings.Configuration;
+        Action action = () => ChurchReportDevelopmentConfiguration.ValidateDevelopmentConfiguration(settings.Root, "Development", configuration);
+        action.Should().NotThrow();
+    }
+
+    /// <summary>建立每案例獨立的假基底及去除帳密實值的 Development 複本；不讀真實基底機密或既有使用者 secrets。</summary>
+    /// <returns>由測試先釋放組態根，再清除暫存目錄的唯一 owner。</returns>
+    private static SettingsScope LoadDevelopment() => new(ProductRoot());
+
+    /// <summary>擁有單案組態與暫存檔；Development 保留實際鍵與網址，帳密實值先換成假值再寫入測試目錄。</summary>
+    private sealed class SettingsScope : IDisposable
+    {
+        /// <summary>單案獨立假設定根，不承載或共享使用者資料。</summary>
+        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "p0-env-" + Guid.NewGuid().ToString("N"));
+
+        /// <summary>只包含假基底及 Development 覆寫的根；本 scope 唯一擁有並確定釋放。</summary>
+        internal ConfigurationRoot Configuration { get; }
+
+        /// <summary>注入假正式設定及清除帳密實值的 Development 複本；所有來源停用 reload，secrets 僅能來自明確傳入的測試組件。</summary>
+        /// <param name="productRoot">當前 worktree 的產品根；實際憑證只在解析期間存在，寫檔前一律以假值取代。</param>
+        /// <param name="secretsAssembly">測試專屬 secrets 組件；省略時使用沒有 UserSecretsId 的測試組件，不觸碰真實 secrets。</param>
+        internal SettingsScope(string productRoot, Assembly? secretsAssembly = null)
+        {
+            try
+            {
+                Directory.CreateDirectory(Root);
+                File.WriteAllText(Path.Combine(Root, "appsettings.json"), """
+                    {"CrmConnection":{"Organization":"sunnyvalechback","ServerUrl":"https://sunnyvalechback.speechmessage.com.tw","Domain":"[REDACTED]","Username":"[REDACTED]","Password":"[REDACTED]"},"LineMessaging":{"DefaultOrganization":"Jesus","Jesus":{"ChannelAccessToken":"[REDACTED]"},"JesusBack":{"ChannelAccessToken":"[REDACTED]"}},"QPAY_ORGANIZATION":"sunnyvalechback","DynamicsAccess":{"Package01FeeReadsEnabled":true,"Embedded":{"OrganizationWebApiBaseUri":"https://sunnyvalechback.speechmessage.com.tw/api/data/v9.1","ResourceUri":"https://sunnyvalechback.speechmessage.com.tw"}}}
+                    """, new System.Text.UTF8Encoding(false));
+                var development = JsonNode.Parse(File.ReadAllText(Path.Combine(productRoot, "appsettings.Development.json")),
+                    documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject();
+                var crm = development["CrmConnection"]!.AsObject();
+                foreach (var key in new[] { "Domain", "Username", "Password" })
+                {
+                    // 只保留鍵是否存在；不以真實值作比較，也不把它複製到暫存目錄或斷言輸出。
+                    if (crm.ContainsKey(key)) crm[key] = "JSON-FAKE";
+                }
+                File.WriteAllText(Path.Combine(Root, "appsettings.Development.json"), development.ToJsonString(),
+                    new System.Text.UTF8Encoding(false));
+                Configuration = (ConfigurationRoot)new ConfigurationBuilder().SetBasePath(Root).AddJsonFile("appsettings.json", false, false)
+                .AddChurchReportDevelopmentOverrides(Root, "Development", secretsAssembly ?? typeof(DevelopmentOrganizationSettingsTests).Assembly).Build();
+            }
+            catch
+            {
+                // 建構中途失敗尚未交出 owner，仍由本 scope 清除已建立的假設定目錄。
+                Dispose();
+                throw;
+            }
+        }
+
+        /// <summary>先關組態根，再確認絕對路徑位於暫存根及專有前綴內才刪除，避免 watcher、handle 或跨案例資料殘留。</summary>
+        public void Dispose()
+        {
+            Configuration?.Dispose();
+            var actual = Path.GetFullPath(Root);
+            var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
+            if (!actual.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(actual).StartsWith("p0-env-", StringComparison.Ordinal))
+                throw new InvalidOperationException("拒絕清理 scope 以外的目錄。");
+            if (Directory.Exists(actual)) Directory.Delete(actual, recursive: true);
+        }
+    }
+
+    /// <summary>
+    /// 以不可預測識別碼隔離一份假 User Secrets；不列舉、不覆寫既有 secrets，也不改程序環境。
+    /// 僅保留固定假值及可回收動態組件，Dispose 時確認路徑仍位於自己擁有的 secrets 子目錄後刪除。
+    /// </summary>
+    private sealed class SecretsScope : IDisposable
+    {
+        private readonly string _root;
+        private readonly string _id = "speechmessage-p0-env-test-" + Guid.NewGuid().ToString("N");
+
+        /// <summary>本案專有的 UserSecretsId 所在組件；不使用產品組件，因此不可能載入既有產品 secrets。</summary>
+        internal Assembly Assembly { get; }
+
+        /// <summary>建立僅含固定假資料的秘密檔與可回收的組件識別碼，讓真實 AddUserSecrets provider 處理載入與優先順序。</summary>
+        internal SecretsScope()
+        {
+            _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets", _id);
+            try
+            {
+                Directory.CreateDirectory(_root);
+                File.WriteAllText(Path.Combine(_root, "secrets.json"),
+                    """{"CrmConnection":{"Domain":"[REDACTED]","Username":"[REDACTED]","Password":"[REDACTED]"}}""",
+                    new System.Text.UTF8Encoding(false));
+                var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(_id), AssemblyBuilderAccess.RunAndCollect);
+                assembly.SetCustomAttribute(new CustomAttributeBuilder(
+                    typeof(UserSecretsIdAttribute).GetConstructor(new[] { typeof(string) })!, new object[] { _id }));
+                Assembly = assembly;
+            }
+            catch
+            {
+                Dispose();
+                throw;
+            }
+        }
+
+        /// <summary>組態根已由外層先釋放；只刪除本案建立、且仍位於 UserSecrets 根目錄的唯一識別碼子目錄。</summary>
+        public void Dispose()
+        {
+            var parent = Path.GetFullPath(Path.Combine(
+                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets"))
+                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
+            var actual = Path.GetFullPath(_root);
+            if (!actual.StartsWith(parent, StringComparison.OrdinalIgnoreCase) ||
+                !string.Equals(Path.GetFileName(actual), _id, StringComparison.Ordinal))
+                throw new InvalidOperationException("拒絕清理測試 secrets 以外的目錄。");
+            if (Directory.Exists(actual)) Directory.Delete(actual, recursive: true);
+        }
+    }
+
+    /// <summary>找到最近方案即停止向上，只讀取當前 worktree 的產品設定，不會退回父 worktree。</summary>
+    /// <returns>當前方案內 ChurchReport 的絕對根目錄。</returns>
+    private static string ProductRoot()
+    {
+        var root = new DirectoryInfo(AppContext.BaseDirectory);
+        while (root != null && !File.Exists(Path.Combine(root.FullName, "SpeechMessageProducts.sln"))) root = root.Parent;
+        if (root == null) throw new DirectoryNotFoundException("找不到測試所屬方案。");
+        return Path.Combine(root.FullName, "SpeechMessageProducts.ChurchReport");
+    }
+}

diff --git a/ChurchReport.MemberInfo.Tests/Contracts/DevelopmentSafetyTests.cs b/ChurchReport.MemberInfo.Tests/Contracts/DevelopmentSafetyTests.cs
new file mode 100644
index 00000000..50cd85f5
--- /dev/null
+++ b/ChurchReport.MemberInfo.Tests/Contracts/DevelopmentSafetyTests.cs
@@ -0,0 +1,220 @@
+using System.Reflection;
+using System.Text;
+using ChurchReport.Configuration;
+using FluentAssertions;
+using Microsoft.Extensions.Configuration;
+using Microsoft.Extensions.Configuration.UserSecrets;
+using Xunit;
+
+namespace ChurchReport.MemberInfo.Tests.Contracts;
+
+/// <summary>
+/// 保護 Development 不取用正式 token、CRM 只連指定測試組織與 Production 逐鍵相容的契約。
+/// 所有輸入均為操作區域內假設定；不啟動 Host、不呼叫 CRM／LINE、不改程序環境或目前目錄。
+/// 暫存檔、組態根與串流均由個別測試確定釋放，並行案例不共用可變狀態。
+/// </summary>
+public sealed class DevelopmentSafetyTests
+{
+    /// <summary>注入兩種 Development 大小寫；斷言每個既有別名都遮蔽基底假 token，且預設選到測試別名。</summary>
+    /// <param name="environment">大小寫不同的開發環境名稱，用來注入相同隔離契約。</param>
+    [Theory]
+    [InlineData("Development")]
+    [InlineData("development")]
+    public void DevelopmentLayer_Development_HidesEveryBaseToken(string environment)
+    {
+        using var fixture = new SettingsFixture();
+        var builder = fixture.BaseBuilder();
+        builder.AddChurchReportDevelopmentOverrides(fixture.Root, environment, typeof(DevelopmentSafetyTests).Assembly);
+        using var configuration = (ConfigurationRoot)builder.Build();
+        configuration["LineMessaging:DefaultOrganization"].Should().Be("DevTest");
+        configuration["LineMessaging:Jesus:ChannelAccessToken"].Should().BeEmpty();
+        configuration["LineMessaging:JesusBack:ChannelAccessToken"].Should().BeEmpty();
+        configuration.AsEnumerable().Should().NotContain(p => p.Value == "PROD-FAKE" || p.Value == "PROD-B-FAKE");
+    }
+
+    /// <summary>注入 Production、Staging 與缺省環境；斷言零新增 provider 且所有鍵和值與原基底相同。</summary>
+    /// <param name="environment">正式、其他或缺省環境；均不得新增開發來源。</param>
+    [Theory]
+    [InlineData("Production")]
+    [InlineData("Staging")]
+    [InlineData(null)]
+    public void DevelopmentLayer_NonDevelopment_PreservesProvidersAndEveryValue(string? environment)
+    {
+        using var fixture = new SettingsFixture();
+        var builder = fixture.BaseBuilder();
+        var sourceCount = builder.Sources.Count;
+        using var baseline = (ConfigurationRoot)fixture.BaseBuilder().Build();
+        builder.AddChurchReportDevelopmentOverrides(fixture.Root, environment!, typeof(DevelopmentSafetyTests).Assembly);
+        builder.Sources.Count.Should().Be(sourceCount);
+        using var effective = (ConfigurationRoot)builder.Build();
+        effective.AsEnumerable().Should().BeEquivalentTo(baseline.AsEnumerable());
+    }
+
+    /// <summary>注入衝突與缺省名稱，斷言 DOTNET 優先、其次 ASPNETCORE，皆未指定才採 Production。</summary>
+    /// <param name="dotnet">假 DOTNET 環境名稱，不寫入程序環境。</param>
+    /// <param name="aspnetcore">假 ASPNETCORE 名稱，不寫入程序環境。</param>
+    /// <param name="expected">獨立由 Host 規則決定的字面期望值。</param>
+    [Theory]
+    [InlineData("Production", "Development", "Production")]
+    [InlineData("Development", "Production", "Development")]
+    [InlineData(null, "Development", "Development")]
+    [InlineData(null, null, "Production")]
+    public void Environment_ConflictingInputs_UsesHostPrecedence(string? dotnet, string? aspnetcore, string expected)
+        => ChurchReportDevelopmentConfiguration.ResolveEnvironmentName(dotnet!, aspnetcore!).Should().Be(expected);
+
+    /// <summary>移除測試 token 時，守門仍允許安全設定，但各 token 值保持空白，不能退回正式別名。</summary>
+    [Fact]
+    public void DevelopmentGuard_SafeSettingsWithoutToken_PassesWithoutFallback()
+    {
+        using var fixture = new SettingsFixture();
+        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(SafeValues()).Build();
+        Action action = () => ChurchReportDevelopmentConfiguration.ValidateDevelopmentConfiguration(fixture.Root, "Development", configuration);
+        action.Should().NotThrow();
+        configuration["LineMessaging:DevTest:ChannelAccessToken"].Should().BeNullOrEmpty();
+    }
+
+    /// <summary>逐一注入正式 token、錯誤預設別名、CRM 與 Embedded 主機；拒絕時只回報鍵名，不洩露任何假機密。</summary>
+    /// <param name="key">本案例要破壞的設定鍵。</param>
+    /// <param name="value">固定假值，絕非真實 token、帳密或個資。</param>
+    [Theory]
+    [InlineData("LineMessaging:Jesus:ChannelAccessToken", "PROD-FAKE")]
+    [InlineData("LineMessaging:DefaultOrganization", "Jesus")]
+    [InlineData("LineMessaging:DefaultOrganization", "")]
+    [InlineData("CrmConnection:Organization", "sunnyvalechback")]
+    [InlineData("CrmConnection:ServerUrl", "https://sunnyvalechback.speechmessage.com.tw")]
+    [InlineData("DynamicsAccess:Embedded:OrganizationWebApiBaseUri", "https://sunnyvalechback.speechmessage.com.tw/api/data/v9.1")]
+    [InlineData("DynamicsAccess:Embedded:ResourceUri", "https://sunnyvalechback.speechmessage.com.tw")]
+    [InlineData("DynamicsAccess:Package01FeeReadsEnabled", "invalid")]
+    [InlineData("LineMessaging:DevTest:ChannelAccessToken", "PROD-FAKE")]
+    [InlineData("LINE_CHANNEL_ACCESS_TOKEN", "PROD-FAKE")]
+    public void DevelopmentGuard_UnsafeSetting_RejectsWithoutSecret(string key, string value)
+    {
+        using var fixture = new SettingsFixture();
+        var values = SafeValues();
+        values[key] = value;
+        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(values).Build();
+        Action action = () => ChurchReportDevelopmentConfiguration.ValidateDevelopmentConfiguration(fixture.Root, "Development", configuration);
+        var exception = action.Should().Throw<InvalidOperationException>().Which;
+        exception.Message.Should().Contain(key).And.NotContain("PROD-FAKE").And.NotContain("PROD-B-FAKE").And.NotContain("DEV-FAKE");
+    }
+
+    /// <summary>Production 與未設定環境即使有不安全鍵和不存在的基底路徑，也必須完全略過 Development 守門。</summary>
+    /// <param name="environment">不啟用開發守門的環境或 null。</param>
+    [Theory]
+    [InlineData("Production")]
+    [InlineData(null)]
+    public void DevelopmentGuard_NonDevelopment_DoesNotInspectOrReject(string? environment)
+    {
+        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection().Build();
+        Action action = () => ChurchReportDevelopmentConfiguration.ValidateDevelopmentConfiguration("not-a-real-base-path", environment!, configuration);
+        action.Should().NotThrow();
+    }
+
+    /// <summary>以組件中繼資料驗證 UserSecretsId 真的存在；不開啟任何 secrets 檔案或使用者設定。</summary>
+    [Fact]
+    public void HostAssembly_UserSecretsId_IsAvailableToDevelopmentLoader()
+    {
+        var id = typeof(global::ChurchReport.Program).Assembly.GetCustomAttribute<UserSecretsIdAttribute>();
+        id.Should().NotBeNull();
+        id!.UserSecretsId.Should().NotBeNullOrWhiteSpace();
+    }
+
+    /// <summary>掃描本 worktree 的來源；既有 11 個 LINE 設定建構器與共用預設路徑均須接入覆寫，不得漏接或讀父 worktree。</summary>
+    [Fact]
+    public void SourceBuilders_AllLineConfigurationPaths_UseDevelopmentLayer()
+    {
+        var root = RepositoryRoot();
+        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
+        var files = Directory.EnumerateFiles(Path.Combine(root, "SpeechMessageProducts.ChurchReport"), "*.cs", options)
+            .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj"));
+        var count = 0;
+        foreach (var file in files)
+        {
+            var source = File.ReadAllText(file);
+            if (!source.Contains("new ConfigurationBuilder()", StringComparison.Ordinal) || !source.Contains("AddJsonFile(" + (char)34 + "appsettings.json" + (char)34, StringComparison.Ordinal)) continue;
+            // 這個已盤點的付款除錯器只讀日誌開關，維持原範圍；若日後讀 LINE，必須進入防護而不能繼續豁免。
+            if (Path.GetFileName(file) == "DonationPaymentDebugLogger.cs")
+            {
+                source.Should().NotContain("LineMessaging:").And.NotContain("LineMessagingClient");
+                continue;
+            }
+            count++;
+            source.Should().Contain(".AddChurchReportDevelopmentOverrides(", "每個 LINE 基底檔讀取點都必須啟用相同的環境防護");
+        }
+        count.Should().Be(11);
+        var library = File.ReadAllText(Path.Combine(root, "LineMessagingProcessor", "LineMessagingProcessorClass.cs"));
+        library.Should().Contain("AddDevelopmentOverrides(");
+        var program = File.ReadAllText(Path.Combine(root, "SpeechMessageProducts.ChurchReport", "Program.cs"));
+        var guard = program.IndexOf("ValidateDevelopmentConfiguration(", StringComparison.Ordinal);
+        guard.Should().BeGreaterThan(0);
+        guard.Should().BeLessThan(program.IndexOf("ExceptionOutputOptions.FromConfiguration", StringComparison.Ordinal));
+    }
+
+    /// <summary>用反射呼叫共用程式庫實際覆寫接點，沒有主機 secrets 時預設 token 應為空，且不能出現基底假 token。</summary>
+    [Fact]
+    public void SharedDefaultPath_DevelopmentWithoutSecrets_CannotReturnBaseToken()
+    {
+        using var fixture = new SettingsFixture();
+        var method = typeof(global::LineMessagingProcessor.LineMessagingProcessorClass).GetMethod("AddDevelopmentOverrides", BindingFlags.Static | BindingFlags.NonPublic);
+        method.Should().NotBeNull("共用預設路徑也需要可傳入環境與 basePath 的隔離接點");
+        var builder = fixture.BaseBuilder();
+        method!.Invoke(null, new object[] { builder, fixture.Root, "Development" });
+        using var configuration = (ConfigurationRoot)builder.Build();
+        configuration["LineMessaging:DefaultOrganization"].Should().Be("DevTest");
+        configuration["LineMessaging:DevTest:ChannelAccessToken"].Should().BeNullOrEmpty();
+        configuration.AsEnumerable().Should().NotContain(x => x.Value == "PROD-FAKE" || x.Value == "PROD-B-FAKE");
+    }
+
+    /// <summary>建立可獨立判定的安全假設定；全部為字面測試資料，不借用產品 guard 計算期望值。</summary>
+    private static Dictionary<string, string?> SafeValues() => new()
+    {
+        ["LineMessaging:DefaultOrganization"] = "DevTest",
+        ["LineMessaging:Jesus:ChannelAccessToken"] = "",
+        ["LineMessaging:JesusBack:ChannelAccessToken"] = "",
+        ["CrmConnection:Organization"] = "goodshepherdback",
+        ["CrmConnection:ServerUrl"] = "https://goodshepherdback.speechmessage.com.tw",
+        ["DynamicsAccess:Package01FeeReadsEnabled"] = "true",
+        ["DynamicsAccess:Embedded:OrganizationWebApiBaseUri"] = "https://goodshepherdback.speechmessage.com.tw/api/data/v9.1",
+        ["DynamicsAccess:Embedded:ResourceUri"] = "https://goodshepherdback.speechmessage.com.tw"
+    };
+
+    /// <summary>只定位最近方案根目錄，找到後立即停止向上，避免巢狀 worktree 落到父分支。</summary>
+    private static string RepositoryRoot()
+    {
+        var path = new DirectoryInfo(AppContext.BaseDirectory);
+        while (path != null && !File.Exists(Path.Combine(path.FullName, "SpeechMessageProducts.sln"))) path = path.Parent;
+        return path?.FullName ?? throw new DirectoryNotFoundException("找不到測試所屬 worktree。");
+    }
+
+    /// <summary>單一測試擁有兩個假 JSON；使用不可預測獨立路徑，不讀真實產品設定、User Secrets 或其他案例資料。</summary>
+    private sealed class SettingsFixture : IDisposable
+    {
+        /// <summary>此案例專有的暫存根目錄；只包含固定假設定，Dispose 後整個目錄移除。</summary>
+        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "p0-sec-" + Guid.NewGuid().ToString("N"));
+
+        /// <summary>建立基底假正式值及 Development 空白覆寫；故障由各測試另行注入，沒有任何外部連線。</summary>
+        internal SettingsFixture()
+        {
+            Directory.CreateDirectory(Root);
+            File.WriteAllText(Path.Combine(Root, "appsettings.json"), """
+                {"LineMessaging":{"DefaultOrganization":"Jesus","Jesus":{"ChannelAccessToken":"[REDACTED]"},"JesusBack":{"ChannelAccessToken":"[REDACTED]"}},"Untouched":{"Value":"same"}}
+                """, new UTF8Encoding(false));
+            File.WriteAllText(Path.Combine(Root, "appsettings.Development.json"), """
+                {"LineMessaging":{"DefaultOrganization":"DevTest","Jesus":{"ChannelAccessToken":""},"JesusBack":{"ChannelAccessToken":""}}}
+                """, new UTF8Encoding(false));
+        }
+
+        /// <summary>每次建立獨立 builder；reload 停用避免 watcher，回傳的根由個別案例 Dispose。</summary>
+        internal IConfigurationBuilder BaseBuilder() => new ConfigurationBuilder().SetBasePath(Root).AddJsonFile("appsettings.json", false, false);
+
+        /// <summary>所有組態根已先 Dispose；只刪除此 fixture 已知的絕對暫存目錄，防止跨案例清除或保留假機密。</summary>
+        public void Dispose()
+        {
+            var actual = Path.GetFullPath(Root);
+            var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
+            if (!actual.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(actual).StartsWith("p0-sec-", StringComparison.Ordinal))
+                throw new InvalidOperationException("拒絕清理 fixture 以外的目錄。");
+            Directory.Delete(actual, recursive: true);
+        }
+    }
+}

diff --git a/LineMessagingProcessor/LineMessagingProcessorClass.cs b/LineMessagingProcessor/LineMessagingProcessorClass.cs
index a21c6c20f..94952f19c 100644
--- a/LineMessagingProcessor/LineMessagingProcessorClass.cs
+++ b/LineMessagingProcessor/LineMessagingProcessorClass.cs
@@ -87,17 +87,35 @@ namespace LineMessagingProcessor
                 : channelAccessToken;
         }
 
+        /// <summary>
+        /// 解析舊預設建構路徑的一次性部署 token；Lazy 每行程只保存此部署憑證，不含使用者或教會動態狀態。
+        /// Development 先遮蔽基底正式別名，再依既有順序套用環境變數；不引用 ChurchReport 或載入其 User Secrets。
+        /// 不開 reload watcher，讀完立即 Dispose 區域組態根；Production 的 provider 順序及值保持不變。
+        /// </summary>
+        /// <returns>部署設定的 token，未提供測試 token 時為空，後續發送入口會拒絕；不退回其他正式別名。</returns>
         private static string ResolveDefaultChannelAccessToken()
         {
-            var configuration = new ConfigurationBuilder()
+            var builder = new ConfigurationBuilder()
                 .SetBasePath(Directory.GetCurrentDirectory())
-                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
-                .AddEnvironmentVariables()
-                .Build();
-
+                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
+            AddDevelopmentOverrides(builder, Directory.GetCurrentDirectory(),
+                Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production");
+            using var configuration = (ConfigurationRoot)builder.AddEnvironmentVariables().Build();
             return ResolveChannelAccessToken(configuration);
         }
 
+        /// <summary>只追加 Development JSON 的獨立接點；可由呼叫端明確提供目錄與環境，不修改任何程序全域狀態。</summary>
+        /// <param name="builder">已加入原基底來源的建構器；非 Development 直接保留，新增來源不啟用監聽。</param>
+        /// <param name="basePath">部署者控制的設定根，不接受使用者請求內容。</param>
+        /// <param name="environmentName">與主機相同規則解析的環境；大小寫不影響 Development 判斷。</param>
+        /// <returns>原建構器；不加入 ChurchReport User Secrets，不引入主機相依或共用可變請求狀態。</returns>
+        private static IConfigurationBuilder AddDevelopmentOverrides(IConfigurationBuilder builder, string basePath, string environmentName)
+        {
+            if (string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase))
+                builder.SetBasePath(basePath).AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false);
+            return builder;
+        }
+
         private static string ResolveChannelAccessToken(IConfiguration configuration)
         {
             if (configuration == null)

diff --git a/SpeechMessageProducts.ChurchReport/Configuration/ChurchReportDevelopmentConfiguration.cs b/SpeechMessageProducts.ChurchReport/Configuration/ChurchReportDevelopmentConfiguration.cs
new file mode 100644
index 00000000..3ccfd5f9
--- /dev/null
+++ b/SpeechMessageProducts.ChurchReport/Configuration/ChurchReportDevelopmentConfiguration.cs
@@ -0,0 +1,128 @@
+using System;
+using System.IO;
+using System.Linq;
+using System.Reflection;
+using Microsoft.Extensions.Configuration;
+
+namespace ChurchReport.Configuration;
+
+/// <summary>
+/// 讓舊式行程級設定讀取點在 Development 套用與主機相同的檔案及 secrets，並在任何 CRM／LINE client 建立前驗證安全邊界。
+/// 不保存請求、使用者、教會、設定值或憑證；非 Development 完全不新增 provider、不讀基底檔也不改變正式設定。
+/// </summary>
+/// <remarks>
+/// 呼叫端的 static／Lazy 設定仍各行程一份，只包含部署設定，不得放入使用者狀態。
+/// 新增 provider 停用 reload，不新增 watcher、事件訂閱或背景工作；原基底檔的 optional／reload 行為維持不變。
+/// 啟動檢查只持有當次區域設定，基底串流與組態根都在返回前 Dispose；錯誤只有鍵名與固定原因，不包含值或內部例外。
+/// </remarks>
+internal static class ChurchReportDevelopmentConfiguration
+{
+    /// <summary>依現有 WebApplication Host 的順序選擇環境；純函式不讀取或修改程序的環境變數。</summary>
+    /// <param name="dotnetEnvironment">呼叫端取得的 DOTNET_ENVIRONMENT；null 表示未指定。</param>
+    /// <param name="aspnetcoreEnvironment">呼叫端取得的 ASPNETCORE_ENVIRONMENT；僅在前者未指定時採用。</param>
+    /// <returns>先 DOTNET、再 ASPNETCORE，皆未指定則 Production。</returns>
+    internal static string ResolveEnvironmentName(string dotnetEnvironment, string aspnetcoreEnvironment)
+        => dotnetEnvironment ?? aspnetcoreEnvironment ?? "Production";
+
+    /// <summary>既有靜態讀取點的唯一轉接；只讀部署環境名稱，不保存 request 或教會狀態，也不改目前目錄。</summary>
+    /// <param name="builder">已加入原基底檔與原 optional／reload 選項的建構器；生命週期仍由原呼叫端擁有。</param>
+    /// <returns>相同建構器；只有 Development 會追加環境檔及主機 secrets。</returns>
+    internal static IConfigurationBuilder AddChurchReportDevelopmentOverrides(this IConfigurationBuilder builder)
+        => builder.AddChurchReportDevelopmentOverrides(Directory.GetCurrentDirectory(),
+            ResolveEnvironmentName(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"), Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")),
+            typeof(global::ChurchReport.Program).Assembly);
+
+    /// <summary>用明確目錄、環境及擁有者組件組合設定；非 Development 立即返回，不觸碰來源、目錄或 secrets。</summary>
+    /// <param name="builder">原設定建構器；回傳相同實例，不替換或移除既有 provider。</param>
+    /// <param name="basePath">部署者控制的設定根目錄，不可由使用者請求指定。</param>
+    /// <param name="environmentName">已解析的主機環境；Development 比對不分大小寫。</param>
+    /// <param name="secretsAssembly">主機擁有的 UserSecretsId 所在組件；測試使用無 ID 的測試組件，避免讀真實 secrets。</param>
+    /// <returns>只有 Development 才依 Host 順序追加 optional JSON、optional User Secrets，兩者均不監聽變更。</returns>
+    internal static IConfigurationBuilder AddChurchReportDevelopmentOverrides(this IConfigurationBuilder builder,
+        string basePath, string environmentName, Assembly secretsAssembly)
+    {
+        if (!IsDevelopment(environmentName)) return builder;
+        return builder.SetBasePath(basePath)
+            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
+            .AddUserSecrets(secretsAssembly, optional: true, reloadOnChange: false);
+    }
+
+    /// <summary>
+    /// 在 Development 驗證有效 LINE／CRM／Embedded 設定；任何違反邊界立即失敗，不建立外部 client 或通知。
+    /// Production 包含未設定環境時完全略過，保留原有行為；假設定可直接注入，不需變更程序狀態。
+    /// </summary>
+    /// <param name="basePath">受信任的主機根目錄；只用於讀取基底 appsettings.json，不接受 request 路徑。</param>
+    /// <param name="environmentName">主機已選定的環境名稱。</param>
+    /// <param name="configuration">Host 合併後的設定；只借用、不修改、不 Dispose 呼叫端擁有的根。</param>
+    /// <exception cref="InvalidOperationException">缺少可比較的基底設定，或任何鍵違反開發環境邊界；訊息不含設定值。</exception>
+    internal static void ValidateDevelopmentConfiguration(string basePath, string environmentName, IConfiguration configuration)
+    {
+        if (!IsDevelopment(environmentName)) return;
+        // 使用串流直接讀基底快照，避免架構掃描把安全比較用的基底讀取當成對外 token 的來源。
+        // 基底串流在 ReadBaseline 返回前關閉，組態根在本次檢查結束前 Dispose，兩者均不跨請求保存。
+        using var baseline = ReadBaseline(basePath);
+        var baseChannels = baseline.GetSection("LineMessaging").GetChildren()
+            .Where(s => !string.IsNullOrWhiteSpace(s["ChannelAccessToken"])).ToArray();
+        foreach (var channel in configuration.GetSection("LineMessaging").GetChildren())
+        {
+            var value = channel["ChannelAccessToken"];
+            // 也拒絕把基底正式 token 搬到另一個別名，避免僅改別名就繞過檢查。
+            if (!string.IsNullOrWhiteSpace(value) && baseChannels.Any(s => string.Equals(value, s["ChannelAccessToken"], StringComparison.Ordinal)))
+                throw UnsafeKey(channel.Path + ":ChannelAccessToken", "不可沿用基底的正式憑證。");
+        }
+        var environmentToken = configuration["LINE_CHANNEL_ACCESS_TOKEN"];
+        if (!string.IsNullOrWhiteSpace(environmentToken) && baseChannels.Any(s => string.Equals(environmentToken, s["ChannelAccessToken"], StringComparison.Ordinal)))
+            throw UnsafeKey("LINE_CHANNEL_ACCESS_TOKEN", "不可沿用基底的正式憑證。");
+        var defaultOrganization = configuration["LineMessaging:DefaultOrganization"];
+        if (string.IsNullOrWhiteSpace(defaultOrganization) || baseChannels.Any(s => string.Equals(s.Key, defaultOrganization, StringComparison.OrdinalIgnoreCase)))
+            throw UnsafeKey("LineMessaging:DefaultOrganization", "必須選擇獨立的測試別名。");
+        if (!string.Equals(configuration["CrmConnection:Organization"], "goodshepherdback", StringComparison.OrdinalIgnoreCase))
+            throw UnsafeKey("CrmConnection:Organization", "開發模式只能使用指定的測試組織。");
+        ValidateTestHost(configuration, "CrmConnection:ServerUrl");
+        var enabled = configuration["DynamicsAccess:Package01FeeReadsEnabled"];
+        if (enabled != null && !bool.TryParse(enabled, out _))
+            throw UnsafeKey("DynamicsAccess:Package01FeeReadsEnabled", "必須是有效的布林開關。");
+        if (bool.TryParse(enabled, out var readsEnabled) && readsEnabled)
+        {
+            ValidateTestHost(configuration, "DynamicsAccess:Embedded:OrganizationWebApiBaseUri");
+            ValidateTestHost(configuration, "DynamicsAccess:Embedded:ResourceUri");
+        }
+    }
+
+    /// <summary>大小寫不敏感的環境判斷；不讀全域環境，不配置或快取任何設定快照。</summary>
+    /// <param name="name">呼叫端傳入的環境名稱；null 不視為 Development。</param>
+    /// <returns>僅 Development 回傳 true。</returns>
+    private static bool IsDevelopment(string name) => string.Equals(name, "Development", StringComparison.OrdinalIgnoreCase);
+
+    /// <summary>讀取一次基底設定供憑證比較；解析失敗改成固定安全訊息，避免原例外包含來源路徑或機密內容。</summary>
+    /// <param name="basePath">部署者擁有的根目錄。</param>
+    /// <returns>由本次啟動檢查負責 Dispose 的組態根；不開 watcher。</returns>
+    private static ConfigurationRoot ReadBaseline(string basePath)
+    {
+        try
+        {
+            using var stream = File.OpenRead(Path.Combine(basePath, "appsettings.json"));
+            return (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(stream).Build();
+        }
+        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException)
+        {
+            throw UnsafeKey("appsettings.json", "無法安全讀取基底設定，已拒絕開發啟動。");
+        }
+    }
+
+    /// <summary>限制 CRM／Embedded URI 的主機，使用解析後的精確主機比較，不接受相似網域或路徑中的偽裝名稱。</summary>
+    /// <param name="configuration">只借用當次啟動設定，不保存於 static。</param>
+    /// <param name="key">原始碼固定的鍵名，不可含使用者提供的值。</param>
+    private static void ValidateTestHost(IConfiguration configuration, string key)
+    {
+        if (!Uri.TryCreate(configuration[key], UriKind.Absolute, out var uri) ||
+            !string.Equals(uri.Host, "goodshepherdback.speechmessage.com.tw", StringComparison.OrdinalIgnoreCase))
+            throw UnsafeKey(key, "開發模式只能使用指定測試組織的主機。");
+    }
+
+    /// <summary>建立只含可信鍵名與固定原因的拒絕結果；不串接設定值、不保存原始敏感例外。</summary>
+    /// <param name="key">固定鍵名或設定節點路徑，不包含憑證值。</param>
+    /// <param name="reason">開發者定義的固定原因。</param>
+    /// <returns>由 Host 既有初始化錯誤路徑處理的例外；此處不建立 LINE 通知。</returns>
+    private static InvalidOperationException UnsafeKey(string key, string reason) => new(key + "：" + reason);
+}

diff --git a/SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs b/SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs
index b784c9ca..bd506856 100644
--- a/SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs
+++ b/SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs
@@ -22,6 +22,7 @@ using LineMessagingProcessor.Workflows;
 using Microsoft.AspNetCore.Mvc;
 using Microsoft.Extensions.Configuration;
 using Microsoft.Xrm.Sdk;
+using ChurchReport.Configuration;
 using System;
 using System.Collections.Generic;
 using System.Globalization;
@@ -44,7 +45,12 @@ namespace ChurchReport.Models
     public class DonationPaymentManager : Controller, IDisposable
     {
         #region 資料區
-        static ConfigurationBuilder m_ConfigurationBuilder = (ConfigurationBuilder)new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json");
+        /// <summary>
+        /// DonationPaymentManager 的部署設定每行程一份，只保存設定、不保存 request／使用者或教會可變資料。
+        /// Development 接上主機環境檔與 secrets，正式環境不增加 provider；缺少測試 token 不得退回基底正式值。
+        /// 保留原 static／Lazy 與基底 reload 生命週期；新增來源不監聽變更，不建立額外背景資源。
+        /// </summary>
+        static ConfigurationBuilder m_ConfigurationBuilder = (ConfigurationBuilder)new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json").AddChurchReportDevelopmentOverrides();
         static IConfiguration m_Configuration = m_ConfigurationBuilder.Build();
 
         // 商店編號

diff --git a/SpeechMessageProducts.ChurchReport/Program.cs b/SpeechMessageProducts.ChurchReport/Program.cs
index 66af6ead..fd84457d 100644
--- a/SpeechMessageProducts.ChurchReport/Program.cs
+++ b/SpeechMessageProducts.ChurchReport/Program.cs
@@ -67,6 +67,10 @@ namespace ChurchReport
             {
                 var builder = WebApplication.CreateBuilder(args);
                 configuration = builder.Configuration;
+                // 先拒絕不安全的 Development 設定，才建立日誌／LINE sender 與後續 CRM 服務；
+                // Production 完全略過。失敗沿既有初始化 catch 的固定 stderr 路徑，不把憑證帶進通知。
+                ChurchReport.Configuration.ChurchReportDevelopmentConfiguration.ValidateDevelopmentConfiguration(
+                    builder.Environment.ContentRootPath, builder.Environment.EnvironmentName, builder.Configuration);
                 var outputOptions = ExceptionOutputOptions.FromConfiguration(builder.Configuration);
                 diagnostics = new ExceptionDiagnostics(Path.Combine(AppContext.BaseDirectory, "Logs"), outputOptions: outputOptions);
                 registration = ExceptionReporting.Attach(diagnostics);

diff --git a/SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs b/SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs
index 198b53d8..0be187b4 100644
--- a/SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs
+++ b/SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs
@@ -11,6 +11,7 @@
 // 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
 // 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
 // ============================================================================
+using ChurchReport.Configuration;
 using System;
 using System.Collections.Generic;
 using System.IO;
@@ -40,11 +41,16 @@ namespace ChurchReport.Services
         private readonly PaymentFeeTypeHelper _feeTypeHelper;
         private readonly ILineNotificationWorkflow _lineNotificationWorkflow;
 
+        /// <summary>
+        /// PaymentNotificationService 的部署設定每行程一份，只保存設定、不保存 request／使用者或教會可變資料。
+        /// Development 接上主機環境檔與 secrets，正式環境不增加 provider；缺少測試 token 不得退回基底正式值。
+        /// 保留原 static／Lazy 與基底 reload 生命週期；新增來源不監聽變更，不建立額外背景資源。
+        /// </summary>
         private static readonly Lazy<IConfiguration> s_lazyConfiguration = new Lazy<IConfiguration>(() =>
         {
             var builder = new ConfigurationBuilder()
                 .SetBasePath(Directory.GetCurrentDirectory())
-                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
+                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true).AddChurchReportDevelopmentOverrides();
             return builder.Build();
         });
 

diff --git a/SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs b/SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs
index a3e97b0d..badd449b 100644
--- a/SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs
+++ b/SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs
@@ -18,6 +18,7 @@ using Microsoft.AspNetCore.Mvc;
 using Microsoft.Extensions.Configuration;
 using Microsoft.Xrm.Sdk;
 using ChurchReport.Payments;
+using ChurchReport.Configuration;
 using System;
 using System.Collections.Generic;
 using System.Linq;
@@ -52,11 +53,16 @@ namespace ChurchReport.Tools
         // 1. 只有第一次真正需要設定時才讀 appsettings.json，避免建構類別時立刻做檔案 I/O。
         // 2. static 共用同一份設定，避免每次付款回傳都重新建立 IConfiguration。
         // 3. reloadOnChange: true 讓 appsettings.json 更新後可被組態系統重新載入。
+        /// <summary>
+        /// DonationFeePaymentProcessor 的部署設定每行程一份，只保存設定、不保存 request／使用者或教會可變資料。
+        /// Development 接上主機環境檔與 secrets，正式環境不增加 provider；缺少測試 token 不得退回基底正式值。
+        /// 保留原 static／Lazy 與基底 reload 生命週期；新增來源不監聽變更，不建立額外背景資源。
+        /// </summary>
         private static readonly Lazy<IConfiguration> s_lazyConfiguration = new Lazy<IConfiguration>(() =>
         {
             var builder = new ConfigurationBuilder()
                 .SetBasePath(Directory.GetCurrentDirectory())
-                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
+                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true).AddChurchReportDevelopmentOverrides();
             return builder.Build();
         });
 

diff --git a/SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs b/SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs
index fc15d084..dd9fe28c 100644
--- a/SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs
+++ b/SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs
@@ -11,6 +11,7 @@
 // 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
 // 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
 // ============================================================================
+using ChurchReport.Configuration;
 using System;
 using System.Collections.Generic;
 using System.Linq;
@@ -53,9 +54,14 @@ namespace ChurchReport.Tools
             #region Channel Access Token 組態
 
             // 組態建構器與組態實例。宣告為 static readonly，讓整個行程共用一份，
+            /// <summary>
+            /// LineUtilityClass 的部署設定每行程一份，只保存設定、不保存 request／使用者或教會可變資料。
+            /// Development 接上主機環境檔與 secrets，正式環境不增加 provider；缺少測試 token 不得退回基底正式值。
+            /// 保留原 static／Lazy 與基底 reload 生命週期；新增來源不監聽變更，不建立額外背景資源。
+            /// </summary>
             private static readonly IConfigurationBuilder m_ConfigurationBuilder = new ConfigurationBuilder()
                 .SetBasePath(Directory.GetCurrentDirectory())
-                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
+                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false).AddChurchReportDevelopmentOverrides();
             private static readonly IConfiguration m_Configuration = m_ConfigurationBuilder.Build();
 
             // 依組織代號從 appsettings.json 取出對應的 Channel Access Token。
@@ -1144,4 +1150,3 @@ namespace ChurchReport.Tools
 
     #endregion
 }
-

diff --git a/SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs b/SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs
index aee776e8..fda60e21 100644
--- a/SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs
+++ b/SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs
@@ -11,6 +11,7 @@
 // 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
 // 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
 // ============================================================================
+using ChurchReport.Configuration;
 using System;
 using System.IO;
 using ToolUtilityNameSpace;
@@ -62,9 +63,14 @@ namespace ChurchReport.Tools
         bool m_NotifyLineFlag = false;
 
         // 配置管理
+        /// <summary>
+        /// PersonalQrCodeUtility 的部署設定每行程一份，只保存設定、不保存 request／使用者或教會可變資料。
+        /// Development 接上主機環境檔與 secrets，正式環境不增加 provider；缺少測試 token 不得退回基底正式值。
+        /// 保留原 static／Lazy 與基底 reload 生命週期；新增來源不監聽變更，不建立額外背景資源。
+        /// </summary>
         private static readonly IConfigurationBuilder m_ConfigurationBuilder = new ConfigurationBuilder()
             .SetBasePath(Directory.GetCurrentDirectory())
-            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
+            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false).AddChurchReportDevelopmentOverrides();
         private static readonly IConfiguration m_Configuration = m_ConfigurationBuilder.Build();
 
         #endregion

diff --git a/SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs b/SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs
index 14f67d66..05dd64b2 100644
--- a/SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs
+++ b/SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs
@@ -12,6 +12,7 @@
 // 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
 // ============================================================================
 using ChurchReport.WebServiceConnector;
+using ChurchReport.Configuration;
 using System;
 using System.Collections.Generic;
 using System.IO;
@@ -68,9 +69,14 @@ namespace ChurchReport.Tools
         private const string SAVED_FLAG_FIELD = "new_saved_flag";
 
         // 配置管理
+        /// <summary>
+        /// QrCodeUtility 的部署設定每行程一份，只保存設定、不保存 request／使用者或教會可變資料。
+        /// Development 接上主機環境檔與 secrets，正式環境不增加 provider；缺少測試 token 不得退回基底正式值。
+        /// 保留原 static／Lazy 與基底 reload 生命週期；新增來源不監聽變更，不建立額外背景資源。
+        /// </summary>
         private static readonly IConfigurationBuilder m_ConfigurationBuilder = new ConfigurationBuilder()
             .SetBasePath(Directory.GetCurrentDirectory())
-            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
+            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false).AddChurchReportDevelopmentOverrides();
         private static readonly IConfiguration m_Configuration = m_ConfigurationBuilder.Build();
 
         // 追蹤等級

diff --git a/SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs b/SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs
index 932da464..0f6c790f 100644
--- a/SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs
+++ b/SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs
@@ -18,6 +18,7 @@ using Microsoft.AspNetCore.Mvc;
 using Microsoft.Extensions.Configuration;
 using Microsoft.Xrm.Sdk;
 using ChurchReport.Payments;
+using ChurchReport.Configuration;
 using System;
 using System.Collections.Generic;
 using System.IO;
@@ -38,11 +39,16 @@ namespace ChurchReport.Tools
 
         #region 設定與配置
         // ✅ 透過 appsettings.json 讀取設定，避免硬編碼
+        /// <summary>
+        /// RecurringDonationPaymentProcessor 的部署設定每行程一份，只保存設定、不保存 request／使用者或教會可變資料。
+        /// Development 接上主機環境檔與 secrets，正式環境不增加 provider；缺少測試 token 不得退回基底正式值。
+        /// 保留原 static／Lazy 與基底 reload 生命週期；新增來源不監聽變更，不建立額外背景資源。
+        /// </summary>
         private static readonly Lazy<IConfiguration> s_lazyConfiguration = new Lazy<IConfiguration>(() =>
         {
             var builder = new ConfigurationBuilder()
                 .SetBasePath(Directory.GetCurrentDirectory())
-                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
+                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true).AddChurchReportDevelopmentOverrides();
             return builder.Build();
         });
         private static IConfiguration m_Configuration => s_lazyConfiguration.Value;

diff --git a/SpeechMessageProducts.ChurchReport/Tools/SmallGroupQrCodeUtility.cs b/SpeechMessageProducts.ChurchReport/Tools/SmallGroupQrCodeUtility.cs
index 3c4970f8..ea74056b 100644
--- a/SpeechMessageProducts.ChurchReport/Tools/SmallGroupQrCodeUtility.cs
+++ b/SpeechMessageProducts.ChurchReport/Tools/SmallGroupQrCodeUtility.cs
@@ -12,6 +12,7 @@
 // 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
 // ============================================================================
 using ChurchReport.WebServiceConnector;
+using ChurchReport.Configuration;
 using System;
 using System.Collections.Generic;
 using System.IO;
@@ -72,9 +73,14 @@ namespace ChurchReport.Tools
         private const String SAVED_FLAG_FIELD = @"new_saved_flag";
 
         // 配置管理
+        /// <summary>
+        /// SmallGroupQrCodeUtility 的部署設定每行程一份，只保存設定、不保存 request／使用者或教會可變資料。
+        /// Development 接上主機環境檔與 secrets，正式環境不增加 provider；缺少測試 token 不得退回基底正式值。
+        /// 保留原 static／Lazy 與基底 reload 生命週期；新增來源不監聽變更，不建立額外背景資源。
+        /// </summary>
         private static readonly IConfigurationBuilder m_ConfigurationBuilder = new ConfigurationBuilder()
             .SetBasePath(Directory.GetCurrentDirectory())
-            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
+            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false).AddChurchReportDevelopmentOverrides();
         private static readonly IConfiguration m_Configuration = m_ConfigurationBuilder.Build();
 
         #endregion

diff --git a/SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs b/SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs
index e3542c64..d609dccc 100644
--- a/SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs
+++ b/SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs
@@ -11,6 +11,7 @@
 // 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
 // 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
 // ============================================================================
+using ChurchReport.Configuration;
 using System;
 using System.IO;
 using ToolUtilityNameSpace;
@@ -62,9 +63,14 @@ namespace ChurchReport.Tools
         private const bool SEND_LINE_NOTIFICATION = false;
 
         // 配置管理
+        /// <summary>
+        /// SundayQrCodeUtility 的部署設定每行程一份，只保存設定、不保存 request／使用者或教會可變資料。
+        /// Development 接上主機環境檔與 secrets，正式環境不增加 provider；缺少測試 token 不得退回基底正式值。
+        /// 保留原 static／Lazy 與基底 reload 生命週期；新增來源不監聽變更，不建立額外背景資源。
+        /// </summary>
         private static readonly IConfigurationBuilder m_ConfigurationBuilder = new ConfigurationBuilder()
             .SetBasePath(Directory.GetCurrentDirectory())
-            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
+            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false).AddChurchReportDevelopmentOverrides();
         private static readonly IConfiguration m_Configuration = m_ConfigurationBuilder.Build();
 
         #endregion

diff --git a/SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs b/SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs
index 710b4109..2d01733c 100644
--- a/SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs
+++ b/SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs
@@ -21,6 +21,7 @@ using LineMessagingProcessor.Workflows;
 using Microsoft.Extensions.Caching.Memory;
 using Microsoft.Extensions.Configuration;
 using Microsoft.Xrm.Sdk;
+using ChurchReport.Configuration;
 using System;
 using System.IO;
 using ToolUtilityNameSpace;
@@ -47,11 +48,16 @@ namespace ChurchReport.WebServiceConnector
         #region ===== 私有成員 =====
 
         // 配置管理（延遲初始化，線程安全）
+        /// <summary>
+        /// DonationPaymentProcessor.Core 的部署設定每行程一份，只保存設定、不保存 request／使用者或教會可變資料。
+        /// Development 接上主機環境檔與 secrets，正式環境不增加 provider；缺少測試 token 不得退回基底正式值。
+        /// 保留原 static／Lazy 與基底 reload 生命週期；新增來源不監聽變更，不建立額外背景資源。
+        /// </summary>
         private static readonly Lazy<IConfiguration> s_lazyConfiguration = new Lazy<IConfiguration>(() =>
         {
             var builder = new ConfigurationBuilder()
                 .SetBasePath(Directory.GetCurrentDirectory())
-                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
+                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true).AddChurchReportDevelopmentOverrides();
             return builder.Build();
         });
 

diff --git a/SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs b/SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs
index 290b69d8..6f618210 100644
--- a/SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs
+++ b/SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs
@@ -11,6 +11,7 @@
 // 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
 // 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
 // ============================================================================
+using ChurchReport.Configuration;
 using System;
 using System.Collections.Generic;
 using System.Linq;
@@ -46,9 +47,14 @@ namespace ChurchReport.WebServiceConnector
         private PushUtility m_PushUtility { get; set; }
 
         // 配置管理
+        /// <summary>
+        /// LineNotifyUtility 的部署設定每行程一份，只保存設定、不保存 request／使用者或教會可變資料。
+        /// Development 接上主機環境檔與 secrets，正式環境不增加 provider；缺少測試 token 不得退回基底正式值。
+        /// 保留原 static／Lazy 與基底 reload 生命週期；新增來源不監聽變更，不建立額外背景資源。
+        /// </summary>
         private static readonly IConfigurationBuilder m_ConfigurationBuilder = new ConfigurationBuilder()
             .SetBasePath(Directory.GetCurrentDirectory())
-            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
+            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false).AddChurchReportDevelopmentOverrides();
         private static readonly IConfiguration m_Configuration = m_ConfigurationBuilder.Build();
         #endregion
         #region 常數參數

diff --git a/SpeechMessageProducts.ChurchReport/appsettings.Development.json b/SpeechMessageProducts.ChurchReport/appsettings.Development.json
index 80bdbca1..c152b92b 100644
--- a/SpeechMessageProducts.ChurchReport/appsettings.Development.json
+++ b/SpeechMessageProducts.ChurchReport/appsettings.Development.json
@@ -1,4 +1,25 @@
 {
+  // goodshepherdback 是唯一開發測試組織；帳密由本主機 User Secrets 提供。
+  // 空白遮蔽基底正式帳密：未設定 secrets 時不可退回正式憑證；repo 不填任何實值。
+  "CrmConnection": {
+    "Organization": "goodshepherdback",
+    "ServerUrl": "https://goodshepherdback.speechmessage.com.tw/XRMServices/2011/Organization.svc",
+    "Domain": "[REDACTED]",
+    "Username": "[REDACTED]",
+    "Password": "[REDACTED]"
+  },
+  // goodshepherdback 的 ADFS 註冊驗證前，不走 Web API；僅由使用者核定後續啟用。
+  "DynamicsAccess": {
+    "Package01FeeReadsEnabled": false
+  },
+  "QPAY_ORGANIZATION": "goodshepherdback",
+  // 舊讀取點也必須遮蔽每個基底正式 token；缺少測試憑證就拒絕發送，不可退回正式別名。
+  // DevTest 的 ChannelAccessToken 僅放 User Secrets，不在 repo 填入任何實值。
+  "LineMessaging": {
+    "DefaultOrganization": "DevTest",
+    "Jesus": { "ChannelAccessToken": "" },
+    "JesusBack": { "ChannelAccessToken": "" }
+  },
   "DiagnosticsTrace": {
     "Enabled": true,
     "Directory": "D:\\除錯追蹤"
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
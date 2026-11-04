using ChurchReport.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.UserSecrets;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Contracts;

/// <summary>
/// 以實際 Development 檔的鍵與非機密網址驗證測試組織、設定優先順序與傳輸路徑防護；不建立 Host 或外部 client。
/// 複製到測試目錄前先將帳密值換成固定假值，避免真實憑證落入暫存證據；斷言只回報布林結果。
/// 每案獨立擁有組態根、假設定與假 User Secrets，先釋放組態，再於 Dispose 清除所有自建檔案。
/// </summary>
public sealed class DevelopmentOrganizationSettingsTests
{
    /// <summary>基底仍指向舊組織時，本案例應失敗；Development 合併後組織與 URI 主機必須精確符合唯一測試目標。</summary>
    [Fact]
    public void Development_CrmOrganizationAndServer_AreGoodshepherdback()
    {
        using var settings = LoadDevelopment();
        var configuration = settings.Configuration;
        configuration["CrmConnection:Organization"].Should().Be("goodshepherdback");
        Uri.TryCreate(configuration["CrmConnection:ServerUrl"], UriKind.Absolute, out var uri).Should().BeTrue();
        uri!.Host.Should().Be("goodshepherdback.speechmessage.com.tw");
    }

    /// <summary>
    /// 捕捉先前僅驗主機而漏掉服務路徑的回歸；對實際 Development 的非機密網址驗證 HTTPS、精確主機及 Organization.svc 路徑。
    /// 網址錯誤時只輸出失敗的布林條件，不建立連線，也不將設定值放進錯誤訊息。
    /// </summary>
    [Fact]
    public void Development_CrmServerUrl_UsesHttpsAndFullOrganizationServicePath()
    {
        using var settings = LoadDevelopment();
        Uri.TryCreate(settings.Configuration["CrmConnection:ServerUrl"], UriKind.Absolute, out var uri).Should().BeTrue();
        string.Equals(uri!.Scheme, "https", StringComparison.Ordinal).Should().BeTrue();
        string.Equals(uri.Host, "goodshepherdback.speechmessage.com.tw", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
        string.Equals(uri.AbsolutePath, "/XRMServices/2011/Organization.svc", StringComparison.Ordinal).Should().BeTrue();
    }

    /// <summary>
    /// 用唯一識別碼的假 User Secrets 驗證實際 Development 擴充方法的 provider 順序；若 secrets 被漏載或遭 JSON 蓋回，本案失敗。
    /// 所有帳密均為固定假資料，比較先轉成布林，不讀既有使用者 secrets；組態釋放後才清除專有 secrets 目錄。
    /// </summary>
    [Fact]
    public void Development_UserSecrets_OverrideDevelopmentCredentialKeys()
    {
        using var secrets = new SecretsScope();
        using var settings = new SettingsScope(ProductRoot(), secrets.Assembly);
        foreach (var key in new[] { "Domain", "Username", "Password" })
            string.Equals(settings.Configuration["CrmConnection:" + key], "SECRETS-FAKE", StringComparison.Ordinal)
                .Should().BeTrue("User Secrets 必須在 Development JSON 之後載入");
    }

    /// <summary>
    /// 實際 User Secrets provider 提供 DevTest token 時，Development 啟動守門應允許；
    /// 只斷言不拋出例外，不輸出或比較 token 內容。
    /// </summary>
    [Fact]
    public void Development_UserSecrets_DevTestToken_PassesSafetyGuard()
    {
        using var secrets = new SecretsScope();
        using var settings = new SettingsScope(ProductRoot(), secrets.Assembly);

        Action action = () => ChurchReportDevelopmentConfiguration.ValidateDevelopmentConfiguration(
            settings.Root, "Development", settings.Configuration);

        action.Should().NotThrow();
    }

    /// <summary>
    /// 依使用者 2026-11-03 核定，Development 可以存放帳密，故只檢查三個設定鍵存在，不限制值是否空白。
    /// 若設定鍵被刪除，本案必須失敗；不比對或輸出真實值，其他組織、LINE 與 Web API 防護由原案例繼續守門。
    /// </summary>
    [Fact]
    public void Development_Credentials_KeysExistWithoutRestrictingStorage()
    {
        using var settings = LoadDevelopment();
        var configuration = settings.Configuration;
        foreach (var key in new[] { "Domain", "Username", "Password" })
            configuration.GetSection("CrmConnection").GetChildren().Any(section =>
                string.Equals(section.Key, key, StringComparison.Ordinal)).Should().BeTrue("只確認設定鍵存在，不檢查帳密實值");
    }

    /// <summary>未停用前旗標為 true，必須失敗；ADFS 尚未驗證時只允許保留關閉的 Web API 路徑。</summary>
    [Fact]
    public void Development_EmbeddedFeeReads_AreDisabledBeforeAdfsValidation()
    {
        using var settings = LoadDevelopment();
        var configuration = settings.Configuration;
        configuration.GetValue<bool>("DynamicsAccess:Package01FeeReadsEnabled").Should().BeFalse();
    }

    /// <summary>LINE 別名與 token 遮蔽延續 P0-SEC；本包新增的 QPAY 組織未覆寫時會失敗，避免付款路徑指回舊組織。</summary>
    [Fact]
    public void Development_LineAliasesAndQpayOrganization_StayWithinTestBoundary()
    {
        using var settings = LoadDevelopment();
        var configuration = settings.Configuration;
        configuration["LineMessaging:DefaultOrganization"].Should().Be("DevTest");
        string.IsNullOrEmpty(configuration["LineMessaging:Jesus:ChannelAccessToken"]).Should().BeTrue();
        string.IsNullOrEmpty(configuration["LineMessaging:JesusBack:ChannelAccessToken"]).Should().BeTrue();
        configuration["QPAY_ORGANIZATION"].Should().Be("goodshepherdback");
    }

    /// <summary>
    /// 受追蹤的基底設定不得再保存任何 LINE channel access token；
    /// 只驗證欄位為空，不讀取、不輸出其他設定值。
    /// </summary>
    [Fact]
    public void TrackedBaseSettings_LineChannelAccessTokens_AreEmpty()
    {
        var root = ProductRoot();
        var json = JsonNode.Parse(
            File.ReadAllText(Path.Combine(root, "appsettings.json")),
            documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            })!.AsObject();
        var line = json["LineMessaging"]!.AsObject();

        foreach (var alias in new[] { "Jesus", "JesusBack" })
        {
            var token = line[alias]?["ChannelAccessToken"]?.GetValue<string>();
            string.IsNullOrEmpty(token).Should().BeTrue("追蹤設定不得保存 LINE channel access token");
        }
    }

    /// <summary>P0-SEC 後舊 CRM 設定預期被擋；本包完成後同一份實際檔案合併結果必須通過該守門，且不需 secrets 或網路。</summary>
    [Fact]
    public void Development_ActualMergedSettings_PassExistingSafetyGuard()
    {
        using var settings = LoadDevelopment();
        var configuration = settings.Configuration;
        Action action = () => ChurchReportDevelopmentConfiguration.ValidateDevelopmentConfiguration(settings.Root, "Development", configuration);
        action.Should().NotThrow();
    }

    /// <summary>建立每案例獨立的假基底及去除帳密實值的 Development 複本；不讀真實基底機密或既有使用者 secrets。</summary>
    /// <returns>由測試先釋放組態根，再清除暫存目錄的唯一 owner。</returns>
    private static SettingsScope LoadDevelopment() => new(ProductRoot());

    /// <summary>擁有單案組態與暫存檔；Development 保留實際鍵與網址，帳密實值先換成假值再寫入測試目錄。</summary>
    private sealed class SettingsScope : IDisposable
    {
        /// <summary>單案獨立假設定根，不承載或共享使用者資料。</summary>
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "p0-env-" + Guid.NewGuid().ToString("N"));

        /// <summary>只包含假基底及 Development 覆寫的根；本 scope 唯一擁有並確定釋放。</summary>
        internal ConfigurationRoot Configuration { get; }

        /// <summary>注入假正式設定及清除帳密實值的 Development 複本；所有來源停用 reload，secrets 僅能來自明確傳入的測試組件。</summary>
        /// <param name="productRoot">當前 worktree 的產品根；實際憑證只在解析期間存在，寫檔前一律以假值取代。</param>
        /// <param name="secretsAssembly">測試專屬 secrets 組件；省略時使用沒有 UserSecretsId 的測試組件，不觸碰真實 secrets。</param>
        internal SettingsScope(string productRoot, Assembly? secretsAssembly = null)
        {
            try
            {
                Directory.CreateDirectory(Root);
                File.WriteAllText(Path.Combine(Root, "appsettings.json"), """
                    {"CrmConnection":{"Organization":"sunnyvalechback","ServerUrl":"https://sunnyvalechback.speechmessage.com.tw","Domain":"PROD-FAKE-DOMAIN","Username":"PROD-FAKE-USER","Password":"PROD-FAKE-PASSWORD"},"LineMessaging":{"DefaultOrganization":"Jesus","Jesus":{"ChannelAccessToken":"PROD-FAKE"},"JesusBack":{"ChannelAccessToken":"PROD-B-FAKE"}},"QPAY_ORGANIZATION":"sunnyvalechback","DynamicsAccess":{"Package01FeeReadsEnabled":true,"Embedded":{"OrganizationWebApiBaseUri":"https://sunnyvalechback.speechmessage.com.tw/api/data/v9.1","ResourceUri":"https://sunnyvalechback.speechmessage.com.tw"}}}
                    """, new System.Text.UTF8Encoding(false));
                var development = JsonNode.Parse(File.ReadAllText(Path.Combine(productRoot, "appsettings.Development.json")),
                    documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject();
                var crm = development["CrmConnection"]!.AsObject();
                foreach (var key in new[] { "Domain", "Username", "Password" })
                {
                    // 只保留鍵是否存在；不以真實值作比較，也不把它複製到暫存目錄或斷言輸出。
                    if (crm.ContainsKey(key)) crm[key] = "JSON-FAKE";
                }
                File.WriteAllText(Path.Combine(Root, "appsettings.Development.json"), development.ToJsonString(),
                    new System.Text.UTF8Encoding(false));
                Configuration = (ConfigurationRoot)new ConfigurationBuilder().SetBasePath(Root).AddJsonFile("appsettings.json", false, false)
                .AddChurchReportDevelopmentOverrides(Root, "Development", secretsAssembly ?? typeof(DevelopmentOrganizationSettingsTests).Assembly).Build();
            }
            catch
            {
                // 建構中途失敗尚未交出 owner，仍由本 scope 清除已建立的假設定目錄。
                Dispose();
                throw;
            }
        }

        /// <summary>先關組態根，再確認絕對路徑位於暫存根及專有前綴內才刪除，避免 watcher、handle 或跨案例資料殘留。</summary>
        public void Dispose()
        {
            Configuration?.Dispose();
            var actual = Path.GetFullPath(Root);
            var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!actual.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(actual).StartsWith("p0-env-", StringComparison.Ordinal))
                throw new InvalidOperationException("拒絕清理 scope 以外的目錄。");
            if (Directory.Exists(actual)) Directory.Delete(actual, recursive: true);
        }
    }

    /// <summary>
    /// 以不可預測識別碼隔離一份假 User Secrets；不列舉、不覆寫既有 secrets，也不改程序環境。
    /// 僅保留固定假值及可回收動態組件，Dispose 時確認路徑仍位於自己擁有的 secrets 子目錄後刪除。
    /// </summary>
    private sealed class SecretsScope : IDisposable
    {
        private readonly string _root;
        private readonly string _id = "speechmessage-p0-env-test-" + Guid.NewGuid().ToString("N");

        /// <summary>本案專有的 UserSecretsId 所在組件；不使用產品組件，因此不可能載入既有產品 secrets。</summary>
        internal Assembly Assembly { get; }

        /// <summary>建立僅含固定假資料的秘密檔與可回收的組件識別碼，讓真實 AddUserSecrets provider 處理載入與優先順序。</summary>
        internal SecretsScope()
        {
            _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets", _id);
            try
            {
                Directory.CreateDirectory(_root);
                File.WriteAllText(Path.Combine(_root, "secrets.json"),
                    """{"CrmConnection":{"Domain":"SECRETS-FAKE","Username":"SECRETS-FAKE","Password":"SECRETS-FAKE"},"LineMessaging":{"DevTest":{"ChannelAccessToken":"DEV-FAKE"}}}""",
                    new System.Text.UTF8Encoding(false));
                var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(_id), AssemblyBuilderAccess.RunAndCollect);
                assembly.SetCustomAttribute(new CustomAttributeBuilder(
                    typeof(UserSecretsIdAttribute).GetConstructor(new[] { typeof(string) })!, new object[] { _id }));
                Assembly = assembly;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>組態根已由外層先釋放；只刪除本案建立、且仍位於 UserSecrets 根目錄的唯一識別碼子目錄。</summary>
        public void Dispose()
        {
            var parent = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets"))
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var actual = Path.GetFullPath(_root);
            if (!actual.StartsWith(parent, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileName(actual), _id, StringComparison.Ordinal))
                throw new InvalidOperationException("拒絕清理測試 secrets 以外的目錄。");
            if (Directory.Exists(actual)) Directory.Delete(actual, recursive: true);
        }
    }

    /// <summary>找到最近方案即停止向上，只讀取當前 worktree 的產品設定，不會退回父 worktree。</summary>
    /// <returns>當前方案內 ChurchReport 的絕對根目錄。</returns>
    private static string ProductRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "SpeechMessageProducts.sln"))) root = root.Parent;
        if (root == null) throw new DirectoryNotFoundException("找不到測試所屬方案。");
        return Path.Combine(root.FullName, "SpeechMessageProducts.ChurchReport");
    }
}

using ChurchReport.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Contracts;

/// <summary>
/// 以實際 Development 檔配上假基底驗證組織、憑證遮蔽與傳輸路徑防護；不建立 Host 或外部 client。
/// 設定僅在測試區域存活，根於返回前 Dispose；所有憑證斷言先轉成布林，失敗也不能印出基底實值。
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

    /// <summary>未加 Development 空白遮蔽時，基底帳密仍可讀出，因此必須失敗；只判斷是否空白，不把實值交給測試輸出。</summary>
    [Fact]
    public void Development_Credentials_AreUnavailableUntilUserSecretsOverrides()
    {
        using var settings = LoadDevelopment();
        var configuration = settings.Configuration;
        foreach (var key in new[] { "Domain", "Username", "Password" })
            string.IsNullOrEmpty(configuration["CrmConnection:" + key]).Should().BeTrue("repo 的開發設定不可沿用基底帳密");
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

    /// <summary>P0-SEC 後舊 CRM 設定預期被擋；本包完成後同一份實際檔案合併結果必須通過該守門，且不需 secrets 或網路。</summary>
    [Fact]
    public void Development_ActualMergedSettings_PassExistingSafetyGuard()
    {
        using var settings = LoadDevelopment();
        var configuration = settings.Configuration;
        Action action = () => ChurchReportDevelopmentConfiguration.ValidateDevelopmentConfiguration(settings.Root, "Development", configuration);
        action.Should().NotThrow();
    }

    /// <summary>建立每案例獨立的假基底，只複製 repo 的 Development 檔；不讀真實基底機密或使用者 secrets。</summary>
    /// <returns>由測試先釋放組態根，再清除暫存目錄的唯一 owner。</returns>
    private static SettingsScope LoadDevelopment() => new(ProductRoot());

    /// <summary>擁有單案組態與暫存檔；Production 假資料固定，原 Development 檔完整複製以驗證真正發布的覆寫內容。</summary>
    private sealed class SettingsScope : IDisposable
    {
        /// <summary>單案獨立假設定根，不承載或共享使用者資料。</summary>
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "p0-env-" + Guid.NewGuid().ToString("N"));

        /// <summary>只包含假基底及 Development 覆寫的根；本 scope 唯一擁有並確定釋放。</summary>
        internal ConfigurationRoot Configuration { get; }

        /// <summary>注入假正式 token／帳密及舊組織，再套實際 Development 檔；所有來源都停用 reload，不啟動 watcher。</summary>
        /// <param name="productRoot">當前 worktree 的產品根，只讀取不含實值機密的 Development 檔。</param>
        internal SettingsScope(string productRoot)
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, "appsettings.json"), """
                {"CrmConnection":{"Organization":"sunnyvalechback","ServerUrl":"https://sunnyvalechback.speechmessage.com.tw","Domain":"PROD-FAKE-DOMAIN","Username":"PROD-FAKE-USER","Password":"PROD-FAKE-PASSWORD"},"LineMessaging":{"DefaultOrganization":"Jesus","Jesus":{"ChannelAccessToken":"PROD-FAKE"},"JesusBack":{"ChannelAccessToken":"PROD-B-FAKE"}},"QPAY_ORGANIZATION":"sunnyvalechback","DynamicsAccess":{"Package01FeeReadsEnabled":true,"Embedded":{"OrganizationWebApiBaseUri":"https://sunnyvalechback.speechmessage.com.tw/api/data/v9.1","ResourceUri":"https://sunnyvalechback.speechmessage.com.tw"}}}
                """, new System.Text.UTF8Encoding(false));
            File.Copy(Path.Combine(productRoot, "appsettings.Development.json"), Path.Combine(Root, "appsettings.Development.json"));
            Configuration = (ConfigurationRoot)new ConfigurationBuilder().SetBasePath(Root).AddJsonFile("appsettings.json", false, false)
                .AddChurchReportDevelopmentOverrides(Root, "Development", typeof(DevelopmentOrganizationSettingsTests).Assembly).Build();
        }

        /// <summary>先關組態根，再確認絕對路徑位於暫存根及專有前綴內才刪除，避免 watcher、handle 或跨案例資料殘留。</summary>
        public void Dispose()
        {
            Configuration.Dispose();
            var actual = Path.GetFullPath(Root);
            var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!actual.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(actual).StartsWith("p0-env-", StringComparison.Ordinal))
                throw new InvalidOperationException("拒絕清理 scope 以外的目錄。");
            Directory.Delete(actual, recursive: true);
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

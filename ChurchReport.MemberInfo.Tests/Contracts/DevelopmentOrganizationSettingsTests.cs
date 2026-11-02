using ChurchReport.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Contracts;

/// <summary>
/// 以實際部署檔案驗證 Development 的組織、憑證遮蔽與傳輸路徑防護；不建立 Host 或外部 client。
/// 設定僅在測試區域存活，根於返回前 Dispose；所有憑證斷言先轉成布林，失敗也不能印出基底實值。
/// </summary>
public sealed class DevelopmentOrganizationSettingsTests
{
    /// <summary>基底仍指向舊組織時，本案例應失敗；Development 合併後組織與 URI 主機必須精確符合唯一測試目標。</summary>
    [Fact]
    public void Development_CrmOrganizationAndServer_AreGoodshepherdback()
    {
        using var configuration = LoadDevelopment();
        configuration["CrmConnection:Organization"].Should().Be("goodshepherdback");
        Uri.TryCreate(configuration["CrmConnection:ServerUrl"], UriKind.Absolute, out var uri).Should().BeTrue();
        uri!.Host.Should().Be("goodshepherdback.speechmessage.com.tw");
    }

    /// <summary>未加 Development 空白遮蔽時，基底帳密仍可讀出，因此必須失敗；只判斷是否空白，不把實值交給測試輸出。</summary>
    [Fact]
    public void Development_Credentials_AreUnavailableUntilUserSecretsOverrides()
    {
        using var configuration = LoadDevelopment();
        foreach (var key in new[] { "Domain", "Username", "Password" })
            string.IsNullOrEmpty(configuration["CrmConnection:" + key]).Should().BeTrue("repo 的開發設定不可沿用基底帳密");
    }

    /// <summary>未停用前旗標為 true，必須失敗；ADFS 尚未驗證時只允許保留關閉的 Web API 路徑。</summary>
    [Fact]
    public void Development_EmbeddedFeeReads_AreDisabledBeforeAdfsValidation()
    {
        using var configuration = LoadDevelopment();
        configuration.GetValue<bool>("DynamicsAccess:Package01FeeReadsEnabled").Should().BeFalse();
    }

    /// <summary>LINE 別名與 token 遮蔽延續 P0-SEC；本包新增的 QPAY 組織未覆寫時會失敗，避免付款路徑指回舊組織。</summary>
    [Fact]
    public void Development_LineAliasesAndQpayOrganization_StayWithinTestBoundary()
    {
        using var configuration = LoadDevelopment();
        configuration["LineMessaging:DefaultOrganization"].Should().Be("DevTest");
        string.IsNullOrEmpty(configuration["LineMessaging:Jesus:ChannelAccessToken"]).Should().BeTrue();
        string.IsNullOrEmpty(configuration["LineMessaging:JesusBack:ChannelAccessToken"]).Should().BeTrue();
        configuration["QPAY_ORGANIZATION"].Should().Be("goodshepherdback");
    }

    /// <summary>P0-SEC 後舊 CRM 設定預期被擋；本包完成後同一份實際檔案合併結果必須通過該守門，且不需 secrets 或網路。</summary>
    [Fact]
    public void Development_ActualMergedSettings_PassExistingSafetyGuard()
    {
        using var configuration = LoadDevelopment();
        Action action = () => ChurchReportDevelopmentConfiguration.ValidateDevelopmentConfiguration(ProductRoot(), "Development", configuration);
        action.Should().NotThrow();
    }

    /// <summary>只載入 repo 的基底及 Development 檔，明確傳入無 secrets ID 的測試組件，杜絕讀取使用者真實 secrets。</summary>
    /// <returns>只由該案例擁有並 Dispose 的區域組態根。</returns>
    private static ConfigurationRoot LoadDevelopment()
    {
        var root = ProductRoot();
        return (ConfigurationRoot)new ConfigurationBuilder().SetBasePath(root).AddJsonFile("appsettings.json", false, false)
            .AddChurchReportDevelopmentOverrides(root, "Development", typeof(DevelopmentOrganizationSettingsTests).Assembly).Build();
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

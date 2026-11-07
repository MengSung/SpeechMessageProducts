/*
檔案整體說明

目的與定位：驗證 SpeechMessage.Hosting 的啟動守門在正式環境、語系發布政策及
Development 設定邊界上的 fail-closed 契約。測試只使用合成組態與暫存 appsettings，
不載入真實 User Secrets、不建立 CRM／LINE client、不改程序環境或目前目錄。

主要功能與流程：
本檔以反例注入偽語系、未發布語系、錯誤組織、通用 LINE token 及非允許測試端點，
斷言守門拒絕啟動且例外訊息只包含固定鍵名與原因，不包含 token、密碼、request body
或個資。正例使用 goodshepherdback 的合成 HTTPS 設定，確認離線安全設定可通過。

使用邊界與維護：
每個案例擁有自己的暫存目錄與 IConfiguration root，離開案例即 Dispose 並刪除檔案。
這些測試只證明純函式與合成設定行為，不能代替 ICU 子程序、D365、LINE 或金流
沙盒實機驗收；修改守門時須同步檢查 ChurchReport 既有 DevelopmentSafetyTests。
*/
using Microsoft.Extensions.Configuration;
using SpeechMessage.Localization;
using SpeechMessage.Hosting;
using Xunit;

namespace SpeechMessage.Hosting.Tests;

/// <summary>
/// 保護主機啟動時的語系與 Development 設定隔離。每個案例只使用合成輸入，
/// 並以固定錯誤邊界斷言拒絕結果，不接觸真實秘密或外部服務。
/// </summary>
public sealed class StartupGuardsTests
{
    /// <summary>正式環境不得啟用偽語系或把 qps-ploc 放入已發布清單。</summary>
    [Fact]
    public void Production_rejects_pseudo_locale_configuration()
    {
        Assert.Throws<InvalidOperationException>(() =>
            StartupGuards.ValidateLocalization(
                new LocalizationOptions(allowPseudoLocale: true),
                isProduction: true));

        Assert.Throws<InvalidOperationException>(() =>
            StartupGuards.ValidateLocalization(
                new LocalizationOptions(["zh-TW", "qps-ploc"]),
                isProduction: true));
    }

    /// <summary>正式環境不得把英文空殼或未知語系宣稱為已完成發布語系。</summary>
    [Fact]
    public void Production_rejects_unreleased_cultures()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            StartupGuards.ValidateLocalization(
                new LocalizationOptions(["zh-TW", "en"]),
                isProduction: true));

        Assert.DoesNotContain("secret", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Development 只接受指定測試組織、固定 HTTPS 端點及 DevTest User Secrets 來源。</summary>
    [Fact]
    public void Development_rejects_unsafe_configuration_without_echoing_values()
    {
        using var fixture = new ConfigurationFixture();
        fixture.WriteBaseConfiguration();
        var configuration = fixture.Build(new Dictionary<string, string?>
        {
            ["LineMessaging:DefaultOrganization"] = "Jesus",
            ["LineMessaging:Jesus:ChannelAccessToken"] = "token-private-A",
            ["CrmConnection:Organization"] = "other-org",
            ["CrmConnection:ServerUrl"] = "https://other.example.invalid/XRMServices/2011/Organization.svc"
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            StartupGuards.ValidateDevelopmentConfiguration(
                fixture.Root,
                "Development",
                configuration));

        Assert.DoesNotContain("token-private-A", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("other.example.invalid", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("password", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>合成的安全 Development 設定可通過，且不會建立外部連線或背景工作。</summary>
    [Fact]
    public void Development_accepts_bounded_test_configuration()
    {
        using var fixture = new ConfigurationFixture();
        fixture.WriteBaseConfiguration();
        var configuration = fixture.Build(new Dictionary<string, string?>
        {
            ["LineMessaging:DefaultOrganization"] = "DevTest",
            ["CrmConnection:Organization"] = "goodshepherdback",
            ["CrmConnection:ServerUrl"] = "https://goodshepherdback.speechmessage.com.tw/XRMServices/2011/Organization.svc",
            ["DynamicsAccess:Package01FeeReadsEnabled"] = "false"
        });

        StartupGuards.ValidateDevelopmentConfiguration(
            fixture.Root,
            "Development",
            configuration);
    }

    /// <summary>
    /// 本案例擁有合成 appsettings 與組態 root；Dispose 只刪除自己建立的檔案與目錄，
    /// 不讀取或修改使用者設定。
    /// </summary>
    private sealed class ConfigurationFixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "speechmessage-hosting-guard-" + Guid.NewGuid().ToString("N"));
        private string BasePath => Path.Combine(Root, "appsettings.json");

        internal ConfigurationFixture() => Directory.CreateDirectory(Root);

        internal void WriteBaseConfiguration()
        {
            File.WriteAllText(
                BasePath,
                """
                {
                  "LineMessaging": {
                    "Jesus": { "ChannelAccessToken": "" },
                    "DevTest": { "ChannelAccessToken": "" }
                  },
                  "CrmConnection": {
                    "Organization": "goodshepherdback",
                    "ServerUrl": "https://goodshepherdback.speechmessage.com.tw/XRMServices/2011/Organization.svc"
                  }
                }
                """);
        }

        internal IConfigurationRoot Build(IReadOnlyDictionary<string, string?> values)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Root)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddInMemoryCollection(values);
            return builder.Build();
        }

        /// <summary>關閉本案例組態 root 並刪除其固定暫存目錄。</summary>
        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}

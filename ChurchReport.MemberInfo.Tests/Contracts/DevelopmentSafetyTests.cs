using System.Reflection;
using System.Text;
using ChurchReport.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.UserSecrets;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Contracts;

/// <summary>
/// 保護 Development 不取用正式 token、CRM 只連指定測試組織與 Production 逐鍵相容的契約。
/// 所有輸入均為操作區域內假設定；不啟動 Host、不呼叫 CRM／LINE、不改程序環境或目前目錄。
/// 暫存檔、組態根與串流均由個別測試確定釋放，並行案例不共用可變狀態。
/// </summary>
public sealed class DevelopmentSafetyTests
{
    /// <summary>注入兩種 Development 大小寫；斷言每個既有別名都遮蔽基底假 token，且預設選到測試別名。</summary>
    /// <param name="environment">大小寫不同的開發環境名稱，用來注入相同隔離契約。</param>
    [Theory]
    [InlineData("Development")]
    [InlineData("development")]
    public void DevelopmentLayer_Development_HidesEveryBaseToken(string environment)
    {
        using var fixture = new SettingsFixture();
        var builder = fixture.BaseBuilder();
        builder.AddChurchReportDevelopmentOverrides(fixture.Root, environment, typeof(DevelopmentSafetyTests).Assembly);
        using var configuration = (ConfigurationRoot)builder.Build();
        configuration["LineMessaging:DefaultOrganization"].Should().Be("DevTest");
        configuration["LineMessaging:Jesus:ChannelAccessToken"].Should().BeEmpty();
        configuration["LineMessaging:JesusBack:ChannelAccessToken"].Should().BeEmpty();
        configuration.AsEnumerable().Should().NotContain(p => p.Value == "PROD-FAKE" || p.Value == "PROD-B-FAKE");
    }

    /// <summary>注入 Production、Staging 與缺省環境；斷言零新增 provider 且所有鍵和值與原基底相同。</summary>
    /// <param name="environment">正式、其他或缺省環境；均不得新增開發來源。</param>
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData(null)]
    public void DevelopmentLayer_NonDevelopment_PreservesProvidersAndEveryValue(string? environment)
    {
        using var fixture = new SettingsFixture();
        var builder = fixture.BaseBuilder();
        var sourceCount = builder.Sources.Count;
        using var baseline = (ConfigurationRoot)fixture.BaseBuilder().Build();
        builder.AddChurchReportDevelopmentOverrides(fixture.Root, environment!, typeof(DevelopmentSafetyTests).Assembly);
        builder.Sources.Count.Should().Be(sourceCount);
        using var effective = (ConfigurationRoot)builder.Build();
        effective.AsEnumerable().Should().BeEquivalentTo(baseline.AsEnumerable());
    }

    /// <summary>注入衝突與缺省名稱，斷言 DOTNET 優先、其次 ASPNETCORE，皆未指定才採 Production。</summary>
    /// <param name="dotnet">假 DOTNET 環境名稱，不寫入程序環境。</param>
    /// <param name="aspnetcore">假 ASPNETCORE 名稱，不寫入程序環境。</param>
    /// <param name="expected">獨立由 Host 規則決定的字面期望值。</param>
    [Theory]
    [InlineData("Production", "Development", "Production")]
    [InlineData("Development", "Production", "Development")]
    [InlineData(null, "Development", "Development")]
    [InlineData(null, null, "Production")]
    public void Environment_ConflictingInputs_UsesHostPrecedence(string? dotnet, string? aspnetcore, string expected)
        => ChurchReportDevelopmentConfiguration.ResolveEnvironmentName(dotnet!, aspnetcore!).Should().Be(expected);

    /// <summary>移除測試 token 時，守門仍允許安全設定，但各 token 值保持空白，不能退回正式別名。</summary>
    [Fact]
    public void DevelopmentGuard_SafeSettingsWithoutToken_PassesWithoutFallback()
    {
        using var fixture = new SettingsFixture();
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(SafeValues()).Build();
        Action action = () => ChurchReportDevelopmentConfiguration.ValidateDevelopmentConfiguration(fixture.Root, "Development", configuration);
        action.Should().NotThrow();
        configuration["LineMessaging:DevTest:ChannelAccessToken"].Should().BeNullOrEmpty();
    }

    /// <summary>逐一注入正式 token、錯誤預設別名、CRM 與 Embedded 主機；拒絕時只回報鍵名，不洩露任何假機密。</summary>
    /// <param name="key">本案例要破壞的設定鍵。</param>
    /// <param name="value">固定假值，絕非真實 token、帳密或個資。</param>
    [Theory]
    [InlineData("LineMessaging:Jesus:ChannelAccessToken", "PROD-FAKE")]
    [InlineData("LineMessaging:DefaultOrganization", "Jesus")]
    [InlineData("LineMessaging:DefaultOrganization", "")]
    [InlineData("CrmConnection:Organization", "sunnyvalechback")]
    [InlineData("CrmConnection:ServerUrl", "https://sunnyvalechback.speechmessage.com.tw")]
    [InlineData("DynamicsAccess:Embedded:OrganizationWebApiBaseUri", "https://sunnyvalechback.speechmessage.com.tw/api/data/v9.1")]
    [InlineData("DynamicsAccess:Embedded:ResourceUri", "https://sunnyvalechback.speechmessage.com.tw")]
    [InlineData("DynamicsAccess:Package01FeeReadsEnabled", "invalid")]
    [InlineData("LineMessaging:DevTest:ChannelAccessToken", "PROD-FAKE")]
    [InlineData("LINE_CHANNEL_ACCESS_TOKEN", "PROD-FAKE")]
    public void DevelopmentGuard_UnsafeSetting_RejectsWithoutSecret(string key, string value)
    {
        using var fixture = new SettingsFixture();
        var values = SafeValues();
        values[key] = value;
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        Action action = () => ChurchReportDevelopmentConfiguration.ValidateDevelopmentConfiguration(fixture.Root, "Development", configuration);
        var exception = action.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain(key).And.NotContain("PROD-FAKE").And.NotContain("PROD-B-FAKE").And.NotContain("DEV-FAKE");
    }

    /// <summary>Production 與未設定環境即使有不安全鍵和不存在的基底路徑，也必須完全略過 Development 守門。</summary>
    /// <param name="environment">不啟用開發守門的環境或 null。</param>
    [Theory]
    [InlineData("Production")]
    [InlineData(null)]
    public void DevelopmentGuard_NonDevelopment_DoesNotInspectOrReject(string? environment)
    {
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection().Build();
        Action action = () => ChurchReportDevelopmentConfiguration.ValidateDevelopmentConfiguration("not-a-real-base-path", environment!, configuration);
        action.Should().NotThrow();
    }

    /// <summary>以組件中繼資料驗證 UserSecretsId 真的存在；不開啟任何 secrets 檔案或使用者設定。</summary>
    [Fact]
    public void HostAssembly_UserSecretsId_IsAvailableToDevelopmentLoader()
    {
        var id = typeof(global::ChurchReport.Program).Assembly.GetCustomAttribute<UserSecretsIdAttribute>();
        id.Should().NotBeNull();
        id!.UserSecretsId.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>掃描本 worktree 的來源；既有 11 個 LINE 設定建構器與共用預設路徑均須接入覆寫，不得漏接或讀父 worktree。</summary>
    [Fact]
    public void SourceBuilders_AllLineConfigurationPaths_UseDevelopmentLayer()
    {
        var root = RepositoryRoot();
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var files = Directory.EnumerateFiles(Path.Combine(root, "SpeechMessageProducts.ChurchReport"), "*.cs", options)
            .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj"));
        var count = 0;
        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            if (!source.Contains("new ConfigurationBuilder()", StringComparison.Ordinal) || !source.Contains("AddJsonFile(" + (char)34 + "appsettings.json" + (char)34, StringComparison.Ordinal)) continue;
            // 這個已盤點的付款除錯器只讀日誌開關，維持原範圍；若日後讀 LINE，必須進入防護而不能繼續豁免。
            if (Path.GetFileName(file) == "DonationPaymentDebugLogger.cs")
            {
                source.Should().NotContain("LineMessaging:").And.NotContain("LineMessagingClient");
                continue;
            }
            count++;
            source.Should().Contain(".AddChurchReportDevelopmentOverrides(", "每個 LINE 基底檔讀取點都必須啟用相同的環境防護");
        }
        count.Should().Be(11);
        var library = File.ReadAllText(Path.Combine(root, "LineMessagingProcessor", "LineMessagingProcessorClass.cs"));
        library.Should().Contain("AddDevelopmentOverrides(");
        var program = File.ReadAllText(Path.Combine(root, "SpeechMessageProducts.ChurchReport", "Program.cs"));
        var guard = program.IndexOf("ValidateDevelopmentConfiguration(", StringComparison.Ordinal);
        guard.Should().BeGreaterThan(0);
        guard.Should().BeLessThan(program.IndexOf("ExceptionOutputOptions.FromConfiguration", StringComparison.Ordinal));
    }

    /// <summary>用反射呼叫共用程式庫實際覆寫接點，沒有主機 secrets 時預設 token 應為空，且不能出現基底假 token。</summary>
    [Fact]
    public void SharedDefaultPath_DevelopmentWithoutSecrets_CannotReturnBaseToken()
    {
        using var fixture = new SettingsFixture();
        var method = typeof(global::LineMessagingProcessor.LineMessagingProcessorClass).GetMethod("AddDevelopmentOverrides", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull("共用預設路徑也需要可傳入環境與 basePath 的隔離接點");
        var builder = fixture.BaseBuilder();
        method!.Invoke(null, new object[] { builder, fixture.Root, "Development" });
        using var configuration = (ConfigurationRoot)builder.Build();
        configuration["LineMessaging:DefaultOrganization"].Should().Be("DevTest");
        configuration["LineMessaging:DevTest:ChannelAccessToken"].Should().BeNullOrEmpty();
        configuration.AsEnumerable().Should().NotContain(x => x.Value == "PROD-FAKE" || x.Value == "PROD-B-FAKE");
    }

    /// <summary>建立可獨立判定的安全假設定；全部為字面測試資料，不借用產品 guard 計算期望值。</summary>
    private static Dictionary<string, string?> SafeValues() => new()
    {
        ["LineMessaging:DefaultOrganization"] = "DevTest",
        ["LineMessaging:Jesus:ChannelAccessToken"] = "",
        ["LineMessaging:JesusBack:ChannelAccessToken"] = "",
        ["CrmConnection:Organization"] = "goodshepherdback",
        ["CrmConnection:ServerUrl"] = "https://goodshepherdback.speechmessage.com.tw",
        ["DynamicsAccess:Package01FeeReadsEnabled"] = "true",
        ["DynamicsAccess:Embedded:OrganizationWebApiBaseUri"] = "https://goodshepherdback.speechmessage.com.tw/api/data/v9.1",
        ["DynamicsAccess:Embedded:ResourceUri"] = "https://goodshepherdback.speechmessage.com.tw"
    };

    /// <summary>只定位最近方案根目錄，找到後立即停止向上，避免巢狀 worktree 落到父分支。</summary>
    private static string RepositoryRoot()
    {
        var path = new DirectoryInfo(AppContext.BaseDirectory);
        while (path != null && !File.Exists(Path.Combine(path.FullName, "SpeechMessageProducts.sln"))) path = path.Parent;
        return path?.FullName ?? throw new DirectoryNotFoundException("找不到測試所屬 worktree。");
    }

    /// <summary>單一測試擁有兩個假 JSON；使用不可預測獨立路徑，不讀真實產品設定、User Secrets 或其他案例資料。</summary>
    private sealed class SettingsFixture : IDisposable
    {
        /// <summary>此案例專有的暫存根目錄；只包含固定假設定，Dispose 後整個目錄移除。</summary>
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "p0-sec-" + Guid.NewGuid().ToString("N"));

        /// <summary>建立基底假正式值及 Development 空白覆寫；故障由各測試另行注入，沒有任何外部連線。</summary>
        internal SettingsFixture()
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, "appsettings.json"), """
                {"LineMessaging":{"DefaultOrganization":"Jesus","Jesus":{"ChannelAccessToken":"PROD-FAKE"},"JesusBack":{"ChannelAccessToken":"PROD-B-FAKE"}},"Untouched":{"Value":"same"}}
                """, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(Root, "appsettings.Development.json"), """
                {"LineMessaging":{"DefaultOrganization":"DevTest","Jesus":{"ChannelAccessToken":""},"JesusBack":{"ChannelAccessToken":""}}}
                """, new UTF8Encoding(false));
        }

        /// <summary>每次建立獨立 builder；reload 停用避免 watcher，回傳的根由個別案例 Dispose。</summary>
        internal IConfigurationBuilder BaseBuilder() => new ConfigurationBuilder().SetBasePath(Root).AddJsonFile("appsettings.json", false, false);

        /// <summary>所有組態根已先 Dispose；只刪除此 fixture 已知的絕對暫存目錄，防止跨案例清除或保留假機密。</summary>
        public void Dispose()
        {
            var actual = Path.GetFullPath(Root);
            var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!actual.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(actual).StartsWith("p0-sec-", StringComparison.Ordinal))
                throw new InvalidOperationException("拒絕清理 fixture 以外的目錄。");
            Directory.Delete(actual, recursive: true);
        }
    }
}

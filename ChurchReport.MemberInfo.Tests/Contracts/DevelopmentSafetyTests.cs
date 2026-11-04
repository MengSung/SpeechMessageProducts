using System.Reflection;
using System.Text;
using ChurchReport.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
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

    /// <summary>注入 Production、Staging 與缺省環境；只追加專用 LINE 環境來源，且保留所有基底鍵和值。</summary>
    /// <param name="environment">正式、其他或缺省環境；不得新增 Development JSON 或 User Secrets。</param>
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
        builder.Sources.Count.Should().Be(sourceCount + 1);
        builder.Sources.Last().Should().BeOfType<EnvironmentVariablesConfigurationSource>()
            .Which.Prefix.Should().Be("LINE__");
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

    /// <summary>
    /// 基底假 token 全部清空後仍不得以既有正式別名當預設；斷言守門拒絕且不回顯設定內容。
    /// 每案只覆寫自己的合成 JSON，證明別名邊界獨立於 token 是否存在，不觸碰產品設定或外部服務。
    /// </summary>
    /// <param name="alias">刻意選用的既有正式別名，包含大小寫變體。</param>
    [Theory]
    [InlineData("Jesus")]
    [InlineData("JesusBack")]
    [InlineData("jesus")]
    public void DevelopmentGuard_EmptyBaselineTokens_StillRejectsProductionAlias(string alias)
    {
        using var fixture = new SettingsFixture();
        File.WriteAllText(Path.Combine(fixture.Root, "appsettings.json"),
            """{"LineMessaging":{"DefaultOrganization":"Jesus","Jesus":{"ChannelAccessToken":""},"JesusBack":{"ChannelAccessToken":""}}}""");
        var values = SafeValues();
        values["LineMessaging:DefaultOrganization"] = alias;
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        Action action = () => ChurchReportDevelopmentConfiguration.ValidateDevelopmentConfiguration(fixture.Root, "Development", configuration);
        action.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("LineMessaging:DefaultOrganization");
    }

    /// <summary>
    /// 基底正式 token 已清空時，Development 仍不得接受任何正式別名的非空值。
    /// 這個案例故意使用與基底不同的固定假值，避免測試只驗證舊有的「值相等」比較。
    /// </summary>
    [Fact]
    public void DevelopmentGuard_EmptyBaselineTokens_RejectsAnyProductionAliasToken()
    {
        using var fixture = new SettingsFixture();
        File.WriteAllText(Path.Combine(fixture.Root, "appsettings.json"),
            """{"LineMessaging":{"DefaultOrganization":"Jesus","Jesus":{"ChannelAccessToken":""},"JesusBack":{"ChannelAccessToken":""}}}""");
        var values = SafeValues();
        values["LineMessaging:Jesus:ChannelAccessToken"] = "UNRELATED-FAKE";
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        Action action = () => ChurchReportDevelopmentConfiguration.ValidateDevelopmentConfiguration(
            fixture.Root, "Development", configuration);

        var exception = action.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain("LineMessaging:Jesus:ChannelAccessToken")
            .And.NotContain("UNRELATED-FAKE");
    }

    /// <summary>
    /// 基底正式 token 清空後，Development 仍不得接受通用 LINE 環境 token；
    /// 使用與基底無關的假值證明拒絕條件不依賴憑證相等比較。
    /// </summary>
    [Fact]
    public void DevelopmentGuard_EmptyBaselineTokens_RejectsGenericEnvironmentToken()
    {
        using var fixture = new SettingsFixture();
        File.WriteAllText(Path.Combine(fixture.Root, "appsettings.json"),
            """{"LineMessaging":{"DefaultOrganization":"Jesus","Jesus":{"ChannelAccessToken":""},"JesusBack":{"ChannelAccessToken":""}}}""");
        var values = SafeValues();
        values["LINE_CHANNEL_ACCESS_TOKEN"] = "UNRELATED-FAKE";
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        Action action = () => ChurchReportDevelopmentConfiguration.ValidateDevelopmentConfiguration(
            fixture.Root, "Development", configuration);

        var exception = action.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain("LINE_CHANNEL_ACCESS_TOKEN")
            .And.NotContain("UNRELATED-FAKE");
    }

    /// <summary>
    /// Development 的 DevTest token 必須由 User Secrets provider 提供；
    /// 以記憶體 provider 注入同名值時必須 fail closed，且錯誤不得回顯假 token。
    /// </summary>
    [Fact]
    public void DevelopmentGuard_DevTestTokenFromNonSecretProvider_IsRejected()
    {
        using var fixture = new SettingsFixture();
        var values = SafeValues();
        values["LineMessaging:DevTest:ChannelAccessToken"] = "DEV-FAKE";
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        Action action = () => ChurchReportDevelopmentConfiguration.ValidateDevelopmentConfiguration(
            fixture.Root, "Development", configuration);

        var exception = action.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain("LineMessaging:DevTest:ChannelAccessToken")
            .And.NotContain("DEV-FAKE");
    }

    /// <summary>
    /// 非 Development 的舊式組態建構器必須加入專用 LINE__ 環境變數 provider，
    /// 讓正式 token 不必回到受追蹤的 JSON；測試只檢查 provider 類型，不碰程序環境。
    /// </summary>
    [Fact]
    public void NonDevelopmentLayer_AddsPrefixedLineEnvironmentProvider()
    {
        using var fixture = new SettingsFixture();
        var builder = fixture.BaseBuilder();

        builder.AddChurchReportDevelopmentOverrides(fixture.Root, "Production", typeof(DevelopmentSafetyTests).Assembly);

        builder.Sources.Should().ContainSingle(source => source is EnvironmentVariablesConfigurationSource);
    }

    /// <summary>
    /// 以正確測試主機搭配不安全協定、錯誤服務路徑或額外 URI 成分注入有效組態；守門須在建線前拒絕。
    /// 測試直接執行安全入口而非只檢查 JSON，目前端點全部為字面假資料；例外僅可含鍵名與固定原因。
    /// </summary>
    /// <param name="key">受測 CRM 或 Embedded 設定鍵。</param>
    /// <param name="uri">主機正確但其他部分錯誤的合成網址。</param>
    [Theory]
    [InlineData("CrmConnection:ServerUrl", "http://goodshepherdback.speechmessage.com.tw/XRMServices/2011/Organization.svc")]
    [InlineData("CrmConnection:ServerUrl", "https://goodshepherdback.speechmessage.com.tw/")]
    [InlineData("CrmConnection:ServerUrl", "https://goodshepherdback.speechmessage.com.tw/XRMServices/2011/Organization.svc?synthetic=1")]
    [InlineData("CrmConnection:ServerUrl", "https://goodshepherdback.speechmessage.com.tw/XRMServices/2011/Organization.svc#synthetic")]
    [InlineData("CrmConnection:ServerUrl", "https://synthetic@goodshepherdback.speechmessage.com.tw/XRMServices/2011/Organization.svc")]
    [InlineData("CrmConnection:ServerUrl", "https://goodshepherdback.speechmessage.com.tw:8443/XRMServices/2011/Organization.svc")]
    [InlineData("DynamicsAccess:Embedded:OrganizationWebApiBaseUri", "http://goodshepherdback.speechmessage.com.tw/api/data/v9.1")]
    [InlineData("DynamicsAccess:Embedded:ResourceUri", "http://goodshepherdback.speechmessage.com.tw")]
    public void DevelopmentGuard_UnsafeEndpoint_RejectsBeforeConnection(string key, string uri)
    {
        using var fixture = new SettingsFixture();
        var values = SafeValues();
        values[key] = uri;
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        Action action = () => ChurchReportDevelopmentConfiguration.ValidateDevelopmentConfiguration(fixture.Root, "Development", configuration);
        var exception = action.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain(key).And.NotContain(uri);
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
        library.Should().Contain("AddEnvironmentVariables(prefix: \"LINE__\")");
        library.Should().Contain("string.Equals(environmentName, \"Development\", StringComparison.OrdinalIgnoreCase)");
        library.IndexOf("builder.AddEnvironmentVariables();", StringComparison.Ordinal)
            .Should().BeLessThan(library.IndexOf("builder.AddEnvironmentVariables(prefix: \"LINE__\")", StringComparison.Ordinal));
        var program = File.ReadAllText(Path.Combine(root, "SpeechMessageProducts.ChurchReport", "Program.cs"));
        var lineEnvironment = program.IndexOf("AddChurchReportDeploymentLineEnvironmentOverrides(", StringComparison.Ordinal);
        lineEnvironment.Should().BeGreaterThan(0);
        lineEnvironment.Should().BeLessThan(program.IndexOf("ValidateDevelopmentConfiguration(", StringComparison.Ordinal));
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
        ["CrmConnection:ServerUrl"] = "https://goodshepherdback.speechmessage.com.tw/XRMServices/2011/Organization.svc",
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

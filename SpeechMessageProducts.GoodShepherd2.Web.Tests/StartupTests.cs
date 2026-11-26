/*
 * 檔案整體說明
 *
 * 目的與定位：保護好牧人 2.0 F2 的離線啟動邊界，驗證只有核定開發組織可以啟動，
 * 且真實 ToolUtility 註冊會把 GoodShepherd2 傳入完整連線隔離鍵，避免沿用舊產品的池。
 *
 * 主要功能與流程：以記憶體組態、合成 IHostEnvironment 與拒絕所有呼叫的 CRM factory
 * 注入不允許的環境及組織，再斷言啟動立即拒絕；產品案例使用真正 AddToolUtility、
 * DI manager 及 pool owner，透過只讀反射解析 manager 已接收的四個隔離欄位，
 * 比較同組織、同環境及同服務身分下的產品鍵與 pool owner，避免只驗證常數自我相等。
 *
 * 使用邊界與維護：所有值皆為合成資料，不讀 User Secrets、不變更程序環境，不呼叫
 * Acquire、WhoAmI 或任何外部端點，因此不能宣稱實際子池租借或實機連線已通過。
 * 每案唯一擁有其組態及 DI 容器，容器先 Dispose 以停止 pool timer，之後釋放組態；
 * 決定性斷言確認建線、租約與等待者皆為零，容器關閉後 manager 必須拒絕再使用。
 * 反射只讀 manager 的既有隔離值，不讀密碼、不在案外快取任何管理器或設定參照。
 */
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using SpeechMessageProducts.GoodShepherd2.Web.Startup;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.DependencyInjection;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd2.Web.Tests;

/// <summary>
/// 驗證 F2 開發環境守門及真實 DI 產品隔離契約；所有 fixture 皆為單案擁有，
/// 外部 factory 拒絕呼叫，讓誤建線立即失敗且不需存取任何實機或憑證。
/// </summary>
public sealed class StartupTests
{
    /// <summary>只供離線組態使用的保留網域與完整組織服務路徑，不可當成部署端點。</summary>
    private const string TestOrganizationUrl =
        "https://goodshepherd20.example/XRMServices/2011/Organization.svc";

    /// <summary>
    /// 注入舊產品組織與只允許 goodshepherd20 的部署清單，確認 Development 在建線前拒絕；
    /// 組態由本案釋放，合成環境不持有 Host、Session 或任何外部資源。
    /// </summary>
    [Fact]
    public void Startup_DevelopmentWithNonTestOrganization_Throws()
    {
        using var configuration = Configuration("https://goodshepherdback.example/");

        Assert.Throws<InvalidOperationException>(() =>
            GoodShepherd2StartupGuard.Validate(configuration, EnvironmentNamed("Development")));
    }

    /// <summary>
    /// 即使組織端點有效，Production 在 G5 開放前仍必須拒絕；
    /// 本案只執行純組態守門，不建立服務容器、檔案告警或網路工作。
    /// </summary>
    [Fact]
    public void Startup_Production_IsRefusedUntilG5()
    {
        using var configuration = Configuration();

        Assert.Throws<InvalidOperationException>(() =>
            GoodShepherd2StartupGuard.Validate(configuration, EnvironmentNamed("Production")));
    }

    /// <summary>
    /// 經真實 AddToolUtility 解析管理器已接收的完整隔離鍵，產品必須是 GoodShepherd2，
    /// 並保留合成環境、組織及服務身分；建線與租借計數必須為零。
    /// 容器結束後再操作 manager 必須失敗，確保它與 pool timer 不逃離容器生命週期。
    /// </summary>
    [Fact]
    public void Startup_UsesProductGoodShepherd2()
    {
        using var configuration = Configuration();
        var factory = new Mock<ICrmConnectionService>(MockBehavior.Strict);
        var services = Services(configuration, GoodShepherd2Product.Name, factory);
        DataverseConnectionManager manager;

        using (var provider = services.BuildServiceProvider())
        {
            manager = Assert.IsType<DataverseConnectionManager>(
                provider.GetRequiredService<IDataverseConnectionManager>());
            var key = ConnectionKey(manager);

            Assert.Equal("GoodShepherd2", key.Product);
            Assert.Equal("Development", key.Environment);
            Assert.Equal(TestOrganizationUrl, key.OrganizationUrl);
            Assert.Equal("synthetic-service", key.EffectiveIdentity);
            AssertEmptyPool(manager);
        }

        Assert.Throws<ObjectDisposedException>(() => manager.GetMetrics());
        factory.VerifyNoOtherCalls();
    }

    /// <summary>
    /// 同組織、同環境與同服務身分經兩個真實 DI 容器註冊新舊產品後，完整鍵必不相等，
    /// pool owner 也必不同；本案不呼叫 Acquire，因此斷言尚未建立任何實際 client 或子池。
    /// 兩個容器先後 Dispose，關閉後的 manager 都必須拒絕再用，factory 全程零呼叫。
    /// </summary>
    [Fact]
    public void Pool_GoodShepherd2AndGoodShepherd_UseDifferentSubPools()
    {
        using var configuration = Configuration();
        var factory = new Mock<ICrmConnectionService>(MockBehavior.Strict);
        var currentServices = Services(configuration, GoodShepherd2Product.Name, factory);
        var legacyServices = Services(configuration, "GoodShepherd", factory);
        DataverseConnectionManager current;
        DataverseConnectionManager legacy;

        using (var currentProvider = currentServices.BuildServiceProvider())
        using (var legacyProvider = legacyServices.BuildServiceProvider())
        {
            current = Assert.IsType<DataverseConnectionManager>(
                currentProvider.GetRequiredService<IDataverseConnectionManager>());
            legacy = Assert.IsType<DataverseConnectionManager>(
                legacyProvider.GetRequiredService<IDataverseConnectionManager>());
            var currentKey = ConnectionKey(current);
            var legacyKey = ConnectionKey(legacy);

            Assert.Equal("GoodShepherd2", currentKey.Product);
            Assert.Equal("GoodShepherd", legacyKey.Product);
            Assert.NotEqual(currentKey, legacyKey);
            Assert.Equal(currentKey.OrganizationUrl, legacyKey.OrganizationUrl);
            Assert.Equal(currentKey.Environment, legacyKey.Environment);
            Assert.Equal(currentKey.EffectiveIdentity, legacyKey.EffectiveIdentity);
            Assert.NotSame(current.Pool, legacy.Pool);
            AssertEmptyPool(current);
            AssertEmptyPool(legacy);
        }

        Assert.Throws<ObjectDisposedException>(() => current.GetMetrics());
        Assert.Throws<ObjectDisposedException>(() => legacy.GetMetrics());
        factory.VerifyNoOtherCalls();
    }

    /// <summary>
    /// 合成的完整 HTTPS 端點只有在短組織名稱精確符合第一個 DNS label，或完整主機
    /// 精確符合部署清單時才可啟動；明確預設埠與 DNS 大小寫不應改變合法組織身分。
    /// </summary>
    /// <param name="serverUrl">不會連線的合成端點。</param>
    /// <param name="allowedHost">受信任部署清單中的單一允許值。</param>
    [Theory]
    [InlineData(TestOrganizationUrl, "goodshepherd20")]
    [InlineData(TestOrganizationUrl, "goodshepherd20.example")]
    [InlineData(TestOrganizationUrl, "GOODSHEPHERD20.EXAMPLE")]
    [InlineData("https://GOODSHEPHERD20.example/XRMServices/2011/Organization.svc", "goodshepherd20")]
    [InlineData("https://goodshepherd20.example:443/XRMServices/2011/Organization.svc", "goodshepherd20")]
    [InlineData("https://goodshepherd20/XRMServices/2011/Organization.svc", "goodshepherd20")]
    public void Startup_DevelopmentWithExactAllowedHost_Passes(string serverUrl, string allowedHost)
    {
        using var configuration = Configuration(serverUrl);
        configuration["GoodShepherd2:AllowedDevelopmentOrganizations:0"] = allowedHost;

        GoodShepherd2StartupGuard.Validate(configuration, EnvironmentNamed("Development"));
    }

    /// <summary>
    /// 注入協定、憑證、埠、query、fragment、路徑正規化與主機前後綴等繞過輸入；
    /// 每案必須在任何服務解析與外部 I/O 前拋出 InvalidOperationException。
    /// </summary>
    /// <param name="serverUrl">應被拒絕的合成輸入；null 代表設定缺漏。</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-a-uri")]
    [InlineData("/XRMServices/2011/Organization.svc")]
    [InlineData("http://goodshepherd20.example/XRMServices/2011/Organization.svc")]
    [InlineData("https://goodshepherd20.example/")]
    [InlineData("https://goodshepherd20.example:444/XRMServices/2011/Organization.svc")]
    [InlineData("https://synthetic:credential@goodshepherd20.example/XRMServices/2011/Organization.svc")]
    [InlineData("https://@goodshepherd20.example/XRMServices/2011/Organization.svc")]
    [InlineData("https://goodshepherd20.example/XRMServices/2011/Organization.svc?synthetic=credential")]
    [InlineData("https://goodshepherd20.example/XRMServices/2011/Organization.svc?")]
    [InlineData("https://goodshepherd20.example/XRMServices/2011/Organization.svc#synthetic")]
    [InlineData("https://goodshepherd20.example/XRMServices/2011/Organization.svc#")]
    [InlineData("https://goodshepherd20.example/XRMServices/2011/Organization.svc/")]
    [InlineData("https://goodshepherd20.example/org/XRMServices/2011/Organization.svc")]
    [InlineData("https://goodshepherd20.example/prefix/../XRMServices/2011/Organization.svc")]
    [InlineData("https://goodshepherd20.example/XRMServices/2011/../2011/Organization.svc")]
    [InlineData("https://goodshepherd20.example/%58RMServices/2011/Organization.svc")]
    [InlineData("https://goodshepherd20.example\\XRMServices\\2011\\Organization.svc")]
    [InlineData(" https://goodshepherd20.example/XRMServices/2011/Organization.svc")]
    [InlineData("https://goodshepherd20.example/XRMServices/2011/Organization.svc\r\n")]
    [InlineData("https://goodshepherd20.example./XRMServices/2011/Organization.svc")]
    [InlineData("https://goodshepherd200.example/XRMServices/2011/Organization.svc")]
    [InlineData("https://evilgoodshepherd20.example/XRMServices/2011/Organization.svc")]
    [InlineData("https://attacker.goodshepherd20.example/XRMServices/2011/Organization.svc")]
    [InlineData("https://goodshepherdback.example/XRMServices/2011/Organization.svc")]
    public void Startup_UnsafeOrNonTestEndpoint_Throws(string? serverUrl)
    {
        using var configuration = Configuration(serverUrl);

        Assert.Throws<InvalidOperationException>(() =>
            GoodShepherd2StartupGuard.Validate(configuration, EnvironmentNamed("Development")));
    }

    /// <summary>
    /// 完整主機清單不可退化為 suffix、substring、萬用字元或空白比對；
    /// 設定值只活在單案記憶體組態，失敗不應發布可供後續請求使用的部分結果。
    /// </summary>
    /// <param name="allowedHost">未精確允許合成組織的部署值。</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("*.example")]
    [InlineData("goodshepherd2")]
    [InlineData("example")]
    [InlineData("goodshepherd20.other.example")]
    [InlineData("goodshepherd20.example.attacker")]
    [InlineData("https://goodshepherd20.example")]
    public void Startup_DevelopmentWithoutExactAllowedHost_Throws(string? allowedHost)
    {
        using var configuration = Configuration();
        configuration["GoodShepherd2:AllowedDevelopmentOrganizations:0"] = allowedHost;

        Assert.Throws<InvalidOperationException>(() =>
            GoodShepherd2StartupGuard.Validate(configuration, EnvironmentNamed("Development")));
    }

    /// <summary>
    /// 完整主機清單只能允許那一台主機，即使攻擊主機仍以 goodshepherd20 開頭，
    /// 也不能以共同前綴或後綴誤放行；本案使用完整服務路徑隔離主機比對這個失敗點。
    /// </summary>
    /// <param name="host">與允許完整主機近似、但不相等的合成主機。</param>
    [Theory]
    [InlineData("goodshepherd20.example.attacker")]
    [InlineData("goodshepherd20.attacker.example")]
    [InlineData("attacker.goodshepherd20.example")]
    public void Startup_FullHostAllowList_DoesNotMatchPrefixOrSuffix(string host)
    {
        using var configuration = Configuration(
            $"https://{host}/XRMServices/2011/Organization.svc");
        configuration["GoodShepherd2:AllowedDevelopmentOrganizations:0"] = "goodshepherd20.example";

        Assert.Throws<InvalidOperationException>(() =>
            GoodShepherd2StartupGuard.Validate(configuration, EnvironmentNamed("Development")));
    }

    /// <summary>
    /// 清單逐項從有效組態讀取，第一筆不匹配時仍能找到後面的完整允許主機；
    /// 不建立靜態清單副本或連線，單案結束即釋放全部設定參照。
    /// </summary>
    [Fact]
    public void Startup_AllowedHostInLaterConfigurationEntry_Passes()
    {
        using var configuration = Configuration();
        configuration["GoodShepherd2:AllowedDevelopmentOrganizations:0"] = "goodshepherd20.other.example";
        configuration["GoodShepherd2:AllowedDevelopmentOrganizations:1"] = "goodshepherd20.example";

        GoodShepherd2StartupGuard.Validate(configuration, EnvironmentNamed("Development"));
    }

    /// <summary>
    /// 即使部署清單誤列舊好牧人或其他組織，也不能開放本產品連往該端點；
    /// 以完整合法服務路徑排除「只是缺路徑」的假陽性，不進行實際 DNS 或版本探測。
    /// </summary>
    /// <param name="host">應禁止的合成舊組織或其他產品主機。</param>
    [Theory]
    [InlineData("goodshepherdback.example")]
    [InlineData("goodshepherd.example")]
    [InlineData("crm82.example")]
    [InlineData("construction.example")]
    [InlineData("association.example")]
    public void Startup_AllowListCannotEnableOtherOrganizations_Throws(string host)
    {
        using var configuration = Configuration(
            $"https://{host}/XRMServices/2011/Organization.svc");
        configuration["GoodShepherd2:AllowedDevelopmentOrganizations:0"] = host;

        Assert.Throws<InvalidOperationException>(() =>
            GoodShepherd2StartupGuard.Validate(configuration, EnvironmentNamed("Development")));
    }

    /// <summary>
    /// D27 只在來源為 null 時回退，DOTNET 即使為空字串或未知名稱仍優先；
    /// 純方法不能讀程序環境或自行把未識別值降級為可啟動的 Development。
    /// </summary>
    /// <param name="dotnetEnvironment">合成 DOTNET 快照，null 代表未設定。</param>
    /// <param name="aspnetcoreEnvironment">合成 ASPNETCORE 快照，null 代表未設定。</param>
    /// <param name="expected">獨立指定的 D27 選擇結果。</param>
    [Theory]
    [InlineData(null, null, "Production")]
    [InlineData(null, "Development", "Development")]
    [InlineData("Development", "Production", "Development")]
    [InlineData("Production", "Development", "Production")]
    [InlineData("Staging", "Development", "Staging")]
    [InlineData("", "Development", "")]
    [InlineData(" ", "Development", " ")]
    public void Environment_DotnetThenAspnetcoreThenProduction(
        string? dotnetEnvironment, string? aspnetcoreEnvironment, string expected)
    {
        Assert.Equal(expected, GoodShepherd2StartupGuard.ResolveEnvironmentName(
            dotnetEnvironment, aspnetcoreEnvironment));
    }

    /// <summary>
    /// 注入未知、空白或拼寫近似環境，證明只有完整 Development 名稱可啟動；
    /// 組態內雖有 Development，也不能覆寫已由組合根選定的環境快照。
    /// </summary>
    /// <param name="environmentName">應拒絕的合成環境快照。</param>
    [Theory]
    [InlineData("Staging")]
    [InlineData("Test")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Development ")]
    public void Startup_UnknownEnvironment_FailsClosed(string environmentName)
    {
        using var configuration = Configuration();

        Assert.Throws<InvalidOperationException>(() =>
            GoodShepherd2StartupGuard.Validate(configuration, EnvironmentNamed(environmentName)));
    }

    /// <summary>
    /// 驗證部署環境比對維持 ordinal 大小寫忽略，且只依借入 Host 快照；
    /// 組態故意列 Production，不能影響已選定的 Development，不更動程序環境。
    /// </summary>
    /// <param name="environmentName">合法但大小寫不同的 Development 名稱。</param>
    [Theory]
    [InlineData("development")]
    [InlineData("DEVELOPMENT")]
    public void Startup_HostDevelopmentSnapshot_IsAuthoritative(string environmentName)
    {
        using var configuration = Configuration();
        configuration["DOTNET_ENVIRONMENT"] = "Production";
        configuration["ASPNETCORE_ENVIRONMENT"] = "Production";

        GoodShepherd2StartupGuard.Validate(configuration, EnvironmentNamed(environmentName));
    }

    /// <summary>
    /// 部署清單修正後必須重新驗證當次有效組態，不可重用其他呼叫的成功或失敗快取；
    /// 單案順序注入允許、拒絕、恢復，決定性斷言保護無共享可變狀態的契約。
    /// </summary>
    [Fact]
    public void Startup_AllowedHostChanges_AreRevalidatedWithoutSharedState()
    {
        using var configuration = Configuration();
        var environment = EnvironmentNamed("Development");
        GoodShepherd2StartupGuard.Validate(configuration, environment);
        configuration["GoodShepherd2:AllowedDevelopmentOrganizations:0"] = "goodshepherd20.other.example";

        Assert.Throws<InvalidOperationException>(() =>
            GoodShepherd2StartupGuard.Validate(configuration, environment));

        configuration["GoodShepherd2:AllowedDevelopmentOrganizations:0"] = "goodshepherd20.example";
        GoodShepherd2StartupGuard.Validate(configuration, environment);
    }

    /// <summary>
    /// 將合成帳密、query 與未知環境名稱放入拒絕輸入，錯誤仍只能使用固定理由；
    /// 相同失敗分類的訊息必相等且不含輸入值，避免啟動日誌外洩部署設定。
    /// </summary>
    [Fact]
    public void Startup_RejectionMessages_DoNotContainConfigurationOrEnvironmentValues()
    {
        using var credentialConfiguration = Configuration(
            "https://synthetic:credential@goodshepherd20.example/XRMServices/2011/Organization.svc");
        using var queryConfiguration = Configuration(
            "https://goodshepherd20.example/XRMServices/2011/Organization.svc?synthetic=credential");
        var environment = EnvironmentNamed("Development");

        var credentialFailure = Assert.Throws<InvalidOperationException>(() =>
            GoodShepherd2StartupGuard.Validate(credentialConfiguration, environment));
        var queryFailure = Assert.Throws<InvalidOperationException>(() =>
            GoodShepherd2StartupGuard.Validate(queryConfiguration, environment));
        var productionFailure = Assert.Throws<InvalidOperationException>(() =>
            GoodShepherd2StartupGuard.Validate(queryConfiguration, EnvironmentNamed("Production")));
        var unknownFailure = Assert.Throws<InvalidOperationException>(() =>
            GoodShepherd2StartupGuard.Validate(queryConfiguration, EnvironmentNamed("synthetic-private-environment")));

        Assert.Equal(credentialFailure.Message, queryFailure.Message);
        Assert.Equal(productionFailure.Message, unknownFailure.Message);
        Assert.DoesNotContain("credential", credentialFailure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("goodshepherd20.example", credentialFailure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-private-environment", unknownFailure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 建立不含機密、檔案 provider 或 watcher 的單案組態；同時明確提供兩個環境鍵，
    /// 避免既有 ToolUtility 的環境回退碰到程序設定。呼叫端必須在容器之後 Dispose。
    /// </summary>
    /// <param name="serverUrl">合成組織端點；不會被拿去建立連線。</param>
    /// <returns>本案唯一擁有的記憶體組態。</returns>
    private static ConfigurationManager Configuration(string? serverUrl = TestOrganizationUrl)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CrmConnection:ServerUrl"] = serverUrl,
            ["CrmConnection:Username"] = "synthetic-service",
            ["GoodShepherd2:AllowedDevelopmentOrganizations:0"] = "goodshepherd20",
            ["DOTNET_ENVIRONMENT"] = "Development",
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Dataverse:Pool:MinSize"] = "1",
            ["Dataverse:Pool:MaxN"] = "1"
        });
        return configuration;
    }

    /// <summary>以單案合成環境名稱代替真實 Host，不讀或修改任何程序環境變數。</summary>
    /// <param name="environmentName">欲注入守門的完整環境名稱。</param>
    /// <returns>只提供環境名稱的無資源替身。</returns>
    private static IHostEnvironment EnvironmentNamed(string environmentName) =>
        Mock.Of<IHostEnvironment>(environment => environment.EnvironmentName == environmentName);

    /// <summary>
    /// 只替換外部 CRM factory，保留產品註冊、管理器與 pool 的真實組裝；
    /// 呼叫端擁有服務容器，factory 不接受任何方法呼叫，禁止離線案例意外建線。
    /// </summary>
    /// <param name="configuration">由案例擁有且明確提供隔離邊界的記憶體組態。</param>
    /// <param name="product">組合根提供的新或舊產品名稱。</param>
    /// <param name="factory">拒絕一切外部連線操作的合成依賴。</param>
    /// <returns>尚未建置的服務集合；不建立隱藏容器。</returns>
    private static ServiceCollection Services(
        IConfiguration configuration, string product, Mock<ICrmConnectionService> factory)
    {
        var services = new ServiceCollection();
        services.AddSingleton(factory.Object);
        services.AddToolUtility(configuration, product);
        return services;
    }

    /// <summary>
    /// 只讀 manager 由真實 DI 注入的四個隔離欄位，組成 Acquire 將使用的完整鍵；
    /// 不呼叫 Acquire、不製造子池、不讀密碼，也不為測試擴張產品診斷 API。
    /// </summary>
    /// <param name="manager">本案容器已解析、尚未釋放的真實管理器。</param>
    /// <returns>由管理器實際接收值組成的不可變連線鍵。</returns>
    private static DataverseConnectionKey ConnectionKey(DataverseConnectionManager manager) =>
        new(Boundary(manager, "_product"), Boundary(manager, "_environment"),
            Boundary(manager, "_organizationUrl"), Boundary(manager, "_effectiveIdentity"));

    /// <summary>反射只讀指定隔離欄位；欄位不存在或型別改變時讓測試明確失敗。</summary>
    /// <param name="manager">只活在單案容器內的真實管理器。</param>
    /// <param name="fieldName">本檔固定選定的非機密隔離欄位名稱。</param>
    /// <returns>管理器接收的原始字串；不複製到任何共享狀態。</returns>
    private static string Boundary(DataverseConnectionManager manager, string fieldName)
    {
        var field = typeof(DataverseConnectionManager).GetField(
            fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<string>(field.GetValue(manager));
    }

    /// <summary>以真實 pool 計數證明產品註冊與解析未建線、未租借、未留下等待者或子池。</summary>
    /// <param name="manager">本案容器擁有、尚未釋放的管理器。</param>
    private static void AssertEmptyPool(DataverseConnectionManager manager)
    {
        var metrics = manager.GetMetrics();
        Assert.Equal(0, metrics.Created);
        Assert.Equal(0, metrics.TotalAcquires);
        Assert.Equal(0, metrics.Leased);
        Assert.Equal(0, metrics.Waiting);
        Assert.Equal(0, metrics.SubPoolCount);
    }
}

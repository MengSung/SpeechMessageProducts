using System.Collections.Concurrent;
using ChurchReport.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 驗證 ChurchReport Dynamics process host 的唯一擁有權、單一設定世代與確定性釋放契約。
/// 這些測試刻意不連線到真實 Gateway；它們只建立產品端 provider／executor 圖，確認主 DI 擁有的
/// singleton 是唯一 mutable owner，而舊 static facade 僅轉送到已由 hosted lifecycle 發佈的 owner。
/// 測試同時保護併行 Dispose 的冪等性，避免關機競爭重複釋放 HttpClientFactory handler、timer 或 socket pool。
/// </summary>
public sealed class DonationDynamicsAccessBootstrapLifecycleTests
{
    /// <summary>
    /// 驗證同一 host 只建立一個不可變設定世代；同設定重入必須重用 executor，設定變更則必須 fail-closed
    /// 並要求先完成 restart／Dispose。測試確認舊 owner 在 Dispose 後成為 terminal，只有新 Generic Host owner
    /// 才能建立新世代，保護 restart-required 與 cleanup 契約，且不保留舊 endpoint、handler 或 provider 強參考。
    /// </summary>
    [Fact]
    public async Task Process_host_reuses_one_generation_and_requires_restart_for_configuration_changes()
    {
        var host = new DonationDynamicsAccessProcessHost();

        try
        {
            var first = DonationDynamicsAccessBootstrap.BindOptions(
                CreateGatewayConfiguration("https://gateway-a.internal/"));
            var replacement = DonationDynamicsAccessBootstrap.BindOptions(
                CreateGatewayConfiguration("https://gateway-b.internal/"));

            var firstExecutor = host.GetOrCreateGatewayExecutor(first);
            host.GetOrCreateGatewayExecutor(first).Should().BeSameAs(firstExecutor);

            var action = () => host.GetOrCreateGatewayExecutor(replacement);
            action.Should().Throw<InvalidOperationException>()
                .WithMessage("*restart*");

            await host.DisposeAsync();

            var disposedAction = () => host.GetOrCreateGatewayExecutor(replacement);
            disposedAction.Should().Throw<ObjectDisposedException>();

            await using var replacementHost = new DonationDynamicsAccessProcessHost();
            replacementHost.GetOrCreateGatewayExecutor(replacement).Should().NotBeSameAs(firstExecutor);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    /// <summary>
    /// 驗證多個關機路徑同時要求 Dispose 時只有 process host 會序列化 provider cleanup，所有呼叫者都能
    /// 等待同一個確定性結果，且完成後所有遲到的 GetOrCreate 都 fail-closed。這個故障模型對應
    /// Generic Host StopAsync 與 DI container DisposeAsync 可能先後觸發的真實關機順序。
    /// </summary>
    [Fact]
    public async Task Process_host_dispose_is_concurrent_idempotent_and_terminal()
    {
        var host = new DonationDynamicsAccessProcessHost();
        var first = DonationDynamicsAccessBootstrap.BindOptions(
            CreateGatewayConfiguration("https://gateway-a.internal/"));
        host.GetOrCreateGatewayExecutor(first);

        var disposeTasks = Enumerable.Range(0, 8)
            .Select(_ => host.DisposeAsync().AsTask())
            .ToArray();

        await Task.WhenAll(disposeTasks);

        var action = () => host.GetOrCreateGatewayExecutor(first);
        action.Should().Throw<ObjectDisposedException>();
        await host.DisposeAsync();
    }

    /// <summary>
    /// 驗證舊 static 呼叫點仍可經 hosted lifecycle 使用主 DI 的 singleton，但 static 類別本身不再擁有
    /// ServiceProvider。Start 發佈相容 facade，Stop 先撤銷新呼叫再 Dispose owner，避免關機期間建立第二個
    /// handler generation，亦避免把 provider 存活時間延長到跨 host／跨測試邊界。
    /// </summary>
    [Fact]
    public async Task Legacy_static_facade_uses_the_hosted_singleton_without_owning_a_provider()
    {
        var host = new DonationDynamicsAccessProcessHost();
        var lifetime = new DonationDynamicsAccessBootstrapLifetime(host);
        var configuration = CreateGatewayConfiguration("https://gateway-a.internal/");

        await lifetime.StartAsync(CancellationToken.None);
        try
        {
            DonationDynamicsAccessBootstrap.TryCreatePackage01Client(configuration)
                .Should().NotBeNull();
        }
        finally
        {
            await lifetime.StopAsync(CancellationToken.None);
        }

        var action = () => DonationDynamicsAccessBootstrap.TryCreatePackage01Client(configuration);
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*host*started*");
    }

    /// <summary>
    /// 模擬 preflight 使 Generic Host 啟動失敗後，DI container 直接 Dispose singleton、未先呼叫 lifecycle
    /// StopAsync 的 rollback 路徑。process host 必須自行撤銷精確相同的 static facade 參考；主要 assertion 是
    /// 後續舊呼叫點回報 host 未啟動，而不是穿越到已 terminal owner 丟出 ObjectDisposedException。
    /// </summary>
    [Fact]
    public async Task Direct_di_disposal_detaches_the_legacy_static_facade()
    {
        var host = new DonationDynamicsAccessProcessHost();
        var lifetime = new DonationDynamicsAccessBootstrapLifetime(host);
        var configuration = CreateGatewayConfiguration("https://gateway-a.internal/");

        await lifetime.StartAsync(CancellationToken.None);
        await host.DisposeAsync();

        var action = () => DonationDynamicsAccessBootstrap.TryCreatePackage01Client(configuration);
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*host*started*");

        await lifetime.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// 以反射保護 static 信任邊界：過渡 facade 可以保存不具資源擁有權的介面參考，但不得再次引入
    /// static ServiceProvider 或無界 provider dictionary。若此測試失敗，代表 HTTP pool、timer、token cache
    /// 可能脫離 Generic Host 的 shutdown owner，形成跨世代保留或記憶體／socket 洩漏。
    /// </summary>
    [Fact]
    public void Bootstrap_does_not_retain_a_static_provider_owner_or_unbounded_provider_dictionary()
    {
        var staticFields = typeof(DonationDynamicsAccessBootstrap).GetFields(
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public);

        staticFields.Should().NotContain(field =>
            typeof(IServiceProvider).IsAssignableFrom(field.FieldType));
        staticFields.Should().NotContain(field =>
            field.FieldType.IsGenericType &&
            field.FieldType.GetGenericTypeDefinition() == typeof(ConcurrentDictionary<,>));
    }

    /// <summary>
    /// 驗證 Startup 只註冊一個 singleton abstraction／implementation 映射，讓 Generic Host 成為 process host
    /// 的唯一生命週期 owner。此測試只檢查 descriptor，不解析網站其他 CRM singleton，因此不會接觸帳密、
    /// 真實 endpoint 或建立任何外部連線。
    /// </summary>
    [Fact]
    public void Startup_registers_the_process_host_as_one_main_di_singleton()
    {
        var services = new ServiceCollection();
        var startup = new ChurchReport.Startup(CreateDisabledConfiguration());

        startup.ConfigureServices(services);

        var registration = services.Should()
            .ContainSingle(descriptor =>
                descriptor.ServiceType == typeof(IDonationDynamicsAccessProcessHost))
            .Subject;
        registration.Lifetime.Should().Be(ServiceLifetime.Singleton);
        registration.ImplementationType.Should().Be(typeof(DonationDynamicsAccessProcessHost));
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService) &&
            descriptor.ImplementationType == typeof(DonationDynamicsAccessBootstrapLifetime));
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService) &&
            descriptor.ImplementationType == typeof(DynamicsGatewayPreflightHostedService));
    }

    /// <summary>
    /// 驗證 Visual Studio Development profile 依 ASP.NET Core 的基底後覆寫順序，明確把 ChurchReport 指向
    /// localhost Local Gateway 與 Gateway 已授權的 <c>crm82</c> profile；同時 Package 1 consumer flag 必須
    /// 維持關閉，確保目前只完成 hosting/configuration 對齊，不會建立 ProductClient、HTTP handler、token cache
    /// 或送出任何 Dynamics operation。測試只讀 checked-in JSON，不解析 Embedded credential reference 的值，
    /// 也不建立網路、Session、Timer 或背景工作；localhost URI assertion 防止 Development 設定誤指 Central／正式 endpoint。
    /// </summary>
    [Fact]
    public void Development_configuration_selects_local_gateway_while_package01_reads_remain_disabled()
    {
        var repositoryRoot = FindRepositoryRoot();
        var applicationRoot = Path.Combine(repositoryRoot, "SpeechMessageProducts.ChurchReport");
        var configuration = new ConfigurationBuilder()
            .SetBasePath(applicationRoot)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: false, reloadOnChange: false)
            .Build();

        configuration.GetValue<bool>("DynamicsAccess:Package01FeeReadsEnabled").Should().BeFalse();
        configuration["DynamicsAccess:ExecutionMode"].Should().Be("Gateway");
        configuration["DynamicsAccess:ProfileAlias"].Should().Be("crm82");
        configuration["DynamicsAccess:CeVersion"].Should().Be("8.2");
        configuration["DynamicsAccess:Gateway:ApiPrefix"].Should().Be("/v1");

        var endpointText = configuration["DynamicsAccess:Gateway:Endpoint"];
        Uri.TryCreate(endpointText, UriKind.Absolute, out var endpoint).Should().BeTrue();
        endpoint.Should().NotBeNull();
        endpoint!.Scheme.Should().Be(Uri.UriSchemeHttps);
        endpoint.IsLoopback.Should().BeTrue();
        endpoint.Port.Should().Be(7244);
    }

    /// <summary>
    /// 建立 feature flag 關閉的最小設定，供 Startup descriptor 測試使用；不提供任何 credential 或真實 CRM
    /// endpoint，確保測試只穿越服務註冊邊界而不誤觸外部資源。
    /// </summary>
    private static IConfiguration CreateDisabledConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DynamicsAccess:Package01FeeReadsEnabled"] = "false"
            })
            .Build();
    }

    /// <summary>
    /// 建立不含秘密的 Gateway 設定；endpoint 僅使用保留的 internal 測試名稱，測試不會送出 HTTP。
    /// </summary>
    /// <param name="endpoint">用來區分不可變 process generation 的 HTTPS 測試位址。</param>
    /// <returns>可由 production binding helper 解析的記憶體設定。</returns>
    private static IConfiguration CreateGatewayConfiguration(string endpoint)
    {
        var values = new Dictionary<string, string?>
        {
            ["DynamicsAccess:Package01FeeReadsEnabled"] = "true",
            ["DynamicsAccess:ExecutionMode"] = "Gateway",
            ["DynamicsAccess:ProfileAlias"] = "jesus-prod",
            ["DynamicsAccess:Gateway:Endpoint"] = endpoint,
            ["DynamicsAccess:Gateway:ApiPrefix"] = "/v1"
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    /// <summary>
    /// 從測試輸出目錄向上尋找同時包含 ChurchReport 與 Dynamics Gateway 專案的目前 worktree root。
    /// 這個 fail-closed 探索避免測試依賴 process working directory 而誤讀另一個 checkout；方法只建立短命的
    /// <see cref="DirectoryInfo"/> 物件，不持有 file handle、watcher 或 cache，找不到可信根目錄時立即失敗。
    /// </summary>
    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
             current is not null;
             current = current.Parent)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "SpeechMessageProducts.ChurchReport")) &&
                Directory.Exists(Path.Combine(current.FullName, "SpeechMessage.Dynamics.Gateway")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "找不到同時包含 ChurchReport 與 Dynamics Gateway 專案的目前 repository root。");
    }
}

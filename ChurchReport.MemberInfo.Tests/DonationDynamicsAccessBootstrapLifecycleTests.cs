using System.Collections.Concurrent;
using ChurchReport.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.MemberInfo;
using SpeechMessage.Dynamics.ProductClient.SpecialResources;
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
    /// 驗證 Embedded 設定可以在不提供 Gateway endpoint 時完成純產品選項繫結。此測試刻意帶入
    /// CrmConnection 密碼與舊 Embedded 欄位，並斷言選項物件只保存 mode／alias；因此產品設定不會把
    /// endpoint、credential 或 secret-reference 複製到長生命週期 process host 或 session。
    /// </summary>
    [Fact]
    public void Bind_options_accepts_embedded_without_gateway_endpoint_or_secret_projection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DynamicsAccess:Package01FeeReadsEnabled"] = "true",
                ["DynamicsAccess:ConnectionMode"] = "Embedded",
                ["DynamicsAccess:ProfileAlias"] = "legacy-embedded",
                ["DynamicsAccess:Embedded:OrganizationWebApiBaseUri"] = "https://crm.invalid/api/data/v9.1/",
                ["DynamicsAccess:Embedded:SecretReference"] = "test-secret-reference",
                ["CrmConnection:Password"] = "test-password"
            })
            .Build();

        var options = DonationDynamicsAccessBootstrap.BindOptions(configuration);

        options.ConnectionMode.Should().Be(SpeechMessage.Dynamics.Abstractions.Execution.ConnectionMode.Embedded);
        options.ProfileAlias.Should().Be("legacy-embedded");
        options.Gateway!.Endpoint.Should().BeEmpty();
    }

    /// <summary>
    /// 保護 P7.2 contact basic-info consumer 尚未啟用時是嚴格 no-op：即使沒有已啟動的 process host，
    /// composition helper 也只能回傳 null，不建立 executor、HTTP handler、Data8 pool、credential 或任何
    /// ChurchReport 流量。未來 P7.4 只需在獨立 reviewed flag 變為 true 後接入同一個 host owner。
    /// </summary>
    [Fact]
    public void Package02_contact_updates_remain_disabled_by_default_before_host_resolution()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        DonationDynamicsAccessBootstrap.IsPackage02ContactBasicInfoUpdatesEnabled(configuration)
            .Should().BeFalse();
        DonationDynamicsAccessBootstrap.TryCreatePackage02ContactBasicInfoClient(configuration)
            .Should().BeNull();
    }

    /// <summary>
    /// 保護未來 reviewed flag 開啟時仍可由主 DI 注入既有 typed client，而不要求 helper 另建 provider。
    /// 測試使用不擁有資源的純記憶體替身；它只證明 flag／composition 邊界，不執行 contact write 或 CE operation。
    /// </summary>
    [Fact]
    public void Package02_contact_updates_accept_an_injected_client_only_when_flag_is_enabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DynamicsAccess:Package02ContactBasicInfoUpdatesEnabled"] = "true"
            })
            .Build();
        var injected = new DisabledContactBasicInfoClient();

        DonationDynamicsAccessBootstrap.IsPackage02ContactBasicInfoUpdatesEnabled(configuration)
            .Should().BeTrue();
        DonationDynamicsAccessBootstrap.TryCreatePackage02ContactBasicInfoClient(configuration, injected)
            .Should().BeSameAs(injected);
    }

    /// <summary>
    /// 保護 P7.2 Slice B consumer composition 預設為嚴格 no-op。故障注入是沒有 process host、endpoint 或
    /// credential 的空設定；決定性斷言是 flag=false 且 helper 回傳 null，因而不建立 executor、HTTP handler、
    /// Data8 pool、metadata cache、session、timer 或 ChurchReport 流量。
    /// </summary>
    [Fact]
    public void Package02_contact_profile_operations_remain_disabled_by_default_before_host_resolution()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        DonationDynamicsAccessBootstrap.IsPackage02ContactProfileOperationsEnabled(configuration)
            .Should().BeFalse();
        DonationDynamicsAccessBootstrap.TryCreatePackage02ContactProfileClient(configuration)
            .Should().BeNull();
    }

    /// <summary>
    /// 保護 reviewed flag 未來開啟時只借用主 DI 注入的 stateless Slice B typed client，不自行建立 provider 或
    /// transport。故障注入是 in-memory injected client；決定性斷言是原物件被回傳且沒有執行 B1 write／B2 query、
    /// CE operation、LINE call、timer、stream 或 background work。
    /// </summary>
    [Fact]
    public void Package02_contact_profile_operations_accept_an_injected_client_only_when_flag_is_enabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DynamicsAccess:Package02ContactProfileOperationsEnabled"] = "true"
            })
            .Build();
        var injected = new DisabledContactProfileClient();

        DonationDynamicsAccessBootstrap.IsPackage02ContactProfileOperationsEnabled(configuration)
            .Should().BeTrue();
        DonationDynamicsAccessBootstrap.TryCreatePackage02ContactProfileClient(configuration, injected)
            .Should().BeSameAs(injected);
    }

    /// <summary>
    /// 保護 P7.4 Package03 圖片 consumer 在預設 gate 關閉時完全不解析 process host。故障注入是空白設定；
    /// 決定性斷言是 gate=false 且 helper 回傳 null，所以不會建立 provider、HTTP handler、Data8 pool、
    /// session、cache、timer 或任何圖片讀取流量。
    /// </summary>
    [Fact]
    public void Package03_contact_image_read_remains_disabled_by_default_before_host_resolution()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        DonationDynamicsAccessBootstrap.IsPackage03SpecialResourcesEnabled(configuration)
            .Should().BeFalse();
        DonationDynamicsAccessBootstrap.TryCreatePackage03SpecialResourceClient(configuration)
            .Should().BeNull();
    }

    /// <summary>
    /// 保護未來經審查的 Package03 gate 開啟時，只可借用 DI 已擁有的 stateless typed client。此測試替身
    /// 不建立任何外部資源；決定性斷言是 helper 原樣回傳替身而沒有執行影像 read、write、metadata 或統計 operation。
    /// </summary>
    [Fact]
    public void Package03_contact_image_read_accepts_an_injected_client_only_when_flag_is_enabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DynamicsAccess:Package03SpecialResourcesEnabled"] = "true",
                ["DynamicsAccess:ProfileAlias"] = "crm91"
            })
            .Build();
        var injected = new DisabledPackage03SpecialResourceClient();

        DonationDynamicsAccessBootstrap.IsPackage03SpecialResourcesEnabled(configuration)
            .Should().BeTrue();
        DonationDynamicsAccessBootstrap.TryCreatePackage03SpecialResourceClient(configuration, injected)
            .Should().BeSameAs(injected);
    }

    /// <summary>
    /// 保護實際 ChurchReport process host 可以由既有 CrmConnection 組成一個 Embedded adapter，而不要求或讀取
    /// Gateway endpoint。故障注入是缺少 Gateway 區段；決定性斷言是同設定只重用一個 adapter generation，尚未
    /// 執行 operation 前不會建立 Data8 client，且 host Dispose 後拒絕再次組成。測試使用 example.invalid 與
    /// 虛擬帳密字串，從不呼叫 executor，因此不產生 WCF、ADFS、HTTP、permit、timer 或真實 Session。
    /// </summary>
    [Fact]
    public async Task Embedded_process_host_composes_one_adapter_without_gateway_endpoint_and_becomes_terminal_after_dispose()
    {
        var configuration = CreateEmbeddedConfiguration();
        var options = DonationDynamicsAccessBootstrap.BindOptions(configuration);
        await using var host = new DonationDynamicsAccessProcessHost();

        var first = host.GetOrCreateEmbeddedExecutor(options, configuration);
        var second = host.GetOrCreateEmbeddedExecutor(options, configuration);

        first.Should().BeOfType<SpeechMessage.Dynamics.Embedded.EmbeddedHostAdapter>();
        second.Should().BeSameAs(first);

        await host.DisposeAsync();

        var action = () => host.GetOrCreateEmbeddedExecutor(options, configuration);
        action.Should().Throw<ObjectDisposedException>();
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
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(LegacyToolUtilityDrainController) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService) &&
            descriptor.ImplementationType == typeof(LegacyToolUtilityAdmissionHostedService));
    }

    /// <summary>
    /// 驗證 Visual Studio Development profile 依 ASP.NET Core 的基底後覆寫順序，明確選擇 Embedded 與
    /// <c>sunnyvalechback</c>。Package 1 consumer flag 維持關閉，所以既有收費讀取不會切換；P4 host startup
    /// 仍會建立唯一受控 runtime 並執行一次 WhoAmI，這不是 consumer migration。測試只讀 checked-in JSON；基底
    /// 設定可能保留 Gateway 欄位供其他部署模式使用，但 Embedded adapter 依契約不讀取它，因此不會悄悄建立
    /// localhost 或 Central HTTP session。
    /// </summary>
    [Fact]
    public void Development_configuration_selects_embedded_while_package01_reads_remain_disabled()
    {
        var repositoryRoot = FindRepositoryRoot();
        var applicationRoot = Path.Combine(repositoryRoot, "SpeechMessageProducts.ChurchReport");
        var configuration = new ConfigurationBuilder()
            .SetBasePath(applicationRoot)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: false, reloadOnChange: false)
            .Build();

        configuration.GetValue<bool>("DynamicsAccess:Package01FeeReadsEnabled").Should().BeFalse();
        configuration["DynamicsAccess:ConnectionMode"].Should().Be("Embedded");
        configuration["DynamicsAccess:ProfileAlias"].Should().Be("sunnyvalechback");
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
            ["DynamicsAccess:ConnectionMode"] = "DedicatedGateway",
            ["DynamicsAccess:ProfileAlias"] = "jesus-prod",
            ["DynamicsAccess:Gateway:Endpoint"] = endpoint,
            ["DynamicsAccess:Gateway:ApiPrefix"] = "/v1"
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    /// <summary>不建立任何外部資源的 composition 測試替身；不應在 disabled／registration 測試中被呼叫。</summary>
    private sealed class DisabledContactBasicInfoClient : IPackage02ContactBasicInfoUpdateClient
    {
        public Task<ContactBasicInfoUpdateResult> UpdateAsync(
            ContactBasicInfoUpdateRequest request,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Disabled composition test client must not execute.");
    }

    /// <summary>
    /// 不擁有資源的 Slice B composition 替身；任一 method 被執行即失敗，證明本測試只驗證 flag／DI 邊界。
    /// </summary>
    private sealed class DisabledContactProfileClient : IPackage02ContactProfileClient
    {
        /// <summary>禁止 disabled composition 測試執行 LINE profile write。</summary>
        public Task<ContactLineProfileUpdateResult> UpdateLineProfileAsync(
            ContactLineProfileUpdateRequest request,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Disabled Slice B composition test client must not execute.");

        /// <summary>禁止 disabled composition 測試執行 aggregate function。</summary>
        public Task<UngroupedCommitmentCountResult> CountUngroupedCommitmentAsync(
            UngroupedCommitmentCountRequest request,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Disabled Slice B composition test client must not execute.");
    }

    /// <summary>
    /// 不擁有資源的 Package03 composition 替身。每一個 capability method 都明確拒絕執行，確保上述測試只驗證
    /// gate 與 DI ownership，而不會意外啟動 connector、保留 image bytes 或跨 request 重用可變 state。
    /// </summary>
    private sealed class DisabledPackage03SpecialResourceClient : IPackage03SpecialResourceClient
    {
        /// <summary>禁止 disabled composition 測試執行 contact image read。</summary>
        public Task<ContactImageResult> RetrieveContactImageAsync(
            ContactImageRetrieveRequest request,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Disabled Package03 composition test client must not execute.");

        /// <summary>禁止 disabled composition 測試執行 MemberInfo image write。</summary>
        public Task<ContactImageUpdateResult> UpdateMemberInfoContactImageAsync(
            ContactImageUpdateRequest request,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Disabled Package03 composition test client must not execute.");

        /// <summary>禁止 disabled composition 測試執行 NewPerson image write。</summary>
        public Task<ContactImageUpdateResult> UpdateNewPersonContactImageAsync(
            ContactImageUpdateRequest request,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Disabled Package03 composition test client must not execute.");

        /// <summary>禁止 disabled composition 測試執行 metadata read。</summary>
        public Task<OptionSetRetrieveResult> RetrieveOptionSetAsync(
            OptionSetRetrieveRequest request,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Disabled Package03 composition test client must not execute.");

        /// <summary>禁止 disabled composition 測試執行 weekly statistics read。</summary>
        public Task<MeetingStatisticsRetrieveResult> RetrieveMeetingStatisticsAsync(
            MeetingStatisticsRetrieveRequest request,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Disabled Package03 composition test client must not execute.");
    }

    /// <summary>
    /// 建立只供 composition／lifecycle 測試的 Embedded 設定。CrmConnection 是既有產品設定來源，但本 helper
    /// 不提供真實 endpoint 或 credential，且測試不會執行 adapter；因此它只驗證 mapper 與 DI ownership，
    /// 不建立外部連線或把任何資料寫入靜態狀態。
    /// </summary>
    private static IConfiguration CreateEmbeddedConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DynamicsAccess:Package01FeeReadsEnabled"] = "false",
                ["DynamicsAccess:ConnectionMode"] = "Embedded",
                ["DynamicsAccess:ProfileAlias"] = "sunnyvalechback",
                ["CrmConnection:Organization"] = "sunnyvalechback",
                ["CrmConnection:OrganizationId"] = "bfb92ead-3705-f011-8143-00155d006608",
                ["CrmConnection:CeVersion"] = "9.1",
                ["CrmConnection:ServerUrl"] = "https://example.invalid/CrmApp/XRMServices/2011/Organization.svc",
                ["CrmConnection:Username"] = "test-user",
                ["CrmConnection:Password"] = "test-password",
                ["CrmConnection:MinPoolSize"] = "0",
                ["CrmConnection:MaxPoolSize"] = "1",
                ["CrmConnection:ConnectionTimeoutSeconds"] = "5",
                ["CrmConnection:IdleTimeoutMinutes"] = "1"
            })
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

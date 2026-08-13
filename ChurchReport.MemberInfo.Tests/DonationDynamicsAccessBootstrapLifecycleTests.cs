using System.Collections.Concurrent;
using ChurchReport.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.FeeReads;
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
    /// 驗證認獻單 typed-read 的 capability gate 同時受 Package01 基礎 gate 與專屬 sub-gate 保護。
    /// 此測試以沒有 ProcessHost 的組態呼叫 factory；因此任何非空 client 都代表 disabled 路徑錯誤地
    /// 建立了 host、HTTP handler、Data8 pool 或 credential graph。兩個 gate 缺省或只有 sub-gate 時
    /// 必須直接回傳 <see langword="null"/>，確保 rollback 僅需將 deployment 設定設回 false，且不殘留
    /// request、session、profile 或 transport 資源。
    /// </summary>
    [Fact]
    public void Package01_dedication_booking_read_requires_base_and_sub_gates_before_host_resolution()
    {
        var disabledConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var subGateOnlyConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DynamicsAccess:Package01DedicationBookingReadEnabled"] = "true"
            })
            .Build();

        DonationDynamicsAccessBootstrap.IsPackage01DedicationBookingReadEnabled(disabledConfiguration)
            .Should().BeFalse();
        DonationDynamicsAccessBootstrap.IsPackage01DedicationBookingReadEnabled(subGateOnlyConfiguration)
            .Should().BeFalse();
        DonationDynamicsAccessBootstrap.TryCreatePackage01DedicationBookingReadClient(disabledConfiguration)
            .Should().BeNull();
        DonationDynamicsAccessBootstrap.TryCreatePackage01DedicationBookingReadClient(subGateOnlyConfiguration)
            .Should().BeNull();
    }

    /// <summary>
    /// 驗證唯有 deployment 組態同時啟用 base/sub gate 且設定完整 ProfileAlias 時，factory 才能接受
    /// 由 DI 提供的無狀態 typed facade。injected facade 只讓測試避開真實 transport，不能取代 profile
    /// isolation boundary；呼叫者無法藉此指定 endpoint、credential、connector 或其他 CRM routing 狀態。
    /// </summary>
    [Fact]
    public void Package01_dedication_booking_read_accepts_an_injected_client_only_after_reviewed_gate_and_profile_validation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DynamicsAccess:Package01FeeReadsEnabled"] = "true",
                ["DynamicsAccess:Package01DedicationBookingReadEnabled"] = "true",
                ["DynamicsAccess:ProfileAlias"] = "crm91"
            })
            .Build();
        var injected = new DisabledPackage01DedicationBookingReadClient();

        DonationDynamicsAccessBootstrap.IsPackage01DedicationBookingReadEnabled(configuration)
            .Should().BeTrue();
        DonationDynamicsAccessBootstrap.TryCreatePackage01DedicationBookingReadClient(configuration, injected)
            .Should().BeSameAs(injected);
    }

    /// <summary>
    /// 驗證啟用 gate 不會讓空白 deployment ProfileAlias 繞過隔離邊界。錯誤必須發生在 injected facade
    /// 或 ProcessHost 解析之前，避免未分區的 client、lease 或 credential graph 因組態缺漏被建立後又
    /// 被另一位使用者或 profile 重用。
    /// </summary>
    [Fact]
    public void Package01_dedication_booking_read_rejects_empty_deployment_profile_before_client_or_host_resolution()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DynamicsAccess:Package01FeeReadsEnabled"] = "true",
                ["DynamicsAccess:Package01DedicationBookingReadEnabled"] = "true"
            })
            .Build();

        Action create = () => DonationDynamicsAccessBootstrap
            .TryCreatePackage01DedicationBookingReadClient(
                configuration,
                new DisabledPackage01DedicationBookingReadClient());

        create.Should().Throw<InvalidOperationException>()
            .WithMessage("*ProfileAlias*");
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
    /// 保護 reviewed gate 與完整 deployment ProfileAlias 都有效時，factory 才可借用主 DI 注入的 stateless
    /// Slice B typed client，而不自行建立 provider 或 transport。故障注入是 resource-free in-memory injected client；
    /// 決定性斷言是已驗證 profile 後原物件被回傳，且沒有執行 B1 write／B2 query、CE operation、LINE call、
    /// timer、stream 或 background work。ProfileAlias 仍只由 deployment configuration 擁有，injected facade 不可提供
    /// 或覆寫它，避免不同 request/profile 把同一 facade 當成 routing authority。
    /// </summary>
    [Fact]
    public void Package02_contact_profile_operations_accept_an_injected_client_only_when_flag_is_enabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DynamicsAccess:Package02ContactProfileOperationsEnabled"] = "true",
                ["DynamicsAccess:ProfileAlias"] = "crm91"
            })
            .Build();
        var injected = new DisabledContactProfileClient();

        DonationDynamicsAccessBootstrap.IsPackage02ContactProfileOperationsEnabled(configuration)
            .Should().BeTrue();
        DonationDynamicsAccessBootstrap.TryCreatePackage02ContactProfileClient(configuration, injected)
            .Should().BeSameAs(injected);
    }

    /// <summary>
    /// 保護 P7.4 ORG-CALL-00024 sub-gate 預設是嚴格 no-op。故障注入是沒有 process host、endpoint、
    /// credential 或 profile 的空設定，以及僅開啟 sub-gate 卻未開 base gate 的設定；決定性斷言是兩種情況
    /// 都不允許 typed consumer，因而不會建立 executor、provider、handler、Data8 pool、Session 或任何 CE 流量。
    /// </summary>
    [Fact]
    public void Package02_ungrouped_commitment_read_requires_both_base_and_sub_gates_before_host_resolution()
    {
        var disabledConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var subGateOnlyConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DynamicsAccess:Package02UngroupedCommitmentReadEnabled"] = "true"
            })
            .Build();

        DonationDynamicsAccessBootstrap.IsPackage02UngroupedCommitmentReadEnabled(disabledConfiguration)
            .Should().BeFalse();
        DonationDynamicsAccessBootstrap.IsPackage02UngroupedCommitmentReadEnabled(subGateOnlyConfiguration)
            .Should().BeFalse();
        DonationDynamicsAccessBootstrap.TryCreatePackage02UngroupedCommitmentReadClient(disabledConfiguration)
            .Should().BeNull();
        DonationDynamicsAccessBootstrap.TryCreatePackage02UngroupedCommitmentReadClient(subGateOnlyConfiguration)
            .Should().BeNull();
    }

    /// <summary>
    /// 保護 base/sub-gate 都開啟時，aggregate child 仍只可借用主 DI 已擁有的 stateless Package02 client。
    /// 故障注入是純記憶體 test double；決定性斷言是 bootstrap 不執行 count、LINE write、provider、pool、
    /// handler 或 background work，只回傳原 injection，資源 ownership 保持在 process host。
    /// </summary>
    [Fact]
    public void Package02_ungrouped_commitment_read_allows_only_a_reviewed_base_and_sub_gate_combination()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DynamicsAccess:Package02ContactProfileOperationsEnabled"] = "true",
                ["DynamicsAccess:Package02UngroupedCommitmentReadEnabled"] = "true",
                ["DynamicsAccess:ProfileAlias"] = "crm91"
            })
            .Build();
        var injected = new DisabledContactProfileClient();

        DonationDynamicsAccessBootstrap.IsPackage02UngroupedCommitmentReadEnabled(configuration)
            .Should().BeTrue();
        DonationDynamicsAccessBootstrap.TryCreatePackage02UngroupedCommitmentReadClient(configuration, injected)
            .Should().BeSameAs(injected);
    }

    /// <summary>
    /// 保護 ORG-CALL-00024 的 enabled composition 在解析 process host、provider、handler、pool 或 credential 前，
    /// 先拒絕空白 deployment ProfileAlias。故障注入是 base/sub-gate 都開啟但沒有 ProfileAlias 的設定；決定性斷言
    /// 是固定 profile 驗證錯誤先出現，而不是 host 未啟動或其他 composition 錯誤，避免無效部署設定延長資源生命週期。
    /// </summary>
    [Fact]
    public void Package02_ungrouped_commitment_read_rejects_an_empty_deployment_profile_before_host_resolution()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DynamicsAccess:Package02ContactProfileOperationsEnabled"] = "true",
                ["DynamicsAccess:Package02UngroupedCommitmentReadEnabled"] = "true"
            })
            .Build();

        Action create = () => DonationDynamicsAccessBootstrap
            .TryCreatePackage02UngroupedCommitmentReadClient(configuration);

        create.Should().Throw<InvalidOperationException>()
            .WithMessage("*ProfileAlias*");
    }

    /// <summary>
    /// 保護通用 Package02 contact-profile facade 在 base gate 開啟時，不能讓 injected fake 或 DI facade 繞過
    /// deployment-owned ProfileAlias 驗證。故障注入是只開啟 base gate、刻意省略 ProfileAlias，並傳入完全不配置
    /// connector、pool、handler、credential、timer 或背景工作的純記憶體替身；決定性斷言是 factory 必須在任何
    /// process-host resolution 前回傳固定 ProfileAlias 驗證錯誤，而不是交還 facade 或以 host 未啟動錯誤掩蓋設定缺口。
    /// 這個測試只在本機堆疊配置 input，測試結束後沒有跨 user、profile 或 request 保留的 mutable state 或資源。
    /// </summary>
    [Fact]
    public void Package02_contact_profile_client_rejects_an_empty_deployment_profile_before_host_resolution()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DynamicsAccess:Package02ContactProfileOperationsEnabled"] = "true"
            })
            .Build();

        Action create = () => DonationDynamicsAccessBootstrap
            .TryCreatePackage02ContactProfileClient(configuration, new DisabledContactProfileClient());

        create.Should().Throw<InvalidOperationException>()
            .WithMessage("*ProfileAlias*");
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
    /// 保護 ORG-CALL-00040 的 metadata sub-gate 預設為嚴格 no-op。故障注入是空設定與只開 sub-gate 的設定；
    /// 決定性斷言是兩者都在 host resolution 前回傳 null，不建立 provider、handler、pool、metadata cache、
    /// Session 或 outbound I/O，也不會因其他 Package03 capability 的 base gate 未明確開啟而切流。
    /// </summary>
    [Fact]
    public void Package03_memberinfo_commitment_metadata_read_requires_both_base_and_sub_gates_before_host_resolution()
    {
        var disabledConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var subGateOnlyConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DynamicsAccess:Package03MemberInfoCommitmentMetadataReadEnabled"] = "true"
            })
            .Build();

        DonationDynamicsAccessBootstrap.IsPackage03MemberInfoCommitmentMetadataReadEnabled(disabledConfiguration)
            .Should().BeFalse();
        DonationDynamicsAccessBootstrap.IsPackage03MemberInfoCommitmentMetadataReadEnabled(subGateOnlyConfiguration)
            .Should().BeFalse();
        DonationDynamicsAccessBootstrap.TryCreatePackage03MemberInfoCommitmentMetadataReadClient(disabledConfiguration)
            .Should().BeNull();
        DonationDynamicsAccessBootstrap.TryCreatePackage03MemberInfoCommitmentMetadataReadClient(subGateOnlyConfiguration)
            .Should().BeNull();
    }

    /// <summary>
    /// 保護兩層部署 gate 都啟用時，metadata child 只借用 DI 已擁有的 stateless Package03 facade。故障注入是
    /// 純記憶體 fake；決定性斷言是 bootstrap 原樣回傳同一物件，沒有執行 metadata/image/weekly operation、
    /// 建立 process host、provider、pool、handler 或 background work，資源 owner 維持在 Generic Host。
    /// </summary>
    [Fact]
    public void Package03_memberinfo_commitment_metadata_read_accepts_only_a_reviewed_base_and_sub_gate_combination()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DynamicsAccess:Package03SpecialResourcesEnabled"] = "true",
                ["DynamicsAccess:Package03MemberInfoCommitmentMetadataReadEnabled"] = "true",
                ["DynamicsAccess:ProfileAlias"] = "crm91"
            })
            .Build();
        var injected = new DisabledPackage03SpecialResourceClient();

        DonationDynamicsAccessBootstrap.IsPackage03MemberInfoCommitmentMetadataReadEnabled(configuration)
            .Should().BeTrue();
        DonationDynamicsAccessBootstrap.TryCreatePackage03MemberInfoCommitmentMetadataReadClient(configuration, injected)
            .Should().BeSameAs(injected);
    }

    /// <summary>
    /// 保護 metadata enabled composition 在碰觸 process host、provider、handler、pool 或 credential 前先驗證
    /// deployment ProfileAlias。故障注入是 base/sub gate 均開啟但 alias 空白；決定性斷言是 profile 錯誤先出現，
    /// 不會因 host 尚未啟動而掩蓋錯誤或猜選另一個 Dynamics profile。
    /// </summary>
    [Fact]
    public void Package03_memberinfo_commitment_metadata_read_rejects_an_empty_deployment_profile_before_host_resolution()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DynamicsAccess:Package03SpecialResourcesEnabled"] = "true",
                ["DynamicsAccess:Package03MemberInfoCommitmentMetadataReadEnabled"] = "true"
            })
            .Build();

        Action create = () => DonationDynamicsAccessBootstrap
            .TryCreatePackage03MemberInfoCommitmentMetadataReadClient(configuration);

        create.Should().Throw<InvalidOperationException>()
            .WithMessage("*ProfileAlias*");
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
    /// 為 bootstrap composition 測試提供不具 transport、快取、背景工作或可釋放資源的 typed facade。
    /// 任一執行呼叫都代表本應 short-circuit 的測試錯誤，因此立即擲出；此 double 不保存 profile、
    /// contact、DTO 或 cancellation state，避免測試本身製造跨案例洩漏。
    /// </summary>
    private sealed class DisabledPackage01DedicationBookingReadClient : IPackage01DedicationBookingReadClient
    {
        /// <summary>
        /// 禁止 disabled composition 測試意外執行 read operation；factory contract 僅驗證注入與
        /// deployment gate，並不授權任何 outbound I/O 或 CE 查詢。
        /// </summary>
        public Task<IReadOnlyList<SpeechMessage.Dynamics.ProductClient.Models.DedicationBookingRecordDto>>
            RetrieveDedicationBookingsByContactAsync(
                string profileAlias,
                string workloadSubjectId,
                Guid contactId,
                string? contactName = null,
                CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Disabled Package01 dedication-booking test client must not execute.");
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

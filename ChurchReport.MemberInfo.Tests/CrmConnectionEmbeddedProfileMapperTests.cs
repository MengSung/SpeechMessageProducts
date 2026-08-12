using ChurchReport.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Connectors.Data8;
using System.Text.Json;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 驗證 ChurchReport 既有 CrmConnection 只在啟動組合根轉換為受控的 Dynamics Profile 與 Organization Catalog。
/// 測試不建立 Data8 client、WCF channel、permit、timer 或背景工作；它只檢查 immutable 設定投影，避免產品請求、
/// Session 或 controller 取得 OrganizationId、endpoint、ConnectorKind 或 credential。
/// </summary>
public sealed class CrmConnectionEmbeddedProfileMapperTests
{
    /// <summary>
    /// 保護 sunnyvalechback 的既有 CrmConnection 可以產生唯一同 alias 的 Data8 Profile 與 enabled Catalog entry。
    /// 故障模型是 mapper 必須拒絕不存在或不一致的欄位；決定性斷言是成功結果只含 credential reference，
    /// 不含 password 值，且 Catalog 與 Profile 的 OrganizationAlias 完全相同。
    /// </summary>
    [Fact]
    public void Try_create_maps_existing_crm_connection_to_one_data8_profile_and_catalog_entry()
    {
        var configuration = CreateConfiguration();
        var options = new ProductDynamicsOptions
        {
            ConnectionMode = ConnectionMode.Embedded,
            ProfileAlias = "sunnyvalechback"
        };

        var created = CrmConnectionEmbeddedProfileMapper.TryCreate(
            configuration,
            options,
            out var profiles,
            out var catalog,
            out var errorCode);

        created.Should().BeTrue();
        errorCode.Should().BeEmpty();
        profiles.Should().ContainSingle();
        catalog.Should().ContainSingle();

        var profile = profiles["sunnyvalechback"];
        profile.OrganizationAlias.Should().Be("sunnyvalechback");
        profile.CeVersion.Should().Be(CeVersion.Ce91);
        profile.ConnectorKind.Should().Be(ConnectorKind.Data8);
        profile.CredentialReference.Should().Be("churchreport.crmconnection");

        var organization = catalog["sunnyvalechback"];
        organization.OrganizationId.Should().Be(Guid.Parse("bfb92ead-3705-f011-8143-00155d006608"));
        organization.State.Should().Be(OrganizationState.Enabled);
        organization.ServiceUri.Should().Be("https://sunnyvalechback.speechmessage.com.tw/XRMServices/2011/Organization.svc");
    }

    /// <summary>
    /// 保護 ProfileAlias 不能在 request 或不一致的產品設定下改指另一個 CrmConnection organization。
    /// 故障注入為合法但不同的 alias；決定性斷言是 mapper fail closed 且不輸出任何半完成 Profile 或 Catalog，
    /// 避免下游 admission／pool 用錯實體 Organization 建立可重用連線。
    /// </summary>
    [Fact]
    public void Try_create_rejects_a_profile_alias_that_does_not_match_crm_connection_organization()
    {
        var created = CrmConnectionEmbeddedProfileMapper.TryCreate(
            CreateConfiguration(),
            new ProductDynamicsOptions
            {
                ConnectionMode = ConnectionMode.Embedded,
                ProfileAlias = "elijah"
            },
            out var profiles,
            out var catalog,
            out var errorCode);

        created.Should().BeFalse();
        errorCode.Should().Be("embedded.profile-alias-mismatch");
        profiles.Should().BeEmpty();
        catalog.Should().BeEmpty();
    }

    /// <summary>
    /// 保護 P4 中的中央 Organization Catalog 能讓產品只以 ProfileAlias 選擇已知 CE 8.2 組織。
    /// 故障模型是舊版 mapper 仍直接相信單一 CrmConnection:OrganizationId，因而無法切換別名或把 9.1
    /// 的識別誤套給 8.2；決定性斷言是 Catalog 的 GUID 與 CE 版本必須由 selected alias 的 immutable
    /// entry 決定。測試全程沒有 endpoint、credential、permit、client、timer 或背景工作，因此不會建立
    /// Session 或任何可跨 request／組織殘留的資源。
    /// </summary>
    [Fact]
    public void Try_create_resolves_selected_ce82_profile_from_the_central_organization_catalog()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CrmConnection:Organization"] = "sunnyvalechback",
                ["CrmConnection:ServerUrl"] = "https://example.invalid/XRMServices/2011/Organization.svc",
                ["CrmConnection:MinPoolSize"] = "0",
                ["CrmConnection:MaxPoolSize"] = "1",
                ["CrmConnection:ConnectionTimeoutSeconds"] = "30",
                ["CrmConnection:IdleTimeoutMinutes"] = "10",
                ["CrmConnection:OrganizationCatalog:jesus:FriendlyName"] = "音訊教會",
                ["CrmConnection:OrganizationCatalog:jesus:UniqueName"] = "jesus",
                ["CrmConnection:OrganizationCatalog:jesus:OrganizationId"] = "4d701c24-2102-eb11-80da-00155d006913",
                ["CrmConnection:OrganizationCatalog:jesus:State"] = "Enabled",
                ["CrmConnection:OrganizationCatalog:jesus:CeVersion"] = "8.2",
                ["CrmConnection:OrganizationCatalog:jesus:ServiceUri"] = "https://example.invalid/XRMServices/2011/Organization.svc"
            })
            .Build();

        var created = CrmConnectionEmbeddedProfileMapper.TryCreate(
            configuration,
            new ProductDynamicsOptions
            {
                ConnectionMode = ConnectionMode.Embedded,
                ProfileAlias = "jesus"
            },
            out var profiles,
            out var catalog,
            out var errorCode);

        created.Should().BeTrue(errorCode);
        profiles["jesus"].CeVersion.Should().Be(CeVersion.Ce82);
        catalog["jesus"].OrganizationId.Should().Be(Guid.Parse("4d701c24-2102-eb11-80da-00155d006913"));
        catalog["jesus"].State.Should().Be(OrganizationState.Enabled);
    }

    /// <summary>
    /// 保護清單中明示 Disabled 的組織不能因為 alias 存在就取得 Profile、admission permit 或 Data8 client。
    /// 故障注入為具有合法 GUID 與版本但 State=Disabled 的 catalog entry；決定性斷言是 mapper 在任何
    /// 外部連線資源建立前 fail closed，並且兩個輸出集合皆為空，避免停用租戶與有效租戶共用 session 或 pool。
    /// </summary>
    [Fact]
    public void Try_create_rejects_a_disabled_catalog_entry_before_profile_or_client_creation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CrmConnection:ServerUrl"] = "https://example.invalid/XRMServices/2011/Organization.svc",
                ["CrmConnection:OrganizationCatalog:imchurch:FriendlyName"] = "iM行動教會",
                ["CrmConnection:OrganizationCatalog:imchurch:UniqueName"] = "imchurch",
                ["CrmConnection:OrganizationCatalog:imchurch:OrganizationId"] = "155ab748-1f5a-eb11-8121-00155d006607",
                ["CrmConnection:OrganizationCatalog:imchurch:State"] = "Disabled",
                ["CrmConnection:OrganizationCatalog:imchurch:CeVersion"] = "8.2"
            })
            .Build();

        var created = CrmConnectionEmbeddedProfileMapper.TryCreate(
            configuration,
            new ProductDynamicsOptions
            {
                ConnectionMode = ConnectionMode.Embedded,
                ProfileAlias = "imchurch"
            },
            out var profiles,
            out var catalog,
            out var errorCode);

        created.Should().BeFalse();
        errorCode.Should().Be("embedded.organization-disabled");
        profiles.Should().BeEmpty();
        catalog.Should().BeEmpty();
    }

    /// <summary>
    /// 保護版本控制中的 ChurchReport Development 設定確實能提供 Embedded 組合根所需的組織識別與 CE 版本。
    /// 此測試不讀取 Password，也不建立 Data8 client、WCF channel、permit、timer 或背景工作；它只驗證設定快照
    /// 能在任何外部 I/O 之前被安全映射，避免 F5 直到取得連線資源後才發現 OrganizationId 缺漏。
    /// </summary>
    [Fact]
    public void Checked_in_churchreport_configuration_maps_the_selected_embedded_profile()
    {
        var applicationRoot = Path.Combine(FindRepositoryRoot(), "SpeechMessageProducts.ChurchReport");
        var configuration = new ConfigurationBuilder()
            .SetBasePath(applicationRoot)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: false, reloadOnChange: false)
            .Build();

        var options = DonationDynamicsAccessBootstrap.BindOptions(configuration);
        var created = CrmConnectionEmbeddedProfileMapper.TryCreate(
            configuration,
            options,
            out var profiles,
            out var catalog,
            out var errorCode);

        created.Should().BeTrue(errorCode);
        profiles.Should().ContainKey("sunnyvalechback");
        catalog.Should().ContainKey("sunnyvalechback");
    }

    /// <summary>
    /// 驗證一般 Development 組態永遠保留 Embedded，Dedicated Gateway 則只能由 Visual Studio 專用
    /// 啟動設定以環境變數明確選擇。這個契約使一般 F5 不會意外產生 HTTP hop，也讓同一份靜態
    /// 組態不會保存 Gateway 模式的可變端點、認證或 Session 狀態。
    /// 測試直接讀取 checked-in JSON：若 Dedicated 專用 profile 遺漏、覆寫錯誤，或 Development
    /// 預設被改回 DedicatedGateway，便在尚未建立 HttpClient、Data8 client、permit 或背景工作前失敗。
    /// </summary>
    [Fact]
    public void Checked_in_churchreport_launch_profile_selects_dedicated_gateway_without_replacing_embedded_development_default()
    {
        var applicationRoot = Path.Combine(FindRepositoryRoot(), "SpeechMessageProducts.ChurchReport");
        var configuration = new ConfigurationBuilder()
            .SetBasePath(applicationRoot)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: false, reloadOnChange: false)
            .Build();

        DonationDynamicsAccessBootstrap.BindOptions(configuration)
            .ConnectionMode.Should().Be(ConnectionMode.Embedded);

        using var launchSettings = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(applicationRoot, "Properties", "launchSettings.json")));
        var dedicatedEnvironment = launchSettings.RootElement
            .GetProperty("profiles")
            .GetProperty("DedicatedGateway")
            .GetProperty("environmentVariables");

        dedicatedEnvironment.GetProperty("DynamicsAccess__ConnectionMode").GetString()
            .Should().Be("DedicatedGateway");
        dedicatedEnvironment.GetProperty("DynamicsAccess__ProfileAlias").GetString()
            .Should().Be("sunnyvalechback");
        dedicatedEnvironment.GetProperty("DynamicsAccess__Gateway__Endpoint").GetString()
            .Should().Be("https://localhost:7244/");
        dedicatedEnvironment.GetProperty("DynamicsAccess__Package01FeeReadsEnabled").GetString()
            .Should().Be("false",
                because: "P7.4 尚未取得 durable admission、legacy coverage 與 drain-first/non-overlap evidence；F5 DedicatedGateway profile 也不得繞過 deployment-owned feature gate");

        var gatewayRoot = Path.Combine(FindRepositoryRoot(), "SpeechMessage.Dynamics.Gateway");
        using var gatewayLaunchSettings = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(gatewayRoot, "Properties", "launchSettings.json")));
        var gatewayDedicatedEnvironment = gatewayLaunchSettings.RootElement
            .GetProperty("profiles")
            .GetProperty("DedicatedGateway")
            .GetProperty("environmentVariables");

        gatewayDedicatedEnvironment
            .GetProperty("DynamicsGateway__ActiveWorkloadBindingSet")
            .GetString()
            .Should().Be("DedicatedGateway");
        gatewayDedicatedEnvironment
            .GetProperty("DynamicsGateway__WorkloadBindingSets__DedicatedGateway__0__ProfileAliases__0")
            .GetString()
            .Should().Be("sunnyvalechback");
        gatewayDedicatedEnvironment
            .GetProperty("DynamicsGateway__WorkloadBindingSets__DedicatedGateway__0__CapabilityOperationIds__0")
            .GetString()
            .Should().Be("runtime.health.whoami");
    }

    /// <summary>
    /// 保護啟動 composition root 可從既有 CrmConnection 建立受控 Data8 factory settings，而不是讓 controller
    /// 或 request 自行讀取 endpoint、帳號或密碼。故障模型是密碼欄位的存在被錯誤投影到 Profile；主要 assertion
    /// 是結果只有固定 credential reference 與 service scalar，且 settings 型別沒有公開 Password property，避免
    /// 明碼被產品執行期程式、Session、log 或 Pool key 讀取／保存。
    /// </summary>
    [Fact]
    public void Try_create_connection_settings_reads_crm_credentials_only_at_the_startup_boundary()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CrmConnection:ServerUrl"] = "https://example.invalid/XRMServices/2011/Organization.svc",
                ["CrmConnection:Username"] = "TEST\\service",
                ["CrmConnection:Password"] = "not-a-real-password"
            })
            .Build();

        var created = CrmConnectionEmbeddedProfileMapper.TryCreateConnectionSettings(
            configuration,
            "https://example.invalid/XRMServices/2011/Organization.svc",
            out var settings,
            out var errorCode);

        created.Should().BeTrue(errorCode);
        settings.Should().NotBeNull();
        settings!.CredentialReference.Should().Be("churchreport.crmconnection");
        settings.ServiceUri.Should().Be("https://example.invalid/XRMServices/2011/Organization.svc");
        typeof(Data8OnPremiseConnectionSettings).GetProperty("Password").Should().BeNull();
    }

    /// <summary>
    /// 保護 Embedded factory 只使用 selected Catalog 的組織服務 URI，而非沿用舊 CrmConnection transport URI。
    /// 故障注入為兩個有效但不同主機的 HTTPS URI；決定性斷言是 settings 精確採用 Catalog URI，讓產品只切換
    /// ProfileAlias 就能取得正確 endpoint。測試不建立 Data8 client、WCF channel、permit、timer 或背景工作，
    /// 因此不會造成跨組織 session 或連線池洩漏。
    /// </summary>
    [Fact]
    public void Try_create_connection_settings_uses_the_selected_catalog_uri_instead_of_the_legacy_transport_uri()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CrmConnection:ServerUrl"] = "https://ce91.example.invalid/XRMServices/2011/Organization.svc",
                ["CrmConnection:Username"] = "TEST\\service",
                ["CrmConnection:Password"] = "not-a-real-password"
            })
            .Build();

        var created = CrmConnectionEmbeddedProfileMapper.TryCreateConnectionSettings(
            configuration,
            "https://ce82.example.invalid/XRMServices/2011/Organization.svc",
            out var settings,
            out var errorCode);

        created.Should().BeTrue(errorCode);
        settings.Should().NotBeNull();
        settings!.ServiceUri.Should().Be("https://ce82.example.invalid/XRMServices/2011/Organization.svc");
    }

    /// <summary>
    /// 建立不含真實密碼的最小 CrmConnection 設定。credential reference 是固定名稱而非 password；
    /// mapper 因而不需要也不得讀取密碼、token 或其他秘密來完成純設定投影。
    /// </summary>
    private static IConfiguration CreateConfiguration()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CrmConnection:Organization"] = "sunnyvalechback",
                ["CrmConnection:OrganizationId"] = "bfb92ead-3705-f011-8143-00155d006608",
                ["CrmConnection:CeVersion"] = "9.1",
                ["CrmConnection:ServerUrl"] = "https://sunnyvalechback.speechmessage.com.tw/XRMServices/2011/Organization.svc",
                ["CrmConnection:MinPoolSize"] = "3",
                ["CrmConnection:MaxPoolSize"] = "20",
                ["CrmConnection:ConnectionTimeoutSeconds"] = "30",
                ["CrmConnection:IdleTimeoutMinutes"] = "10"
            })
            .Build();

    /// <summary>
    /// 由測試輸出目錄向上尋找目前 worktree，避免將絕對使用者路徑或 session 目錄保存在測試程式中。
    /// DirectoryInfo 僅在測試期間短暫使用，不會開啟 FileSystemWatcher 或保留檔案控制代碼。
    /// </summary>
    private static string FindRepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
             current is not null;
             current = current.Parent)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "SpeechMessageProducts.ChurchReport")) &&
                Directory.Exists(Path.Combine(current.FullName, "SpeechMessage.Dynamics.Embedded")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("The current worktree root was not found.");
    }
}

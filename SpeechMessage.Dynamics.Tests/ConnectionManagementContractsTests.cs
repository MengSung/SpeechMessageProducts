// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/ConnectionManagementContractsTests.cs
// 用途：以測試先行方式固定新版 Dynamics 連線管理的公開契約、防呆解析與請求邊界。
//
// 本測試刻意不建立 CRM 連線、HTTP 用戶端、Worker、Permit、Timer 或背景工作。它只驗證
// 純記憶體、不可變設定快照與同步 Guard，因此失敗時也不會留下 Session、連線或資源。
// 每個案例都保護跨 Organization/Profile 隔離：請求僅能選擇 ProfileAlias，不能攜帶
// 端點、憑證、Connector 或 Organization 身分來改變既定路由。
// ============================================================================

using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ControlPlane.Configuration;
using SpeechMessage.Dynamics.ControlPlane.Guard;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 P1 新連線管理契約。這些測試是後續 Embedded、Dedicated Gateway、Central Gateway
/// 共用的最小安全基線：模式選擇、Profile 解析與 Request Guard 必須一致，且所有不明或
/// 不相容設定一律 fail-closed。
/// </summary>
public sealed class ConnectionManagementContractsTests
{
    /// <summary>
    /// 保護產品端只能由部署設定選擇三個明確連線模式。枚舉值是持久化 JSON 的契約，
    /// 不允許含糊的 Gateway 舊名稱或 request-time 模式切換，以免不同產品、Session
    /// 或 Organization 在同一個處理程序中意外共用可變傳輸狀態。
    /// </summary>
    [Fact]
    public void Connection_mode_exposes_only_the_three_deployment_modes()
    {
        Enum.GetNames<ConnectionMode>().Should().BeEquivalentTo(
            nameof(ConnectionMode.Embedded),
            nameof(ConnectionMode.DedicatedGateway),
            nameof(ConnectionMode.CentralGateway));
    }

    /// <summary>
    /// 保護產品設定只保留部署模式、Profile Alias 與 Gateway 位置。Organization GUID、
    /// Connector、CRM endpoint 與憑證必須留在部署端 Catalog/Profile，避免產品 JSON
    /// 成為跨租戶路由或敏感資訊的來源。
    /// </summary>
    [Fact]
    public void Product_options_expose_only_connection_mode_profile_alias_and_gateway()
    {
        typeof(ProductDynamicsOptions).GetProperties()
            .Select(property => property.Name)
            .Should().BeEquivalentTo(
                nameof(ProductDynamicsOptions.ConnectionMode),
                nameof(ProductDynamicsOptions.ProfileAlias),
                nameof(ProductDynamicsOptions.Gateway));
    }

    /// <summary>
    /// 保護未知 Alias 不會觸發 Connector、Permit 或 Credential 解析。Resolver 只讀取建構
    /// 時建立的不可變快照；即使原始 Dictionary 其後被修改，也不會讓既有請求轉向別的
    /// Organization 或保留外部可變設定參考。
    /// </summary>
    [Fact]
    public void Resolver_fails_closed_for_unknown_profile_alias()
    {
        var resolver = CreateResolver();

        var resolved = resolver.TryResolve("unknown", out _, out var error);

        resolved.Should().BeFalse();
        error.Should().Be("profile.not-found");
    }

    /// <summary>
    /// 保護停用 Organization 不能因為 Profile 名稱合法而繼續取得連線。此檢查發生在
    /// 任一 Connector、Worker 或憑證所有者被建立之前，所以不會造成失敗路徑的資源洩漏。
    /// </summary>
    [Fact]
    public void Resolver_fails_closed_for_disabled_organization()
    {
        var resolver = CreateResolver(state: OrganizationState.Disabled);

        var resolved = resolver.TryResolve("sunnyvalechback", out _, out var error);

        resolved.Should().BeFalse();
        error.Should().Be("organization.disabled");
    }

    /// <summary>
    /// 保護尚未取得的 GUID 佔位值不能成為可運行 Profile。全零與全 f GUID 都可能在樣板
    /// 或未完成部署中出現；它們若通過會把容量帳本與 Pool 隔離建立在錯誤身分上。
    /// </summary>
    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("ffffffff-ffff-ffff-ffff-ffffffffffff")]
    public void Resolver_rejects_placeholder_organization_identity(string organizationId)
    {
        var resolver = CreateResolver(organizationId: Guid.Parse(organizationId));

        var resolved = resolver.TryResolve("sunnyvalechback", out _, out var error);

        resolved.Should().BeFalse();
        error.Should().Be("organization.identity-placeholder");
    }

    /// <summary>
    /// 保護 Worker 與 CE 版本在 Profile 建立時就固定相容性，不能由呼叫端觸發隱式 fallback。
    /// 這可避免 CE 9.1 的可變 SDK/WCF 狀態被錯誤載入或混入 CE 8.2 的 runtime generation。
    /// </summary>
    [Fact]
    public void Resolver_rejects_incompatible_connector_and_ce_version()
    {
        var resolver = CreateResolver(
            ceVersion: CeVersion.Ce91,
            connectorKind: ConnectorKind.OfficialCrm82Worker);

        var resolved = resolver.TryResolve("sunnyvalechback", out _, out var error);

        resolved.Should().BeFalse();
        error.Should().Be("profile.connector-incompatible");
    }

    /// <summary>
    /// 保護 Alias 的大小寫差異只會解析到同一份不可變 Profile snapshot，而不是建立第二個
    /// Pool 或 Session 容器。Pool Generation 在同一 Alias 下必須維持單一、可追溯的所有權。
    /// </summary>
    [Fact]
    public void Resolver_normalizes_alias_case_to_the_same_immutable_profile()
    {
        var resolver = CreateResolver();

        resolver.TryResolve("SUNNYVALECHBACK", out var upperCase, out var upperError).Should().BeTrue();
        resolver.TryResolve("sunnyvalechback", out var lowerCase, out var lowerError).Should().BeTrue();

        upperError.Should().BeEmpty();
        lowerError.Should().BeEmpty();
        upperCase.Should().Be(lowerCase);
        upperCase!.GenerationId.Should().Be(7);
    }

    /// <summary>
    /// 保護請求不能把 CRM 路由、憑證或任意 FetchXML 偷渡到部署端。Guard 在 Resolver、
    /// admission permit、Pool、Worker 與任何網路 I/O 之前同步拒絕，因此被拒絕的字典
    /// 不會被長期快取或轉換成可保留的連線狀態。
    /// </summary>
    [Theory]
    [InlineData("organizationId")]
    [InlineData("connectorKind")]
    [InlineData("credential")]
    [InlineData("endpoint")]
    [InlineData("fetchXml")]
    public void Request_guard_rejects_reserved_routing_parameters(string reservedParameter)
    {
        var guard = new RequestGuard([OperationIds.RuntimeHealthWhoAmI]);
        var request = CreateRequest(parameters: new Dictionary<string, object?>
        {
            [reservedParameter] = "untrusted"
        });

        var result = guard.Inspect(request, RequestOrigin.Embedded);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("request.reserved-parameter");
    }

    /// <summary>
    /// 保護空白或超長 Alias 在輸入邊界立刻被拒絕，不能用來建立大量 Resolver key、Pool key
    /// 或追蹤項目。檢查不配置資源，也不保存原始請求資料。
    /// </summary>
    [Theory]
    [InlineData(" ")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz")]
    public void Request_guard_rejects_invalid_profile_alias(string profileAlias)
    {
        var guard = new RequestGuard([OperationIds.RuntimeHealthWhoAmI]);

        var result = guard.Inspect(CreateRequest(profileAlias: profileAlias), RequestOrigin.CentralGateway);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("request.invalid-profile-alias");
    }

    /// <summary>
    /// 保護未登錄 capability operation 不會進入 connector 路徑。操作白名單會在建構時複製為
    /// immutable、大小寫不敏感集合，避免呼叫端在執行途中改寫集合造成授權或 Session 邊界漂移。
    /// </summary>
    [Fact]
    public void Request_guard_rejects_unregistered_operation_before_any_runtime_is_created()
    {
        var guard = new RequestGuard([OperationIds.RuntimeHealthWhoAmI]);

        var result = guard.Inspect(
            CreateRequest(capabilityOperationId: "unregistered.operation"),
            RequestOrigin.DedicatedGateway);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("operation.not-registered");
    }

    /// <summary>
    /// 建立純記憶體的測試 Catalog。測試資料不含密碼、Token、Cookie 或實際 CRM 連線字串；
    /// Resolver 只保存不可變 scalar 值，沒有需要 Dispose 的資源。
    /// </summary>
    private static ConfigurationProfileResolver CreateResolver(
        OrganizationState state = OrganizationState.Enabled,
        Guid? organizationId = null,
        CeVersion ceVersion = CeVersion.Ce91,
        ConnectorKind connectorKind = ConnectorKind.Data8)
        => new(
            new Dictionary<string, DynamicsProfileOptions>(StringComparer.Ordinal)
            {
                ["sunnyvalechback"] = new()
                {
                    OrganizationAlias = "sunnyvalechback",
                    CeVersion = ceVersion,
                    ConnectorKind = connectorKind,
                    CredentialReference = "development-sunnyvalechback",
                    Pool = new PoolPolicy(),
                    Operation = new OperationPolicy()
                }
            },
            new Dictionary<string, OrganizationCatalogEntry>(StringComparer.Ordinal)
            {
                ["sunnyvalechback"] = new()
                {
                    FriendlyName = "聖谷行道會（公司研發）",
                    UniqueName = "sunnyvalechback",
                    OrganizationId = organizationId ?? Guid.Parse("bfb92ead-3705-f011-8143-00155d006608"),
                    State = state,
                    ServiceUri = "https://sunnyvalechback.speechmessage.com.tw/XRMServices/2011/Organization.svc"
                }
            },
            generationId: 7);

    /// <summary>
    /// 建立不含 CRM surface 的最小請求。輸入 dictionary 僅在 Guard 呼叫期間使用，Guard 不得
    /// 保留該參考；測試因此能偵測規格退化而不需要連線、Session 或背景執行緒。
    /// </summary>
    private static OperationExecutionRequest CreateRequest(
        string profileAlias = "sunnyvalechback",
        string capabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
        IReadOnlyDictionary<string, object?>? parameters = null)
        => new()
        {
            ProfileAlias = profileAlias,
            CapabilityOperationId = capabilityOperationId,
            WorkloadSubjectId = "test-workload",
            Parameters = parameters ?? new Dictionary<string, object?>()
        };
}

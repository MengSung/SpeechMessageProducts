using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.ControlPlane.Capacity;
using SpeechMessage.Dynamics.ControlPlane.Configuration;
using SpeechMessage.Dynamics.ControlPlane.Runtime;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 Official Worker 的 Profile resolver 會把 deployment-owned 設定與目前 Active Runtime generation
/// 綁在一起。測試不建立 process、pipe、admission、credential 或 CE 連線；它只保護 generation replacement
/// 後 Router 不會繼續使用舊 snapshot，避免跨世代 client、session 或 profile mutable state 重用。
/// </summary>
public sealed class OfficialWorkerRuntimeProfileResolverTests
{
    /// <summary>
    /// 驗證 Active Runtime generation 由 resolver 動態投影，設定 resolver 原本的 generation 不會在
    /// replacement 後繼續主導路由。這是 P6 的 generation-owned Router 前置契約。
    /// </summary>
    [Fact]
    public void Resolve_projects_the_current_active_runtime_generation()
    {
        var source = CreateResolver(CeVersion.Ce91, ConnectorKind.OfficialCrm91Worker, generationId: 1);
        var active = new TrackingActiveGenerationResolver(CreateRuntimeKey("crm91", 7, "9.1"));
        var resolver = new OfficialWorkerRuntimeProfileResolver(source, active);

        resolver.TryResolve("crm91", out var profile, out var error).Should().BeTrue();

        error.Should().BeEmpty();
        profile.Should().NotBeNull();
        profile!.GenerationId.Should().Be(7);
        active.LookupCount.Should().Be(1);
    }

    /// <summary>
    /// 驗證 Active Runtime 的 CE 版本與部署 Profile 不一致時 fail closed，且不把錯誤當成 Data8 或
    /// 另一個 Official Worker 的容錯路徑。錯誤只回傳固定分類，不建立任何 transport resource。
    /// </summary>
    [Fact]
    public void Resolve_rejects_an_active_runtime_with_the_wrong_ce_version()
    {
        var source = CreateResolver(CeVersion.Ce91, ConnectorKind.OfficialCrm91Worker, generationId: 1);
        var active = new TrackingActiveGenerationResolver(CreateRuntimeKey("crm91", 7, "8.2"));
        var resolver = new OfficialWorkerRuntimeProfileResolver(source, active);

        resolver.TryResolve("crm91", out var profile, out var error).Should().BeFalse();

        profile.Should().BeNull();
        error.Should().Be("profile.runtime-version-incompatible");
        active.LookupCount.Should().Be(1);
    }

    /// <summary>建立只含測試 scalar 的部署 Profile resolver；沒有端點、token 或 credential 值。</summary>
    private static ConfigurationProfileResolver CreateResolver(
        CeVersion ceVersion,
        ConnectorKind connectorKind,
        long generationId)
        => new(
            new Dictionary<string, DynamicsProfileOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["crm91"] = new()
                {
                    OrganizationAlias = "crm91",
                    CeVersion = ceVersion,
                    ConnectorKind = connectorKind,
                    CredentialReference = "test-credential-reference",
                    Pool = new PoolPolicy(),
                    Operation = new OperationPolicy()
                }
            },
            new Dictionary<string, OrganizationCatalogEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["crm91"] = new()
                {
                    FriendlyName = "test-organization",
                    UniqueName = "crm91",
                    OrganizationId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    State = OrganizationState.Enabled,
                    ServiceUri = "https://crm.example.test/XRMServices/2011/Organization.svc"
                }
            },
            generationId);

    /// <summary>建立不含秘密的 current-runtime key；URI 只作容量隔離測試值。</summary>
    private static ProfileRuntimeKey CreateRuntimeKey(string alias, long generation, string ceVersion)
        => new(
            alias,
            generation,
            ceVersion,
            new CanonicalOrganizationCapacityKey(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "https://crm.example.test/"));

    /// <summary>
    /// 測試擁有的 bounded generation lookup；只計數查詢，不保存 request、identity、session 或任何可釋放資源。
    /// </summary>
    private sealed class TrackingActiveGenerationResolver : IActiveProfileGenerationResolver
    {
        private readonly ProfileRuntimeKey _key;

        /// <summary>建立固定回傳 key 的測試替身。</summary>
        public TrackingActiveGenerationResolver(ProfileRuntimeKey key) => _key = key;

        /// <summary>取得測試期間查詢次數。</summary>
        public int LookupCount { get; private set; }

        /// <summary>回傳目前 deployment-owned Active generation。</summary>
        public bool TryGetActiveRuntimeKey(string profileAlias, out ProfileRuntimeKey key)
        {
            LookupCount++;
            if (!string.Equals(profileAlias, _key.ProfileAlias, StringComparison.OrdinalIgnoreCase))
            {
                key = default;
                return false;
            }

            key = _key;
            return true;
        }
    }
}

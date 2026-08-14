// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/AppNamedMembershipData8Tests.cs
// 用途：保護 ORG-CALL-00057 在 Data8 executor 邊界的輸入驗證與零 I/O 契約。
//
// 測試替身只記錄目前測試例項的 router 呼叫次數，完全不建立 CRM service、Pool、
// lease、連線、計時器、快取或背景工作。這使測試能明確證明空白 contact GUID
// 在任何可重用、跨 profile 的資源取得之前 fail closed。
// ============================================================================

using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.Connectors.Data8;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 App-named membership read 的 Data8 dispatch 輸入邊界。
///
/// 此 capability 只接受 server 已決定要查詢的單一非空 contact GUID；呼叫端無法藉由空 GUID、
/// profile、connector 或任何額外狀態擴大 CRM 查詢範圍。測試保護的安全契約是無效輸入必須在
/// router、Pool、lease 與 outbound I/O 前被拒絕，因此不會配置或保留另一個 request／profile 的
/// Data8 session。
/// </summary>
public sealed class AppNamedMembershipData8Tests
{
    /// <summary>
    /// 保護空白 contact GUID 的 fail-closed 契約。
    ///
    /// 故障注入為唯一參數 <c>contactId</c> 使用 <see cref="Guid.Empty"/>。決定性斷言同時要求
    /// 固定 <c>operation.invalid-parameters</c> 分類與 router 零次呼叫，證明輸入驗證早於 connector
    /// 路由，且失敗不會取得、污染或遺留可跨 request 重用的 client、lease 或 session。
    /// </summary>
    [Fact]
    public async Task App_named_membership_rejects_an_empty_contact_identifier_before_connector_router_io()
    {
        var router = new RecordingRouter();
        var executor = new Data8ProfileOperationExecutor(new FixedProfileResolver(CreateProfile()), router);
        var request = new OperationExecutionRequest
        {
            ProfileAlias = "app-named-membership-profile",
            CapabilityOperationId = "list.membership.retrieve.appnamed.by.contact",
            WorkloadSubjectId = "app-named-membership-test",
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["contactId"] = Guid.Empty
            }
        };

        var result = await executor.ExecuteAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("operation.invalid-parameters");
        router.ResolveCount.Should().Be(0);
    }

    /// <summary>
    /// 建立只供本測試使用的 server-resolved Data8 profile snapshot。
    ///
    /// profile 是不可變部署值，沒有 credential 實值、client、cookie、session 或快取。executor 僅能以此
    /// snapshot 驗證 routing；本測試的拒絕路徑不得將它交給 router，因而沒有額外資源需要 cleanup。
    /// </summary>
    /// <returns>具有限 timeout 與固定 generation 的 Data8 profile。</returns>
    private static ResolvedProfile CreateProfile()
        => new(
            "app-named-membership-profile",
            "app-named-membership-organization",
            Guid.Parse("a7777777-3333-2222-1111-000000000005"),
            CeVersion.Ce91,
            ConnectorKind.Data8,
            "app-named-membership-credential-reference",
            new ResolvedPoolPolicy(0, 1, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1), false),
            new ResolvedOperationPolicy(TimeSpan.FromSeconds(5), 0, TimeSpan.Zero),
            GenerationId: 1);

    /// <summary>
    /// 提供固定且不可變 profile 的 resolver 替身。
    ///
    /// 替身不快取 request 或解析 caller 控制的 profile 值；只接受建構時指定的 alias。這讓測試可單獨驗證
    /// parameter validation 是否早於 router，而不引入 profile/session 的跨測試可變狀態。
    /// </summary>
    private sealed class FixedProfileResolver : IProfileResolver
    {
        private readonly ResolvedProfile _profile;

        /// <summary>
        /// 以測試唯一擁有的 immutable profile 建立 resolver。
        /// </summary>
        /// <param name="profile">不得為 null 的固定 server-resolved profile。</param>
        public FixedProfileResolver(ResolvedProfile profile)
            => _profile = profile ?? throw new ArgumentNullException(nameof(profile));

        /// <summary>
        /// 只解析預期 alias；不符時回傳去識別化固定錯誤，不建立 connector 或任何 session 資源。
        /// </summary>
        /// <param name="profileAlias">executor 欲解析的 profile alias。</param>
        /// <param name="profile">成功時的 immutable profile snapshot；失敗時為 null。</param>
        /// <param name="error">不含 endpoint、credential 或其他 profile 細節的固定錯誤碼。</param>
        /// <returns>alias 與固定 profile 相符時為 <see langword="true"/>。</returns>
        public bool TryResolve(string profileAlias, out ResolvedProfile? profile, out string error)
        {
            if (string.Equals(profileAlias, _profile.ProfileAlias, StringComparison.Ordinal))
            {
                profile = _profile;
                error = string.Empty;
                return true;
            }

            profile = null;
            error = "profile-not-found";
            return false;
        }
    }

    /// <summary>
    /// 只記錄 router 是否被觸碰的 fail-fast 替身。
    ///
    /// 計數器是 instance-local 並以 <see cref="Interlocked"/> 維護，沒有 static request state。若無效輸入仍
    /// 到達 router，替身會立即丟出，防止測試意外配置可跨 request 使用的 Pool 或 connector。
    /// </summary>
    private sealed class RecordingRouter : IConnectorRouter
    {
        private int _resolveCount;

        /// <summary>
        /// 取得本 router instance 的 Resolve 次數，以判定無效輸入是否在 connector I/O 前被拒絕。
        /// </summary>
        public int ResolveCount => Volatile.Read(ref _resolveCount);

        /// <summary>
        /// 記錄不應到達的 pool 路由嘗試並立即拒絕。
        /// </summary>
        /// <param name="profile">executor 已解析但本測試不得保存的 profile。</param>
        /// <returns>此 fail-fast 路徑永不回傳 pool。</returns>
        public IConnectorPool Resolve(ResolvedProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            Interlocked.Increment(ref _resolveCount);
            throw new InvalidOperationException("Invalid App-named membership input must not resolve a connector pool.");
        }
    }
}

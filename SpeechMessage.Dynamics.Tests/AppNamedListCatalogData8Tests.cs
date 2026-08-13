// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/AppNamedListCatalogData8Tests.cs
// 用途：保護 ORG-CALL-00014 在 Data8 executor 邊界的零參數契約。測試替身只記錄 resolver/router 呼叫次數，
//       不建立 CRM service、連線、計時器、背景工作、快取或任何跨請求狀態。
// ============================================================================

using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.Connectors.Data8;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 App-named 名單目錄的 Data8 dispatch 邊界。這些測試只處理 request-local immutable scalar，故意不建立
/// CRM SDK、Data8 pool 或 network I/O；其受保護的契約是任何 caller-supplied parameter 都要在 connector
/// router／pool 配置前 fail closed，避免錯誤參數影響 profile、client、session 或另一個工作負載的資源。
/// </summary>
public sealed class AppNamedListCatalogData8Tests
{
    /// <summary>
    /// 驗證非空 parameter map 不會進入 router。故障注入是提供未被 registry 宣告的 <c>listId</c>；決定性斷言是
    /// executor 回傳固定 invalid-parameters code，且 router 計數保持零，證明不會因無效輸入取得 pool、lease 或
    /// connector I/O，也不會留下可供後續 request 重用的資源。
    /// </summary>
    [Fact]
    public async Task App_named_catalog_rejects_non_empty_parameters_before_connector_router_io()
    {
        var router = new RecordingRouter();
        var executor = new Data8ProfileOperationExecutor(new FixedProfileResolver(CreateProfile()), router);
        var request = new OperationExecutionRequest
        {
            ProfileAlias = "catalog-profile",
            CapabilityOperationId = OperationIds.ListCatalogRetrieveAppNamed,
            WorkloadSubjectId = "catalog-test",
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["listId"] = Guid.Parse("77777777-3333-2222-1111-000000000004")
            }
        };

        var result = await executor.ExecuteAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("operation.invalid-parameters");
        router.ResolveCount.Should().Be(0);
    }

    /// <summary>
    /// 建立只供此測試使用的已解析 Data8 profile。其值不含可用端點或秘密；executor 在 router 前拒絕輸入，因此
    /// profile 只提供型別正確的 isolation snapshot，沒有 client、session、cache、timer 或可釋放資源需要保留。
    /// </summary>
    /// <returns>固定且不可變的測試 profile。</returns>
    private static ResolvedProfile CreateProfile()
        => new(
            "catalog-profile",
            "catalog-organization",
            Guid.Parse("88888888-3333-2222-1111-000000000005"),
            CeVersion.Ce91,
            ConnectorKind.Data8,
            "catalog-credential-reference",
            new ResolvedPoolPolicy(0, 1, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1), false),
            new ResolvedOperationPolicy(TimeSpan.FromSeconds(5), 0, TimeSpan.Zero),
            GenerationId: 1);

    /// <summary>
    /// 只回傳固定 immutable profile 的 resolver 替身；它不快取 request，也不解析 caller profile 值，因此測試能
    /// 專注於參數驗證是否早於 router I/O，而非引入 profile/session 可變狀態。
    /// </summary>
    private sealed class FixedProfileResolver : IProfileResolver
    {
        private readonly ResolvedProfile _profile;

        /// <summary>
        /// 以測試擁有的 immutable profile 建立 resolver，避免替身在測試結束後保留 request 或外部資源。
        /// </summary>
        /// <param name="profile">預期成功解析的固定 Data8 profile。</param>
        public FixedProfileResolver(ResolvedProfile profile)
            => _profile = profile ?? throw new ArgumentNullException(nameof(profile));

        /// <summary>
        /// 只接受預期 alias；不符時回傳固定拒絕訊息。此方法不配置 connector，故不會替無效輸入建立 session 或
        /// 任何需要清理的資源。
        /// </summary>
        /// <param name="profileAlias">executor 欲解析的 profile alias。</param>
        /// <param name="profile">成功時的 immutable profile snapshot。</param>
        /// <param name="error">失敗時的去識別化錯誤碼。</param>
        /// <returns>alias 符合固定測試 profile 時為 <see langword="true"/>。</returns>
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
    /// 僅記錄 router 是否遭碰觸的替身。若 executor 的前置驗證失效，此替身立刻拋出，避免測試意外取得 pool、
    /// lease、connector 或外部資源；計數只存在測試例項，沒有跨測試共享狀態。
    /// </summary>
    private sealed class RecordingRouter : IConnectorRouter
    {
        private int _resolveCount;

        /// <summary>
        /// 取得 router 被呼叫的次數，供斷言無效參數不會觸發 connector I/O。
        /// </summary>
        public int ResolveCount => Volatile.Read(ref _resolveCount);

        /// <summary>
        /// 記錄一次不應發生的 router 呼叫後立即拒絕。此替身沒有 pool owner，因此絕不回傳或保留 connector。
        /// </summary>
        /// <param name="profile">若呼叫發生時 executor 已解析的 profile。</param>
        /// <returns>不會正常回傳；用來阻止意外取得 connector pool。</returns>
        public IConnectorPool Resolve(ResolvedProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            Interlocked.Increment(ref _resolveCount);
            throw new InvalidOperationException("Invalid catalog parameters must not resolve a connector pool.");
        }
    }
}

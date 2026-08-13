// ============================================================================
// 檔案路徑：SpeechMessage.Dynamics.Tests/AppNamedSmallGroupListCatalogData8Tests.cs
// 用途：驗證 ORG-CALL-00065 的 Data8 執行器會在連線池、租約與 CRM I/O 之前拒絕不屬於
//       固定小組 App 點名名單目錄契約的呼叫端參數。
//
// 本測試不建立 CRM service，也不保存 request、profile、router 或 connector 的可變參考。
// RecordingRouter 僅以 Interlocked 記錄目前測試範圍內的 Resolve 次數；只要參數驗證失敗，
// 次數必須保持零，證明不會配置另一位使用者／設定檔的 Data8 client、session 或 lease。
// ============================================================================

using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.Connectors.Data8;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證小組 App 點名名單目錄的 Data8 dispatch 邊界。
///
/// 這個 capability 是無呼叫端參數的 server-owned 固定查詢；測試刻意帶入一個 listId，以保護
/// 「先驗證、後路由」的隔離與資源生命週期契約。失敗時只能回傳穩定錯誤分類，不能碰觸 pool、
/// connector、CRM session 或留下任何可跨 request 重用的輸入資料。
/// </summary>
public sealed class AppNamedSmallGroupListCatalogData8Tests
{
    /// <summary>
    /// 保護無參數固定查詢契約：任一額外參數必須在 connector router I/O 之前被拒絕。
    ///
    /// 故障注入為呼叫端傳入不被允許的 <c>listId</c>。決定性斷言同時確認固定
    /// <c>operation.invalid-parameters</c> 錯誤與 router 的零次 Resolve，避免不合法輸入進入
    /// Data8 pool 而取得、污染或保留任何 profile-specific client/session。
    /// </summary>
    [Fact]
    public async Task App_named_small_group_catalog_rejects_non_empty_parameters_before_connector_router_io()
    {
        var router = new RecordingRouter();
        var executor = new Data8ProfileOperationExecutor(new FixedProfileResolver(CreateProfile()), router);
        var request = new OperationExecutionRequest
        {
            ProfileAlias = "small-group-catalog-profile",
            CapabilityOperationId = OperationIds.ListCatalogRetrieveAppNamedSmallGroups,
            WorkloadSubjectId = "small-group-catalog-test",
            Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["listId"] = Guid.Parse("57777777-3333-2222-1111-000000000004")
            }
        };

        var result = await executor.ExecuteAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("operation.invalid-parameters");
        router.ResolveCount.Should().Be(0);
    }

    /// <summary>
    /// 建立僅供本測試使用的固定 Data8 profile snapshot。
    ///
    /// snapshot 是不可變值，沒有 credential 實值、client、cookie、session 或快取；executor 只能使用
    /// 此 server-resolved profile 驗證請求，且本測試的拒絕路徑不得讓它流入 router。
    /// </summary>
    /// <returns>具有有限 timeout 與單一 generation 的測試 profile。</returns>
    private static ResolvedProfile CreateProfile()
        => new(
            "small-group-catalog-profile",
            "small-group-catalog-organization",
            Guid.Parse("68888888-3333-2222-1111-000000000005"),
            CeVersion.Ce91,
            ConnectorKind.Data8,
            "small-group-catalog-credential-reference",
            new ResolvedPoolPolicy(0, 1, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1), false),
            new ResolvedOperationPolicy(TimeSpan.FromSeconds(5), 0, TimeSpan.Zero),
            GenerationId: 1);

    /// <summary>
    /// 提供一個固定的 server-resolved profile，避免測試透過可變組態或其他 request 的設定檔資料取得路由權限。
    ///
    /// 此替身不保留呼叫者輸入，且只接受建構時指定的 alias；任何 mismatch 皆以固定錯誤失敗，防止 A/B profile
    /// 資料在測試或未來 executor 修改中交叉使用。
    /// </summary>
    private sealed class FixedProfileResolver : IProfileResolver
    {
        private readonly ResolvedProfile _profile;

        /// <summary>
        /// 初始化唯一的不可變 profile snapshot。
        /// </summary>
        /// <param name="profile">由測試建立且不得為 null 的 server-resolved profile。</param>
        public FixedProfileResolver(ResolvedProfile profile)
            => _profile = profile ?? throw new ArgumentNullException(nameof(profile));

        /// <summary>
        /// 只解析預期 alias；成功時回傳同一不可變 snapshot，失敗時不透露其他 profile 的資訊。
        /// </summary>
        /// <param name="profileAlias">executor 從目前 request 取得、僅用於比對的 alias。</param>
        /// <param name="profile">成功時的固定 profile；失敗時為 null。</param>
        /// <param name="error">不含 credential、endpoint 或 session 細節的固定錯誤類別。</param>
        /// <returns>alias 符合時為 <see langword="true"/>。</returns>
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
    /// 記錄 router 是否遭到呼叫的 fail-fast 替身。
    ///
    /// Resolve 一旦被呼叫便立刻拋出例外，因為本測試的唯一正確行為是非法參數在取得 pool 前被拒絕。計數器為
    /// instance-local，沒有 static request 狀態，並以 <see cref="Interlocked"/> 確保平行測試仍可準確觀察。
    /// </summary>
    private sealed class RecordingRouter : IConnectorRouter
    {
        private int _resolveCount;

        /// <summary>
        /// 取得本 router instance 的 Resolve 次數；不暴露或保存任何 profile、pool、lease 或 connector。
        /// </summary>
        public int ResolveCount => Volatile.Read(ref _resolveCount);

        /// <summary>
        /// 記錄不應到達的 pool 路由嘗試並立即失敗，防止測試替身意外建立可跨 request 使用的資源。
        /// </summary>
        /// <param name="profile">executor 已解析的 profile；僅驗證其非 null，不會保存其參考。</param>
        /// <returns>此拒絕路徑永不回傳 pool。</returns>
        public IConnectorPool Resolve(ResolvedProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            Interlocked.Increment(ref _resolveCount);
            throw new InvalidOperationException("Invalid small-group catalog parameters must not resolve a connector pool.");
        }
    }
}

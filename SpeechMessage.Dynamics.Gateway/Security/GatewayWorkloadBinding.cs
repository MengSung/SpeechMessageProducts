using System.Collections.Immutable;

namespace SpeechMessage.Dynamics.Gateway.Security;

/// <summary>
/// 表示 Gateway 在啟動時由部署設定建立的一份不可變 workload 授權快照。
/// 這個型別把「已驗證 Windows principal（優先 SID、其次完整 principal name）」固定映射到唯一
/// <see cref="WorkloadSubjectId"/>，再限制可用的 Profile Alias 與 Capability Operation；HTTP request body、
/// route、header 或 ClaimsPrincipal 均不能修改這份 binding。Instance 由 singleton authorizer 唯一擁有，
/// 只包含短字串與 immutable collections，不持有 <c>HttpContext</c>、credential、token、session、stream、
/// cancellation registration、socket 或背景工作，因此可安全供並行 request 唯讀共享，Host disposal 時也沒有額外 cleanup 動作。
/// Constructor 會防禦性複製集合；若輸入為空或不完整會立即失敗，避免半有效 binding 被發布後才在流量路徑猜測權限。
/// </summary>
public sealed class GatewayWorkloadBinding
{
    private readonly ImmutableHashSet<string> _profileAliasSet;
    private readonly ImmutableHashSet<string> _capabilityOperationIdSet;

    /// <summary>
    /// 建立已完成語法、重複與 registry 驗證的 immutable binding。
    /// 呼叫端（<see cref="ConfigurationGatewayOperationAuthorizer"/>）必須先把 alias／operation 轉成 registry canonical 值；
    /// 本 constructor 再複製一次，確保 configuration provider 或暫存 List 後續變更不會改寫已發布授權。
    /// </summary>
    /// <param name="windowsSid">可選的 canonical Windows SID；存在時 authorizer 優先使用。</param>
    /// <param name="principalName">可選的完整 Windows principal name；只在 authenticated principal 沒有可用 SID 時 fallback，不用於覆蓋未 mapping 的有效 SID。</param>
    /// <param name="workloadSubjectId">伺服器擁有的 bounded workload subject，供 admission 與稽核使用。</param>
    /// <param name="profileAliases">此 workload 可呼叫的 canonical Profile Alias 白名單。</param>
    /// <param name="capabilityOperationIds">此 workload 可呼叫的 canonical Operation ID 白名單。</param>
    public GatewayWorkloadBinding(
        string? windowsSid,
        string? principalName,
        string workloadSubjectId,
        IEnumerable<string> profileAliases,
        IEnumerable<string> capabilityOperationIds)
    {
        ArgumentNullException.ThrowIfNull(profileAliases);
        ArgumentNullException.ThrowIfNull(capabilityOperationIds);

        if (string.IsNullOrWhiteSpace(windowsSid) && string.IsNullOrWhiteSpace(principalName))
        {
            throw new ArgumentException(
                "A workload binding requires a Windows SID or principal name.",
                nameof(principalName));
        }

        if (string.IsNullOrWhiteSpace(workloadSubjectId))
        {
            throw new ArgumentException(
                "A workload binding requires a workload subject ID.",
                nameof(workloadSubjectId));
        }

        WindowsSid = windowsSid;
        PrincipalName = principalName;
        WorkloadSubjectId = workloadSubjectId;
        ProfileAliases = profileAliases.ToImmutableArray();
        CapabilityOperationIds = capabilityOperationIds.ToImmutableArray();
        if (ProfileAliases.IsDefaultOrEmpty || CapabilityOperationIds.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A workload binding requires at least one profile alias and operation ID.");
        }

        _profileAliasSet = ProfileAliases.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        _capabilityOperationIdSet = CapabilityOperationIds.ToImmutableHashSet(
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 取得 canonical Windows SID；設定後它是穩定的安全主體鍵。只有 request principal 本身沒有可用 SID 時，
    /// authorizer 才可使用同一 binding 的完整 principal name 相容路徑；有效 SID 未命中必須 fail closed。
    /// </summary>
    public string? WindowsSid { get; }

    /// <summary>取得不可部分比對、不可 wildcard 的完整 authenticated principal name。</summary>
    public string? PrincipalName { get; }

    /// <summary>取得伺服器產生 request 與 admission key 時唯一可用的 workload subject。</summary>
    public string WorkloadSubjectId { get; }

    /// <summary>取得防禦性複製且不可變的 canonical Profile Alias 白名單。</summary>
    public ImmutableArray<string> ProfileAliases { get; }

    /// <summary>取得防禦性複製且不可變的 canonical Capability Operation ID 白名單。</summary>
    public ImmutableArray<string> CapabilityOperationIds { get; }

    /// <summary>
    /// 以 Windows 語意的 OrdinalIgnoreCase 完整值比對 alias；不做 prefix、substring、pattern 或 fallback，
    /// 因此未列入 binding 的現在或未來 profile 都會 fail closed。方法只讀 immutable set，可無鎖並行呼叫。
    /// </summary>
    public bool AllowsProfileAlias(string profileAlias)
        => _profileAliasSet.Contains(profileAlias);

    /// <summary>
    /// 以 OrdinalIgnoreCase 完整值比對 canonical operation ID；不把某個 package 或 alias 推導成整組能力，
    /// 方法只讀 immutable set、無配置與 I/O，適合在 request materialization 前的熱路徑執行。
    /// </summary>
    public bool AllowsCapabilityOperation(string capabilityOperationId)
        => _capabilityOperationIdSet.Contains(capabilityOperationId);
}

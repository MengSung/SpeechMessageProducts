using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ChurchReport.Security;

/// <summary>
/// 表示 MemberInfo target authorization 的封閉存取模式。此值只能由 server-owned
/// evidence provider 決定；Cookie login kind、Session、browser locator、query string
/// 或前端傳入的 list ID 都不能直接轉成此 enum。
/// </summary>
public enum MemberInfoTargetAccessMode
{
    /// <summary>
    /// 代表已由獨立 server-owned source 證明的全教會範圍。此模式沒有 Shepherd
    /// list allowlist，後續 capability 仍必須自行套用目前資料與 operation policy。
    /// </summary>
    ChurchWide,

    /// <summary>
    /// 代表已由完整 server-owned assignment evidence 證明的牧養名單範圍。只有
    /// <see cref="MemberInfoTargetAuthorizationScope.VisibleListIds"/> 中的唯一 GUID
    /// 可被後續 capability 當成 target allowlist。
    /// </summary>
    AssignedLists
}

/// <summary>
/// 表示 target authorization 建立結果的固定去識別化分類。列舉值不得攜帶名單、
/// contact、endpoint、credential、token、CRM response 或原始例外，因此 failure
/// 不會成為跨使用者資料探測通道。
/// </summary>
public enum MemberInfoTargetAuthorizationFailure
{
    /// <summary>已建立完整且可使用的 immutable target scope。</summary>
    None,

    /// <summary>缺少已驗證的 P7 request scope，故沒有可信任的 subject。</summary>
    MissingRequestScope,

    /// <summary>目前沒有 server-owned source evidence；不得使用 legacy fallback。</summary>
    SourceUnavailable,

    /// <summary>evidence subject 與目前 request subject 不一致或為空。</summary>
    SubjectMismatch,

    /// <summary>evidence access mode 不在封閉 allowlist。</summary>
    UnsupportedAccessMode,

    /// <summary>target ID 為空、重複、超過上限，或 Church mode 不當地附帶 target IDs。</summary>
    InvalidOrDuplicateTarget,

    /// <summary>source 已回應但無法證明 assignment 關係完整，故不可發布 partial allowlist。</summary>
    IncompleteAssignmentEvidence
}

/// <summary>
/// 封裝未來 server-owned provider 完成授權來源查核後交給純 resolver 的最小 evidence。
/// 型別會在建立時 defensive-copy 所有 GUID，並且不保存 principal、HttpContext、Session、
/// credential、CRM Entity、profile、connector、endpoint、cache 或取消註冊。呼叫端必須只在
/// server-side source 已完整驗證時使用 <see cref="Create"/>；此型別本身不允許 legacy
/// ListManager、browser 或 caller-supplied 欄位成為 authority。
/// </summary>
public sealed class MemberInfoTargetAuthorizationEvidence
{
    private MemberInfoTargetAuthorizationEvidence(
        Guid subjectContactId,
        MemberInfoTargetAccessMode accessMode,
        IReadOnlyList<Guid> assignedListIds,
        bool assignmentEvidenceComplete)
    {
        SubjectContactId = subjectContactId;
        AccessMode = accessMode;
        AssignedListIds = assignedListIds;
        AssignmentEvidenceComplete = assignmentEvidenceComplete;
    }

    /// <summary>
    /// 取得 source 已比對的 subject contact GUID。resolver 仍會把它與本 request 的
    /// <see cref="P7GatewayRequestScope.SubjectContactId"/> 精確比較，避免 A 的 evidence
    /// 被誤套用到 B。
    /// </summary>
    public Guid SubjectContactId { get; }

    /// <summary>
    /// 取得 server-owned source 選定的封閉 access mode；resolver 會再次驗證 enum 值，
    /// 因此 cast 出的未知整數不可能穿越此邊界。
    /// </summary>
    public MemberInfoTargetAccessMode AccessMode { get; }

    /// <summary>
    /// 取得建立 evidence 時複製的 assignment IDs。此 collection 只描述 source 證據，
    /// 不是 browser input，也不是可供後續 request 修改的 ListManager backing list。
    /// </summary>
    public IReadOnlyList<Guid> AssignedListIds { get; }

    /// <summary>
    /// 指示 provider 是否已完成涵蓋所有必要 relationship 的查核。false 代表不完整
    /// source，而不是「空集合」；resolver 必須 fail closed 且不得回填 legacy 資料。
    /// </summary>
    public bool AssignmentEvidenceComplete { get; }

    /// <summary>
    /// 從 server-owned source 的已驗證 scalar 建立 evidence snapshot。輸入列舉會立即
    /// defensive-copy；方法不執行 I/O、cache、profile 選擇、connector 配置或背景工作。
    /// 未來 provider 的 cancellation、fault、timeout 與 resource cleanup 由 provider 自己
    /// 擁有，本純資料型別不保存 cancellation token 或可釋放資源。
    /// </summary>
    /// <param name="subjectContactId">server source 已解析的 subject GUID；resolver 會再與 request scope 比對。</param>
    /// <param name="accessMode">server source 選定的封閉模式，不能由 HTTP input 選擇。</param>
    /// <param name="assignedListIds">source 回傳的可見名單候選；可為 null，並在此立即複製。</param>
    /// <param name="assignmentEvidenceComplete">完整 source 證明旗標；false 一律不會產生 scope。</param>
    /// <returns>不與原輸入 collection 共用 backing storage 的 immutable evidence。</returns>
    internal static MemberInfoTargetAuthorizationEvidence Create(
        Guid subjectContactId,
        MemberInfoTargetAccessMode accessMode,
        IEnumerable<Guid>? assignedListIds,
        bool assignmentEvidenceComplete)
    {
        return new MemberInfoTargetAuthorizationEvidence(
            subjectContactId,
            accessMode,
            CopyIds(assignedListIds),
            assignmentEvidenceComplete);
    }

    /// <summary>
    /// 立即複製列舉結果，避免 evidence 留住 caller 的 mutable list。此 helper 不驗證
    /// authorization 語意；驗證集中於 resolver，確保所有 failure 都使用同一分類。
    /// </summary>
    /// <param name="source">可能為 null 的 source enumeration；不會被保存。</param>
    /// <returns>不暴露 backing list 的只讀 GUID snapshot。</returns>
    private static IReadOnlyList<Guid> CopyIds(IEnumerable<Guid>? source)
    {
        var copied = new List<Guid>();
        if (source is not null)
        {
            foreach (var item in source)
            {
                copied.Add(item);
            }
        }

        return new ReadOnlyCollection<Guid>(copied);
    }
}

/// <summary>
/// 表示已驗證、不可變且 request-local 的 MemberInfo target authorization scope。scope
/// 只包含 subject、封閉 mode 與 bounded allowlist；它不持有 HttpContext、principal、
/// Session、credential、Entity、profile、connector、transport、timer、cache 或任何需 Dispose
/// 的資源。request 結束後沒有 retained user state，後續 capability 仍須自行決定 profile、
/// operation、target projection、lease owner 與 rollback policy。
/// </summary>
public sealed class MemberInfoTargetAuthorizationScope
{
    internal MemberInfoTargetAuthorizationScope(
        MemberInfoTargetAccessMode accessMode,
        Guid subjectContactId,
        IReadOnlyList<Guid> visibleListIds)
    {
        AccessMode = accessMode;
        SubjectContactId = subjectContactId;
        VisibleListIds = visibleListIds;
    }

    /// <summary>取得不受 browser 或 login kind 影響的已驗證封閉 access mode。</summary>
    public MemberInfoTargetAccessMode AccessMode { get; }

    /// <summary>取得與目前 P7 request scope 精確相同的 subject contact GUID。</summary>
    public Guid SubjectContactId { get; }

    /// <summary>
    /// 取得已 defensive-copy、唯一且受固定上限保護的 Shepherd list allowlist。Church
    /// scope 永遠回傳空集合；空集合的 AssignedLists scope 代表來源完整但目前沒有可見小組。
    /// </summary>
    public IReadOnlyList<Guid> VisibleListIds { get; }
}

/// <summary>
/// 封裝 scope 建立成功或固定 failure 的結果。它不保存 source provider、principal、
/// raw exception、credential 或 CRM state；失敗時 <see cref="Scope"/> 必為 null，
/// 呼叫端必須在任何 locator、cache、client 或外部 I/O 前停止。
/// </summary>
public readonly record struct MemberInfoTargetAuthorizationResolution(
    MemberInfoTargetAuthorizationScope? Scope,
    MemberInfoTargetAuthorizationFailure Failure);

/// <summary>
/// 從既有 P7 identity scope 與 server-owned evidence 建立 MemberInfo target scope 的純
/// resolver。此 resolver 沒有 DI、I/O、cache、static mutable state、retry、timer、
/// cancellation registration 或背景工作；它只在目前 stack frame 內驗證與 defensive-copy，
/// 因而沒有連線、lease、stream 或其他資源所有權。source unavailable、fault 或取消時，
/// 未來 provider 必須不呼叫此 resolver 或傳遞 null evidence，使呼叫鏈在 I/O 前 fail closed。
/// </summary>
public static class MemberInfoTargetAuthorizationScopeResolver
{
    /// <summary>
    /// 防止異常 assignment source 將無界 GUID 集合保存在單一 request scope。這是固定
    /// 上限而非可由 caller 覆寫的設定，避免一個 request 占用不受控記憶體或產生巨大 IN query。
    /// </summary>
    public const int MaximumVisibleListIds = 512;

    /// <summary>
    /// 驗證 request subject、evidence completeness、access mode 與 target IDs，建立新的
    /// immutable scope。此方法不接受 browser locator、owner、profile、connector、endpoint、
    /// credential、Session 或 cancellation token；它不能選擇 source，也不做 legacy fallback。
    /// </summary>
    /// <param name="requestScope">已由 Cookie resolver 建立的目前 request identity baseline。</param>
    /// <param name="evidence">只可由 server-owned source 產生的 target evidence；null 表示 source unavailable。</param>
    /// <returns>成功時的新 scope；失敗時 null scope 與固定去識別化 failure。</returns>
    public static MemberInfoTargetAuthorizationResolution TryCreate(
        P7GatewayRequestScope? requestScope,
        MemberInfoTargetAuthorizationEvidence? evidence)
    {
        if (requestScope is null)
        {
            return Fail(MemberInfoTargetAuthorizationFailure.MissingRequestScope);
        }

        if (evidence is null)
        {
            return Fail(MemberInfoTargetAuthorizationFailure.SourceUnavailable);
        }

        if (evidence.SubjectContactId == Guid.Empty ||
            evidence.SubjectContactId != requestScope.SubjectContactId)
        {
            return Fail(MemberInfoTargetAuthorizationFailure.SubjectMismatch);
        }

        if (!Enum.IsDefined(typeof(MemberInfoTargetAccessMode), evidence.AccessMode))
        {
            return Fail(MemberInfoTargetAuthorizationFailure.UnsupportedAccessMode);
        }

        if (!evidence.AssignmentEvidenceComplete)
        {
            return Fail(MemberInfoTargetAuthorizationFailure.IncompleteAssignmentEvidence);
        }

        if (evidence.AccessMode == MemberInfoTargetAccessMode.ChurchWide)
        {
            if (evidence.AssignedListIds.Count != 0)
            {
                return Fail(MemberInfoTargetAuthorizationFailure.InvalidOrDuplicateTarget);
            }

            return Succeed(requestScope.SubjectContactId, evidence.AccessMode, Array.Empty<Guid>());
        }

        if (!TryCopyUniqueBoundedIds(evidence.AssignedListIds, out var visibleListIds))
        {
            return Fail(MemberInfoTargetAuthorizationFailure.InvalidOrDuplicateTarget);
        }

        return Succeed(requestScope.SubjectContactId, evidence.AccessMode, visibleListIds);
    }

    /// <summary>
    /// 驗證並複製 Shepherd target IDs。每個 ID 必須非空、唯一且總數不超過固定上限；
    /// 成功後回傳的 ReadOnlyCollection 不共用 evidence backing list，避免後續 code
    /// 在 authorization 與 response mapping 間改變 allowlist。
    /// </summary>
    /// <param name="source">server evidence 的已複製 GUID snapshot。</param>
    /// <param name="copied">成功時的新、只讀且 request-local GUID collection。</param>
    /// <returns>所有 target 都有效、唯一且在上限內時為 true。</returns>
    private static bool TryCopyUniqueBoundedIds(
        IReadOnlyList<Guid> source,
        out IReadOnlyList<Guid> copied)
    {
        copied = Array.Empty<Guid>();
        if (source.Count > MaximumVisibleListIds)
        {
            return false;
        }

        var unique = new HashSet<Guid>();
        var result = new List<Guid>(source.Count);
        foreach (var item in source)
        {
            if (item == Guid.Empty || !unique.Add(item))
            {
                return false;
            }

            result.Add(item);
        }

        copied = new ReadOnlyCollection<Guid>(result);
        return true;
    }

    /// <summary>
    /// 建立成功結果並再次將 collection 封閉在 scope 內。純 resolver 不取得資源，
    /// 因此這條成功路徑沒有 dispose、retry 或 cleanup 工作；外部 provider/connector
    /// 的 cancellation 與 deterministic release 不能被此 DTO 接手或隱藏。
    /// </summary>
    /// <param name="subjectContactId">已比對完成的目前 request subject。</param>
    /// <param name="accessMode">已驗證的封閉 target mode。</param>
    /// <param name="visibleListIds">已驗證、只讀的 request-local target collection。</param>
    /// <returns>沒有跨 request retained state 的成功 resolution。</returns>
    private static MemberInfoTargetAuthorizationResolution Succeed(
        Guid subjectContactId,
        MemberInfoTargetAccessMode accessMode,
        IReadOnlyList<Guid> visibleListIds)
    {
        return new MemberInfoTargetAuthorizationResolution(
            new MemberInfoTargetAuthorizationScope(accessMode, subjectContactId, visibleListIds),
            MemberInfoTargetAuthorizationFailure.None);
    }

    /// <summary>
    /// 產生不含 scope 的固定 failure result。helper 不保存 evidence、request scope 或
    /// 例外，避免 failure path 留住其他使用者的 identity/target state。
    /// </summary>
    /// <param name="failure">唯一可公開的去識別化 failure 分類。</param>
    /// <returns>scope 為 null 的 fail-closed resolution。</returns>
    private static MemberInfoTargetAuthorizationResolution Fail(
        MemberInfoTargetAuthorizationFailure failure)
    {
        return new MemberInfoTargetAuthorizationResolution(null, failure);
    }
}

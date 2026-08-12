// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Operations/P72ContactOnboardingLocalDecision.cs
// 用途：定義 P7.2 continuation Slice F 新人聯絡人 onboarding 的純本機決策與 local-only
//       計畫。它只處理去識別化的 graph 完整性摘要，絕不建立 CRM 連線或寫入外部系統。
// ============================================================================

namespace SpeechMessage.Dynamics.Abstractions.Operations;

/// <summary>
/// 表示 Slice F 唯一允許的 server-owned onboarding 意圖。
///
/// <para>
/// 此列舉刻意不含 Owner 指派、名單關聯、出席、通知、刪除或任意 CRM entity 模式。那些都是未來
/// governed executor 在取得 task-owned fixture、ledger、授權、精確 read-back 與 cleanup 證據後才可
/// 依序處理的子步驟；本機決策只能辨識完整的新 graph 建立意圖，不能把其中任一子步驟暴露為呼叫端
/// 可選的權限。
/// </para>
/// </summary>
public enum P72ContactOnboardingMode
{
    /// <summary>建立完整 contact、membership 與 present-record graph 的未來受治理意圖。</summary>
    CreateFullGraph = 0
}

/// <summary>
/// 表示受治理 read-back 轉換而來的最小、新人 graph 狀態摘要。
///
/// <para>
/// 所有屬性只描述「是否已觀察到」的布林狀態；型別不得加入 CRM ID、Owner、名單、週報、連線、
/// profile、principal、Session 或 notification payload。這使每一筆 observation 都只在當前呼叫中
/// 存活，不會成為跨使用者、跨小組或跨 profile 的 routing authority 或 retained state。
/// </para>
/// </summary>
public sealed class P72ContactOnboardingLocalObservation
{
    /// <summary>read-back 是否完整可信；false 代表 timeout、paging、schema 或授權不確定。</summary>
    public required bool IsComplete { get; init; }

    /// <summary>是否已觀察到目標 contact；true 時不可猜測其是否屬於本次 fresh fixture。</summary>
    public required bool ContactExists { get; init; }

    /// <summary>是否已觀察到 owner 指派；true 代表 multi-step graph 可能已有部分完成。</summary>
    public required bool OwnerAssigned { get; init; }

    /// <summary>是否已觀察到名單 membership；true 時不可重播或自行移除未知關聯。</summary>
    public required bool MembershipCreated { get; init; }

    /// <summary>是否已觀察到 present record；true 時只可由 ledger owner 做精確 reconcile。</summary>
    public required bool PresentRecordCreated { get; init; }

    /// <summary>通知是否已被嘗試；通知不可安全刪除或重送，因此 true 一律停止目前工作。</summary>
    public required bool NotificationAttempted { get; init; }

    /// <summary>transport、取消或 child evidence 是否留下不確定結果；true 時不得 replay。</summary>
    public required bool HasUncertainOutcome { get; init; }
}

/// <summary>表示 onboarding 本機決策的固定、去識別化下一步分類。</summary>
public enum P72ContactOnboardingDisposition
{
    /// <summary>狀態不完整、不確定或不受支援；停止且禁止重播。</summary>
    NoGo = 0,

    /// <summary>完整且全新的 graph；最多只能準備未來一次受治理 dispatch。</summary>
    PrepareFutureGovernedDispatch = 1,

    /// <summary>至少一個可 ledger 化 graph 節點已存在；保持現況並禁止重播。</summary>
    AlreadyApplied = 2
}

/// <summary>表示 onboarding 決策的 bounded failure category。</summary>
public enum P72ContactOnboardingFailureCategory
{
    /// <summary>觀察完整且目前分類可安全解釋；不代表 CE 實證成功。</summary>
    None = 0,

    /// <summary>read-back 不完整、通知已嘗試、timeout 或未知 mode，故不能安全前進。</summary>
    Unavailable = 1
}

/// <summary>
/// 由未來 fresh-fixture ledger 擁有者執行的唯一反向 cleanup 節點。
///
/// <para>
/// 順序只涵蓋可由 task-owned known key 精確辨識的資料圖譜。通知刻意不在此列舉，因為它不是可安全
/// 刪除、撤回或重播的 CRM 資料節點；將其納入 cleanup 會造成額外外部副作用。
/// </para>
/// </summary>
public enum P72ContactOnboardingCleanupStep
{
    /// <summary>先移除已由本次 ledger 證明建立的 present record。</summary>
    PresentRecord = 0,

    /// <summary>接著解除已知 membership。</summary>
    Membership = 1,

    /// <summary>最後才刪除已知 task-owned contact。</summary>
    Contact = 2
}

/// <summary>
/// 封裝 Slice F immutable 本機決策結果。
///
/// <para>
/// 結果只含固定 enum 與衍生布林值，不持有 CRM identity、service、connector、lease、client、
/// Session、cache、timer、process、stream、subscription 或 background task。呼叫完成後沒有共享
/// mutable state 或資源可被其他使用者重用；真正的 resource lifecycle 仍由未來 executor 的單一
/// owner 以 finally／dispose／ledger cleanup 管理。
/// </para>
/// </summary>
public sealed class P72ContactOnboardingDecisionResult
{
    internal P72ContactOnboardingDecisionResult(
        P72ContactOnboardingDisposition disposition,
        P72ContactOnboardingFailureCategory failureCategory)
    {
        Disposition = disposition;
        FailureCategory = failureCategory;
    }

    /// <summary>只有全新、完整 graph 才可準備未來受治理 dispatch；這不是 CE 寫入授權。</summary>
    public bool CanPrepareFutureDispatch =>
        Disposition == P72ContactOnboardingDisposition.PrepareFutureGovernedDispatch;

    /// <summary>所有 prepare 結果日後都必須以精確 graph projection 做 read-back。</summary>
    public bool RequiresExactReadBack => CanPrepareFutureDispatch;

    /// <summary>除尚未 dispatch 的全新準備狀態外，所有結果均禁止重播。</summary>
    public bool ProhibitsReplay => !CanPrepareFutureDispatch;

    /// <summary>固定且去識別化的本機 disposition。</summary>
    public P72ContactOnboardingDisposition Disposition { get; }

    /// <summary>固定 failure category，不含原始 CRM／通知／transport exception。</summary>
    public P72ContactOnboardingFailureCategory FailureCategory { get; }
}

/// <summary>
/// 以純同步 reducer 決定 Slice F onboarding 是否僅能準備 future governed dispatch。
///
/// <para>
/// 此型別沒有 static mutable state；唯一 static 值是不可修改的 cleanup 順序。它不讀取環境、
/// HttpContext、Session、principal、ToolUtility、Factory、connector 或 CRM service，也不排程 detached
/// task。因此 legacy <c>NewPersonController</c> 的背景工作與 session 狀態不會被帶入新的 contract。
/// timeout、partial graph、通知嘗試或任何 ambiguous 狀態都 fail closed，不能在這裡自動補寫或重播。
/// </para>
/// </summary>
public static class P72ContactOnboardingLocalDecision
{
    private static readonly IReadOnlyList<P72ContactOnboardingCleanupStep> CleanupOrder =
        Array.AsReadOnly(
        [
            P72ContactOnboardingCleanupStep.PresentRecord,
            P72ContactOnboardingCleanupStep.Membership,
            P72ContactOnboardingCleanupStep.Contact
        ]);

    /// <summary>
    /// 取得由 future ledger owner 使用的固定 reverse cleanup 順序。
    /// 此集合不可由呼叫端修改，且不包含 notification；未知 key 永遠不可掃描或猜測。
    /// </summary>
    public static IReadOnlyList<P72ContactOnboardingCleanupStep> ReverseCleanupOrder => CleanupOrder;

    /// <summary>
    /// 將完整性與 graph 狀態轉為 bounded disposition。
    /// 完整空 graph 才可準備 future dispatch；既有可 ledger 節點為 already-applied；通知、timeout、
    /// incomplete 或未知 mode 一律 unavailable no-go。此方法沒有 I/O、retry、cleanup 或資源所有權。
    /// </summary>
    /// <param name="mode">唯一允許的 server-owned onboarding 模式。</param>
    /// <param name="observation">去識別化、只屬於當前 operation 的 read-back 摘要。</param>
    /// <returns>不含 CRM 資料或 side effect 的 immutable 決策。</returns>
    public static P72ContactOnboardingDecisionResult Resolve(
        P72ContactOnboardingMode mode,
        P72ContactOnboardingLocalObservation? observation)
    {
        if (observation is null || !observation.IsComplete || observation.HasUncertainOutcome ||
            mode != P72ContactOnboardingMode.CreateFullGraph)
        {
            return new P72ContactOnboardingDecisionResult(
                P72ContactOnboardingDisposition.NoGo,
                P72ContactOnboardingFailureCategory.Unavailable);
        }

        if (observation.NotificationAttempted)
        {
            return new P72ContactOnboardingDecisionResult(
                P72ContactOnboardingDisposition.NoGo,
                P72ContactOnboardingFailureCategory.Unavailable);
        }

        if (observation.ContactExists || observation.OwnerAssigned || observation.MembershipCreated ||
            observation.PresentRecordCreated)
        {
            return new P72ContactOnboardingDecisionResult(
                P72ContactOnboardingDisposition.AlreadyApplied,
                P72ContactOnboardingFailureCategory.None);
        }

        return new P72ContactOnboardingDecisionResult(
            P72ContactOnboardingDisposition.PrepareFutureGovernedDispatch,
            P72ContactOnboardingFailureCategory.None);
    }
}

/// <summary>
/// 封裝 onboarding 決策與可選 local-only plan 的 immutable 結果。
///
/// <para>
/// 計畫只會在 complete fresh graph 與完整 allowlist 通過時存在。失敗、already-applied、partial 或
/// uncertain 狀態一律回傳 null plan，避免呼叫端保存半成品並在另一個 Session／request 誤送 executor。
/// </para>
/// </summary>
public sealed class P72ContactOnboardingLocalPlanBuildResult
{
    internal P72ContactOnboardingLocalPlanBuildResult(
        P72ContactOnboardingDecisionResult decision,
        P72ContinuationLocalPlan? plan,
        P72LocalPlanFailureCategory failureCategory)
    {
        Decision = decision ?? throw new ArgumentNullException(nameof(decision));
        Plan = plan;
        FailureCategory = failureCategory;
    }

    /// <summary>不含 remote identity 的本機 graph 決策。</summary>
    public P72ContactOnboardingDecisionResult Decision { get; }

    /// <summary>成功時的 immutable、不可執行 local-only plan；失敗時為 null。</summary>
    public P72ContinuationLocalPlan? Plan { get; }

    /// <summary>輸入 allowlist 的固定去識別化錯誤分類。</summary>
    public P72LocalPlanFailureCategory FailureCategory { get; }

    /// <summary>只有 prepare decision 與 common plan validation 都成功時才為 true。</summary>
    public bool Succeeded =>
        Decision.CanPrepareFutureDispatch && Plan is not null &&
        FailureCategory == P72LocalPlanFailureCategory.None;
}

/// <summary>
/// 建立 Slice F 的固定 local-only plan。
///
/// <para>
/// 此 builder 只接受 opaque <c>fixtureGraphKey</c>，並立刻交由共用 builder defensive-copy。它不允許
/// caller 附帶 Owner、profile、endpoint、entity、payload 或其他 authority；也不連線 CRM、取得 lease、
/// 建立 client、使用 Session 或安排背景工作。CE dispatch 和產品 consumer 仍由 catalog 固定為 false。
/// </para>
/// </summary>
public static class P72ContactOnboardingLocalPlanBuilder
{
    /// <summary>
    /// 以 fresh graph observation 與唯一 opaque fixture key 建立 local-only plan。
    /// additionalInputs 僅供防禦性驗證，除了 null／空集合以外的任何欄位都會造成 no-plan，防止 caller
    /// 插入 routing authority。未來真正 CE cycle 仍必須擁有新的 nonce、ledger、一次 dispatch、精確
    /// read-back、reconcile 和 reverse-known-key cleanup。
    /// </summary>
    /// <param name="observation">最小、去識別化 onboarding read-back observation。</param>
    /// <param name="fixtureGraphKey">task-owned fresh fixture graph 的 bounded opaque key。</param>
    /// <param name="additionalInputs">必須為 null 或空集合；其他輸入一律拒絕。</param>
    /// <returns>決策與可選的不可執行 local-only plan。</returns>
    public static P72ContactOnboardingLocalPlanBuildResult Build(
        P72ContactOnboardingLocalObservation? observation,
        string? fixtureGraphKey,
        IReadOnlyDictionary<string, string?>? additionalInputs = null)
    {
        var decision = P72ContactOnboardingLocalDecision.Resolve(
            P72ContactOnboardingMode.CreateFullGraph,
            observation);
        if (!decision.CanPrepareFutureDispatch)
        {
            return new P72ContactOnboardingLocalPlanBuildResult(
                decision,
                null,
                P72LocalPlanFailureCategory.None);
        }

        var inputs = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["fixtureGraphKey"] = fixtureGraphKey
        };
        if (additionalInputs is not null)
        {
            foreach (var input in additionalInputs)
            {
                if (!inputs.TryAdd(input.Key, input.Value))
                {
                    return new P72ContactOnboardingLocalPlanBuildResult(
                        decision,
                        null,
                        P72LocalPlanFailureCategory.InputNamesMismatch);
                }
            }
        }

        var generic = P72ContinuationLocalOnlyPlanBuilder.Build(new P72ContinuationLocalPlanRequest
        {
            OperationId = OperationIds.NewPersonContactCreateFullOnboarding,
            Inputs = inputs
        });
        return new P72ContactOnboardingLocalPlanBuildResult(
            decision,
            generic.Plan,
            generic.FailureCategory);
    }
}

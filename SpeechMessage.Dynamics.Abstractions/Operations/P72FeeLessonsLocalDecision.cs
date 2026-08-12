// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Operations/P72FeeLessonsLocalDecision.cs
// 用途：定義 P7.2 continuation Slice G fee／stor-lesson 的純本機決策與 bounded local-only
//       draft/plan。它不接觸金額、CRM 實體、Session、ToolUtility 或任何外部資源。
// ============================================================================

namespace SpeechMessage.Dynamics.Abstractions.Operations;

/// <summary>表示 Slice G 固定且 server-owned 的變更模式。</summary>
public enum P72FeeLessonsChangeMode
{
    /// <summary>依已驗證 stor-lesson 狀態更新 fee 的未來受治理意圖。</summary>
    UpdateFeeByStorLesson = 0,

    /// <summary>由已驗證 stor-lesson 建立一筆 task-owned fee 的未來受治理意圖。</summary>
    CreateFeeFromStorLesson = 1
}

/// <summary>
/// 表示 fee／stor-lesson read-back 的最小、去識別化狀態摘要。
///
/// <para>
/// 型別只保存布林分類，不含 fee、lesson、contact、Owner、金額、幣別、profile、endpoint、credential、
/// CRM ID 或原始例外。它不應被放進 Session、ChangeHistory、static、cache、queue 或背景工作；每個
/// 觀察都屬於當前 operation，未來 executor 必須以自己的 ledger 與 exact projection 取得 known key。
/// </para>
/// </summary>
public sealed class P72FeeLessonsLocalObservation
{
    /// <summary>read-back 是否完整可信；false 時不能從預設旗標推論資料不存在。</summary>
    public required bool IsComplete { get; init; }

    /// <summary>目標範圍是否已有 fee；create 的 true 代表已套用或需要對帳，不可再建立。</summary>
    public required bool FeeExists { get; init; }

    /// <summary>stor-lesson 是否仍符合固定的 server-owned expected state。</summary>
    public required bool StorLessonExpectedState { get; init; }

    /// <summary>已知 fee 是否已具預期 owner 狀態；不攜帶 owner identity。</summary>
    public required bool OwnerAssigned { get; init; }

    /// <summary>多步 mutation 是否已有部分完成；true 時只能 reconcile/cleanup，不能 replay。</summary>
    public required bool HasPartialCompletion { get; init; }

    /// <summary>timeout、cancel 或 transport 使結果不可證明；true 時必須 no-go。</summary>
    public required bool HasUncertainOutcome { get; init; }
}

/// <summary>表示 fee／stor-lesson local-only 的受限 disposition。</summary>
public enum P72FeeLessonsDisposition
{
    /// <summary>狀態不足、timeout、partial 或未知 mode；禁止重播。</summary>
    NoGo = 0,

    /// <summary>完整且符合模式的 fresh operation；僅準備 future governed dispatch。</summary>
    PrepareFutureGovernedDispatch = 1,

    /// <summary>complete read-back 已證明 create target 存在；保持現況並禁止重播。</summary>
    AlreadyApplied = 2,

    /// <summary>部分完成必須由 future ledger owner 做 exact reconcile 與 known-key cleanup。</summary>
    RequireReconciliation = 3
}

/// <summary>表示 fee／stor-lesson 決策的 fixed failure category。</summary>
public enum P72FeeLessonsFailureCategory
{
    /// <summary>觀察完整且可安全分類；不代表 CE write 或實機證據成功。</summary>
    None = 0,

    /// <summary>incomplete、timeout、unexpected state 或未知 mode，使 remote state 不可判定。</summary>
    Unavailable = 1
}

/// <summary>
/// 封裝 immutable fee／stor-lesson 本機決策。
///
/// <para>
/// 結果只包含 enum 與衍生布林，沒有 mutable graph、ChangeHistory、CRM service、client、lease、
/// Session、cache、timer 或 detached background task。這保證 A/B operations 即使交錯也不會共享或
/// 保留前一個 fee 的狀態；真正資源與 reconciliation 擁有者必須在未來 governed executor 中明確建立。
/// </para>
/// </summary>
public sealed class P72FeeLessonsDecisionResult
{
    internal P72FeeLessonsDecisionResult(
        P72FeeLessonsDisposition disposition,
        P72FeeLessonsFailureCategory failureCategory)
    {
        Disposition = disposition;
        FailureCategory = failureCategory;
    }

    /// <summary>只有完整、無 partial／uncertain 的 observation 可準備 future dispatch。</summary>
    public bool CanPrepareFutureDispatch =>
        Disposition == P72FeeLessonsDisposition.PrepareFutureGovernedDispatch;

    /// <summary>prepare 結果日後必須以固定 fee／stor-lesson projection 做精確 read-back。</summary>
    public bool RequiresExactReadBack => CanPrepareFutureDispatch;

    /// <summary>部分完成時要求 future ledger owner 只依已知 key 做 reconciliation/cleanup。</summary>
    public bool RequiresKnownKeyCleanup =>
        Disposition == P72FeeLessonsDisposition.RequireReconciliation;

    /// <summary>除尚未 dispatch 的 fresh 準備結果外，全部分類都禁止 replay。</summary>
    public bool ProhibitsReplay => !CanPrepareFutureDispatch;

    /// <summary>固定、去識別化的操作 disposition。</summary>
    public P72FeeLessonsDisposition Disposition { get; }

    /// <summary>固定 failure category，不含 payment、CRM 或 transport raw error。</summary>
    public P72FeeLessonsFailureCategory FailureCategory { get; }
}

/// <summary>
/// 建立 Slice G 純同步、無副作用的決策。
///
/// <para>
/// reducer 不讀取 Legacy <c>FeeList</c>、<c>ChangeHistory</c>、Session、ToolUtility、Factory 或 CRM。
/// update 和 create 的多步寫入都只在未來 governed executor 中執行；任何 partial completion、
/// timeout 或 ambiguous result 都不能以本機邏輯補寫。這避免舊有「提交後清空 ChangeHistory」的模式
/// 成為重新使用跨 request mutable state 的新契約。
/// </para>
/// </summary>
public static class P72FeeLessonsLocalDecision
{
    /// <summary>
    /// 根據固定 mode 與最小 read-back observation 產生 local disposition。
    /// update 要求 fee 已存在、owner 預期且 stor-lesson 狀態正確；create 要求 fee 不存在、owner
    /// 預期且 stor-lesson 狀態正確。create 若發現 fee 已存在且沒有 partial／uncertain 結果，僅能
    /// already-applied；fee 已建立但 owner 指派失敗等 partial state 必須 reconciliation，不可 replay。
    /// </summary>
    /// <param name="mode">固定的 update 或 create mode。</param>
    /// <param name="observation">只限目前 operation 的去識別化 observation。</param>
    /// <returns>沒有 I/O、重試或資源所有權的 immutable result。</returns>
    public static P72FeeLessonsDecisionResult Resolve(
        P72FeeLessonsChangeMode mode,
        P72FeeLessonsLocalObservation? observation)
    {
        if (observation is null || !observation.IsComplete || observation.HasUncertainOutcome ||
            mode is not P72FeeLessonsChangeMode.UpdateFeeByStorLesson and not P72FeeLessonsChangeMode.CreateFeeFromStorLesson)
        {
            return new P72FeeLessonsDecisionResult(
                P72FeeLessonsDisposition.NoGo,
                P72FeeLessonsFailureCategory.Unavailable);
        }

        if (observation.HasPartialCompletion)
        {
            if (mode == P72FeeLessonsChangeMode.CreateFeeFromStorLesson && observation.FeeExists &&
                !observation.OwnerAssigned)
            {
                return new P72FeeLessonsDecisionResult(
                    P72FeeLessonsDisposition.RequireReconciliation,
                    P72FeeLessonsFailureCategory.None);
            }

            return new P72FeeLessonsDecisionResult(
                P72FeeLessonsDisposition.NoGo,
                P72FeeLessonsFailureCategory.Unavailable);
        }

        if (!observation.StorLessonExpectedState)
        {
            return new P72FeeLessonsDecisionResult(
                P72FeeLessonsDisposition.NoGo,
                P72FeeLessonsFailureCategory.Unavailable);
        }

        if (mode == P72FeeLessonsChangeMode.CreateFeeFromStorLesson)
        {
            if (!observation.OwnerAssigned)
            {
                return new P72FeeLessonsDecisionResult(
                    P72FeeLessonsDisposition.NoGo,
                    P72FeeLessonsFailureCategory.Unavailable);
            }

            return observation.FeeExists
                ? new P72FeeLessonsDecisionResult(
                    P72FeeLessonsDisposition.AlreadyApplied,
                    P72FeeLessonsFailureCategory.None)
                : new P72FeeLessonsDecisionResult(
                    P72FeeLessonsDisposition.PrepareFutureGovernedDispatch,
                    P72FeeLessonsFailureCategory.None);
        }

        if (!observation.FeeExists || !observation.OwnerAssigned)
        {
            return new P72FeeLessonsDecisionResult(
                P72FeeLessonsDisposition.NoGo,
                P72FeeLessonsFailureCategory.Unavailable);
        }

        return new P72FeeLessonsDecisionResult(
            P72FeeLessonsDisposition.PrepareFutureGovernedDispatch,
            P72FeeLessonsFailureCategory.None);
    }
}

/// <summary>
/// 封裝 Slice G decision 與可選 immutable local-only plan。
///
/// <para>
/// 失敗、already-applied 和 reconciliation 結果均不攜帶 partial plan，避免 caller 將先前的
/// draftKey、fixtureKey 或 changeSet 保存到 Session 並誤送往另一個操作。成功計畫仍保持
/// CE dispatch / product consumer 為 false。
/// </para>
/// </summary>
public sealed class P72FeeLessonsLocalPlanBuildResult
{
    internal P72FeeLessonsLocalPlanBuildResult(
        P72FeeLessonsDecisionResult decision,
        P72ContinuationLocalPlan? plan,
        P72LocalPlanFailureCategory failureCategory,
        bool isOperationLocalDraft = false)
    {
        Decision = decision ?? throw new ArgumentNullException(nameof(decision));
        Plan = plan;
        FailureCategory = failureCategory;
        IsOperationLocalDraft = isOperationLocalDraft;
    }

    /// <summary>去識別化的 fee／stor-lesson decision。</summary>
    public P72FeeLessonsDecisionResult Decision { get; }

    /// <summary>完整 allowlist 驗證成功時才存在的不可執行 immutable plan。</summary>
    public P72ContinuationLocalPlan? Plan { get; }

    /// <summary>固定的 input validation failure category。</summary>
    public P72LocalPlanFailureCategory FailureCategory { get; }

    /// <summary>
    /// 指出成功計畫是否只屬於本次 operation 的 in-memory draft。true 時它只可做 local projection
    /// 與 discard，絕不表示 CE dispatch 或 consumer 已獲授權。
    /// </summary>
    public bool IsOperationLocalDraft { get; }

    /// <summary>
    /// 僅在 future dispatch decision 已準備完成，或明確的 operation-local draft 已完成防禦性複製時為 true。
    /// 這兩種成功刻意分開：draft 的成功不會提升 <see cref="P72FeeLessonsDecisionResult.CanPrepareFutureDispatch"/>。
    /// </summary>
    public bool Succeeded =>
        (Decision.CanPrepareFutureDispatch || IsOperationLocalDraft) && Plan is not null &&
        FailureCategory == P72LocalPlanFailureCategory.None;
}

/// <summary>
/// 建立 Slice G 的 in-memory draft 與 future-governed local-only plans。
///
/// <para>
/// 所有方法都只配置 method-local dictionary，並由共用 builder 立即複製為 immutable snapshot；不保存
/// draft 到 static、<c>FeeList.ChangeHistory</c>、Session、cache 或 queue。stage 只是 local projection，
/// cleanup 固定為 discard；update/create 的 CE 對應 operation 同樣維持 catalog fail-closed，不建立
/// connector、lease、client 或 background task。
/// </para>
/// </summary>
public static class P72FeeLessonsLocalPlanBuilder
{
    /// <summary>
    /// 建立 per-operation、discard-only fee draft 的 local-only plan。任何額外 key 都會 fail closed，
    /// 不會產生可被後續 request 撿取的 partial draft。
    /// </summary>
    /// <param name="draftKey">bounded opaque draft key。</param>
    /// <param name="changeSet">bounded、server-validated change-set category。</param>
    /// <param name="additionalInputs">必須為 null 或空集合。</param>
    /// <returns>不可 dispatch 的 immutable plan 或固定 input error。</returns>
    public static P72FeeLessonsLocalPlanBuildResult StageDraft(
        string? draftKey,
        string? changeSet,
        IReadOnlyDictionary<string, string?>? additionalInputs = null)
    {
        var generic = BuildGeneric(
            OperationIds.FeesEditorStageInmemoryChange,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["draftKey"] = draftKey,
                ["changeSet"] = changeSet
            },
            additionalInputs);
        return new P72FeeLessonsLocalPlanBuildResult(
            new P72FeeLessonsDecisionResult(
                P72FeeLessonsDisposition.NoGo,
                P72FeeLessonsFailureCategory.None),
            generic.Plan,
            generic.FailureCategory,
            isOperationLocalDraft: generic.Succeeded);
    }

    /// <summary>以完整 update decision、fixture key 與 bounded change set 建立 local-only plan。</summary>
    public static P72FeeLessonsLocalPlanBuildResult BuildUpdateFeeByStorLesson(
        P72FeeLessonsLocalObservation? observation,
        string? fixtureKey,
        string? changeSet,
        IReadOnlyDictionary<string, string?>? additionalInputs = null)
        => BuildForDecision(
            P72FeeLessonsChangeMode.UpdateFeeByStorLesson,
            observation,
            OperationIds.FeesEditorUpdateByStorLesson,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["fixtureKey"] = fixtureKey,
                ["changeSet"] = changeSet
            },
            additionalInputs);

    /// <summary>以完整 create decision 與唯一 fixture key 建立 local-only plan。</summary>
    public static P72FeeLessonsLocalPlanBuildResult BuildCreateFeeFromStorLesson(
        P72FeeLessonsLocalObservation? observation,
        string? fixtureKey,
        IReadOnlyDictionary<string, string?>? additionalInputs = null)
        => BuildForDecision(
            P72FeeLessonsChangeMode.CreateFeeFromStorLesson,
            observation,
            OperationIds.FeesCreateFromStorLesson,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["fixtureKey"] = fixtureKey
            },
            additionalInputs);

    /// <summary>
    /// 將 local draft inputs 加上防禦性額外欄位檢查後交給共用 builder。此 helper 不保存任何輸入；
    /// 每次返回後可由 GC 回收暫存字典，唯一可存活的結果是由 caller 擁有的 immutable plan snapshot。
    /// </summary>
    private static P72ContinuationLocalPlanBuildResult BuildGeneric(
        string operationId,
        Dictionary<string, string?> inputs,
        IReadOnlyDictionary<string, string?>? additionalInputs)
    {
        if (!TryMerge(inputs, additionalInputs))
        {
            return P72ContinuationLocalPlanBuildResult.Failure(P72LocalPlanFailureCategory.InputNamesMismatch);
        }

        return P72ContinuationLocalOnlyPlanBuilder.Build(new P72ContinuationLocalPlanRequest
        {
            OperationId = operationId,
            Inputs = inputs
        });
    }

    /// <summary>先解析 decision；沒有 prepare 許可時不可建立 partial plan。</summary>
    private static P72FeeLessonsLocalPlanBuildResult BuildForDecision(
        P72FeeLessonsChangeMode mode,
        P72FeeLessonsLocalObservation? observation,
        string operationId,
        Dictionary<string, string?> inputs,
        IReadOnlyDictionary<string, string?>? additionalInputs)
    {
        var decision = P72FeeLessonsLocalDecision.Resolve(mode, observation);
        if (!decision.CanPrepareFutureDispatch)
        {
            return new P72FeeLessonsLocalPlanBuildResult(
                decision,
                null,
                P72LocalPlanFailureCategory.None);
        }

        var generic = BuildGeneric(operationId, inputs, additionalInputs);
        return new P72FeeLessonsLocalPlanBuildResult(
            decision,
            generic.Plan,
            generic.FailureCategory);
    }

    /// <summary>
    /// 合併 optional inputs 時只接受空集合；任何 non-empty source 都無法與 catalog 完整 key set 相符。
    /// 採逐項 TryAdd 保留 deterministic 行為並避免覆寫基礎 allowlist 值。
    /// </summary>
    private static bool TryMerge(
        IDictionary<string, string?> inputs,
        IReadOnlyDictionary<string, string?>? additionalInputs)
    {
        if (additionalInputs is null)
        {
            return true;
        }

        foreach (var input in additionalInputs)
        {
            if (!inputs.TryAdd(input.Key, input.Value))
            {
                return false;
            }
        }

        return additionalInputs.Count == 0;
    }
}

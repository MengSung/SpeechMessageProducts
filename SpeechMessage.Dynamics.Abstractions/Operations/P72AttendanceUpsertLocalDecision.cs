// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Operations/P72AttendanceUpsertLocalDecision.cs
// 用途：定義 P7.2 continuation Slice H attendance download-create／upload-upsert 的純本機
//       cardinality 決策。它整合既有週報分類，但不建立、修改或刪除任何 CRM 資料。
// ============================================================================

namespace SpeechMessage.Dynamics.Abstractions.Operations;

/// <summary>表示 attendance 的兩個 server-owned local-only operation modes。</summary>
public enum P72AttendanceUpsertMode
{
    /// <summary>下載流程只允許在目標 attendance 不存在時準備建立。</summary>
    DownloadCreate = 0,

    /// <summary>上傳流程僅依 exact attendance cardinality 準備建立或更新。</summary>
    UploadUpsert = 1
}

/// <summary>
/// 表示 attendance read-back 的最小、去識別化輸入。
///
/// <para>
/// 這個型別只保留完整性、attendance cardinality、預期 state、transport 不確定性與已分類週報決策。
/// 它不得加入 attendance／週報 record ID、contact、Owner、小組、follow-up、profile、principal、
/// endpoint、credential、connector 或 raw CRM entity。週報 decision 必須先由既有純 reducer 產生，
/// 因此本 reducer 無法從 count 猜選或保留任一週報 reference。
/// </para>
/// </summary>
public sealed class P72AttendanceUpsertLocalObservation
{
    /// <summary>attendance read-back 是否完整；false 表示 timeout、paging 或 schema 不可信。</summary>
    public required bool IsComplete { get; init; }

    /// <summary>目標 attendance cardinality；負數無效，兩筆以上代表 duplicate。</summary>
    public required int ExistingAttendanceCount { get; init; }

    /// <summary>完整 read-back 是否已證明預期 present state 已套用。</summary>
    public required bool IsExpectedStateApplied { get; init; }

    /// <summary>timeout、取消、child failure 或 ambiguous dispatch 是否使結果不確定。</summary>
    public required bool HasUncertainOutcome { get; init; }

    /// <summary>預先去識別化的週報 cardinality decision，不包含 CRM identity。</summary>
    public required P72AttendanceWeeklyReportDecisionResult WeeklyReportDecision { get; init; }
}

/// <summary>表示 attendance 本機操作的 bounded disposition。</summary>
public enum P72AttendanceUpsertLocalDisposition
{
    /// <summary>資料不完整、duplicate、衝突或未知 mode；禁止重播。</summary>
    NoGo = 0,

    /// <summary>zero-active weekly report 下準備建立，未來 executor 必須維持不關聯週報。</summary>
    PrepareCreateUnlinked = 1,

    /// <summary>exactly-one-active weekly report 下準備建立，未來 executor 必須精確連結並 read-back。</summary>
    PrepareCreateLinked = 2,

    /// <summary>zero-active weekly report 下準備更新，未來 executor 必須維持不關聯週報。</summary>
    PrepareUpdateUnlinked = 3,

    /// <summary>exactly-one-active weekly report 下準備更新，未來 executor 必須精確連結並 read-back。</summary>
    PrepareUpdateLinked = 4,

    /// <summary>完整 read-back 已證明 expected attendance state，保持現況並禁止重播。</summary>
    AlreadyApplied = 5,

    /// <summary>future executor 必須對已知狀態做精確 reconciliation，不可在本機層補寫。</summary>
    RequireReconciliation = 6,

    /// <summary>future ledger owner 必須依 known key 執行 reverse cleanup，不可猜測 record。</summary>
    RequireCleanup = 7
}

/// <summary>表示 attendance 決策的固定 failure category。</summary>
public enum P72AttendanceUpsertLocalFailureCategory
{
    /// <summary>狀態可安全分類；不代表 CE write、read-back 或 cleanup 已完成。</summary>
    None = 0,

    /// <summary>incomplete、uncertain、invalid weekly decision 或未知 mode 使狀態不可證明。</summary>
    Unavailable = 1,

    /// <summary>download-create 發現已有 target，不能隱式降級為 update。</summary>
    CreateTargetAlreadyExists = 2,

    /// <summary>目標範圍有兩筆以上 attendance，不可掃描、猜選或更新其中一筆。</summary>
    DuplicateAttendance = 3
}

/// <summary>
/// 封裝 immutable attendance local decision。
///
/// <para>
/// 結果不含 CRM、使用者、週報、contact、owner、group、follow-up 或連線資料；它也不擁有 client、
/// lease、Session、cache、timer、queue 或 background task。唯一 cleanup policy 是 metadata，真正的
/// cleanup 只能由 future CE ledger owner 對精確 known key 執行，故每個 A/B request 不會共享 mutable
/// 參考或資源生命週期。
/// </para>
/// </summary>
public sealed class P72AttendanceUpsertLocalDecisionResult
{
    internal P72AttendanceUpsertLocalDecisionResult(
        P72AttendanceUpsertLocalDisposition disposition,
        P72AttendanceUpsertLocalFailureCategory failureCategory)
    {
        Disposition = disposition;
        FailureCategory = failureCategory;
    }

    /// <summary>只有四個 Prepare 分支才可準備 future governed dispatch。</summary>
    public bool CanPrepareFutureDispatch => Disposition is
        P72AttendanceUpsertLocalDisposition.PrepareCreateUnlinked or
        P72AttendanceUpsertLocalDisposition.PrepareCreateLinked or
        P72AttendanceUpsertLocalDisposition.PrepareUpdateUnlinked or
        P72AttendanceUpsertLocalDisposition.PrepareUpdateLinked;

    /// <summary>任何 prepare branch 均需 future executor 以 fixed projection 做 exact read-back。</summary>
    public bool RequiresExactReadBack => CanPrepareFutureDispatch;

    /// <summary>除了尚未 dispatch 的 prepare 狀態外，所有結論一律禁止 replay。</summary>
    public bool ProhibitsReplay => !CanPrepareFutureDispatch;

    /// <summary>attendance CE operations 的未來 cleanup 必須只使用 ledger known keys。</summary>
    public P72LocalCleanupPolicy CleanupPolicy => P72LocalCleanupPolicy.ReverseKnownKeys;

    /// <summary>去識別化、bounded disposition。</summary>
    public P72AttendanceUpsertLocalDisposition Disposition { get; }

    /// <summary>固定 failure category，不含 raw CRM 例外或 identity。</summary>
    public P72AttendanceUpsertLocalFailureCategory FailureCategory { get; }
}

/// <summary>
/// 建立 Slice H attendance 的純本機 cardinality reducer。
///
/// <para>
/// reducer 只使用當前 observation 與既有 weekly decision，沒有 static mutable state。它不讀取或修改
/// Session、HttpContext、ListSmallGroupWeeklyReport、ToolUtility、connector、CRM service、contact、owner、
/// group 或 follow-up，也不建立 detached upload/background work。zero-active 是合法不關聯分支；
/// exactly-one-active 必須標示精確連結；duplicate/unavailable、timeout 和 ambiguous 皆 fail closed。
/// </para>
/// </summary>
public static class P72AttendanceUpsertLocalDecision
{
    /// <summary>
    /// 將 attendance 與 weekly cardinality 轉為 bounded create/update/no-go decision。
    /// download create 僅接受 zero attendance；upload upsert 僅接受 zero/create 或 exactly-one/update；
    /// expected state 已存在時是 already-applied。此方法不發出 CE dispatch，未來一次 mutation 仍需
    /// 獨立 fresh fixture、ledger、exact read-back、reconcile 與 deterministic cleanup。
    /// </summary>
    /// <param name="mode">固定 download-create 或 upload-upsert mode。</param>
    /// <param name="observation">只限 current operation 的去識別化 observation。</param>
    /// <returns>沒有外部資料、I/O 或重播行為的 immutable result。</returns>
    public static P72AttendanceUpsertLocalDecisionResult Resolve(
        P72AttendanceUpsertMode mode,
        P72AttendanceUpsertLocalObservation? observation)
    {
        if (observation is null || !observation.IsComplete || observation.HasUncertainOutcome ||
            observation.ExistingAttendanceCount < 0 || observation.WeeklyReportDecision is null ||
            mode is not P72AttendanceUpsertMode.DownloadCreate and not P72AttendanceUpsertMode.UploadUpsert)
        {
            return Result(P72AttendanceUpsertLocalDisposition.NoGo, P72AttendanceUpsertLocalFailureCategory.Unavailable);
        }

        if (!observation.WeeklyReportDecision.CanProceed)
        {
            return Result(P72AttendanceUpsertLocalDisposition.NoGo, P72AttendanceUpsertLocalFailureCategory.Unavailable);
        }

        if (observation.ExistingAttendanceCount > 1)
        {
            return Result(P72AttendanceUpsertLocalDisposition.NoGo, P72AttendanceUpsertLocalFailureCategory.DuplicateAttendance);
        }

        if (observation.ExistingAttendanceCount == 0 && observation.IsExpectedStateApplied)
        {
            return Result(P72AttendanceUpsertLocalDisposition.NoGo, P72AttendanceUpsertLocalFailureCategory.Unavailable);
        }

        if (observation.ExistingAttendanceCount == 1 && observation.IsExpectedStateApplied)
        {
            return Result(P72AttendanceUpsertLocalDisposition.AlreadyApplied, P72AttendanceUpsertLocalFailureCategory.None);
        }

        if (mode == P72AttendanceUpsertMode.DownloadCreate && observation.ExistingAttendanceCount != 0)
        {
            return Result(
                P72AttendanceUpsertLocalDisposition.NoGo,
                P72AttendanceUpsertLocalFailureCategory.CreateTargetAlreadyExists);
        }

        return (mode, observation.ExistingAttendanceCount, observation.WeeklyReportDecision.Disposition) switch
        {
            (_, 0, P72AttendanceWeeklyReportDisposition.ProceedUnlinked) =>
                Result(P72AttendanceUpsertLocalDisposition.PrepareCreateUnlinked, P72AttendanceUpsertLocalFailureCategory.None),
            (_, 0, P72AttendanceWeeklyReportDisposition.ProceedWithExactLink) =>
                Result(P72AttendanceUpsertLocalDisposition.PrepareCreateLinked, P72AttendanceUpsertLocalFailureCategory.None),
            (P72AttendanceUpsertMode.UploadUpsert, 1, P72AttendanceWeeklyReportDisposition.ProceedUnlinked) =>
                Result(P72AttendanceUpsertLocalDisposition.PrepareUpdateUnlinked, P72AttendanceUpsertLocalFailureCategory.None),
            (P72AttendanceUpsertMode.UploadUpsert, 1, P72AttendanceWeeklyReportDisposition.ProceedWithExactLink) =>
                Result(P72AttendanceUpsertLocalDisposition.PrepareUpdateLinked, P72AttendanceUpsertLocalFailureCategory.None),
            _ => Result(P72AttendanceUpsertLocalDisposition.NoGo, P72AttendanceUpsertLocalFailureCategory.Unavailable)
        };
    }

    /// <summary>集中建立 bounded result，避免各分支重複配置或遺漏 failure category。</summary>
    private static P72AttendanceUpsertLocalDecisionResult Result(
        P72AttendanceUpsertLocalDisposition disposition,
        P72AttendanceUpsertLocalFailureCategory failureCategory)
        => new(disposition, failureCategory);
}

/// <summary>
/// 封裝 attendance decision 與可選 local-only plan。
///
/// <para>
/// 不可安全的 weekly、duplicate、timeout、already-applied 或 input error 都不提供 partial plan。這避免
/// legacy upload path 將 contact/owner/group/follow-up 的 mutation 權限偷渡給新 contract，也不會保留
/// caller dictionary 到後續 request。
/// </para>
/// </summary>
public sealed class P72AttendanceUpsertLocalPlanBuildResult
{
    internal P72AttendanceUpsertLocalPlanBuildResult(
        P72AttendanceUpsertLocalDecisionResult decision,
        P72ContinuationLocalPlan? plan,
        P72LocalPlanFailureCategory failureCategory)
    {
        Decision = decision ?? throw new ArgumentNullException(nameof(decision));
        Plan = plan;
        FailureCategory = failureCategory;
    }

    /// <summary>去識別化 attendance／weekly decision。</summary>
    public P72AttendanceUpsertLocalDecisionResult Decision { get; }

    /// <summary>成功時只含 defensive-copied bounded input 的 immutable local-only plan。</summary>
    public P72ContinuationLocalPlan? Plan { get; }

    /// <summary>輸入 allowlist 的固定 failure category。</summary>
    public P72LocalPlanFailureCategory FailureCategory { get; }

    /// <summary>只有 Prepare decision 和 semantic plan validation 都成功時為 true。</summary>
    public bool Succeeded =>
        Decision.CanPrepareFutureDispatch && Plan is not null &&
        FailureCategory == P72LocalPlanFailureCategory.None;
}

/// <summary>
/// 建立 Slice H attendance local-only plan。
///
/// <para>
/// builder 選擇的唯一 operation ID 由 server-owned mode 決定，永遠只接受 attendanceKey、weekStartDate、
/// presentState 三個 catalog keys。它交由既有 semantic builder 驗證 ISO Sunday 與固定 state，之後立即
/// defensive-copy；不接受 weekly ID、Owner、Contact、group、follow-up、profile 或 endpoint，也不執行
/// CRM、Data8、ToolUtility、Session、feature flag 或產品 consumer。
/// </para>
/// </summary>
public static class P72AttendanceUpsertLocalPlanBuilder
{
    /// <summary>
    /// 依 attendance decision 建立 bounded local-only plan。weekly report decision 已 no-go 或 mode 不合法時
    /// 直接回傳 null plan；成功結果仍保持 catalog 的 CE dispatch 與 product consumer 皆為 false。
    /// </summary>
    /// <param name="mode">server-owned attendance mode。</param>
    /// <param name="observation">最小、去識別化 observation。</param>
    /// <param name="inputs">只含 attendanceKey、weekStartDate、presentState 的 caller dictionary。</param>
    /// <returns>decision 與可選的不可執行 local-only plan。</returns>
    public static P72AttendanceUpsertLocalPlanBuildResult Build(
        P72AttendanceUpsertMode mode,
        P72AttendanceUpsertLocalObservation? observation,
        IReadOnlyDictionary<string, string?>? inputs)
    {
        var decision = P72AttendanceUpsertLocalDecision.Resolve(mode, observation);
        if (!decision.CanPrepareFutureDispatch)
        {
            return new P72AttendanceUpsertLocalPlanBuildResult(
                decision,
                null,
                P72LocalPlanFailureCategory.None);
        }

        var operationId = mode == P72AttendanceUpsertMode.DownloadCreate
            ? OperationIds.PresentRecordCreateOnDownload
            : OperationIds.PresentRecordUpsertOnUpload;
        var generic = P72AttendanceLocalPlanBuilder.Build(operationId, inputs);
        return new P72AttendanceUpsertLocalPlanBuildResult(
            decision,
            generic.Plan,
            generic.FailureCategory);
    }
}

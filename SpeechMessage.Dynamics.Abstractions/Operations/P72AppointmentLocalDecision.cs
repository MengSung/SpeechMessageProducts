// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Operations/P72AppointmentLocalDecision.cs
// 用途：以純本機、零 I/O 的方式定義 P7.2 continuation Slice E appointment create/update 的
//       cardinality、部分完成與 timeout no-replay 決策；本檔案絕不執行 CRM 寫入。
// ============================================================================

namespace SpeechMessage.Dynamics.Abstractions.Operations;

/// <summary>
/// 表示 appointment 的固定變更模式。
///
/// <para>
/// 此型別不接受 delete、assign、schedule、Owner、entity、CRM ID 或其他 caller authority。舊流程的
/// create、assign 與 schedule 是多步動作；Slice E 本機層只將 create/update 分開分類，未來受治理
/// executor 必須為每一步建立 ledger、read-back、reconciliation 與 reverse cleanup，不能把這個
/// enum 當作多步寫入的授權或交易保證。
/// </para>
/// </summary>
public enum P72AppointmentChangeMode
{
    /// <summary>
    /// 建立一筆 task-owned fresh appointment 的意圖。任何既存目標都不是自動更新或重用的許可。
    /// </summary>
    Create = 0,

    /// <summary>
    /// 更新一筆已由受治理 read-back 精確證明存在的 appointment 的意圖。
    /// </summary>
    Update = 1
}

/// <summary>
/// 描述 appointment 目標的最小、去識別化 read-back observation。
///
/// <para>
/// <see cref="IsComplete"/> 由擁有受治理查詢範圍的元件設定；false 代表 timeout、ambiguous transport、
/// paging、schema 或授權問題，不能把預設 count 視為零。<see cref="ExistingRecordCount"/> 只保留
/// cardinality，不攜帶 CRM ID、名稱、Owner、profile、endpoint、credential、token、Session 或 raw
/// exception。所有值只在呼叫堆疊中使用，不能存入 static、cache、singleton、queue 或背景工作。
/// </para>
/// </summary>
public sealed class P72AppointmentLocalObservation
{
    /// <summary>
    /// 指出受治理 read-back 是否完整可信。false 時必須在 connector、lease 或 CRM client 前 fail closed。
    /// </summary>
    public required bool IsComplete { get; init; }

    /// <summary>
    /// 精確目標範圍內的 record cardinality；負數代表本機觀察無效，零或一以外的正數代表 duplicate。
    /// </summary>
    public required int ExistingRecordCount { get; init; }

    /// <summary>
    /// 指出完整 read-back 是否已證明預期變更已存在。true 時不得重播 create、update、assign 或 schedule。
    /// </summary>
    public required bool IsTargetStateAlreadyApplied { get; init; }
}

/// <summary>
/// 表示 appointment local-only 決策的固定 disposition。
/// </summary>
public enum P72AppointmentDisposition
{
    /// <summary>
    /// observation 或 mode 不可證明安全，停止並禁止重播。
    /// </summary>
    NoGo = 0,

    /// <summary>
    /// 完整且 zero-target 的建立意圖；只可準備未來受治理 create，不能直接 dispatch。
    /// </summary>
    PrepareCreate = 1,

    /// <summary>
    /// 完整且 exactly-one-target 的更新意圖；只可準備未來受治理 update，不能直接 dispatch。
    /// </summary>
    PrepareUpdate = 2,

    /// <summary>
    /// 完整 read-back 已證明目標狀態存在，保持現況並禁止重播。
    /// </summary>
    AlreadyApplied = 3
}

/// <summary>
/// 表示 appointment 本機決策的 bounded failure category。
/// </summary>
public enum P72AppointmentFailureCategory
{
    /// <summary>
    /// observation 完整且沒有 fail-closed 原因；此值不代表 CE evidence 或產品切流已完成。
    /// </summary>
    None = 0,

    /// <summary>
    /// timeout、ambiguous、incomplete、負數 cardinality 或不支援 mode，使當前狀態不可安全判定。
    /// </summary>
    Unavailable = 1,

    /// <summary>
    /// update 需要的 target 不存在；不得把 update 降級為 create。
    /// </summary>
    TargetMissing = 2,

    /// <summary>
    /// create 發現已有一筆尚未達目標狀態的 target；不得把 create 自動改為 update。
    /// </summary>
    TargetAlreadyExists = 3,

    /// <summary>
    /// 目標範圍有多筆資料；不得掃描、猜選或任意指定其中一筆。
    /// </summary>
    DuplicateTarget = 4
}

/// <summary>
/// 封裝 immutable appointment 本機決策的結果。
///
/// <para>
/// 結果只保留受限 disposition 與分類，不含 CRM、使用者或連線資訊。它不持有 Session、cache、
/// ToolUtility、connector、lease、client、timer、process、stream、subscription 或 background task；
/// 因此每個 request 可安全獨立處理，且沒有資源或 mutable state 需交給下一位使用者。
/// </para>
/// </summary>
public sealed class P72AppointmentDecisionResult
{
    internal P72AppointmentDecisionResult(
        P72AppointmentDisposition disposition,
        P72AppointmentFailureCategory failureCategory)
    {
        Disposition = disposition;
        FailureCategory = failureCategory;
    }

    /// <summary>
    /// 指出本機層是否可準備未來受治理操作。true 不代表能進行 CE dispatch、產品 consumer、feature flag、
    /// P7.4 cutover 或 P7.5 ToolUtility 移除；catalog 與 executor 仍固定 fail closed。
    /// </summary>
    public bool CanPrepareFutureDispatch =>
        Disposition is P72AppointmentDisposition.PrepareCreate or P72AppointmentDisposition.PrepareUpdate;

    /// <summary>
    /// 指出未來受治理 create/update 是否必須進行 exact read-back。已處理與 no-go 不會提供可重播計畫。
    /// </summary>
    public bool RequiresExactReadBack => CanPrepareFutureDispatch;

    /// <summary>
    /// 指出目前結果是否禁止重播。除了尚未 dispatch 的 fresh create/update 準備狀態以外，所有分類都禁止
    /// 重播；即使準備狀態日後遭遇 timeout、ambiguous、no-go、read-back mismatch 或 cleanup uncertainty，
    /// 也必須轉為禁止重播的終態。
    /// </summary>
    public bool ProhibitsReplay => !CanPrepareFutureDispatch;

    /// <summary>
    /// 受限的本機下一步分類。
    /// </summary>
    public P72AppointmentDisposition Disposition { get; }

    /// <summary>
    /// 不含原始 CRM／provider 例外的固定失敗分類。
    /// </summary>
    public P72AppointmentFailureCategory FailureCategory { get; }
}

/// <summary>
/// 建立 Slice E appointment 的同步、無副作用本機決策。
///
/// <para>
/// 此 reducer 不會呼叫 CRM、Data8、ToolUtility、Factory、Session、cache、connector、lease、client、
/// timer、process 或背景工作。它只根據 server-owned fixed mode 與受治理 read-back 的 cardinality 做
/// fail-closed 分類，因此不會接受 caller 指定 Owner、entity、profile、endpoint、credential、token 或
/// 目標 ID，並避免 session-cached legacy `AppointmentsListManager` 的 mutable state 成為新路徑依賴。
/// </para>
/// </summary>
public static class P72AppointmentLocalDecision
{
    /// <summary>
    /// 解析 appointment create/update 的本機 disposition。
    ///
    /// <para>
    /// create 僅接受完整 zero-target；update 僅接受完整 exactly-one-target；目標已達成時禁止重播；
    /// duplicate、missing、已存在、timeout、ambiguous 與 invalid observation 一律 fail closed。這是未來
    /// governed executor 的前置語意，不是替代 CE fixture、ledger、read-back、reconcile 或 cleanup 的證據。
    /// </para>
    /// </summary>
    /// <param name="mode">固定 create 或 update mode。</param>
    /// <param name="observation">最小、去識別化的精確目標 read-back observation。</param>
    /// <returns>不含 side effect 或外部資料的 immutable 決策。</returns>
    public static P72AppointmentDecisionResult Resolve(
        P72AppointmentChangeMode mode,
        P72AppointmentLocalObservation? observation)
    {
        if (observation is null || !observation.IsComplete || observation.ExistingRecordCount < 0 ||
            (mode != P72AppointmentChangeMode.Create && mode != P72AppointmentChangeMode.Update))
        {
            return new P72AppointmentDecisionResult(
                P72AppointmentDisposition.NoGo,
                P72AppointmentFailureCategory.Unavailable);
        }

        if (observation.ExistingRecordCount > 1)
        {
            return new P72AppointmentDecisionResult(
                P72AppointmentDisposition.NoGo,
                P72AppointmentFailureCategory.DuplicateTarget);
        }

        if (mode == P72AppointmentChangeMode.Create)
        {
            if (observation.ExistingRecordCount == 0 && !observation.IsTargetStateAlreadyApplied)
            {
                return new P72AppointmentDecisionResult(
                    P72AppointmentDisposition.PrepareCreate,
                    P72AppointmentFailureCategory.None);
            }

            if (observation.ExistingRecordCount == 1 && observation.IsTargetStateAlreadyApplied)
            {
                return new P72AppointmentDecisionResult(
                    P72AppointmentDisposition.AlreadyApplied,
                    P72AppointmentFailureCategory.None);
            }

            return new P72AppointmentDecisionResult(
                P72AppointmentDisposition.NoGo,
                observation.ExistingRecordCount == 0
                    ? P72AppointmentFailureCategory.Unavailable
                    : P72AppointmentFailureCategory.TargetAlreadyExists);
        }

        if (observation.ExistingRecordCount == 0)
        {
            return new P72AppointmentDecisionResult(
                P72AppointmentDisposition.NoGo,
                P72AppointmentFailureCategory.TargetMissing);
        }

        return observation.IsTargetStateAlreadyApplied
            ? new P72AppointmentDecisionResult(
                P72AppointmentDisposition.AlreadyApplied,
                P72AppointmentFailureCategory.None)
            : new P72AppointmentDecisionResult(
                P72AppointmentDisposition.PrepareUpdate,
                P72AppointmentFailureCategory.None);
    }
}

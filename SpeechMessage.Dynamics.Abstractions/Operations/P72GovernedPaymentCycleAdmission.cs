// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Operations/P72GovernedPaymentCycleAdmission.cs
// 用途：定義 P7.2 定期奉獻付款回傳受控寫入家族的純本機 cycle admission state machine。
//       它只處理去識別化、immutable 的階段證據，不建立 CRM/Data8 client、不讀寫檔案或網路，
//       也不接線 feature gate、Session、HttpContext、ToolUtility 或 legacy payment writer。
// ============================================================================

namespace SpeechMessage.Dynamics.Abstractions.Operations;

/// <summary>
/// 表示受控付款 cycle 已由 future executor 證明的階段。
///
/// <para>
/// 階段只描述本 task-owned fresh fixture cycle 的去識別化進度，不帶 CRM ID、fixture marker、
/// Owner、profile、endpoint、credential、token、原始回應或例外。future executor 必須將每一個實際
/// side effect 寫入自己的 single-writer ledger；本 enum 不會建立、保存或猜選任何遠端資料。
/// </para>
/// </summary>
public enum P72GovernedPaymentCycleStage
{
    /// <summary>尚未做 I/O，只能驗證 fresh binding 後要求零 mutation preflight。</summary>
    Bootstrap = 0,

    /// <summary>future executor 已完成零 mutation preflight，尚未建立 fresh fixture。</summary>
    PreflightCompleted = 1,

    /// <summary>本次 ledger 已證明 fresh fixture 的 known keys，尚未發送付款寫入。</summary>
    Provisioned = 2,

    /// <summary>恰好一個 allowlisted mutation 已送出，必須先做 exact read-back。</summary>
    Dispatched = 3,

    /// <summary>fixed typed projection 已完成，必須確認 remote effect 後才能 cleanup。</summary>
    ReadBackVerified = 4,

    /// <summary>reconciliation 已確認全部 ledger-known effects，下一步只能 cleanup。</summary>
    Reconciled = 5,

    /// <summary>reverse-known-key cleanup 與最後 read-back 已完成或明確失敗。</summary>
    CleanupVerified = 6
}

/// <summary>
/// 表示零 mutation preflight 的固定、去識別化結果分類。
///
/// <para>
/// 這個分類不暴露 CRM 查詢、Owner、帳號、端點或 baseline。只有 <see cref="Go"/> 允許 future
/// executor provision 本次 fresh fixture；其餘分類都是該 cycle family 的 terminal no-go，不能用 retry、
/// CRM user scan、猜選 Owner 或建立共享資料補救。
/// </para>
/// </summary>
public enum P72GovernedPaymentPreflightOutcome
{
    /// <summary>尚未執行 preflight，不能推論 fixture 或寫入權限。</summary>
    NotRun = 0,

    /// <summary>所有 fixed read-only preflight 條件均已獨立證明。</summary>
    Go = 1,

    /// <summary>deployment-owned probe、權限或固定必要資料不可用。</summary>
    Unavailable = 2,

    /// <summary>target fixture cardinality 非唯一，不能選擇或修改其中任一筆。</summary>
    DuplicateFixture = 3,

    /// <summary>server-derived writer authorization 或 distinct owner binding 未獲證明。</summary>
    AuthorizationUnproven = 4,

    /// <summary>fresh fixture 的 preimage/baseline 無法以固定 projection 證明。</summary>
    BaselineUnprovable = 5
}

/// <summary>
/// 表示恰好一次 future allowlisted dispatch 的去識別化結果。
///
/// <para>
/// <see cref="Timeout"/>、<see cref="Ambiguous"/> 與 <see cref="Partial"/> 都表示遠端效果不可證明。
/// 它們不允許 reducer 重新打開 dispatch；outer ledger owner 僅能依 exact known keys 停止、read-back、
/// reconcile 或 cleanup，且不得重播 mutation。
/// </para>
/// </summary>
public enum P72GovernedPaymentDispatchOutcome
{
    /// <summary>尚未嘗試 mutation。</summary>
    NotAttempted = 0,

    /// <summary>transport 已回覆一次 mutation 完成；仍未證明 postimage。</summary>
    Applied = 1,

    /// <summary>transport timeout，可能已套用也可能未套用。</summary>
    Timeout = 2,

    /// <summary>transport 或 child process 結果模糊，不能安全推論 remote state。</summary>
    Ambiguous = 3,

    /// <summary>已知多步或 payload 效果部分完成，不能視為成功 transaction。</summary>
    Partial = 4
}

/// <summary>表示固定 typed postimage read-back 的去識別化結果。</summary>
public enum P72GovernedPaymentReadBackOutcome
{
    /// <summary>尚未進行 read-back。</summary>
    NotRun = 0,

    /// <summary>fixed projection 與本次 ledger 記錄的預期 postimage 完全相符。</summary>
    ExactMatch = 1,

    /// <summary>fixed projection 與預期 postimage 不相符。</summary>
    Mismatch = 2,

    /// <summary>projection 缺少、逾時或無法取得，不能推論狀態未變。</summary>
    Unavailable = 3
}

/// <summary>表示 exact read-back 後的去識別化 reconciliation 結果。</summary>
public enum P72GovernedPaymentReconciliationOutcome
{
    /// <summary>尚未 reconciliation。</summary>
    NotRun = 0,

    /// <summary>已確認所有 effect 都對應本次 ledger 的 known keys 與預期狀態。</summary>
    ExactEffectConfirmed = 1,

    /// <summary>至少一個 effect 無法歸屬或狀態不明，不能進入 cleanup complete。</summary>
    UnknownEffect = 2
}

/// <summary>表示 reverse-known-key cleanup 的去識別化結果。</summary>
public enum P72GovernedPaymentCleanupOutcome
{
    /// <summary>尚未 cleanup。</summary>
    NotRun = 0,

    /// <summary>已依固定反向順序完成 cleanup，並由 final fixed projection 證明。</summary>
    Completed = 1,

    /// <summary>cleanup transport 或 final read-back 不確定。</summary>
    Uncertain = 2,

    /// <summary>cleanup 已確定失敗；不得宣稱 cycle 可釋出。</summary>
    Failed = 3
}

/// <summary>
/// 封裝 cycle admission 的 immutable、去識別化輸入。
///
/// <para>
/// 此 record 只容納 future executor 已完成的固定分類與布林證據。它刻意不含 nonce 字串、ledger path、
/// CRM record ID、Owner identity、ProfileAlias、endpoint、credential、Entity、client、lease、Session、
/// HttpContext、token、raw response 或 exception。每個 invocation 都使用自己的 immutable instance，故
/// A/B request、不同 profile 或不同 cycle 不會共享 mutable state；真實 nonce/ledger/fixture 的保護與
/// dispose/cleanup 所有權則屬於 future executor 的另一個明確 scope。
/// </para>
/// </summary>
public sealed record P72GovernedPaymentCycleAdmissionObservation
{
    /// <summary>future executor 已證明的 cycle stage。</summary>
    public required P72GovernedPaymentCycleStage Stage { get; init; }

    /// <summary>family binding 是否明確是全新 payment family，而非 historical Slice C 或其他 family。</summary>
    public required bool IsFreshFamilyBinding { get; init; }

    /// <summary>future executor 是否持有非空、僅屬本 cycle 的 nonce；值本身不進入此 contract。</summary>
    public required bool HasNonEmptyNonce { get; init; }

    /// <summary>task-owned descriptor 是否完整、immutable 且已與本 family binding。</summary>
    public required bool HasCompleteTaskOwnedDescriptor { get; init; }

    /// <summary>single-writer ledger 是否已固定繫結於 family/descriptor/nonce，不能接受 caller 覆寫。</summary>
    public required bool IsLedgerBoundToFamily { get; init; }

    /// <summary>provision 前 ledger 是否可證明為空，避免 stale 或 prior-cycle effect 被誤認為本次資料。</summary>
    public required bool WasLedgerEmptyBeforeProvision { get; init; }

    /// <summary>server-derived writer authorization 與 required owner binding 是否均已證明。</summary>
    public required bool HasServerAuthorizedWriter { get; init; }

    /// <summary>本 slice 是否恰好定義一個 mutation allowlist entry，而非 generic CRUD 或多步 batch。</summary>
    public required bool HasExactlyOneAllowlistedMutation { get; init; }

    /// <summary>descriptor 是否定義 fixed typed preimage/postimage projection，供 exact read-back 使用。</summary>
    public required bool HasExpectedExactProjection { get; init; }

    /// <summary>provision 後 ledger 是否已記錄所有 fresh fixture 的 exact known keys；值本身不暴露。</summary>
    public required bool HasKnownProvisionedKeys { get; init; }

    /// <summary>零 mutation preflight 的固定分類。</summary>
    public required P72GovernedPaymentPreflightOutcome PreflightOutcome { get; init; }

    /// <summary>本 ledger 已記錄的 mutation dispatch 次數；只有精確的一次可進入 read-back。</summary>
    public required int DispatchCount { get; init; }

    /// <summary>是否至少已可能送出一次 mutation；true 後不允許以 reducer retry。</summary>
    public required bool OperationExecuted { get; init; }

    /// <summary>一次 dispatch 的去識別化 transport/result 分類。</summary>
    public required P72GovernedPaymentDispatchOutcome DispatchOutcome { get; init; }

    /// <summary>fixed typed postimage read-back 的分類。</summary>
    public required P72GovernedPaymentReadBackOutcome ReadBackOutcome { get; init; }

    /// <summary>ledger-known effects 的 reconciliation 分類。</summary>
    public required P72GovernedPaymentReconciliationOutcome ReconciliationOutcome { get; init; }

    /// <summary>reverse-known-key cleanup 的分類。</summary>
    public required P72GovernedPaymentCleanupOutcome CleanupOutcome { get; init; }
}

/// <summary>表示 pure cycle admission 可安全輸出的 bounded disposition。</summary>
public enum P72GovernedPaymentCycleAdmissionDisposition
{
    /// <summary>證據不完整、狀態矛盾或任何 no-go；該 cycle family 禁止 replay。</summary>
    NoGo = 0,

    /// <summary>fresh bootstrap 已驗證，未來 executor 只能執行零 mutation preflight。</summary>
    PreflightRequired = 1,

    /// <summary>preflight 已 go 或 fixture 已 provision；依 result properties 決定可 provision 或單次 dispatch。</summary>
    ProvisionAllowed = 2,

    /// <summary>一次 mutation 已送出，必須以 exact fixed projection read-back。</summary>
    ReadBackRequired = 3,

    /// <summary>reconciliation 已證明 known effects，唯一下一步是 deterministic cleanup。</summary>
    CleanupRequired = 4,

    /// <summary>cleanup 與 final read-back 已證明完成；cycle 仍永久禁止 replay。</summary>
    Completed = 5
}

/// <summary>表示 admission failure 的固定、去識別化分類。</summary>
public enum P72GovernedPaymentCycleAdmissionFailureCategory
{
    /// <summary>目前 stage 可安全繼續本機 state machine；不代表 CE 或 consumer 已獲授權。</summary>
    None = 0,

    /// <summary>fresh binding、descriptor、ledger、authorization、allowlist 或 projection 基礎不完整。</summary>
    BootstrapInvalid = 1,

    /// <summary>preflight 不是 go 或其 stage evidence 不一致。</summary>
    PreflightNotGo = 2,

    /// <summary>provisioned state 缺 known keys、dispatch count 或 lifecycle evidence 不一致。</summary>
    ProvisionInvalid = 3,

    /// <summary>timeout、ambiguous 或 partial dispatch 使 remote effect 不可證明。</summary>
    UncertainDispatch = 4,

    /// <summary>read-back 缺失、不相符或與 stage evidence 矛盾。</summary>
    ReadBackUnproven = 5,

    /// <summary>reconciliation 缺失、unknown effect 或與 stage evidence 矛盾。</summary>
    ReconciliationUnproven = 6,

    /// <summary>cleanup failure、uncertainty 或 final evidence 不完整。</summary>
    CleanupUnproven = 7,

    /// <summary>未知 enum 值或無法合法到達的 stage transition。</summary>
    StateInconsistent = 8
}

/// <summary>
/// 封裝 immutable admission result。
///
/// <para>
/// 結果不擁有外部資源，沒有 CRM ID、Owner、client、lease、temporary file、Session、cache 或背景工作。
/// `CeDispatchAllowed` 與 `ProductConsumerAllowed` 固定為 false：本型別只證明 future executor 的 local
/// state-machine 入口，絕不自行授權 CE I/O、feature enablement、traffic cutover、P7.5 或 P8。
/// </para>
/// </summary>
public sealed class P72GovernedPaymentCycleAdmissionResult
{
    internal P72GovernedPaymentCycleAdmissionResult(
        P72GovernedPaymentCycleAdmissionDisposition disposition,
        P72GovernedPaymentCycleAdmissionFailureCategory failureCategory,
        bool canRunReadOnlyPreflight = false,
        bool canProvisionFreshFixture = false,
        bool canDispatchExactlyOnce = false,
        bool requiresExactReadBack = false,
        bool requiresReconciliation = false,
        bool requiresCleanup = false)
    {
        Disposition = disposition;
        FailureCategory = failureCategory;
        CanRunReadOnlyPreflight = canRunReadOnlyPreflight;
        CanProvisionFreshFixture = canProvisionFreshFixture;
        CanDispatchExactlyOnce = canDispatchExactlyOnce;
        RequiresExactReadBack = requiresExactReadBack;
        RequiresReconciliation = requiresReconciliation;
        RequiresCleanup = requiresCleanup;
    }

    /// <summary>固定、去識別化的安全 disposition。</summary>
    public P72GovernedPaymentCycleAdmissionDisposition Disposition { get; }

    /// <summary>不含遠端細節的 failure category。</summary>
    public P72GovernedPaymentCycleAdmissionFailureCategory FailureCategory { get; }

    /// <summary>只有 fresh bootstrap 可執行零 mutation preflight。</summary>
    public bool CanRunReadOnlyPreflight { get; }

    /// <summary>只有 preflight=go 可 provision 本 task-owned fresh fixture。</summary>
    public bool CanProvisionFreshFixture { get; }

    /// <summary>只有 exact known-key provision 可以讓 future executor發送恰好一次 allowlisted mutation。</summary>
    public bool CanDispatchExactlyOnce { get; }

    /// <summary>一次已套用的 mutation 或其 verified stage 是否必須先做 exact fixed projection read-back。</summary>
    public bool RequiresExactReadBack { get; }

    /// <summary>exact read-back 後是否仍必須確認所有 ledger-known effects。</summary>
    public bool RequiresReconciliation { get; }

    /// <summary>reconciled known effects 是否必須依 reverse-known-key order cleanup。</summary>
    public bool RequiresCleanup { get; }

    /// <summary>本 local-only contract 永遠不直接授權 CE dispatch。</summary>
    public bool CeDispatchAllowed => false;

    /// <summary>本 local-only contract 永遠不直接授權產品 consumer 或流量切換。</summary>
    public bool ProductConsumerAllowed => false;

    /// <summary>
    /// 是否禁止重新發送 mutation。只有尚未執行任何 mutation 的 bootstrap/preflight/provision 階段可為 false；
    /// 一旦 mutation 可能已送出，或任何 no-go/completed 已發生，future executor 必須停止 replay。
    /// </summary>
    public bool ProhibitsReplay => !CanRunReadOnlyPreflight &&
        !CanProvisionFreshFixture &&
        !CanDispatchExactlyOnce;
}

/// <summary>
/// 建立 P7.2 payment family 的 pure, fail-closed cycle admission result。
///
/// <para>
/// 此 reducer 不對 CRM、Data8、檔案、網路、feature flag、Session、HttpContext、ToolUtility 或 legacy
/// processor 執行任何動作。它只接受 future executor 已完成的 immutable stage evidence，嚴格驗證同一
/// family 的 bootstrap → preflight → provision → one dispatch → read-back → reconcile → cleanup 順序。
/// timeout、ambiguous、partial、mismatch、unknown effect 與 cleanup uncertainty 都不可重新開啟 dispatch。
/// 實際 nonce、descriptor、ledger、lease、client、stream、temporary directory、cancellation registration
/// 與 cleanup 資源必須由 future executor 以明確所有權和 finally/dispose 路徑管理。
/// </para>
/// </summary>
public static class P72GovernedPaymentCycleAdmission
{
    /// <summary>
    /// 解析 payment cycle 的下一個唯一安全本機階段。
    ///
    /// <param name="observation">當前 operation 的去識別化 immutable stage evidence。</param>
    /// <returns>無 I/O、無資源所有權、無 CE 授權的 bounded admission result。</returns>
    public static P72GovernedPaymentCycleAdmissionResult Admit(
        P72GovernedPaymentCycleAdmissionObservation? observation)
    {
        if (observation is null || !HasValidFoundation(observation))
        {
            return NoGo(P72GovernedPaymentCycleAdmissionFailureCategory.BootstrapInvalid);
        }

        return observation.Stage switch
        {
            P72GovernedPaymentCycleStage.Bootstrap => AdmitBootstrap(observation),
            P72GovernedPaymentCycleStage.PreflightCompleted => AdmitPreflightCompleted(observation),
            P72GovernedPaymentCycleStage.Provisioned => AdmitProvisioned(observation),
            P72GovernedPaymentCycleStage.Dispatched => AdmitDispatched(observation),
            P72GovernedPaymentCycleStage.ReadBackVerified => AdmitReadBackVerified(observation),
            P72GovernedPaymentCycleStage.Reconciled => AdmitReconciled(observation),
            P72GovernedPaymentCycleStage.CleanupVerified => AdmitCleanupVerified(observation),
            _ => NoGo(P72GovernedPaymentCycleAdmissionFailureCategory.StateInconsistent)
        };
    }

    /// <summary>
    /// 驗證所有 stage 共用的 fresh-family 前置條件。這些條件沒有任何 caller-selected identity；
    /// 若 server-authorized writer、descriptor、single-writer ledger 或 fixed projection 其中一項未證明，
    /// reducer 在最早時點停止，避免以後續 remote I/O 或 retry 推測安全性。
    /// </summary>
    private static bool HasValidFoundation(P72GovernedPaymentCycleAdmissionObservation observation)
        => observation.IsFreshFamilyBinding &&
           observation.HasNonEmptyNonce &&
           observation.HasCompleteTaskOwnedDescriptor &&
           observation.IsLedgerBoundToFamily &&
           observation.HasServerAuthorizedWriter &&
           observation.HasExactlyOneAllowlistedMutation &&
           observation.HasExpectedExactProjection &&
           observation.DispatchCount >= 0;

    /// <summary>bootstrap 只允許轉往零 mutation preflight，不能已有 provision、dispatch 或 read-back evidence。</summary>
    private static P72GovernedPaymentCycleAdmissionResult AdmitBootstrap(
        P72GovernedPaymentCycleAdmissionObservation observation)
    {
        if (!observation.WasLedgerEmptyBeforeProvision || observation.HasKnownProvisionedKeys ||
            observation.PreflightOutcome != P72GovernedPaymentPreflightOutcome.NotRun ||
            observation.DispatchCount != 0 || observation.OperationExecuted ||
            observation.DispatchOutcome != P72GovernedPaymentDispatchOutcome.NotAttempted ||
            observation.ReadBackOutcome != P72GovernedPaymentReadBackOutcome.NotRun ||
            observation.ReconciliationOutcome != P72GovernedPaymentReconciliationOutcome.NotRun ||
            observation.CleanupOutcome != P72GovernedPaymentCleanupOutcome.NotRun)
        {
            return NoGo(P72GovernedPaymentCycleAdmissionFailureCategory.StateInconsistent);
        }

        return new P72GovernedPaymentCycleAdmissionResult(
            P72GovernedPaymentCycleAdmissionDisposition.PreflightRequired,
            P72GovernedPaymentCycleAdmissionFailureCategory.None,
            canRunReadOnlyPreflight: true);
    }

    /// <summary>preflight 完成後，只有固定 Go 可進入 provision；其餘分類永久停止此 cycle family。</summary>
    private static P72GovernedPaymentCycleAdmissionResult AdmitPreflightCompleted(
        P72GovernedPaymentCycleAdmissionObservation observation)
    {
        if (!observation.WasLedgerEmptyBeforeProvision || observation.HasKnownProvisionedKeys ||
            observation.DispatchCount != 0 || observation.OperationExecuted ||
            observation.DispatchOutcome != P72GovernedPaymentDispatchOutcome.NotAttempted ||
            observation.ReadBackOutcome != P72GovernedPaymentReadBackOutcome.NotRun ||
            observation.ReconciliationOutcome != P72GovernedPaymentReconciliationOutcome.NotRun ||
            observation.CleanupOutcome != P72GovernedPaymentCleanupOutcome.NotRun)
        {
            return NoGo(P72GovernedPaymentCycleAdmissionFailureCategory.StateInconsistent);
        }

        return observation.PreflightOutcome == P72GovernedPaymentPreflightOutcome.Go
            ? new P72GovernedPaymentCycleAdmissionResult(
                P72GovernedPaymentCycleAdmissionDisposition.ProvisionAllowed,
                P72GovernedPaymentCycleAdmissionFailureCategory.None,
                canProvisionFreshFixture: true)
            : NoGo(P72GovernedPaymentCycleAdmissionFailureCategory.PreflightNotGo);
    }

    /// <summary>provisioned state 只允許恰好一次尚未發送的 future mutation admission。</summary>
    private static P72GovernedPaymentCycleAdmissionResult AdmitProvisioned(
        P72GovernedPaymentCycleAdmissionObservation observation)
    {
        if (observation.PreflightOutcome != P72GovernedPaymentPreflightOutcome.Go ||
            !observation.HasKnownProvisionedKeys || observation.DispatchCount != 0 || observation.OperationExecuted ||
            observation.DispatchOutcome != P72GovernedPaymentDispatchOutcome.NotAttempted ||
            observation.ReadBackOutcome != P72GovernedPaymentReadBackOutcome.NotRun ||
            observation.ReconciliationOutcome != P72GovernedPaymentReconciliationOutcome.NotRun ||
            observation.CleanupOutcome != P72GovernedPaymentCleanupOutcome.NotRun)
        {
            return NoGo(P72GovernedPaymentCycleAdmissionFailureCategory.ProvisionInvalid);
        }

        return new P72GovernedPaymentCycleAdmissionResult(
            P72GovernedPaymentCycleAdmissionDisposition.ProvisionAllowed,
            P72GovernedPaymentCycleAdmissionFailureCategory.None,
            canDispatchExactlyOnce: true);
    }

    /// <summary>一次 dispatch 後，Applied 必須 read-back；不確定結果一律 terminal no-go。</summary>
    private static P72GovernedPaymentCycleAdmissionResult AdmitDispatched(
        P72GovernedPaymentCycleAdmissionObservation observation)
    {
        if (!HasSingleAppliedDispatchShape(observation))
        {
            return observation.DispatchCount == 1 && observation.OperationExecuted &&
                   observation.DispatchOutcome is P72GovernedPaymentDispatchOutcome.Timeout or
                       P72GovernedPaymentDispatchOutcome.Ambiguous or
                       P72GovernedPaymentDispatchOutcome.Partial
                ? NoGo(P72GovernedPaymentCycleAdmissionFailureCategory.UncertainDispatch)
                : NoGo(P72GovernedPaymentCycleAdmissionFailureCategory.StateInconsistent);
        }

        return new P72GovernedPaymentCycleAdmissionResult(
            P72GovernedPaymentCycleAdmissionDisposition.ReadBackRequired,
            P72GovernedPaymentCycleAdmissionFailureCategory.None,
            requiresExactReadBack: true);
    }

    /// <summary>exact postimage 成功後仍須 reconciliation；不相符或不可用不得當成完成。</summary>
    private static P72GovernedPaymentCycleAdmissionResult AdmitReadBackVerified(
        P72GovernedPaymentCycleAdmissionObservation observation)
    {
        if (!HasSingleAppliedDispatchShape(observation))
        {
            return NoGo(P72GovernedPaymentCycleAdmissionFailureCategory.StateInconsistent);
        }

        if (observation.ReadBackOutcome != P72GovernedPaymentReadBackOutcome.ExactMatch ||
            observation.ReconciliationOutcome != P72GovernedPaymentReconciliationOutcome.NotRun ||
            observation.CleanupOutcome != P72GovernedPaymentCleanupOutcome.NotRun)
        {
            return NoGo(P72GovernedPaymentCycleAdmissionFailureCategory.ReadBackUnproven);
        }

        return new P72GovernedPaymentCycleAdmissionResult(
            P72GovernedPaymentCycleAdmissionDisposition.ReadBackRequired,
            P72GovernedPaymentCycleAdmissionFailureCategory.None,
            requiresExactReadBack: true,
            requiresReconciliation: true);
    }

    /// <summary>只有 exact reconciliation 可進入 cleanup；unknown effect 必須停在 no-go。</summary>
    private static P72GovernedPaymentCycleAdmissionResult AdmitReconciled(
        P72GovernedPaymentCycleAdmissionObservation observation)
    {
        if (!HasSingleAppliedDispatchShape(observation) ||
            observation.ReadBackOutcome != P72GovernedPaymentReadBackOutcome.ExactMatch)
        {
            return NoGo(P72GovernedPaymentCycleAdmissionFailureCategory.ReadBackUnproven);
        }

        if (observation.ReconciliationOutcome != P72GovernedPaymentReconciliationOutcome.ExactEffectConfirmed ||
            observation.CleanupOutcome != P72GovernedPaymentCleanupOutcome.NotRun)
        {
            return NoGo(P72GovernedPaymentCycleAdmissionFailureCategory.ReconciliationUnproven);
        }

        return new P72GovernedPaymentCycleAdmissionResult(
            P72GovernedPaymentCycleAdmissionDisposition.CleanupRequired,
            P72GovernedPaymentCycleAdmissionFailureCategory.None,
            requiresCleanup: true);
    }

    /// <summary>cleanup complete 是唯一可完成 cycle 的結果；uncertain/failed cleanup 永遠不可釋出。</summary>
    private static P72GovernedPaymentCycleAdmissionResult AdmitCleanupVerified(
        P72GovernedPaymentCycleAdmissionObservation observation)
    {
        if (!HasSingleAppliedDispatchShape(observation) ||
            observation.ReadBackOutcome != P72GovernedPaymentReadBackOutcome.ExactMatch ||
            observation.ReconciliationOutcome != P72GovernedPaymentReconciliationOutcome.ExactEffectConfirmed)
        {
            return NoGo(P72GovernedPaymentCycleAdmissionFailureCategory.StateInconsistent);
        }

        return observation.CleanupOutcome == P72GovernedPaymentCleanupOutcome.Completed
            ? new P72GovernedPaymentCycleAdmissionResult(
                P72GovernedPaymentCycleAdmissionDisposition.Completed,
                P72GovernedPaymentCycleAdmissionFailureCategory.None)
            : NoGo(P72GovernedPaymentCycleAdmissionFailureCategory.CleanupUnproven);
    }

    /// <summary>驗證所有 dispatch 後 stage 共用的「恰好一次且已套用」不變量。</summary>
    private static bool HasSingleAppliedDispatchShape(P72GovernedPaymentCycleAdmissionObservation observation)
        => observation.PreflightOutcome == P72GovernedPaymentPreflightOutcome.Go &&
           observation.HasKnownProvisionedKeys &&
           observation.DispatchCount == 1 &&
           observation.OperationExecuted &&
           observation.DispatchOutcome == P72GovernedPaymentDispatchOutcome.Applied;

    /// <summary>集中建立 terminal no-go，保證所有拒絕分支均不暴露 I/O 或重新發送權限。</summary>
    private static P72GovernedPaymentCycleAdmissionResult NoGo(
        P72GovernedPaymentCycleAdmissionFailureCategory failureCategory)
        => new(
            P72GovernedPaymentCycleAdmissionDisposition.NoGo,
            failureCategory);
}

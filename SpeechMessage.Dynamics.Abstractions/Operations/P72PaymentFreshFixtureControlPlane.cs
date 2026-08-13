// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Operations/P72PaymentFreshFixtureControlPlane.cs
// 用途：定義 P7.2 付款回傳第一個 writer family 的純本機 fresh-fixture descriptor/ledger
//       控制面。它只檢查去識別化的完整性證據，絕不建立 CRM/Data8 client、發送 CE request、
//       讀寫檔案、啟用 feature gate，或保留 Session、HttpContext 與付款資料。
// ============================================================================

namespace SpeechMessage.Dynamics.Abstractions.Operations;

/// <summary>
/// 表示付款專用 fresh fixture 控制面目前唯一允許的 writer family。
///
/// <para>
/// 第一個 family 只描述付款成功後的一次 fee update。fee create、owner assignment、booking
/// completion、contact card profile 與 notification 都是獨立 side effect，必須由未來各自的
/// descriptor、ledger、read-back、reconcile 與 cleanup owner 處理；不能藉由 enum 擴充或
/// generic CRUD 偷渡進這個 family。
/// </para>
/// </summary>
public enum P72PaymentFreshFixtureFamily
{
    /// <summary>只允許在 payment success 已由上游證明後更新本次 task-owned fresh fee。</summary>
    FeeUpdateAfterPayment = 0
}

/// <summary>
/// 表示此 payment fixture descriptor 固定的單一 allowlisted mutation。
///
/// <para>
/// 這是控制面分類，沒有 CRM field map、record ID、owner 或 caller 可指定的資料。真正 executor
/// 仍須從 server-owned secured ledger 讀取 exact known key 與固定 postimage，並在 uncertain
/// transport 時停止且禁止 replay。
/// </para>
/// </summary>
public enum P72PaymentFreshFixtureMutation
{
    /// <summary>付款成功後的單次 fee update；不包含任何 create、assign、booking 或通知。</summary>
    FeeUpdateAfterPayment = 0
}

/// <summary>表示 payment fresh-fixture 控制面可安全輸出的下一步。</summary>
public enum P72PaymentFreshFixtureControlPlaneDisposition
{
    /// <summary>descriptor 或 ledger 證據不足；此 cycle 不得 provision、dispatch 或 replay。</summary>
    NoGo = 0,

    /// <summary>控制面完整，但唯一允許下一步仍是零 mutation 的 fixed read-only preflight。</summary>
    ReadOnlyPreflightRequired = 1
}

/// <summary>表示控制面拒絕結果的固定、去識別化類別。</summary>
public enum P72PaymentFreshFixtureControlPlaneFailureCategory
{
    /// <summary>本機完整性檢查通過；不代表 CE、consumer、traffic 或 deployment 已獲授權。</summary>
    None = 0,

    /// <summary>family 不是此 child 明確定義的單一付款 fee-update family。</summary>
    FamilyUnsupported = 1,

    /// <summary>descriptor schema version 缺失或與目前 immutable contract 不相符。</summary>
    SchemaInvalid = 2,

    /// <summary>fresh nonce 或 immutable descriptor digest 未獲證明。</summary>
    DescriptorBindingUnproven = 3,

    /// <summary>single-writer ledger 不是空白 fresh ledger，或未綁定 secure exact known keys。</summary>
    LedgerBindingUnproven = 4,

    /// <summary>server-derived distinct owner/writer binding 未獲證明。</summary>
    WriterAuthorizationUnproven = 5,

    /// <summary>fee-update-only allowlist、fixed read-back projection 或 cleanup plan 未獲證明。</summary>
    OperationContractUnproven = 6
}

/// <summary>
/// 封裝未來 executor 已完成、但不暴露敏感內容的 payment fixture 控制面證據。
///
/// <para>
/// 真實 nonce、fixture marker、CRM IDs、owner/profile identity、descriptor bytes、preimage/postimage
/// 與 ledger path 只可存在於未來 executor 的受保護單一 owner scope。本 immutable record 僅保留
/// 有界布林證據與 schema/family 分類，因此 A/B request、profile 或 cycle 不會共享 mutable state、
/// 也無 Session、cache、client、lease、stream、process 或 cancellation registration 可洩漏。
/// </para>
/// </summary>
public sealed record P72PaymentFreshFixtureControlPlaneInput
{
    /// <summary>descriptor 宣告的 payment writer family，未定義值一律 fail closed。</summary>
    public required P72PaymentFreshFixtureFamily Family { get; init; }

    /// <summary>descriptor/ledger 共用的固定 schema version，不接受 caller 自選版本。</summary>
    public required string SchemaVersion { get; init; }

    /// <summary>future owner 已證明本 cycle nonce 非空、全新且未與歷史 Slice C 或其他 cycle 重用。</summary>
    public required bool HasFreshNonce { get; init; }

    /// <summary>future owner 已驗證 immutable descriptor digest 與本 cycle ledger binding 相符。</summary>
    public required bool HasImmutableDescriptorDigest { get; init; }

    /// <summary>provision 前的 single-writer ledger 已證明為空，避免 stale effect 被錯認為 fresh fixture。</summary>
    public required bool HasEmptySingleWriterLedger { get; init; }

    /// <summary>ledger 僅能保存其受保護 scope 產生的 exact known keys，不能接收 caller supplied key。</summary>
    public required bool HasSecureExactKeyLedger { get; init; }

    /// <summary>server-derived active and distinct writer/owner binding 已獲證明；本型別不持有其值。</summary>
    public required bool HasServerDerivedDistinctOwnerBinding { get; init; }

    /// <summary>allowlist 僅有 fee update，沒有 generic field map、create、assign 或其他 side effect。</summary>
    public required bool HasFeeUpdateOnlyAllowlist { get; init; }

    /// <summary>descriptor 已定義 exact fixed typed preimage/postimage projection，供未來 read-back 使用。</summary>
    public required bool HasFixedExactReadBackProjection { get; init; }

    /// <summary>descriptor 已定義 reverse-known-key cleanup/rollback 順序，且不會掃描或刪除未知資料。</summary>
    public required bool HasReverseKnownKeyCleanupPlan { get; init; }
}

/// <summary>
/// 封裝純本機控制面 assessment。
///
/// <para>
/// 無論結果為何，<see cref="CeDispatchAllowed"/> 和 <see cref="ProductConsumerAllowed"/> 永遠為
/// false。它們是防止 contract 直接被當成 runtime write authorization 或 rollout gate 的硬邊界；
/// future executor 仍必須依序完成 read-only preflight、fresh provision、single dispatch、exact
/// read-back、reconcile 及 deterministic cleanup，並在所有外部資源的 finally/dispose 路徑釋放。
/// </para>
/// </summary>
public sealed class P72PaymentFreshFixtureControlPlaneAssessment
{
    internal P72PaymentFreshFixtureControlPlaneAssessment(
        P72PaymentFreshFixtureControlPlaneDisposition disposition,
        P72PaymentFreshFixtureControlPlaneFailureCategory failureCategory,
        bool canRunReadOnlyPreflight,
        P72PaymentFreshFixtureMutation allowedMutation)
    {
        Disposition = disposition;
        FailureCategory = failureCategory;
        CanRunReadOnlyPreflight = canRunReadOnlyPreflight;
        AllowedMutation = allowedMutation;
    }

    /// <summary>固定且不含遠端資料的下一步分類。</summary>
    public P72PaymentFreshFixtureControlPlaneDisposition Disposition { get; }

    /// <summary>固定且去識別化的拒絕分類。</summary>
    public P72PaymentFreshFixtureControlPlaneFailureCategory FailureCategory { get; }

    /// <summary>僅完整 bootstrap 可執行零 mutation fixed read-only preflight。</summary>
    public bool CanRunReadOnlyPreflight { get; }

    /// <summary>控制面不直接允許 fixture provision；preflight=go 仍須由 cycle admission 再次確認。</summary>
    public bool CanProvisionFreshFixture => false;

    /// <summary>控制面不直接允許 dispatch；只輸出未來 executor 必須驗證的單一 mutation 類別。</summary>
    public bool CanDispatchExactlyOnce => false;

    /// <summary>唯一固定的 operation ID，不由 caller input 或 descriptor 文字決定。</summary>
    public string OperationId => OperationIds.PaymentsFeeUpdateAfterPayment;

    /// <summary>唯一固定的 mutation 分類，防止 generic CRUD 或相鄰 side effect 混入。</summary>
    public P72PaymentFreshFixtureMutation AllowedMutation { get; }

    /// <summary>此純本機控制面永遠不直接授權 CE dispatch。</summary>
    public bool CeDispatchAllowed => false;

    /// <summary>此純本機控制面永遠不直接授權 ProductClient consumer、feature flag 或 traffic。</summary>
    public bool ProductConsumerAllowed => false;

    /// <summary>拒絕的 descriptor 不可重播；尚未 dispatch 的完整 bootstrap 可進行其唯一 preflight。</summary>
    public bool ProhibitsReplay => !CanRunReadOnlyPreflight;
}

/// <summary>
/// 評估 P7.2 付款 first writer family 的 fresh-fixture descriptor/ledger bootstrap。
///
/// <para>
/// 此類別是 pure local reducer：沒有 Data8、CRM SDK、network、file I/O、feature gate、Session、
/// HttpContext、ToolUtility 或 legacy payment processor 依賴。它不產生 nonce、ID、owner、baseline
/// 或 ledger；只在全部 server-owned completeness evidence 已存在時開放一個零 mutation preflight。
/// 任何缺口均 fail closed，禁止 provision、dispatch、retry 或以掃描 CRM/猜選 Owner 補救。
/// </para>
/// </summary>
public static class P72PaymentFreshFixtureControlPlane
{
    /// <summary>payment fresh-fixture descriptor/ledger 的固定 schema version。</summary>
    public const string CurrentSchemaVersion = "p72-payment-fixture-v1";

    /// <summary>
    /// 以 immutable 去識別化 evidence 評估唯一安全的下一步。
    ///
    /// <param name="input">future executor 已完成的 payment descriptor/ledger completeness evidence。</param>
    /// <returns>只有 no-go 或 read-only-preflight 的 bounded local assessment。</returns>
    public static P72PaymentFreshFixtureControlPlaneAssessment Evaluate(
        P72PaymentFreshFixtureControlPlaneInput? input)
    {
        if (input is null)
        {
            return NoGo(P72PaymentFreshFixtureControlPlaneFailureCategory.DescriptorBindingUnproven);
        }

        if (input.Family != P72PaymentFreshFixtureFamily.FeeUpdateAfterPayment)
        {
            return NoGo(P72PaymentFreshFixtureControlPlaneFailureCategory.FamilyUnsupported);
        }

        if (!string.Equals(input.SchemaVersion, CurrentSchemaVersion, StringComparison.Ordinal))
        {
            return NoGo(P72PaymentFreshFixtureControlPlaneFailureCategory.SchemaInvalid);
        }

        if (!input.HasFreshNonce || !input.HasImmutableDescriptorDigest)
        {
            return NoGo(P72PaymentFreshFixtureControlPlaneFailureCategory.DescriptorBindingUnproven);
        }

        if (!input.HasEmptySingleWriterLedger || !input.HasSecureExactKeyLedger)
        {
            return NoGo(P72PaymentFreshFixtureControlPlaneFailureCategory.LedgerBindingUnproven);
        }

        if (!input.HasServerDerivedDistinctOwnerBinding)
        {
            return NoGo(P72PaymentFreshFixtureControlPlaneFailureCategory.WriterAuthorizationUnproven);
        }

        if (!input.HasFeeUpdateOnlyAllowlist || !input.HasFixedExactReadBackProjection ||
            !input.HasReverseKnownKeyCleanupPlan)
        {
            return NoGo(P72PaymentFreshFixtureControlPlaneFailureCategory.OperationContractUnproven);
        }

        return new P72PaymentFreshFixtureControlPlaneAssessment(
            P72PaymentFreshFixtureControlPlaneDisposition.ReadOnlyPreflightRequired,
            P72PaymentFreshFixtureControlPlaneFailureCategory.None,
            canRunReadOnlyPreflight: true,
            P72PaymentFreshFixtureMutation.FeeUpdateAfterPayment);
    }

    /// <summary>
    /// 建立固定 no-go assessment。它不暴露拒絕的敏感細節，也不保留任何輸入參考；future executor
    /// 必須將實際 descriptor/ledger 的有序 cleanup 與資源釋放留在自己的 bounded owner scope。
    /// </summary>
    private static P72PaymentFreshFixtureControlPlaneAssessment NoGo(
        P72PaymentFreshFixtureControlPlaneFailureCategory failureCategory)
        => new(
            P72PaymentFreshFixtureControlPlaneDisposition.NoGo,
            failureCategory,
            canRunReadOnlyPreflight: false,
            P72PaymentFreshFixtureMutation.FeeUpdateAfterPayment);
}

// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Operations/P72DonationPaymentLocalPlanBuilder.cs
// 用途：只將 Slice D「完整、首次、仍待付款的成功」決策轉為固定的 local-only 費用更新計畫；
//       其他付款結果一律沒有計畫，避免 callback、timeout 或舊資料造成寫入重播。
// ============================================================================

namespace SpeechMessage.Dynamics.Abstractions.Operations;

/// <summary>
/// 封裝付款決策與可選 local-only 操作計畫的結果。
///
/// <para>
/// 這個結果不含 CRM ID、訂單號、付款資料、Owner、profile、endpoint、credential、token 或原始例外。
/// 它不配置外部資源，也不會安排背景工作；<see cref="Plan"/> 只有在付款決策能準備未來受治理操作時
/// 才存在，且其 CE dispatch 與產品 consumer 旗標仍由 catalog 固定為 false。
/// </para>
/// </summary>
public sealed class P72DonationPaymentLocalPlanBuildResult
{
    internal P72DonationPaymentLocalPlanBuildResult(
        P72DonationPaymentDecisionResult decision,
        P72ContinuationLocalPlan? plan,
        P72LocalPlanFailureCategory failureCategory)
    {
        Decision = decision ?? throw new ArgumentNullException(nameof(decision));
        Plan = plan;
        FailureCategory = failureCategory;
    }

    /// <summary>
    /// 付款本機決策。它會保留 no-go、already-processed 或 reconciliation 的固定分類，供未來治理層
    /// 記錄去識別化結果；它本身不授權或執行 CRM 動作。
    /// </summary>
    public P72DonationPaymentDecisionResult Decision { get; }

    /// <summary>
    /// 只在 fresh success 的固定 allowlist 條件下存在的 immutable local-only plan；其他情況必為 null，
    /// 防止呼叫端持有或誤送一份 partial plan。
    /// </summary>
    public P72ContinuationLocalPlan? Plan { get; }

    /// <summary>
    /// plan 輸入驗證的固定失敗分類。付款 no-go 並不被偽裝成 input error，而由 <see cref="Decision"/>
    /// 說明，以保留 timeout／replay 生命週期語意。
    /// </summary>
    public P72LocalPlanFailureCategory FailureCategory { get; }

    /// <summary>
    /// 只有付款決策可準備後續治理操作、共用 plan builder 成功，且取得完整 local-only plan 時為 true。
    /// true 仍不表示 CE evidence、CRM write、產品切流或 ToolUtility migration 已完成。
    /// </summary>
    public bool Succeeded =>
        Decision.CanPrepareFutureDispatch &&
        Plan is not null &&
        FailureCategory == P72LocalPlanFailureCategory.None;
}

/// <summary>
/// 建立 Slice D 付款後費用更新的 local-only 計畫。
///
/// <para>
/// 本 builder 不接受任意 operation、Owner、entity、profile、connector、endpoint、credential、token、
/// 金額、訂單或 CRM ID。它僅能使用 catalog 固定的
/// <see cref="OperationIds.PaymentsFeeUpdateAfterPayment"/> 與去識別化 fixture key，並委派給共用的
/// <see cref="P72ContinuationLocalOnlyPlanBuilder"/> 做 immutable snapshot 與 allowlist 驗證。
/// 沒有 I/O、Session、cache、static mutable state、lease、client、timer、process、stream、subscription 或
/// background task；呼叫結束後沒有資源需要釋放，也沒有跨 request 可重用的資料。
/// </para>
/// </summary>
public static class P72DonationPaymentLocalPlanBuilder
{
    /// <summary>
    /// 將付款後費用更新的最小觀察轉為 local-only plan。
    ///
    /// <para>
    /// 若付款不是完整、首次、仍待付款的成功，結果只回傳原決策，絕不建立 partial plan 或嘗試重播。
    /// 若是 fresh success，才建立單一 <c>fixtureKey</c> 的 immutable plan；未來 CE cycle 仍必須使用
    /// 新 nonce、ledger、preflight、一次 dispatch、exact read-back、reconciliation 與 cleanup，並在
    /// timeout、ambiguous、no-go、mismatch 或 cleanup uncertainty 時停止而不重試。
    /// </para>
    /// </summary>
    /// <param name="observation">付款邊界及受治理 read-back 提供的最小、去識別化觀察。</param>
    /// <param name="fixtureKey">本次 task-owned fresh fixture 的 opaque key，不可為 CRM ID 或 routing authority。</param>
    /// <returns>付款決策與可選的不可執行 local-only plan。</returns>
    public static P72DonationPaymentLocalPlanBuildResult BuildFeeUpdateAfterPayment(
        P72DonationPaymentLocalObservation? observation,
        string? fixtureKey)
    {
        var decision = P72DonationPaymentLocalDecision.Resolve(observation);
        if (!decision.CanPrepareFutureDispatch)
        {
            return new P72DonationPaymentLocalPlanBuildResult(
                decision,
                null,
                P72LocalPlanFailureCategory.None);
        }

        var generic = P72ContinuationLocalOnlyPlanBuilder.Build(new P72ContinuationLocalPlanRequest
        {
            OperationId = OperationIds.PaymentsFeeUpdateAfterPayment,
            Inputs = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["fixtureKey"] = fixtureKey,
                ["transition"] = "payment-succeeded"
            }
        });

        return new P72DonationPaymentLocalPlanBuildResult(
            decision,
            generic.Plan,
            generic.FailureCategory);
    }
}

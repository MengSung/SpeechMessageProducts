// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Operations/P72AppointmentLocalPlanBuilder.cs
// 用途：只將通過 Slice E appointment cardinality 決策的 create/update 意圖轉為固定 local-only plan。
//       它不接受 caller authority，也絕不執行 CRM、產品切流或 ToolUtility migration。
// ============================================================================

namespace SpeechMessage.Dynamics.Abstractions.Operations;

/// <summary>
/// 封裝 appointment 決策與可選 local-only plan 的 immutable 結果。
/// </summary>
public sealed class P72AppointmentLocalPlanBuildResult
{
    internal P72AppointmentLocalPlanBuildResult(
        P72AppointmentDecisionResult decision,
        P72ContinuationLocalPlan? plan,
        P72LocalPlanFailureCategory failureCategory)
    {
        Decision = decision ?? throw new ArgumentNullException(nameof(decision));
        Plan = plan;
        FailureCategory = failureCategory;
    }

    /// <summary>
    /// appointment cardinality／replay 決策。它不包含 CRM identity 或 operation authority。
    /// </summary>
    public P72AppointmentDecisionResult Decision { get; }

    /// <summary>
    /// 僅在 valid create/update 決策與完整 allowlist input 下存在的 local-only plan；其他情況必為 null。
    /// </summary>
    public P72ContinuationLocalPlan? Plan { get; }

    /// <summary>
    /// 只表示 opaque input validation 的固定錯誤分類，不會將 timeout／duplicate 等決策問題誤寫成輸入錯誤。
    /// </summary>
    public P72LocalPlanFailureCategory FailureCategory { get; }

    /// <summary>
    /// true 表示有不可執行 local-only plan；它不是 CE evidence、實機寫入、P7.4 cutover 或 P7.5 completion。
    /// </summary>
    public bool Succeeded =>
        Decision.CanPrepareFutureDispatch &&
        Plan is not null &&
        FailureCategory == P72LocalPlanFailureCategory.None;
}

/// <summary>
/// 建立 Slice E appointment create/update 的 local-only plan。
///
/// <para>
/// 本 builder 的唯一 operation 是 catalog 的 <see cref="OperationIds.AppointmentsEntityCreateOrUpdate"/>；
/// 它只接受字串 create/update、task-owned opaque fixture key，以及受限的 optional input dictionary。
/// optional dictionary 即使含一個 Owner、profile、entity、endpoint、credential、token 或其他額外 key，
/// 都會 fail closed；這防止產品或呼叫端藉由本機 helper 選擇 CRM authority。它不建立 connector、lease、
/// client、Session、cache、timer、process、stream、subscription 或 background task，沒有跨使用者狀態可保留。
/// </para>
/// </summary>
public static class P72AppointmentLocalPlanBuilder
{
    /// <summary>
    /// 依固定 mode、完整 read-back observation 與 opaque fixture key 建立 appointment local-only plan。
    ///
    /// <para>
    /// valid create/update 會被翻譯成 catalog 的 <c>changeMode</c>，並由共用 builder 防禦性複製輸入；
    /// no-go、already-applied、partial-completion 與 invalid observation 一律沒有 plan。未來 CE executor
    /// 仍必須建立自己的 ledger、一次 dispatch、exact read-back、reconcile、reverse cleanup 與 timeout
    /// no-replay，不得將此純本機結果視為實機成功。
    /// </para>
    /// </summary>
    /// <param name="modeText">只接受 ordinal 小寫 <c>create</c> 或 <c>update</c>。</param>
    /// <param name="observation">受治理 read-back 提供的最小 cardinality observation。</param>
    /// <param name="fixtureKey">本次 task-owned fresh fixture 的 opaque key。</param>
    /// <param name="additionalInputs">只允許 null 或空字典；用於防禦 caller 插入 authority 的回歸測試。</param>
    /// <returns>決策與可選 fail-closed local-only plan。</returns>
    public static P72AppointmentLocalPlanBuildResult Build(
        string? modeText,
        P72AppointmentLocalObservation? observation,
        string? fixtureKey,
        IReadOnlyDictionary<string, string?>? additionalInputs = null)
    {
        if (!TryParseMode(modeText, out var mode))
        {
            return new P72AppointmentLocalPlanBuildResult(
                P72AppointmentLocalDecision.Resolve((P72AppointmentChangeMode)(-1), observation),
                null,
                P72LocalPlanFailureCategory.InputValueInvalid);
        }

        var decision = P72AppointmentLocalDecision.Resolve(mode, observation);
        if (!decision.CanPrepareFutureDispatch)
        {
            return new P72AppointmentLocalPlanBuildResult(
                decision,
                null,
                P72LocalPlanFailureCategory.None);
        }

        var inputs = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["fixtureKey"] = fixtureKey,
            ["changeMode"] = modeText
        };
        if (additionalInputs is not null)
        {
            foreach (var pair in additionalInputs)
            {
                if (!inputs.TryAdd(pair.Key, pair.Value))
                {
                    return new P72AppointmentLocalPlanBuildResult(
                        decision,
                        null,
                        P72LocalPlanFailureCategory.InputNamesMismatch);
                }
            }
        }

        var generic = P72ContinuationLocalOnlyPlanBuilder.Build(new P72ContinuationLocalPlanRequest
        {
            OperationId = OperationIds.AppointmentsEntityCreateOrUpdate,
            Inputs = inputs
        });
        return new P72AppointmentLocalPlanBuildResult(decision, generic.Plan, generic.FailureCategory);
    }

    /// <summary>
    /// 將公開文字邊界轉為固定 enum。比較採 ordinal，拒絕大小寫、空白與未來任意 mode，避免模式字串
    /// 被擴張成隱藏的 operation／routing authority。
    /// </summary>
    private static bool TryParseMode(string? modeText, out P72AppointmentChangeMode mode)
    {
        if (string.Equals(modeText, "create", StringComparison.Ordinal))
        {
            mode = P72AppointmentChangeMode.Create;
            return true;
        }

        if (string.Equals(modeText, "update", StringComparison.Ordinal))
        {
            mode = P72AppointmentChangeMode.Update;
            return true;
        }

        mode = default;
        return false;
    }
}

namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 依多個 policy 的決策結果套用 RichMenu。
/// Orchestrator 只負責挑選最高優先權決策並呼叫 assignment workflow，不直接知道產品角色、資料庫或畫面流程。
/// </summary>
/// <remarks>
/// 保母級說明：
/// 這個類別是「產品規則」與「LINE RichMenu 指派」之間的協調者。
/// 未來不同產品可以各自提供多個 <see cref="IRichMenuPolicy"/>，例如依角色、租戶、
/// 會員狀態、案件階段、文字觸發或臨時活動狀態決定要切換到哪一個 RichMenu。
///
/// Orchestrator 本身不判斷任何產品角色或業務身分。
/// 它只收集所有 policy 的決策，選出優先權最高者，再交給
/// <see cref="ILineRichMenuAssignmentWorkflow"/> 做真正的 LINE 綁定或解除。
///
/// 這種切法可以避免共用專案被產品業務規則污染，也讓每個 policy 維持單一責任。
/// </remarks>
public sealed class RichMenuOrchestrator : IRichMenuOrchestrator
{
    // 所有產品註冊進來的 RichMenu 決策規則。
    // IEnumerable 讓產品可以透過 DI 加入多個 policy，Orchestrator 不需要知道實際數量。
    private readonly IEnumerable<IRichMenuPolicy> _policies;

    // 真正負責 Assign / Unassign 的工作流。
    // Orchestrator 只決定「該做什麼」，assignment workflow 負責「怎麼呼叫 LINE」。
    private readonly ILineRichMenuAssignmentWorkflow _assignmentWorkflow;

    /// <summary>
    /// 建立 RichMenu 協調器，並接收產品端註冊的所有決策 policy。
    /// </summary>
    /// <param name="policies">
    /// 由 DI 註冊進來的決策規則集合；若兩個 policy 回傳相同優先權，會保留先註冊者。
    /// </param>
    /// <param name="assignmentWorkflow">真正負責呼叫 LINE link / unlink 的共用指派工作流。</param>
    public RichMenuOrchestrator(
        IEnumerable<IRichMenuPolicy> policies,
        ILineRichMenuAssignmentWorkflow assignmentWorkflow)
    {
        _policies = policies ?? throw new ArgumentNullException(nameof(policies));
        _assignmentWorkflow = assignmentWorkflow ?? throw new ArgumentNullException(nameof(assignmentWorkflow));
    }

    /// <summary>
    /// 評估所有 RichMenu policy，選出最高優先權決策並套用到指定 LINE 使用者。
    /// </summary>
    /// <param name="context">包含 LINE user id、角色、收到文字、目前選單與產品屬性的決策上下文。</param>
    /// <param name="cancellationToken">傳遞給 policy 與 assignment workflow 的取消權杖。</param>
    /// <returns>標準化的指派結果，描述是否成功、是否變更，以及最後套用的 menu key。</returns>
    public async Task<LineRichMenuAssignmentResult> ApplyAsync(RichMenuContext context, CancellationToken cancellationToken = default)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        // 預設為 None，表示所有 policy 都沒有提出任何 RichMenu 變更。
        // 這讓沒有命中規則時可以安全回傳 NoChange，而不是誤綁選單。
        var best = RichMenuDecision.None;
        foreach (var policy in _policies)
        {
            // 逐一詢問 policy：「針對目前使用者上下文，你建議要套用哪個選單？」
            // policy 可以根據產品自己的角色、狀態、文字觸發、會員等級等資訊做判斷。
            var decision = await policy.DecideAsync(context, cancellationToken).ConfigureAwait(false);

            // 只保留最高優先權決策。
            // 若兩個 policy 同優先權，先註冊者保留，避免後註冊者不明確覆蓋。
            if (decision.Priority > best.Priority)
            {
                best = decision;
            }
        }

        if (best.Priority == RichMenuDecisionPriority.None)
        {
            // 沒有任何 policy 想改 RichMenu，回報 NoChange。
            // 呼叫端可以據此知道不需要通知使用者，也不需要寫狀態。
            return LineRichMenuAssignmentResult.NoChange(context.CurrentMenuKey);
        }

        if (best.Unlink)
        {
            // policy 明確要求解除個人 RichMenu 綁定。
            // 解除後 LINE 會回到 channel default RichMenu，或沒有選單。
            return await _assignmentWorkflow.UnassignAsync(context.LineUserId, cancellationToken).ConfigureAwait(false);
        }

        // 一般情境：policy 選出 menuKey，交給 assignment workflow 解析成 richMenuId 並綁定到使用者。
        return await _assignmentWorkflow.AssignAsync(context.LineUserId, best.MenuKey!, cancellationToken).ConfigureAwait(false);
    }
}

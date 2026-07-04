namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 依多個 policy 的決策結果套用 RichMenu。
/// Orchestrator 只負責挑選最高優先權決策並呼叫 assignment workflow，不直接知道產品角色、資料庫或畫面流程。
/// </summary>
public sealed class RichMenuOrchestrator : IRichMenuOrchestrator
{
    private readonly IEnumerable<IRichMenuPolicy> _policies;
    private readonly ILineRichMenuAssignmentWorkflow _assignmentWorkflow;

    public RichMenuOrchestrator(
        IEnumerable<IRichMenuPolicy> policies,
        ILineRichMenuAssignmentWorkflow assignmentWorkflow)
    {
        _policies = policies ?? throw new ArgumentNullException(nameof(policies));
        _assignmentWorkflow = assignmentWorkflow ?? throw new ArgumentNullException(nameof(assignmentWorkflow));
    }

    public async Task<LineRichMenuAssignmentResult> ApplyAsync(RichMenuContext context, CancellationToken cancellationToken = default)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var best = RichMenuDecision.None;
        foreach (var policy in _policies)
        {
            var decision = await policy.DecideAsync(context, cancellationToken).ConfigureAwait(false);
            if (decision.Priority > best.Priority)
            {
                best = decision;
            }
        }

        if (best.Priority == RichMenuDecisionPriority.None)
        {
            return LineRichMenuAssignmentResult.NoChange(context.CurrentMenuKey);
        }

        if (best.Unlink)
        {
            return await _assignmentWorkflow.UnassignAsync(context.LineUserId, cancellationToken).ConfigureAwait(false);
        }

        return await _assignmentWorkflow.AssignAsync(context.LineUserId, best.MenuKey!, cancellationToken).ConfigureAwait(false);
    }
}

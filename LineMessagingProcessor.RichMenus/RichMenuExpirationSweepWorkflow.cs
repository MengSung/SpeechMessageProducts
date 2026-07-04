namespace LineMessagingProcessor.RichMenus;

public sealed class RichMenuExpirationSweepWorkflow : IRichMenuExpirationSweepWorkflow
{
    private readonly IRichMenuStateStore _stateStore;
    private readonly ILineRichMenuAssignmentWorkflow _assignmentWorkflow;

    public RichMenuExpirationSweepWorkflow(
        IRichMenuStateStore stateStore,
        ILineRichMenuAssignmentWorkflow assignmentWorkflow)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _assignmentWorkflow = assignmentWorkflow ?? throw new ArgumentNullException(nameof(assignmentWorkflow));
    }

    public async Task<RichMenuExpirationSweepReport> SweepAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var expired = await _stateStore.GetExpiredAsync(now, cancellationToken).ConfigureAwait(false);
        var restored = 0;

        foreach (var state in expired)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(state.PreviousMenuKey))
            {
                await _assignmentWorkflow.UnassignAsync(state.LineUserId, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _assignmentWorkflow.AssignAsync(state.LineUserId, state.PreviousMenuKey, cancellationToken).ConfigureAwait(false);
            }

            restored++;
        }

        return new RichMenuExpirationSweepReport(expired.Count, restored);
    }
}

namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 掃描到期的 RichMenu 使用者狀態，並把使用者恢復到前一個選單或解除綁定。
/// </summary>
/// <remarks>
/// 保母級說明：
/// 有些 RichMenu 可能是暫時性的，例如活動期間、繳費提醒期間、維修流程期間、
/// 或使用者輸入某個文字後短時間切到特定功能選單。
///
/// <see cref="IRichMenuStateStore"/> 會保存使用者目前選單、上一個選單與到期時間。
/// 當背景排程呼叫 <see cref="SweepAsync"/> 時，本 workflow 會：
/// 1. 找出已經過期的使用者狀態。
/// 2. 如果該狀態有 PreviousMenuKey，就把使用者指派回前一個選單。
/// 3. 如果沒有 PreviousMenuKey，就解除使用者 RichMenu 綁定，讓 LINE 回到預設選單。
///
/// 這個類別不直接操作 LINE API，而是委派給 <see cref="ILineRichMenuAssignmentWorkflow"/>。
/// 這樣到期還原與一般指派會走同一套錯誤處理、cache 與狀態一致性邏輯。
/// </remarks>
public sealed class RichMenuExpirationSweepWorkflow : IRichMenuExpirationSweepWorkflow
{
    // 狀態儲存抽象。未來產品可替換成資料庫、Redis 或其他持久化實作。
    private readonly IRichMenuStateStore _stateStore;

    // 負責實際 Assign / Unassign 的共用工作流。
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
        // 取得所有到期狀態。
        // 注意：這裡不假設狀態存在於記憶體，真正來源由 IRichMenuStateStore 實作決定。
        var expired = await _stateStore.GetExpiredAsync(now, cancellationToken).ConfigureAwait(false);
        var restored = 0;

        foreach (var state in expired)
        {
            // 背景排程可能被主機停止或取消，逐筆處理前都尊重 cancellation token。
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(state.PreviousMenuKey))
            {
                // 沒有上一個選單可回復時，解除使用者個人 RichMenu。
                // LINE 會顯示 channel default RichMenu，或在沒有 default 時不顯示選單。
                await _assignmentWorkflow.UnassignAsync(state.LineUserId, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // 有上一個選單時，回復到該選單。
                // 例如使用者原本是 member-main，短暫切到 payment-reminder，到期後回 member-main。
                await _assignmentWorkflow.AssignAsync(state.LineUserId, state.PreviousMenuKey, cancellationToken).ConfigureAwait(false);
            }

            // 目前 report 只統計嘗試還原成功走完的筆數。
            // 若未來需要逐筆錯誤報告，可以在這裡擴充 report item，而不改 assignment workflow。
            restored++;
        }

        // TotalExpired：本次掃到的到期筆數。
        // Restored：本次已完成還原或解除綁定的筆數。
        return new RichMenuExpirationSweepReport(expired.Count, restored);
    }
}

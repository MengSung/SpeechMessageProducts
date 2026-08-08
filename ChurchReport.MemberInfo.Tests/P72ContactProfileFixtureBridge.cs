// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/P72ContactProfileFixtureBridge.cs
// 用途：P7.2 Slice B1 LINE profile 的 task-owned fixture bridge。
//       僅允許三個欄位、單次寫入、read-back reconciliation 與 baseline restore。
// ============================================================================

using SpeechMessage.Dynamics.ProductClient.MemberInfo;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>封閉的 LINE profile fixture snapshot；不保存 endpoint、credential 或 session。</summary>
internal sealed record P72ContactLineProfileSnapshot(
    string? PictureUrl,
    string? StatusMessage,
    string? DisplayName);

/// <summary>
/// 擁有單一 contact fixture 的同步讀取與還原責任。實作必須在 Dispose 時釋放其
/// 唯一持有的 CRM service，且不得提供 generic CRUD 或任意 query 入口。
/// </summary>
internal interface IP72ContactLineProfileFixtureStore : IDisposable
{
    /// <summary>只讀取 B1 三個 allowlisted LINE 欄位。</summary>
    P72ContactLineProfileSnapshot Read(Guid contactId);

    /// <summary>只還原 B1 三個欄位的 baseline。</summary>
    void Restore(Guid contactId, P72ContactLineProfileSnapshot baseline);
}

/// <summary>B1 bridge 的去識別化結果狀態。</summary>
internal sealed record P72ContactLineProfileBridgeResult(
    string Outcome,
    string Reason,
    bool OperationExecuted,
    string SentinelState,
    string CleanupState);

/// <summary>
/// 執行一次 B1 sentinel update。write 失敗後不自動重試，僅以 read-back 判斷
/// 是否已提交；只有狀態明確為 baseline 或 sentinel 時才執行 cleanup。
/// </summary>
internal static class P72ContactProfileFixtureBridge
{
    /// <summary>執行 bounded B1 flow，所有 mutable fixture state 都由呼叫端 scope 擁有。</summary>
    public static async Task<P72ContactLineProfileBridgeResult> ExecuteAsync(
        IPackage02ContactProfileClient client,
        IP72ContactLineProfileFixtureStore store,
        Guid contactId,
        string idempotencyKey,
        P72ContactLineProfileSnapshot sentinel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(sentinel);
        if (contactId == Guid.Empty)
        {
            throw new ArgumentException("ContactId is required.", nameof(contactId));
        }

        var baseline = store.Read(contactId);
        var operationExecuted = false;
        var sentinelState = "baseline";
        var cleanupState = "not-required";
        var reason = string.Empty;
        var writeOutcomeIsAmbiguous = false;

        try
        {
            operationExecuted = true;
            var result = await client.UpdateLineProfileAsync(
                new ContactLineProfileUpdateRequest
                {
                    ProfileAlias = "sunnyvalechback",
                    WorkloadSubjectId = "p7.2-contact-line-profile",
                    ContactId = contactId,
                    PictureMode = ContactLineProfileNullableTextMode.Set,
                    PictureUrl = sentinel.PictureUrl,
                    StatusMode = ContactLineProfileNullableTextMode.Set,
                    StatusMessage = sentinel.StatusMessage,
                    DisplayNameMode = ContactLineProfileDisplayNameMode.Set,
                    DisplayName = sentinel.DisplayName,
                    IdempotencyKey = idempotencyKey
                },
                cancellationToken).ConfigureAwait(false);

            if (result.Disposition != ContactLineProfileUpdateDisposition.Changed ||
                result.CorrelationCategory != ContactLineProfileUpdateCorrelationCategory.ReadBackConfirmed)
            {
                reason = "write-response-state-mismatch";
            }
            else
            {
                var afterWrite = store.Read(contactId);
                if (!Matches(afterWrite, sentinel))
                {
                    reason = "reconciliation-failed";
                    sentinelState = "unknown";
                }
                else
                {
                    sentinelState = "confirmed";
                }
            }
        }
        catch (Exception)
        {
            reason = "write-ambiguous";
            writeOutcomeIsAmbiguous = true;
            try
            {
                var afterFault = store.Read(contactId);
                if (Matches(afterFault, sentinel))
                {
                    sentinelState = "confirmed-after-fault";
                    reason = "write-ambiguous-reconciled";
                }
                else if (Matches(afterFault, baseline))
                {
                    sentinelState = "baseline";
                }
                else
                {
                    sentinelState = "unknown";
                    cleanupState = "manual-reconciliation-required";
                    return new("no-go", reason, operationExecuted, sentinelState, cleanupState);
                }
            }
            catch (Exception)
            {
                sentinelState = "unknown";
                cleanupState = "manual-reconciliation-required";
                return new("no-go", reason, operationExecuted, sentinelState, cleanupState);
            }
        }

        if (!Matches(store.Read(contactId), sentinel) && !Matches(store.Read(contactId), baseline))
        {
            cleanupState = "manual-reconciliation-required";
            return new("no-go", string.IsNullOrWhiteSpace(reason) ? "reconciliation-failed" : reason,
                operationExecuted, sentinelState, cleanupState);
        }

        if (Matches(store.Read(contactId), sentinel))
        {
            try
            {
                store.Restore(contactId, baseline);
                cleanupState = Matches(store.Read(contactId), baseline)
                    ? "restored"
                    : "manual-reconciliation-required";
            }
            catch (Exception)
            {
                cleanupState = "manual-reconciliation-required";
            }
        }

        if (cleanupState != "restored" && sentinelState != "baseline")
        {
            return new("no-go", "cleanup-failed", operationExecuted, sentinelState, cleanupState);
        }

        if (writeOutcomeIsAmbiguous)
        {
            return new("no-go", reason, operationExecuted, sentinelState, cleanupState);
        }

        return cleanupState == "restored"
            ? new("go", string.Empty, operationExecuted, sentinelState, cleanupState)
            : new("no-go", string.IsNullOrWhiteSpace(reason) ? "cleanup-failed" : reason,
                operationExecuted, sentinelState, cleanupState);
    }

    /// <summary>比較三個 allowlisted 欄位，避免跨 fixture 或跨 tenant state 混用。</summary>
    private static bool Matches(
        P72ContactLineProfileSnapshot actual,
        P72ContactLineProfileSnapshot expected)
        => string.Equals(actual.PictureUrl, expected.PictureUrl, StringComparison.Ordinal) &&
           string.Equals(actual.StatusMessage, expected.StatusMessage, StringComparison.Ordinal) &&
           string.Equals(actual.DisplayName, expected.DisplayName, StringComparison.Ordinal);
}

/// <summary>
/// B2 parity store 的唯一 legacy read 入口。它只能用相同的 bounded search
/// category 回傳 typed value/count projection，不接受 caller-supplied FetchXML。
/// </summary>
internal interface IP72UngroupedCommitmentParityStore : IDisposable
{
    /// <summary>以同一個 sanitized search category 執行 legacy read projection。</summary>
    IReadOnlyList<UngroupedCommitmentCountDto> ReadLegacyCounts(string? search);
}

/// <summary>B2 bridge 的去識別化 read evidence 狀態。</summary>
internal sealed record P72UngroupedCommitmentBridgeResult(
    string Outcome,
    string Reason,
    bool OperationExecuted,
    string ParityState,
    int RowCount);

/// <summary>
/// 執行 B2 一次 Data8 aggregate read 與一次 legacy parity read；沒有 mutation，
/// 也不會在 timeout 或 mismatch 後重試。
/// </summary>
internal static class P72UngroupedCommitmentFixtureBridge
{
    /// <summary>執行 bounded read-only parity flow。</summary>
    public static async Task<P72UngroupedCommitmentBridgeResult> ExecuteAsync(
        IPackage02ContactProfileClient client,
        IP72UngroupedCommitmentParityStore store,
        string profileAlias,
        string workloadSubjectId,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(store);
        if (string.IsNullOrWhiteSpace(profileAlias) || string.IsNullOrWhiteSpace(workloadSubjectId))
        {
            throw new ArgumentException("B2 routing values are required.");
        }

        UngroupedCommitmentCountResult data8;
        try
        {
            data8 = await client.CountUngroupedCommitmentAsync(
                new UngroupedCommitmentCountRequest
                {
                    ProfileAlias = profileAlias,
                    WorkloadSubjectId = workloadSubjectId,
                    Search = search
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new("no-go", "read-timeout", true, "unknown", 0);
        }
        catch (Exception)
        {
            return new("no-go", "data8-read-failed", true, "unknown", 0);
        }

        IReadOnlyList<UngroupedCommitmentCountDto> legacy;
        try
        {
            legacy = store.ReadLegacyCounts(search);
        }
        catch (Exception)
        {
            return new("no-go", "legacy-read-failed", true, "unknown", 0);
        }

        try
        {
            var data8Counts = data8.Counts
                .OrderBy(static row => row.Value)
                .ThenBy(static row => row.Count)
                .ToArray();
            var legacyCounts = legacy
                .OrderBy(static row => row.Value)
                .ThenBy(static row => row.Count)
                .ToArray();
            if (!data8Counts.SequenceEqual(legacyCounts))
            {
                return new("no-go", "legacy-parity-mismatch", true, "mismatch", data8Counts.Length);
            }

            return new("go", string.Empty, true, "confirmed", data8Counts.Length);
        }
        catch (Exception)
        {
            return new("no-go", "read-failed", true, "unknown", 0);
        }
    }
}

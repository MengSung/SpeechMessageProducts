// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/P72ContactProfileFixtureBridgeTests.cs
// 用途：以離線替身驗證 P7.2 Slice B1 LINE profile fixture bridge 的
//       baseline、sentinel、read-back、cleanup 與 ambiguous outcome 契約。
// ============================================================================

using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.MemberInfo;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 驗證 B1 fixture bridge 只執行一次 allowlisted LINE profile mutation，
/// 並在結果可確認時還原三個欄位的 baseline。測試替身不持有任何 credential、
/// endpoint 或 session，藉此保護 bridge 的 resource ownership 邊界。
/// </summary>
public sealed class P72ContactProfileFixtureBridgeTests
{
    private static readonly Guid ContactId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly P72ContactLineProfileSnapshot Baseline = new(
        "https://example.invalid/baseline.png",
        "baseline status",
        "baseline display");
    private static readonly P72ContactLineProfileSnapshot Sentinel = new(
        "https://example.invalid/p72-sentinel.png",
        "p72 sentinel status",
        "p72 sentinel display");

    /// <summary>
    /// 保護成功寫入必須 read-back 確認，並在同一個 bounded flow 還原 baseline。
    /// </summary>
    [Fact]
    public async Task Successful_line_profile_write_restores_baseline()
    {
        using var store = new RecordingStore(Baseline);
        var client = new RecordingClient(request =>
        {
            store.Current = new(request.PictureUrl, request.StatusMessage, request.DisplayName);
            return Task.FromResult(new ContactLineProfileUpdateResult
            {
                Disposition = ContactLineProfileUpdateDisposition.Changed,
                CorrelationCategory = ContactLineProfileUpdateCorrelationCategory.ReadBackConfirmed
            });
        });

        var result = await P72ContactProfileFixtureBridge.ExecuteAsync(
            client,
            store,
            ContactId,
            "p72-profile-success",
            Sentinel);

        result.Outcome.Should().Be("go");
        result.Reason.Should().BeEmpty();
        result.SentinelState.Should().Be("confirmed");
        result.CleanupState.Should().Be("restored");
        client.CallCount.Should().Be(1);
        store.RestoreCount.Should().Be(1);
        store.Current.Should().Be(Baseline);
    }

    /// <summary>
    /// 保護 write response 不明確時不得自動重試；若 read-back 證實 sentinel，
    /// 仍須還原 baseline，但整個 evidence 必須維持 no-go。
    /// </summary>
    [Fact]
    public async Task Ambiguous_write_after_commit_restores_without_retry()
    {
        using var store = new RecordingStore(Baseline);
        var client = new RecordingClient(request =>
        {
            store.Current = new(request.PictureUrl, request.StatusMessage, request.DisplayName);
            throw new InvalidOperationException("fault-after-commit");
        });

        var result = await P72ContactProfileFixtureBridge.ExecuteAsync(
            client,
            store,
            ContactId,
            "p72-profile-ambiguous",
            Sentinel);

        result.Outcome.Should().Be("no-go");
        result.Reason.Should().Be("write-ambiguous-reconciled");
        result.SentinelState.Should().Be("confirmed-after-fault");
        result.CleanupState.Should().Be("restored");
        client.CallCount.Should().Be(1);
        store.RestoreCount.Should().Be(1);
        store.Current.Should().Be(Baseline);
    }

    private sealed class RecordingStore : IP72ContactLineProfileFixtureStore
    {
        public RecordingStore(P72ContactLineProfileSnapshot current) => Current = current;

        public P72ContactLineProfileSnapshot Current { get; set; }

        public int RestoreCount { get; private set; }

        public P72ContactLineProfileSnapshot Read(Guid contactId)
        {
            contactId.Should().Be(ContactId);
            return Current;
        }

        public void Restore(Guid contactId, P72ContactLineProfileSnapshot baseline)
        {
            contactId.Should().Be(ContactId);
            RestoreCount++;
            Current = baseline;
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingClient : IPackage02ContactProfileClient
    {
        private readonly Func<ContactLineProfileUpdateRequest, Task<ContactLineProfileUpdateResult>> _operation;

        public RecordingClient(Func<ContactLineProfileUpdateRequest, Task<ContactLineProfileUpdateResult>> operation)
            => _operation = operation;

        public int CallCount { get; private set; }

        public Task<ContactLineProfileUpdateResult> UpdateLineProfileAsync(
            ContactLineProfileUpdateRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return _operation(request);
        }

        public Task<UngroupedCommitmentCountResult> CountUngroupedCommitmentAsync(
            UngroupedCommitmentCountRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}

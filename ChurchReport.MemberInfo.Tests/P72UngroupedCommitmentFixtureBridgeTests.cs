// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/P72UngroupedCommitmentFixtureBridgeTests.cs
// 用途：驗證 P7.2 Slice B2 的 bounded read-only aggregate parity bridge。
// ============================================================================

using FluentAssertions;
using SpeechMessage.Dynamics.ProductClient.MemberInfo;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>保護 B2 Data8 結果必須與同一輸入類別的 legacy projection 完全一致。</summary>
public sealed class P72UngroupedCommitmentFixtureBridgeTests
{
    /// <summary>成功 parity 只允許一次 Data8 read、一次 legacy read，並完成 store dispose。</summary>
    [Fact]
    public async Task Matching_read_only_aggregate_returns_go()
    {
        using var store = new RecordingParityStore(
            new[] { new UngroupedCommitmentCountDto { Value = 1, Count = 4 } });
        var client = new RecordingClient(new[] { new UngroupedCommitmentCountDto { Value = 1, Count = 4 } });

        var result = await P72UngroupedCommitmentFixtureBridge.ExecuteAsync(
            client,
            store,
            "sunnyvalechback",
            "p7.2-ungrouped-aggregate");

        result.Outcome.Should().Be("go");
        result.Reason.Should().BeEmpty();
        result.ParityState.Should().Be("confirmed");
        result.OperationExecuted.Should().BeTrue();
        client.CallCount.Should().Be(1);
        store.ReadCount.Should().Be(1);
    }

    /// <summary>結果不一致時必須 fail closed，且不產生任何 mutation 或 retry。</summary>
    [Fact]
    public async Task Mismatched_read_only_aggregate_returns_no_go()
    {
        using var store = new RecordingParityStore(
            new[] { new UngroupedCommitmentCountDto { Value = 1, Count = 4 } });
        var client = new RecordingClient(new[] { new UngroupedCommitmentCountDto { Value = 1, Count = 5 } });

        var result = await P72UngroupedCommitmentFixtureBridge.ExecuteAsync(
            client,
            store,
            "sunnyvalechback",
            "p7.2-ungrouped-aggregate-mismatch");

        result.Outcome.Should().Be("no-go");
        result.Reason.Should().Be("legacy-parity-mismatch");
        result.ParityState.Should().Be("mismatch");
        result.OperationExecuted.Should().BeTrue();
        client.CallCount.Should().Be(1);
    }

    /// <summary>保護 Data8 讀取失敗必須與 legacy parity 失敗分流，避免操作者誤修另一側查詢。</summary>
    [Fact]
    public async Task Data8_read_failure_returns_sanitized_source_category()
    {
        using var store = new RecordingParityStore(Array.Empty<UngroupedCommitmentCountDto>());
        var client = new ThrowingClient();

        var result = await P72UngroupedCommitmentFixtureBridge.ExecuteAsync(
            client,
            store,
            "sunnyvalechback",
            "p7.2-data8-read-failure");

        result.Outcome.Should().Be("no-go");
        result.Reason.Should().Be("data8-read-failed");
        result.OperationExecuted.Should().BeTrue();
        store.ReadCount.Should().Be(0, because: "legacy parity must not run after the connector read fails");
    }

    /// <summary>保護 legacy parity 讀取失敗只回傳去識別化分類，且不得重試已完成的 Data8 read。</summary>
    [Fact]
    public async Task Legacy_read_failure_returns_sanitized_source_category()
    {
        using var store = new ThrowingParityStore();
        var client = new RecordingClient(Array.Empty<UngroupedCommitmentCountDto>());

        var result = await P72UngroupedCommitmentFixtureBridge.ExecuteAsync(
            client,
            store,
            "sunnyvalechback",
            "p7.2-legacy-read-failure");

        result.Outcome.Should().Be("no-go");
        result.Reason.Should().Be("legacy-read-failed");
        result.OperationExecuted.Should().BeTrue();
        client.CallCount.Should().Be(1);
    }

    private sealed class RecordingParityStore : IP72UngroupedCommitmentParityStore
    {
        private readonly IReadOnlyList<UngroupedCommitmentCountDto> _legacyCounts;

        public RecordingParityStore(IReadOnlyList<UngroupedCommitmentCountDto> legacyCounts)
            => _legacyCounts = legacyCounts;

        public int ReadCount { get; private set; }

        public IReadOnlyList<UngroupedCommitmentCountDto> ReadLegacyCounts(string? search)
        {
            search.Should().BeNull();
            ReadCount++;
            return _legacyCounts;
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingClient : IPackage02ContactProfileClient
    {
        private readonly IReadOnlyList<UngroupedCommitmentCountDto> _counts;

        public RecordingClient(IReadOnlyList<UngroupedCommitmentCountDto> counts) => _counts = counts;

        public int CallCount { get; private set; }

        public Task<ContactLineProfileUpdateResult> UpdateLineProfileAsync(
            ContactLineProfileUpdateRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<UngroupedCommitmentCountResult> CountUngroupedCommitmentAsync(
            UngroupedCommitmentCountRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.Search.Should().BeNull();
            CallCount++;
            return Task.FromResult(new UngroupedCommitmentCountResult { Counts = _counts });
        }
    }

    private sealed class ThrowingClient : IPackage02ContactProfileClient
    {
        public Task<ContactLineProfileUpdateResult> UpdateLineProfileAsync(
            ContactLineProfileUpdateRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<UngroupedCommitmentCountResult> CountUngroupedCommitmentAsync(
            UngroupedCommitmentCountRequest request,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Injected Data8 read failure.");
    }

    private sealed class ThrowingParityStore : IP72UngroupedCommitmentParityStore
    {
        public IReadOnlyList<UngroupedCommitmentCountDto> ReadLegacyCounts(string? search)
            => throw new InvalidOperationException("Injected legacy parity failure.");

        public void Dispose()
        {
        }
    }
}

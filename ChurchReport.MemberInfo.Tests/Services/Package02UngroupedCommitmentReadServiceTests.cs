// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/Services/Package02UngroupedCommitmentReadServiceTests.cs
// 用途：保護 P7.4 ORG-CALL-00024 的 Package02 未分組承諾 aggregate 讀取服務，確保固定 deployment
//       profile/workload、取消傳遞、DTO 完整性與 A/B request/profile 隔離。
//
// 信任與生命週期：
// 1. 所有 fake client 與 result 僅存在於單一測試；它們不建立 CRM、Gateway、Data8、Session、cache、timer、
//    stream、背景工作或任何外部資源。
// 2. 測試以 distinct scalar marker 交錯兩個要求，並改寫 upstream collection 與已發布 collection；決定性斷言是
//    任何改寫都不會污染另一個 request 或 service result。
// 3. cancellation、duplicate、negative count 與 typed fault 都必須在發布 partial count 前 fail closed，禁止 retry
//    或 legacy aggregate fallback，避免不確定 transport 結果被下一個 profile／使用者重用。
// ============================================================================

using ChurchReport.Services.MemberInfo;
using FluentAssertions;
using SpeechMessage.Dynamics.ProductClient.MemberInfo;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Services;

/// <summary>
/// 驗證 Package02 未分組承諾讀取 coordinator 只在 controller 已完成 Church scope 與既有授權前置後，
/// 以固定 deployment scalar 呼叫唯一 typed ProductClient operation。這些是本機 contract tests，
/// 不構成 CE、feature enablement、traffic cutover、P7.5 或 P8 證據。
/// </summary>
public sealed class Package02UngroupedCommitmentReadServiceTests
{
    /// <summary>
    /// 保護 service 以固定 profile/workload 和原樣 cancellation token dispatch，且將 upstream DTO collection
    /// defensive-copy 成不可變的 request-local count result。故障注入是呼叫後改寫 upstream 陣列與第一份
    /// published dictionary；決定性斷言是後續讀取仍保留原 marker，不能污染同一 request 的已驗證資料。
    /// </summary>
    [Fact]
    public async Task RetrieveAsync_uses_fixed_request_and_defensively_copies_valid_counts()
    {
        var upstream = new[]
        {
            new UngroupedCommitmentCountDto { Value = 3, Count = 7 },
            new UngroupedCommitmentCountDto { Value = 8, Count = 2 }
        };
        var client = new RecordingContactProfileClient(
            (_, _) => Task.FromResult(new UngroupedCommitmentCountResult { Counts = upstream }));
        var service = new Package02UngroupedCommitmentReadService(client, "crm91");
        using var cancellation = new CancellationTokenSource();

        var result = await service.RetrieveAsync("王", cancellation.Token);
        upstream[0] = new UngroupedCommitmentCountDto { Value = 3, Count = 999 };
        var firstCounts = result.GetCounts();

        client.ProfileAlias.Should().Be("crm91");
        client.WorkloadSubjectId.Should().Be("church-report-memberinfo-ungrouped-commitment-read");
        client.Search.Should().Be("王");
        client.ObservedCancellationToken.Should().Be(cancellation.Token);
        firstCounts.Should().BeEquivalentTo(new Dictionary<int, int> { [3] = 7, [8] = 2 });

        Action mutatePublishedCounts = () => ((IDictionary<int, int>)firstCounts)[3] = 123;
        mutatePublishedCounts.Should().Throw<NotSupportedException>();
        result.GetCounts().Should().BeEquivalentTo(new Dictionary<int, int> { [3] = 7, [8] = 2 });
    }

    /// <summary>
    /// 保護 malformed typed result 在 service boundary fail closed。故障注入是 duplicate raw value、negative
    /// count 與 null result；決定性斷言是每一種錯誤都丟出固定例外且沒有 count map 被發布，不能以 legacy
    /// aggregate 或部分 DTO 補救。
    /// </summary>
    [Fact]
    public async Task RetrieveAsync_rejects_duplicate_negative_or_incomplete_counts_before_publish()
    {
        var duplicateClient = new RecordingContactProfileClient(
            (_, _) => Task.FromResult(new UngroupedCommitmentCountResult
            {
                Counts =
                [
                    new UngroupedCommitmentCountDto { Value = 3, Count = 1 },
                    new UngroupedCommitmentCountDto { Value = 3, Count = 2 }
                ]
            }));
        var duplicateService = new Package02UngroupedCommitmentReadService(duplicateClient, "crm91");

        Func<Task> duplicate = () => duplicateService.RetrieveAsync(null, CancellationToken.None);

        await duplicate.Should().ThrowAsync<InvalidOperationException>();

        var negativeClient = new RecordingContactProfileClient(
            (_, _) => Task.FromResult(new UngroupedCommitmentCountResult
            {
                Counts = [new UngroupedCommitmentCountDto { Value = 8, Count = -1 }]
            }));
        var negativeService = new Package02UngroupedCommitmentReadService(negativeClient, "crm91");

        Func<Task> negative = () => negativeService.RetrieveAsync(null, CancellationToken.None);

        await negative.Should().ThrowAsync<InvalidOperationException>();

        var incompleteClient = new RecordingContactProfileClient((_, _) => Task.FromResult<UngroupedCommitmentCountResult>(null!));
        var incompleteService = new Package02UngroupedCommitmentReadService(incompleteClient, "crm91");

        Func<Task> incomplete = () => incompleteService.RetrieveAsync(null, CancellationToken.None);

        await incomplete.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// 保護交錯 profile A/B 讀取不共用 request、result、cancellation 或 count map。fake client 以兩個非同步
    /// completion source 反轉完成順序；決定性斷言是每個 service 只送出自己的 server-owned profile marker，
    /// 且回應集合不會因另一個 service 的 result 被改寫。
    /// </summary>
    [Fact]
    public async Task RetrieveAsync_keeps_interleaved_profile_results_and_cancellation_isolated()
    {
        var first = new TaskCompletionSource<UngroupedCommitmentCountResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<UngroupedCommitmentCountResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new RecordingContactProfileClient(
            (request, _) => string.Equals(request.ProfileAlias, "profile-a", StringComparison.Ordinal)
                ? first.Task
                : second.Task);
        var firstService = new Package02UngroupedCommitmentReadService(client, "profile-a");
        var secondService = new Package02UngroupedCommitmentReadService(client, "profile-b");
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();

        var firstOperation = firstService.RetrieveAsync("A", firstCancellation.Token);
        var secondOperation = secondService.RetrieveAsync("B", secondCancellation.Token);
        second.SetResult(new UngroupedCommitmentCountResult
        {
            Counts = [new UngroupedCommitmentCountDto { Value = 20, Count = 2 }]
        });
        first.SetResult(new UngroupedCommitmentCountResult
        {
            Counts = [new UngroupedCommitmentCountDto { Value = 10, Count = 1 }]
        });

        var results = await Task.WhenAll(firstOperation, secondOperation);

        results[0].GetCounts().Should().BeEquivalentTo(new Dictionary<int, int> { [10] = 1 });
        results[1].GetCounts().Should().BeEquivalentTo(new Dictionary<int, int> { [20] = 2 });
        results[0].GetCounts().Should().NotBeSameAs(results[1].GetCounts());
        client.Requests.Select(request => request.ProfileAlias).Should().BeEquivalentTo("profile-a", "profile-b");
        client.CancellationTokens.Should().Contain(firstCancellation.Token);
        client.CancellationTokens.Should().Contain(secondCancellation.Token);
    }

    /// <summary>
    /// 保護 request cancellation 不被 service 轉成一般 fault 或 fallback。故障注入是 fake client 回傳取消的
    /// typed task；決定性斷言是 <see cref="OperationCanceledException"/> 原樣傳播且觀察到的 token 與 caller
    /// token 完全一致，讓下游 executor/lease owner 可依既有 deterministic cleanup 處理不確定 transport。
    /// </summary>
    [Fact]
    public async Task RetrieveAsync_propagates_cancellation_without_retry_or_fallback()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new RecordingContactProfileClient(
            (_, _) => Task.FromCanceled<UngroupedCommitmentCountResult>(new CancellationToken(canceled: true)));
        var service = new Package02UngroupedCommitmentReadService(client, "crm91");

        Func<Task> cancelled = () => service.RetrieveAsync(null, cancellation.Token);

        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        client.ObservedCancellationToken.Should().Be(cancellation.Token);
        client.CountInvocationCount.Should().Be(1);
    }

    /// <summary>
    /// request-local fake client 只記錄本測試的 typed requests。它不含 static state、cache、legacy fallback、
    /// retry、CRM SDK 或外部資源；未使用的 LINE write method 明確拒絕，防止測試意外跨入 mutation family。
    /// </summary>
    private sealed class RecordingContactProfileClient : IPackage02ContactProfileClient
    {
        private readonly Func<UngroupedCommitmentCountRequest, CancellationToken, Task<UngroupedCommitmentCountResult>> _count;

        /// <summary>以 test-owned delegate 建立 fake client；delegate 的閉包只在單一測試存活。</summary>
        public RecordingContactProfileClient(
            Func<UngroupedCommitmentCountRequest, CancellationToken, Task<UngroupedCommitmentCountResult>> count)
            => _count = count;

        /// <summary>已觀察的 typed request 副本；僅供本測試斷言，不會流入 production。</summary>
        public List<UngroupedCommitmentCountRequest> Requests { get; } = [];

        /// <summary>已觀察的 request cancellation token；不註冊或持久化 callback。</summary>
        public List<CancellationToken> CancellationTokens { get; } = [];

        /// <summary>最後一次 server-owned profile scalar。</summary>
        public string? ProfileAlias => Requests.LastOrDefault()?.ProfileAlias;

        /// <summary>最後一次固定 workload scalar。</summary>
        public string? WorkloadSubjectId => Requests.LastOrDefault()?.WorkloadSubjectId;

        /// <summary>最後一次 optional search；它不是 profile、owner 或 arbitrary query。</summary>
        public string? Search => Requests.LastOrDefault()?.Search;

        /// <summary>最後一次 observed cancellation token。</summary>
        public CancellationToken ObservedCancellationToken => CancellationTokens.LastOrDefault();

        /// <summary>typed count 呼叫次數；用來鎖定 cancellation 不會 retry。</summary>
        public int CountInvocationCount { get; private set; }

        /// <summary>記錄 aggregate request 後交給 test-owned response；不建立任何連線或資源 owner。</summary>
        public Task<UngroupedCommitmentCountResult> CountUngroupedCommitmentAsync(
            UngroupedCommitmentCountRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            CancellationTokens.Add(cancellationToken);
            CountInvocationCount++;
            return _count(request, cancellationToken);
        }

        /// <summary>本 read-only test 明確禁止 LINE profile mutation family。</summary>
        public Task<ContactLineProfileUpdateResult> UpdateLineProfileAsync(
            ContactLineProfileUpdateRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}

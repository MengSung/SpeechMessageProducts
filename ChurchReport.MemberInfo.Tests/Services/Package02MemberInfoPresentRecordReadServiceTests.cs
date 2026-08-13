// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/Services/Package02MemberInfoPresentRecordReadServiceTests.cs
// 用途：保護 P7.4 ORG-CALL-00026 的出席紀錄 request-local DTO coordinator，確保部署 profile、固定 workload、
//       防禦性複製、取消與 A/B profile 隔離均不會透過 MemberInfo 讀取頁面洩漏。
//
// 信任與生命週期：
// 1. 所有 fake client、DTO 與完成來源都只屬於單一測試；不建立 CRM、Data8、Gateway、Session、cache、timer、
//    stream、背景工作、取消註冊或任何可釋放外部資源。
// 2. 測試會在上游回傳後改寫來源 collection，並交錯完成 A/B 要求；決定性斷言是已發佈結果仍是各 request 的
//    私有 snapshot，不能被另一個 profile、呼叫端集合或後續 response 改寫。
// 3. 取消、空列、重複 identity 與 typed fault 都必須在發布前 fail closed；service 不可 retry 或回到 legacy
//    ToolUtility/CRM 路徑，讓 executor/process-host 保持唯一 transport cleanup owner。
// ============================================================================

using ChurchReport.Services.MemberInfo;
using FluentAssertions;
using SpeechMessage.Dynamics.ProductClient.MemberInfo;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Services;

/// <summary>
/// 驗證出席紀錄 service 只在 controller 已完成 MemberInfo session 與 contact authorization 後，才以固定的
/// deployment-owned profile 與 server-owned workload 呼叫獨立 ProductClient。這些測試是本機 DTO 合約，
/// 不建立 CE fixture、不啟用 feature gate，亦不是 traffic、P7.5 或 P8 證據。
/// </summary>
public sealed class Package02MemberInfoPresentRecordReadServiceTests
{
    /// <summary>
    /// 保護 service 對單一已授權 contact 原樣傳遞 cancellation，並把上游 DTO collection 複製成不能寫回的
    /// request-local result。故障注入是在 dispatch 後置換上游陣列與嘗試改寫公開 collection；決定性斷言是
    /// 既有結果保留原 marker，且公開集合不是 backing array 或可寫 List。
    /// </summary>
    [Fact]
    public async Task RetrieveAsync_uses_fixed_request_and_defensively_copies_upstream_rows()
    {
        var contactId = Guid.NewGuid();
        var sourceRows = new[] { CreateRow("original") };
        var client = new RecordingPresentRecordClient(
            (_, _) => Task.FromResult<IReadOnlyList<MemberInfoPresentRecordReadDto>>(sourceRows));
        var service = new Package02MemberInfoPresentRecordReadService(client, "crm91");
        using var cancellation = new CancellationTokenSource();

        var result = await service.RetrieveAsync(contactId, cancellation.Token);
        sourceRows[0] = CreateRow("replacement");

        client.ProfileAlias.Should().Be("crm91");
        client.WorkloadSubjectId.Should().Be("church-report-memberinfo-present-record-read");
        client.ContactId.Should().Be(contactId);
        client.ObservedCancellationToken.Should().Be(cancellation.Token);
        result.GetRows().Should().ContainSingle().Which.PrayItem.Should().Be("pray-original");
        result.GetRows().Should().NotBeAssignableTo<MemberInfoPresentRecordReadDto[]>();
        result.GetRows().Should().NotBeAssignableTo<List<MemberInfoPresentRecordReadDto>>();
    }

    /// <summary>
    /// 保護 malformed upstream rows 在 service 邊界全數拒絕。故障注入分別為 null row、空 GUID 與同 response
    /// 重複 GUID；決定性斷言是每種情況都丟出固定例外，沒有 partial row、legacy fallback 或第二次 dispatch。
    /// </summary>
    [Fact]
    public async Task RetrieveAsync_rejects_incomplete_or_duplicate_rows_before_publish()
    {
        var duplicateId = Guid.NewGuid();
        var incompleteClient = new RecordingPresentRecordClient(
            (_, _) => Task.FromResult<IReadOnlyList<MemberInfoPresentRecordReadDto>>([null!]));
        var duplicateClient = new RecordingPresentRecordClient(
            (_, _) => Task.FromResult<IReadOnlyList<MemberInfoPresentRecordReadDto>>(
                [CreateRow("one", duplicateId), CreateRow("two", duplicateId)]));
        var emptyIdClient = new RecordingPresentRecordClient(
            (_, _) => Task.FromResult<IReadOnlyList<MemberInfoPresentRecordReadDto>>([CreateRow("empty", Guid.Empty)]));

        await new Package02MemberInfoPresentRecordReadService(incompleteClient, "crm91")
            .Invoking(service => service.RetrieveAsync(Guid.NewGuid(), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
        await new Package02MemberInfoPresentRecordReadService(duplicateClient, "crm91")
            .Invoking(service => service.RetrieveAsync(Guid.NewGuid(), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
        await new Package02MemberInfoPresentRecordReadService(emptyIdClient, "crm91")
            .Invoking(service => service.RetrieveAsync(Guid.NewGuid(), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();

        incompleteClient.InvocationCount.Should().Be(1);
        duplicateClient.InvocationCount.Should().Be(1);
        emptyIdClient.InvocationCount.Should().Be(1);
    }

    /// <summary>
    /// 保護交錯完成的 A/B profile 要求不共用 request、completion、結果列或 cancellation。fake client 將兩個
    /// profile 導向不同 completion source 並反向完成；決定性斷言是每個結果僅含自己的 marker，且 profile/
    /// contact/cancellation scalar 沒有被另一個呼叫覆寫。
    /// </summary>
    [Fact]
    public async Task RetrieveAsync_keeps_interleaved_profile_results_and_cancellation_isolated()
    {
        var first = new TaskCompletionSource<IReadOnlyList<MemberInfoPresentRecordReadDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<IReadOnlyList<MemberInfoPresentRecordReadDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new RecordingPresentRecordClient(
            (request, _) => string.Equals(request.ProfileAlias, "profile-a", StringComparison.Ordinal)
                ? first.Task
                : second.Task);
        var firstService = new Package02MemberInfoPresentRecordReadService(client, "profile-a");
        var secondService = new Package02MemberInfoPresentRecordReadService(client, "profile-b");
        var firstContactId = Guid.NewGuid();
        var secondContactId = Guid.NewGuid();
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();

        var firstOperation = firstService.RetrieveAsync(firstContactId, firstCancellation.Token);
        var secondOperation = secondService.RetrieveAsync(secondContactId, secondCancellation.Token);
        second.SetResult([CreateRow("b")]);
        first.SetResult([CreateRow("a")]);

        var results = await Task.WhenAll(firstOperation, secondOperation);

        results[0].GetRows().Should().ContainSingle().Which.PrayItem.Should().Be("pray-a");
        results[1].GetRows().Should().ContainSingle().Which.PrayItem.Should().Be("pray-b");
        results[0].GetRows().Should().NotBeSameAs(results[1].GetRows());
        client.Requests.Select(request => request.ProfileAlias).Should().BeEquivalentTo("profile-a", "profile-b");
        client.Requests.Select(request => request.ContactId).Should().BeEquivalentTo(new[] { firstContactId, secondContactId });
        client.CancellationTokens.Should().Contain(firstCancellation.Token);
        client.CancellationTokens.Should().Contain(secondCancellation.Token);
    }

    /// <summary>
    /// 保護下游已取消工作原樣離開 service。故障注入是 fake client 回傳取消 task；決定性斷言是
    /// <see cref="OperationCanceledException"/> 未被轉為一般錯誤或 legacy retry，且 dispatch 次數維持一次。
    /// </summary>
    [Fact]
    public async Task RetrieveAsync_propagates_cancellation_without_retry_or_legacy_fallback()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new RecordingPresentRecordClient(
            (_, _) => Task.FromCanceled<IReadOnlyList<MemberInfoPresentRecordReadDto>>(
                new CancellationToken(canceled: true)));
        var service = new Package02MemberInfoPresentRecordReadService(client, "crm91");

        await service.Invoking(instance => instance.RetrieveAsync(Guid.NewGuid(), cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        client.ObservedCancellationToken.Should().Be(cancellation.Token);
        client.InvocationCount.Should().Be(1);
    }

    /// <summary>
    /// 建立可辨識的 immutable DTO 列；資料只用於區分測試 request-local snapshot，沒有 CRM Entity、profile、
    /// credential、Session、lease 或 resource owner。
    /// </summary>
    /// <param name="marker">用於驗證 A/B 與 defensive-copy 的本機測試 marker。</param>
    /// <param name="id">可選 identity，可用於注入空值或重複 GUID 故障。</param>
    /// <returns>封閉 ProductClient contract 可接受的純量出席列。</returns>
    private static MemberInfoPresentRecordReadDto CreateRow(string marker, Guid? id = null)
        => new()
        {
            PresentRecordId = id ?? Guid.NewGuid(),
            ContactFullName = $"member-{marker}",
            SundayDate = new DateTime(2026, 8, 9),
            Sunday = true,
            SmallGroup = false,
            PrayItem = $"pray-{marker}"
        };

    /// <summary>
    /// test-owned 無狀態 client，只記錄當次測試的 immutable request scalar。它沒有 static/cache、retry、
    /// CRM SDK 或外部資源；所有資料會隨 test instance 釋放，不能污染另一個測試或產品 request。
    /// </summary>
    private sealed class RecordingPresentRecordClient : IMemberInfoPresentRecordReadClient
    {
        private readonly Func<MemberInfoPresentRecordReadRequest, CancellationToken, Task<IReadOnlyList<MemberInfoPresentRecordReadDto>>> _retrieve;

        /// <summary>以 test-owned delegate 建立 fake；delegate closure 的生命週期不超出目前 test。</summary>
        public RecordingPresentRecordClient(
            Func<MemberInfoPresentRecordReadRequest, CancellationToken, Task<IReadOnlyList<MemberInfoPresentRecordReadDto>>> retrieve)
            => _retrieve = retrieve;

        /// <summary>目前測試觀察到的 typed request；不會交給 production 或以 static 保存。</summary>
        public List<MemberInfoPresentRecordReadRequest> Requests { get; } = [];

        /// <summary>目前測試觀察到的 cancellation token；不註冊 callback 或接管 CTS lifecycle。</summary>
        public List<CancellationToken> CancellationTokens { get; } = [];

        /// <summary>最後一個 deployment profile scalar。</summary>
        public string? ProfileAlias => Requests.LastOrDefault()?.ProfileAlias;

        /// <summary>最後一個 server-owned workload scalar。</summary>
        public string? WorkloadSubjectId => Requests.LastOrDefault()?.WorkloadSubjectId;

        /// <summary>最後一個 controller 已授權 contact locator。</summary>
        public Guid ContactId => Requests.LastOrDefault()?.ContactId ?? Guid.Empty;

        /// <summary>最後一個原樣轉遞 token。</summary>
        public CancellationToken ObservedCancellationToken => CancellationTokens.LastOrDefault();

        /// <summary>唯一 typed dispatch 次數；用來證明取消/故障不觸發 retry。</summary>
        public int InvocationCount { get; private set; }

        /// <summary>
        /// 記錄目前 request 後回傳 test-owned response。fake 不建立連線、pool、timer 或 cancel registration，
        /// 所以不會偷走 executor/process-host 的唯一 cleanup responsibility。
        /// </summary>
        /// <param name="request">service 建立的 deployment-owned typed request。</param>
        /// <param name="cancellationToken">由呼叫端原樣傳入且僅供 assertion 的 token。</param>
        /// <returns>test delegate 的 request-local DTO snapshot。</returns>
        public Task<IReadOnlyList<MemberInfoPresentRecordReadDto>> RetrievePresentRecordsByContactAsync(
            MemberInfoPresentRecordReadRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            CancellationTokens.Add(cancellationToken);
            InvocationCount++;
            return _retrieve(request, cancellationToken);
        }
    }
}

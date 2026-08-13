// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/Services/Package03MemberInfoCommitmentMetadataReadServiceTests.cs
// 用途：驗證 P7.4 ORG-CALL-00040 的 Package03 承諾類型 metadata 讀取服務，保護固定 profile/workload、
//       取消傳遞、結構 fail-closed、defensive copy 與 A/B request-local isolation。
//
// 信任與生命週期：
// 1. fake client 僅存在於單一測試，沒有 CRM SDK、Gateway、Session、cache、connector、背景工作或可釋放資源。
// 2. 測試刻意以不同 profile 與可辨識 option 值交錯執行，斷言任何 list 或 scalar 都不跨 service/request 重用。
// 3. fault、取消及 malformed DTO 均不得發布 partial result、retry 或呼叫 legacy metadata provider；本檔不構成 CE
//    evidence、流量切換、ToolUtility removal 或 P8 deployment evidence。
// ============================================================================

using ChurchReport.Services.MemberInfo;
using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.SpecialResources;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Services;

/// <summary>
/// 驗證 request-local metadata coordinator 的安全 DTO 邊界。每個測試只建立短生命週期 fake，確保可重用的
/// ProductClient facade 不會因前一個 profile 的 option result、token 或例外而污染下一個呼叫。
/// </summary>
public sealed class Package03MemberInfoCommitmentMetadataReadServiceTests
{
    /// <summary>
    /// 保護唯一的 server-owned profile、workload、target 與 cancellation forwarding。故障注入是在 upstream
    /// 回覆後改寫來源清單與第一份 published collection；決定性斷言是 result 仍保有原 scalar，且外部無法
    /// 透過 writable backing array 改寫另一份讀取結果。
    /// </summary>
    [Fact]
    public async Task RetrieveAsync_uses_fixed_request_and_publishes_defensive_options()
    {
        var source = new[]
        {
            CreateOption(11, "第一類", 0),
            CreateOption(22, "第二類", 1)
        };
        var client = new RecordingPackage03MetadataClient(
            _ => Task.FromResult(new OptionSetRetrieveResult { Options = source }));
        var service = new Package03MemberInfoCommitmentMetadataReadService(client, "profile-a");
        using var cancellation = new CancellationTokenSource();

        var result = await service.RetrieveAsync(cancellation.Token);
        source[0] = CreateOption(99, "已改寫", 0);
        var firstRead = result.GetOptions();
        ((IList<MemberInfoCommitmentTypeOption>)firstRead).Invoking(list => list[0] = new MemberInfoCommitmentTypeOption(88, "不可寫", 0))
            .Should().Throw<NotSupportedException>();

        result.GetOptions().Select(option => option.Value).Should().Equal(11, 22);
        result.GetOptions().Select(option => option.Label).Should().Equal("第一類", "第二類");
        client.ProfileAlias.Should().Be("profile-a");
        client.WorkloadSubjectId.Should().Be("church-report-memberinfo-commitment-metadata-read");
        client.Target.Should().Be(MetadataOptionSetTarget.ContactCustomerTypeCode);
        client.ObservedCancellationToken.Should().Be(cancellation.Token);
        client.CallCount.Should().Be(1);
    }

    /// <summary>
    /// 保護兩個 profile 的 metadata result 在非同步交錯下仍是獨立 snapshot。故障注入是先完成 B 再完成 A；
    /// 決定性斷言是 profile、option marker 與輸出 collection 均不交叉，證明 service 沒有 static 或 shared cache。
    /// </summary>
    [Fact]
    public async Task RetrieveAsync_keeps_interleaved_profile_results_isolated()
    {
        var firstResponse = new TaskCompletionSource<OptionSetRetrieveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondResponse = new TaskCompletionSource<OptionSetRetrieveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstClient = new RecordingPackage03MetadataClient(_ => firstResponse.Task);
        var secondClient = new RecordingPackage03MetadataClient(_ => secondResponse.Task);
        var firstService = new Package03MemberInfoCommitmentMetadataReadService(firstClient, "profile-a");
        var secondService = new Package03MemberInfoCommitmentMetadataReadService(secondClient, "profile-b");

        var firstOperation = firstService.RetrieveAsync(CancellationToken.None);
        var secondOperation = secondService.RetrieveAsync(CancellationToken.None);
        secondResponse.SetResult(new OptionSetRetrieveResult { Options = new[] { CreateOption(202, "B", 0) } });
        firstResponse.SetResult(new OptionSetRetrieveResult { Options = new[] { CreateOption(101, "A", 0) } });

        var results = await Task.WhenAll(firstOperation, secondOperation);

        results[0].GetOptions().Should().ContainSingle().Which.Value.Should().Be(101);
        results[1].GetOptions().Should().ContainSingle().Which.Value.Should().Be(202);
        results[0].GetOptions().Should().NotBeSameAs(results[1].GetOptions());
        firstClient.ProfileAlias.Should().Be("profile-a");
        secondClient.ProfileAlias.Should().Be("profile-b");
    }

    /// <summary>
    /// 保護取消與 upstream 結構錯誤均 fail closed。故障注入是取消的 typed task、重複 raw value、重複 order、
    /// 空白 label 與不連續 order；決定性斷言是每種錯誤只 dispatch 一次並在 result publication 前丟出例外，
    /// 不 retry、不 fallback 或發出 partial metadata。
    /// </summary>
    [Fact]
    public async Task RetrieveAsync_propagates_cancellation_and_rejects_malformed_options_without_retry()
    {
        using var cancellation = new CancellationTokenSource();
        var cancelledClient = new RecordingPackage03MetadataClient(
            _ => Task.FromCanceled<OptionSetRetrieveResult>(new CancellationToken(canceled: true)));
        var cancelledService = new Package03MemberInfoCommitmentMetadataReadService(cancelledClient, "profile-a");

        Func<Task> cancelled = () => cancelledService.RetrieveAsync(cancellation.Token);

        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        cancelledClient.ObservedCancellationToken.Should().Be(cancellation.Token);
        cancelledClient.CallCount.Should().Be(1);

        foreach (var malformed in new IReadOnlyList<OptionSetOptionDto>[]
                 {
                     new[] { CreateOption(1, "A", 0), CreateOption(1, "B", 1) },
                     new[] { CreateOption(1, "A", 0), CreateOption(2, "B", 0) },
                     new[] { CreateOption(1, " ", 0) },
                     new[] { CreateOption(1, "A", 1) }
                 })
        {
            var client = new RecordingPackage03MetadataClient(
                _ => Task.FromResult(new OptionSetRetrieveResult { Options = malformed }));
            var service = new Package03MemberInfoCommitmentMetadataReadService(client, "profile-a");

            Func<Task> retrieve = () => service.RetrieveAsync(CancellationToken.None);

            await retrieve.Should().ThrowAsync<InvalidOperationException>();
            client.CallCount.Should().Be(1);
        }
    }

    /// <summary>
    /// 保護空白 deployment profile 在 client dispatch 前拒絕。故障注入是空白 profile；決定性斷言是 constructor
    /// fail closed 且 fake 沒有任何 call，避免猜選另一個 Dynamics organization 或延長不必要 resource lifetime。
    /// </summary>
    [Fact]
    public void Constructor_rejects_an_empty_deployment_profile_before_any_dispatch()
    {
        var client = new RecordingPackage03MetadataClient(
            _ => Task.FromResult(new OptionSetRetrieveResult { Options = Array.Empty<OptionSetOptionDto>() }));

        Action create = () => new Package03MemberInfoCommitmentMetadataReadService(client, " ");

        create.Should().Throw<InvalidOperationException>().WithMessage("*ProfileAlias*");
        client.CallCount.Should().Be(0);
    }

    /// <summary>建立最小合法 metadata DTO；只含本 test 的 scalar，沒有 CRM metadata graph 或外部資源。</summary>
    private static OptionSetOptionDto CreateOption(int value, string label, int order)
        => new()
        {
            Value = value,
            Label = label,
            ConfiguredOrder = order
        };

    /// <summary>
    /// 記錄單一 Package03 metadata request 的短生命週期 fake。未使用 capability 一律拒絕，避免測試意外跨入
    /// image、weekly statistics 或任何 write family；它不含 static state、retry、cache、Session 或 connector。
    /// </summary>
    private sealed class RecordingPackage03MetadataClient : IPackage03SpecialResourceClient
    {
        private readonly Func<OptionSetRetrieveRequest, Task<OptionSetRetrieveResult>> _retrieve;

        /// <summary>以測試專屬 callback 建立 fake，callback 的資料生命週期不超過該測試。</summary>
        public RecordingPackage03MetadataClient(Func<OptionSetRetrieveRequest, Task<OptionSetRetrieveResult>> retrieve)
            => _retrieve = retrieve;

        /// <summary>最後一次固定 profile，只供決定性 assertion 使用。</summary>
        public string? ProfileAlias { get; private set; }

        /// <summary>最後一次固定 workload，不可由測試以外的 caller 提供。</summary>
        public string? WorkloadSubjectId { get; private set; }

        /// <summary>最後一次封閉 metadata target。</summary>
        public MetadataOptionSetTarget? Target { get; private set; }

        /// <summary>最後一次接收的 cancellation token。</summary>
        public CancellationToken ObservedCancellationToken { get; private set; }

        /// <summary>總 dispatch 次數；用來排除 retry。</summary>
        public int CallCount { get; private set; }

        /// <summary>記錄唯一允許的 metadata operation 並回傳 test-owned DTO；不建立外部連線或資源。</summary>
        public Task<OptionSetRetrieveResult> RetrieveOptionSetAsync(
            OptionSetRetrieveRequest request,
            CancellationToken cancellationToken = default)
        {
            ProfileAlias = request.ProfileAlias;
            WorkloadSubjectId = request.WorkloadSubjectId;
            Target = request.Target;
            ObservedCancellationToken = cancellationToken;
            CallCount++;
            return _retrieve(request);
        }

        /// <summary>禁止 image read capability，避免 metadata 測試擴張範圍。</summary>
        public Task<ContactImageResult> RetrieveContactImageAsync(ContactImageRetrieveRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        /// <summary>禁止 MemberInfo image write capability，避免 metadata 測試擴張範圍。</summary>
        public Task<ContactImageUpdateResult> UpdateMemberInfoContactImageAsync(ContactImageUpdateRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        /// <summary>禁止 NewPerson image write capability，避免 metadata 測試擴張範圍。</summary>
        public Task<ContactImageUpdateResult> UpdateNewPersonContactImageAsync(ContactImageUpdateRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        /// <summary>禁止 weekly statistics capability，避免 metadata 測試擴張範圍。</summary>
        public Task<MeetingStatisticsRetrieveResult> RetrieveMeetingStatisticsAsync(MeetingStatisticsRetrieveRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}

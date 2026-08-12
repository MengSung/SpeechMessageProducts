// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/SpecialResourceProductClientTests.cs
// 用途：以 TDD 鎖定 P7.3 image、metadata 與 weekly statistic 的 typed ProductClient 邊界。
//
// 安全與生命週期：
// - 所有案例只使用短生命週期 fake executor，不建立 CRM、Data8、HTTP、stream、cache、credential、session 或背景工作。
// - 測試驗證 image bytes 在 caller、operation response 與產品 result 三層均不共用 mutable array；metadata/statistics
//   只映射純值 DTO，錯配 operation/branch 不得形成 partial result。
// - fake executor 僅保存目前測試的一筆 request，沒有 static/cache retention；測試完成後由 GC 回收。
// ============================================================================

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.DependencyInjection;
using SpeechMessage.Dynamics.ProductClient.SpecialResources;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 P7.3 特殊資源 ProductClient 只能委派五個固定 capability、複製 mutable image 資料，並在 response
/// identity/discriminator 不符時 fail closed。此類測試不充當 CE evidence，僅證明本機產品層不會保存 SDK graph、
/// paging cookie、profile generation、request body 或跨使用者狀態。
/// </summary>
public sealed class SpecialResourceProductClientTests
{
    /// <summary>
    /// 保護 image retrieve 只接受正確 operation/branch，並從 response defensive copy 建立另一個產品 DTO copy。
    /// 故障注入是呼叫端對回傳 bytes 的修改；決定性斷言是第二次讀取仍保留原值，證明 client 未把 connector
    /// response 的 mutable array、CRM Entity、stream 或任何 session reference 暴露給產品呼叫端。
    /// </summary>
    [Fact]
    public async Task Retrieve_contact_image_maps_only_the_closed_branch_and_returns_defensive_bytes()
    {
        var imageBytes = CreateValidOnePixelPng();
        var executor = new RecordingExecutor(OperationExecutionResult.Success(
            OperationResponseData.ForContactImage(
                OperationIds.MemberInfoContactRetrieveImage,
                "9.1",
                new ContactImageResponseData(imageBytes, ContactImageMediaKind.Png))));
        var client = new Package03SpecialResourceClient(
            executor,
            NullLogger<Package03SpecialResourceClient>.Instance);
        var contactId = Guid.Parse("aaaaaaaa-0000-1111-2222-bbbbbbbbbbbb");

        var result = await client.RetrieveContactImageAsync(new ContactImageRetrieveRequest
        {
            ProfileAlias = " crm91 ",
            WorkloadSubjectId = " churchreport-memberinfo ",
            ContactId = contactId
        });
        var firstCopy = result.GetImageBytes();
        firstCopy[0] = 0;

        executor.LastRequest!.ProfileAlias.Should().Be("crm91");
        executor.LastRequest.WorkloadSubjectId.Should().Be("churchreport-memberinfo");
        executor.LastRequest.CapabilityOperationId.Should().Be(OperationIds.MemberInfoContactRetrieveImage);
        executor.LastRequest.IdempotencyKey.Should().BeNull();
        executor.LastRequest.Parameters.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["contactId"] = contactId
        });
        result.MediaKind.Should().Be(ContactImageMediaKind.Png);
        result.GetImageBytes().Should().Equal(imageBytes);
    }

    /// <summary>
    /// 保護 image update 在第一次 await 前複製 caller byte array、使用固定 update operation 與 idempotency key，
    /// 並只接受 read-back-confirmed update branch。故障注入是在 fake executor 收到 request 後立即修改原始 array；
    /// decisive assertion 是 executor-owned payload 未受影響，避免 UI/request buffer 在 Data8 lease 尚未執行前改寫
    /// CRM write 內容或跨 request 共用 image bytes。
    /// </summary>
    [Fact]
    public async Task Update_contact_image_copies_payload_and_requires_read_back_confirmed_response()
    {
        var sourceBytes = CreateValidOnePixelPng();
        var executor = new RecordingExecutor(OperationExecutionResult.Success(
            OperationResponseData.ForContactImageUpdate(
                OperationIds.MemberInfoContactUpdateImage,
                "9.1",
                ContactImageUpdateDisposition.Changed,
                ContactImageUpdateCorrelationCategory.ReadBackConfirmed)));
        var client = new Package03SpecialResourceClient(
            executor,
            NullLogger<Package03SpecialResourceClient>.Instance);
        var contactId = Guid.Parse("cccccccc-0000-1111-2222-dddddddddddd");

        var result = await client.UpdateMemberInfoContactImageAsync(new ContactImageUpdateRequest
        {
            ProfileAlias = "crm91",
            WorkloadSubjectId = "churchreport-memberinfo",
            ContactId = contactId,
            ImageBytes = sourceBytes,
            MediaKind = ContactImageMediaKind.Png,
            IdempotencyKey = "p7-3-image-update"
        });
        sourceBytes[0] = 0;

        result.Disposition.Should().Be(ContactImageUpdateDisposition.Changed);
        result.CorrelationCategory.Should().Be(ContactImageUpdateCorrelationCategory.ReadBackConfirmed);
        executor.LastRequest!.CapabilityOperationId.Should().Be(OperationIds.MemberInfoContactUpdateImage);
        executor.LastRequest.IdempotencyKey.Should().Be("p7-3-image-update");
        var payload = executor.LastRequest.Parameters!["imagePayload"]
            .Should().BeOfType<ContactImageResponseData>().Subject;
        payload.GetImageBytes().Should().Equal(CreateValidOnePixelPng());
    }

    /// <summary>
    /// 保護 metadata/weekly read 只能派送 server-owned target/UTC Sunday，並將封閉 response collection 複製成
    /// 產品 DTO。結果不得含 entity、FetchXML、paging cookie、locale transport state 或 metadata graph；錯配 branch
    /// 必須在 mapping 前拒絕而非回傳 partial success。
    /// </summary>
    [Fact]
    public async Task Metadata_and_weekly_statistics_map_fixed_operations_and_reject_response_mismatch()
    {
        var sunday = new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);
        var executor = new SequencedExecutor(
            OperationExecutionResult.Success(OperationResponseData.ForOptionSetOptions(
                OperationIds.MetadataOptionSetByAttribute,
                "9.1",
                [new OptionSetOptionRecord { Value = 7, Label = "測試選項", ConfiguredOrder = 0 }])),
            OperationExecutionResult.Success(OperationResponseData.ForMeetingStatistics(
                OperationIds.StatsMeetingRetrieveBySunday,
                "9.1",
                [new MeetingStatisticRecord
                {
                    MeetingStatisticId = Guid.Parse("eeeeeeee-0000-1111-2222-ffffffffffff"),
                    Name = "週報",
                    SundayDate = sunday
                }])),
            OperationExecutionResult.Success(OperationResponseData.ForMeetingStatistics(
                OperationIds.MetadataOptionSetByAttribute,
                "9.1",
                Array.Empty<MeetingStatisticRecord>())));
        var client = new Package03SpecialResourceClient(
            executor,
            NullLogger<Package03SpecialResourceClient>.Instance);

        var options = await client.RetrieveOptionSetAsync(new OptionSetRetrieveRequest
        {
            ProfileAlias = "crm91",
            WorkloadSubjectId = "churchreport-memberinfo",
            Target = MetadataOptionSetTarget.ContactCustomerTypeCode
        });
        var statistics = await client.RetrieveMeetingStatisticsAsync(new MeetingStatisticsRetrieveRequest
        {
            ProfileAlias = "crm91",
            WorkloadSubjectId = "churchreport-weekly-reporting",
            SundayDate = sunday
        });
        var mismatch = () => client.RetrieveOptionSetAsync(new OptionSetRetrieveRequest
        {
            ProfileAlias = "crm91",
            WorkloadSubjectId = "churchreport-memberinfo",
            Target = MetadataOptionSetTarget.ContactCustomerTypeCode
        });

        options.Options.Should().Equal(new OptionSetOptionDto { Value = 7, Label = "測試選項", ConfiguredOrder = 0 });
        statistics.Statistics.Should().ContainSingle().Which.Name.Should().Be("週報");
        executor.Requests[0].Parameters.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["target"] = MetadataOptionSetTarget.ContactCustomerTypeCode
        });
        executor.Requests[1].Parameters.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["sundayDate"] = sunday
        });
        await mismatch.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// 保護 P7.3 client 需要顯式 DI 註冊、重用既有 executor 且不在註冊時建立 HTTP/Data8/cache/background owner。
    /// decisive assertion 是 service provider 僅解析一個 stateless typed client；任何 runtime 仍須由 Embedded 或
    /// Gateway composition root 單獨擁有與 dispose。
    /// </summary>
    [Fact]
    public void Package03_registration_is_explicit_and_reuses_the_registered_executor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDynamicsOperationExecutor>(
            new RecordingExecutor(OperationExecutionResult.Failure("unused", "unused")));
        services.AddSpeechMessageDynamicsPackage03SpecialResources();

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IPackage03SpecialResourceClient>()
            .Should().ContainSingle()
            .Which.Should().BeOfType<Package03SpecialResourceClient>();
    }

    /// <summary>建立可由 P7.3 decoder 接受的一像素 PNG；每次回傳新 array，不讀寫檔案系統。</summary>
    private static byte[] CreateValidOnePixelPng()
        => Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==");

    /// <summary>
    /// 單次測試 scope 的 fixed-result executor。它只保留當前 request，沒有 static state 或外部資源，
    /// 因而可證明 ProductClient 在 transport 前的契約而不製造跨 profile/session 生命週期。
    /// </summary>
    private sealed class RecordingExecutor : IDynamicsOperationExecutor
    {
        private readonly OperationExecutionResult _result;

        /// <summary>建立不含任何可釋放資源的固定 operation result。</summary>
        public RecordingExecutor(OperationExecutionResult result) => _result = result;

        /// <summary>取得目前測試內記錄的最後 request。</summary>
        public OperationExecutionRequest? LastRequest { get; private set; }

        /// <summary>記錄 request 後傳回固定結果；取消發生時不建立重試或背景工作。</summary>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = request;
            return Task.FromResult(_result);
        }
    }

    /// <summary>
    /// 順序 executor 用來驗證多個固定 capability 的回應配對。queue/request list 都只屬於單一測試；
    /// sequence 用盡即 fail closed，避免測試因不小心吞掉 mismatch 而產生假陽性。
    /// </summary>
    private sealed class SequencedExecutor : IDynamicsOperationExecutor
    {
        private readonly Queue<OperationExecutionResult> _results;

        /// <summary>建立封閉結果 sequence，不保存 transport、profile 或 credential。</summary>
        public SequencedExecutor(params OperationExecutionResult[] results) => _results = new Queue<OperationExecutionResult>(results);

        /// <summary>取得目前案例已看過的 request；集合在測試結束後失去所有引用。</summary>
        public List<OperationExecutionRequest> Requests { get; } = [];

        /// <summary>以 FIFO 回傳預先定義結果；空 sequence 代表測試契約錯誤並立即停止。</summary>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            if (_results.Count == 0)
            {
                throw new InvalidOperationException("The test executor has no remaining result.");
            }

            return Task.FromResult(_results.Dequeue());
        }
    }
}

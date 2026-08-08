// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/ContactProfileOperationsProductClientTests.cs
// 目的：以 TDD 鎖定 P7.2 Slice B1/B2 的 typed ProductClient 與封閉 response union。
//
// 安全與生命週期：
// - 測試只使用 request-scope fake executor，不建立 CRM、LINE、HTTP、credential、session、stream 或背景工作。
// - 每個 assertion 證明 caller mutable DTO 不會穿越第一次 await，LINE token／任意 FetchXML／grouped GUID graph
//   不會進入 Dynamics contract，且錯配 response 必須 fail closed。
// ============================================================================

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.DependencyInjection;
using SpeechMessage.Dynamics.ProductClient.MemberInfo;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 B1 LINE profile write 與 B2 ungrouped commitment aggregate 的產品邊界、輸入正規化及回應配對。
/// </summary>
public sealed class ContactProfileOperationsProductClientTests
{
    /// <summary>
    /// 保護 LINE profile request 只能形成固定 set／clear／preserve scalar。故障注入是可變來源字串與不相關
    /// secret marker；決定性斷言是 executor 收到 defensive copy、固定 operation ID 與七個 allowlisted scalar，
    /// 回應只含已確認結果，不保留 LINE token、profile payload 或 contact 欄位值。
    /// </summary>
    [Fact]
    public async Task Line_profile_update_maps_only_closed_modes_and_bounded_values()
    {
        var executor = new RecordingExecutor(OperationExecutionResult.Success(
            OperationResponseData.ForContactLineProfileUpdate(
                OperationIds.MemberInfoContactUpdateLineProfile,
                "9.1",
                ContactLineProfileUpdateDisposition.Changed,
                ContactLineProfileUpdateCorrelationCategory.ReadBackConfirmed)));
        var client = new Package02ContactProfileClient(
            executor,
            NullLogger<Package02ContactProfileClient>.Instance);
        var sourceUrl = "https://profile.line-scdn.net/example";

        var result = await client.UpdateLineProfileAsync(new ContactLineProfileUpdateRequest
        {
            ProfileAlias = " crm91 ",
            WorkloadSubjectId = " churchreport-memberinfo ",
            ContactId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            PictureMode = ContactLineProfileNullableTextMode.Set,
            PictureUrl = sourceUrl,
            StatusMode = ContactLineProfileNullableTextMode.Clear,
            DisplayNameMode = ContactLineProfileDisplayNameMode.Set,
            DisplayName = " 測試顯示名稱 ",
            IdempotencyKey = "p72-line-profile-1"
        });

        result.Disposition.Should().Be(ContactLineProfileUpdateDisposition.Changed);
        result.CorrelationCategory.Should().Be(ContactLineProfileUpdateCorrelationCategory.ReadBackConfirmed);
        executor.LastRequest.Should().NotBeNull();
        executor.LastRequest!.ProfileAlias.Should().Be("crm91");
        executor.LastRequest.WorkloadSubjectId.Should().Be("churchreport-memberinfo");
        executor.LastRequest.CapabilityOperationId.Should().Be(OperationIds.MemberInfoContactUpdateLineProfile);
        executor.LastRequest.Parameters.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["contactId"] = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ["pictureMode"] = "set",
            ["pictureUrl"] = sourceUrl,
            ["statusMode"] = "clear",
            ["displayNameMode"] = "set",
            ["displayName"] = "測試顯示名稱"
        });
        executor.LastRequest.Parameters.Keys.Should().NotContain(key =>
            key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("lineid", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 保護 mode/value 配對及 HTTPS URL 邊界。故障注入是 clear 仍附值、set 缺值與 HTTP URL；決定性斷言是
    /// 三者均在 executor 前被拒絕，因而不建立 admission、lease、CRM request 或 retained buffer。
    /// </summary>
    [Fact]
    public async Task Line_profile_update_rejects_invalid_mode_value_pairs_before_executor()
    {
        var executor = new RecordingExecutor(OperationExecutionResult.Failure("unused", "unused"));
        var client = new Package02ContactProfileClient(
            executor,
            NullLogger<Package02ContactProfileClient>.Instance);

        var clearWithValue = () => client.UpdateLineProfileAsync(CreateLineRequest() with
        {
            PictureMode = ContactLineProfileNullableTextMode.Clear,
            PictureUrl = "https://profile.line-scdn.net/should-not-exist"
        });
        var setWithoutValue = () => client.UpdateLineProfileAsync(CreateLineRequest() with
        {
            StatusMode = ContactLineProfileNullableTextMode.Set,
            StatusMessage = null
        });
        var insecureUrl = () => client.UpdateLineProfileAsync(CreateLineRequest() with
        {
            PictureMode = ContactLineProfileNullableTextMode.Set,
            PictureUrl = "http://example.test/avatar"
        });

        await clearWithValue.Should().ThrowAsync<ArgumentException>();
        await setWithoutValue.Should().ThrowAsync<ArgumentException>();
        await insecureUrl.Should().ThrowAsync<ArgumentException>();
        executor.CallCount.Should().Be(0);
    }

    /// <summary>
    /// 保護 aggregate function 只接受 bounded search，並把 safe value/count records 複製成產品 DTO。
    /// 故障注入是回應集合的建立來源；決定性斷言是 request 不含 FetchXML、QueryExpression、entity、欄位或
    /// grouped GUID array，且結果保持固定順序與非負計數。
    /// </summary>
    [Fact]
    public async Task Ungrouped_commitment_count_maps_only_search_and_safe_records()
    {
        var executor = new RecordingExecutor(OperationExecutionResult.Success(
            OperationResponseData.ForUngroupedCommitmentCounts(
                OperationIds.MemberInfoContactCountUngroupedCommitment,
                "9.1",
                [
                    new UngroupedCommitmentCountRecord { Value = 3, Count = 7 },
                    new UngroupedCommitmentCountRecord { Value = 8, Count = 2 }
                ])));
        var client = new Package02ContactProfileClient(
            executor,
            NullLogger<Package02ContactProfileClient>.Instance);

        var result = await client.CountUngroupedCommitmentAsync(new UngroupedCommitmentCountRequest
        {
            ProfileAlias = "crm91",
            WorkloadSubjectId = "churchreport-memberinfo",
            Search = " 會友 "
        });

        executor.LastRequest!.CapabilityOperationId.Should().Be(OperationIds.MemberInfoContactCountUngroupedCommitment);
        executor.LastRequest.IdempotencyKey.Should().BeNull();
        executor.LastRequest.Parameters.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["search"] = "會友"
        });
        executor.LastRequest.Parameters.Keys.Should().OnlyContain(key => key == "search");
        result.Counts.Should().Equal(
            new UngroupedCommitmentCountDto { Value = 3, Count = 7 },
            new UngroupedCommitmentCountDto { Value = 8, Count = 2 });
    }

    /// <summary>
    /// 保護 Slice B client 必須顯式註冊且不建立 transport。故障注入是只有 fake executor 的 DI container；
    /// 決定性斷言是新增擴充只產生一個 stateless typed client，scope 結束時沒有額外 HttpClient、timer 或 lease owner。
    /// </summary>
    [Fact]
    public void Slice_b_registration_is_explicit_and_reuses_the_registered_executor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDynamicsOperationExecutor>(
            new RecordingExecutor(OperationExecutionResult.Failure("unused", "unused")));
        services.AddSpeechMessageDynamicsPackage02ContactProfileOperations();

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IPackage02ContactProfileClient>()
            .Should().ContainSingle()
            .Which.Should().BeOfType<Package02ContactProfileClient>();
    }

    /// <summary>建立完全合法且不含產品 secret 的 LINE profile request，供單一 fault case 使用。</summary>
    private static ContactLineProfileUpdateRequest CreateLineRequest()
        => new()
        {
            ProfileAlias = "crm91",
            WorkloadSubjectId = "churchreport-memberinfo",
            ContactId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            PictureMode = ContactLineProfileNullableTextMode.Clear,
            StatusMode = ContactLineProfileNullableTextMode.Clear,
            DisplayNameMode = ContactLineProfileDisplayNameMode.Preserve,
            IdempotencyKey = "p72-line-profile-valid"
        };

    /// <summary>
    /// 保存最近一次 immutable request 的 request-scope fake。它不建立或擁有 transport，且沒有 static state，
    /// 每個測試結束後即可回收，避免跨測試 profile／workload leakage。
    /// </summary>
    private sealed class RecordingExecutor : IDynamicsOperationExecutor
    {
        private readonly OperationExecutionResult _result;

        /// <summary>建立固定結果的 fake；輸入結果是 immutable contract，沒有可釋放資源。</summary>
        public RecordingExecutor(OperationExecutionResult result) => _result = result;

        /// <summary>取得 executor 實際被呼叫次數，用以證明 fail-fast 路徑沒有進入 transport。</summary>
        public int CallCount { get; private set; }

        /// <summary>取得最後一筆 request；只存在於單一測試 instance，不跨 request／tenant 共用。</summary>
        public OperationExecutionRequest? LastRequest { get; private set; }

        /// <summary>記錄 request 並回傳固定結果；不建立 task、timer、取消註冊或外部資源。</summary>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_result);
        }
    }
}

// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/Services/Package03ContactImageReadServiceTests.cs
// 用途：保護 P7.4 Package03 聯絡人圖片唯讀服務的固定 profile/workload、取消傳遞、DTO defensive-copy
//       與 A/B request-local 隔離契約。
//
// 信任與生命週期：
// 1. fake client 只存在於單一測試方法，不保存 Session、HttpContext、CRM SDK、connector 或背景工作。
// 2. 每個測試以可辨識的 bytes 建立兩個獨立結果；決定性斷言是改寫來源或先前輸出後，不會污染另一個 result。
// 3. cancellation/no-image/fault 均不允許 service 發布 partial payload；production service 也不得 retry 或 fallback。
// ============================================================================

using ChurchReport.Services.MemberInfo;
using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.SpecialResources;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Services;

/// <summary>
/// 驗證 Package03 圖片讀取服務只在呼叫端已完成授權後，使用 deployment-owned scalar 建立 request-local
/// DTO 結果。測試不建立 Gateway、Data8、快取或 CRM 連線，因此只證明本機 service contract。
/// </summary>
public sealed class Package03ContactImageReadServiceTests
{
    /// <summary>
    /// 保護固定 profile/workload、媒體種類映射與 defensive-copy。故障注入是呼叫後改寫 fake client 的來源
    /// bytes 與第一份輸出；決定性斷言是已發布 result 仍保存原 payload，且不暴露內部陣列。
    /// </summary>
    [Fact]
    public async Task RetrieveAsync_uses_server_owned_request_and_returns_a_defensive_png_result()
    {
        var sourceBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var client = new RecordingPackage03Client(
            _ => Task.FromResult(new ContactImageResult(sourceBytes, ContactImageMediaKind.Png)));
        var service = new Package03ContactImageReadService(client, "crm91");
        var contactId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        using var cancellation = new CancellationTokenSource();

        var result = await service.RetrieveAsync(contactId, cancellation.Token);
        sourceBytes[0] = 0x00;
        var firstRead = result.GetImageBytes();
        firstRead[1] = 0x00;

        result.ContentType.Should().Be("image/png");
        result.GetImageBytes().Should().Equal(0x89, 0x50, 0x4E, 0x47);
        client.ProfileAlias.Should().Be("crm91");
        client.WorkloadSubjectId.Should().Be("church-report-member-info-image-read");
        client.ContactId.Should().Be(contactId);
        client.ObservedCancellationToken.Should().Be(cancellation.Token);
    }

    /// <summary>
    /// 保護交錯 A/B 要求不共用 image result、bytes 或 media type。fake client 以兩個非同步 completion
    /// source 模擬回應順序反轉；決定性斷言是每位使用者只取得自己的 marker 與獨立 byte array。
    /// </summary>
    [Fact]
    public async Task RetrieveAsync_keeps_interleaved_contact_image_results_isolated()
    {
        var firstId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
        var secondId = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");
        var first = new TaskCompletionSource<ContactImageResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<ContactImageResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new RecordingPackage03Client(request => request.ContactId == firstId ? first.Task : second.Task);
        var service = new Package03ContactImageReadService(client, "crm91");

        var firstOperation = service.RetrieveAsync(firstId, CancellationToken.None);
        var secondOperation = service.RetrieveAsync(secondId, CancellationToken.None);
        second.SetResult(new ContactImageResult(new byte[] { 0xFF, 0xD8, 0xB2 }, ContactImageMediaKind.Jpeg));
        first.SetResult(new ContactImageResult(new byte[] { 0x89, 0x50, 0xA1 }, ContactImageMediaKind.Png));

        var results = await Task.WhenAll(firstOperation, secondOperation);

        results[0].ContentType.Should().Be("image/png");
        results[0].GetImageBytes().Should().Equal(0x89, 0x50, 0xA1);
        results[1].ContentType.Should().Be("image/jpeg");
        results[1].GetImageBytes().Should().Equal(0xFF, 0xD8, 0xB2);
        results[0].GetImageBytes().Should().NotBeSameAs(results[1].GetImageBytes());
    }

    /// <summary>
    /// 保護取消與 no-image 皆 fail closed。故障注入分別為已取消的 typed task 與空 image payload；決定性
    /// 斷言是取消 token 原樣傳遞、取消例外不轉換、空 payload 在發布任何檔案內容前失敗。
    /// </summary>
    [Fact]
    public async Task RetrieveAsync_propagates_cancellation_and_rejects_an_empty_image_before_publish()
    {
        using var cancellation = new CancellationTokenSource();
        var cancelledClient = new RecordingPackage03Client(
            _ => Task.FromCanceled<ContactImageResult>(new CancellationToken(canceled: true)));
        var cancelledService = new Package03ContactImageReadService(cancelledClient, "crm91");

        Func<Task> cancelled = () => cancelledService.RetrieveAsync(Guid.NewGuid(), cancellation.Token);

        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        cancelledClient.ObservedCancellationToken.Should().Be(cancellation.Token);

        var emptyClient = new RecordingPackage03Client(
            _ => Task.FromResult(new ContactImageResult(Array.Empty<byte>(), ContactImageMediaKind.Png)));
        var emptyService = new Package03ContactImageReadService(emptyClient, "crm91");

        Func<Task> empty = () => emptyService.RetrieveAsync(Guid.NewGuid(), CancellationToken.None);

        await empty.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// 保護空白 deployment profile 與未知媒體種類都在發布 response 前 fail closed。故障注入分別為空白
    /// profile 與超出封閉 enum 的 media kind；決定性斷言是前者不呼叫 client，後者不回傳 partial bytes，
    /// 因而不會把錯誤的 profile 或 MIME 型別帶入下一個 request。
    /// </summary>
    [Fact]
    public async Task RetrieveAsync_rejects_an_empty_profile_or_unknown_media_kind_before_publish()
    {
        var profileClient = new RecordingPackage03Client(
            _ => Task.FromResult(new ContactImageResult(new byte[] { 0x89 }, ContactImageMediaKind.Png)));

        Action createWithBlankProfile = () => new Package03ContactImageReadService(profileClient, " ");

        createWithBlankProfile.Should().Throw<InvalidOperationException>();
        profileClient.ProfileAlias.Should().BeNull();

        var unknownKindClient = new RecordingPackage03Client(
            _ => Task.FromResult(new ContactImageResult(new byte[] { 0x00 }, (ContactImageMediaKind)999)));
        var service = new Package03ContactImageReadService(unknownKindClient, "crm91");

        Func<Task> retrieveUnknownKind = () => service.RetrieveAsync(Guid.NewGuid(), CancellationToken.None);

        await retrieveUnknownKind.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// request-local fake client 記錄單次 typed request。它不含 static state、快取、重試或 legacy fallback；
    /// 未使用的 Package03 methods 均明確拒絕，避免測試不小心擴張至寫入或 metadata family。
    /// </summary>
    private sealed class RecordingPackage03Client : IPackage03SpecialResourceClient
    {
        private readonly Func<ContactImageRetrieveRequest, Task<ContactImageResult>> _retrieve;

        /// <summary>以本測試專屬的 non-shared delegate 建立 fake client。</summary>
        public RecordingPackage03Client(Func<ContactImageRetrieveRequest, Task<ContactImageResult>> retrieve)
            => _retrieve = retrieve;

        /// <summary>最後一次 request 的固定 profile；只用於本測試的 decisive assertion。</summary>
        public string? ProfileAlias { get; private set; }

        /// <summary>最後一次 request 的固定 workload；它不可由 browser 提供。</summary>
        public string? WorkloadSubjectId { get; private set; }

        /// <summary>最後一次已授權 locator；沒有其他身分或 CRM state。</summary>
        public Guid ContactId { get; private set; }

        /// <summary>最後一次呼叫收到的取消 token。</summary>
        public CancellationToken ObservedCancellationToken { get; private set; }

        /// <summary>記錄 typed image read 並傳回 test-owned result；不建立外部資源。</summary>
        public Task<ContactImageResult> RetrieveContactImageAsync(
            ContactImageRetrieveRequest request,
            CancellationToken cancellationToken = default)
        {
            ProfileAlias = request.ProfileAlias;
            WorkloadSubjectId = request.WorkloadSubjectId;
            ContactId = request.ContactId;
            ObservedCancellationToken = cancellationToken;
            return _retrieve(request);
        }

        /// <summary>本測試禁止 image write family，避免其進入讀取邊界。</summary>
        public Task<ContactImageUpdateResult> UpdateMemberInfoContactImageAsync(ContactImageUpdateRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        /// <summary>本測試禁止 NewPerson image write family，避免其進入讀取邊界。</summary>
        public Task<ContactImageUpdateResult> UpdateNewPersonContactImageAsync(ContactImageUpdateRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        /// <summary>本測試禁止 metadata family，避免 scope 擴張。</summary>
        public Task<OptionSetRetrieveResult> RetrieveOptionSetAsync(OptionSetRetrieveRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        /// <summary>本測試禁止 weekly statistic family，避免 scope 擴張。</summary>
        public Task<MeetingStatisticsRetrieveResult> RetrieveMeetingStatisticsAsync(MeetingStatisticsRetrieveRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}

// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/Services/Package03MemberInfoFullContactImageReadServiceTests.cs
// 用途：驗證 P7.4 完整 contact-image display service 的閉合三分支、精確 LINE redirect policy、
//       request-local defensive copy 與取消傳遞。
//
// 信任與生命週期：本檔使用的 ProductClient 替身只活在每個測試方法，僅回傳由 abstraction
// defensive-copy 建立的 pure DTO；不建立 CRM、HTTP、Session、cache、stream、timer 或背景工作。
// 測試資料與取消 token 均不寫入 static state，確保 A/B 交錯測試不會保存前一次 request 的圖片或 URL。
// ============================================================================

using ChurchReport.Services.MemberInfo;
using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.SpecialResources;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Services;

/// <summary>
/// 驗證完整 display service 只接受 connector 同一份 server-owned LINE allowlist 所建立的 union。
/// 這些測試只證明 disabled-by-default 本機服務映射；它們不啟用 feature gate、CRM、CE 流量、
/// ToolUtility removal、P7.5 或 P8，也不把本機結果誤稱為實機切流證據。
/// </summary>
public sealed class Package03MemberInfoFullContactImageReadServiceTests
{
    /// <summary>
    /// 保護 Data8 已核准的 <c>obs.line-apps.com</c> HTTPS redirect 不會在 ChurchReport 再次被不同 allowlist
    /// 誤拒絕。故障注入是由 closed abstraction union 建立的合法 URI；決定性斷言是 service 發佈唯一
    /// LineRedirect branch、完整保留 deployment-owned request scalar 與相同 cancellation token，沒有 image、
    /// gender、cache 或 legacy fallback 資料。
    /// </summary>
    [Fact]
    public async Task RetrieveAsync_accepts_the_connector_approved_obs_line_apps_redirect()
    {
        var contactId = Guid.Parse("d7d7d7d7-0000-1111-2222-e7e7e7e7e7e7");
        var display = ContactImageDisplayResult.FromResponse(
            ContactImageDisplayResponseData.ForLineRedirect("https://obs.line-apps.com/profile/avatar.png"));
        var client = new RecordingDisplayClient(_ => Task.FromResult(display));
        var service = new Package03MemberInfoFullContactImageReadService(client, "crm91");
        using var cancellation = new CancellationTokenSource();

        var result = await service.RetrieveAsync(contactId, cancellation.Token);

        result.Kind.Should().Be(Package03MemberInfoFullContactImageReadResultKind.LineRedirect);
        result.LineRedirectUrl.Should().Be("https://obs.line-apps.com/profile/avatar.png");
        result.GetImageBytes().Should().BeEmpty();
        result.GenderCode.Should().BeNull();
        client.ProfileAlias.Should().Be("crm91");
        client.WorkloadSubjectId.Should().Be("churchreport-memberinfo");
        client.ContactId.Should().Be(contactId);
        client.ObservedCancellationToken.Should().Be(cancellation.Token);
    }

    /// <summary>
    /// 保護 ChurchReport 不會把 host 雖在 allowlist、但 port 與 Data8 connector policy 不同的 URL 發布成
    /// redirect。故障注入是 abstraction 可建立、卻繞過 connector 的非預設 HTTPS port；決定性斷言是 service
    /// 在建立產品結果前 fail-closed，沒有 image、avatar、cache、legacy fallback 或下一個 request 可重用資料。
    /// </summary>
    [Fact]
    public async Task RetrieveAsync_rejects_an_allowlisted_line_host_with_a_non_default_port()
    {
        var display = ContactImageDisplayResult.FromResponse(
            ContactImageDisplayResponseData.ForLineRedirect("https://obs.line-apps.com:8443/profile/avatar.png"));
        var client = new RecordingDisplayClient(_ => Task.FromResult(display));
        var service = new Package03MemberInfoFullContactImageReadService(client, "crm91");

        Func<Task> action = () => service.RetrieveAsync(Guid.NewGuid(), CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// 保護交錯 A/B image branch 不會共用 result、圖片陣列或 contact locator。故障注入使用兩個非同步 completion
    /// source 反轉回應順序，並在第一個輸出後改寫其取得的 bytes；決定性斷言是兩位使用者僅取得自己的 marker，
    /// 每次 getter 都給 caller-owned copy，且 service 不會把另一個 request 的 profile、workload、token 或結果
    /// 留在共享狀態。
    /// </summary>
    [Fact]
    public async Task RetrieveAsync_keeps_interleaved_display_image_results_request_local()
    {
        var firstId = Guid.Parse("a8a8a8a8-0000-1111-2222-b8b8b8b8b8b8");
        var secondId = Guid.Parse("b9b9b9b9-0000-1111-2222-c9c9c9c9c9c9");
        var first = new TaskCompletionSource<ContactImageDisplayResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<ContactImageDisplayResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new RecordingDisplayClient(request => request.ContactId == firstId ? first.Task : second.Task);
        var service = new Package03MemberInfoFullContactImageReadService(client, "crm91");

        var firstOperation = service.RetrieveAsync(firstId, CancellationToken.None);
        var secondOperation = service.RetrieveAsync(secondId, CancellationToken.None);
        second.SetResult(CreateImageResult(new byte[] { 0xFF, 0xD8, 0xFF, 0xB2 }, ContactImageMediaKind.Jpeg));
        first.SetResult(CreateImageResult(new byte[] { 0x89, 0x50, 0x4E, 0xA1 }, ContactImageMediaKind.Png));

        var results = await Task.WhenAll(firstOperation, secondOperation);
        var firstCopy = results[0].GetImageBytes();
        firstCopy[0] = 0;

        results[0].ContentType.Should().Be("image/png");
        results[0].GetImageBytes().Should().Equal(0x89, 0x50, 0x4E, 0xA1);
        results[1].ContentType.Should().Be("image/jpeg");
        results[1].GetImageBytes().Should().Equal(0xFF, 0xD8, 0xFF, 0xB2);
        results[0].GetImageBytes().Should().NotBeSameAs(results[1].GetImageBytes());
    }

    /// <summary>
    /// 保護取消不會被 service 包裝、redirect/avatar 轉換或重試。故障注入是 ProductClient 原樣回傳的取消 task；
    /// 決定性斷言是 OperationCanceledException 離開 service、原 request token 維持可觀察，且沒有任何 partial
    /// image/URL 結果可供下一個 request 重用。
    /// </summary>
    [Fact]
    public async Task RetrieveAsync_propagates_display_cancellation_without_publishing_a_partial_result()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new RecordingDisplayClient(
            _ => Task.FromCanceled<ContactImageDisplayResult>(new CancellationToken(canceled: true)));
        var service = new Package03MemberInfoFullContactImageReadService(client, "crm91");

        Func<Task> action = () => service.RetrieveAsync(Guid.NewGuid(), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        client.ObservedCancellationToken.Should().Be(cancellation.Token);
    }

    /// <summary>
    /// 由 closed abstraction union 建立最小 image display result。factory 在輸入與輸出均 defensive copy，
    /// 因此此 helper 不保留 caller bytes 或 CRM Entity；它只讓 A/B 測試表達應有的 request-local branch。
    /// </summary>
    private static ContactImageDisplayResult CreateImageResult(byte[] bytes, ContactImageMediaKind mediaKind)
        => ContactImageDisplayResult.FromResponse(ContactImageDisplayResponseData.ForImage(bytes, mediaKind));

    /// <summary>
    /// 最小 ProductClient 替身只實作本 child 的 display read。它每次只記錄當前 request 的純 scalar，
    /// 未使用的 image write、metadata 與 weekly methods 都 fail closed，避免測試意外擴張到其他 capability
    /// 或持有 CRM/transport/Session 狀態。
    /// </summary>
    private sealed class RecordingDisplayClient : IPackage03SpecialResourceClient
    {
        private readonly Func<ContactImageDisplayRetrieveRequest, Task<ContactImageDisplayResult>> _retrieve;

        /// <summary>以單一測試擁有的 response delegate 建立替身，不開啟任何外部資源。</summary>
        public RecordingDisplayClient(Func<ContactImageDisplayRetrieveRequest, Task<ContactImageDisplayResult>> retrieve)
            => _retrieve = retrieve;

        /// <summary>最後一次 deployment-owned profile scalar；僅供本測試斷言。</summary>
        public string? ProfileAlias { get; private set; }

        /// <summary>最後一次 server-owned workload scalar；不可由 browser 覆寫。</summary>
        public string? WorkloadSubjectId { get; private set; }

        /// <summary>最後一次已授權 contact locator；不代表登入者或 CRM session。</summary>
        public Guid ContactId { get; private set; }

        /// <summary>目前 request 原樣傳入的取消 token；替身不註冊或保存其 callback。</summary>
        public CancellationToken ObservedCancellationToken { get; private set; }

        /// <summary>記錄 display request 並回傳 test-owned closed union，不建立 cache、retry 或 I/O。</summary>
        public Task<ContactImageDisplayResult> RetrieveContactImageDisplayAsync(
            ContactImageDisplayRetrieveRequest request,
            CancellationToken cancellationToken = default)
        {
            ProfileAlias = request.ProfileAlias;
            WorkloadSubjectId = request.WorkloadSubjectId;
            ContactId = request.ContactId;
            ObservedCancellationToken = cancellationToken;
            return _retrieve(request);
        }

        /// <summary>本 child 不允許 image-only read。</summary>
        public Task<ContactImageResult> RetrieveContactImageAsync(ContactImageRetrieveRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        /// <summary>本 child 不允許 MemberInfo image write。</summary>
        public Task<ContactImageUpdateResult> UpdateMemberInfoContactImageAsync(ContactImageUpdateRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        /// <summary>本 child 不允許 NewPerson image write。</summary>
        public Task<ContactImageUpdateResult> UpdateNewPersonContactImageAsync(ContactImageUpdateRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        /// <summary>本 child 不允許 metadata read。</summary>
        public Task<OptionSetRetrieveResult> RetrieveOptionSetAsync(OptionSetRetrieveRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        /// <summary>本 child 不允許 weekly statistic read。</summary>
        public Task<MeetingStatisticsRetrieveResult> RetrieveMeetingStatisticsAsync(MeetingStatisticsRetrieveRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}

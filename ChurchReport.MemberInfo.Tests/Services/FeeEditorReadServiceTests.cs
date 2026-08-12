// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/Services/FeeEditorReadServiceTests.cs
// 目的：保護 ORG-CALL-00066 唯讀 DTO service 的 operation、取消、完整性與跨請求隔離契約。
//
// 測試邊界：本檔使用每個測試私有的 fake ProductClient，不建立 CRM、Gateway host、HTTP、
// Session、cache、timer、背景工作或可重用連線。故障注入是 mismatch/null/cancelled response；
// 決定性斷言是 service 只在所有 row 都通過 target match 後才建立新的不可變 result，避免
// partial response 或 A/B request 透過集合、DTO、profile 或取消 state 交叉洩漏。
// ============================================================================

using System.Collections;
using ChurchReport.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.ProductClient.FeeReads;
using SpeechMessage.Dynamics.ProductClient.Models;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Services;

/// <summary>
/// 驗證 <see cref="FeeEditorReadService"/> 僅使用固定的 Package01 fee-editor operation。
/// fake 的所有觀察值與資料都侷限在單一測試 instance，確保測試可檢查 service 的 request-local
/// 行為而不是依賴 static state；測試結束時沒有 disposable resource 或 callback 要清理。
/// </summary>
public sealed class FeeEditorReadServiceTests
{
    /// <summary>
    /// 成功讀取必須使用 server composition 提供的 profile、固定 workload 與精確 target，並把目前
    /// request token 原樣交給唯一 typed operation。測試也驗證 result 對 source row 做 defensive copy，
    /// 所以 client 後續修改自己的 array 不會改寫已授權且即將序列化的 response。
    /// </summary>
    [Fact]
    public async Task Typed_fee_editor_read_uses_fixed_operation_and_publishes_a_defensive_immutable_result()
    {
        var target = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var source = new[]
        {
            new StorLessonRecordDto
            {
                StorLessonId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                ContactId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                DiscipleLessonId = target,
                ContactName = "A-測試資料",
                DiscipleLessonName = "A-課程",
                FeeAmount = 123m
            }
        };
        var client = new FeeEditorReadClient(_ => Task.FromResult<IReadOnlyList<StorLessonRecordDto>>(source));
        var service = new FeeEditorReadService(
            client,
            Options.Create(new ProductDynamicsOptions { ProfileAlias = "server-owned-profile" }));
        using var cancellation = new CancellationTokenSource();

        var result = await service.RetrieveAsync(target, cancellation.Token);
        source[0] = new StorLessonRecordDto
        {
            DiscipleLessonId = target,
            ContactName = "被替換的來源"
        };

        client.RequestCount.Should().Be(1);
        client.ObservedProfileAlias.Should().Be("server-owned-profile");
        client.ObservedWorkloadSubject.Should().Be("church-report-service");
        client.ObservedLessonId.Should().Be(target);
        client.ObservedCancellationToken.Should().Be(cancellation.Token);
        result.Rows.Should().ContainSingle().Which.ContactName.Should().Be("A-測試資料");
        result.Rows.Should().NotBeAssignableTo<StorLessonRecordDto[]>();
        ((IList)result.Rows).Invoking(rows => rows[0] = result.Rows[0])
            .Should().Throw<NotSupportedException>();
    }

    /// <summary>
    /// null 或另一門課程的 row 表示上游 response 不能精確歸屬目前已授權 target。service 必須在
    /// 建立任何 result 前 fail closed，而不是濾掉壞資料後發布 partial list、改走 legacy 或重試。
    /// fake 沒有 CRM 或背景資源，故例外路徑只驗證資料完整性與不發布契約。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Null_or_mismatched_upstream_lesson_row_is_rejected_without_partial_publication(bool hasNullLessonId)
    {
        var target = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var other = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var client = new FeeEditorReadClient(_ => Task.FromResult<IReadOnlyList<StorLessonRecordDto>>(
        [
            new StorLessonRecordDto { DiscipleLessonId = target, ContactName = "完整列" },
            new StorLessonRecordDto
            {
                DiscipleLessonId = hasNullLessonId ? null : other,
                ContactName = "不可發布列"
            }
        ]));
        var service = new FeeEditorReadService(
            client,
            Options.Create(new ProductDynamicsOptions { ProfileAlias = "server-owned-profile" }));

        Func<Task> operation = () => service.RetrieveAsync(target, CancellationToken.None);

        await operation.Should().ThrowAsync<InvalidOperationException>();
        client.RequestCount.Should().Be(1);
    }

    /// <summary>
    /// 已取消的 typed operation 必須原樣傳回，且不得將 cancellation 轉為 legacy retry、其他
    /// capability 或部分資料。決定性斷言是唯一費用編輯器呼叫仍收到同一 token；fake instance
    /// 在測試結束後可回收，不會把取消 registration 或 response 資料保留給其他案例。
    /// </summary>
    [Fact]
    public async Task Cancellation_is_forwarded_without_a_fallback_or_retry()
    {
        var target = Guid.Parse("88888888-8888-8888-8888-888888888888");
        using var cancellation = new CancellationTokenSource();
        var client = new FeeEditorReadClient(_ =>
            Task.FromCanceled<IReadOnlyList<StorLessonRecordDto>>(new CancellationToken(canceled: true)));
        var service = new FeeEditorReadService(
            client,
            Options.Create(new ProductDynamicsOptions { ProfileAlias = "server-owned-profile" }));

        Func<Task> operation = () => service.RetrieveAsync(target, cancellation.Token);

        await operation.Should().ThrowAsync<OperationCanceledException>();
        client.RequestCount.Should().Be(1);
        client.ObservedCancellationToken.Should().Be(cancellation.Token);
    }

    /// <summary>
    /// A/B 交錯讀取具有不同 target 與 marker 時，每次 invocation 都必須得到自己新建的 result、
    /// rows 與 row projection，不能以 static collection、client 的前一筆回應或 shared cache 混用。
    /// 此測試使用同一 service 以覆蓋 stateless coordinator 的真實 boundary；fake 僅在當前測試保存
    /// 已完成的 request-local rows，沒有外部資源或跨測試生命周期。
    /// </summary>
    [Fact]
    public async Task Interleaved_requests_receive_distinct_results_and_rows_without_cross_request_markers()
    {
        var targetA = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var targetB = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var client = new FeeEditorReadClient(target => Task.FromResult<IReadOnlyList<StorLessonRecordDto>>(
        [
            new StorLessonRecordDto
            {
                DiscipleLessonId = target,
                ContactName = target == targetA ? "A-資料" : "B-資料"
            }
        ]));
        var service = new FeeEditorReadService(
            client,
            Options.Create(new ProductDynamicsOptions { ProfileAlias = "server-owned-profile" }));

        var requestA = service.RetrieveAsync(targetA, CancellationToken.None);
        var requestB = service.RetrieveAsync(targetB, CancellationToken.None);
        var resultB = await requestB;
        var resultA = await requestA;

        resultA.Should().NotBeSameAs(resultB);
        resultA.Rows.Should().NotBeSameAs(resultB.Rows);
        resultA.Rows[0].Should().NotBeSameAs(resultB.Rows[0]);
        resultA.Rows[0].ContactName.Should().Be("A-資料");
        resultA.Rows[0].ContactName.Should().NotContain("B-");
        resultB.Rows[0].ContactName.Should().Be("B-資料");
        resultB.Rows[0].ContactName.Should().NotContain("A-");
    }

    /// <summary>
    /// 本測試私有的 typed client fake。除費用編輯器 operation 外的所有 capability 都立即失敗，
    /// 防止 service 的錯誤 fallback 被空結果掩蓋。觀察值不使用 static，且 fake 不建立連線、
    /// timer、queue 或 cancellation registration，因此唯一資源生命週期是目前測試的 managed object。
    /// </summary>
    private sealed class FeeEditorReadClient : IPackage01FeeReadClient
    {
        private readonly Func<Guid, Task<IReadOnlyList<StorLessonRecordDto>>> _retrieve;

        /// <summary>
        /// 建立只供目前測試使用的結果工廠；factory 由測試提供以安全模擬成功、mismatch 與取消。
        /// </summary>
        public FeeEditorReadClient(Func<Guid, Task<IReadOnlyList<StorLessonRecordDto>>> retrieve)
        {
            _retrieve = retrieve;
        }

        /// <summary>唯一允許 operation 的呼叫次數，用來偵測 retry 或 fallback。</summary>
        public int RequestCount { get; private set; }

        /// <summary>service 本次使用的 profile；僅供單一測試檢查，不跨 request 保存。</summary>
        public string? ObservedProfileAlias { get; private set; }

        /// <summary>service 固定傳遞的 workload subject；不能由 browser 輸入控制。</summary>
        public string? ObservedWorkloadSubject { get; private set; }

        /// <summary>service 傳遞的 lesson locator，必須等於已授權 target。</summary>
        public Guid ObservedLessonId { get; private set; }

        /// <summary>service 原樣轉交的 request cancellation token。</summary>
        public CancellationToken ObservedCancellationToken { get; private set; }

        /// <summary>
        /// 模擬精確的 fee-editor operation。每次呼叫只記錄本 request 的純值並交由測試 factory
        /// 回傳資料；它不根據 profile 或 workload 改選任何 CRM、endpoint 或 shared session。
        /// </summary>
        public Task<IReadOnlyList<StorLessonRecordDto>> RetrieveFeeEditorRowsByDiscipleLessonAsync(
            string profileAlias,
            string workloadSubjectId,
            Guid discipleLessonId,
            CancellationToken cancellationToken = default)
        {
            RequestCount++;
            ObservedProfileAlias = profileAlias;
            ObservedWorkloadSubject = workloadSubjectId;
            ObservedLessonId = discipleLessonId;
            ObservedCancellationToken = cancellationToken;
            return _retrieve(discipleLessonId);
        }

        /// <summary>不允許錯誤的 fee date-range operation。</summary>
        public Task<IReadOnlyList<FeeRecordDto>> RetrieveDedicationFeesByContactDateRangeAsync(string profileAlias, string workloadSubjectId, Guid contactId, DateTime startDate, DateTime endDate, string? contactName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <summary>不允許錯誤的一般 contact fee operation。</summary>
        public Task<IReadOnlyList<FeeRecordDto>> RetrieveDedicationFeesByContactAsync(string profileAlias, string workloadSubjectId, Guid contactId, string? contactName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <summary>不允許錯誤的 dedication period operation。</summary>
        public Task<IReadOnlyList<FeeRecordDto>> RetrieveFeesByDedicationPeriodAsync(string profileAlias, string workloadSubjectId, Guid dedicationBookingId, string paidPeriod, string? dedicationBookingName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <summary>不允許錯誤的 contact stor-lesson operation。</summary>
        public Task<IReadOnlyList<StorLessonRecordDto>> RetrieveStorLessonsByContactAsync(string profileAlias, string workloadSubjectId, Guid contactId, string? contactName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <summary>不允許錯誤的一般 disciple-lesson operation。</summary>
        public Task<IReadOnlyList<StorLessonRecordDto>> RetrieveStorLessonsByDiscipleLessonAsync(string profileAlias, string workloadSubjectId, Guid discipleLessonId, string? lessonName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

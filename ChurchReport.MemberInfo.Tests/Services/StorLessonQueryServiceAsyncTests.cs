// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/Services/StorLessonQueryServiceAsyncTests.cs
// 目的：保護 Package01 stor-lesson 非同步投影路徑的取消、隔離與無 SDK 補查契約。
//
// 測試邊界：此檔只以每個測試私有的 fake ProductClient 模擬已完成驗證的產品端回應；
// 不建立 CRM 連線、Entity 快取、背景工作或共享可變集合。每次呼叫的 DTO 與 projection
// 都必須只活在該測試的 request 範圍，以偵測同步封鎖、取消遺失或 A/B 資料混用。
// ============================================================================

using ChurchReport.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.ProductClient.FeeReads;
using SpeechMessage.Dynamics.ProductClient.Models;
using ToolUtilityNameSpace;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Services;

/// <summary>
/// 驗證 StorLessonQueryService 的 Package01 typed 非同步分支。
/// 本測試的決定性斷言是：未完成的遠端讀取不能同步封鎖 request thread、取消必須原樣傳遞，
/// 且 lesson 顯示欄位只能由本次 DTO 投影而來，不得另以 ToolUtility 讀取 CRM Entity。
/// </summary>
public sealed class StorLessonQueryServiceAsyncTests
{
    /// <summary>
    /// 當 ProductClient I/O 尚未完成時，服務必須回傳未完成的 Task；這可防止
    /// GetAwaiter().GetResult() 造成 ASP.NET Core request thread 同步阻塞。測試同時確認
    /// 課程日期與階段名稱直接來自本次 response DTO，避免額外 CRM 補查保留或混用 session。
    /// </summary>
    [Fact]
    public async Task Package01_contact_read_is_async_and_projects_lesson_display_fields_from_its_own_dto()
    {
        var source = new TaskCompletionSource<IReadOnlyList<StorLessonRecordDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new DeferredStorLessonReadClient(source.Task);
        var valid = true;
        using var utility = new ToolUtilityClass(ref valid);
        var service = new StorLessonQueryService(
            utility,
            client,
            Options.Create(new ProductDynamicsOptions { ProfileAlias = "church-report-test" }),
            package01FeeReadsEnabled: true);
        using var cancellation = new CancellationTokenSource();

        var operation = service.GetByContactAsync(
            contactName: null,
            contactId: "11111111-1111-1111-1111-111111111111",
            cancellation.Token);

        operation.IsCompleted.Should().BeFalse(
            "Package01 的遠端讀取必須 await，而不是同步等待尚未完成的 I/O");

        var classStartUtc = new DateTimeOffset(2026, 8, 9, 1, 2, 3, TimeSpan.Zero);
        source.SetResult(new[]
        {
            new StorLessonRecordDto
            {
                StorLessonId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                ContactId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                DiscipleLessonId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                DiscipleLessonName = "測試課程",
                ClassStartDate = classStartUtc,
                StageName = "進行中",
                CurrentComplete = false
            }
        });

        var rows = await operation;

        rows.Should().ContainSingle();
        rows[0].DiscipleLessonsName.Should().Be("測試課程");
        rows[0].DiscipleLessonsDateTime.Should().Be(classStartUtc.LocalDateTime);
        rows[0].StageName.Should().Be("進行中");
        client.ObservedCancellationToken.Should().Be(cancellation.Token);
    }

    /// <summary>
    /// 模擬 ProductClient 在 dispatch 後收到取消。服務不得吞掉取消、改走 legacy 路徑、建立重試，
    /// 或留下部分 projection；取消 token 必須被原樣傳遞到唯一的 request-local client 呼叫。
    /// </summary>
    [Fact]
    public async Task Package01_contact_read_propagates_cancellation_without_a_legacy_fallback()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new DeferredStorLessonReadClient(
            Task.FromCanceled<IReadOnlyList<StorLessonRecordDto>>(new CancellationToken(canceled: true)));
        var valid = true;
        using var utility = new ToolUtilityClass(ref valid);
        var service = new StorLessonQueryService(
            utility,
            client,
            Options.Create(new ProductDynamicsOptions { ProfileAlias = "church-report-test" }),
            package01FeeReadsEnabled: true);

        Func<Task> action = async () => await service.GetByContactAsync(
            contactName: null,
            contactId: "44444444-4444-4444-4444-444444444444",
            cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        client.ObservedCancellationToken.Should().Be(cancellation.Token);
        client.ContactRequestCount.Should().Be(1);
    }

    /// <summary>
    /// 交錯完成兩個不同 contact 的讀取，以可辨識 marker 驗證 A/B request 的 response DTO、
    /// 投影集合與顯示欄位不會透過 static、singleton 或重用集合交叉洩漏。
    /// </summary>
    [Fact]
    public async Task Package01_contact_reads_keep_interleaved_request_projections_isolated()
    {
        var client = new PerContactStorLessonReadClient();
        var valid = true;
        using var utility = new ToolUtilityClass(ref valid);
        var service = new StorLessonQueryService(
            utility,
            client,
            Options.Create(new ProductDynamicsOptions { ProfileAlias = "church-report-test" }),
            package01FeeReadsEnabled: true);
        var contactA = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var contactB = Guid.Parse("66666666-6666-6666-6666-666666666666");

        var operationA = service.GetByContactAsync(null, contactA.ToString(), CancellationToken.None);
        var operationB = service.GetByContactAsync(null, contactB.ToString(), CancellationToken.None);

        client.Complete(contactB, "B-課程", "B-階段");
        client.Complete(contactA, "A-課程", "A-階段");

        var resultA = await operationA;
        var resultB = await operationB;

        resultA.Should().ContainSingle().Which.DiscipleLessonsName.Should().Be("A-課程");
        resultA[0].StageName.Should().Be("A-階段");
        resultA[0].DiscipleLessonsName.Should().NotContain("B-");
        resultB.Should().ContainSingle().Which.DiscipleLessonsName.Should().Be("B-課程");
        resultB[0].StageName.Should().Be("B-階段");
        resultB[0].DiscipleLessonsName.Should().NotContain("A-");
    }

    /// <summary>
    /// 當 connector 明確回傳沒有開課日期時，typed projection 必須維持既有 UI 的
    /// <see cref="DateTime.MinValue"/> 哨兵值，而不是把 <see cref="DateTimeOffset.MinValue"/>
    /// 轉換成本機時間後顯示為看似有效的日期。此故障注入保護 null 的資料語意與時區隔離；
    /// DTO、client 與 projection 都只存在本測試 request，沒有 CRM、快取或背景資源需要釋放。
    /// </summary>
    [Fact]
    public async Task Package01_contact_read_keeps_missing_class_start_date_as_legacy_minimum_date()
    {
        var client = new DeferredStorLessonReadClient(Task.FromResult<IReadOnlyList<StorLessonRecordDto>>(
        [
            new StorLessonRecordDto
            {
                StorLessonId = Guid.Parse("77777777-7777-7777-7777-777777777777"),
                ContactId = Guid.Parse("88888888-8888-8888-8888-888888888888"),
                DiscipleLessonName = "無開課日期測試課程",
                ClassStartDate = null,
                StageName = "未設定"
            }
        ]));
        var valid = true;
        using var utility = new ToolUtilityClass(ref valid);
        var service = new StorLessonQueryService(
            utility,
            client,
            Options.Create(new ProductDynamicsOptions { ProfileAlias = "church-report-test" }),
            package01FeeReadsEnabled: true);

        var rows = await service.GetByContactAsync(
            contactName: null,
            contactId: "88888888-8888-8888-8888-888888888888",
            CancellationToken.None);

        rows.Should().ContainSingle();
        rows[0].DiscipleLessonsDateTime.Should().Be(DateTime.MinValue);
        rows[0].StageName.Should().Be("未設定");
    }

    /// <summary>
    /// 驗證 connector 傳來的 UTC 最小值不會因目前主機時區轉換而變成看似有效的本機開課時間。
    /// 故障注入使用 <see cref="DateTimeOffset.MinValue"/>，它在正偏移主機可能被顯示成凌晨後的
    /// 時刻、在負偏移主機則可能無法表示；決定性斷言是服務一律回傳既有 UI 的
    /// <see cref="DateTime.MinValue"/> 哨兵。DTO 與 fake client 皆為單一測試私有，沒有 CRM、
    /// 快取或背景資源，故不會形成跨 request 的時區或資料狀態洩漏。
    /// </summary>
    [Fact]
    public async Task Package01_contact_read_keeps_minimum_utc_sentinel_as_legacy_minimum_date()
    {
        var client = new DeferredStorLessonReadClient(Task.FromResult<IReadOnlyList<StorLessonRecordDto>>(
        [
            new StorLessonRecordDto
            {
                StorLessonId = Guid.Parse("12121212-1212-1212-1212-121212121212"),
                ContactId = Guid.Parse("34343434-3434-3434-3434-343434343434"),
                DiscipleLessonName = "最小 UTC 哨兵測試課程",
                ClassStartDate = DateTimeOffset.MinValue,
                StageName = "未設定"
            }
        ]));
        var valid = true;
        using var utility = new ToolUtilityClass(ref valid);
        var service = new StorLessonQueryService(
            utility,
            client,
            Options.Create(new ProductDynamicsOptions { ProfileAlias = "church-report-test" }),
            package01FeeReadsEnabled: true);

        var rows = await service.GetByContactAsync(
            contactName: null,
            contactId: "34343434-3434-3434-3434-343434343434",
            CancellationToken.None);

        rows.Should().ContainSingle();
        rows[0].DiscipleLessonsDateTime.Should().Be(DateTime.MinValue);
        rows[0].StageName.Should().Be("未設定");
    }

    /// <summary>
    /// 依門徒課程的 typed API 必須原樣傳遞取消、使用該輸入作為缺少 DTO lookup 時的預設 ID，
    /// 並把 null 開課日期與階段維持為舊 UI 的最小日期及空字串。此案例防止 contact path 已安全
    /// 但共用 executor 的另一個公開 API 偷走 token、fallback 到 legacy 或保留前一個 request 的欄位。
    /// fake 的觀察值只活在測試 instance，沒有 CRM session、共享 cache 或背景清理工作。
    /// </summary>
    [Fact]
    public async Task Package01_disciple_lesson_read_forwards_cancellation_and_preserves_null_display_values()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new DeferredStorLessonReadClient(Task.FromResult<IReadOnlyList<StorLessonRecordDto>>(
        [
            new StorLessonRecordDto
            {
                StorLessonId = Guid.Parse("99999999-9999-9999-9999-999999999999"),
                DiscipleLessonId = null,
                DiscipleLessonName = "門徒課程 typed 測試",
                ClassStartDate = null,
                StageName = null
            }
        ]));
        var valid = true;
        using var utility = new ToolUtilityClass(ref valid);
        var service = new StorLessonQueryService(
            utility,
            client,
            Options.Create(new ProductDynamicsOptions { ProfileAlias = "church-report-test" }),
            package01FeeReadsEnabled: true);
        var discipleLessonId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var rows = await service.GetByDiscipleLessonAsync(
            lessonName: null,
            discipleLessonId: discipleLessonId.ToString(),
            cancellation.Token);

        rows.Should().ContainSingle();
        rows[0].DiscipleLessonId.Should().Be(discipleLessonId);
        rows[0].DiscipleLessonsDateTime.Should().Be(DateTime.MinValue);
        rows[0].StageName.Should().BeEmpty();
        client.ObservedDiscipleLessonCancellationToken.Should().Be(cancellation.Token);
        client.DiscipleLessonRequestCount.Should().Be(1);
        client.ContactRequestCount.Should().Be(0);
    }

    /// <summary>
    /// ProductClient 對依門徒課程 read 回傳取消時，服務不得改走 legacy 或另一個 typed operation。
    /// 決定性斷言是相同 cancellation token 會由唯一 disciple-lesson 呼叫觀察到、contact 計數維持
    /// 零，且取消原樣傳回；這避免在 request 終止後保留部分結果或啟動背景 fallback。
    /// </summary>
    [Fact]
    public async Task Package01_disciple_lesson_read_propagates_cancellation_without_contact_or_legacy_fallback()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new DeferredStorLessonReadClient(
            Task.FromCanceled<IReadOnlyList<StorLessonRecordDto>>(new CancellationToken(canceled: true)));
        var valid = true;
        using var utility = new ToolUtilityClass(ref valid);
        var service = new StorLessonQueryService(
            utility,
            client,
            Options.Create(new ProductDynamicsOptions { ProfileAlias = "church-report-test" }),
            package01FeeReadsEnabled: true);

        Func<Task> action = async () => await service.GetByDiscipleLessonAsync(
            lessonName: null,
            discipleLessonId: "bbbbbbbb-cccc-dddd-eeee-ffffffffffff",
            cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        client.ObservedDiscipleLessonCancellationToken.Should().Be(cancellation.Token);
        client.DiscipleLessonRequestCount.Should().Be(1);
        client.ContactRequestCount.Should().Be(0);
    }

    /// <summary>
    /// 以 deferred Task 模擬唯一的 Package01 stor-lesson client。所有觀察值均為 instance state，
    /// 不跨測試或 request 共用；這可明確驗證服務是否傳遞 cancellation，而不是測試 fake 自己的行為。
    /// </summary>
    private sealed class DeferredStorLessonReadClient : IPackage01FeeReadClient
    {
        private readonly Task<IReadOnlyList<StorLessonRecordDto>> _result;

        /// <summary>
        /// 建立本測試專屬的固定結果來源；結果 task 的完成時機由測試控制，以可靠重現同步封鎖與取消路徑。
        /// </summary>
        public DeferredStorLessonReadClient(Task<IReadOnlyList<StorLessonRecordDto>> result)
        {
            _result = result;
        }

        /// <summary>
        /// 取得服務最後傳入的取消 token；此值只在 fake instance 的短生命週期內保存。
        /// </summary>
        public CancellationToken ObservedCancellationToken { get; private set; }

        /// <summary>
        /// 取得唯一門徒課程 typed 呼叫收到的取消 token；值只存在短生命週期 fake instance，
        /// 不會跨測試或跨使用者保留。
        /// </summary>
        public CancellationToken ObservedDiscipleLessonCancellationToken { get; private set; }

        /// <summary>
        /// 取得 typed contact operation 的呼叫次數，以確保取消時沒有 fallback 或 retry。
        /// </summary>
        public int ContactRequestCount { get; private set; }

        /// <summary>
        /// 取得門徒課程 typed operation 呼叫次數，以判斷取消或 fault 是否錯誤 fallback 到其他路徑。
        /// </summary>
        public int DiscipleLessonRequestCount { get; private set; }

        /// <summary>
        /// 模擬 typed contact read 並記錄服務交付的取消 token 與呼叫次數。profile、workload、contact
        /// 與名稱僅在本次呼叫參數範圍使用，不寫入 shared state；回傳由測試建構的 task，使測試可辨識
        /// 真正的 await 與取消傳遞行為。
        /// </summary>
        public Task<IReadOnlyList<StorLessonRecordDto>> RetrieveStorLessonsByContactAsync(
            string profileAlias,
            string workloadSubjectId,
            Guid contactId,
            string? contactName = null,
            CancellationToken cancellationToken = default)
        {
            ObservedCancellationToken = cancellationToken;
            ContactRequestCount++;
            return _result;
        }

        /// <summary>
        /// 此 fake 不支援 date-range fee read，呼叫即失敗以防測試意外使用錯 capability。它不建立
        /// CRM、HTTP、timer 或可重用資源，故沒有跨測試清理責任。
        /// </summary>
        public Task<IReadOnlyList<FeeRecordDto>> RetrieveDedicationFeesByContactDateRangeAsync(string profileAlias, string workloadSubjectId, Guid contactId, DateTime startDate, DateTime endDate, string? contactName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <summary>
        /// 此 fake 不支援一般 contact fee read；拋出受控例外可防止 stor-lesson 測試被未預期的
        /// fallback 掩蓋，且不保留任何 request 資料。
        /// </summary>
        public Task<IReadOnlyList<FeeRecordDto>> RetrieveDedicationFeesByContactAsync(string profileAlias, string workloadSubjectId, Guid contactId, string? contactName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <summary>
        /// 此 fake 不支援 dedication-period fee read，確保測試只驗證既定 stor-lesson operation。
        /// 例外在呼叫端同步觀察，沒有背景工作或資源留存。
        /// </summary>
        public Task<IReadOnlyList<FeeRecordDto>> RetrieveFeesByDedicationPeriodAsync(string profileAlias, string workloadSubjectId, Guid dedicationBookingId, string paidPeriod, string? dedicationBookingName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <summary>
        /// 此 fake 不支援費用編輯的門徒課程讀取；這可立即暴露錯誤 capability routing，而不是
        /// 產生看似成功的空資料或跨 domain fallback。
        /// </summary>
        public Task<IReadOnlyList<StorLessonRecordDto>> RetrieveFeeEditorRowsByDiscipleLessonAsync(string profileAlias, string workloadSubjectId, Guid discipleLessonId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <summary>
        /// 模擬唯一門徒課程 typed read。方法記錄 cancellation 與次數後回傳測試安排的結果；它不
        /// 依名稱、profile 或 workload 選擇其他 response，也不建立 CRM／HTTP 連線或共享可變狀態。
        /// </summary>
        public Task<IReadOnlyList<StorLessonRecordDto>> RetrieveStorLessonsByDiscipleLessonAsync(
            string profileAlias,
            string workloadSubjectId,
            Guid discipleLessonId,
            string? lessonName = null,
            CancellationToken cancellationToken = default)
        {
            ObservedDiscipleLessonCancellationToken = cancellationToken;
            DiscipleLessonRequestCount++;
            return _result;
        }
    }

    /// <summary>
    /// 為 A/B 交錯情境保留各 contact 的獨立 TaskCompletionSource。字典只存在單一測試 fake 中，
    /// 且在測試結束後可被回收，沒有任何 production shared cache 或背景資源。
    /// </summary>
    private sealed class PerContactStorLessonReadClient : IPackage01FeeReadClient
    {
        private readonly Dictionary<Guid, TaskCompletionSource<IReadOnlyList<StorLessonRecordDto>>> _sources = new();

        /// <summary>
        /// 為指定 contact 建立唯一的 deferred response。每個 TaskCompletionSource 只對應本次測試中
        /// 一個 request，沒有 static cache 或跨測試共用，因此可可靠偵測 A/B 結果混用。
        /// </summary>
        public Task<IReadOnlyList<StorLessonRecordDto>> RetrieveStorLessonsByContactAsync(
            string profileAlias,
            string workloadSubjectId,
            Guid contactId,
            string? contactName = null,
            CancellationToken cancellationToken = default)
        {
            var source = new TaskCompletionSource<IReadOnlyList<StorLessonRecordDto>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _sources.Add(contactId, source);
            return source.Task;
        }

        /// <summary>
        /// 完成指定 contact 的唯一 response；測試只使用已由呼叫建立的 key，缺漏時立即失敗而非猜測資料來源。
        /// </summary>
        public void Complete(Guid contactId, string lessonName, string stageName)
        {
            _sources[contactId].SetResult(new[]
            {
                new StorLessonRecordDto
                {
                    StorLessonId = Guid.NewGuid(),
                    ContactId = contactId,
                    DiscipleLessonName = lessonName,
                    StageName = stageName,
                    ClassStartDate = new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero)
                }
            });
        }

        /// <summary>
        /// 此 A/B fake 不支援 date-range fee read。若服務誤跨 capability 呼叫，立即失敗而不是
        /// 將另一個資料域的結果回傳給目前 request。
        /// </summary>
        public Task<IReadOnlyList<FeeRecordDto>> RetrieveDedicationFeesByContactDateRangeAsync(string profileAlias, string workloadSubjectId, Guid contactId, DateTime startDate, DateTime endDate, string? contactName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <summary>
        /// 此 A/B fake 不支援一般 contact fee read；不建立或保留任何外部資源。
        /// </summary>
        public Task<IReadOnlyList<FeeRecordDto>> RetrieveDedicationFeesByContactAsync(string profileAlias, string workloadSubjectId, Guid contactId, string? contactName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <summary>
        /// 此 A/B fake 不支援 dedication-period fee read，保護測試的 operation 邊界。
        /// </summary>
        public Task<IReadOnlyList<FeeRecordDto>> RetrieveFeesByDedicationPeriodAsync(string profileAlias, string workloadSubjectId, Guid dedicationBookingId, string paidPeriod, string? dedicationBookingName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <summary>
        /// 此 A/B fake 不支援費用編輯門徒課程 read，錯誤呼叫必須立即可見而非 fallback。
        /// </summary>
        public Task<IReadOnlyList<StorLessonRecordDto>> RetrieveFeeEditorRowsByDiscipleLessonAsync(string profileAlias, string workloadSubjectId, Guid discipleLessonId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <summary>
        /// 此 A/B fake 不支援依門徒課程 read，因測試只需驗證 contact request 的交錯隔離。
        /// </summary>
        public Task<IReadOnlyList<StorLessonRecordDto>> RetrieveStorLessonsByDiscipleLessonAsync(string profileAlias, string workloadSubjectId, Guid discipleLessonId, string? lessonName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

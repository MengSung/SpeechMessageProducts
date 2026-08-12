// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/Package01FeeReadClientTests.cs
// 用途：驗證 Package 1 ProductClient 只消費封閉的共用回應契約，並把已投影的
//       fee/stor-lesson 記錄一對一轉換成公開產品 DTO；測試不建立 CRM 連線、
//       不保留 OData JSON，也不讓測試替身擁有任何長生命週期資源。
// ============================================================================

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.FeeReads;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 Package 1 讀取用戶端的封閉回應邊界。
/// 測試替身只回傳 <see cref="OperationResponseData"/>，使產品端無法藉由測試或實作
/// 重新接觸 CRM/OData 欄位、接續連結、權杖或其他上游擴充資料。
/// </summary>
public sealed class Package01FeeReadClientTests
{
    /// <summary>
    /// 驗證日期區間費用查詢僅傳送登錄的具型別參數，並將 Gateway 已完成驗證與投影的
    /// 費用記錄逐欄複製到公開 DTO。此測試不允許 ProductClient 解析原始 JSON，避免
    /// 金額、標籤或個資欄位的相容性規則在跨產品邊界被重複實作。
    /// </summary>
    [Fact]
    public async Task Date_range_query_sends_typed_parameters_only()
    {
        OperationExecutionRequest? seen = null;
        var executor = new FakeExecutor(request =>
        {
            seen = request;
            return CreateFeeResult(
                request.CapabilityOperationId,
                new Package01FeeRecord
                {
                    FeeId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    CreatedOn = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
                    PayDate = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero),
                    Amount = 1200m,
                    PayWayOption = 100000001,
                    PayWayLabel = "credit-card",
                    CategoryLabel = "dedication",
                    Others = "note",
                    PaidPeriod = "1",
                    Name = "fee-name"
                });
        });

        var client = CreateClient(executor);
        var contactId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        var rows = await client.RetrieveDedicationFeesByContactDateRangeAsync(
            "jesus-dev",
            "church-report-service",
            contactId,
            new DateTime(2026, 1, 1),
            new DateTime(2026, 1, 31),
            "contact-name");

        seen.Should().NotBeNull();
        seen!.CapabilityOperationId.Should().Be(OperationIds.FeeDedicationRetrieveByContactDateRange);
        seen.Parameters.Keys.Should().BeEquivalentTo(new[] { "contactId", "startDate", "endDate", "contactName" });
        seen.Parameters.ContainsKey("rawFetchXml").Should().BeFalse();

        rows.Should().ContainSingle();
        rows[0].FeeId.Should().Be(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        rows[0].CreatedOn.Should().Be(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
        rows[0].PayDate.Should().Be(new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero));
        rows[0].Amount.Should().Be(1200m);
        rows[0].PayWayOption.Should().Be(100000001);
        rows[0].PayWayLabel.Should().Be("credit-card");
        rows[0].CategoryLabel.Should().Be("dedication");
        rows[0].Others.Should().Be("note");
        rows[0].PaidPeriod.Should().Be("1");
        rows[0].Name.Should().Be("fee-name");
    }

    /// <summary>
    /// 驗證費用編輯器的獨立唯讀能力固定映射到 <c>fees.editor.load.by.disciplelesson</c>，且只把
    /// 已授權的 lesson GUID 作為具型別參數交給 executor。此回歸測試不建立 CRM、HTTP、連線或
    /// 背景工作；它只檢查短生命週期的 request，以防日後產品端誤改用一般 stor-lesson 或費用
    /// operation 而越過 P7.4 的 DTO-only authorization boundary。
    /// </summary>
    [Fact]
    public async Task Fee_editor_read_sends_only_the_exact_registered_disciple_lesson_operation()
    {
        OperationExecutionRequest? seen = null;
        var target = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var executor = new FakeExecutor(request =>
        {
            seen = request;
            return CreateStorLessonResult(
                request.CapabilityOperationId,
                new Package01StorLessonRecord { DiscipleLessonId = target });
        });
        var client = CreateClient(executor);

        var rows = await client.RetrieveFeeEditorRowsByDiscipleLessonAsync(
            "server-owned-profile",
            "church-report-service",
            target,
            CancellationToken.None);

        seen.Should().NotBeNull();
        seen!.CapabilityOperationId.Should().Be(OperationIds.FeesEditorLoadByDiscipleLesson);
        seen.ProfileAlias.Should().Be("server-owned-profile");
        seen.WorkloadSubjectId.Should().Be("church-report-service");
        seen.Parameters.Keys.Should().BeEquivalentTo(new[] { "discipleLessonId" });
        seen.Parameters["discipleLessonId"].Should().Be(target);
        rows.Should().ContainSingle().Which.DiscipleLessonId.Should().Be(target);
    }

    /// <summary>
    /// 驗證封閉費用記錄的可選欄位直接保留 null，且缺省金額維持既有的零值預設。
    /// 這是產品 DTO 的相容性保證；原始 OData 金額、日期與選項集轉換必須在 Gateway
    /// 投影器完成，避免用戶端保存任何上游格式或容錯解析路徑。
    /// </summary>
    [Fact]
    public async Task Fee_read_preserves_null_fields_and_zero_amount_from_closed_record()
    {
        var executor = new FakeExecutor(request => CreateFeeResult(
            request.CapabilityOperationId,
            new Package01FeeRecord { Name = "zero-default" }));
        var client = CreateClient(executor);

        var rows = await client.RetrieveDedicationFeesByContactAsync(
            "jesus-prod",
            "church-report-service",
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

        rows.Should().ContainSingle();
        rows[0].FeeId.Should().BeNull();
        rows[0].CreatedOn.Should().BeNull();
        rows[0].PayDate.Should().BeNull();
        rows[0].Amount.Should().Be(0m);
        rows[0].PayWayOption.Should().BeNull();
        rows[0].PayWayLabel.Should().BeNull();
        rows[0].CategoryLabel.Should().BeNull();
        rows[0].Others.Should().BeNull();
        rows[0].PaidPeriod.Should().BeNull();
        rows[0].Name.Should().Be("zero-default");
    }

    /// <summary>
    /// 驗證費用 API 收到 stor-lesson branch 時會在建立任何產品 DTO 前 fail-closed。
    /// 這可阻止錯誤的 Gateway 路由或被污染的回應分支把不同資料分類的欄位帶入費用流程。
    /// </summary>
    [Fact]
    public async Task Fee_read_rejects_stor_lesson_response_kind_before_mapping()
    {
        var executor = new FakeExecutor(_ => CreateStorLessonResult(
            OperationIds.LessonsStorRetrieveByContact,
            new Package01StorLessonRecord()));
        var client = CreateClient(executor);

        var act = () => client.RetrieveDedicationFeesByContactAsync(
            "jesus-prod",
            "church-report-service",
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// 驗證即使 response kind 正確，回應的 capability operation ID 仍須與本次請求完全相同。
    /// 此檢查在 DTO 配置前執行，避免快取、重試或錯誤回應交叉污染另一個 Package 1 讀取流程。
    /// </summary>
    [Fact]
    public async Task Fee_read_rejects_mismatched_operation_id_before_mapping()
    {
        var executor = new FakeExecutor(_ => CreateFeeResult(
            OperationIds.FeesRetrieveByDedicationPeriod,
            new Package01FeeRecord { Amount = 500m }));
        var client = CreateClient(executor);

        var act = () => client.RetrieveDedicationFeesByContactAsync(
            "jesus-prod",
            "church-report-service",
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// 驗證 stor-lesson 查詢僅傳送登錄參數，並完整複製封閉記錄的 lookup、日期、布林與
    /// 個資欄位。替身沒有 HTTP、串流或計時器 owner，因此測試完成後不會遺留跨測試狀態。
    /// </summary>
    [Fact]
    public async Task Stor_lessons_by_contact_sends_typed_parameters_and_maps_closed_records()
    {
        OperationExecutionRequest? seen = null;
        var executor = new FakeExecutor(request =>
        {
            seen = request;
            return CreateStorLessonResult(
                request.CapabilityOperationId,
                new Package01StorLessonRecord
                {
                    StorLessonId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                    ContactId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    DiscipleLessonId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                    CreatedOn = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
                    PayDate = new DateTimeOffset(2026, 2, 2, 0, 0, 0, TimeSpan.Zero),
                    CurrentComplete = true,
                    ContactName = "contact-name",
                    ContactMobile = "0912345678",
                    DiscipleLessonName = "lesson-001",
                    FeeAmount = 700m
                });
        });

        var client = CreateClient(executor);
        var rows = await client.RetrieveStorLessonsByContactAsync(
            "jesus-prod",
            "church-report-service",
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "contact-name");

        seen.Should().NotBeNull();
        seen!.CapabilityOperationId.Should().Be(OperationIds.LessonsStorRetrieveByContact);
        seen.Parameters.Keys.Should().BeEquivalentTo(new[] { "contactId", "contactName" });
        rows.Should().ContainSingle();
        rows[0].StorLessonId.Should().Be(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        rows[0].ContactId.Should().Be(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        rows[0].DiscipleLessonId.Should().Be(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"));
        rows[0].CreatedOn.Should().Be(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        rows[0].PayDate.Should().Be(new DateTimeOffset(2026, 2, 2, 0, 0, 0, TimeSpan.Zero));
        rows[0].CurrentComplete.Should().BeTrue();
        rows[0].ContactName.Should().Be("contact-name");
        rows[0].ContactMobile.Should().Be("0912345678");
        rows[0].DiscipleLessonName.Should().Be("lesson-001");
        rows[0].FeeAmount.Should().Be(700m);
    }

    /// <summary>
    /// 驗證依聯絡人讀取的 stor-lesson 公開方法，會把 connector 已封閉的開課 UTC 時間與目前階段
    /// 一對一投影至產品 DTO；同時刻意輸入 null 顯示欄位，防止產品端以共用預設值補寫前一筆或
    /// 其他請求的姓名、手機與課程名稱。替身只在此次呼叫同步產生不可變回應，沒有保存 profile、
    /// workload、CRM 實體或外部資源；決定性斷言同時守護欄位精確性與跨請求資料隔離契約。
    /// </summary>
    [Fact]
    public async Task Stor_lessons_by_contact_maps_class_start_date_stage_name_and_nullable_display_fields()
    {
        var classStartDate = new DateTimeOffset(2026, 8, 12, 9, 30, 0, TimeSpan.Zero);
        var executor = new FakeExecutor(request => CreateStorLessonResult(
            request.CapabilityOperationId,
            new Package01StorLessonRecord
            {
                StorLessonId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                ClassStartDate = classStartDate,
                StageName = "裝備課程－進行中",
                ContactName = null,
                ContactMobile = null,
                DiscipleLessonName = null
            }));
        var client = CreateClient(executor);

        var rows = await client.RetrieveStorLessonsByContactAsync(
            "jesus-prod",
            "church-report-service",
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

        rows.Should().ContainSingle();
        rows[0].ClassStartDate.Should().Be(classStartDate);
        rows[0].StageName.Should().Be("裝備課程－進行中");
        rows[0].ContactName.Should().BeNull();
        rows[0].ContactMobile.Should().BeNull();
        rows[0].DiscipleLessonName.Should().BeNull();
    }

    /// <summary>
    /// 驗證 stor-lesson API 收到費用 branch 時會拒絕回應，而非把財務欄位誤當成課程資料。
    /// 故障會在產品 DTO 建立前結束，讓上游分支錯配不會形成跨資料域的殘留或快取資料。
    /// </summary>
    [Fact]
    public async Task Stor_lesson_read_rejects_fee_response_kind_before_mapping()
    {
        var executor = new FakeExecutor(_ => CreateFeeResult(
            OperationIds.FeeDedicationRetrieveByContact,
            new Package01FeeRecord()));
        var client = CreateClient(executor);

        var act = () => client.RetrieveStorLessonsByContactAsync(
            "jesus-prod",
            "church-report-service",
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// 驗證成功但沒有 data branch 的既有語意維持為空集合，而不是將合法的 no-data 成功
    /// 誤判為通訊失敗。空集合沒有外部資源 owner，且不會保留上一個請求的回應資料。
    /// </summary>
    [Fact]
    public async Task Fee_read_returns_empty_list_for_successful_no_data_envelope()
    {
        var executor = new FakeExecutor(_ => OperationExecutionResult.Success(null));
        var client = CreateClient(executor);

        var rows = await client.RetrieveDedicationFeesByContactAsync(
            "jesus-prod",
            "church-report-service",
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

        rows.Should().BeEmpty();
    }

    /// <summary>
    /// 驗證 dedication period 查詢保留現有的必填參數與空結果行為，但空結果來自具型別的
    /// fee branch，而非未驗證的 OData value 陣列。
    /// </summary>
    [Fact]
    public async Task Fees_by_dedication_period_requires_paid_period_parameter()
    {
        OperationExecutionRequest? seen = null;
        var executor = new FakeExecutor(request =>
        {
            seen = request;
            return CreateFeeResult(request.CapabilityOperationId);
        });

        var client = CreateClient(executor);
        var rows = await client.RetrieveFeesByDedicationPeriodAsync(
            "jesus-prod",
            "church-report-service",
            Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            "2026-01",
            "booking-name");

        seen.Should().NotBeNull();
        seen!.CapabilityOperationId.Should().Be(OperationIds.FeesRetrieveByDedicationPeriod);
        seen.Parameters.Keys.Should().BeEquivalentTo(new[]
        {
            "dedicationBookingId", "paidPeriod", "dedicationBookingName"
        });
        rows.Should().BeEmpty();
    }

    /// <summary>
    /// 驗證 disciple-lesson 查詢不攜帶未登錄查詢片段，並保留封閉 stor-lesson 記錄中的
    /// nullable 欄位。此案例特別固定 null FeeAmount，確保產品端不重新套用舊 OData 金額轉換。
    /// </summary>
    [Fact]
    public async Task Stor_lessons_by_disciple_lesson_sends_typed_parameters_and_preserves_nulls()
    {
        OperationExecutionRequest? seen = null;
        var executor = new FakeExecutor(request =>
        {
            seen = request;
            return CreateStorLessonResult(
                request.CapabilityOperationId,
                new Package01StorLessonRecord
                {
                    StorLessonId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    ContactId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    CurrentComplete = false
                });
        });

        var client = CreateClient(executor);
        var rows = await client.RetrieveStorLessonsByDiscipleLessonAsync(
            "jesus-prod",
            "church-report-service",
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            "lesson-001");

        seen.Should().NotBeNull();
        seen!.CapabilityOperationId.Should().Be(OperationIds.LessonsStorRetrieveByDiscipleLesson);
        seen.Parameters.Keys.Should().BeEquivalentTo(new[] { "discipleLessonId", "lessonName" });
        rows.Should().ContainSingle();
        rows[0].ContactId.Should().Be(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        rows[0].DiscipleLessonId.Should().BeNull();
        rows[0].FeeAmount.Should().BeNull();
        rows[0].CurrentComplete.Should().BeFalse();
    }

    /// <summary>
    /// 驗證依門徒課程讀取的 stor-lesson 公開方法，與依聯絡人路徑使用相同的封閉 DTO 合約：開課
    /// 時間不可遺失，null 階段不可被產品端猜測或補成舊值。三個顯示欄位提供不同於另一測試的
    /// 非 null marker，證明每次映射建立新的 request-local DTO，而非重用前次呼叫的可變顯示資料。
    /// 此測試不建立計時器、連線或背景工作；executor 回傳後沒有可由下一位使用者觀察到的資源或狀態。
    /// </summary>
    [Fact]
    public async Task Stor_lessons_by_disciple_lesson_maps_class_start_date_and_null_stage_without_display_leakage()
    {
        var classStartDate = new DateTimeOffset(2026, 8, 13, 14, 45, 0, TimeSpan.FromHours(8));
        var executor = new FakeExecutor(request => CreateStorLessonResult(
            request.CapabilityOperationId,
            new Package01StorLessonRecord
            {
                StorLessonId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                ClassStartDate = classStartDate,
                StageName = null,
                ContactName = "門徒甲",
                ContactMobile = "0900000000",
                DiscipleLessonName = "門徒課程－八月班"
            }));
        var client = CreateClient(executor);

        var rows = await client.RetrieveStorLessonsByDiscipleLessonAsync(
            "jesus-prod",
            "church-report-service",
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"));

        rows.Should().ContainSingle();
        rows[0].ClassStartDate.Should().Be(classStartDate);
        rows[0].StageName.Should().BeNull();
        rows[0].ContactName.Should().Be("門徒甲");
        rows[0].ContactMobile.Should().Be("0900000000");
        rows[0].DiscipleLessonName.Should().Be("門徒課程－八月班");
    }

    /// <summary>
    /// 建立受測用戶端並集中管理無狀態 logger 的注入；此 helper 不建立網路、計時器、串流
    /// 或快取，因此沒有需要跨測試延後釋放的資源 owner。
    /// </summary>
    private static Package01FeeReadClient CreateClient(IDynamicsOperationExecutor executor)
        => new(executor, NullLogger<Package01FeeReadClient>.Instance);

    /// <summary>
    /// 建立與指定 capability operation 綁定的封閉 fee branch。operation ID 明確由呼叫端提供，
    /// 讓測試能證明產品端在 DTO 配置前檢查回應與請求的身分一致性。
    /// </summary>
    private static OperationExecutionResult CreateFeeResult(
        string operationId,
        params Package01FeeRecord[] records)
        => OperationExecutionResult.Success(
            OperationResponseData.ForPackage01FeeRecords(operationId, "9.1", records));

    /// <summary>
    /// 建立與指定 capability operation 綁定的封閉 stor-lesson branch。這個 helper 不接受原始
    /// JSON 或 OData 欄位，避免測試替身意外重建產品邊界以外的上游資料格式。
    /// </summary>
    private static OperationExecutionResult CreateStorLessonResult(
        string operationId,
        params Package01StorLessonRecord[] records)
        => OperationExecutionResult.Success(
            OperationResponseData.ForPackage01StorLessonRecords(operationId, "9.1", records));

    /// <summary>
    /// 以同步、無資源所有權的 executor 替身固定用戶端輸入與回應。它不建立 HTTP 連線、
    /// 不保存前一個請求，也不吞掉取消訊號；因此每個測試都有獨立且可重複的契約邊界。
    /// </summary>
    private sealed class FakeExecutor : IDynamicsOperationExecutor
    {
        private readonly Func<OperationExecutionRequest, OperationExecutionResult> _handler;

        /// <summary>
        /// 儲存單一測試案例的回應委派；委派僅在當次 ExecuteAsync 呼叫使用，避免把請求、
        /// 工作負載主體或回應資料保留在共享靜態狀態中。
        /// </summary>
        public FakeExecutor(Func<OperationExecutionRequest, OperationExecutionResult> handler)
        {
            _handler = handler;
        }

        /// <summary>
        /// 立即回傳測試定義的封閉結果。此替身沒有非同步 I/O 或可釋放資源，因此取消 token
        /// 不會被註冊或保存；真實 executor 的取消與釋放責任仍由其自身生命週期擁有。
        /// </summary>
        public Task<OperationExecutionResult> ExecuteAsync(
            OperationExecutionRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_handler(request));
    }
}

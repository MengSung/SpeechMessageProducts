// ============================================================================
// P7.2 後續 Slice D–H operation ID 本機契約測試
//
// 本檔案保護第一版候選版對 D–H 的固定能力名稱。它只讀取編譯期常數，不建立
// connector、Data8 client、CRM request、Session、快取、背景工作或 CE 連線。每個
// assertion 要求 ID 與 coverage matrix 一致，避免產品或未來 executor 各自拼接未受控字串。
// ============================================================================

using System.Reflection;
using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 P7.2 continuation 的 Slice D–H operation ID 為不可變、server-owned 常數。
///
/// <para>
/// 測試保護的契約是：付款、約談、新人、費用與出席的 operation 都必須有唯一的
/// coverage-matrix 對應名稱，且不能由產品輸入決定 entity、endpoint、credential、
/// Owner 或任意 CRM 欄位。故障注入是缺少或錯誤常數；決定性斷言是完整名稱/值配對。
/// </para>
/// </summary>
public sealed class P72ContinuationOperationIdsTests
{
    /// <summary>
    /// 保護 D–H 的十三個 local-only capability ID。這些 ID 的存在不表示 CE executor
    /// 或產品流量已啟用；它們只是後續 immutable catalog、fixture ledger 與 fail-closed
    /// dispatch gate 的唯一索引，因此不保存 request、profile、connector lease 或資源。
    /// </summary>
    [Fact]
    public void Slice_d_to_h_operation_ids_match_the_approved_coverage_matrix()
    {
        var fields = typeof(OperationIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!);

        fields.Should().Contain(new Dictionary<string, string>
        {
            ["PaymentsFeeUpdateAfterPayment"] = "payments.fee.update.after.payment",
            ["PaymentsDedicationCompleteRecurring"] = "payments.dedication.complete.recurring",
            ["PaymentsContactCreateOrUpdate"] = "payments.contact.create.or.update",
            ["PaymentsDedicationCancelBooking"] = "payments.dedication.cancel.booking",
            ["PaymentsContactCreateWithDedicationNumber"] = "payments.contact.create.with.dedication.number",
            ["PaymentsContactUpdateCardProfile"] = "payments.contact.update.card.profile",
            ["AppointmentsEntityCreateOrUpdate"] = "appointments.entity.create.or.update",
            ["NewPersonContactCreateFullOnboarding"] = "newperson.contact.create.full.onboarding",
            ["FeesEditorUpdateByStorLesson"] = "fees.editor.update.by.storlesson",
            ["FeesEditorStageInmemoryChange"] = "fees.editor.stage.inmemory.change",
            ["FeesCreateFromStorLesson"] = "fees.create.from.storlesson",
            ["PresentRecordCreateOnDownload"] = "presentrecord.create.on.download",
            ["PresentRecordUpsertOnUpload"] = "presentrecord.upsert.on.upload"
        });
    }

    /// <summary>
    /// 保護 D–H 的 local-only catalog 會與 ID 常數存在於同一個 Abstractions assembly，
    /// 避免未來只增加字串卻沒有對應的 allowlist、read-back、timeout 與 cleanup 政策。
    /// 反射僅檢查 immutable 型別是否已發布，不建立任何 catalog instance 或外部資源。
    /// </summary>
    [Fact]
    public void Slice_d_to_h_publish_a_local_only_catalog_type_before_any_executor_wiring()
    {
        typeof(OperationIds)
            .Assembly
            .GetType(
                "SpeechMessage.Dynamics.Abstractions.Operations.P72ContinuationLocalOnlyCatalog",
                throwOnError: false)
            .Should()
            .NotBeNull();
    }

    /// <summary>
    /// 保護 D–H local-only catalog 與 coverage matrix 的完整對應。
    /// </summary>
    [Fact]
    public void Slice_d_to_h_local_catalog_is_complete_and_remains_fail_closed()
    {
        var expectedOperationIds = new[]
        {
            OperationIds.PaymentsFeeUpdateAfterPayment,
            OperationIds.PaymentsDedicationCompleteRecurring,
            OperationIds.PaymentsContactCreateOrUpdate,
            OperationIds.PaymentsDedicationCancelBooking,
            OperationIds.PaymentsContactCreateWithDedicationNumber,
            OperationIds.PaymentsContactUpdateCardProfile,
            OperationIds.AppointmentsEntityCreateOrUpdate,
            OperationIds.NewPersonContactCreateFullOnboarding,
            OperationIds.FeesEditorUpdateByStorLesson,
            OperationIds.FeesEditorStageInmemoryChange,
            OperationIds.FeesCreateFromStorLesson,
            OperationIds.PresentRecordCreateOnDownload,
            OperationIds.PresentRecordUpsertOnUpload
        };

        var definitions = P72ContinuationLocalOnlyCatalog.All;

        definitions.Should().HaveCount(expectedOperationIds.Length);
        definitions.Select(definition => definition.OperationId)
            .Should()
            .BeEquivalentTo(expectedOperationIds);
        definitions.Should().OnlyHaveUniqueItems(definition => definition.OperationId);
        definitions.Should().OnlyContain(definition =>
            !definition.CeExecutorEnabled &&
            !definition.ConsumerEnabled &&
            definition.AllowedOutputNames.SequenceEqual(
                new[] { "outcome", "reasonCategory", "reconciliationCategory", "cleanupCategory", "operationExecuted" }));

        var forbiddenInputFragments = new[]
        {
            "owner", "entity", "endpoint", "credential", "token", "fetch", "organization", "profile"
        };
        definitions.SelectMany(definition => definition.AllowedInputNames)
            .Should()
            .OnlyContain(inputName => forbiddenInputFragments.All(fragment =>
                !inputName.Contains(fragment, StringComparison.OrdinalIgnoreCase)));

        definitions.Where(definition => definition.Slice == P72ContinuationSlice.Attendance)
            .Should()
            .HaveCount(2)
            .And.OnlyContain(definition =>
                definition.WeeklyReportPolicy == P72LocalWeeklyReportPolicy.ZeroActiveUnlinkedOrExactlyOneLinked);
        definitions.Where(definition => definition.Slice != P72ContinuationSlice.Attendance)
            .Should()
            .OnlyContain(definition => definition.WeeklyReportPolicy == P72LocalWeeklyReportPolicy.NotApplicable);

        definitions.Select(definition =>
                (definition.OperationId, definition.CallSiteId, definition.Slice, definition.CapabilityFamily))
            .Should()
            .BeEquivalentTo(new[]
            {
                (OperationIds.PaymentsFeeUpdateAfterPayment, "ORG-CALL-00036", P72ContinuationSlice.DonationLifecycle, "churchreport.donation.lifecycle"),
                (OperationIds.PaymentsDedicationCompleteRecurring, "ORG-CALL-00037", P72ContinuationSlice.DonationLifecycle, "churchreport.donation.lifecycle"),
                (OperationIds.PaymentsContactCreateOrUpdate, "ORG-CALL-00038", P72ContinuationSlice.DonationLifecycle, "churchreport.donation.lifecycle"),
                (OperationIds.PaymentsDedicationCancelBooking, "ORG-CALL-00042", P72ContinuationSlice.DonationLifecycle, "churchreport.donation.lifecycle"),
                (OperationIds.PaymentsContactCreateWithDedicationNumber, "ORG-CALL-00043", P72ContinuationSlice.DonationLifecycle, "churchreport.donation.lifecycle"),
                (OperationIds.PaymentsContactUpdateCardProfile, "ORG-CALL-00049", P72ContinuationSlice.DonationLifecycle, "churchreport.donation.lifecycle"),
                (OperationIds.AppointmentsEntityCreateOrUpdate, "ORG-CALL-00039", P72ContinuationSlice.Appointments, "churchreport.appointments"),
                (OperationIds.NewPersonContactCreateFullOnboarding, "ORG-CALL-00044", P72ContinuationSlice.ContactOnboarding, "churchreport.contact.onboarding"),
                (OperationIds.FeesEditorUpdateByStorLesson, "ORG-CALL-00048", P72ContinuationSlice.FeeLessons, "churchreport.fee.lessons"),
                (OperationIds.FeesEditorStageInmemoryChange, "ORG-CALL-00050", P72ContinuationSlice.FeeLessons, "churchreport.fee.lessons"),
                (OperationIds.FeesCreateFromStorLesson, "ORG-CALL-00067", P72ContinuationSlice.FeeLessons, "churchreport.fee.lessons"),
                (OperationIds.PresentRecordCreateOnDownload, "ORG-CALL-00068", P72ContinuationSlice.Attendance, "churchreport.attendance"),
                (OperationIds.PresentRecordUpsertOnUpload, "ORG-CALL-00069", P72ContinuationSlice.Attendance, "churchreport.attendance")
            });
    }

    /// <summary>
    /// 保護 catalog builder 會拒絕可能攜帶 credential、組織或 Data8 profile 選路權限的
    /// input 名稱。故障注入只以反射呼叫純 metadata factory，完全不建構 connector、client、
    /// Session、token、lease 或 CE request；決定性斷言是每種被禁止的名稱都在 type
    /// initialization 邊界以固定 InvalidOperationException fail closed。
    /// </summary>
    /// <param name="forbiddenInputName">不得出現在 local-only contract 的 authority fragment。</param>
    [Theory]
    [InlineData("accessToken")]
    [InlineData("organizationAlias")]
    [InlineData("profileAlias")]
    public void Local_catalog_definition_rejects_newly_forbidden_routing_authority_input_names(
        string forbiddenInputName)
    {
        var definition = typeof(P72ContinuationLocalOnlyCatalog).GetMethod(
            "Definition",
            BindingFlags.Static | BindingFlags.NonPublic);

        definition.Should().NotBeNull("測試必須直接保護 catalog 的唯一 metadata 建構邊界");

        Action createDefinition = () => definition!.Invoke(null,
        [
            "local.test.operation",
            "LOCAL-CALL-00001",
            P72ContinuationSlice.DonationLifecycle,
            "local.test.family",
            "local.test.template",
            P72LocalMutationPolicy.SingleAllowlistedDispatch,
            new[] { forbiddenInputName },
            P72LocalReadBackPolicy.ExactProjection,
            P72LocalTimeoutPolicy.NoReplayAfterUncertainDispatch,
            P72LocalCleanupPolicy.ReverseKnownKeys
        ]);

        var invocation = createDefinition.Should()
            .Throw<TargetInvocationException>()
            .Which;
        invocation.InnerException.Should().BeOfType<InvalidOperationException>();
    }

    /// <summary>
    /// 保護每個 local-only capability 都具有無重播的 timeout、可回溯 cleanup 與 read-back 政策，
    /// 防止未來只補 operation ID 就意外形成未治理的 CE dispatch。
    /// </summary>
    [Fact]
    public void Slice_d_to_h_local_catalog_has_bounded_readback_timeout_and_cleanup_policies()
    {
        P72ContinuationLocalOnlyCatalog.All.Should().OnlyContain(definition =>
            definition.ReadBackPolicy == P72LocalReadBackPolicy.ExactProjection ||
            definition.ReadBackPolicy == P72LocalReadBackPolicy.LocalStateProjection);
        P72ContinuationLocalOnlyCatalog.All.Should().OnlyContain(definition =>
            definition.TimeoutPolicy == P72LocalTimeoutPolicy.NoReplayAfterUncertainDispatch ||
            definition.TimeoutPolicy == P72LocalTimeoutPolicy.NoExternalDispatch);
        P72ContinuationLocalOnlyCatalog.All.Should().OnlyContain(definition =>
            definition.CleanupPolicy == P72LocalCleanupPolicy.ReverseKnownKeys ||
            definition.CleanupPolicy == P72LocalCleanupPolicy.DiscardOperationLocalState);
    }

    /// <summary>
    /// 保護 local-only metadata 的靜態集合不會因呼叫端保存或改寫回傳 collection 而污染下一個
    /// request。故障注入是取得兩次 catalog；決定性斷言為兩者不同集合實體、各自有完整內容，
    /// 且 definition 的輸入輸出集合不可修改。
    /// </summary>
    [Fact]
    public void Slice_d_to_h_local_catalog_returns_isolated_read_only_snapshots()
    {
        var first = P72ContinuationLocalOnlyCatalog.All;
        var second = P72ContinuationLocalOnlyCatalog.All;

        first.Should().NotBeSameAs(second);
        first.Should().HaveCount(13);
        second.Should().HaveCount(13);
        var definition = first.First();

        Action mutateInputs = () => ((IList<string>)definition.AllowedInputNames).Add("unexpected");
        Action mutateOutputs = () => ((IList<string>)definition.AllowedOutputNames).Add("unexpected");

        mutateInputs.Should().Throw<NotSupportedException>();
        mutateOutputs.Should().Throw<NotSupportedException>();
        second.First(candidate => candidate.OperationId == definition.OperationId)
            .AllowedInputNames
            .Should()
            .BeEquivalentTo(definition.AllowedInputNames);
    }
}

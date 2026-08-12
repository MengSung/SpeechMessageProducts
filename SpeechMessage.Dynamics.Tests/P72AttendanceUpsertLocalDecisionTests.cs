// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/P72AttendanceUpsertLocalDecisionTests.cs
// 用途：以 TDD 保護 Slice H attendance download-create／upload-upsert 的純本機安全決策。
//       測試不建立 CRM fixture、不執行 I/O，也不持有任何跨請求可重用的外部資源。
// ============================================================================

using System.Collections.Concurrent;
using System.Reflection;
using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 Slice H attendance create／upsert 在進入受治理 CE executor 前的最小安全決策契約。
///
/// <para>
/// 這些測試刻意只使用去識別化 scalar observation、既有的週報 cardinality 決策和 task-owned
/// opaque input；不傳入或保存 record ID、weekly-report ID、Owner、Contact、Profile、connector、
/// lease、client、Session、token 或 raw CRM response。未來 production reducer 必須保持純函式，
/// 對每次呼叫建立 immutable 結果，且絕不能以 static、cache、singleton、timer、queue 或背景工作
/// 保留某一位使用者／小組的決策。任何 prepare 結果仍只是未來 governed cycle 的前置語意：真正
/// dispatch 必須另有 exact read-back、reconciliation、ledger 與 reverse-known-keys cleanup。
/// </para>
/// </summary>
public sealed class P72AttendanceUpsertLocalDecisionTests
{
    /// <summary>
    /// 保護 observation 的信任邊界只能是完整性、attendance cardinality、expected-state、
    /// uncertain-outcome 和已去識別化的週報決策。決定性斷言是 public instance surface 完全等於
    /// 此五項，防止未來把 record／weekly-report identifier、Owner、Contact 或 Profile 加入本機層，
    /// 使其成為跨使用者或跨小組可保留的 routing authority。
    /// </summary>
    [Fact]
    public void Observation_exposes_only_the_minimum_deidentified_local_decision_inputs()
    {
        var properties = typeof(P72AttendanceUpsertLocalObservation)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        properties.Should().Equal(
            "ExistingAttendanceCount",
            "HasUncertainOutcome",
            "IsComplete",
            "IsExpectedStateApplied",
            "WeeklyReportDecision");
    }

    /// <summary>
    /// download create 只有在完整 read-back 證明 attendance 恰為零，且週報為 zero-active 時才可
    /// 準備不關聯建立。決定性斷言是結果只描述 create-unlinked、要求 exact read-back 與已知 key
    /// 的逆序 cleanup；它不授權直接 dispatch、重播、接觸 Contact／Owner 或自動建立週報。
    /// </summary>
    [Fact]
    public void Resolve_prepares_unlinked_download_create_only_for_complete_zero_attendance()
    {
        var result = P72AttendanceUpsertLocalDecision.Resolve(
            P72AttendanceUpsertMode.DownloadCreate,
            CreateObservation(
                existingAttendanceCount: 0,
                isExpectedStateApplied: false,
                weeklyReportDecision: ResolveWeeklyReport(activeReportCount: 0)));

        result.Disposition.Should().Be(P72AttendanceUpsertLocalDisposition.PrepareCreateUnlinked);
        result.CanPrepareFutureDispatch.Should().BeTrue();
        result.RequiresExactReadBack.Should().BeTrue();
        result.ProhibitsReplay.Should().BeFalse();
        result.CleanupPolicy.Should().Be(P72LocalCleanupPolicy.ReverseKnownKeys);
        result.FailureCategory.Should().Be(P72AttendanceUpsertLocalFailureCategory.None);
    }

    /// <summary>
    /// 保護 upload upsert 的唯一二分語意：零筆只能準備 create，恰一筆且尚未達預期狀態時只能
    /// 準備 update。兩個案例都使用 exactly-one weekly-report，決定性斷言是結果只能宣告 linked
    /// enum 分支並要求 exact read-back，不能把週報 identity、contact 或 owner 放進本機結果。
    /// </summary>
    [Theory]
    [InlineData(0, P72AttendanceUpsertLocalDisposition.PrepareCreateLinked)]
    [InlineData(1, P72AttendanceUpsertLocalDisposition.PrepareUpdateLinked)]
    public void Resolve_prepares_upload_create_or_update_only_for_zero_or_exactly_one_attendance(
        int existingAttendanceCount,
        P72AttendanceUpsertLocalDisposition expectedDisposition)
    {
        var result = P72AttendanceUpsertLocalDecision.Resolve(
            P72AttendanceUpsertMode.UploadUpsert,
            CreateObservation(
                existingAttendanceCount,
                isExpectedStateApplied: false,
                weeklyReportDecision: ResolveWeeklyReport(activeReportCount: 1)));

        result.Disposition.Should().Be(expectedDisposition);
        result.CanPrepareFutureDispatch.Should().BeTrue();
        result.RequiresExactReadBack.Should().BeTrue();
        result.ProhibitsReplay.Should().BeFalse();
        result.CleanupPolicy.Should().Be(P72LocalCleanupPolicy.ReverseKnownKeys);
    }

    /// <summary>
    /// 保護 download create 不得因為已有一筆 attendance 而悄悄降級為 update。故障注入提供完整、
    /// 恰一筆、尚未達預期狀態的 observation；決定性斷言是 no-go 且禁止重播，避免下載流程修改
    /// 呼叫端未明確授權的既存紀錄。
    /// </summary>
    [Fact]
    public void Resolve_fails_closed_when_download_create_observes_an_existing_attendance()
    {
        var result = P72AttendanceUpsertLocalDecision.Resolve(
            P72AttendanceUpsertMode.DownloadCreate,
            CreateObservation(
                existingAttendanceCount: 1,
                isExpectedStateApplied: false,
                weeklyReportDecision: ResolveWeeklyReport(activeReportCount: 0)));

        result.Disposition.Should().Be(P72AttendanceUpsertLocalDisposition.NoGo);
        result.CanPrepareFutureDispatch.Should().BeFalse();
        result.ProhibitsReplay.Should().BeTrue();
        result.FailureCategory.Should().Be(P72AttendanceUpsertLocalFailureCategory.CreateTargetAlreadyExists);
    }

    /// <summary>
    /// 完整 read-back 已證明恰一筆 attendance 已達 expected state 時，upload upsert 必須回報
    /// already-applied，而不是再次更新。決定性斷言是沒有 prepare plan、沒有 replay，並保留
    /// reverse-known-keys cleanup 分類給未來 governed executor，而非在本機層接觸 CRM。
    /// </summary>
    [Fact]
    public void Resolve_returns_already_applied_without_replay_when_expected_state_is_proven()
    {
        var result = P72AttendanceUpsertLocalDecision.Resolve(
            P72AttendanceUpsertMode.UploadUpsert,
            CreateObservation(
                existingAttendanceCount: 1,
                isExpectedStateApplied: true,
                weeklyReportDecision: ResolveWeeklyReport(activeReportCount: 1)));

        result.Disposition.Should().Be(P72AttendanceUpsertLocalDisposition.AlreadyApplied);
        result.CanPrepareFutureDispatch.Should().BeFalse();
        result.RequiresExactReadBack.Should().BeFalse();
        result.ProhibitsReplay.Should().BeTrue();
        result.CleanupPolicy.Should().Be(P72LocalCleanupPolicy.ReverseKnownKeys);
        result.FailureCategory.Should().Be(P72AttendanceUpsertLocalFailureCategory.None);
    }

    /// <summary>
    /// 保護「沒有 attendance 卻宣稱 expected state 已套用」的自相矛盾 observation 必須 fail closed。
    ///
    /// <para>
    /// 此組合可能來自 stale cache、部分 read-back 或錯誤 mapping；它不能被降級成 fresh create，否則
    /// unknown remote state 可能被重播成新的 present record。故障注入對 download 與 upload 都提供零筆
    /// attendance 加上 true expected-state；決定性斷言是 unavailable no-go，沒有 plan、replay 或隱式
    /// contact／owner／group／follow-up mutation 權限。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(P72AttendanceUpsertMode.DownloadCreate)]
    [InlineData(P72AttendanceUpsertMode.UploadUpsert)]
    public void Resolve_fails_closed_when_zero_attendance_claims_expected_state_is_already_applied(
        P72AttendanceUpsertMode mode)
    {
        var result = P72AttendanceUpsertLocalDecision.Resolve(
            mode,
            CreateObservation(
                existingAttendanceCount: 0,
                isExpectedStateApplied: true,
                weeklyReportDecision: ResolveWeeklyReport(activeReportCount: 0)));

        result.Disposition.Should().Be(P72AttendanceUpsertLocalDisposition.NoGo);
        result.CanPrepareFutureDispatch.Should().BeFalse();
        result.FailureCategory.Should().Be(P72AttendanceUpsertLocalFailureCategory.Unavailable);
        result.ProhibitsReplay.Should().BeTrue();
    }

    /// <summary>
    /// timeout、paging／schema 不完整、負數 cardinality 與 uncertain outcome 都不能被推論為零筆。
    /// 故障注入分別覆蓋四種不可證明狀態；決定性斷言是所有案例均為 unavailable no-go 並禁止
    /// replay，防止 transport 不確定時對同一 attendance 做第二次 mutation。
    /// </summary>
    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(true, -1, false)]
    [InlineData(true, 0, true)]
    public void Resolve_fails_closed_for_incomplete_invalid_or_uncertain_observation(
        bool isComplete,
        int existingAttendanceCount,
        bool hasUncertainOutcome)
    {
        var result = P72AttendanceUpsertLocalDecision.Resolve(
            P72AttendanceUpsertMode.UploadUpsert,
            new P72AttendanceUpsertLocalObservation
            {
                IsComplete = isComplete,
                ExistingAttendanceCount = existingAttendanceCount,
                IsExpectedStateApplied = false,
                HasUncertainOutcome = hasUncertainOutcome,
                WeeklyReportDecision = ResolveWeeklyReport(activeReportCount: 0)
            });

        result.Disposition.Should().Be(P72AttendanceUpsertLocalDisposition.NoGo);
        result.CanPrepareFutureDispatch.Should().BeFalse();
        result.ProhibitsReplay.Should().BeTrue();
        result.FailureCategory.Should().Be(P72AttendanceUpsertLocalFailureCategory.Unavailable);
    }

    /// <summary>
    /// duplicate attendance 不是可任選一筆更新的訊號。故障注入提供兩筆完整投影；決定性斷言是
    /// duplicate-attendance no-go，且結果只能以 bounded enum 分類表達後續 reconciliation／cleanup
    /// 需要，不能輸出或猜選任何 record ID。
    /// </summary>
    [Fact]
    public void Resolve_fails_closed_without_replay_for_duplicate_attendance()
    {
        var result = P72AttendanceUpsertLocalDecision.Resolve(
            P72AttendanceUpsertMode.UploadUpsert,
            CreateObservation(
                existingAttendanceCount: 2,
                isExpectedStateApplied: false,
                weeklyReportDecision: ResolveWeeklyReport(activeReportCount: 0)));

        result.Disposition.Should().Be(P72AttendanceUpsertLocalDisposition.NoGo);
        result.CanPrepareFutureDispatch.Should().BeFalse();
        result.ProhibitsReplay.Should().BeTrue();
        result.FailureCategory.Should().Be(P72AttendanceUpsertLocalFailureCategory.DuplicateAttendance);
    }

    /// <summary>
    /// weekly report 為 duplicate-active 或 unavailable 時，即使 attendance cardinality 看似可寫也
    /// 不得產生 local plan。此測試重用既有週報 reducer 的 result，確保 attendance reducer 不自行
    /// 重算、忽略或放寬其 fail-closed 決策。
    /// </summary>
    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 0)]
    public void BuildPlan_returns_no_plan_when_weekly_report_decision_is_duplicate_or_unavailable(
        bool isWeeklyObservationComplete,
        int activeReportCount)
    {
        var result = P72AttendanceUpsertLocalPlanBuilder.Build(
            P72AttendanceUpsertMode.UploadUpsert,
            CreateObservation(
                existingAttendanceCount: 0,
                isExpectedStateApplied: false,
                weeklyReportDecision: P72AttendanceWeeklyReportDecision.Resolve(
                    new P72AttendanceWeeklyReportObservation
                    {
                        IsComplete = isWeeklyObservationComplete,
                        ActiveReportCount = activeReportCount
                    })),
            CreateInputs("attendance-a"));

        result.Succeeded.Should().BeFalse();
        result.Plan.Should().BeNull();
        result.Decision.Disposition.Should().Be(P72AttendanceUpsertLocalDisposition.NoGo);
        result.Decision.ProhibitsReplay.Should().BeTrue();
    }

    /// <summary>
    /// zero-active 必須形成不關聯 create plan；exactly-one-active 則形成要求精確連結 read-back 的
    /// create 或 update plan。三種 valid prepare decision 都只接受 attendanceKey、weekStartDate、
    /// presentState，並產生非空 local-only plan。決定性斷言是 input key 完全相等、計畫沒有 CE
    /// dispatch／產品 consumer 權限；opaque attendance key 只在本測試的 call stack 與 immutable plan
    /// snapshot 中存活。
    /// </summary>
    [Theory]
    [InlineData(0, 0, P72AttendanceUpsertLocalDisposition.PrepareCreateUnlinked)]
    [InlineData(1, 0, P72AttendanceUpsertLocalDisposition.PrepareCreateLinked)]
    [InlineData(1, 1, P72AttendanceUpsertLocalDisposition.PrepareUpdateLinked)]
    public void BuildPlan_accepts_exact_attendance_inputs_and_returns_the_required_nonempty_local_only_plan(
        int activeReportCount,
        int existingAttendanceCount,
        P72AttendanceUpsertLocalDisposition expectedDisposition)
    {
        var result = P72AttendanceUpsertLocalPlanBuilder.Build(
            P72AttendanceUpsertMode.UploadUpsert,
            CreateObservation(
                existingAttendanceCount,
                isExpectedStateApplied: false,
                weeklyReportDecision: ResolveWeeklyReport(activeReportCount)),
            CreateInputs("attendance-valid"));

        result.Succeeded.Should().BeTrue();
        result.Plan.Should().NotBeNull();
        result.Plan!.Inputs.Keys.Should().BeEquivalentTo(
            ["attendanceKey", "weekStartDate", "presentState"]);
        result.Plan.Inputs.Should().NotBeEmpty();
        result.Plan.CeDispatchAllowed.Should().BeFalse();
        result.Plan.ProductConsumerAllowed.Should().BeFalse();
        result.Decision.Disposition.Should().Be(expectedDisposition);
        result.Decision.RequiresExactReadBack.Should().BeTrue();
    }

    /// <summary>
    /// caller 不得把 owner、contact、profile 或空 attendance key 混入 prepare plan。故障注入同時
    /// 覆蓋一個額外 authority 欄位與一個空白必要值；決定性斷言是兩者都不回傳 partial plan，
    /// 因此 future executor 沒有可被誤 dispatch 的輸入集合。
    /// </summary>
    [Theory]
    [InlineData("owner", "caller-owner")]
    [InlineData("contact", "caller-contact")]
    [InlineData("profile", "caller-profile")]
    [InlineData("attendanceKey", "")]
    public void BuildPlan_rejects_extra_authority_or_empty_required_input_without_a_partial_plan(
        string inputName,
        string inputValue)
    {
        var inputs = CreateInputs("attendance-rejected");
        inputs[inputName] = inputValue;

        var result = P72AttendanceUpsertLocalPlanBuilder.Build(
            P72AttendanceUpsertMode.UploadUpsert,
            CreateObservation(
                existingAttendanceCount: 1,
                isExpectedStateApplied: false,
                weeklyReportDecision: ResolveWeeklyReport(activeReportCount: 0)),
            inputs);

        result.Succeeded.Should().BeFalse();
        result.Plan.Should().BeNull();
        result.FailureCategory.Should().Be(
            inputName == "attendanceKey"
                ? P72LocalPlanFailureCategory.InputValueInvalid
                : P72LocalPlanFailureCategory.InputNamesMismatch);
    }

    /// <summary>
    /// 本機結果的 operation disposition 只能是 create、update、unlinked、linked、already-applied、
    /// reconciliation、cleanup 或 no-go 類別；不得有 Contact、Owner、Group、FollowUp、record ID 或
    /// weekly-report ID 之類的 mutation authority。這是 API surface regression guard，而非 CE I/O 測試。
    /// </summary>
    [Fact]
    public void Disposition_enum_exposes_only_bounded_attendance_operation_categories()
    {
        Enum.GetNames<P72AttendanceUpsertLocalDisposition>().Should().BeEquivalentTo(
        [
            "NoGo",
            "PrepareCreateUnlinked",
            "PrepareCreateLinked",
            "PrepareUpdateUnlinked",
            "PrepareUpdateLinked",
            "AlreadyApplied",
            "RequireReconciliation",
            "RequireCleanup"
        ]);
    }

    /// <summary>
    /// A/B 同時呼叫 reducer 時必須只由各自的 immutable observation 決定結果。Barrier 強制兩個工作
    /// 一起跨越呼叫邊界；A 的 zero／unlinked 與 B 的 one／linked 形成可辨識的本機狀態，但不使用任何
    /// 使用者、tenant、profile 或 CRM marker。決定性斷言是兩個結果都保留自己的 disposition，證明
    /// reducer 不依賴上一個呼叫的 mutable state，也不需要資源 cleanup。
    /// </summary>
    [Fact]
    public async Task Resolve_keeps_concurrent_a_and_b_attendance_decisions_operation_local()
    {
        using var barrier = new Barrier(2);
        var results = new ConcurrentBag<P72AttendanceUpsertLocalDecisionResult>();

        var a = Task.Run(() => ResolveAfterBarrier(
            P72AttendanceUpsertMode.DownloadCreate,
            CreateObservation(0, false, ResolveWeeklyReport(activeReportCount: 0)),
            barrier,
            results));
        var b = Task.Run(() => ResolveAfterBarrier(
            P72AttendanceUpsertMode.UploadUpsert,
            CreateObservation(1, false, ResolveWeeklyReport(activeReportCount: 1)),
            barrier,
            results));

        await Task.WhenAll(a, b);

        results.Select(result => result.Disposition).Should().BeEquivalentTo(
        [
            P72AttendanceUpsertLocalDisposition.PrepareCreateUnlinked,
            P72AttendanceUpsertLocalDisposition.PrepareUpdateLinked
        ]);
    }

    /// <summary>
    /// 建立符合目前 attendance input allowlist 的有效字典。值是 bounded 測試資料，並非 CRM ID、
    /// Contact、Owner、Profile、endpoint 或 credential；每次呼叫都回傳新的 dictionary，避免測試自身
    /// 意外共用可變集合而掩蓋 production 的 A/B isolation defect。
    /// </summary>
    private static Dictionary<string, string?> CreateInputs(string attendanceKey)
        => new(StringComparer.Ordinal)
        {
            ["attendanceKey"] = attendanceKey,
            ["weekStartDate"] = "2026-08-09",
            ["presentState"] = "present"
        };

    /// <summary>
    /// 建立 attendance reducer 能接受的最小 observation。週報部分必須已由既有純 reducer 去識別化，
    /// 因此本 helper 不可能混入 CRM reference 或上游 raw query；它不配置外部資源，生命週期受單一
    /// 測試方法的 stack／GC 所界定。
    /// </summary>
    private static P72AttendanceUpsertLocalObservation CreateObservation(
        int existingAttendanceCount,
        bool isExpectedStateApplied,
        P72AttendanceWeeklyReportDecisionResult weeklyReportDecision)
        => new()
        {
            IsComplete = true,
            ExistingAttendanceCount = existingAttendanceCount,
            IsExpectedStateApplied = isExpectedStateApplied,
            HasUncertainOutcome = false,
            WeeklyReportDecision = weeklyReportDecision
        };

    /// <summary>
    /// 以既有週報 cardinality reducer 產生不含 weekly-report identity 的結果。此 helper 固定完整查詢，
    /// 使個別測試能明確控制 zero、exactly-one 與 duplicate 分支；unavailable 案例會直接在測試中
    /// 建立不完整 observation，避免把 fault injection 隱藏在 helper 裡。
    /// </summary>
    private static P72AttendanceWeeklyReportDecisionResult ResolveWeeklyReport(int activeReportCount)
        => P72AttendanceWeeklyReportDecision.Resolve(new P72AttendanceWeeklyReportObservation
        {
            IsComplete = true,
            ActiveReportCount = activeReportCount
        });

    /// <summary>
    /// 將 A/B 呼叫同步到同一個 reducer 邊界後才收集 immutable result。ConcurrentBag 的唯一 owner 是
    /// 這個測試，會隨方法結束釋放；production reducer 不得仿效把結果放進 static、cache 或 queue。
    /// </summary>
    private static void ResolveAfterBarrier(
        P72AttendanceUpsertMode mode,
        P72AttendanceUpsertLocalObservation observation,
        Barrier barrier,
        ConcurrentBag<P72AttendanceUpsertLocalDecisionResult> results)
    {
        barrier.SignalAndWait();
        results.Add(P72AttendanceUpsertLocalDecision.Resolve(mode, observation));
    }
}

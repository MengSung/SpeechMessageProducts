// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/P72AppointmentLocalDecisionTests.cs
// 用途：保護 P7.2 continuation Slice E appointment 的本機 cardinality、重播與部分完成契約。
//       測試不建立 CRM fixture、不呼叫 ToolUtility、connector、lease 或產品 consumer。
// ============================================================================

using System.Collections.Concurrent;
using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 appointment create/update 在 local-only 邊界的安全決策。
///
/// <para>
/// 舊流程的 create 可能接續 assign 與 schedule，任何一個步驟的部分完成都不能以相同請求重播。
/// 因此本測試只接受固定的 <c>create</c>/<c>update</c> mode 與去識別化的目標 cardinality；
/// 未完成、重複、缺少或已達目標狀態均會明確分類，不保存 Owner、CRM ID、Session、Principal、
/// profile、endpoint、credential、token 或 raw exception。所有決策都是 request-local immutable
/// scalar，確保 A/B 交錯不會互相污染。
/// </para>
/// </summary>
public sealed class P72AppointmentLocalDecisionTests
{
    /// <summary>
    /// 建立模式在完整且沒有既存目標時，只能準備一次未來受治理建立操作，並要求之後精確 read-back。
    /// </summary>
    [Fact]
    public void Resolve_prepares_create_for_complete_zero_record_observation()
    {
        var result = P72AppointmentLocalDecision.Resolve(
            P72AppointmentChangeMode.Create,
            new P72AppointmentLocalObservation
            {
                IsComplete = true,
                ExistingRecordCount = 0,
                IsTargetStateAlreadyApplied = false
            });

        result.CanPrepareFutureDispatch.Should().BeTrue();
        result.Disposition.Should().Be(P72AppointmentDisposition.PrepareCreate);
        result.RequiresExactReadBack.Should().BeTrue();
        result.ProhibitsReplay.Should().BeFalse();
        result.FailureCategory.Should().Be(P72AppointmentFailureCategory.None);
    }

    /// <summary>
    /// 更新模式必須先證明恰好一筆目標；零筆不是建立的替代條件，而是 target-missing no-go。
    /// </summary>
    [Fact]
    public void Resolve_fails_closed_for_update_when_target_is_missing()
    {
        var result = P72AppointmentLocalDecision.Resolve(
            P72AppointmentChangeMode.Update,
            new P72AppointmentLocalObservation
            {
                IsComplete = true,
                ExistingRecordCount = 0,
                IsTargetStateAlreadyApplied = false
            });

        result.CanPrepareFutureDispatch.Should().BeFalse();
        result.Disposition.Should().Be(P72AppointmentDisposition.NoGo);
        result.FailureCategory.Should().Be(P72AppointmentFailureCategory.TargetMissing);
        result.ProhibitsReplay.Should().BeTrue();
    }

    /// <summary>
    /// 更新模式在恰好一筆且尚未達目標狀態時，才可準備一次未來受治理更新。
    /// </summary>
    [Fact]
    public void Resolve_prepares_update_for_complete_exactly_one_target()
    {
        var result = P72AppointmentLocalDecision.Resolve(
            P72AppointmentChangeMode.Update,
            new P72AppointmentLocalObservation
            {
                IsComplete = true,
                ExistingRecordCount = 1,
                IsTargetStateAlreadyApplied = false
            });

        result.CanPrepareFutureDispatch.Should().BeTrue();
        result.Disposition.Should().Be(P72AppointmentDisposition.PrepareUpdate);
        result.RequiresExactReadBack.Should().BeTrue();
        result.ProhibitsReplay.Should().BeFalse();
    }

    /// <summary>
    /// 任一 mode 若 read-back 已證明目標狀態完成，必須回報 already-applied，不能再次更新或重新排程。
    /// </summary>
    [Theory]
    [InlineData(P72AppointmentChangeMode.Create, 1)]
    [InlineData(P72AppointmentChangeMode.Update, 1)]
    public void Resolve_does_not_replay_when_exact_target_state_is_already_applied(
        P72AppointmentChangeMode mode,
        int existingRecordCount)
    {
        var result = P72AppointmentLocalDecision.Resolve(
            mode,
            new P72AppointmentLocalObservation
            {
                IsComplete = true,
                ExistingRecordCount = existingRecordCount,
                IsTargetStateAlreadyApplied = true
            });

        result.CanPrepareFutureDispatch.Should().BeFalse();
        result.Disposition.Should().Be(P72AppointmentDisposition.AlreadyApplied);
        result.FailureCategory.Should().Be(P72AppointmentFailureCategory.None);
        result.ProhibitsReplay.Should().BeTrue();
    }

    /// <summary>
    /// 建立模式若已存在未達目標的一筆資料，不得把它當成可安全更新的替代路徑；這是明確衝突而非
    /// caller 可猜選的 owner／entity。決定性斷言是 target-already-exists no-go。
    /// </summary>
    [Fact]
    public void Resolve_fails_closed_for_create_when_a_target_already_exists()
    {
        var result = P72AppointmentLocalDecision.Resolve(
            P72AppointmentChangeMode.Create,
            new P72AppointmentLocalObservation
            {
                IsComplete = true,
                ExistingRecordCount = 1,
                IsTargetStateAlreadyApplied = false
            });

        result.CanPrepareFutureDispatch.Should().BeFalse();
        result.Disposition.Should().Be(P72AppointmentDisposition.NoGo);
        result.FailureCategory.Should().Be(P72AppointmentFailureCategory.TargetAlreadyExists);
        result.ProhibitsReplay.Should().BeTrue();
    }

    /// <summary>
    /// duplicate-active／duplicate-target 必須 fail closed，不得掃描或猜選其中一筆。
    /// </summary>
    [Theory]
    [InlineData(P72AppointmentChangeMode.Create)]
    [InlineData(P72AppointmentChangeMode.Update)]
    public void Resolve_fails_closed_for_duplicate_target_observation(P72AppointmentChangeMode mode)
    {
        var result = P72AppointmentLocalDecision.Resolve(
            mode,
            new P72AppointmentLocalObservation
            {
                IsComplete = true,
                ExistingRecordCount = 2,
                IsTargetStateAlreadyApplied = false
            });

        result.CanPrepareFutureDispatch.Should().BeFalse();
        result.Disposition.Should().Be(P72AppointmentDisposition.NoGo);
        result.FailureCategory.Should().Be(P72AppointmentFailureCategory.DuplicateTarget);
        result.ProhibitsReplay.Should().BeTrue();
    }

    /// <summary>
    /// timeout、schema 不完整與負數 cardinality 一律 unavailable no-go，即使 count 看似零也不能建立。
    /// </summary>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, -1)]
    public void Resolve_fails_closed_for_incomplete_or_invalid_observation(bool isComplete, int count)
    {
        var result = P72AppointmentLocalDecision.Resolve(
            P72AppointmentChangeMode.Create,
            new P72AppointmentLocalObservation
            {
                IsComplete = isComplete,
                ExistingRecordCount = count,
                IsTargetStateAlreadyApplied = false
            });

        result.CanPrepareFutureDispatch.Should().BeFalse();
        result.Disposition.Should().Be(P72AppointmentDisposition.NoGo);
        result.FailureCategory.Should().Be(P72AppointmentFailureCategory.Unavailable);
        result.ProhibitsReplay.Should().BeTrue();
    }

    /// <summary>
    /// A/B 交錯決策必須各自保有 marker 對應的 mode 與結果；production 層沒有 static、cache、Session、
    /// lease、client 或 background task 可保存上一個使用者的 appointment 狀態。
    /// </summary>
    [Fact]
    public async Task Resolve_keeps_interleaved_a_and_b_appointment_decisions_operation_local()
    {
        using var barrier = new Barrier(2);
        var results = new ConcurrentBag<P72AppointmentDecisionResult>();

        var a = Task.Run(() => ResolveAfterBarrier(
            P72AppointmentChangeMode.Create,
            new P72AppointmentLocalObservation
            {
                IsComplete = true,
                ExistingRecordCount = 0,
                IsTargetStateAlreadyApplied = false
            },
            barrier,
            results));
        var b = Task.Run(() => ResolveAfterBarrier(
            P72AppointmentChangeMode.Update,
            new P72AppointmentLocalObservation
            {
                IsComplete = true,
                ExistingRecordCount = 1,
                IsTargetStateAlreadyApplied = false
            },
            barrier,
            results));

        await Task.WhenAll(a, b);

        results.Select(result => result.Disposition).Should().BeEquivalentTo(
        [P72AppointmentDisposition.PrepareCreate, P72AppointmentDisposition.PrepareUpdate]);
    }

    /// <summary>
    /// local-only plan builder 只接受固定 appointment operation、fixture key 與 create/update mode，
    /// 並拒絕 caller authority。它永遠不會開啟 CE dispatch 或產品 consumer。
    /// </summary>
    [Fact]
    public void Build_creates_only_a_fail_closed_appointment_plan_for_a_valid_decision()
    {
        var result = P72AppointmentLocalPlanBuilder.Build(
            "create",
            new P72AppointmentLocalObservation
            {
                IsComplete = true,
                ExistingRecordCount = 0,
                IsTargetStateAlreadyApplied = false
            },
            "appointment-fixture-a");

        result.Succeeded.Should().BeTrue();
        result.Plan.Should().NotBeNull();
        result.Plan!.Definition.OperationId.Should().Be(OperationIds.AppointmentsEntityCreateOrUpdate);
        result.Plan.CeDispatchAllowed.Should().BeFalse();
        result.Plan.ProductConsumerAllowed.Should().BeFalse();
    }

    /// <summary>
    /// caller 指定 owner、profile 或其他額外 authority 時，不會形成部分計畫。
    /// </summary>
    [Fact]
    public void Build_rejects_extra_authority_without_a_partial_plan()
    {
        var result = P72AppointmentLocalPlanBuilder.Build(
            "update",
            new P72AppointmentLocalObservation
            {
                IsComplete = true,
                ExistingRecordCount = 1,
                IsTargetStateAlreadyApplied = false
            },
            "appointment-fixture-b",
            new Dictionary<string, string?>
            {
                ["owner"] = "caller-owner"
            });

        result.Succeeded.Should().BeFalse();
        result.Plan.Should().BeNull();
        result.FailureCategory.Should().Be(P72LocalPlanFailureCategory.InputNamesMismatch);
    }

    /// <summary>
    /// 不支援的 mode 不可落入預設 create，也不可產生 local-only plan。故障注入是 caller 傳入大小寫
    /// 不符的文字；決定性斷言是 bounded input error 與 unavailable no-go 同時保留，沒有任何 executor
    /// 或 product consumer 可用的 plan。
    /// </summary>
    [Fact]
    public void Build_rejects_noncanonical_change_mode_without_a_partial_plan()
    {
        var result = P72AppointmentLocalPlanBuilder.Build(
            "Create",
            new P72AppointmentLocalObservation
            {
                IsComplete = true,
                ExistingRecordCount = 0,
                IsTargetStateAlreadyApplied = false
            },
            "appointment-fixture-c");

        result.Succeeded.Should().BeFalse();
        result.Plan.Should().BeNull();
        result.FailureCategory.Should().Be(P72LocalPlanFailureCategory.InputValueInvalid);
        result.Decision.Disposition.Should().Be(P72AppointmentDisposition.NoGo);
        result.Decision.FailureCategory.Should().Be(P72AppointmentFailureCategory.Unavailable);
    }

    private static void ResolveAfterBarrier(
        P72AppointmentChangeMode mode,
        P72AppointmentLocalObservation observation,
        Barrier barrier,
        ConcurrentBag<P72AppointmentDecisionResult> results)
    {
        barrier.SignalAndWait();
        results.Add(P72AppointmentLocalDecision.Resolve(mode, observation));
    }
}

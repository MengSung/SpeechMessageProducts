// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/P72FeeLessonsLocalDecisionTests.cs
// 用途：以 TDD 固定 P7.2 continuation Slice G fee／stor-lesson 的純本機決策、草稿與
//       local-only plan 契約；測試絕不建立 CRM fixture、呼叫 CE、connector、lease 或產品 consumer。
// ============================================================================

using System.Collections.Concurrent;
using System.Reflection;
using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 fee／stor-lesson Slice G 在取得受治理實證以前，只能產生不含身分、金額、CRM ID、
/// profile 或連線資訊的 operation-local 判定與計畫。
/// <para>
/// 此測試保護的合約是：每一次呼叫只使用呼叫堆疊中的 immutable scalar；決策器與 builder
/// 不得保存 A/B 的資料至 static、Session、cache、queue、timer 或背景工作。所有可準備的
/// mutation 仍必須等待未來受治理 dispatch 的 exact read-back；本機結果本身不授權 CE 寫入。
/// 測試刻意注入部分完成、timeout／不確定結果、重複 fee 與 owner 指派失敗，證明這些情況
/// 都會 fail closed，而不是把不完整的狀態重播給另一個 request。
/// </para>
/// </summary>
public sealed class P72FeeLessonsLocalDecisionTests
{
    /// <summary>
    /// 保護 stage draft 僅為單次 operation 擁有的 immutable local state：有效的 draftKey 與
    /// changeSet 只能建立 catalog 固定的 in-memory plan，且唯一 cleanup 為丟棄 operation-local
    /// state。決定性斷言同時確認此分支既不允許 CE dispatch，也不允許產品 consumer。
    /// </summary>
    [Fact]
    public void StageDraft_creates_an_immutable_per_operation_local_plan_with_discard_only_cleanup()
    {
        var result = P72FeeLessonsLocalPlanBuilder.StageDraft(
            "fee-draft-a",
            "stor-lesson-state=ready");

        result.Succeeded.Should().BeTrue();
        result.FailureCategory.Should().Be(P72LocalPlanFailureCategory.None);
        result.Plan.Should().NotBeNull();
        result.Plan!.Definition.OperationId.Should().Be(OperationIds.FeesEditorStageInmemoryChange);
        result.Plan.Definition.MutationPolicy.Should().Be(P72LocalMutationPolicy.OperationLocalInMemoryOnly);
        result.Plan.Definition.ReadBackPolicy.Should().Be(P72LocalReadBackPolicy.LocalStateProjection);
        result.Plan.Definition.TimeoutPolicy.Should().Be(P72LocalTimeoutPolicy.NoExternalDispatch);
        result.Plan.Definition.CleanupPolicy.Should().Be(P72LocalCleanupPolicy.DiscardOperationLocalState);
        result.Plan.Inputs.Should().BeEquivalentTo(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["draftKey"] = "fee-draft-a",
            ["changeSet"] = "stor-lesson-state=ready"
        });
        result.Plan.CeDispatchAllowed.Should().BeFalse();
        result.Plan.ProductConsumerAllowed.Should().BeFalse();
    }

    /// <summary>
    /// 保護 stage 邊界只接受非空且受長度上限保護的 draftKey／changeSet；本案例分別注入空白、
    /// 超長值與 caller 偽造的 profile authority。任何一項不合法時都不得保留 partial draft
    /// 或建立 plan，避免後續 request 撿取先前草稿。
    /// </summary>
    [Fact]
    public void StageDraft_rejects_invalid_or_extra_input_without_retaining_a_partial_draft()
    {
        var tooLong = new string('x', P72ContinuationLocalOnlyPlanBuilder.MaximumInputValueCharacters + 1);

        var emptyDraftKey = P72FeeLessonsLocalPlanBuilder.StageDraft(null, "change-set");
        var blankChangeSet = P72FeeLessonsLocalPlanBuilder.StageDraft("fee-draft-b", "   ");
        var overlongDraftKey = P72FeeLessonsLocalPlanBuilder.StageDraft(tooLong, "change-set");
        var extraAuthority = P72FeeLessonsLocalPlanBuilder.StageDraft(
            "fee-draft-c",
            "change-set",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["profile"] = "caller-profile"
            });

        AssertNoPlan(emptyDraftKey, P72LocalPlanFailureCategory.InputValueInvalid);
        AssertNoPlan(blankChangeSet, P72LocalPlanFailureCategory.InputValueInvalid);
        AssertNoPlan(overlongDraftKey, P72LocalPlanFailureCategory.InputValueInvalid);
        AssertNoPlan(extraAuthority, P72LocalPlanFailureCategory.InputNamesMismatch);
    }

    /// <summary>
    /// 保護完整且可證明兩個目標狀態都可更新的 fee／stor-lesson observation，只能準備一次未來
    /// 受治理 dispatch，並強制 exact read-back。此案例不包含任何 CRM ID、owner、金額或 profile，
    /// 因而不能被誤用為直接 mutation payload。
    /// </summary>
    [Fact]
    public void Resolve_prepares_future_governed_update_only_for_complete_expected_states()
    {
        var result = P72FeeLessonsLocalDecision.Resolve(
            P72FeeLessonsChangeMode.UpdateFeeByStorLesson,
            CreateUpdateObservation());

        result.CanPrepareFutureDispatch.Should().BeTrue();
        result.Disposition.Should().Be(P72FeeLessonsDisposition.PrepareFutureGovernedDispatch);
        result.FailureCategory.Should().Be(P72FeeLessonsFailureCategory.None);
        result.RequiresExactReadBack.Should().BeTrue();
        result.ProhibitsReplay.Should().BeFalse();
    }

    /// <summary>
    /// 保護受控 create 的唯一可前進條件：read-back 完整、尚未存在 fee、stor-lesson 仍在預期
    /// 狀態，且沒有 partial completion 或不確定結果。成功判定也只能準備未來治理流程，不能
    /// 直接執行 CE 或把 create 結果交給產品 consumer。
    /// </summary>
    [Fact]
    public void Resolve_prepares_future_governed_create_only_for_a_complete_controlled_observation()
    {
        var result = P72FeeLessonsLocalDecision.Resolve(
            P72FeeLessonsChangeMode.CreateFeeFromStorLesson,
            new P72FeeLessonsLocalObservation
            {
                IsComplete = true,
                FeeExists = false,
                StorLessonExpectedState = true,
                OwnerAssigned = true,
                HasPartialCompletion = false,
                HasUncertainOutcome = false
            });

        result.CanPrepareFutureDispatch.Should().BeTrue();
        result.Disposition.Should().Be(P72FeeLessonsDisposition.PrepareFutureGovernedDispatch);
        result.RequiresExactReadBack.Should().BeTrue();
        result.ProhibitsReplay.Should().BeFalse();
    }

    /// <summary>
    /// 保護 create 前的 owner 指派準備狀態也必須由完整 read-back 證明。
    ///
    /// <para>
    /// fee 尚不存在不等於未來 owner assignment 一定安全；若 server-owned owner derivation 尚未完成，
    /// 直接建立 fee 會重現舊流程「先建立、後指派失敗」的部分完成風險。故障注入讓 fee 保持不存在，
    /// 但將 <c>OwnerAssigned</c> 設為 false；決定性斷言是 unavailable no-go 與 no replay，而不是
    /// 準備一份可被下一層誤 dispatch 的 plan。
    /// </para>
    /// </summary>
    [Fact]
    public void Resolve_fails_closed_for_create_without_proven_owner_assignment_readiness()
    {
        var result = P72FeeLessonsLocalDecision.Resolve(
            P72FeeLessonsChangeMode.CreateFeeFromStorLesson,
            new P72FeeLessonsLocalObservation
            {
                IsComplete = true,
                FeeExists = false,
                StorLessonExpectedState = true,
                OwnerAssigned = false,
                HasPartialCompletion = false,
                HasUncertainOutcome = false
            });

        result.CanPrepareFutureDispatch.Should().BeFalse();
        result.Disposition.Should().Be(P72FeeLessonsDisposition.NoGo);
        result.FailureCategory.Should().Be(P72FeeLessonsFailureCategory.Unavailable);
        result.ProhibitsReplay.Should().BeTrue();
    }

    /// <summary>
    /// 保護 create 的既存 fee read-back 只能被歸類為 already-applied，且禁止 replay。這可避免
    /// timeout 後重試建立第二筆費用，亦不把既存實體的 ID 或 owner 回傳給本機呼叫端。
    /// </summary>
    [Fact]
    public void Resolve_does_not_replay_create_when_a_fee_already_exists()
    {
        var result = P72FeeLessonsLocalDecision.Resolve(
            P72FeeLessonsChangeMode.CreateFeeFromStorLesson,
            new P72FeeLessonsLocalObservation
            {
                IsComplete = true,
                FeeExists = true,
                StorLessonExpectedState = true,
                OwnerAssigned = true,
                HasPartialCompletion = false,
                HasUncertainOutcome = false
            });

        result.CanPrepareFutureDispatch.Should().BeFalse();
        result.Disposition.Should().Be(P72FeeLessonsDisposition.AlreadyApplied);
        result.ProhibitsReplay.Should().BeTrue();
    }

    /// <summary>
    /// 保護「fee 已建立但 owner 指派失敗」這種多步 create 的部分完成案例：不得自動 replay，
    /// 必須要求 reconciliation 與 known-key cleanup，讓未來受治理層以 task-owned key 查回並
    /// 依固定順序清理；本機決策絕不接受或保存 owner identity。
    /// </summary>
    [Fact]
    public void Resolve_requires_reconciliation_and_known_key_cleanup_when_create_owner_assignment_partially_fails()
    {
        var result = P72FeeLessonsLocalDecision.Resolve(
            P72FeeLessonsChangeMode.CreateFeeFromStorLesson,
            new P72FeeLessonsLocalObservation
            {
                IsComplete = true,
                FeeExists = true,
                StorLessonExpectedState = true,
                OwnerAssigned = false,
                HasPartialCompletion = true,
                HasUncertainOutcome = false
            });

        result.CanPrepareFutureDispatch.Should().BeFalse();
        result.Disposition.Should().Be(P72FeeLessonsDisposition.RequireReconciliation);
        result.RequiresKnownKeyCleanup.Should().BeTrue();
        result.ProhibitsReplay.Should().BeTrue();
    }

    /// <summary>
    /// 保護任何 incomplete read-back、partial completion、timeout／uncertain outcome、未預期
    /// stor-lesson 狀態或未定義 mode 都會 fail closed。故障注入只使用純 scalar observation；
    /// 決定性斷言確認沒有情況會被誤判為可 dispatch 或可重播。
    /// </summary>
    [Theory]
    [InlineData(false, true, true, true, false, false)]
    [InlineData(true, true, true, true, true, false)]
    [InlineData(true, true, true, true, false, true)]
    [InlineData(true, true, false, true, false, false)]
    public void Resolve_fails_closed_for_incomplete_partial_uncertain_or_unexpected_state(
        bool isComplete,
        bool feeExists,
        bool storLessonExpectedState,
        bool ownerAssigned,
        bool hasPartialCompletion,
        bool hasUncertainOutcome)
    {
        var result = P72FeeLessonsLocalDecision.Resolve(
            P72FeeLessonsChangeMode.UpdateFeeByStorLesson,
            new P72FeeLessonsLocalObservation
            {
                IsComplete = isComplete,
                FeeExists = feeExists,
                StorLessonExpectedState = storLessonExpectedState,
                OwnerAssigned = ownerAssigned,
                HasPartialCompletion = hasPartialCompletion,
                HasUncertainOutcome = hasUncertainOutcome
            });

        result.CanPrepareFutureDispatch.Should().BeFalse();
        result.Disposition.Should().Be(P72FeeLessonsDisposition.NoGo);
        result.ProhibitsReplay.Should().BeTrue();
    }

    /// <summary>
    /// 保護 null observation 與未定義的 change mode 沒有可恢復的預設路徑。這兩個輸入代表受治理
    /// read-back 不存在或呼叫端嘗試跨越 allowlist；結果必須是無外部副作用的 no-go，而不是猜測
    /// 先前 request 的狀態或重用它的 draft。
    /// </summary>
    [Fact]
    public void Resolve_fails_closed_for_null_observation_or_unknown_change_mode()
    {
        var nullObservation = P72FeeLessonsLocalDecision.Resolve(
            P72FeeLessonsChangeMode.CreateFeeFromStorLesson,
            null);
        var unknownMode = P72FeeLessonsLocalDecision.Resolve(
            (P72FeeLessonsChangeMode)999,
            CreateUpdateObservation());

        nullObservation.CanPrepareFutureDispatch.Should().BeFalse();
        nullObservation.Disposition.Should().Be(P72FeeLessonsDisposition.NoGo);
        nullObservation.ProhibitsReplay.Should().BeTrue();
        unknownMode.CanPrepareFutureDispatch.Should().BeFalse();
        unknownMode.Disposition.Should().Be(P72FeeLessonsDisposition.NoGo);
        unknownMode.ProhibitsReplay.Should().BeTrue();
    }

    /// <summary>
    /// 保護 update builder 的邊界只使用 fixtureKey 與 changeSet，並讓已通過的本機決策轉成
    /// catalog 固定、不可 dispatch 的 plan。read-back 的 exactness 留在 decision，plan 只保存
    /// 有長度上限的 opaque scalar snapshot，不能攜帶 caller 的 CRM authority。
    /// </summary>
    [Fact]
    public void BuildUpdate_prepares_only_a_bounded_local_plan_with_fixture_key_and_change_set()
    {
        var result = P72FeeLessonsLocalPlanBuilder.BuildUpdateFeeByStorLesson(
            CreateUpdateObservation(),
            "fee-fixture-update-a",
            "stor-lesson-state=completed");

        result.Succeeded.Should().BeTrue();
        result.Decision.Disposition.Should().Be(P72FeeLessonsDisposition.PrepareFutureGovernedDispatch);
        result.Plan.Should().NotBeNull();
        result.Plan!.Definition.OperationId.Should().Be(OperationIds.FeesEditorUpdateByStorLesson);
        result.Plan.Inputs.Should().BeEquivalentTo(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["fixtureKey"] = "fee-fixture-update-a",
            ["changeSet"] = "stor-lesson-state=completed"
        });
        result.Plan.CeDispatchAllowed.Should().BeFalse();
        result.Plan.ProductConsumerAllowed.Should().BeFalse();
    }

    /// <summary>
    /// 保護 create builder 的邊界比 update 更窄：它只能使用 fixtureKey，不能由 caller 附帶
    /// changeSet、owner、profile 或 endpoint。成功結果仍是 local-only plan，任何實際 create、
    /// owner 指派、reconciliation 與 cleanup 都只能由後續受治理流程擁有。
    /// </summary>
    [Fact]
    public void BuildCreate_prepares_only_a_bounded_local_plan_with_fixture_key()
    {
        var result = P72FeeLessonsLocalPlanBuilder.BuildCreateFeeFromStorLesson(
            new P72FeeLessonsLocalObservation
            {
                IsComplete = true,
                FeeExists = false,
                StorLessonExpectedState = true,
                OwnerAssigned = true,
                HasPartialCompletion = false,
                HasUncertainOutcome = false
            },
            "fee-fixture-create-a");

        result.Succeeded.Should().BeTrue();
        result.Decision.Disposition.Should().Be(P72FeeLessonsDisposition.PrepareFutureGovernedDispatch);
        result.Plan.Should().NotBeNull();
        result.Plan!.Definition.OperationId.Should().Be(OperationIds.FeesCreateFromStorLesson);
        result.Plan.Inputs.Should().BeEquivalentTo(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["fixtureKey"] = "fee-fixture-create-a"
        });
        result.Plan.CeDispatchAllowed.Should().BeFalse();
        result.Plan.ProductConsumerAllowed.Should().BeFalse();
    }

    /// <summary>
    /// 保護三個 Slice G builder 的固定輸入邊界。故障注入涵蓋 owner、profile、endpoint 等 caller
    /// authority，以及空白與超長 key／changeSet；每個斷言都要求 no plan，避免 partial plan
    /// 遺留在記憶體、Session 或下一個 A/B request 可見的位置。
    /// </summary>
    [Fact]
    public void Builders_reject_extra_authority_empty_or_overlong_values_without_a_plan()
    {
        var tooLong = new string('x', P72ContinuationLocalOnlyPlanBuilder.MaximumInputValueCharacters + 1);

        var updateWithOwner = P72FeeLessonsLocalPlanBuilder.BuildUpdateFeeByStorLesson(
            CreateUpdateObservation(),
            "fee-fixture-update-b",
            "state=ready",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["owner"] = "caller-owner"
            });
        var createWithProfile = P72FeeLessonsLocalPlanBuilder.BuildCreateFeeFromStorLesson(
            CreateControlledCreateObservation(),
            "fee-fixture-create-b",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["profile"] = "caller-profile"
            });
        var updateWithEndpoint = P72FeeLessonsLocalPlanBuilder.BuildUpdateFeeByStorLesson(
            CreateUpdateObservation(),
            "fee-fixture-update-c",
            "state=ready",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["endpoint"] = "https://caller.invalid"
            });
        var emptyCreateFixture = P72FeeLessonsLocalPlanBuilder.BuildCreateFeeFromStorLesson(
            CreateControlledCreateObservation(),
            "   ");
        var overlongUpdateChangeSet = P72FeeLessonsLocalPlanBuilder.BuildUpdateFeeByStorLesson(
            CreateUpdateObservation(),
            "fee-fixture-update-d",
            tooLong);

        AssertNoPlan(updateWithOwner, P72LocalPlanFailureCategory.InputNamesMismatch);
        AssertNoPlan(createWithProfile, P72LocalPlanFailureCategory.InputNamesMismatch);
        AssertNoPlan(updateWithEndpoint, P72LocalPlanFailureCategory.InputNamesMismatch);
        AssertNoPlan(emptyCreateFixture, P72LocalPlanFailureCategory.InputValueInvalid);
        AssertNoPlan(overlongUpdateChangeSet, P72LocalPlanFailureCategory.InputValueInvalid);
    }

    /// <summary>
    /// 保護 A/B 同時取得不同 observation 時，結果只由各自 immutable input 決定。A 是可前進的
    /// update，B 是 uncertain no-go；透過 barrier 讓兩個呼叫交錯，決定性斷言確認沒有 shared
    /// mutable state、前一個 decision、計畫或 subject marker 被另一個呼叫重用。
    /// </summary>
    [Fact]
    public async Task Resolve_keeps_concurrent_a_and_b_fee_lesson_decisions_operation_local()
    {
        using var barrier = new Barrier(2);
        var results = new ConcurrentBag<P72FeeLessonsDecisionResult>();

        var a = Task.Run(() => ResolveAfterBarrier(
            P72FeeLessonsChangeMode.UpdateFeeByStorLesson,
            CreateUpdateObservation(),
            barrier,
            results));
        var b = Task.Run(() => ResolveAfterBarrier(
            P72FeeLessonsChangeMode.CreateFeeFromStorLesson,
            new P72FeeLessonsLocalObservation
            {
                IsComplete = true,
                FeeExists = false,
                StorLessonExpectedState = true,
                OwnerAssigned = true,
                HasPartialCompletion = false,
                HasUncertainOutcome = true
            },
            barrier,
            results));

        await Task.WhenAll(a, b);

        results.Select(result => result.Disposition).Should().BeEquivalentTo(
        [
            P72FeeLessonsDisposition.PrepareFutureGovernedDispatch,
            P72FeeLessonsDisposition.NoGo
        ]);
    }

    /// <summary>
    /// 保護 observation 是最小、去識別化的 read-back projection。反射斷言刻意排除任何 ID、
    /// owner identity、amount、profile、endpoint 或原始 CRM entity；此形狀讓決策器沒有可跨使用者
    /// 保留的敏感 mutable state，也迫使實際 reconciliation 使用後續受治理的 known-key owner。
    /// </summary>
    [Fact]
    public void Observation_exposes_only_the_six_sanitized_fee_lesson_state_flags()
    {
        var names = typeof(P72FeeLessonsLocalObservation)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        names.Should().Equal(
            "FeeExists",
            "HasPartialCompletion",
            "HasUncertainOutcome",
            "IsComplete",
            "OwnerAssigned",
            "StorLessonExpectedState");
    }

    /// <summary>
    /// 建立可安全準備 update 的最小、完整、去識別化 observation。此 helper 不持有 fixture、
    /// subject、profile 或 CRM 資源；其唯一用途是讓每個測試明確表達受保護的狀態組合。
    /// </summary>
    private static P72FeeLessonsLocalObservation CreateUpdateObservation()
        => new()
        {
            IsComplete = true,
            FeeExists = true,
            StorLessonExpectedState = true,
            OwnerAssigned = true,
            HasPartialCompletion = false,
            HasUncertainOutcome = false
        };

    /// <summary>
    /// 建立受控 create 的最小 observation；fee 尚不存在，但 stor-lesson 已通過受治理 read-back
    /// 的預期狀態確認。此物件在呼叫端建立並立即傳入，沒有共享 owner、Session 或 cache 壽命。
    /// </summary>
    private static P72FeeLessonsLocalObservation CreateControlledCreateObservation()
        => new()
        {
            IsComplete = true,
            FeeExists = false,
            StorLessonExpectedState = true,
            OwnerAssigned = true,
            HasPartialCompletion = false,
            HasUncertainOutcome = false
        };

    /// <summary>
    /// 斷言輸入驗證失敗時沒有 partial plan。這個集中 assertion 保護所有 builder 的 resource
    /// boundary：失敗結果只能含 bounded 分類，不能留下可由後續 request dispatch 或消費的計畫。
    /// </summary>
    private static void AssertNoPlan(
        P72FeeLessonsLocalPlanBuildResult result,
        P72LocalPlanFailureCategory failureCategory)
    {
        result.Succeeded.Should().BeFalse();
        result.FailureCategory.Should().Be(failureCategory);
        result.Plan.Should().BeNull();
    }

    /// <summary>
    /// 讓兩個純本機決策在相同同步點交錯執行，以暴露 static、cache 或 Session 重用造成的 A/B
    /// 洩漏。barrier 與 concurrent bag 的生命週期由測試方法擁有並在完成後釋放；production
    /// decision API 只接收 immutable scalar observation，因此不得保存任何這些測試物件。
    /// </summary>
    private static void ResolveAfterBarrier(
        P72FeeLessonsChangeMode mode,
        P72FeeLessonsLocalObservation observation,
        Barrier barrier,
        ConcurrentBag<P72FeeLessonsDecisionResult> results)
    {
        barrier.SignalAndWait();
        results.Add(P72FeeLessonsLocalDecision.Resolve(mode, observation));
    }
}

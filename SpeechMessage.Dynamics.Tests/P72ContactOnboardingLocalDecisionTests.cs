// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/P72ContactOnboardingLocalDecisionTests.cs
// 用途：以純本機、零 I/O 的紅燈契約測試，保護 P7.2 Slice F 新人聯絡人 onboarding
//       的安全決策、不可重播、精確讀回、反向清理與 A/B 請求隔離要求。
// ============================================================================

using System.Collections.Concurrent;
using System.Reflection;
using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 Slice F 新人聯絡人 onboarding 的純本機安全決策契約。
///
/// <para>
/// 此測試類別刻意不建立 CRM 用戶端、connector、lease、Session、背景工作、計時器、快取或任何
/// 網路 I/O。它只將不可變的 read-back 布林摘要送入 reducer，確保未來受治理的 executor 只能在
/// 「聯絡人及所有 graph 子步驟均未發生、讀回完整且沒有不確定結果」時取得一份 local-only plan。
/// 任一既有記錄、部分完成、通知曾被嘗試、逾時或不完整讀回都必須 fail closed，不能透過重播來
/// 猜測遠端狀態。
/// </para>
///
/// <para>
/// 測試同時保護 A/B 併發決策的 request-local 特性：結果不得寫入 static、singleton、共享集合或
/// Session，因此 A 的全新 graph 不得因 B 的既有 contact 而改變，反之亦然。清理順序只描述
/// 未來由 ledger 擁有者執行的已知 graph 節點；LINE 通知沒有可安全重播或刪除的補償動作，故不得
/// 出現在清理序列中。
/// </para>
/// </summary>
public sealed class P72ContactOnboardingLocalDecisionTests
{
    /// <summary>
    /// 保護 read-back observation 的最小化資料邊界。
    ///
    /// <para>
    /// 決定性斷言是公開 instance properties 必須恰好是七個布林旗標；其中沒有 CRM ID、owner、
    /// profile、endpoint、payload、principal 或例外內容。此限制讓 reducer 無法跨請求保留可識別
    /// 的使用者、租戶或設定檔資料，也避免 local-only plan 成為傳遞遠端權限的旁路。
    /// </para>
    /// </summary>
    [Fact]
    public void Observation_exposes_only_the_allowlisted_boolean_read_back_flags()
    {
        var properties = typeof(P72ContactOnboardingLocalObservation)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();

        properties.Select(property => property.Name).Should().Equal(
        [
            "ContactExists",
            "HasUncertainOutcome",
            "IsComplete",
            "MembershipCreated",
            "NotificationAttempted",
            "OwnerAssigned",
            "PresentRecordCreated"
        ]);
        properties.Should().OnlyContain(property => property.PropertyType == typeof(bool));
    }

    /// <summary>
    /// 保護完全且全新的 graph 僅能準備一次未來受治理 dispatch 的成功路徑。
    ///
    /// <para>
    /// 故障注入為所有已套用與不確定旗標皆為 <see langword="false"/>；決定性斷言是結果要求 exact
    /// read-back、允許 local-only plan 前置作業且尚未禁止重播。此結果本身不執行 CRM 寫入，真正
    /// dispatch、ledger、reconcile 與資源釋放仍由後續具伺服器端授權邊界的單一 owner 負責。
    /// </para>
    /// </summary>
    [Fact]
    public void Resolve_prepares_future_governed_dispatch_only_for_a_complete_fresh_graph()
    {
        var result = P72ContactOnboardingLocalDecision.Resolve(
            P72ContactOnboardingMode.CreateFullGraph,
            FreshObservation());

        result.CanPrepareFutureDispatch.Should().BeTrue();
        result.Disposition.Should().Be(P72ContactOnboardingDisposition.PrepareFutureGovernedDispatch);
        result.RequiresExactReadBack.Should().BeTrue();
        result.ProhibitsReplay.Should().BeFalse();
        result.FailureCategory.Should().Be(P72ContactOnboardingFailureCategory.None);
    }

    /// <summary>
    /// 保護已存在 contact 或任一可 ledger 化 graph 子步驟時的不可重播規則。
    ///
    /// <para>
    /// 每一組輸入模擬 contact、owner assignment、membership 或 present record 已經由先前請求完成。
    /// 決定性斷言是 reducer 回傳 <c>AlreadyApplied</c>，不建立新的 dispatch 途徑並明確禁止 replay；
    /// 因為缺少本次請求擁有的已知 ID 時，重播或猜測清理都可能覆寫另一位使用者的資料。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void Resolve_marks_an_existing_graph_node_as_already_applied(
        bool contactExists,
        bool ownerAssigned,
        bool membershipCreated,
        bool presentRecordCreated)
    {
        var result = P72ContactOnboardingLocalDecision.Resolve(
            P72ContactOnboardingMode.CreateFullGraph,
            new P72ContactOnboardingLocalObservation
            {
                IsComplete = true,
                ContactExists = contactExists,
                OwnerAssigned = ownerAssigned,
                MembershipCreated = membershipCreated,
                PresentRecordCreated = presentRecordCreated,
                NotificationAttempted = false,
                HasUncertainOutcome = false
            });

        result.CanPrepareFutureDispatch.Should().BeFalse();
        result.Disposition.Should().Be(P72ContactOnboardingDisposition.AlreadyApplied);
        result.ProhibitsReplay.Should().BeTrue();
        result.FailureCategory.Should().Be(P72ContactOnboardingFailureCategory.None);
    }

    /// <summary>
    /// 保護通知曾被嘗試後的 fail-closed 規則。
    ///
    /// <para>
    /// 通知系統可能在呼叫端逾時前已收到訊息，因此 <c>NotificationAttempted</c> 不是可安全刪除或
    /// 重送的 graph ledger 節點。決定性斷言是結果為 <c>NoGo</c> 且禁止 replay，避免以重送通知的
    /// 方式向同一人或另一個 profile 產生重複、錯置或跨請求的副作用。
    /// </para>
    /// </summary>
    [Fact]
    public void Resolve_fails_closed_when_notification_was_attempted()
    {
        var result = P72ContactOnboardingLocalDecision.Resolve(
            P72ContactOnboardingMode.CreateFullGraph,
            new P72ContactOnboardingLocalObservation
            {
                IsComplete = true,
                ContactExists = false,
                OwnerAssigned = false,
                MembershipCreated = false,
                PresentRecordCreated = false,
                NotificationAttempted = true,
                HasUncertainOutcome = false
            });

        result.CanPrepareFutureDispatch.Should().BeFalse();
        result.Disposition.Should().Be(P72ContactOnboardingDisposition.NoGo);
        result.FailureCategory.Should().Be(P72ContactOnboardingFailureCategory.Unavailable);
        result.ProhibitsReplay.Should().BeTrue();
    }

    /// <summary>
    /// 保護 timeout、取消或其他不確定遠端結果不會重新播放 onboarding mutation。
    ///
    /// <para>
    /// 故障注入使用 <c>HasUncertainOutcome</c>，代表 connector、傳輸或 read-back 的結論無法證明
    /// 遠端基線。決定性斷言是無條件 <c>NoGo</c>；未來只能由具完整 isolation boundary 的 reconciler
    /// 以精確讀回和本次 ledger 處理，而不能藉由本機 reducer 重新建立 contact graph。
    /// </para>
    /// </summary>
    [Fact]
    public void Resolve_fails_closed_for_an_uncertain_outcome()
    {
        var result = P72ContactOnboardingLocalDecision.Resolve(
            P72ContactOnboardingMode.CreateFullGraph,
            new P72ContactOnboardingLocalObservation
            {
                IsComplete = true,
                ContactExists = false,
                OwnerAssigned = false,
                MembershipCreated = false,
                PresentRecordCreated = false,
                NotificationAttempted = false,
                HasUncertainOutcome = true
            });

        result.CanPrepareFutureDispatch.Should().BeFalse();
        result.Disposition.Should().Be(P72ContactOnboardingDisposition.NoGo);
        result.FailureCategory.Should().Be(P72ContactOnboardingFailureCategory.Unavailable);
        result.ProhibitsReplay.Should().BeTrue();
    }

    /// <summary>
    /// 保護不完整或不存在的 read-back observation 不會被當成空 graph。
    ///
    /// <para>
    /// 此測試注入 <see langword="null"/> 與 <c>IsComplete=false</c>，覆蓋逾時、取消、分頁失敗或
    /// schema 驗證未完成的情境。決定性斷言是兩者都回傳 <c>NoGo</c>、分類為不可用且禁止 replay，
    /// 防止將未知遠端狀態錯誤地轉成可建立新 contact 的空集合。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resolve_fails_closed_for_incomplete_or_null_observation(bool useNull)
    {
        P72ContactOnboardingLocalObservation? observation = useNull
            ? null
            : new P72ContactOnboardingLocalObservation
            {
                IsComplete = false,
                ContactExists = false,
                OwnerAssigned = false,
                MembershipCreated = false,
                PresentRecordCreated = false,
                NotificationAttempted = false,
                HasUncertainOutcome = false
            };

        var result = P72ContactOnboardingLocalDecision.Resolve(
            P72ContactOnboardingMode.CreateFullGraph,
            observation);

        result.CanPrepareFutureDispatch.Should().BeFalse();
        result.Disposition.Should().Be(P72ContactOnboardingDisposition.NoGo);
        result.FailureCategory.Should().Be(P72ContactOnboardingFailureCategory.Unavailable);
        result.ProhibitsReplay.Should().BeTrue();
    }

    /// <summary>
    /// 保護未定義 onboarding enum 值不會降級成預設的建立模式。
    ///
    /// <para>
    /// 故障注入為未宣告的 enum 數值；決定性斷言是 <c>NoGo</c> 與不可用分類。雖然目前只允許由
    /// server-owned flow 選擇的完整 graph 建立模式，仍須驗證 enum 擴充、反序列化錯誤或意外轉型
    /// 不會讓未知值進入 dispatch、cleanup 或重播路徑。
    /// </para>
    /// </summary>
    [Fact]
    public void Resolve_fails_closed_for_an_undefined_onboarding_mode()
    {
        var result = P72ContactOnboardingLocalDecision.Resolve(
            (P72ContactOnboardingMode)999,
            FreshObservation());

        result.CanPrepareFutureDispatch.Should().BeFalse();
        result.Disposition.Should().Be(P72ContactOnboardingDisposition.NoGo);
        result.FailureCategory.Should().Be(P72ContactOnboardingFailureCategory.Unavailable);
        result.ProhibitsReplay.Should().BeTrue();
    }

    /// <summary>
    /// 保護多記錄 graph 的精確反向清理順序與通知排除規則。
    ///
    /// <para>
    /// 決定性斷言是清理列舉與序列只能依序包含 present record、membership、contact；這是由已知
    /// ledger ID 驅動的反向所有權釋放順序。通知不在 enum 或序列中，因為它不是可安全復原的資料列，
    /// 不得被 cleanup 或 replay 誤認成可刪除的 mutation。
    /// </para>
    /// </summary>
    [Fact]
    public void Reverse_cleanup_order_is_present_record_then_membership_then_contact_without_notification()
    {
        Enum.GetNames<P72ContactOnboardingCleanupStep>().Should().Equal(
        ["PresentRecord", "Membership", "Contact"]);
        P72ContactOnboardingLocalDecision.ReverseCleanupOrder.Should().Equal(
        [
            P72ContactOnboardingCleanupStep.PresentRecord,
            P72ContactOnboardingCleanupStep.Membership,
            P72ContactOnboardingCleanupStep.Contact
        ]);
    }

    /// <summary>
    /// 保護唯一允許的 fixture graph key 能建立 local-only plan，並保留精確讀回前置條件。
    ///
    /// <para>
    /// 決定性斷言是 plan 的 operation ID 固定為 Slice F catalog 項目、inputs 只含
    /// <c>fixtureGraphKey</c>，而 CE dispatch 與產品 consumer 都仍為 false。這證明 builder 不持有
    /// CRM service、profile、owner 或 payload；它的記憶體生命週期僅限回傳的不可變 plan。
    /// </para>
    /// </summary>
    [Fact]
    public void Build_creates_only_a_local_plan_for_a_valid_fresh_fixture_graph()
    {
        var result = P72ContactOnboardingLocalPlanBuilder.Build(
            FreshObservation(),
            "contact-onboarding-fixture-a");

        result.Succeeded.Should().BeTrue();
        result.Plan.Should().NotBeNull();
        result.Plan!.Definition.OperationId.Should().Be(OperationIds.NewPersonContactCreateFullOnboarding);
        result.Plan.Inputs.Should().BeEquivalentTo(new Dictionary<string, string?>
        {
            ["fixtureGraphKey"] = "contact-onboarding-fixture-a"
        });
        result.Plan.CeDispatchAllowed.Should().BeFalse();
        result.Plan.ProductConsumerAllowed.Should().BeFalse();
    }

    /// <summary>
    /// 保護 caller 無法藉由額外輸入注入 owner、profile 或 endpoint authority。
    ///
    /// <para>
    /// 每一筆資料都模擬外部輸入嘗試指定跨使用者或跨設定檔路由資訊。決定性斷言是 builder 回傳
    /// input-name mismatch 且沒有 partial plan；伺服器端授權與未來 executor 必須自行導出 owner、
    /// profile 和 endpoint，不能信任這些呼叫端值。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("owner")]
    [InlineData("profile")]
    [InlineData("endpoint")]
    public void Build_rejects_caller_supplied_authority_without_a_partial_plan(string inputName)
    {
        var result = P72ContactOnboardingLocalPlanBuilder.Build(
            FreshObservation(),
            "contact-onboarding-fixture-b",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [inputName] = "caller-controlled-value"
            });

        result.Succeeded.Should().BeFalse();
        result.Plan.Should().BeNull();
        result.FailureCategory.Should().Be(P72LocalPlanFailureCategory.InputNamesMismatch);
    }

    /// <summary>
    /// 保護空白 fixture graph key 不會產生可後續執行的 plan。
    ///
    /// <para>
    /// 空白 key 無法指向 task-owned fresh ledger graph；決定性斷言是 fail-closed input-value error 與
    /// null plan。這避免未來清理在缺少已知 ID 的情況下掃描、猜測或觸及其他請求建立的 contact。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_rejects_an_empty_fixture_graph_key_without_a_plan(string fixtureGraphKey)
    {
        var result = P72ContactOnboardingLocalPlanBuilder.Build(
            FreshObservation(),
            fixtureGraphKey);

        result.Succeeded.Should().BeFalse();
        result.Plan.Should().BeNull();
        result.FailureCategory.Should().Be(P72LocalPlanFailureCategory.InputValueInvalid);
    }

    /// <summary>
    /// 保護超過共用 local-only 上限的 fixture graph key 不會造成未界定的記憶體保留或 payload 傳遞。
    ///
    /// <para>
    /// 故障注入建立比共同 builder 上限多一個字元的 key；決定性斷言是 input-value error 與 null plan。
    /// 固定上限可讓每次純本機決策的配置量有界，且不會把任意長的呼叫端文字保留到 retry、queue 或
    /// background work。
    /// </para>
    /// </summary>
    [Fact]
    public void Build_rejects_an_oversized_fixture_graph_key_without_a_plan()
    {
        var fixtureGraphKey = new string(
            'x',
            P72ContinuationLocalOnlyPlanBuilder.MaximumInputValueCharacters + 1);

        var result = P72ContactOnboardingLocalPlanBuilder.Build(
            FreshObservation(),
            fixtureGraphKey);

        result.Succeeded.Should().BeFalse();
        result.Plan.Should().BeNull();
        result.FailureCategory.Should().Be(P72LocalPlanFailureCategory.InputValueInvalid);
    }

    /// <summary>
    /// 保護非全新、部分完成或不確定的 graph 不會得到 partial local-only plan。
    ///
    /// <para>
    /// 故障注入分別涵蓋既有 membership 與不確定結果。決定性斷言是兩種情況都沒有 plan；這要求未來
    /// reconciler 先以同一個 server-validated isolation boundary 讀回並確認 ledger，而不能把失敗
    /// 轉化成可執行的半成品 dispatch。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Build_does_not_create_a_plan_for_a_non_fresh_or_uncertain_graph(
        bool membershipCreated,
        bool hasUncertainOutcome)
    {
        var result = P72ContactOnboardingLocalPlanBuilder.Build(
            new P72ContactOnboardingLocalObservation
            {
                IsComplete = true,
                ContactExists = false,
                OwnerAssigned = false,
                MembershipCreated = membershipCreated,
                PresentRecordCreated = false,
                NotificationAttempted = false,
                HasUncertainOutcome = hasUncertainOutcome
            },
            "contact-onboarding-fixture-c");

        result.Succeeded.Should().BeFalse();
        result.Plan.Should().BeNull();
    }

    /// <summary>
    /// 保護 A/B 交錯執行時 reducer 不會共享可變決策狀態。
    ///
    /// <para>
    /// 測試以 Barrier 強制兩個工作同時進入 resolver：A 是全新 graph，B 則有既有 contact。決定性
    /// 斷言是結果集合同時保有 A 的 prepare 與 B 的 already-applied disposition；若 implementation
    /// 把最近 observation、plan 或 disposition 存入 static、cache 或 Session，這個契約將暴露跨請求
    /// 汙染。Barrier 與 ConcurrentBag 都在測試結束時由單一 using owner 釋放，沒有背景工作殘留。
    /// </para>
    /// </summary>
    [Fact]
    public async Task Resolve_keeps_interleaved_a_and_b_onboarding_decisions_operation_local()
    {
        using var barrier = new Barrier(2);
        var results = new ConcurrentBag<P72ContactOnboardingDecisionResult>();

        var a = Task.Run(() => ResolveAfterBarrier(FreshObservation(), barrier, results));
        var b = Task.Run(() => ResolveAfterBarrier(
            new P72ContactOnboardingLocalObservation
            {
                IsComplete = true,
                ContactExists = true,
                OwnerAssigned = false,
                MembershipCreated = false,
                PresentRecordCreated = false,
                NotificationAttempted = false,
                HasUncertainOutcome = false
            },
            barrier,
            results));

        await Task.WhenAll(a, b);

        results.Select(result => result.Disposition).Should().BeEquivalentTo(
        [
            P72ContactOnboardingDisposition.PrepareFutureGovernedDispatch,
            P72ContactOnboardingDisposition.AlreadyApplied
        ]);
    }

    /// <summary>
    /// 建立不含身份、設定檔、ID 或 payload 的完整全新 graph read-back 摘要。
    ///
    /// <para>
    /// 此 helper 只配置一次短生命週期的不可變初始化資料，供測試描述基線狀態；它不連線、快取或保留
    /// caller 狀態。任何 production 實作都必須在伺服器端授權完成後自行取得等價 read-back，而不能
    /// 將本 helper 或測試資料帶入背景工作。
    /// </para>
    /// </summary>
    private static P72ContactOnboardingLocalObservation FreshObservation()
        => new()
        {
            IsComplete = true,
            ContactExists = false,
            OwnerAssigned = false,
            MembershipCreated = false,
            PresentRecordCreated = false,
            NotificationAttempted = false,
            HasUncertainOutcome = false
        };

    /// <summary>
    /// 在同步屏障後執行單次純本機決策並將結果寫入測試擁有的併發集合。
    ///
    /// <para>
    /// Barrier 只存活至外層測試的 <c>using</c> 區塊結束，ConcurrentBag 亦不會逃逸至 production。
    /// 此 helper 的決定性職責是製造 A/B 交錯，而不模擬 I/O、重試、計時器或背景排程；因此失敗可直接
    /// 歸因於 reducer 的共享可變狀態或隔離契約破壞。
    /// </para>
    /// </summary>
    private static void ResolveAfterBarrier(
        P72ContactOnboardingLocalObservation observation,
        Barrier barrier,
        ConcurrentBag<P72ContactOnboardingDecisionResult> results)
    {
        barrier.SignalAndWait();
        results.Add(P72ContactOnboardingLocalDecision.Resolve(
            P72ContactOnboardingMode.CreateFullGraph,
            observation));
    }
}

// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/P72DonationPaymentLocalDecisionTests.cs
// 用途：保護 P7.2 continuation Slice D 的付款回呼本機決策：只有完整、已正規化成功、
//       尚未處理且仍待付款的觀察，才可準備一次未來受治理的操作；其餘結果都不得重播。
// ============================================================================

using System.Collections.Concurrent;
using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證付款回呼觀察的 Slice D 本機決策契約。
///
/// <para>
/// 本測試刻意不建立 CRM fixture、不保存訂單或身分資料，也不取得 connector、lease 或 client。
/// 它保護的風險是兩個來源同時回呼相同付款時，舊流程可能將同一筆款項重複寫入。決策輸入只保留
/// 完整性、正規化結果與去識別化的已處理／待付款旗標；A/B 交錯測試則確認任何輸入都不會被 static、
/// cache、Session 或另一個呼叫保留。未來 CE executor 仍須自行擁有 ledger、exact read-back、
/// reconciliation、timeout no-replay 與 deterministic cleanup，不能以本測試代替實機證據。
/// </para>
/// </summary>
public sealed class P72DonationPaymentLocalDecisionTests
{
    /// <summary>
    /// 保護 Slice D 專用 builder 只會為「完整、首次、仍待付款的成功」建立
    /// <c>payments.fee.update.after.payment</c> 的 local-only plan。決定性斷言是 plan 的 CE dispatch
    /// 與產品 consumer 旗標都維持 false；因此此測試不能被視為付款寫入、P7.4 切流或 P7.5 移除證據。
    /// </summary>
    [Fact]
    public void Build_prepares_only_the_fee_update_local_plan_for_a_fresh_success()
    {
        var result = P72DonationPaymentLocalPlanBuilder.BuildFeeUpdateAfterPayment(
            new P72DonationPaymentLocalObservation
            {
                IsComplete = true,
                Outcome = P72DonationPaymentOutcome.Succeeded,
                HasMatchingProcessedOrder = false,
                IsAwaitingPayment = true
            },
            "fixture-key-a");

        result.Succeeded.Should().BeTrue();
        result.Decision.Disposition.Should().Be(P72DonationPaymentDisposition.PrepareFutureGovernedDispatch);
        result.Plan.Should().NotBeNull();
        result.Plan!.Definition.OperationId.Should().Be(OperationIds.PaymentsFeeUpdateAfterPayment);
        result.Plan.Inputs.Should().BeEquivalentTo(new Dictionary<string, string?>
        {
            ["fixtureKey"] = "fixture-key-a",
            ["transition"] = "payment-succeeded"
        });
        result.Plan.CeDispatchAllowed.Should().BeFalse();
        result.Plan.ProductConsumerAllowed.Should().BeFalse();
    }

    /// <summary>
    /// 保護已處理、失敗、pending、unknown、incomplete 與 null 付款結果都不會產生 partial plan。
    /// 故障注入涵蓋 callback duplication 與 timeout；決定性斷言是計畫一律為 null，呼叫端無從把
    /// no-go／reconciliation 結果誤送到 executor。
    /// </summary>
    [Theory]
    [InlineData(P72DonationPaymentOutcome.Succeeded, true, true)]
    [InlineData(P72DonationPaymentOutcome.Succeeded, false, false)]
    [InlineData(P72DonationPaymentOutcome.Failed, false, true)]
    [InlineData(P72DonationPaymentOutcome.Pending, false, true)]
    [InlineData(P72DonationPaymentOutcome.Unknown, false, true)]
    public void Build_does_not_create_a_partial_plan_when_payment_is_not_a_fresh_success(
        P72DonationPaymentOutcome outcome,
        bool hasMatchingProcessedOrder,
        bool isAwaitingPayment)
    {
        var result = P72DonationPaymentLocalPlanBuilder.BuildFeeUpdateAfterPayment(
            new P72DonationPaymentLocalObservation
            {
                IsComplete = true,
                Outcome = outcome,
                HasMatchingProcessedOrder = hasMatchingProcessedOrder,
                IsAwaitingPayment = isAwaitingPayment
            },
            "fixture-key-b");

        result.Succeeded.Should().BeFalse();
        result.Plan.Should().BeNull();
        result.Decision.CanPrepareFutureDispatch.Should().BeFalse();
    }

    /// <summary>
    /// 保護 fixture key 不可為空白或超過共用 local-only plan 的長度上限。故障注入是 malformed
    /// ledger key；決定性斷言是 builder 將輸入錯誤與付款 no-go 分開，且不建立 partial plan。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_rejects_invalid_fixture_key_without_creating_a_plan(string fixtureKey)
    {
        var result = P72DonationPaymentLocalPlanBuilder.BuildFeeUpdateAfterPayment(
            new P72DonationPaymentLocalObservation
            {
                IsComplete = true,
                Outcome = P72DonationPaymentOutcome.Succeeded,
                HasMatchingProcessedOrder = false,
                IsAwaitingPayment = true
            },
            fixtureKey);

        result.Succeeded.Should().BeFalse();
        result.FailureCategory.Should().Be(P72LocalPlanFailureCategory.InputValueInvalid);
        result.Plan.Should().BeNull();
    }

    /// <summary>
    /// 保護完整成功且尚未處理的付款，才會被分類為可準備一次未來受治理 dispatch。
    /// 故障注入是將同一份觀察標記為「尚無相同訂單、費用仍待付款」；決定性斷言是結果仍只是一個
    /// local-only 準備決策，沒有 CRM ID、訂單值或任何 transport 資源。
    /// </summary>
    [Fact]
    public void Resolve_prepares_one_future_governed_dispatch_for_complete_fresh_success()
    {
        var result = P72DonationPaymentLocalDecision.Resolve(new P72DonationPaymentLocalObservation
        {
            IsComplete = true,
            Outcome = P72DonationPaymentOutcome.Succeeded,
            HasMatchingProcessedOrder = false,
            IsAwaitingPayment = true
        });

        result.CanPrepareFutureDispatch.Should().BeTrue();
        result.Disposition.Should().Be(P72DonationPaymentDisposition.PrepareFutureGovernedDispatch);
        result.FailureCategory.Should().Be(P72DonationPaymentFailureCategory.None);
        result.RequiresReconciliation.Should().BeTrue();
        result.ProhibitsReplay.Should().BeFalse();
    }

    /// <summary>
    /// 保護同一筆成功回呼已由其他來源處理時，不能再次準備寫入。
    /// 故障注入模擬 RETURN_URL 與 BACKEND_URL 交錯到達；決定性斷言是相同訂單旗標會得到
    /// AlreadyProcessed，且不會留下 partial plan 或重播許可。
    /// </summary>
    [Fact]
    public void Resolve_does_not_replay_a_success_with_an_existing_matching_order()
    {
        var result = P72DonationPaymentLocalDecision.Resolve(new P72DonationPaymentLocalObservation
        {
            IsComplete = true,
            Outcome = P72DonationPaymentOutcome.Succeeded,
            HasMatchingProcessedOrder = true,
            IsAwaitingPayment = true
        });

        result.CanPrepareFutureDispatch.Should().BeFalse();
        result.Disposition.Should().Be(P72DonationPaymentDisposition.AlreadyProcessed);
        result.FailureCategory.Should().Be(P72DonationPaymentFailureCategory.None);
        result.RequiresReconciliation.Should().BeTrue();
        result.ProhibitsReplay.Should().BeTrue();
    }

    /// <summary>
    /// 保護成功但費用已不再待付款的觀察，不得因付款結果看似成功而再次入帳。
    /// 故障注入是狀態已被前一個完整處理流程改變；決定性斷言是分類為 AlreadyProcessed，避免
    /// 以過時快照覆寫既有狀態。
    /// </summary>
    [Fact]
    public void Resolve_does_not_replay_a_success_when_fee_is_no_longer_awaiting_payment()
    {
        var result = P72DonationPaymentLocalDecision.Resolve(new P72DonationPaymentLocalObservation
        {
            IsComplete = true,
            Outcome = P72DonationPaymentOutcome.Succeeded,
            HasMatchingProcessedOrder = false,
            IsAwaitingPayment = false
        });

        result.CanPrepareFutureDispatch.Should().BeFalse();
        result.Disposition.Should().Be(P72DonationPaymentDisposition.AlreadyProcessed);
        result.ProhibitsReplay.Should().BeTrue();
    }

    /// <summary>
    /// 保護失敗結果只能交由未來受治理 reconciliation 判讀，不能由失敗回呼自動修改或重播 CRM。
    /// 決定性斷言是結果保持 no-dispatch 並標示 reconciliation；這避免把 provider 暫時失敗誤當成
    /// 可安全補寫的資料庫狀態。
    /// </summary>
    [Fact]
    public void Resolve_requires_reconciliation_without_dispatch_for_complete_failure()
    {
        var result = P72DonationPaymentLocalDecision.Resolve(new P72DonationPaymentLocalObservation
        {
            IsComplete = true,
            Outcome = P72DonationPaymentOutcome.Failed,
            HasMatchingProcessedOrder = false,
            IsAwaitingPayment = true
        });

        result.CanPrepareFutureDispatch.Should().BeFalse();
        result.Disposition.Should().Be(P72DonationPaymentDisposition.RequireReconciliation);
        result.FailureCategory.Should().Be(P72DonationPaymentFailureCategory.None);
        result.RequiresReconciliation.Should().BeTrue();
        result.ProhibitsReplay.Should().BeTrue();
    }

    /// <summary>
    /// 保護 pending、unknown、未完成與 null 觀察一律 fail closed。
    /// 故障注入涵蓋 timeout、原始 provider 回應不足與 parent 無法確認 child 終態；決定性斷言是
    /// 每一種狀態都沒有 dispatch 許可，並以無敏感資料的固定分類回報。
    /// </summary>
    [Theory]
    [InlineData(P72DonationPaymentOutcome.Pending)]
    [InlineData(P72DonationPaymentOutcome.Unknown)]
    public void Resolve_fails_closed_for_non_terminal_or_unknown_outcomes(P72DonationPaymentOutcome outcome)
    {
        var result = P72DonationPaymentLocalDecision.Resolve(new P72DonationPaymentLocalObservation
        {
            IsComplete = true,
            Outcome = outcome,
            HasMatchingProcessedOrder = false,
            IsAwaitingPayment = true
        });

        result.CanPrepareFutureDispatch.Should().BeFalse();
        result.Disposition.Should().Be(P72DonationPaymentDisposition.NoGo);
        result.FailureCategory.Should().Be(P72DonationPaymentFailureCategory.Unavailable);
        result.RequiresReconciliation.Should().BeTrue();
        result.ProhibitsReplay.Should().BeTrue();
    }

    /// <summary>
    /// 保護未來列舉版本漂移或未受信任轉換產生的未知數值，不會落入 AlreadyProcessed 等正常分支。
    /// 故障注入是不存在的 outcome 數值；決定性斷言是它仍被視為 Unavailable no-go，避免未知金流狀態
    /// 被錯誤解讀為可保留既有結果而掩蓋後續 reconciliation。
    /// </summary>
    [Fact]
    public void Resolve_fails_closed_for_an_undefined_outcome_value()
    {
        var result = P72DonationPaymentLocalDecision.Resolve(new P72DonationPaymentLocalObservation
        {
            IsComplete = true,
            Outcome = (P72DonationPaymentOutcome)999,
            HasMatchingProcessedOrder = false,
            IsAwaitingPayment = true
        });

        result.CanPrepareFutureDispatch.Should().BeFalse();
        result.Disposition.Should().Be(P72DonationPaymentDisposition.NoGo);
        result.FailureCategory.Should().Be(P72DonationPaymentFailureCategory.Unavailable);
        result.ProhibitsReplay.Should().BeTrue();
    }

    /// <summary>
    /// 保護 incomplete 或 null 的付款觀察不會被誤判為尚未處理的成功。
    /// 故障注入是 transport timeout 後只取得預設布林值；決定性斷言是輸出為 Unavailable no-go，
    /// 以阻斷 timeout 後的自動重播。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resolve_fails_closed_for_incomplete_or_null_observation(bool useNull)
    {
        P72DonationPaymentLocalObservation? observation = useNull
            ? null
            : new P72DonationPaymentLocalObservation
            {
                IsComplete = false,
                Outcome = P72DonationPaymentOutcome.Succeeded,
                HasMatchingProcessedOrder = false,
                IsAwaitingPayment = true
            };

        var result = P72DonationPaymentLocalDecision.Resolve(observation);

        result.CanPrepareFutureDispatch.Should().BeFalse();
        result.Disposition.Should().Be(P72DonationPaymentDisposition.NoGo);
        result.FailureCategory.Should().Be(P72DonationPaymentFailureCategory.Unavailable);
        result.ProhibitsReplay.Should().BeTrue();
    }

    /// <summary>
    /// 保護 A、B 交錯付款決策不會互相覆寫或重用 mutable state。
    /// 此測試透過 Barrier 強制同時進入決策點；決定性斷言是 A 的 fresh success 與 B 的既處理成功，
    /// 分別保有自己的 disposition，且沒有共享 Session、cache、lease、client 或背景工作可供保留。
    /// </summary>
    [Fact]
    public async Task Resolve_keeps_interleaved_a_and_b_payment_observations_operation_local()
    {
        using var barrier = new Barrier(2);
        var results = new ConcurrentBag<P72DonationPaymentDecisionResult>();

        var a = Task.Run(() => ResolveAfterBarrier(
            new P72DonationPaymentLocalObservation
            {
                IsComplete = true,
                Outcome = P72DonationPaymentOutcome.Succeeded,
                HasMatchingProcessedOrder = false,
                IsAwaitingPayment = true
            },
            barrier,
            results));
        var b = Task.Run(() => ResolveAfterBarrier(
            new P72DonationPaymentLocalObservation
            {
                IsComplete = true,
                Outcome = P72DonationPaymentOutcome.Succeeded,
                HasMatchingProcessedOrder = true,
                IsAwaitingPayment = true
            },
            barrier,
            results));

        await Task.WhenAll(a, b);

        results.Select(result => result.Disposition).Should().BeEquivalentTo(
        [
            P72DonationPaymentDisposition.PrepareFutureGovernedDispatch,
            P72DonationPaymentDisposition.AlreadyProcessed
        ]);
    }

    /// <summary>
    /// 保護定期定額付款回傳的 local-only plan 不能因為已取得一份有效的「付款後費用更新」草稿，
    /// 就暗中提升為其他付款寫入家族。故障注入是呼叫端試圖以同一 observation 取得 booking completion、
    /// contact card、fee create 或 owner assignment 的 dispatch 權限；決定性斷言是本層只公開既有單一
    /// operation ID，且 CE dispatch 與產品 consumer 都保持關閉。這避免 callback 在尚無獨立 ledger、
    /// read-back、reconcile 與 cleanup owner 時，把多步金融流程包裝成一個可重播的泛用寫入。
    /// </summary>
    [Fact]
    public void Build_keeps_a_recurring_payment_return_plan_local_only_and_single_family()
    {
        var result = P72DonationPaymentLocalPlanBuilder.BuildFeeUpdateAfterPayment(
            new P72DonationPaymentLocalObservation
            {
                IsComplete = true,
                Outcome = P72DonationPaymentOutcome.Succeeded,
                HasMatchingProcessedOrder = false,
                IsAwaitingPayment = true
            },
            "fixture-key-single-family");

        result.Succeeded.Should().BeTrue();
        result.Plan.Should().NotBeNull();
        result.Plan!.Definition.OperationId.Should().Be(OperationIds.PaymentsFeeUpdateAfterPayment);
        result.Plan.Definition.MutationPolicy.Should().Be(P72LocalMutationPolicy.SingleAllowlistedDispatch);
        result.Plan.Definition.ReadBackPolicy.Should().Be(P72LocalReadBackPolicy.ExactProjection);
        result.Plan.Definition.TimeoutPolicy.Should().Be(P72LocalTimeoutPolicy.NoReplayAfterUncertainDispatch);
        result.Plan.Definition.CleanupPolicy.Should().Be(P72LocalCleanupPolicy.ReverseKnownKeys);
        result.Plan.CeDispatchAllowed.Should().BeFalse();
        result.Plan.ProductConsumerAllowed.Should().BeFalse();
    }

    /// <summary>
    /// 保護 A/B 兩個請求即使使用相同有效付款分類，各自建立的 local-only plan 仍必須擁有防禦性複製後的
    /// input snapshot。故障注入是 A 先取得 plan，然後呼叫端修改原始輸入字典，再讓 B 使用不同 fixture
    /// key 建立另一份 plan；決定性斷言是兩份 plan 不共用 dictionary 參考，A 不會取得 B 或後續 caller
    /// 的 marker。測試不建立 connector、lease、CRM fixture 或背景工作，因此暫存集合在方法返回後由當前
    /// request/test owner 釋放，並證明純本機邊界不會保留跨使用者 state。
    /// </summary>
    [Fact]
    public void Build_defensively_copies_interleaved_payment_return_plan_inputs()
    {
        var aInputs = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["fixtureKey"] = "fixture-key-a",
            ["transition"] = "payment-succeeded"
        };

        var aGenericPlan = P72ContinuationLocalOnlyPlanBuilder.Build(new P72ContinuationLocalPlanRequest
        {
            OperationId = OperationIds.PaymentsFeeUpdateAfterPayment,
            Inputs = aInputs
        });
        aInputs["fixtureKey"] = "fixture-key-mutated-after-build";

        var b = P72DonationPaymentLocalPlanBuilder.BuildFeeUpdateAfterPayment(
            new P72DonationPaymentLocalObservation
            {
                IsComplete = true,
                Outcome = P72DonationPaymentOutcome.Succeeded,
                HasMatchingProcessedOrder = false,
                IsAwaitingPayment = true
            },
            "fixture-key-b");

        aGenericPlan.Succeeded.Should().BeTrue();
        aGenericPlan.Plan.Should().NotBeNull();
        aGenericPlan.Plan!.Inputs["fixtureKey"].Should().Be("fixture-key-a");
        b.Succeeded.Should().BeTrue();
        b.Plan.Should().NotBeNull();
        b.Plan!.Inputs["fixtureKey"].Should().Be("fixture-key-b");
        b.Plan.Inputs.Should().NotBeSameAs(aGenericPlan.Plan.Inputs);
        b.Plan.CeDispatchAllowed.Should().BeFalse();
        b.Plan.ProductConsumerAllowed.Should().BeFalse();
    }

    /// <summary>
    /// 模擬兩個獨立 request 同時進入決策點，不讓測試依賴排程巧合。
    /// Barrier 與 concurrent bag 都只由這個測試持有並在測試結束釋放；production 決策沒有任何共享
    /// collection、timer、subscription 或 background task。
    /// </summary>
    private static void ResolveAfterBarrier(
        P72DonationPaymentLocalObservation observation,
        Barrier barrier,
        ConcurrentBag<P72DonationPaymentDecisionResult> results)
    {
        barrier.SignalAndWait();
        results.Add(P72DonationPaymentLocalDecision.Resolve(observation));
    }
}

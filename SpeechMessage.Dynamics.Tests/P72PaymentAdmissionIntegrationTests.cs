// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/P72PaymentAdmissionIntegrationTests.cs
// 用途：驗證付款 local-only plan 與受治理付款週期准入器之間的純本機邊界；本檔不建立連線、
//       不保留跨測試狀態，也不觸發任何外部副作用。
// ============================================================================

using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 P7.2 付款 local-only plan 與付款週期准入器在公開純本機邊界的整合契約。
///
/// <para>
/// 此測試類別刻意只組合不可變 observation、local-only plan 與准入結果。付款 plan 只能保留受限的
/// opaque key 與固定 transition，不能自行變成 provision 或派送許可；每一次准入都必須由另一份完整的
/// 新鮮週期 observation 證明。所有資料都由個別測試擁有，測試結束即失去可達參考，避免 A/B 操作之間
/// 保留或共用可變狀態。
/// </para>
/// </summary>
public sealed class P72PaymentAdmissionIntegrationTests
{
    /// <summary>
    /// 保護有效付款 local-only plan 不會略過獨立的新鮮 bootstrap 與唯讀 preflight。
    /// 故障注入是把尚未完成 preflight 的 observation 偽裝成 provisioned；決定性斷言是此狀態 fail-closed，
    /// 而有效 plan 與 bootstrap 結果的兩個派送開關始終為 false。只有另一份完整 observation 的
    /// preflight=<c>Go</c> 才能取得 provision 許可，且該許可仍不是外部派送許可。
    /// </summary>
    [Fact]
    public void BuildAndAdmit_keeps_a_fresh_plan_local_only_until_a_separate_preflight_is_proven()
    {
        var planBuild = P72DonationPaymentLocalPlanBuilder.BuildFeeUpdateAfterPayment(
            CreateFreshPaymentObservation(),
            "payment-fixture-a");
        var bootstrap = P72GovernedPaymentCycleAdmission.Admit(CreateFreshAdmissionObservation());
        var prematureProvision = P72GovernedPaymentCycleAdmission.Admit(CreateFreshAdmissionObservation() with
        {
            Stage = P72GovernedPaymentCycleStage.Provisioned,
            HasKnownProvisionedKeys = true
        });
        var preflight = P72GovernedPaymentCycleAdmission.Admit(CreateFreshAdmissionObservation() with
        {
            Stage = P72GovernedPaymentCycleStage.PreflightCompleted,
            PreflightOutcome = P72GovernedPaymentPreflightOutcome.Go
        });

        planBuild.Succeeded.Should().BeTrue();
        planBuild.Plan.Should().NotBeNull();
        planBuild.Plan!.CeDispatchAllowed.Should().BeFalse();
        planBuild.Plan.ProductConsumerAllowed.Should().BeFalse();
        bootstrap.Disposition.Should().Be(P72GovernedPaymentCycleAdmissionDisposition.PreflightRequired);
        bootstrap.CanRunReadOnlyPreflight.Should().BeTrue();
        bootstrap.CanProvisionFreshFixture.Should().BeFalse();
        bootstrap.CanDispatchExactlyOnce.Should().BeFalse();
        bootstrap.CeDispatchAllowed.Should().BeFalse();
        bootstrap.ProductConsumerAllowed.Should().BeFalse();
        prematureProvision.Disposition.Should().Be(P72GovernedPaymentCycleAdmissionDisposition.NoGo);
        prematureProvision.ProhibitsReplay.Should().BeTrue();
        preflight.Disposition.Should().Be(P72GovernedPaymentCycleAdmissionDisposition.ProvisionAllowed);
        preflight.CanProvisionFreshFixture.Should().BeTrue();
        preflight.CanDispatchExactlyOnce.Should().BeFalse();
        preflight.CeDispatchAllowed.Should().BeFalse();
        preflight.ProductConsumerAllowed.Should().BeFalse();
    }

    /// <summary>
    /// 保護呼叫端提供的 opaque plan key 與已建立的 local-only plan 都不能取代 server-side 准入證據。
    /// 故障注入是保留有效付款觀察與非空 plan key，卻拿掉獨立 observation 的授權寫入證明；決定性斷言是
    /// 准入器回傳固定 no-go，沒有 provision 或一次派送能力。這證明 plan 輸入與輸出不會攜帶派送權限。
    /// </summary>
    [Fact]
    public void BuildAndAdmit_rejects_an_unproven_separate_admission_even_when_the_plan_is_valid()
    {
        var planBuild = P72DonationPaymentLocalPlanBuilder.BuildFeeUpdateAfterPayment(
            CreateFreshPaymentObservation(),
            "caller-supplied-opaque-plan-key");
        var rejectedAdmission = P72GovernedPaymentCycleAdmission.Admit(CreateFreshAdmissionObservation() with
        {
            HasServerAuthorizedWriter = false
        });

        planBuild.Succeeded.Should().BeTrue();
        planBuild.Plan.Should().NotBeNull();
        planBuild.Plan!.Inputs.Keys.Should().BeEquivalentTo(["fixtureKey", "transition"]);
        planBuild.Plan.Inputs["fixtureKey"].Should().Be("caller-supplied-opaque-plan-key");
        planBuild.Plan.CeDispatchAllowed.Should().BeFalse();
        planBuild.Plan.ProductConsumerAllowed.Should().BeFalse();
        rejectedAdmission.Disposition.Should().Be(P72GovernedPaymentCycleAdmissionDisposition.NoGo);
        rejectedAdmission.FailureCategory.Should().Be(P72GovernedPaymentCycleAdmissionFailureCategory.BootstrapInvalid);
        rejectedAdmission.CanProvisionFreshFixture.Should().BeFalse();
        rejectedAdmission.CanDispatchExactlyOnce.Should().BeFalse();
        rejectedAdmission.CeDispatchAllowed.Should().BeFalse();
        rejectedAdmission.ProductConsumerAllowed.Should().BeFalse();
        rejectedAdmission.ProhibitsReplay.Should().BeTrue();
    }

    /// <summary>
    /// 保護交錯執行的 A/B 付款 plan 與准入 observation 沒有共用可變參考，也不會讓 A 的有效 preflight
    /// 提升 B 的不完整 descriptor。故障注入為 B 缺少 task-owned descriptor；決定性斷言是兩份 plan 的
    /// input snapshot 與兩份准入結果都不同，而 A 只得到 provision 許可、B 則固定 fail-closed。
    /// </summary>
    [Fact]
    public async Task BuildAndAdmit_keeps_interleaved_a_and_b_inputs_operation_local()
    {
        var aPlanBuild = P72DonationPaymentLocalPlanBuilder.BuildFeeUpdateAfterPayment(
            CreateFreshPaymentObservation(),
            "payment-fixture-a");
        var bPlanBuild = P72DonationPaymentLocalPlanBuilder.BuildFeeUpdateAfterPayment(
            CreateFreshPaymentObservation(),
            "payment-fixture-b");
        var aObservation = CreateFreshAdmissionObservation() with
        {
            Stage = P72GovernedPaymentCycleStage.PreflightCompleted,
            PreflightOutcome = P72GovernedPaymentPreflightOutcome.Go
        };
        var bObservation = CreateFreshAdmissionObservation() with
        {
            HasCompleteTaskOwnedDescriptor = false
        };

        var results = await Task.WhenAll(
            Task.Run(() => P72GovernedPaymentCycleAdmission.Admit(aObservation)),
            Task.Run(() => P72GovernedPaymentCycleAdmission.Admit(bObservation)));

        aPlanBuild.Succeeded.Should().BeTrue();
        bPlanBuild.Succeeded.Should().BeTrue();
        aPlanBuild.Plan.Should().NotBeNull();
        bPlanBuild.Plan.Should().NotBeNull();
        aPlanBuild.Plan!.Inputs.Should().NotBeSameAs(bPlanBuild.Plan!.Inputs);
        aPlanBuild.Plan.Inputs["fixtureKey"].Should().Be("payment-fixture-a");
        bPlanBuild.Plan.Inputs["fixtureKey"].Should().Be("payment-fixture-b");
        aObservation.Should().NotBeSameAs(bObservation);
        results[0].Should().NotBeSameAs(results[1]);
        results[0].Disposition.Should().Be(P72GovernedPaymentCycleAdmissionDisposition.ProvisionAllowed);
        results[0].CanProvisionFreshFixture.Should().BeTrue();
        results[0].CanDispatchExactlyOnce.Should().BeFalse();
        results[0].CeDispatchAllowed.Should().BeFalse();
        results[0].ProductConsumerAllowed.Should().BeFalse();
        results[1].Disposition.Should().Be(P72GovernedPaymentCycleAdmissionDisposition.NoGo);
        results[1].CanProvisionFreshFixture.Should().BeFalse();
        results[1].CanDispatchExactlyOnce.Should().BeFalse();
        results[1].CeDispatchAllowed.Should().BeFalse();
        results[1].ProductConsumerAllowed.Should().BeFalse();
        results[1].ProhibitsReplay.Should().BeTrue();
    }

    /// <summary>
    /// 建立完整、首次且仍待付款的最小付款 observation。此資料只屬於目前測試方法，沒有外部資源、
    /// 長期快取或可由另一個操作重用的可變集合，因此可安全用於驗證 local-only plan 邊界。
    /// </summary>
    private static P72DonationPaymentLocalObservation CreateFreshPaymentObservation()
        => new()
        {
            IsComplete = true,
            Outcome = P72DonationPaymentOutcome.Succeeded,
            HasMatchingProcessedOrder = false,
            IsAwaitingPayment = true
        };

    /// <summary>
    /// 建立只可要求唯讀 preflight 的完整新鮮週期 observation。各欄位都是單次呼叫的不可變證據；
    /// 未來的 provision、派送、讀回、核對與清理必須以新的 observation 重新證明，不能由付款 plan
    /// 或先前 A/B 結果推導。此 helper 不擁有任何需釋放的資源。
    /// </summary>
    private static P72GovernedPaymentCycleAdmissionObservation CreateFreshAdmissionObservation()
        => new()
        {
            Stage = P72GovernedPaymentCycleStage.Bootstrap,
            IsFreshFamilyBinding = true,
            HasNonEmptyNonce = true,
            HasCompleteTaskOwnedDescriptor = true,
            IsLedgerBoundToFamily = true,
            WasLedgerEmptyBeforeProvision = true,
            HasServerAuthorizedWriter = true,
            HasExactlyOneAllowlistedMutation = true,
            HasExpectedExactProjection = true,
            HasKnownProvisionedKeys = false,
            PreflightOutcome = P72GovernedPaymentPreflightOutcome.NotRun,
            DispatchCount = 0,
            OperationExecuted = false,
            DispatchOutcome = P72GovernedPaymentDispatchOutcome.NotAttempted,
            ReadBackOutcome = P72GovernedPaymentReadBackOutcome.NotRun,
            ReconciliationOutcome = P72GovernedPaymentReconciliationOutcome.NotRun,
            CleanupOutcome = P72GovernedPaymentCleanupOutcome.NotRun
        };
}

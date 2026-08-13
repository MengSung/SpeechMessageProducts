// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/P72GovernedPaymentCycleAdmissionTests.cs
// 用途：保護 P7.2 定期奉獻付款回傳新寫入家族的純本機 cycle admission 契約。測試只使用
//       去識別化、immutable stage evidence；不建立 fixture、不連線 CE、不保留 Session、CRM ID、
//       Owner、profile、credential、connector、client 或任何跨測試 mutable state。
// ============================================================================

using System.Collections.Concurrent;
using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 P7.2 受控付款回傳寫入家族的 local-only cycle admission。
///
/// <para>
/// 本測試保護的契約是「一個新的、task-owned payment family 只有在 fresh binding、完整 descriptor、
/// 空白 initial ledger 與零 mutation preflight=go 都獲證明時，才能前往下一個 future governed stage」。
/// 所有 timeout、ambiguous、partial、read-back mismatch、cleanup uncertainty 或不完整證據都必須 fail
/// closed，且不可重播。測試刻意不使用 Data8、CRM SDK、檔案、網路、feature gate、Session、cache 或背景
/// 工作，以免測試本身引入跨使用者／跨 cycle state 或資源生命週期風險。
/// </para>
/// </summary>
public sealed class P72GovernedPaymentCycleAdmissionTests
{
    /// <summary>
    /// 保護全新 payment family 在尚未進行任何 I/O 前，只能要求零 mutation preflight 的起始契約。
    /// 故障注入使用完整性旗標為 false、空 nonce、缺 descriptor、非 fresh ledger、未證明 server
    /// authorization、非單一 allowlist mutation 與缺 exact projection。決定性斷言是所有情形都不會
    /// 產生 provision 或 dispatch 許可，並以去識別化 failure category fail closed。
    /// </summary>
    [Fact]
    public void Admit_allows_only_a_complete_fresh_bootstrap_to_start_read_only_preflight()
    {
        var result = P72GovernedPaymentCycleAdmission.Admit(CreateBootstrapObservation());

        result.Disposition.Should().Be(P72GovernedPaymentCycleAdmissionDisposition.PreflightRequired);
        result.FailureCategory.Should().Be(P72GovernedPaymentCycleAdmissionFailureCategory.None);
        result.CanRunReadOnlyPreflight.Should().BeTrue();
        result.CanProvisionFreshFixture.Should().BeFalse();
        result.CanDispatchExactlyOnce.Should().BeFalse();
        result.CeDispatchAllowed.Should().BeFalse();
        result.ProductConsumerAllowed.Should().BeFalse();
        result.ProhibitsReplay.Should().BeFalse();
    }

    /// <summary>
    /// 保護 bootstrap 的所有 identity、descriptor、ledger、authorization 與 projection 前置條件。
    /// 每個故障注入都只改變一個去識別化布林分類，證明 reducer 不需要 CRM identity 或 caller 選擇的
    /// Owner 才能拒絕不安全 cycle；決定性斷言是固定 no-go、沒有 CE permission 且禁止 replay。
    /// </summary>
    [Fact]
    public void Admit_fails_closed_when_the_fresh_family_binding_or_descriptor_is_incomplete()
    {
        var baseline = CreateBootstrapObservation();
        var invalidObservations = new[]
        {
            baseline with { IsFreshFamilyBinding = false },
            baseline with { HasNonEmptyNonce = false },
            baseline with { HasCompleteTaskOwnedDescriptor = false },
            baseline with { IsLedgerBoundToFamily = false },
            baseline with { WasLedgerEmptyBeforeProvision = false },
            baseline with { HasServerAuthorizedWriter = false },
            baseline with { HasExactlyOneAllowlistedMutation = false },
            baseline with { HasExpectedExactProjection = false }
        };

        foreach (var observation in invalidObservations)
        {
            var result = P72GovernedPaymentCycleAdmission.Admit(observation);

            result.Disposition.Should().Be(P72GovernedPaymentCycleAdmissionDisposition.NoGo);
            result.FailureCategory.Should().NotBe(P72GovernedPaymentCycleAdmissionFailureCategory.None);
            result.CanRunReadOnlyPreflight.Should().BeFalse();
            result.CanProvisionFreshFixture.Should().BeFalse();
            result.CanDispatchExactlyOnce.Should().BeFalse();
            result.CeDispatchAllowed.Should().BeFalse();
            result.ProductConsumerAllowed.Should().BeFalse();
            result.ProhibitsReplay.Should().BeTrue();
        }
    }

    /// <summary>
    /// 保護零 mutation preflight 成功後才可 provision fresh fixture 的階段轉移。測試不以任何 CRM
    /// response 推論成功，而只使用 future executor 已驗證的固定 `Go` 分類；決定性斷言是 reducer
    /// 允許 provision 卻仍不允許 CE dispatch 或 product consumer。
    /// </summary>
    [Fact]
    public void Admit_allows_provision_only_after_read_only_preflight_is_go()
    {
        var result = P72GovernedPaymentCycleAdmission.Admit(CreateBootstrapObservation() with
        {
            Stage = P72GovernedPaymentCycleStage.PreflightCompleted,
            PreflightOutcome = P72GovernedPaymentPreflightOutcome.Go
        });

        result.Disposition.Should().Be(P72GovernedPaymentCycleAdmissionDisposition.ProvisionAllowed);
        result.FailureCategory.Should().Be(P72GovernedPaymentCycleAdmissionFailureCategory.None);
        result.CanRunReadOnlyPreflight.Should().BeFalse();
        result.CanProvisionFreshFixture.Should().BeTrue();
        result.CanDispatchExactlyOnce.Should().BeFalse();
        result.CeDispatchAllowed.Should().BeFalse();
        result.ProductConsumerAllowed.Should().BeFalse();
        result.ProhibitsReplay.Should().BeFalse();
    }

    /// <summary>
    /// 保護 preflight 的固定非 go 分類。故障注入涵蓋 unavailable、duplicate fixture、authorization
    /// 未證明與 baseline 無法證明；每個情形都必須在 provision 前停止，不能藉由 retry、猜選 Owner
    /// 或建立另一筆共享資料繼續。決定性斷言是 no-go 與 no-replay。
    /// </summary>
    [Theory]
    [InlineData(P72GovernedPaymentPreflightOutcome.Unavailable)]
    [InlineData(P72GovernedPaymentPreflightOutcome.DuplicateFixture)]
    [InlineData(P72GovernedPaymentPreflightOutcome.AuthorizationUnproven)]
    [InlineData(P72GovernedPaymentPreflightOutcome.BaselineUnprovable)]
    public void Admit_stops_the_family_when_read_only_preflight_is_not_go(
        P72GovernedPaymentPreflightOutcome preflightOutcome)
    {
        var result = P72GovernedPaymentCycleAdmission.Admit(CreateBootstrapObservation() with
        {
            Stage = P72GovernedPaymentCycleStage.PreflightCompleted,
            PreflightOutcome = preflightOutcome
        });

        result.Disposition.Should().Be(P72GovernedPaymentCycleAdmissionDisposition.NoGo);
        result.FailureCategory.Should().NotBe(P72GovernedPaymentCycleAdmissionFailureCategory.None);
        result.CanProvisionFreshFixture.Should().BeFalse();
        result.CanDispatchExactlyOnce.Should().BeFalse();
        result.ProhibitsReplay.Should().BeTrue();
    }

    /// <summary>
    /// 保護 provision 已由本次 ledger 證明之後，future executor 只可獲得一次 allowlisted dispatch
    /// 許可。此結果仍是 local-only admission，不會建立 Data8 client、CRM request 或 feature gate；
    /// 決定性斷言是單次 dispatch flag 為 true，而公開 CE／consumer flags 仍固定為 false。
    /// </summary>
    [Fact]
    public void Admit_allows_exactly_one_future_dispatch_only_for_a_proven_fresh_provision()
    {
        var result = P72GovernedPaymentCycleAdmission.Admit(CreateBootstrapObservation() with
        {
            Stage = P72GovernedPaymentCycleStage.Provisioned,
            PreflightOutcome = P72GovernedPaymentPreflightOutcome.Go,
            HasKnownProvisionedKeys = true
        });

        result.Disposition.Should().Be(P72GovernedPaymentCycleAdmissionDisposition.ProvisionAllowed);
        result.FailureCategory.Should().Be(P72GovernedPaymentCycleAdmissionFailureCategory.None);
        result.CanProvisionFreshFixture.Should().BeFalse();
        result.CanDispatchExactlyOnce.Should().BeTrue();
        result.CeDispatchAllowed.Should().BeFalse();
        result.ProductConsumerAllowed.Should().BeFalse();
        result.ProhibitsReplay.Should().BeFalse();
    }

    /// <summary>
    /// 保護 writer 不可將缺 ledger key、二次 dispatch 或宣稱已執行卻仍停留在 provisioned stage
    /// 視為可安全重試。每個故障注入只使用 scalar state，決定性斷言是禁止下一個 dispatch 並以
    /// no-go 固定結束此 cycle family。
    /// </summary>
    [Fact]
    public void Admit_rejects_incomplete_or_replayed_provision_state_before_dispatch()
    {
        var baseline = CreateBootstrapObservation() with
        {
            Stage = P72GovernedPaymentCycleStage.Provisioned,
            PreflightOutcome = P72GovernedPaymentPreflightOutcome.Go,
            HasKnownProvisionedKeys = true
        };
        var invalidObservations = new[]
        {
            baseline with { HasKnownProvisionedKeys = false },
            baseline with { DispatchCount = 1, OperationExecuted = true, DispatchOutcome = P72GovernedPaymentDispatchOutcome.Applied },
            baseline with { DispatchCount = 2, OperationExecuted = true, DispatchOutcome = P72GovernedPaymentDispatchOutcome.Applied }
        };

        foreach (var observation in invalidObservations)
        {
            var result = P72GovernedPaymentCycleAdmission.Admit(observation);

            result.Disposition.Should().Be(P72GovernedPaymentCycleAdmissionDisposition.NoGo);
            result.CanDispatchExactlyOnce.Should().BeFalse();
            result.ProhibitsReplay.Should().BeTrue();
        }
    }

    /// <summary>
    /// 保護成功的唯一 dispatch 不會直接被當作完成。future executor 必須先執行 exact read-back，
    /// 因此結果只能要求 read-back 並禁止重播原 mutation；決定性斷言是沒有 cleanup 或 completed
    /// permission，且 local-only CE／consumer flags 始終為 false。
    /// </summary>
    [Fact]
    public void Admit_requires_exact_read_back_after_the_single_applied_dispatch()
    {
        var result = P72GovernedPaymentCycleAdmission.Admit(CreateDispatchedObservation());

        result.Disposition.Should().Be(P72GovernedPaymentCycleAdmissionDisposition.ReadBackRequired);
        result.FailureCategory.Should().Be(P72GovernedPaymentCycleAdmissionFailureCategory.None);
        result.RequiresExactReadBack.Should().BeTrue();
        result.RequiresReconciliation.Should().BeFalse();
        result.RequiresCleanup.Should().BeFalse();
        result.ProhibitsReplay.Should().BeTrue();
        result.CeDispatchAllowed.Should().BeFalse();
        result.ProductConsumerAllowed.Should().BeFalse();
    }

    /// <summary>
    /// 保護 timeout、ambiguous 或 partial dispatch 永遠不能轉成第二次寫入。故障注入假設一次
    /// allowlisted mutation 已可能送出，決定性斷言是無論 transport 回報如何，都回傳 no-go、
    /// 不建立 read-back success 假象並禁止 replay。
    /// </summary>
    [Theory]
    [InlineData(P72GovernedPaymentDispatchOutcome.Timeout)]
    [InlineData(P72GovernedPaymentDispatchOutcome.Ambiguous)]
    [InlineData(P72GovernedPaymentDispatchOutcome.Partial)]
    public void Admit_fails_closed_without_replay_after_an_uncertain_dispatch(
        P72GovernedPaymentDispatchOutcome dispatchOutcome)
    {
        var result = P72GovernedPaymentCycleAdmission.Admit(CreateDispatchedObservation() with
        {
            DispatchOutcome = dispatchOutcome
        });

        result.Disposition.Should().Be(P72GovernedPaymentCycleAdmissionDisposition.NoGo);
        result.FailureCategory.Should().Be(P72GovernedPaymentCycleAdmissionFailureCategory.UncertainDispatch);
        result.RequiresExactReadBack.Should().BeFalse();
        result.ProhibitsReplay.Should().BeTrue();
    }

    /// <summary>
    /// 保護 read-back、reconciliation 與 cleanup 的嚴格順序。成功 read-back 仍須先做 exact effect
    /// reconciliation，reconciled 狀態才可 cleanup；只有 cleanup complete 才能完成 cycle。決定性斷言
    /// 是每一步都不重新開放 dispatch，且無共享 mutable state 或外部 I/O。
    /// </summary>
    [Fact]
    public void Admit_requires_reconciliation_then_cleanup_before_the_cycle_is_completed()
    {
        var readBack = P72GovernedPaymentCycleAdmission.Admit(CreateDispatchedObservation() with
        {
            Stage = P72GovernedPaymentCycleStage.ReadBackVerified,
            ReadBackOutcome = P72GovernedPaymentReadBackOutcome.ExactMatch
        });
        var cleanup = P72GovernedPaymentCycleAdmission.Admit(CreateDispatchedObservation() with
        {
            Stage = P72GovernedPaymentCycleStage.Reconciled,
            ReadBackOutcome = P72GovernedPaymentReadBackOutcome.ExactMatch,
            ReconciliationOutcome = P72GovernedPaymentReconciliationOutcome.ExactEffectConfirmed
        });
        var complete = P72GovernedPaymentCycleAdmission.Admit(CreateDispatchedObservation() with
        {
            Stage = P72GovernedPaymentCycleStage.CleanupVerified,
            ReadBackOutcome = P72GovernedPaymentReadBackOutcome.ExactMatch,
            ReconciliationOutcome = P72GovernedPaymentReconciliationOutcome.ExactEffectConfirmed,
            CleanupOutcome = P72GovernedPaymentCleanupOutcome.Completed
        });

        readBack.Disposition.Should().Be(P72GovernedPaymentCycleAdmissionDisposition.ReadBackRequired);
        readBack.RequiresReconciliation.Should().BeTrue();
        readBack.ProhibitsReplay.Should().BeTrue();
        cleanup.Disposition.Should().Be(P72GovernedPaymentCycleAdmissionDisposition.CleanupRequired);
        cleanup.RequiresCleanup.Should().BeTrue();
        cleanup.ProhibitsReplay.Should().BeTrue();
        complete.Disposition.Should().Be(P72GovernedPaymentCycleAdmissionDisposition.Completed);
        complete.ProhibitsReplay.Should().BeTrue();
        complete.CeDispatchAllowed.Should().BeFalse();
        complete.ProductConsumerAllowed.Should().BeFalse();
    }

    /// <summary>
    /// 保護 read-back mismatch、unavailable、unknown effect、cleanup uncertainty 與 cleanup failure
    /// 不能被錯當成 completed。每個故障注入都在一次 dispatch 後發生，決定性斷言是固定 no-go、
    /// 無二次 dispatch 與禁止 replay，讓外層 executor 只能保留已知 ledger key 做安全處置。
    /// </summary>
    [Fact]
    public void Admit_fails_closed_for_read_back_reconciliation_or_cleanup_uncertainty()
    {
        var dispatched = CreateDispatchedObservation();
        var invalidObservations = new[]
        {
            dispatched with
            {
                Stage = P72GovernedPaymentCycleStage.ReadBackVerified,
                ReadBackOutcome = P72GovernedPaymentReadBackOutcome.Mismatch
            },
            dispatched with
            {
                Stage = P72GovernedPaymentCycleStage.ReadBackVerified,
                ReadBackOutcome = P72GovernedPaymentReadBackOutcome.Unavailable
            },
            dispatched with
            {
                Stage = P72GovernedPaymentCycleStage.Reconciled,
                ReadBackOutcome = P72GovernedPaymentReadBackOutcome.ExactMatch,
                ReconciliationOutcome = P72GovernedPaymentReconciliationOutcome.UnknownEffect
            },
            dispatched with
            {
                Stage = P72GovernedPaymentCycleStage.CleanupVerified,
                ReadBackOutcome = P72GovernedPaymentReadBackOutcome.ExactMatch,
                ReconciliationOutcome = P72GovernedPaymentReconciliationOutcome.ExactEffectConfirmed,
                CleanupOutcome = P72GovernedPaymentCleanupOutcome.Uncertain
            },
            dispatched with
            {
                Stage = P72GovernedPaymentCycleStage.CleanupVerified,
                ReadBackOutcome = P72GovernedPaymentReadBackOutcome.ExactMatch,
                ReconciliationOutcome = P72GovernedPaymentReconciliationOutcome.ExactEffectConfirmed,
                CleanupOutcome = P72GovernedPaymentCleanupOutcome.Failed
            }
        };

        foreach (var observation in invalidObservations)
        {
            var result = P72GovernedPaymentCycleAdmission.Admit(observation);

            result.Disposition.Should().Be(P72GovernedPaymentCycleAdmissionDisposition.NoGo);
            result.CanDispatchExactlyOnce.Should().BeFalse();
            result.ProhibitsReplay.Should().BeTrue();
        }
    }

    /// <summary>
    /// 保護 A/B 交錯 admission 只使用傳入的 immutable observation。故障注入讓 A 是合法 bootstrap、
    /// B 是缺 descriptor 的 no-go；使用 barrier 強制兩個 task 同時解析。決定性斷言是輸出恰好保留
    /// 兩個獨立 disposition，沒有任何 Session、cache、static mutable state 或跨 cycle contamination。
    /// </summary>
    [Fact]
    public async Task Admit_keeps_interleaved_a_and_b_cycle_observations_operation_local()
    {
        using var barrier = new Barrier(2);
        var results = new ConcurrentBag<P72GovernedPaymentCycleAdmissionResult>();
        var a = Task.Run(() => AdmitAfterBarrier(CreateBootstrapObservation(), barrier, results));
        var b = Task.Run(() => AdmitAfterBarrier(
            CreateBootstrapObservation() with { HasCompleteTaskOwnedDescriptor = false },
            barrier,
            results));

        await Task.WhenAll(a, b);

        results.Select(result => result.Disposition).Should().BeEquivalentTo(
        [
            P72GovernedPaymentCycleAdmissionDisposition.PreflightRequired,
            P72GovernedPaymentCycleAdmissionDisposition.NoGo
        ]);
    }

    /// <summary>
    /// 建立不含任何 CRM identity、Owner、profile、endpoint、credential 或 mutable collection 的完整
    /// bootstrap observation。每次呼叫配置新的 immutable record，避免測試 helper 自身跨 A/B operation
    /// 保留可變資料；production executor 仍須另行產生並保護真實 nonce/descriptor/ledger。
    /// </summary>
    private static P72GovernedPaymentCycleAdmissionObservation CreateBootstrapObservation()
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

    /// <summary>
    /// 建立已由 future executor provision 並恰好發出一次成功 mutation 的去識別化 observation。
    /// 它只服務 reducer state-machine 測試，沒有宣稱 CE 已實際執行或允許讀取任何 CRM record。
    /// </summary>
    private static P72GovernedPaymentCycleAdmissionObservation CreateDispatchedObservation()
        => CreateBootstrapObservation() with
        {
            Stage = P72GovernedPaymentCycleStage.Dispatched,
            PreflightOutcome = P72GovernedPaymentPreflightOutcome.Go,
            HasKnownProvisionedKeys = true,
            DispatchCount = 1,
            OperationExecuted = true,
            DispatchOutcome = P72GovernedPaymentDispatchOutcome.Applied
        };

    /// <summary>
    /// 在 barrier 後執行 single admission，讓 A/B 測試同時抵達 pure reducer。結果 bag 只在此 test
    /// 方法存活期間被擁有；所有 task 結束後它不會被 production Session、cache 或 background worker 保留。
    /// </summary>
    private static void AdmitAfterBarrier(
        P72GovernedPaymentCycleAdmissionObservation observation,
        Barrier barrier,
        ConcurrentBag<P72GovernedPaymentCycleAdmissionResult> results)
    {
        barrier.SignalAndWait();
        results.Add(P72GovernedPaymentCycleAdmission.Admit(observation));
    }
}

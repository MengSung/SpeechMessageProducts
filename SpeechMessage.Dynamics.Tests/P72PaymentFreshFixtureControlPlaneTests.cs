// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/P72PaymentFreshFixtureControlPlaneTests.cs
// 用途：保護 P7.2 付款回傳寫入家族在未來建立 fresh fixture 前，必須先取得完整、
//       去識別化且不可變的 descriptor/ledger 控制面證據；本測試不建立 CRM fixture、
//       不接觸 Data8、網路、檔案或 feature gate。
// ============================================================================

using System.Collections.Concurrent;
using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證付款專用 fresh-fixture 控制面的純本機契約。
///
/// <para>
/// 受保護的風險是 future executor 以缺少 nonce、descriptor digest、single-writer ledger、
/// server-derived owner binding、固定 projection 或 cleanup 順序的半成品資料，錯誤地取得
/// provision 或 dispatch 權限。測試只傳遞固定分類與布林證據，不傳遞 CRM ID、Owner、profile、
/// endpoint、credential、token、raw response 或 exception；因此不會把 fixture 的敏感控制資料
/// 保留在測試輸出、Session、cache 或共享 collection。
/// </para>
/// </summary>
public sealed class P72PaymentFreshFixtureControlPlaneTests
{
    /// <summary>
    /// 保護完整的 payment-specific descriptor/ledger bootstrap 只能開放零 mutation preflight。
    /// 故障模型是假設未來 executor 尚未建立任何 CRM fixture；決定性斷言是結果不會直接開放
    /// provision、CE dispatch 或產品 consumer，防止「control plane 存在」被誤解為遠端寫入授權。
    /// </summary>
    [Fact]
    public void Evaluate_allows_only_read_only_preflight_for_a_complete_fresh_payment_descriptor()
    {
        var result = P72PaymentFreshFixtureControlPlane.Evaluate(CreateCompleteBootstrap());

        result.Disposition.Should().Be(P72PaymentFreshFixtureControlPlaneDisposition.ReadOnlyPreflightRequired);
        result.FailureCategory.Should().Be(P72PaymentFreshFixtureControlPlaneFailureCategory.None);
        result.CanRunReadOnlyPreflight.Should().BeTrue();
        result.CanProvisionFreshFixture.Should().BeFalse();
        result.CanDispatchExactlyOnce.Should().BeFalse();
        result.CeDispatchAllowed.Should().BeFalse();
        result.ProductConsumerAllowed.Should().BeFalse();
        result.ProhibitsReplay.Should().BeFalse();
    }

    /// <summary>
    /// 保護每一個 payment fixture descriptor 與 ledger binding 都不可省略。故障注入一次只移除
    /// 一項必要證據，包含 fresh nonce、immutable digest、secure exact-key ledger、owner binding、
    /// fee update allowlist、fixed projection 或反向 cleanup；決定性斷言是每種缺口皆 fail closed，
    /// 且不藉由 CRM 掃描、猜選 Owner 或建立補救資料繼續。
    /// </summary>
    [Fact]
    public void Evaluate_fails_closed_when_any_payment_control_plane_evidence_is_missing()
    {
        var baseline = CreateCompleteBootstrap();
        var invalidInputs = new[]
        {
            baseline with { Family = (P72PaymentFreshFixtureFamily)999 },
            baseline with { SchemaVersion = "" },
            baseline with { HasFreshNonce = false },
            baseline with { HasImmutableDescriptorDigest = false },
            baseline with { HasEmptySingleWriterLedger = false },
            baseline with { HasSecureExactKeyLedger = false },
            baseline with { HasServerDerivedDistinctOwnerBinding = false },
            baseline with { HasFeeUpdateOnlyAllowlist = false },
            baseline with { HasFixedExactReadBackProjection = false },
            baseline with { HasReverseKnownKeyCleanupPlan = false }
        };

        foreach (var input in invalidInputs)
        {
            var result = P72PaymentFreshFixtureControlPlane.Evaluate(input);

            result.Disposition.Should().Be(P72PaymentFreshFixtureControlPlaneDisposition.NoGo);
            result.FailureCategory.Should().NotBe(P72PaymentFreshFixtureControlPlaneFailureCategory.None);
            result.CanRunReadOnlyPreflight.Should().BeFalse();
            result.CanProvisionFreshFixture.Should().BeFalse();
            result.CanDispatchExactlyOnce.Should().BeFalse();
            result.CeDispatchAllowed.Should().BeFalse();
            result.ProductConsumerAllowed.Should().BeFalse();
            result.ProhibitsReplay.Should().BeTrue();
        }
    }

    /// <summary>
    /// 保護此第一個 writer family 的 allowlist 只有已定義的付款成功後費用更新；它不是 generic
    /// CRUD、fee create、owner assign、booking completion、card profile 或通知的授權入口。測試以
    /// 固定 enum 對應 operation ID，決定性斷言是沒有 caller field map、CRM entity 或額外 mutation
    /// family 可由控制面輸入取得。
    /// </summary>
    [Fact]
    public void Evaluate_binds_the_control_plane_to_the_single_fee_update_operation_family()
    {
        var result = P72PaymentFreshFixtureControlPlane.Evaluate(CreateCompleteBootstrap());

        result.OperationId.Should().Be(OperationIds.PaymentsFeeUpdateAfterPayment);
        result.AllowedMutation.Should().Be(P72PaymentFreshFixtureMutation.FeeUpdateAfterPayment);
        result.CanDispatchExactlyOnce.Should().BeFalse();
        result.CeDispatchAllowed.Should().BeFalse();
    }

    /// <summary>
    /// 保護兩個不同 request/cycle 的 descriptor assessment 不共享可變狀態。故障注入讓 A 有完整
    /// bootstrap、B 缺少 secure ledger，並用 Barrier 強制交錯評估；決定性斷言是 A 只能進入
    /// read-only preflight，而 B 停在 no-go。Barrier 與結果容器只屬於本測試，並在 test 結束時
    /// dispose，production control plane 不保留 static、cache、Session、HttpContext 或背景工作。
    /// </summary>
    [Fact]
    public async Task Evaluate_keeps_interleaved_a_and_b_control_plane_assessments_operation_local()
    {
        using var barrier = new Barrier(2);
        var results = new ConcurrentBag<P72PaymentFreshFixtureControlPlaneAssessment>();

        var a = Task.Run(() => EvaluateAfterBarrier(CreateCompleteBootstrap(), barrier, results));
        var b = Task.Run(() => EvaluateAfterBarrier(
            CreateCompleteBootstrap() with { HasSecureExactKeyLedger = false },
            barrier,
            results));

        await Task.WhenAll(a, b);

        results.Select(result => result.Disposition).Should().BeEquivalentTo(
        [
            P72PaymentFreshFixtureControlPlaneDisposition.ReadOnlyPreflightRequired,
            P72PaymentFreshFixtureControlPlaneDisposition.NoGo
        ]);
        results.Should().OnlyContain(result => !result.CeDispatchAllowed && !result.ProductConsumerAllowed);
    }

    /// <summary>
    /// 建立完整的去識別化 payment control-plane bootstrap。實際 nonce、fixture marker、CRM IDs、
    /// owner/profile identity 與 ledger path 只能由未來 executor 的受保護 scope 擁有；本 helper
    /// 只代表它們已由 server-owned descriptor/ledger 認證，避免測試將敏感值硬編碼或輸出。
    /// </summary>
    private static P72PaymentFreshFixtureControlPlaneInput CreateCompleteBootstrap()
        => new()
        {
            Family = P72PaymentFreshFixtureFamily.FeeUpdateAfterPayment,
            SchemaVersion = P72PaymentFreshFixtureControlPlane.CurrentSchemaVersion,
            HasFreshNonce = true,
            HasImmutableDescriptorDigest = true,
            HasEmptySingleWriterLedger = true,
            HasSecureExactKeyLedger = true,
            HasServerDerivedDistinctOwnerBinding = true,
            HasFeeUpdateOnlyAllowlist = true,
            HasFixedExactReadBackProjection = true,
            HasReverseKnownKeyCleanupPlan = true
        };

    /// <summary>
    /// 在 barrier 後執行一個 pure assessment，避免 A/B 測試依賴排程巧合。結果 bag 只在目前測試
    /// 方法內存活，沒有將 request/cycle 資料傳入 production shared state 或資源 owner。
    /// </summary>
    private static void EvaluateAfterBarrier(
        P72PaymentFreshFixtureControlPlaneInput input,
        Barrier barrier,
        ConcurrentBag<P72PaymentFreshFixtureControlPlaneAssessment> results)
    {
        barrier.SignalAndWait();
        results.Add(P72PaymentFreshFixtureControlPlane.Evaluate(input));
    }
}

// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/P72ContinuationLocalOnlyPlanBuilderTests.cs
// 用途：驗證 P7.2 continuation Slice D–H 在尚未取得 CE 實證前，僅能建立不可執行的
//       本機操作計畫；保護輸入 allowlist、防止 A/B 請求交叉保留，以及禁止任何產品或
//       CE dispatch 啟用。
// ============================================================================

using System.Collections.Concurrent;
using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 Slice D–H local-only 操作計畫建立器的輸入隔離與 fail-closed 契約。
///
/// <para>
/// 這些測試刻意不建立 connector、lease、CRM client 或 fixture。它們保護的邊界是：
/// 在 P7.4 consumer cutover 以前，呼叫端最多只能交給本機層一組有界、具名的 opaque
/// fixture 值；建立器必須複製它們、拒絕額外 authority 欄位，並產生永久標示為不可
/// CE dispatch、不可產品啟用的 immutable 計畫。故障注入包含多餘 Owner 欄位、缺少
/// allowlist 欄位與兩個同時抵達建立器的 A/B marker，以證明沒有跨使用者／跨操作
/// 的 mutable state、快取或背景資源殘留。
/// </para>
/// </summary>
public sealed class P72ContinuationLocalOnlyPlanBuilderTests
{
    /// <summary>
    /// 保護每個已盤點 Slice D–H operation 只能以其 catalog 明定的完整輸入集合建立
    /// 本機計畫。決定性斷言是成功計畫會防禦性複製輸入，但 CE dispatch 與產品 consumer
    /// 旗標都保持 false；因此本機測試結果不能被誤用為真實 CE evidence 或 P7.4 切流依據。
    /// </summary>
    [Fact]
    public void Build_copies_every_complete_allowlisted_input_set_and_keeps_the_plan_local_only()
    {
        foreach (var definition in P72ContinuationLocalOnlyCatalog.All)
        {
            var source = CreateInputs(definition, "first");

            var result = P72ContinuationLocalOnlyPlanBuilder.Build(new P72ContinuationLocalPlanRequest
            {
                OperationId = definition.OperationId,
                Inputs = source
            });

            result.Succeeded.Should().BeTrue(definition.OperationId);
            result.FailureCategory.Should().Be(P72LocalPlanFailureCategory.None);
            result.Plan.Should().NotBeNull();
            result.Plan!.Definition.OperationId.Should().Be(definition.OperationId);
            result.Plan.Inputs.Should().BeEquivalentTo(source);
            result.Plan.CeDispatchAllowed.Should().BeFalse();
            result.Plan.ProductConsumerAllowed.Should().BeFalse();

            var firstKey = source.Keys.First();
            var expectedValue = source[firstKey];
            source[firstKey] = "changed-after-build";

            result.Plan.Inputs[firstKey].Should().Be(expectedValue,
                "operation plan must not retain a caller-owned mutable dictionary");
        }
    }

    /// <summary>
    /// 保護 caller 無法把 Owner、Profile、Connector 或其他未列入 catalog 的 authority
    /// 偷渡到本機計畫。故障注入是一筆 otherwise-valid donation fixture 輸入額外帶入
    /// <c>owner</c>；決定性斷言是建立器只回傳固定失敗分類、不回傳部分計畫，也不會把
    /// 欄位寫入任何共享狀態。
    /// </summary>
    [Fact]
    public void Build_rejects_extra_caller_authority_without_creating_a_partial_plan()
    {
        var definition = P72ContinuationLocalOnlyCatalog.All.Single(candidate =>
            candidate.OperationId == OperationIds.PaymentsFeeUpdateAfterPayment);
        var source = CreateInputs(definition, "authority-rejection");
        source.Add("owner", "caller-supplied-owner");

        var result = P72ContinuationLocalOnlyPlanBuilder.Build(new P72ContinuationLocalPlanRequest
        {
            OperationId = definition.OperationId,
            Inputs = source
        });

        result.Succeeded.Should().BeFalse();
        result.FailureCategory.Should().Be(P72LocalPlanFailureCategory.InputNamesMismatch);
        result.Plan.Should().BeNull();
    }

    /// <summary>
    /// 保護缺少 allowlist 欄位時不可猜預設值、不可重用前一個請求的值，也不可建立可被
    /// 後段錯誤 dispatch 的部分計畫。故障注入移除 attendance 的 week-start value；決定性
    /// 斷言是固定 no-go 分類與空計畫，避免週報關聯規則在未完整驗證前被繞過。
    /// </summary>
    [Fact]
    public void Build_rejects_missing_allowlisted_input_without_reusing_prior_request_state()
    {
        var definition = P72ContinuationLocalOnlyCatalog.All.Single(candidate =>
            candidate.OperationId == OperationIds.PresentRecordUpsertOnUpload);
        var source = CreateInputs(definition, "missing-input");
        source.Remove("weekStartDate");

        var result = P72ContinuationLocalOnlyPlanBuilder.Build(new P72ContinuationLocalPlanRequest
        {
            OperationId = definition.OperationId,
            Inputs = source
        });

        result.Succeeded.Should().BeFalse();
        result.FailureCategory.Should().Be(P72LocalPlanFailureCategory.InputNamesMismatch);
        result.Plan.Should().BeNull();
    }

    /// <summary>
    /// 保護兩個同時進入本機建立器的操作不會互相取得 fixture marker。此測試使用
    /// <see cref="Barrier"/> 強制 A/B 同時抵達建立邊界，而非只用可能序列化的
    /// <see cref="Task.WhenAll(IEnumerable{Task})"/>。決定性斷言是每個 immutable plan
    /// 僅保留自身 marker；建立器不擁有 timer、task、lease、client 或可跨請求存活的資源，
    /// 所以工作完成後沒有額外 cleanup owner 可遺留。
    /// </summary>
    [Fact]
    public async Task Build_keeps_concurrent_a_and_b_fixture_markers_operation_local()
    {
        var definition = P72ContinuationLocalOnlyCatalog.All.Single(candidate =>
            candidate.OperationId == OperationIds.FeesEditorStageInmemoryChange);
        var barrier = new Barrier(2);
        var results = new ConcurrentBag<P72ContinuationLocalPlanBuildResult>();

        var a = Task.Run(() => BuildAfterBarrier(definition, "marker-a", barrier, results));
        var b = Task.Run(() => BuildAfterBarrier(definition, "marker-b", barrier, results));

        await Task.WhenAll(a, b);

        results.Should().HaveCount(2).And.OnlyContain(result => result.Succeeded);
        results.Select(result => result.Plan!.Inputs["draftKey"])
            .Should()
            .BeEquivalentTo(new[] { "draftKey-marker-a", "draftKey-marker-b" });
        results.SelectMany(result => result.Plan!.Inputs.Values)
            .Should()
            .OnlyContain(value => value.Contains("marker-a", StringComparison.Ordinal) ||
                                  value.Contains("marker-b", StringComparison.Ordinal));
    }

    /// <summary>
    /// 建立一組與 catalog 完全對應的有界 opaque 值。它刻意不包含 CRM ID、Owner、endpoint、
    /// credential、token、Profile 或原始 payload，讓測試只驗證本機 contract，而不偽裝成
    /// CRM fixture 或真實 CE input。
    /// </summary>
    private static Dictionary<string, string?> CreateInputs(
        P72ContinuationLocalCapabilityDefinition definition,
        string marker)
        => definition.AllowedInputNames.ToDictionary(
            inputName => inputName,
            inputName => (string?)$"{inputName}-{marker}",
            StringComparer.Ordinal);

    /// <summary>
    /// 在兩個工作都已準備後才執行真正的 build，以排除排程剛好串行造成的假陽性。結果只
    /// 暫存於本測試擁有的 concurrent bag；其生命週期受測試方法限制，不會進入 production
    /// static/cache/session 或背景 queue。
    /// </summary>
    private static void BuildAfterBarrier(
        P72ContinuationLocalCapabilityDefinition definition,
        string marker,
        Barrier barrier,
        ConcurrentBag<P72ContinuationLocalPlanBuildResult> results)
    {
        barrier.SignalAndWait();
        results.Add(P72ContinuationLocalOnlyPlanBuilder.Build(new P72ContinuationLocalPlanRequest
        {
            OperationId = definition.OperationId,
            Inputs = CreateInputs(definition, marker)
        }));
    }
}

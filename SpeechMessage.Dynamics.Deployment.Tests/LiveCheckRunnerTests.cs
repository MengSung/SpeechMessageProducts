/*
檔案整體說明
目的與定位：P7 Live編排器的離線契約，以合成round注入SDK等價的seed／trigger／observe故障，不連任何組織。
主要流程：真runner驗分級與狀態、D34、用量、共用ShadowComparer結果，finally以None清理並恢復trace。
資源與隔離：每案新runtime與round，沒有真秘密、網路或持久檔案；round計數只驗owner清理，功能斷言驗真比較結果。
故障與斷言：差異不得略過cleanup，caller取消後也須刪seed／待辦並Dispose，步驟狀態不可被runner改變。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.Json;
using SpeechMessage.Dynamics.Deployment;
using SpeechMessage.Dynamics.Deployment.Live;
using SpeechMessage.Dynamics.Deployment.Shadow;
using Xunit;
namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>真runner單次生命週期，runtime替身只提供資料／故障，不模擬產品比較邏輯。</summary>
public sealed class LiveCheckRunnerTests
{
    private static readonly string[] ObservedFields = { "new_count" };
    /// <summary>production在任何D34／用量／seed之前拒絕；不把白名單視為測試寫入授權。</summary>
    [Fact]
    public void LiveCheck_ProductionOrganization_Rejected()
    {
        var runtime = new Runtime();
        Assert.Throws<InvalidDataException>(() => new LiveCheckRunner(runtime).Run(Organization("production"), "care", new[] { Scenario() }));
        Assert.Equal(0, runtime.D34);
        Assert.False(runtime.Round.Seeded);
    }
    /// <summary>有效live步驟停用或仍Shadow都拒絕，不能由測試器幫忙改狀態來通過。</summary>
    [Theory]
    [InlineData(false, "Live")]
    [InlineData(true, "Shadow")]
    public void LiveCheck_StateNotLiveForModule_Rejected(bool enabled, string mode)
    {
        var runtime = new Runtime();
        Assert.Throws<InvalidDataException>(() => new LiveCheckRunner(runtime).Run(Organization(), "care", new[] { Scenario() with { Enabled = enabled, Mode = mode } }));
        Assert.False(runtime.Round.Seeded);
    }
    /// <summary>dormant列為未檢查，既不算pass也不算difference，沒有任何seed副作用。</summary>
    [Fact]
    public void LiveCheck_DormantItems_SkippedAndReportedAsNotChecked()
    {
        var runtime = new Runtime();
        var result = new LiveCheckRunner(runtime).Run(Organization(), "care", new[] { Scenario() with { Tier = "dormant" } });
        Assert.Equal("not-checked", Assert.Single(result.Cases).Status);
        Assert.Equal(0, result.CheckedCount);
        Assert.False(runtime.Round.Seeded);
    }
    /// <summary>實際欄值不同由共用comparer報差異；清理與trace恢復仍發生。</summary>
    [Fact]
    public void LiveCheck_ObservedWritesDifferFromExpected_ReportsDifference()
    {
        var runtime = new Runtime(); runtime.Round.Actual = Snapshot(2);
        var result = new LiveCheckRunner(runtime).Run(Organization(), "care", new[] { Scenario() });
        Assert.False(result.Passed);
        Assert.NotEmpty(Assert.Single(result.Cases).Comparison.Differences);
        Assert.True(runtime.Round.Cleaned);
        Assert.Equal(1, runtime.Trace);
    }
    /// <summary>用量差異即失敗，不能用資料欄位相同冒稱推播正確。</summary>
    [Fact]
    public void LiveCheck_PushCountMismatch_ReportsDifference()
    {
        var runtime = new Runtime(); runtime.UsageAfter = 12;
        var result = new LiveCheckRunner(runtime).Run(Organization(), "care", new[] { Scenario() with { ExpectedPushes = 1 } });
        Assert.False(result.Passed);
        Assert.Equal(2, Assert.Single(result.Cases).ActualPushes);
        Assert.True(runtime.Round.Cleaned);
    }
    /// <summary>trigger中途失敗或取消仍清seed、核殘留、Dispose並回原trace，原失敗不被取代。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiveCheck_FailureMidRun_DeletesSeedAndLeavesStateUnchanged(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var runtime = new Runtime();
        runtime.Round.TriggerFailure = () => { if (cancel) { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); } throw new InvalidOperationException("synthetic trigger"); };
        if (cancel) Assert.Throws<OperationCanceledException>(() => new LiveCheckRunner(runtime).Run(Organization(), "care", new[] { Scenario() }, cancellation.Token));
        else Assert.Throws<InvalidOperationException>(() => new LiveCheckRunner(runtime).Run(Organization(), "care", new[] { Scenario() }));
        Assert.True(runtime.Round.Cleaned); Assert.True(runtime.Round.Verified); Assert.True(runtime.Round.Disposed);
        Assert.False(runtime.Round.CleanupTokenCancelled);
        Assert.Equal(1, runtime.Trace);
    }
    /// <summary>
    /// 注入種子建立後第二次D34失敗，保護即時安全核對也輸出LIVE-PREP-03；
    /// 決定性斷言確認秘密本文不外洩、失敗仍清理本輪資料並還原trace。
    /// </summary>
    [Fact]
    public void LiveCheck_SecondD34Failure_ReportsSafetyCodeAndCleansRound()
    {
        const string secretSentinel = "SENSITIVE-D34-FAILURE";
        var runtime = new Runtime
        {
            D34FailureAtCall = 2,
            D34Failure = new InvalidOperationException(secretSentinel)
        };

        var failure = Assert.Throws<LivePreparationFailure>(() =>
            new LiveCheckRunner(runtime).Run(Organization(), "care", new[] { Scenario() }));
        using var writer = new StringWriter();
        DeploymentCli.WriteSupplementalFailureDiagnostics(failure, writer);

        Assert.Contains("LIVE-PREP-03", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("module=care", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("scenario=normal", writer.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(secretSentinel, writer.ToString(), StringComparison.Ordinal);
        Assert.True(runtime.Round.Seeded);
        Assert.True(runtime.Round.Cleaned);
        Assert.True(runtime.Round.Verified);
        Assert.True(runtime.Round.Disposed);
        Assert.Equal(1, runtime.Trace);
    }
    /// <summary>待辦真實投影先參與共用比較再清理；清後殘留0才可計通過。</summary>
    [Fact]
    public void LiveCheck_TaskCreatedWithExpectedPayload_CountsAsEvidenceThenDeleted()
    {
        var runtime = new Runtime(); runtime.Round.TaskCount = 1;
        var result = new LiveCheckRunner(runtime).Run(Organization(), "care", new[] { Scenario() });
        Assert.True(result.Passed);
        Assert.Equal(1, Assert.Single(result.Cases).TaskEvidenceCount);
        Assert.True(runtime.Round.Cleaned); Assert.True(runtime.Round.Verified);
        Assert.Equal(2, runtime.D34);
    }
    /// <summary>純合成組織只作runner種類判斷，不建立catalog或任何真連線。</summary>
    private static OrganizationDefinition Organization(string kind = "test") => new("synthetic", kind, "synthetic.invalid", "off");
    /// <summary>唯一穩定情境代號與明列observe，沒有依顯示文字配對。</summary>
    private static LiveCheckScenario Scenario() => new("normal", "New.Activity", "care", "live", true, "Live", 0,
        Snapshot(1), Array.AsReadOnly(new[] { new ShadowObservation("task", "record", Array.AsReadOnly(ObservedFields)) }), new HashSet<string>(StringComparer.Ordinal));
    /// <summary>以JSON數字精確比較，不經double或文字轉換。</summary>
    private static ShadowSnapshot Snapshot(int value) => new(true, Array.AsReadOnly(new[] { new ShadowRow("task", "record", JsonSerializer.SerializeToElement(new { new_count = value })) }));
    /// <summary>本案獨占runtime，trace及用量以純值提供，沒有SDK或外部資源。</summary>
    private sealed class Runtime : ILiveCheckRuntime
    {
        internal Round Round { get; } = new();
        internal int Trace { get; private set; } = 1;
        internal int D34 { get; private set; }
        internal int D34FailureAtCall { get; init; }
        internal Exception? D34Failure { get; init; }
        internal long UsageAfter { get; set; } = 10;
        private int usages;
        /// <summary>以計數證明每輪seed前與trigger前都核D34。</summary>
        public void VerifyD34(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            D34++;
            if (D34 == D34FailureAtCall && D34Failure is not null) throw D34Failure;
        }
        /// <summary>保存原值1，不假設trace已開。</summary>
        public int ReadTraceSetting() => Trace;
        /// <summary>收尾token不可已取消。</summary>
        public void WriteTraceSetting(int value, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); Trace = value; }
        /// <summary>提供前後用量，runner實際計delta。</summary>
        public long ReadUsage(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return usages++ == 0 ? 10 : UsageAfter; }
        /// <summary>round由runner在finally釋放，不借其他案例。</summary>
        public ILiveCheckRound CreateRound(LiveCheckScenario scenario) => Round;
    }
    /// <summary>單次合成round，清理動作可斷言None token，不會自行比對結果。</summary>
    private sealed class Round : ILiveCheckRound
    {
        internal bool Seeded { get; private set; }
        internal bool Cleaned { get; private set; }
        internal bool Verified { get; private set; }
        internal bool Disposed { get; private set; }
        internal bool CleanupTokenCancelled { get; private set; }
        internal Action? TriggerFailure { get; set; }
        internal ShadowSnapshot Actual { get; set; } = Snapshot(1);
        internal int TaskCount { get; set; }
        /// <summary>seed是否真的被runner觸發。</summary>
        public void Seed(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); Seeded = true; }
        /// <summary>注入取消或失敗，不隱藏原始例外。</summary>
        public void Trigger(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); TriggerFailure?.Invoke(); }
        /// <summary>傳回原始實際投影，由runner的真comparer判結果。</summary>
        public LiveCheckObservation Observe(TimeSpan maximumWait, CancellationToken cancellationToken) => new(Actual, TaskCount, null);
        /// <summary>取消後仍需被呼叫且token為None。</summary>
        public void Cleanup(CancellationToken cancellationToken) { CleanupTokenCancelled = cancellationToken.IsCancellationRequested; Cleaned = true; }
        /// <summary>殘留確認須在清理後、Dispose前。</summary>
        public void VerifyNoRemaining(CancellationToken cancellationToken) { Assert.True(Cleaned); Assert.False(Disposed); Verified = true; }
        /// <summary>本地owner最後釋放。</summary>
        public void Dispose() => Disposed = true;
    }
}

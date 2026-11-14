/*
 * 檔案整體說明
 *
 * 目的與定位：保護兩輪編排的互斥、期望比較與故障收尾契約；使用真 DeploymentEngine
 * 及 Fixture 記憶體 SDK，runtime 僅替代 seed／追蹤 I/O，不連 D365、不向 LINE 發送。
 * 主要功能與流程：建立停用新舊步驟與 D5-18 合成綁定，注入 seed、trigger、追蹤、
 * 刪除及設定還原失敗，斷言步驟真實狀態、資源排空及原例外保存。若省略 finally、
 * 兩輪同時啟用或把意圖直接當最終值，本檔的決定性斷言必須失敗。
 * 使用邊界與維護：每案 Fixture 獨占服務及暫存目錄，using 結束後排空。合成 token
 * 只留在 SDK 的自建記錄中，報告不得包含其值；這些斷言不是實機 D34／LINE 證據。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Deployment.Shadow;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>透過正式引擎切換的編排測試，不手動替產品執行停用或清理。</summary>
public sealed class ShadowScenarioRunnerTests
{
    private static readonly string[] ObservedFields = { "score", "subject" };
    /// <summary>沒有舊程式的新模組只跑新輪，報告不能把不存在的舊輪標成已比對。</summary>
    [Fact]
    public void Shadow_Runner_NewBasisDoesNotInventLegacyEvidence()
    {
        using var f = new Fixture();
        var runtime = new Runtime(f);
        var item = Assert.Single(runtime.Run(scenario: Scenario() with { Basis = "new" }));
        Assert.False(item.LegacyExecuted);
        Assert.False(item.BetweenRounds.Comparable);
        Assert.False(item.LegacyToExpected.Comparable);
        Assert.True(item.ShadowToExpected.IsEqual);
        Assert.False(Assert.Single(runtime.Rounds).Legacy);
        runtime.AssertDrained();
    }
    /// <summary>先舊後新使用不同 ID，CRUD 意圖先套初始值，再作三方比較，結束所有狀態排空。</summary>
    [Fact]
    public void Shadow_Runner_TwoRoundsUseSeparateRecordsAndCompareFinalValues()
    {
        using var f = new Fixture();
        var runtime = new Runtime(f);
        var result = runtime.Run();
        var item = Assert.Single(result);
        Assert.True(item.BetweenRounds.IsEqual);
        Assert.True(item.LegacyToExpected.IsEqual);
        Assert.True(item.ShadowToExpected.IsEqual);
        runtime.AssertDrained();
        Assert.Equal(2, runtime.Rounds.Count);
        Assert.True(runtime.Rounds[0].Legacy);
        Assert.False(runtime.Rounds[1].Legacy);
        Assert.NotEqual(runtime.Rounds[0].Id, runtime.Rounds[1].Id);
    }

    /// <summary>在兩輪各階段注入例外；已建立資料仍刪除、原追蹤恢復，且新舊步驟停用。</summary>
    /// <param name="fault">故障注入點。</param>
    [Theory]
    [InlineData("legacy-seed")]
    [InlineData("legacy-trigger")]
    [InlineData("legacy-observe")]
    [InlineData("shadow-seed")]
    [InlineData("shadow-trigger")]
    [InlineData("shadow-trace")]
    [InlineData("trace-write")]
    public void Shadow_FailureMidRun_RestoresStepsTraceAndData(string fault)
    {
        using var f = new Fixture();
        var runtime = new Runtime(f) { Fault = fault };
        Assert.Same(runtime.Failure, Assert.Throws<InvalidOperationException>(() => runtime.Run()));
        runtime.AssertDrained();
    }

    /// <summary>caller 在舊／新 trigger 取消後，收尾仍不使用已取消 token，且第二輪不會偷偷啟動。</summary>
    /// <param name="legacy">取消發生於哪一輪。</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Shadow_Runner_CancellationStillDrains(bool legacy)
    {
        using var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var runtime = new Runtime(f) { CancelDuringLegacy = legacy, Cancellation = cancellation };
        Assert.Throws<OperationCanceledException>(() => runtime.Run(token: cancellation.Token));
        runtime.AssertDrained();
        Assert.Equal(legacy ? 1 : 2, runtime.Rounds.Count);
    }

    /// <summary>清理其中一項失敗仍繼續查殘留、釋放 owner、還原追蹤；原 trigger 例外仍在聚合中。</summary>
    [Fact]
    public void Shadow_Runner_CleanupFailurePreservesOriginalAndContinuesRestoration()
    {
        using var f = new Fixture();
        var runtime = new Runtime(f) { Fault = "legacy-trigger", FailCleanup = true };
        var exception = Assert.Throws<AggregateException>(() => runtime.Run());
        Assert.Contains(runtime.Failure, exception.Flatten().InnerExceptions);
        Assert.Equal(1, runtime.TraceSetting);
        var round = Assert.Single(runtime.Rounds);
        Assert.True(round.Verified);
        Assert.True(round.Disposed);
        Assert.Equal(1, runtime.StepState(runtime.OldStep));
        Assert.Equal(1, runtime.StepState(runtime.NewStep));
    }

    /// <summary>差異必須回傳三方結果，不能僅見新輪等於期望便把舊輪差異隱藏。</summary>
    [Fact]
    public void Shadow_Runner_LegacyDifferenceIsReported()
    {
        using var f = new Fixture();
        var runtime = new Runtime(f) { LegacyValue = 9 };
        var item = Assert.Single(runtime.Run());
        Assert.False(item.BetweenRounds.IsEqual);
        Assert.False(item.LegacyToExpected.IsEqual);
        Assert.True(item.ShadowToExpected.IsEqual);
        runtime.AssertDrained();
    }

    /// <summary>完整但 overflow 的追蹤只可回不可比較；收尾仍完整，不能改當零差異。</summary>
    [Fact]
    public void Shadow_Runner_OverflowIsNotComparable()
    {
        using var f = new Fixture();
        var runtime = new Runtime(f) { Overflow = true };
        var item = Assert.Single(runtime.Run());
        Assert.False(item.BetweenRounds.Comparable);
        Assert.False(item.ShadowToExpected.Comparable);
        runtime.AssertDrained();
    }

    /// <summary>惡意情境文字或合成 token 不得進報告；序號及固定差異種類即可判讀結果。</summary>
    [Fact]
    public void Shadow_TestToken_NeverWrittenToOutputOrReport()
    {
        using var f = new Fixture();
        var runtime = new Runtime(f);
        var text = JsonSerializer.Serialize(runtime.Run());
        Assert.DoesNotContain(Fixture.SecretValue, text, StringComparison.Ordinal);
        Assert.All(Directory.GetFiles(f.Root, "*.json", SearchOption.AllDirectories),
            path => Assert.DoesNotContain(Fixture.SecretValue, File.ReadAllText(path), StringComparison.Ordinal));
    }

    /// <summary>正式組織必須在任何 trace／seed／SDK 寫入前拒絕。</summary>
    [Fact]
    public void Shadow_Runner_NonWhitelistedOrganizationZeroWrites()
    {
        using var f = new Fixture();
        var runtime = new Runtime(f);
        var writes = f.Service.Writes.Count;
        Assert.Throws<InvalidDataException>(() => runtime.Run(organization: "production"));
        Assert.Equal(writes, f.Service.Writes.Count);
        Assert.Equal(0, runtime.TraceWrites);
        Assert.Empty(runtime.Rounds);
    }

    /// <summary>非 CRUD 期望在執行前拒絕；不能跳過推播意圖後宣稱完整影子相符。</summary>
    [Fact]
    public void Shadow_Runner_UnsupportedIntentRejectedBeforeWrites()
    {
        using var f = new Fixture();
        var runtime = new Runtime(f);
        var scenario = Scenario() with { Expected = ShadowTraceParser.Parse(
            "SMV-SHADOW|1|{\"seq\":1,\"operation\":\"Delete\",\"entity\":\"task\",\"id\":\"task-1\"}\r\n" +
            "SMV-SHADOW|1|{\"seq\":2,\"operation\":\"Associate\",\"entity\":\"task\",\"id\":\"task-1\",\"relationship\":\"test\",\"related\":[]}\r\n" +
            "SMV-SHADOW|1|{\"end\":true,\"count\":2}") };
        Assert.Throws<InvalidDataException>(() => runtime.Run(scenario: scenario));
        Assert.Empty(runtime.Rounds);
        Assert.Equal(0, runtime.TraceWrites);
    }

    /// <summary>合成 CRUD 情境，先把 trigger 的 subject 保留，再把意圖的數字欄位套至最終狀態。</summary>
    private static ShadowScenarioDefinition Scenario() => new(
        ShadowTraceParser.Parse("SMV-SHADOW|1|{\"seq\":1,\"operation\":\"Update\",\"entity\":\"task\",\"id\":\"task-1\",\"fields\":{\"score\":2}}\r\nSMV-SHADOW|1|{\"end\":true,\"count\":1}"),
        new[] { new ShadowObservation("task", "task-1", ObservedFields) },
        new HashSet<string>());

    /// <summary>純安全快照；不同實際 ID 透過每輪 aliases 與同一情境代號對應。</summary>
    private static ShadowSnapshot Snapshot(string id, int score) => new(true,
        new[] { new ShadowRow("task", id, JsonSerializer.SerializeToElement(new { score, subject = "triggered" })) });

    /// <summary>本案借用 Fixture 服務；只有產品 runner 會呼叫狀態切換、Seed 和清理。</summary>
    private sealed class Runtime : IShadowScenarioRuntime
    {
        internal readonly Fixture Fixture;
        internal readonly RegistrationDocument Document;
        internal readonly Guid OldStep = Guid.NewGuid();
        internal readonly Guid NewStep;
        internal readonly Guid Church = Guid.NewGuid();
        internal readonly Guid Contact = Guid.NewGuid();
        internal readonly Guid LegacyAssembly = Guid.NewGuid();
        internal readonly Guid LegacyType = Guid.NewGuid();
        internal readonly List<Round> Rounds = new();
        internal readonly InvalidOperationException Failure = new("Injected safe failure");
        internal string Fault = "";
        internal bool FailCleanup;
        internal bool Overflow;
        internal int LegacyValue = 2;
        internal bool? CancelDuringLegacy;
        internal CancellationTokenSource? Cancellation;
        internal int TraceSetting = 1;
        internal int TraceWrites;

        /// <summary>建立合成停用清單與唯一測試綁定，不把這些 GUID 視為實機 ID。</summary>
        internal Runtime(Fixture f)
        {
            Fixture = f;
            var manifest = f.Manifest with { Steps = new[] { f.Manifest.Steps[0] with
            {
                Legacy = new[] { new LegacyStepReference { Assembly = "Vendor.Old", Type = "Vendor.Old.Send", StepId = OldStep.ToString("D") } }
            } } };
            Document = new RegistrationDocument { Assemblies = new[] { manifest } };
            f.Engine.Apply(f.Engine.Plan(manifest, f.Image, Fixture.Organization), manifest, f.Image);
            NewStep = f.Service.Rows.Single(row => row.LogicalName == "sdkmessageprocessingstep").Id;
            f.Service.Seed(new Entity("pluginassembly", LegacyAssembly) { ["name"] = "Vendor.Old" });
            f.Service.Seed(new Entity("plugintype", LegacyType) { ["typename"] = "Vendor.Old.Send", ["pluginassemblyid"] = new EntityReference("pluginassembly", LegacyAssembly) });
            f.Service.Seed(new Entity("sdkmessageprocessingstep", OldStep) { ["eventhandler"] = new EntityReference("plugintype", LegacyType), ["statecode"] = new OptionSetValue(1) });
            f.Service.Seed(new Entity("account", Church));
            f.Service.Seed(new Entity("contact", Contact) { ["new_lineid"] = "SYNTHETIC-TEST", ["parentcustomerid"] = new EntityReference("account", Church) });
        }

        /// <summary>呼叫真 runner；失敗必須從產品路徑傳回，不由測試代替收尾。</summary>
        internal IReadOnlyList<ShadowCaseResult> Run(string organization = Fixture.Organization,
            ShadowScenarioDefinition? scenario = null, CancellationToken token = default) =>
            new ShadowScenarioRunner(Fixture.Engine, Document, organization, "diagnostics", Legacy, this)
                .Run(new[] { scenario ?? Scenario() }, token);

        /// <summary>即時快照從 SDK 狀態讀出，讓互斥驗證看到本次真實切換結果。</summary>
        private LegacyRegistrationSnapshot Legacy() => new(
            new[] { new LegacyAssemblyMetadata(LegacyAssembly, "Vendor.Old", "1.0.0.0", 2) },
            new[] { new LegacyTypeMetadata(LegacyType, LegacyAssembly, "Vendor.Old.Send", false) },
            new[] { new LegacyStepMetadata(OldStep, LegacyType, "Synthetic", "Create", "task", 40, 1, 1, "", StepState(OldStep) == 0) },
            Array.Empty<LegacyImageMetadata>(), Array.Empty<LegacyWorkflowMetadata>());

        /// <summary>直接以穩定 ID 讀正式狀態，避免用產品 helper 自證。</summary>
        internal int StepState(Guid id) => Fixture.Service.Rows.Single(row => row.Id == id).GetAttributeValue<OptionSetValue>("statecode").Value;
        /// <summary>核對所有 owner 都清理、驗證及 Dispose，且沒有自建 task 殘留。</summary>
        internal void AssertDrained()
        {
            Assert.Equal(1, TraceSetting);
            VerifyDisabled();
            Assert.All(Rounds, round => { Assert.True(round.Disposed); Assert.True(round.Verified); });
            Assert.DoesNotContain(Fixture.Service.Rows, row => Rounds.Any(round => round.Id == row.Id));
        }
        /// <summary>只讀案例擁有的設定。</summary>
        public int ReadTraceSetting() => TraceSetting;
        /// <summary>模擬已送達後才失敗，驗證 runner 仍還原原值。</summary>
        public void WriteTraceSetting(int value)
        {
            TraceWrites++;
            TraceSetting = value;
            if (value == 2 && Fault == "trace-write") throw Failure;
        }
        /// <summary>配置尚無遠端 I/O 的 owner，讓 Seed 部分失敗也能進清理路徑。</summary>
        public IShadowScenarioRound CreateRound(ShadowScenarioDefinition scenario, bool legacy)
        {
            var round = new Round(this, legacy);
            Rounds.Add(round);
            return round;
        }
        /// <summary>核對 SDK 的正式狀態，新舊任一啟用都讓測試失敗。</summary>
        public void VerifyDisabled()
        {
            Assert.Equal(1, StepState(OldStep));
            Assert.Equal(1, StepState(NewStep));
        }
    }

    /// <summary>每輪獨占一筆合成 task，注入真實建立後失敗，不由 Dispose 偷補遠端清理。</summary>
    private sealed class Round : IShadowScenarioRound
    {
        private readonly Runtime runtime;
        internal readonly bool Legacy;
        internal readonly Guid Id = Guid.NewGuid();
        internal bool Disposed;
        internal bool Verified;
        private string Point(string operation) => (Legacy ? "legacy-" : "shadow-") + operation;
        /// <summary>只配置本機欄位，不先建立資料。</summary>
        internal Round(Runtime runtime, bool legacy) { this.runtime = runtime; Legacy = legacy; }
        /// <summary>建立後注入失敗以驗證部分 Seed 仍會收尾。</summary>
        public void Seed(CancellationToken cancellationToken)
        {
            runtime.VerifyDisabled();
            runtime.Fixture.Service.Create(new Entity("task", Id) { ["subject"] = "SMV-TEST" });
            if (runtime.Fault == Point("seed")) throw runtime.Failure;
        }
        /// <summary>trigger 已套用而規則未執行的初始快照。</summary>
        public ShadowSnapshot Initial => Snapshot(Id.ToString("D"), 1);
        /// <summary>只對這輪實際 task ID 作明確映射。</summary>
        public IReadOnlyDictionary<string, string> Aliases => new Dictionary<string, string> { [Id.ToString("D")] = "task-1" };
        /// <summary>token 寫入點僅限這輪剛建 task，無共用教會／會友資料。</summary>
        public LegacyRoundContext LegacyContext => new(runtime.Church, runtime.Contact,
            new HashSet<(string, Guid)> { ("task", Id) }, new[] { new LegacyTokenTarget("task", Id, "new_testtoken") }, false);
        /// <summary>trigger 當下必須只啟用正確一輪，並可注入規則例外或 caller 取消。</summary>
        public void Trigger(CancellationToken cancellationToken)
        {
            Assert.Equal(Legacy ? 0 : 1, runtime.StepState(runtime.OldStep));
            Assert.Equal(Legacy ? 1 : 0, runtime.StepState(runtime.NewStep));
            if (runtime.Fault == Point("trigger")) throw runtime.Failure;
            if (runtime.CancelDuringLegacy == Legacy) runtime.Cancellation!.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
        }
        /// <summary>回傳舊輪安全最終值，等待上限必須固定六十秒。</summary>
        public ShadowSnapshot ObserveLegacy(TimeSpan maximumWait, CancellationToken cancellationToken)
        {
            Assert.Equal(TimeSpan.FromSeconds(60), maximumWait);
            if (runtime.Fault == Point("observe")) throw runtime.Failure;
            return Snapshot(Id.ToString("D"), runtime.LegacyValue);
        }
        /// <summary>產生有界新輪 CRUD trace；overflow 必須維持不可比較狀態。</summary>
        public string ReadShadowTrace(TimeSpan maximumWait, CancellationToken cancellationToken)
        {
            Assert.Equal(TimeSpan.FromSeconds(60), maximumWait);
            if (runtime.Fault == Point("trace")) throw runtime.Failure;
            if (runtime.Overflow) return "SMV-SHADOW|1|{\"overflow\":true,\"total\":5}\r\nSMV-SHADOW|1|{\"end\":true,\"count\":0}";
            return "SMV-SHADOW|1|{\"seq\":1,\"operation\":\"Update\",\"entity\":\"task\",\"id\":\"" +
                Id.ToString("D") + "\",\"fields\":{\"score\":2}}\r\nSMV-SHADOW|1|{\"end\":true,\"count\":1}";
        }
        /// <summary>只有新舊停用後才允許刪除；清理失敗不能阻止後續驗證及 Dispose。</summary>
        public void DeleteOwnedRecords()
        {
            runtime.VerifyDisabled();
            runtime.Fixture.Service.Delete("task", Id);
            if (runtime.FailCleanup) throw new IOException("Injected cleanup failure");
        }
        /// <summary>精確核對自建 ID 已不存在，不以任意內容相同列代替。</summary>
        public void VerifyNoRemainingRecords()
        {
            Verified = true;
            Assert.DoesNotContain(runtime.Fixture.Service.Rows, row => row.Id == Id);
        }
        /// <summary>只記錄本機 owner 已排空，不代替產品的遠端刪除責任。</summary>
        public void Dispose() => Disposed = true;
    }
}

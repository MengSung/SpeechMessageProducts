/*
檔案整體說明
目的與定位：部署工具 Live 正常情境的單次編排入口，不修改任何步驟狀態或執行舊程式。
主要流程：先拒絕正式組織與非 Live 狀態，逐輪核對 D34、記錄用量、建立種子、觸發及觀察；
D34失敗使用LIVE-PREP-03安全摘要；實際欄位沿用ShadowComparer精確比較，推播以測試Channel的前後用量核對。
資源與隔離：runtime 由命令借用，round 是每個情境的唯一 owner；finally 即使取消也以
None token 清理、驗證殘留及 Dispose。追蹤原值在最外層恢復，原失敗與清理失敗皆保留。
Dataverse fault只附固定階段與scenario核定observe的實體邏輯名稱及數字碼；可索引集合直接索引，
不讀fault本文，cleanup fault另標cleanup階段且仍完成後續finally釋放。
輸出只含固定情境代號、型別、筆數及安全差異位置，沒有原始記錄、秘密或跨命令快取。
驗證方式：離線故障注入涵蓋取消、欄位差異、推播差異及待辦清理；實機證據另由演練產生。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.ServiceModel;
using System.Threading;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Deployment.Shadow;
namespace SpeechMessage.Dynamics.Deployment.Live;

/// <summary>
/// Live單案SDK故障的安全識別包裝，只保留操作階段、實體邏輯名稱及原始fault供數字碼投影。
/// 原例外僅隨本次命令持有，不讀取或輸出Message、TraceText、Data及任何記錄欄位。
/// </summary>
internal sealed class LiveCheckFailure : Exception
{
    /// <summary>建立受限摘要；stage及entity由runner固定選取，不接受使用者資料作診斷欄位。</summary>
    internal LiveCheckFailure(string stage, string entity, FaultException<OrganizationServiceFault> inner)
        : base("Live check operation failed.", inner)
    {
        SafeSummary = "LIVE-CHECK stage=" + stage + "; entity=" +
            ShadowSeedCreateException.EscapeDiagnosticAlias(entity);
    }

    /// <summary>唯一可列印的文字摘要；不含原始例外本文或記錄識別。</summary>
    internal string SafeSummary { get; }
}

/// <summary>單次命令的已核定情境；Expected 是套用寫入後的最終快照，不是意圖清單。</summary>
internal sealed record LiveCheckScenario(string Name, string Type, string Module, string Tier, bool Enabled,
    string Mode, int ExpectedPushes, ShadowSnapshot Expected, IReadOnlyList<ShadowObservation> Observe,
    IReadOnlySet<string> Volatile);
/// <summary>單輪實際快照與本輪待辦證據數，Aliases 僅允許明確配置的 ID 映射。</summary>
internal sealed record LiveCheckObservation(ShadowSnapshot Actual, int TaskEvidenceCount,
    IReadOnlyDictionary<string, string>? Aliases)
{
    /// <summary>真 SDK 輪以自身 Initial 與明確 alias 套用核定 expected trace，避免跨輪 GUID 配對。</summary>
    public ShadowSnapshot? Expected { get; init; }
}
/// <summary>單案安全結果，不包含被比較的業務值。</summary>
internal sealed record LiveCheckCase(string Name, string Type, string Status, ShadowComparison Comparison,
    long ActualPushes, int TaskEvidenceCount);
/// <summary>完整收尾後發布的報告；未檢查的項目不能計入成功案例。</summary>
internal sealed record LiveCheckResult(IReadOnlyList<LiveCheckCase> Cases)
{
    /// <summary>實際執行的案例數，dormant 不算。</summary>
    public int CheckedCount => Cases.Count(item => item.Status != "not-checked");
    /// <summary>至少有一案且每個已檢查案例都通過；只有 dormant 不冒稱通過。</summary>
    public bool Passed => CheckedCount > 0 && Cases.All(item => item.Status is "pass" or "not-checked");
}
/// <summary>命令借用的 SDK／用量能力，不允許編排器切換步驟或保存秘密。</summary>
internal interface ILiveCheckRuntime
{
    /// <summary>當場核對唯一測試會友與全部 Channel 安全狀態。</summary>
    void VerifyD34(CancellationToken cancellationToken);
    /// <summary>讀取原追蹤設定，值只在此命令保存。</summary>
    int ReadTraceSetting();
    /// <summary>還原追蹤，清理使用不可取消 token。</summary>
    void WriteTraceSetting(int value, CancellationToken cancellationToken);
    /// <summary>讀取測試 Channel 用量，不回傳任何 token。</summary>
    long ReadUsage(CancellationToken cancellationToken);
    /// <summary>配置本輪唯一 owner，尚未建立 seed。</summary>
    ILiveCheckRound CreateRound(LiveCheckScenario scenario);
}
/// <summary>單輪的資料／流程 owner，所有 I/O 同步完成後才 Dispose。</summary>
internal interface ILiveCheckRound : IDisposable
{
    /// <summary>先登錄 attempted ID 再建立本輪資料。</summary>
    void Seed(CancellationToken cancellationToken);
    /// <summary>只觸發本輪 owned 資料，回應不明不重試。</summary>
    void Trigger(CancellationToken cancellationToken);
    /// <summary>有界等待並讀回明列欄位與待辦。</summary>
    LiveCheckObservation Observe(TimeSpan maximumWait, CancellationToken cancellationToken);
    /// <summary>子列先於父列，取消後仍清理。</summary>
    void Cleanup(CancellationToken cancellationToken);
    /// <summary>以權威 ID 核對沒有本輪殘留。</summary>
    void VerifyNoRemaining(CancellationToken cancellationToken);
}
/// <summary>不改步驟狀態的順序執行器；跨輪僅保留安全報告。</summary>
internal sealed class LiveCheckRunner(ILiveCheckRuntime runtime)
{
    /// <summary>驗證整批狀態後執行；任何清理失敗都不能發布通過結果。</summary>
    internal LiveCheckResult Run(OrganizationDefinition organization, string module,
        IReadOnlyList<LiveCheckScenario> scenarios, CancellationToken cancellationToken = default)
    {
        if (organization.Kind != "test" || scenarios.Count is < 1 or > 64 ||
            scenarios.Any(item => item.Module != module || item.Tier is not ("live" or "dormant") ||
                item.ExpectedPushes < 0 || (item.Tier == "live" && (!item.Enabled || item.Mode != "Live"))) ||
            scenarios.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != scenarios.Count)
            throw new InvalidDataException("Live check safety precondition failed.");
        var results = new List<LiveCheckCase>();
        var failures = new List<Exception>();
        var trace = runtime.ReadTraceSetting();
        try
        {
            foreach (var scenario in scenarios)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (scenario.Tier == "dormant")
                {
                    results.Add(new(scenario.Name, scenario.Type, "not-checked", new(false, Array.Empty<ShadowDifference>()), 0, 0));
                    continue;
                }
                var roundFailures = new List<Exception>();
                ILiveCheckRound? round = null;
                var stage = "safety-before-seed";
                try
                {
                    VerifySafety(runtime, scenario, cancellationToken);
                    stage = "usage-before-seed";
                    var before = runtime.ReadUsage(cancellationToken);
                    round = runtime.CreateRound(scenario);
                    stage = "seed";
                    round.Seed(cancellationToken);
                    stage = "safety-after-seed";
                    VerifySafety(runtime, scenario, cancellationToken);
                    stage = "trigger";
                    round.Trigger(cancellationToken);
                    stage = "readback";
                    var observed = round.Observe(TimeSpan.FromSeconds(60), cancellationToken);
                    var comparison = ShadowComparer.CompareFinalStates(observed.Expected ?? scenario.Expected, observed.Actual,
                        scenario.Observe, observed.Aliases, scenario.Volatile);
                    stage = "usage-after-readback";
                    var after = runtime.ReadUsage(cancellationToken);
                    var delta = checked(after - before);
                    if (before < 0 || after < before || observed.TaskEvidenceCount < 0)
                        throw new InvalidDataException("Live check evidence is invalid.");
                    results.Add(new(scenario.Name, scenario.Type,
                        comparison.IsEqual && delta == scenario.ExpectedPushes ? "pass" : "difference",
                        comparison, delta, observed.TaskEvidenceCount));
                }
                catch (FaultException<OrganizationServiceFault> failure)
                {
                    var entity = scenario.Observe.Count == 0 ? "unknown" : scenario.Observe[0].Entity;
                    roundFailures.Add(new LiveCheckFailure(stage, entity, failure));
                }
                catch (Exception failure) { roundFailures.Add(failure); }
                finally
                {
                    if (round is not null)
                    {
                        stage = "cleanup";
                        Attempt(() => round.Cleanup(CancellationToken.None), roundFailures);
                        Attempt(() => round.VerifyNoRemaining(CancellationToken.None), roundFailures);
                        Attempt(round.Dispose, roundFailures);
                    }
                }
                ThrowFailures(roundFailures);
            }
        }
        catch (Exception failure) { failures.Add(failure); }
        finally { Attempt(() => runtime.WriteTraceSetting(trace, CancellationToken.None), failures); }
        ThrowFailures(failures);
        return new(results.AsReadOnly());
    }
    /// <summary>收尾每一項都嘗試，避免前一項失敗造成後續資源無 owner。</summary>
    private static void Attempt(Action action, List<Exception> failures)
    {
        try { action(); } catch (Exception failure) { failures.Add(failure); }
    }
    /// <summary>
    /// 將執行期間每次權威D34核對失敗映射為固定安全原因碼與目前情境識別；
    /// caller取消仍原樣傳遞，真實例外只保留於單輪包裝供共用回報且不輸出本文。
    /// </summary>
    private static void VerifySafety(ILiveCheckRuntime runtime, LiveCheckScenario scenario,
        CancellationToken cancellationToken) =>
        _ = LivePreparationFailure.Capture(LivePreparationReason.Safety, scenario.Module, scenario.Name,
            null, null, () => { runtime.VerifyD34(cancellationToken); return true; }, cancellationToken);

    /// <summary>單一錯誤保留原 stack／取消型別，多個錯誤完整聚合而不覆蓋根因。</summary>
    private static void ThrowFailures(List<Exception> failures)
    {
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException(failures);
    }
}

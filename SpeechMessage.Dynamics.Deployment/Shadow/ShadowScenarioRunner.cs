/*
 * 檔案整體說明
 *
 * 目的與定位：部署工具的先舊後新影子執行編排，統一兩輪的互斥與故障收尾責任。
 * 主要功能與流程：清單、已解析的情境與 operation-local runtime 進入後，先驗輸入，
 * 保存追蹤設定、逐輪建立獨立 seed owner，舊輪交 SwitchModule 的 D5-18 scope，
 * 新輪讀完整追蹤並套成最終快照；兩輪互比且各自與期望比較，最後還原追蹤設定。
 * 使用邊界與維護：SDK 租約與組織寫鎖由 CLI 擁有；runtime 負責精確查詢、時間窗、
 * 資料安全投影及自建列清冊。本檔只比較 CRUD；其他意圖明確拒絕，不冒充推播驗收。
 * 所有 owner 只屬單一 Run；取消不跳過停用、刪除、殘留核對或 Dispose。收尾失敗
 * 保留原始例外並聚合，報告只含比較結果與情境序號，不輸出情境本文或秘密。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace SpeechMessage.Dynamics.Deployment.Shadow;

/// <summary>已解析的安全 CRUD 情境；期望為完整意圖，觀察範圍使用穩定代號。</summary>
internal sealed record ShadowScenarioDefinition(ShadowTrace Expected, IReadOnlyList<ShadowObservation> Observe,
    IReadOnlySet<string> VolatileFields);

/// <summary>單一情境的三方比較；只有完整可比且差異全為零才可標成功。</summary>
internal sealed record ShadowCaseResult(int Index, ShadowComparison BetweenRounds,
    ShadowComparison LegacyToExpected, ShadowComparison ShadowToExpected);

/// <summary>
/// 操作區域的外部能力；實作由同一命令持有，不能在建構 round 時先建立測試資料。
/// Trace 設定只讀寫核定測試組織；VerifyDisabled 必須唯讀確認該模組新舊皆停用。
/// </summary>
internal interface IShadowScenarioRuntime
{
    /// <summary>唯讀取得原追蹤等級，不得以缺欄位推定預設值。</summary>
    int ReadTraceSetting();
    /// <summary>寫入已驗證追蹤等級；失敗可能已送達，runner 仍嘗試還原原值。</summary>
    void WriteTraceSetting(int value);
    /// <summary>只配置本輪 owner；所有 I/O 延遲至 Seed，確保部分建立也能確定清理。</summary>
    IShadowScenarioRound CreateRound(ShadowScenarioDefinition scenario, bool legacy);
    /// <summary>唯讀確認全部所選舊新步驟與流程皆停用；任何歧義或啟用皆失敗。</summary>
    void VerifyDisabled();
}

/// <summary>
/// 單輪 seed、觀察及查詢 owner；別名、初始快照及 D5-18 context 必須屬同一輪，
/// 初始值為 trigger 已套用且規則未執行的安全投影。跨輪不可重用可變資料或 ID。
/// Dispose 只排空本機狀態；遠端清理由 DeleteOwnedRecords 明確執行，且不能用取消略過。
/// </summary>
internal interface IShadowScenarioRound : IDisposable
{
    /// <summary>建立並立即登錄每筆自建資料；部分失敗後仍須允許刪除已登錄記錄。</summary>
    void Seed(CancellationToken cancellationToken);
    /// <summary>該輪的安全初始投影，不得包含 token、LINE ID 或原始個資。</summary>
    ShadowSnapshot Initial { get; }
    /// <summary>實際 ID 到穩定代號的顯式映射；名稱相同不能作為配對依據。</summary>
    IReadOnlyDictionary<string, string> Aliases { get; }
    /// <summary>盤點及 seed 清冊導出的 D5-18 範圍，不由任意 CLI JSON 反序列化。</summary>
    LegacyRoundContext LegacyContext { get; }
    /// <summary>在已啟用的唯一輪內觸發；不得自行啟用其他步驟或建立背景工作。</summary>
    void Trigger(CancellationToken cancellationToken);
    /// <summary>等待所選舊步驟完成並讀安全 observe；最多等待六十秒，失敗不可當空結果。</summary>
    ShadowSnapshot ObserveLegacy(TimeSpan maximumWait, CancellationToken cancellationToken);
    /// <summary>僅取精確型別、目標／關聯及時間窗的安全追蹤；最多六十秒，禁止讀無界歷史。</summary>
    string ReadShadowTrace(TimeSpan maximumWait, CancellationToken cancellationToken);
    /// <summary>停用後刪除所有本輪自建列；每列失敗仍須嘗試其他列，不刪共用測試教會／會友。</summary>
    void DeleteOwnedRecords();
    /// <summary>唯讀確認本輪自建資料殘留為零；查詢失敗或不完整也必須失敗。</summary>
    void VerifyNoRemainingRecords();
}

/// <summary>同步編排先舊後新兩輪；本實例只借用呼叫端的服務與命令租約。</summary>
internal sealed class ShadowScenarioRunner
{
    private readonly DeploymentEngine engine;
    private readonly RegistrationDocument document;
    private readonly string organization;
    private readonly string module;
    private readonly Func<LegacyRegistrationSnapshot> readLegacy;
    private readonly IShadowScenarioRuntime runtime;

    /// <summary>建立單次命令編排器；建構時不取得連線、不讀秘密、不寫入遠端資料。</summary>
    public ShadowScenarioRunner(DeploymentEngine engine, RegistrationDocument document, string organization,
        string module, Func<LegacyRegistrationSnapshot> readLegacy, IShadowScenarioRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(readLegacy);
        ArgumentNullException.ThrowIfNull(runtime);
        this.engine = engine;
        this.document = document;
        this.organization = organization;
        this.module = module;
        this.readLegacy = readLegacy;
        this.runtime = runtime;
    }

    /// <summary>
    /// 執行一至六個完整 CRUD 情境；全部輸入在追蹤寫入前檢查。原設定必須為平台
    /// 已定義的零至二；即使啟用追蹤的回應失敗，也嘗試還原。任何失敗不發布部分結果。
    /// 非 CRUD 情境需由完整意圖比較路徑處理，本入口明確拒絕而不略過。
    /// </summary>
    public IReadOnlyList<ShadowCaseResult> Run(IReadOnlyList<ShadowScenarioDefinition> scenarios,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scenarios);
        if (organization != "goodshepherdback" || scenarios.Count is < 1 or > 6 ||
            document.Assemblies is null || document.Assemblies.Count is < 1 or > 2)
            throw InvalidScenario();
        foreach (var assembly in document.Assemblies)
        {
            RegistrationManifestLoader.Validate(assembly);
            if (assembly.Steps.Any(step => step.EnabledByOrganization.Values.Any(enabled => enabled)))
                throw InvalidScenario();
        }
        foreach (var scenario in scenarios)
        {
            if (scenario is null || scenario.Expected is null || !scenario.Expected.Comparable ||
                scenario.Expected.Intents is null || scenario.Expected.Intents.Count > 128 ||
                scenario.Expected.Intents.Any(intent => intent.Operation is not ("Create" or "Update" or "Delete")) ||
                scenario.Observe is null || scenario.VolatileFields is null ||
                !ShadowComparer.CompareFinalStates(new ShadowSnapshot(true, Array.Empty<ShadowRow>()),
                    new ShadowSnapshot(true, Array.Empty<ShadowRow>()), scenario.Observe).Comparable)
                throw InvalidScenario();
        }
        cancellationToken.ThrowIfCancellationRequested();
        runtime.VerifyDisabled();
        var originalTrace = runtime.ReadTraceSetting();
        if (originalTrace is < 0 or > 2) throw InvalidScenario();
        var failures = new List<Exception>();
        var results = new List<ShadowCaseResult>(scenarios.Count);
        try
        {
            runtime.WriteTraceSetting(2);
            for (var index = 0; index < scenarios.Count; index++)
            {
                var scenario = scenarios[index];
                var legacy = RunRound(scenario, true, cancellationToken);
                var shadow = RunRound(scenario, false, cancellationToken);
                results.Add(new ShadowCaseResult(index + 1,
                    ShadowComparer.CompareFinalStates(legacy.Actual, shadow.Actual, scenario.Observe,
                        volatileFields: scenario.VolatileFields),
                    ShadowComparer.CompareFinalStates(legacy.Actual, legacy.Expected, scenario.Observe,
                        volatileFields: scenario.VolatileFields),
                    ShadowComparer.CompareFinalStates(shadow.Actual, shadow.Expected, scenario.Observe,
                        volatileFields: scenario.VolatileFields)));
            }
        }
        catch (Exception failure) { failures.Add(failure); }
        finally
        {
            Attempt(() => Switch("off"), failures);
            Attempt(runtime.VerifyDisabled, failures);
            Attempt(() => runtime.WriteTraceSetting(originalTrace), failures);
        }
        ThrowFailures(failures);
        return results.AsReadOnly();
    }

    /// <summary>
    /// owner 在 Seed 前取得；任何種子、觸發或觀察失敗都依序停用、刪除、核對與 Dispose。
    /// 舊輪的 token 注入及停用由正式 SwitchModule scope 保護，第二輪只在第一輪完全
    /// 收尾後才建立。兩側先轉情境代號；期望與新輪均依初始值套完整 CRUD 序列。
    /// </summary>
    private RoundResult RunRound(ShadowScenarioDefinition scenario, bool legacy, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        runtime.VerifyDisabled();
        var round = runtime.CreateRound(scenario, legacy) ?? throw InvalidScenario();
        var failures = new List<Exception>();
        RoundResult? result = null;
        try
        {
            round.Seed(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var aliases = ShadowProjection.CopyAliases(round.Aliases);
            var initial = Normalize(round.Initial, aliases);
            var expected = ShadowProjection.Apply(initial, scenario.Expected);
            ShadowSnapshot? actual = null;
            if (legacy)
                engine.SwitchModule(document, organization, module, "legacy", readLegacy,
                    round.LegacyContext, () =>
                    {
                        round.Trigger(cancellationToken);
                        actual = Normalize(round.ObserveLegacy(TimeSpan.FromSeconds(60), cancellationToken), aliases);
                    }, cancellationToken);
            else
            {
                Switch("enabled", cancellationToken);
                round.Trigger(cancellationToken);
                var trace = ShadowTraceParser.Parse(round.ReadShadowTrace(TimeSpan.FromSeconds(60), cancellationToken));
                // 先於清理讀取完整 trace；正規化發生在套用之後，保留真實 ID 的順序一致性。
                actual = Normalize(ShadowProjection.Apply(round.Initial, trace), aliases);
            }
            cancellationToken.ThrowIfCancellationRequested();
            result = new RoundResult(actual ?? throw InvalidScenario(), expected);
        }
        catch (Exception failure) { failures.Add(failure); }
        finally
        {
            Attempt(() => Switch("off"), failures);
            Attempt(runtime.VerifyDisabled, failures);
            Attempt(round.DeleteOwnedRecords, failures);
            Attempt(round.VerifyNoRemainingRecords, failures);
            Attempt(round.Dispose, failures);
        }
        ThrowFailures(failures);
        return result ?? throw InvalidScenario();
    }

    /// <summary>將正式 ID 映射成情境代號；不可比較快照保持不可比較，不發布部分列。</summary>
    private static ShadowSnapshot Normalize(ShadowSnapshot snapshot, IReadOnlyDictionary<string, string>? aliases) =>
        snapshot.Comparable
            ? new ShadowSnapshot(true, Array.AsReadOnly(ShadowProjection.Prepare(snapshot, aliases).Values.ToArray()))
            : new ShadowSnapshot(false, Array.Empty<ShadowRow>());

    /// <summary>借用同一服務與組織寫鎖切換；收尾呼叫固定用未取消的預設 token。</summary>
    private void Switch(string target, CancellationToken cancellationToken = default) =>
        engine.SwitchModule(document, organization, module, target, readLegacy, cancellationToken: cancellationToken);

    /// <summary>獨立嘗試每個清理動作，任何例外都保留給命令共用診斷，不阻止後續排空。</summary>
    private static void Attempt(Action action, List<Exception> failures)
    {
        try { action(); }
        catch (Exception failure) { failures.Add(failure); }
    }

    /// <summary>單一失敗保留原 stack；多個失敗保留全部真實例外，不以清理失敗取代原因。</summary>
    private static void ThrowFailures(List<Exception> failures)
    {
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Shadow execution or cleanup failed.", failures);
    }

    /// <summary>固定安全錯誤，不輸出情境內容、ID、追蹤或秘密。</summary>
    private static InvalidDataException InvalidScenario() => new("Shadow scenario safety precondition failed.");

    /// <summary>本輪 detached 最終值及期望；只活於當次同步執行。</summary>
    private sealed record RoundResult(ShadowSnapshot Actual, ShadowSnapshot Expected);
}

/*
 * 檔案整體說明
 *
 * 目的與定位：驗證影子 SDK owner 的記錄身分、精確查詢及清理邊界；全部依賴為
 * 記憶體 SDK 與假時鐘，不連組織、不發 LINE，也不把合成查詢成功描述為實機證據。
 * 主要功能與流程：直接呼叫正式 runtime／round，建立相同名稱的不同記錄、模擬
 * 建立後回應失敗、系統作業未完成與追蹤歧義，斷言清冊保存、有限等待、零秘密輸出。
 * 使用邊界與維護：案例獨占 Entity 與輸入集合，測試 owner 在 finally 刪除自己的
 * 合成資料；正式 round 的 Dispose 不取代遠端清理，失敗需可由呼叫端繼續嘗試。
 * 顯式 alias 涵蓋 owned 前向 lookup、shared 只讀身分與 trigger patch；注入 SDK
 * 改寫、主鍵錯配及 caller 取消，斷言發布失敗、shared 不變與清冊逆序排空。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Shadow;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>在合成 SDK 上驗證真正 owner，而非手動替產品建立或刪除 seed。</summary>
public sealed class ShadowSdkRuntimeTests
{
    /// <summary>建立已送達但回應失敗時，預先登錄 GUID 仍能精確找回並刪除，不能遺留不明記錄。</summary>
    [Fact]
    public void SdkRound_CreateResponseLostStillFindsOwnedIdForCleanup()
    {
        using var f = new Fixture();
        var service = new PollingService(f.Service) { LoseCreateResponse = true };
        var definition = Definition();
        var runtime = new ShadowSdkRuntime(service, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new Dictionary<ShadowScenarioDefinition, ShadowSdkScenario> { [definition] = Source() }, () => { }, f.Clock);
        using var round = runtime.CreateRound(definition, true);
        Assert.Throws<InvalidOperationException>(() => round.Seed(CancellationToken.None));
        Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "region"));
        round.DeleteOwnedRecords();
        round.VerifyNoRemainingRecords();
    }

    /// <summary>只能在核定 step、目標、時間窗作業成功後讀觀察；延遲 timer 結束須回到零。</summary>
    [Fact]
    public void SdkRound_LegacyWaitsForExactCompletedOperationBeforeObserve()
    {
        using var f = new Fixture();
        var service = new PollingService(f.Service) { ReadyAfter = 2 };
        var definition = Definition();
        var source = Source();
        var runtime = new ShadowSdkRuntime(service, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new Dictionary<ShadowScenarioDefinition, ShadowSdkScenario> { [definition] = source }, () => { }, f.Clock);
        using var round = runtime.CreateRound(definition, true);
        round.Seed(CancellationToken.None);
        round.Trigger(CancellationToken.None);
        var observed = round.ObserveLegacy(TimeSpan.FromSeconds(60), CancellationToken.None);
        Assert.Equal(2, Assert.Single(observed.Rows).Fields.GetProperty("score").GetInt32());
        Assert.Equal(2, service.Polls);
        Assert.Equal(source.LegacyStepIds[0], service.RequestedStep);
        Assert.Equal(round.Aliases.Single(pair => pair.Value == "record-1").Key, service.RequestedTarget.ToString("D"));
        Assert.Equal(0, f.Clock.ActiveTimers);
        round.DeleteOwnedRecords();
    }

    /// <summary>非本輪追蹤原文不可進結果；只抽取已關聯追蹤中的安全版本行，精確保留完整標記。</summary>
    [Fact]
    public void SdkRound_ShadowTraceIsCorrelatedAndOnlySafeLinesLeaveRuntime()
    {
        using var f = new Fixture();
        var service = new PollingService(f.Service);
        var definition = Definition();
        var source = Source();
        var runtime = new ShadowSdkRuntime(service, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new Dictionary<ShadowScenarioDefinition, ShadowSdkScenario> { [definition] = source }, () => { }, f.Clock);
        using var round = runtime.CreateRound(definition, false);
        round.Seed(CancellationToken.None);
        round.Trigger(CancellationToken.None);
        service.Trace = Fixture.SecretValue + "\r\nSMV-SHADOW|1|{\"end\":true,\"count\":0}\r\n";
        var safe = round.ReadShadowTrace(TimeSpan.FromSeconds(60), CancellationToken.None);
        Assert.DoesNotContain(Fixture.SecretValue, safe, StringComparison.Ordinal);
        Assert.True(ShadowTraceParser.Parse(safe).Comparable);
        service.Fault = "missing-end";
        Assert.False(ShadowTraceParser.Parse(round.ReadShadowTrace(TimeSpan.FromSeconds(60), CancellationToken.None)).Comparable);
        Assert.Equal(source.ShadowStepIds[0], service.RequestedStep);
        Assert.Equal("Synthetic.Plugin", service.RequestedType);
        round.DeleteOwnedRecords();
    }

    /// <summary>重複作業、重複追蹤或作業失敗必須明確拒絕；不能選第一筆或誤判零差異。</summary>
    /// <param name="fault">注入的伺服器歧義或失敗。</param>
    [Theory]
    [InlineData("duplicate-operation")]
    [InlineData("duplicate-trace")]
    [InlineData("failed-operation")]
    public void SdkRound_AmbiguousOrFailedExecutionCannotCompare(string fault)
    {
        using var f = new Fixture();
        var service = new PollingService(f.Service) { Fault = fault };
        var definition = Definition();
        var runtime = new ShadowSdkRuntime(service, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new Dictionary<ShadowScenarioDefinition, ShadowSdkScenario> { [definition] = Source() }, () => { }, f.Clock);
        using var round = runtime.CreateRound(definition, false);
        round.Seed(CancellationToken.None);
        round.Trigger(CancellationToken.None);
        Assert.ThrowsAny<Exception>(() => round.ReadShadowTrace(TimeSpan.FromSeconds(60), CancellationToken.None));
        Assert.Equal(0, f.Clock.ActiveTimers);
        round.DeleteOwnedRecords();
    }

    /// <summary>未出現作業最多查六十秒、兩秒一次；逾時不是正常 caller 取消，timer 仍確定釋放。</summary>
    [Fact]
    public void SdkRound_PollingIsBoundedAndTimersDrain()
    {
        using var f = new Fixture();
        var service = new PollingService(f.Service) { ReadyAfter = int.MaxValue };
        var definition = Definition();
        var runtime = new ShadowSdkRuntime(service, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new Dictionary<ShadowScenarioDefinition, ShadowSdkScenario> { [definition] = Source() }, () => { }, f.Clock);
        using var round = runtime.CreateRound(definition, true);
        round.Seed(CancellationToken.None);
        round.Trigger(CancellationToken.None);
        Assert.Throws<TimeoutException>(() => round.ObserveLegacy(TimeSpan.FromSeconds(60), CancellationToken.None));
        Assert.Equal(31, service.Polls);
        Assert.Equal(30, f.Clock.DelayCount);
        Assert.Equal(0, f.Clock.ActiveTimers);
        round.DeleteOwnedRecords();
    }
    /// <summary>兩個同名 seed 仍為不同 ID，前後輪不重用；安全投影不可保存原始姓名或 token。</summary>
    [Fact]
    public void SdkRound_SeedsHaveStableDistinctIdsAndSafeSnapshots()
    {
        using var f = new Fixture();
        var organization = Guid.NewGuid();
        f.Service.Seed(new Entity("organization", organization) { ["plugintracelogsetting"] = new OptionSetValue(1) });
        var definition = Definition();
        var source = Source();
        var runtime = Runtime(f, organization, definition, source);
        Assert.Equal(1, runtime.ReadTraceSetting());
        runtime.WriteTraceSetting(2);
        Assert.Equal(2, runtime.ReadTraceSetting());
        using var round = runtime.CreateRound(definition, false);
        round.Seed(CancellationToken.None);
        var records = f.Service.Rows.Where(row => row.LogicalName == "region").ToArray();
        Assert.Equal(2, records.Length);
        Assert.NotEqual(records[0].Id, records[1].Id);
        Assert.All(records, row => Assert.StartsWith("SMV-TEST-", row.GetAttributeValue<string>("name"), StringComparison.Ordinal));
        var json = JsonSerializer.Serialize(round.Initial);
        Assert.DoesNotContain("Synthetic secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Original Name", json, StringComparison.Ordinal);
        Assert.Equal(2, round.Aliases.Count);
        round.Trigger(CancellationToken.None);
        Assert.Equal(2, f.Service.Rows.Single(row => row.Id.ToString("D") == round.Aliases.Single(pair => pair.Value == "record-1").Key).GetAttributeValue<int>("score"));
        round.DeleteOwnedRecords();
        round.VerifyNoRemainingRecords();
        using var next = runtime.CreateRound(definition, false);
        next.Seed(CancellationToken.None);
        Assert.DoesNotContain(next.Aliases.Keys, key => round.Aliases.ContainsKey(key));
        next.DeleteOwnedRecords();
        next.VerifyNoRemainingRecords();
    }

    /// <summary>第二個 seed 建立時故障，前一筆仍有清冊可清理，不能因未完成整批而遺失 owner。</summary>
    [Fact]
    public void SdkRound_PartialSeedFailureStillDeletesCreatedRows()
    {
        using var f = new Fixture();
        var definition = Definition();
        var runtime = Runtime(f, Guid.NewGuid(), definition, Source());
        using var round = runtime.CreateRound(definition, true);
        var calls = 0;
        f.Service.BeforeWrite = () => { if (++calls == 2) throw new InvalidOperationException("Injected"); };
        Assert.Throws<InvalidOperationException>(() => round.Seed(CancellationToken.None));
        f.Service.BeforeWrite = null;
        round.DeleteOwnedRecords();
        round.VerifyNoRemainingRecords();
        Assert.DoesNotContain(f.Service.Rows, row => row.LogicalName == "region");
    }

    /// <summary>種子重複別名或指定外部資料庫 ID 必須在任何 Create 之前拒絕，不能覆寫既有資料。</summary>
    /// <param name="duplicate">是否重複別名，否則注入外部 ID。</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SdkRound_InvalidSeedRejectedBeforeWrites(bool duplicate)
    {
        using var f = new Fixture();
        var definition = Definition();
        var source = Source();
        var seeds = source.Seeds.ToArray();
        if (duplicate) seeds[1] = seeds[1] with { Alias = seeds[0].Alias };
        else seeds[1].Record.Id = Guid.NewGuid();
        var runtime = Runtime(f, Guid.NewGuid(), definition, source with { Seeds = seeds });
        Assert.ThrowsAny<Exception>(() =>
        {
            using var round = runtime.CreateRound(definition, false);
            round.Seed(CancellationToken.None);
        });
        Assert.Empty(f.Service.Writes);
    }

    /// <summary>Dispose 後不能再執行操作，也不能把 owner 的資料保留給另一個情境。</summary>
    [Fact]
    public void SdkRound_DisposedOwnerCannotBeReused()
    {
        using var f = new Fixture();
        var definition = Definition();
        var runtime = Runtime(f, Guid.NewGuid(), definition, Source());
        var round = runtime.CreateRound(definition, false);
        round.Seed(CancellationToken.None);
        round.DeleteOwnedRecords();
        round.Dispose();
        Assert.Throws<ObjectDisposedException>(() => round.Trigger(CancellationToken.None));
    }

    /// <summary>追蹤設定缺失不得寫入或默認；無效等級同樣零寫入。</summary>
    [Fact]
    public void SdkRuntime_TraceSettingMissingOrInvalidFailsClosed()
    {
        using var f = new Fixture();
        var id = Guid.NewGuid();
        f.Service.Seed(new Entity("organization", id));
        var definition = Definition();
        var runtime = Runtime(f, id, definition, Source());
        Assert.ThrowsAny<Exception>(() => runtime.ReadTraceSetting());
        Assert.ThrowsAny<Exception>(() => runtime.WriteTraceSetting(3));
        Assert.Empty(f.Service.Writes);
    }

    /// <summary>第二筆送達失敗後禁止 trigger；失敗清冊仍可清理，不能以開始過 Seed 當作整批成功。</summary>
    [Fact]
    public void SdkRound_FailedSeedCannotTrigger()
    {
        using var f = new Fixture();
        var definition = Definition();
        using var round = Runtime(f, Guid.NewGuid(), definition, Source()).CreateRound(definition, true);
        var writes = 0;
        f.Service.BeforeWrite = () => { if (++writes == 2) throw new InvalidOperationException("Injected"); };
        Assert.Throws<InvalidOperationException>(() => round.Seed(CancellationToken.None));
        f.Service.BeforeWrite = null;
        Assert.Throws<InvalidDataException>(() => round.Trigger(CancellationToken.None));
        round.DeleteOwnedRecords();
        round.VerifyNoRemainingRecords();
    }

    /// <summary>SDK 修改傳入 Entity 的 ID、lookup 及可變值，不可污染 owner 的初始快照及清理身分。</summary>
    [Fact]
    public void SdkRound_ServiceMutationCannotChangeOwner()
    {
        using var f = new Fixture();
        var source = Source();
        source.Seeds[0].Record["choice"] = new OptionSetValue(7);
        var definition = Definition();
        var service = new PollingService(f.Service) { MutateCreate = true };
        var runtime = new ShadowSdkRuntime(service, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new Dictionary<ShadowScenarioDefinition, ShadowSdkScenario> { [definition] = source }, () => { }, f.Clock);
        using var round = runtime.CreateRound(definition, true);
        var before = JsonSerializer.Serialize(round.Initial);
        round.Seed(CancellationToken.None);
        Assert.Equal(before, JsonSerializer.Serialize(round.Initial));
        round.DeleteOwnedRecords();
        round.VerifyNoRemainingRecords();
        Assert.DoesNotContain(f.Service.Rows, row => row.LogicalName == "region");
    }

    /// <summary>清理已開始後不可再 trigger；重複清理可安全排空，不可在刪除後重新更新同一輪。</summary>
    [Fact]
    public void SdkRound_CleanupClosesWriteLifecycle()
    {
        using var f = new Fixture();
        var definition = Definition();
        using var round = Runtime(f, Guid.NewGuid(), definition, Source()).CreateRound(definition, true);
        round.Seed(CancellationToken.None);
        round.DeleteOwnedRecords();
        Assert.Throws<InvalidDataException>(() => round.Trigger(CancellationToken.None));
        round.DeleteOwnedRecords();
        round.VerifyNoRemainingRecords();
    }

    /// <summary>精確主鍵查詢若 SDK 回傳別的 ID，觀察與清理均拒絕，不可發布其他使用者的列。</summary>
    [Fact]
    public void SdkRound_WrongReadIdentityFailsClosed()
    {
        using var f = new Fixture();
        var definition = Definition();
        var service = new PollingService(f.Service);
        var runtime = new ShadowSdkRuntime(service, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new Dictionary<ShadowScenarioDefinition, ShadowSdkScenario> { [definition] = Source() }, () => { }, f.Clock);
        using var round = runtime.CreateRound(definition, true);
        round.Seed(CancellationToken.None);
        round.Trigger(CancellationToken.None);
        service.WrongOwnedId = true;
        Assert.Throws<InvalidDataException>(() => round.ObserveLegacy(TimeSpan.FromSeconds(1), CancellationToken.None));
        Assert.Throws<AggregateException>(() => round.DeleteOwnedRecords());
        service.WrongOwnedId = false;
        round.DeleteOwnedRecords();
        round.VerifyNoRemainingRecords();
    }

    /// <summary>SDK 同步呼叫期間 caller 取消，完成回應不可誤判成功；後續清理不受已取消 token 阻止。</summary>
    [Fact]
    public void SdkRound_CancellationDuringFinalCreateCannotPublishSeedSuccess()
    {
        using var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var definition = Definition();
        using var round = Runtime(f, Guid.NewGuid(), definition, Source()).CreateRound(definition, true);
        var calls = 0;
        f.Service.BeforeWrite = () => { if (++calls == 2) cancellation.Cancel(); };
        Assert.Throws<OperationCanceledException>(() => round.Seed(cancellation.Token));
        f.Service.BeforeWrite = null;
        Assert.Throws<InvalidDataException>(() => round.Trigger(CancellationToken.None));
        round.DeleteOwnedRecords();
        round.VerifyNoRemainingRecords();
    }

    /// <summary>owned 前向參照及權威 shared lookup 只套入副本；shared 只進別名表且不被查詢、建立或刪除。</summary>
    [Fact]
    public void SdkRound_ExplicitReferencesResolveAfterAllocationAndStayDetached()
    {
        using var f = new Fixture();
        var sharedId = Guid.NewGuid();
        var shared = new EntityReference("contact", sharedId) { Name = "Shared display" };
        var seedLinks = new Dictionary<string, string> { ["parentid"] = "record-2", ["contactid"] = "shared-contact" };
        var patchLinks = new Dictionary<string, string> { ["parentid"] = "shared-contact" };
        var source = Source() with
        {
            SharedReferences = new Dictionary<string, EntityReference> { ["shared-contact"] = shared },
            ReferenceAliases = new Dictionary<string, IReadOnlyDictionary<string, string>> { ["record-1"] = seedLinks },
            TriggerReferenceAliases = patchLinks
        };
        f.Service.Seed(new Entity("contact", sharedId) { ["name"] = "Unchanged" });
        var definition = Definition();
        using var round = Runtime(f, Guid.NewGuid(), definition, source).CreateRound(definition, true);
        shared.Id = Guid.NewGuid();
        seedLinks["parentid"] = "missing";
        patchLinks["parentid"] = "missing";
        round.Seed(CancellationToken.None);
        var first = f.Service.Rows.Single(row => row.Id.ToString("D") == round.Aliases.Single(pair => pair.Value == "record-1").Key);
        var otherId = Guid.Parse(round.Aliases.Single(pair => pair.Value == "record-2").Key);
        Assert.Equal(otherId, first.GetAttributeValue<EntityReference>("parentid").Id);
        Assert.Equal(sharedId, first.GetAttributeValue<EntityReference>("contactid").Id);
        Assert.Null(first.GetAttributeValue<EntityReference>("contactid").Name);
        Assert.Equal("shared-contact", round.Aliases[sharedId.ToString("D")]);
        Assert.Equal(2, round.LegacyContext.CreatedRecords.Count);
        round.Trigger(CancellationToken.None);
        Assert.Equal(sharedId, first.GetAttributeValue<EntityReference>("parentid").Id);
        Assert.False(source.Seeds[0].Record.Contains("parentid"));
        Assert.False(source.Trigger.Fields.Contains("parentid"));
        round.DeleteOwnedRecords();
        round.VerifyNoRemainingRecords();
        Assert.Equal("Unchanged", Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "contact")).GetAttributeValue<string>("name"));
        Assert.DoesNotContain(f.Service.Writes, write => write.EndsWith(":contact", StringComparison.Ordinal));
    }

    /// <summary>缺別名、空 GUID、同 GUID 異別名與 shared 當寫入目標，都必須在第一筆 I/O 前拒絕。</summary>
    /// <param name="fault">顯式參照清冊的畸形來源。</param>
    [Theory]
    [InlineData("missing-target")]
    [InlineData("missing-source")]
    [InlineData("duplicate-id")]
    [InlineData("empty-id")]
    [InlineData("duplicate-alias")]
    [InlineData("empty-alias")]
    [InlineData("shared-trigger")]
    [InlineData("shared-source")]
    [InlineData("bad-attribute")]
    [InlineData("primary-attribute")]
    public void SdkRound_InvalidReferenceGraphRejectedBeforeWrites(string fault)
    {
        using var f = new Fixture();
        var definition = Definition();
        var shared = new Dictionary<string, EntityReference> { ["shared"] = new("contact", Guid.NewGuid()) };
        var links = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        var source = Source();
        switch (fault)
        {
            case "missing-target": links["record-1"] = new Dictionary<string, string> { ["parentid"] = "missing" }; break;
            case "missing-source": links["missing"] = new Dictionary<string, string> { ["parentid"] = "record-1" }; break;
            case "duplicate-id": shared["other"] = new("account", shared["shared"].Id); break;
            case "empty-id": shared["shared"].Id = Guid.Empty; break;
            case "duplicate-alias": shared["record-1"] = new("contact", Guid.NewGuid()); break;
            case "empty-alias": shared[""] = new("contact", Guid.NewGuid()); break;
            case "shared-trigger": source = source with { Trigger = source.Trigger with { TargetAlias = "shared" } }; break;
            case "shared-source": links["shared"] = new Dictionary<string, string> { ["parentid"] = "record-1" }; break;
            case "bad-attribute": links["record-1"] = new Dictionary<string, string> { ["alias.field"] = "shared" }; break;
            case "primary-attribute": links["record-1"] = new Dictionary<string, string> { ["regionid"] = "shared" }; break;
        }
        source = source with { SharedReferences = shared, ReferenceAliases = links };
        Assert.Throws<InvalidDataException>(() => Runtime(f, Guid.NewGuid(), definition, source).CreateRound(definition, true));
        Assert.Empty(f.Service.Writes);
    }

    /// <summary>觸發寫入回應失敗時不可繼續觀察並宣稱成功；仍保留清冊供明確清理。</summary>
    [Fact]
    public void SdkRound_FailedTriggerCannotObserve()
    {
        using var f = new Fixture();
        var definition = Definition();
        var service = new PollingService(f.Service);
        var runtime = new ShadowSdkRuntime(service, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new Dictionary<ShadowScenarioDefinition, ShadowSdkScenario> { [definition] = Source() }, () => { }, f.Clock);
        using var round = runtime.CreateRound(definition, true);
        round.Seed(CancellationToken.None);
        f.Service.BeforeWrite = () => throw new InvalidOperationException("Injected");
        Assert.Throws<InvalidOperationException>(() => round.Trigger(CancellationToken.None));
        f.Service.BeforeWrite = null;
        Assert.Throws<InvalidDataException>(() => round.ObserveLegacy(TimeSpan.FromSeconds(1), CancellationToken.None));
        Assert.Throws<InvalidDataException>(() => round.Trigger(CancellationToken.None));
        round.DeleteOwnedRecords();
        round.VerifyNoRemainingRecords();
    }

    /// <summary>trigger 不接受替代鍵；否則精確 owned ID 的寫入邊界會同時帶入另一個身分來源。</summary>
    [Fact]
    public void SdkRound_TriggerAlternateKeyRejectedBeforeWrites()
    {
        using var f = new Fixture();
        var definition = Definition();
        var source = Source();
        source.Trigger.Fields.KeyAttributes["externalkey"] = "untrusted";
        Assert.Throws<InvalidDataException>(() => Runtime(f, Guid.NewGuid(), definition, source).CreateRound(definition, true));
        Assert.Empty(f.Service.Writes);
    }

    /// <summary>Create 目標延至最後才建立，清理仍須逆實際送出順序；刪除故障不能阻擋其他列，且可再試排空。</summary>
    [Fact]
    public void SdkRound_DeferredCreateCleanupReversesActualAttemptsAndRetries()
    {
        using var f = new Fixture();
        var definition = Definition();
        var source = Source();
        source = source with
        {
            Trigger = source.Trigger with { Message = "Create" },
            TokenFields = Array.Empty<ShadowSdkTokenField>()
        };
        var service = new PollingService(f.Service) { FailFirstDelete = true };
        var runtime = new ShadowSdkRuntime(service, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new Dictionary<ShadowScenarioDefinition, ShadowSdkScenario> { [definition] = source }, () => { }, f.Clock);
        using var round = runtime.CreateRound(definition, true);
        round.Seed(CancellationToken.None);
        round.Trigger(CancellationToken.None);
        var target = Guid.Parse(round.Aliases.Single(pair => pair.Value == "record-1").Key);
        Assert.Throws<AggregateException>(() => round.DeleteOwnedRecords());
        Assert.Equal(target, service.DeleteAttempts[0]);
        Assert.Equal(2, service.DeleteAttempts.Count);
        Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "region"));
        Assert.Throws<InvalidDataException>(() => round.Trigger(CancellationToken.None));
        round.DeleteOwnedRecords();
        round.VerifyNoRemainingRecords();
        Assert.DoesNotContain(f.Service.Rows, row => row.LogicalName == "region");
    }

    /// <summary>只由本測試核定 ID 與清單建立 runtime；沒有真實秘密或身分來源。</summary>
    private static ShadowSdkRuntime Runtime(Fixture f, Guid organization, ShadowScenarioDefinition definition, ShadowSdkScenario source) =>
        new(f.Service, organization, Guid.NewGuid(), Guid.NewGuid(),
            new Dictionary<ShadowScenarioDefinition, ShadowSdkScenario> { [definition] = source }, () => { }, f.Clock);

    /// <summary>安全期望只包含明確 score，姓名等敏感欄位不作明文輸出。</summary>
    private static ShadowScenarioDefinition Definition() => new(
        ShadowTraceParser.Parse("SMV-SHADOW|1|{\"end\":true,\"count\":0}"),
        new[] { new ShadowObservation("region", "record-1", Fields) }, new HashSet<string>());

    private static readonly string[] Fields = { "score" };
    private static readonly string[] PluginTypes = { "Synthetic.Plugin" };

    /// <summary>兩個同名合成記錄，只有第一個是 trigger 目標；外部 ID 永不放進 seed 範本。</summary>
    private static ShadowSdkScenario Source() => new(
        new[] {
            new ShadowSdkSeed("record-1", new Entity("region") { ["name"] = "Original Name", ["score"] = 1, ["new_token"] = "Synthetic secret" }),
            new ShadowSdkSeed("record-2", new Entity("region") { ["name"] = "Original Name", ["score"] = 1 })
        },
        new ShadowSdkTrigger("Update", "record-1", new Entity("region") { ["score"] = 2 }),
        new[] { Guid.NewGuid() }, new[] { Guid.NewGuid() }, PluginTypes,
        new[] { new ShadowSdkTokenField("record-1", "new_token") }, false)
        { PrimaryIdAttributes = new Dictionary<string, string> { ["region"] = "regionid" } };

    /// <summary>只攔截作業與追蹤 metadata，驗證產品查詢條件；其餘 CRUD 借用案例記憶體 SDK。</summary>
    private sealed class PollingService(MemoryOrganization inner) : IOrganizationService
    {
        internal int Polls;
        internal int ReadyAfter = 1;
        internal bool LoseCreateResponse;
        internal bool MutateCreate;
        internal bool WrongOwnedId;
        internal bool FailFirstDelete;
        internal readonly List<Guid> DeleteAttempts = new();
        internal string Fault = "";
        internal string Trace = "SMV-SHADOW|1|{\"end\":true,\"count\":0}";
        internal Guid RequestedStep;
        internal Guid RequestedTarget;
        internal string? RequestedType;
        private readonly Guid correlation = Guid.NewGuid();

        /// <summary>注入真建立後才失敗，保護回應不明時的 owner 清理契約。</summary>
        public Guid Create(Entity entity)
        {
            var id = inner.Create(entity);
            if (MutateCreate)
            {
                entity.Id = Guid.NewGuid();
                if (entity.GetAttributeValue<OptionSetValue>("choice") is { } choice) choice.Value = 99;
            }
            if (LoseCreateResponse) throw new InvalidOperationException("Response lost");
            return id;
        }
        /// <summary>借用案例的精確 ID 更新，無網路。</summary>
        public void Update(Entity entity) => inner.Update(entity);
        /// <summary>記錄精確 ID 的刪除順序；故障只影響第一筆，後續及重試仍借用案例 SDK 排空。</summary>
        public void Delete(string entityName, Guid id)
        {
            DeleteAttempts.Add(id);
            if (FailFirstDelete && DeleteAttempts.Count == 1) throw new InvalidOperationException("Injected delete failure");
            inner.Delete(entityName, id);
        }
        /// <summary>借用案例讀取，不改寫欄位。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => inner.Retrieve(entityName, id, columnSet);
        /// <summary>驗證 server-owned owning step、目標、correlation、型別及時間窗都實際出現在查詢。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var expression = Assert.IsType<QueryExpression>(query);
            if (expression.EntityName is not ("asyncoperation" or "plugintracelog"))
            {
                Assert.False(expression.ColumnSet.AllColumns);
                Assert.Single(expression.Criteria.Conditions);
                Assert.Equal("regionid", expression.Criteria.Conditions[0].AttributeName);
                var rows = inner.RetrieveMultiple(query);
                if (WrongOwnedId && rows.Entities.Count != 0) rows.Entities[0].Id = Guid.NewGuid();
                return rows;
            }
            Assert.Equal(2, expression.TopCount);
            Assert.Contains(expression.Criteria.Conditions, condition => condition.AttributeName == "createdon" &&
                condition.Operator == ConditionOperator.OnOrAfter && condition.Values[0] is DateTime);
            if (expression.EntityName == "asyncoperation")
            {
                Polls++;
                RequestedStep = Assert.IsType<Guid>(expression.Criteria.Conditions.Single(condition => condition.AttributeName == "owningextensionid").Values[0]);
                RequestedTarget = Assert.IsType<Guid>(expression.Criteria.Conditions.Single(condition => condition.AttributeName == "regardingobjectid").Values[0]);
                if (Polls < ReadyAfter) return new EntityCollection();
                var result = new EntityCollection();
                result.Entities.Add(new Entity("asyncoperation", Guid.NewGuid())
                {
                    ["correlationid"] = correlation, ["statecode"] = new OptionSetValue(3),
                    ["statuscode"] = new OptionSetValue(Fault == "failed-operation" ? 31 : 30)
                });
                if (Fault == "duplicate-operation") result.Entities.Add(result.Entities[0]);
                return result;
            }
            Assert.Equal(correlation, expression.Criteria.Conditions.Single(condition => condition.AttributeName == "correlationid").Values[0]);
            RequestedType = Assert.IsType<string>(expression.Criteria.Conditions.Single(condition => condition.AttributeName == "typename").Values[0]);
            var traces = new EntityCollection();
            traces.Entities.Add(new Entity("plugintracelog", Guid.NewGuid())
            {
                ["messageblock"] = Fault == "missing-end" ? "SMV-SHADOW|1|{\"overflow\":true,\"total\":1}" : Trace
            });
            if (Fault == "duplicate-trace") traces.Entities.Add(traces.Entities[0]);
            return traces;
        }
        /// <summary>其他訊息由既有替身白名單處理。</summary>
        public OrganizationResponse Execute(OrganizationRequest request) => inner.Execute(request);
        /// <summary>不提供本工作包未使用的關聯能力。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new NotSupportedException();
        /// <summary>不提供本工作包未使用的解除關聯能力。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new NotSupportedException();
    }
}

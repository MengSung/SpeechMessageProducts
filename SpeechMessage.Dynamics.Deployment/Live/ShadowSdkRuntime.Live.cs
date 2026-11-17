/*
檔案整體說明
目的與定位：既有 Shadow SDK owner 的 Live 能力分部，沿同一 seed／讀回／清理實作，絕不偽裝舊輪。
主要流程：只配置新步驟 round；同步請求完成即已執行，非同步仍按本次 owning step／target 等待。
待辦只查核定測試教會及本次時間窗，payload 內正式 GUID 必須精確引用本輪 owned 記錄，
才納入清冊；不依名稱、相近時間或類型就刪除。預期與實際待辦同樣經安全投影比較。
資源與隔離：借用命令 SDK，不自建連線；原始 payload 僅在查詢方法短期存在，清冊只保留
正式 ID。Processing 待辦拒絕刪除並保留父列，避免工作者仍執行時消失資料。所有輪次同步獨占。
失敗與驗證：取消後清理重查已建立待辦，殘留必須為零；原 Shadow 介面及舊輪拒絕行為保持。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Live;
namespace SpeechMessage.Dynamics.Deployment.Shadow;

/// <summary>新待辦的固定期望欄位，GUID 只允明確 ${alias} 或 ${alias:N}，不解析任意表示式。</summary>
internal sealed record LiveTaskExpectation(string Alias, JsonElement Fields);

/// <summary>單命令 Live 能力，仍使用既有 SDK 及正式 ID 清冊。</summary>
internal sealed partial class ShadowSdkRuntime
{
    /// <summary>明確配置新 Live 輪；不用 VerifyDisabled 或任何切換委派。</summary>
    internal ILiveCheckRound CreateLiveRound(ShadowSdkPreparedScenario scenario, LiveActivityTemplate? activity = null)
    {
        var round = new Round(this, scenario.Definition, scenario.Source, false)
        { liveRound = true, liveDefinition = scenario.Definition, liveActivity = activity is null ? null : new LiveActivityProbe(service, activity),
          liveExpectedTasks = scenario.Source.LiveTasks, liveMetadata = scenario.Source.LiveMetadata };
        try { round.ValidateLiveTasks(); return round; }
        catch { round.Dispose(); throw; }
    }

    /// <summary>單輪 Live 介面接線，不新增第二套投影或種子刪除邏輯。</summary>
    private sealed partial class Round : ILiveCheckRound
    {
        internal bool liveRound;
        private DateTime liveStarted;
        internal IReadOnlyList<LiveTaskExpectation> liveExpectedTasks = Array.Empty<LiveTaskExpectation>();
        internal ShadowSdkMetadata? liveMetadata;
        internal LiveActivityProbe? liveActivity;
        internal ShadowScenarioDefinition? liveDefinition;
        private readonly HashSet<Guid> liveTasks = new();
        /// <summary>等待本輪精確作業後讀回；待辦預期亦參與同一 comparer，不以計數相等假稱內容正確。</summary>
        LiveCheckObservation ILiveCheckRound.Observe(TimeSpan maximumWait, CancellationToken cancellationToken)
        {
            EnsureActive();
            if (!liveRound || liveDefinition is null || cleanupStarted) throw InvalidRuntime();
            Poll(maximumWait, () => liveActivity?.Completed() ?? CompletedOperations(live: true) is not null, cancellationToken);
            var actual = ReadObserved(cancellationToken);
            var tasks = ReadLiveTasks();
            if (liveDefinition.Expected.Intents.Any(intent => intent.Operation == "Push") &&
                tasks.Any(row => row.GetAttributeValue<OptionSetValue>("new_type")?.Value == 100000003))
            {
                Poll(maximumWait, () =>
                {
                    tasks = ReadLiveTasks();
                    if (tasks.Any(row => row.GetAttributeValue<OptionSetValue>("new_taskstatus")?.Value == 100000003)) throw InvalidRuntime();
                    return tasks.All(row => row.GetAttributeValue<OptionSetValue>("new_taskstatus")?.Value == 100000002);
                }, cancellationToken);
            }
            var expected = ShadowProjection.ApplyRecordedWrites(new ShadowSnapshot(true,
                Array.AsReadOnly(ShadowProjection.Prepare(Initial, Aliases).Values.ToArray())), liveDefinition.Expected);
            var expectedTasks = liveExpectedTasks.Select(ExpectedTask).ToArray();
            if (expectedTasks.Length != tasks.Length) actual = new(false, Array.Empty<ShadowRow>());
            else if (tasks.Length != 0)
            {
                // 每案只有一個精確事件待辦才可直接繫結；多筆須由情境明列 related，不能按順序猜身分。
                if (tasks.Length != 1) throw InvalidRuntime();
                BindRelatedAlias(tasks[0].Id.ToString("D"), expectedTasks[0].Id);
                var projected = ShadowSdkProjection.Project(tasks[0]);
                var taskObserve = new ShadowObservation("new_outboundtask", expectedTasks[0].Id,
                    expectedTasks[0].Fields.EnumerateObject().Select(field => field.Name).ToArray());
                var compared = ShadowComparer.CompareFinalStates(new(true, expectedTasks), new(true, new[] { projected }),
                    new[] { taskObserve }, Aliases, liveDefinition.VolatileFields);
                if (!compared.IsEqual) actual = new(false, Array.Empty<ShadowRow>());
            }
            return new(actual, tasks.Length, Aliases) { Expected = expected };
        }
        /// <summary>第一筆 seed 前驗模板可完整轉成正式欄值，不在送達後才發現錯誤期望。</summary>
        internal void ValidateLiveTasks() { foreach (var expectation in liveExpectedTasks) _ = ExpectedTask(expectation); }

        /// <summary>明確 GUID 代號先解析，再沿同一 metadata 值轉換及 recorder 投影；不變更期望欄值或忽略差異。</summary>
        private ShadowRow ExpectedTask(LiveTaskExpectation expectation)
        {
            if (liveMetadata is null) throw InvalidRuntime();
            EntityReference Resolve(string alias, string entity)
            {
                var matches = aliases.Where(pair => pair.Value == alias).ToArray();
                if (matches.Length != 1) throw InvalidRuntime();
                var id = Guid.Parse(matches[0].Key);
                var actual = records.FirstOrDefault(item => item.Record.Id == id).Record;
                if (actual is not null && actual.LogicalName != entity) throw InvalidRuntime();
                if (actual is null && !(entity == "account" && id == runtime.testChurchId || entity == "contact" && id == runtime.testContactId)) throw InvalidRuntime();
                return new(entity, id);
            }
            var node = JsonNode.Parse(expectation.Fields.GetRawText()) as JsonObject ?? throw InvalidRuntime();
            foreach (var field in node.ToArray())
            {
                if (field.Value is not JsonValue value || !value.TryGetValue<string>(out var text)) continue;
                foreach (var pair in aliases)
                {
                    var id = Guid.Parse(pair.Key);
                    text = text.Replace("${" + pair.Value + "}", id.ToString("D"), StringComparison.Ordinal)
                        .Replace("${" + pair.Value + ":N}", id.ToString("N"), StringComparison.Ordinal);
                }
                if (text.Contains("${", StringComparison.Ordinal)) throw InvalidRuntime();
                node[field.Key] = text;
            }
            var entity = liveMetadata.ConvertFields("new_outboundtask", JsonSerializer.SerializeToElement(node), true, Resolve);
            entity.Id = Guid.NewGuid();
            var row = ShadowSdkProjection.Project(entity);
            return row with { Id = expectation.Alias };
        }

        /// <summary>待辦子列先清除，再交原 owner 清種子；工作者尚認領時拒絕破壞其資料。</summary>
        void ILiveCheckRound.Cleanup(CancellationToken cancellationToken)
        {
            EnsureActive();
            liveActivity?.Cleanup();
            if (seedStarted)
            {
                var rows = ReadLiveTasks();
                foreach (var row in rows)
                {
                    if (row.GetAttributeValue<OptionSetValue>("new_taskstatus")?.Value == 100000001) throw InvalidRuntime();
                    runtime.service.Delete("new_outboundtask", row.Id);
                }
            }
            DeleteOwnedRecords();
        }
        /// <summary>逐個正式 ID 與精確 payload 範圍核對，不能只查 seed 而遺留待辦。</summary>
        void ILiveCheckRound.VerifyNoRemaining(CancellationToken cancellationToken)
        {
            liveActivity?.VerifyNoRemaining();
            if (seedStarted && ReadLiveTasks().Length != 0) throw InvalidRuntime();
            foreach (var id in liveTasks)
            {
                var query = new QueryExpression("new_outboundtask") { ColumnSet = new ColumnSet(false), TopCount = 2 };
                query.Criteria.AddCondition("new_outboundtaskid", ConditionOperator.Equal, id);
                if (ReadBounded(query, 1).Length != 0) throw InvalidRuntime();
            }
            VerifyNoRemainingRecords();
        }
        /// <summary>只讀測試教會本輪時間窗；payload 有界且精確 owned GUID 才納管，未知內容不輸出。</summary>
        private Entity[] ReadLiveTasks()
        {
            if (!seedStarted) return Array.Empty<Entity>();
            var query = new QueryExpression("new_outboundtask")
            {
                ColumnSet = new ColumnSet("new_type", "new_taskstatus", "new_accountid", "new_payload", "new_idempotencykey"), TopCount = 65
            };
            query.Criteria.AddCondition("new_accountid", ConditionOperator.Equal, runtime.testChurchId);
            query.Criteria.AddCondition("createdon", ConditionOperator.OnOrAfter, liveStarted);
            var candidates = ReadBounded(query, 64);
            var result = new List<Entity>();
            var owned = attempted.Select(item => item.Id).ToHashSet();
            foreach (var row in candidates)
            {
                if (row.GetAttributeValue<EntityReference>("new_accountid") is not { LogicalName: "account" } church || church.Id != runtime.testChurchId)
                    throw InvalidRuntime();
                var payload = row.GetAttributeValue<string>("new_payload");
                if (string.IsNullOrEmpty(payload) || payload.Length > 4000) throw InvalidRuntime();
                using var json = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 8 });
                Schema.SchemaFiles.RejectDuplicateProperties(json.RootElement);
                if (json.RootElement.ValueKind != JsonValueKind.Object) throw InvalidRuntime();
                if (!json.RootElement.EnumerateObject().Any(field => field.Value.ValueKind == JsonValueKind.String &&
                    Guid.TryParse(field.Value.GetString(), out var id) && owned.Contains(id))) continue;
                liveTasks.Add(row.Id);
                result.Add(row);
            }
            return result.ToArray();
        }
    }
}

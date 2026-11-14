/*
 * 檔案整體說明
 *
 * 目的與定位：影子執行器的命令區域 SDK 資料 owner，將核定情境的 seed、trigger、
 * observe 與追蹤查詢限制在測試組織及本輪 ID。SDK 連線及寫鎖仍由 CLI 擁有。
 * 主要功能與流程：從已核定別名表建立獨立記錄，使用共用 recorder 的安全投影；
 * trigger 後以精確 owning step、regarding ID、correlation 與開始時間查系統作業，
 * 新輪只接受唯一完整追蹤。最後逐筆刪除並唯讀核對自建記錄，清除 owner 參考。
 * 使用邊界與維護：呼叫端須先驗證實體／欄位 metadata、測試教會與會友權威關聯、
 * 盤點 token 來源及模組切換設定。本層不從顯示名稱推斷授權，也不解析任意查詢。
 * 顯式 alias 先配置全部 owned ID，再以 lookup 連結 owned／shared；shared 的授權與
 * metadata 由 caller 負責，本層不查 shared 原列、不寫入或清理 shared。每個 round
 * 只支援單一同步呼叫者；SDK 輸入輸出均深複製，禁止部分 Seed 失敗後觸發。
 * 原始業務資料只存活一輪；追蹤最多六十秒、最多二秒一次且回應有界，取消不略過
 * 清理。所有 SDK 例外由外層共用告警處理，本檔不輸出 token、原始 trace 或個資。
 */
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Plugins.Infrastructure;

namespace SpeechMessage.Dynamics.Deployment.Shadow;

/// <summary>已核對 metadata 的 seed 範本；欄位為 SDK 值，owner 在 Seed 前深層複製。</summary>
internal sealed record ShadowSdkSeed(string Alias, Entity Record);

/// <summary>核定的 CRUD 觸發；只可操作本輪自建列，欄位不得包含真實秘密或個資。</summary>
internal sealed record ShadowSdkTrigger(string Message, string TargetAlias, Entity Fields);

/// <summary>盤點核定的 token 欄位；只指向自建 seed，值由 SwitchModule 同程序注入。</summary>
internal sealed record ShadowSdkTokenField(string Alias, string Attribute);

/// <summary>
/// 一個已解析且完成 metadata 核對的執行情境。Seed 範本的 Guid.Empty 表示本輪新建；
/// references 中的非空 GUID 必須由呼叫端核對為核定測試範圍，不能把外部輸入當授權。
/// </summary>
internal sealed record ShadowSdkScenario(IReadOnlyList<ShadowSdkSeed> Seeds, ShadowSdkTrigger Trigger,
    IReadOnlyList<Guid> LegacyStepIds, IReadOnlyList<Guid> ShadowStepIds,
    IReadOnlyList<string> ShadowTypes, IReadOnlyList<ShadowSdkTokenField> TokenFields,
    bool HasUnconditionalHardcodedToken)
{
    /// <summary>由即時 metadata 或核定結構快照提供的正式主鍵，缺少即拒絕，不猜 logicalname+id。</summary>
    public IReadOnlyDictionary<string, string> PrimaryIdAttributes { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>種子別名到欄位及目標別名的顯式 lookup；所有本輪 GUID 配置完成後套入獨立副本，不按名稱推斷。</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ReferenceAliases { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

    /// <summary>
    /// CLI 已以權威 ID 核對的外部 lookup；本層只複製 logical name／GUID，不讀寫或刪除該列。
    /// metadata、教會／會友歸屬與授權由 caller 負責；別名不可和 owned seed 共用，資料只存活本輪。
    /// </summary>
    public IReadOnlyDictionary<string, EntityReference> SharedReferences { get; init; } =
        new Dictionary<string, EntityReference>(StringComparer.Ordinal);

    /// <summary>trigger patch 欄位到 owned／shared 別名的顯式 lookup，和 seed 參照分開且不改原始 patch。</summary>
    public IReadOnlyDictionary<string, string> TriggerReferenceAliases { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>測試組織的命令區域 runtime；所有資源均由同步 Run 與 round owner 限定。</summary>
internal sealed class ShadowSdkRuntime : IShadowScenarioRuntime
{
    private readonly IOrganizationService service;
    private readonly Guid organizationId;
    private readonly Guid testChurchId;
    private readonly Guid testContactId;
    private readonly IReadOnlyDictionary<ShadowScenarioDefinition, ShadowSdkScenario> scenarios;
    private readonly Action verifyDisabled;
    private readonly TimeProvider clock;

    /// <summary>借用已授權 SDK 及核定設定；建構不寫入，不保留跨命令狀態。</summary>
    internal ShadowSdkRuntime(IOrganizationService service, Guid organizationId, Guid testChurchId,
        Guid testContactId, IReadOnlyDictionary<ShadowScenarioDefinition, ShadowSdkScenario> scenarios,
        Action verifyDisabled, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(scenarios);
        ArgumentNullException.ThrowIfNull(verifyDisabled);
        ArgumentNullException.ThrowIfNull(clock);
        if (organizationId == Guid.Empty || testChurchId == Guid.Empty || testContactId == Guid.Empty ||
            scenarios.Count is < 1 or > 6) throw InvalidRuntime();
        this.service = service;
        this.organizationId = organizationId;
        this.testChurchId = testChurchId;
        this.testContactId = testContactId;
        this.scenarios = scenarios;
        this.verifyDisabled = verifyDisabled;
        this.clock = clock;
    }

    /// <summary>讀取組織的追蹤等級，缺值或非已定義選項必須拒絕。</summary>
    public int ReadTraceSetting()
    {
        var row = service.Retrieve("organization", organizationId, new ColumnSet("plugintracelogsetting"));
        if (row.Id != organizationId || row.LogicalName != "organization" ||
            row.GetAttributeValue<OptionSetValue>("plugintracelogsetting") is not { Value: >= 0 and <= 2 } setting)
            throw InvalidRuntime();
        return setting.Value;
    }
    /// <summary>只寫核定組織的已定義追蹤選項。</summary>
    public void WriteTraceSetting(int value)
    {
        if (value is < 0 or > 2) throw InvalidRuntime();
        service.Update(new Entity("organization", organizationId) { ["plugintracelogsetting"] = new OptionSetValue(value) });
    }
    /// <summary>配置獨立 round owner；seed 的實際 SDK I/O 延遲到 Seed。</summary>
    public IShadowScenarioRound CreateRound(ShadowScenarioDefinition scenario, bool legacy) =>
        scenarios.TryGetValue(scenario, out var source) ? new Round(this, scenario, source, legacy) : throw InvalidRuntime();
    /// <summary>委派命令的唯讀 metadata 核對，不能以快取狀態代替當下狀態。</summary>
    public void VerifyDisabled() => verifyDisabled();

    /// <summary>固定錯誤不含實際 ID、原始 SDK 回應、欄位值或追蹤文字。</summary>
    private static InvalidDataException InvalidRuntime() => new("Shadow SDK runtime safety precondition failed.");

    /// <summary>一輪 seed 與查詢 owner；預先配置 ID 並在送出 Create 前登錄，涵蓋回應不明的失敗。</summary>
    private sealed class Round : IShadowScenarioRound
    {
        private readonly ShadowSdkRuntime runtime;
        private readonly ShadowObservation[] observations;
        private readonly bool legacy;
        private readonly List<(string Alias, Entity Record)> records;
        private readonly Dictionary<string, string> aliases = new(StringComparer.Ordinal);
        private readonly HashSet<(string Entity, Guid Id)> attempted = new();
        // Create trigger 的目標會延後建立，清理順序必須依實際送出順序，不能依 seed 宣告位置。
        private readonly List<(string Entity, Guid Id)> createOrder = new();
        private readonly ShadowSdkTrigger trigger;
        private readonly Guid[] stepIds;
        private readonly string[] types;
        private readonly ShadowSdkTokenField[] tokenFields;
        private readonly bool hardcoded;
        private readonly Dictionary<string, string> primaryIds;
        private bool seedStarted;
        private bool seeded;
        private bool cleanupStarted;
        private bool triggerAttempted;
        private bool triggered;
        private bool disposed;
        private DateTime triggerStarted;

        /// <summary>
        /// 先驗完整候選、唯一別名及範本 ID，再深層複製所有 SDK 值；任何錯誤在首筆寫入前拒絕。
        /// 每輪最多六十四列，單列仍沿用 recorder 的欄位／節點／容量限制。
        /// </summary>
        internal Round(ShadowSdkRuntime runtime, ShadowScenarioDefinition definition, ShadowSdkScenario source, bool legacy)
        {
            this.runtime = runtime;
            if (definition.Observe is null || definition.Observe.Count > 64 ||
                definition.Observe.Any(item => item is null || item.Fields is null || item.Fields.Count > 128))
                throw InvalidRuntime();
            observations = definition.Observe.Select(item => item with { Fields = item.Fields.ToArray() }).ToArray();
            this.legacy = legacy;
            if (source.Seeds is null || source.Seeds.Count is < 1 or > 64 || source.Trigger is null ||
                source.Trigger.Message is not ("Create" or "Update" or "Delete") ||
                source.LegacyStepIds is null || source.ShadowStepIds is null || source.ShadowTypes is null ||
                source.TokenFields is null || source.TokenFields.Count > 32) throw InvalidRuntime();
            if (source.SharedReferences is null || source.SharedReferences.Count > 64 ||
                source.ReferenceAliases is null || source.ReferenceAliases.Count > 64 ||
                source.TriggerReferenceAliases is null || source.TriggerReferenceAliases.Count > 128)
                throw InvalidRuntime();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var references = new Dictionary<string, EntityReference>(StringComparer.Ordinal);
            var identities = new HashSet<Guid>();
            foreach (var pair in source.SharedReferences)
            {
                if (!ShadowProjection.ValidIdentity(pair.Key) || !names.Add(pair.Key) || pair.Value is null ||
                    pair.Value.Id == Guid.Empty || !identities.Add(pair.Value.Id) || !ValidAttribute(pair.Value.LogicalName) ||
                    pair.Value.KeyAttributes.Count != 0) throw InvalidRuntime();
                references.Add(pair.Key, new EntityReference(pair.Value.LogicalName, pair.Value.Id));
                aliases.Add(pair.Value.Id.ToString("D"), pair.Key);
            }
            records = new List<(string, Entity)>(source.Seeds.Count);
            foreach (var seed in source.Seeds)
            {
                if (seed is null || !ShadowProjection.ValidIdentity(seed.Alias) || !names.Add(seed.Alias) ||
                    seed.Record is null || seed.Record.Id != Guid.Empty) throw InvalidRuntime();
                var record = RecordingOrganizationService.CloneEntity(seed.Record, allowAliases: false);
                record.Id = Guid.NewGuid();
                if (!identities.Add(record.Id) || record.KeyAttributes.Count != 0) throw InvalidRuntime();
                references.Add(seed.Alias, new EntityReference(record.LogicalName, record.Id));
                // 只改可寫的顯示欄位，避免設定 fullname 等計算欄位；情境 metadata 仍由 caller 先驗。
                foreach (var field in new[] { "name", "subject" })
                    if (record.Attributes.TryGetValue(field, out var value) && value is string text)
                        record[field] = "SMV-TEST-" + text;
                records.Add((seed.Alias, record));
                aliases.Add(record.Id.ToString("D"), seed.Alias);
            }
            trigger = source.Trigger with { Fields = RecordingOrganizationService.CloneEntity(source.Trigger.Fields, allowAliases: false) };
            var target = records.SingleOrDefault(item => item.Alias == trigger.TargetAlias).Record;
            if (target is null || target.LogicalName != trigger.Fields.LogicalName ||
                trigger.Fields.Id != Guid.Empty || trigger.Fields.KeyAttributes.Count != 0) throw InvalidRuntime();
            foreach (var observation in observations)
                if (!records.Any(record => record.Alias == observation.Id && record.Record.LogicalName == observation.Entity))
                    throw InvalidRuntime();
            stepIds = (legacy ? source.LegacyStepIds : source.ShadowStepIds).ToArray();
            types = source.ShadowTypes.ToArray();
            if (stepIds.Length is < 1 or > 128 || stepIds.Any(id => id == Guid.Empty) ||
                stepIds.Distinct().Count() != stepIds.Length || types.Length is < 1 or > 128 ||
                types.Any(type => string.IsNullOrWhiteSpace(type) || type.Length > 512) ||
                types.Distinct(StringComparer.Ordinal).Count() != types.Length) throw InvalidRuntime();
            tokenFields = source.TokenFields.ToArray();
            foreach (var token in tokenFields)
                if (token is null || !records.Any(item => item.Alias == token.Alias) || string.IsNullOrWhiteSpace(token.Attribute) ||
                    token.Attribute.Length > 128 || token.Attribute.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_') ||
                    (trigger.Message == "Create" && token.Alias == trigger.TargetAlias)) throw InvalidRuntime();
            hardcoded = source.HasUnconditionalHardcodedToken;
            primaryIds = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var record in records)
            {
                if (source.PrimaryIdAttributes is null ||
                    !source.PrimaryIdAttributes.TryGetValue(record.Record.LogicalName, out var primary) ||
                    string.IsNullOrWhiteSpace(primary) || primary.Length > 128 ||
                    primary.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_') ||
                    record.Record.Attributes.ContainsKey(primary) ||
                    (record.Alias == trigger.TargetAlias && trigger.Fields.Attributes.ContainsKey(primary)))
                    throw InvalidRuntime();
                primaryIds[record.Record.LogicalName] = primary;
            }
            foreach (var pair in source.ReferenceAliases)
            {
                var record = records.SingleOrDefault(item => item.Alias == pair.Key).Record;
                if (record is null) throw InvalidRuntime();
                ApplyReferences(record, pair.Value, references);
            }
            ApplyReferences(trigger.Fields, source.TriggerReferenceAliases, references);
        }

        /// <summary>
        /// 在所有 GUID 配置後才將明確別名轉成 lookup；來源僅能是 owned 候選，目標可為 shared。
        /// 主鍵、未知目標、畸形欄位及超量均在第一筆 SDK 寫入前拒絕；每欄獨占 reference 副本。
        /// </summary>
        private void ApplyReferences(Entity candidate, IReadOnlyDictionary<string, string> links,
            Dictionary<string, EntityReference> references)
        {
            if (links is null || links.Count > 128) throw InvalidRuntime();
            var attributes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in links)
            {
                if (!ValidAttribute(pair.Key) || !attributes.Add(pair.Key) ||
                    pair.Key == primaryIds[candidate.LogicalName] ||
                    !ShadowProjection.ValidIdentity(pair.Value) || !references.TryGetValue(pair.Value, out var target))
                    throw InvalidRuntime();
                candidate[pair.Key] = new EntityReference(target.LogicalName, target.Id);
            }
            // 套入 lookup 仍必須符合 recorder 原有的單列欄位／節點／容量上限。
            _ = RecordingOrganizationService.CloneEntity(candidate, allowAliases: false);
        }

        /// <summary>只接受實際 schema 識別字，禁止 alias.field 或空白被當成可寫 lookup。</summary>
        private static bool ValidAttribute(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
            value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

        /// <summary>
        /// 依 caller 核定的相依順序建立 seed，Create 目標延後；每次送出前登錄 GUID。
        /// 同步 SDK 無法中斷已送出呼叫，回應後再驗 caller 取消；整批成功才允許 trigger，失敗僅能清理。
        /// </summary>
        public void Seed(CancellationToken cancellationToken)
        {
            EnsureActive();
            if (seedStarted || cleanupStarted) throw InvalidRuntime();
            seedStarted = true;
            foreach (var item in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (trigger.Message == "Create" && item.Alias == trigger.TargetAlias) continue;
                CreateOwned(item.Record);
                cancellationToken.ThrowIfCancellationRequested();
            }
            cancellationToken.ThrowIfCancellationRequested();
            seeded = true;
        }

        /// <summary>建立規則前的初始候選；trigger patch 先套到安全副本，不讀已被舊規則改過的值。</summary>
        public ShadowSnapshot Initial
        {
            get
            {
                EnsureActive();
                var rows = new List<ShadowRow>();
                foreach (var item in records)
                {
                    if (trigger.Message == "Delete" && item.Alias == trigger.TargetAlias) continue;
                    var record = RecordingOrganizationService.CloneEntity(item.Record, allowAliases: false);
                    if (item.Alias == trigger.TargetAlias)
                        foreach (var pair in trigger.Fields.Attributes) record[pair.Key] = pair.Value;
                    rows.Add(ShadowSdkProjection.Project(record));
                }
                return new ShadowSnapshot(true, rows.AsReadOnly());
            }
        }

        /// <summary>回傳不可變的顯式映射副本，不讓 caller 修改 owner 的 ID 清冊。</summary>
        public IReadOnlyDictionary<string, string> Aliases
        {
            get { EnsureActive(); return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(aliases, StringComparer.Ordinal)); }
        }

        /// <summary>僅以本輪已送出 Create 的清冊建立 D5-18 範圍，token 值不在本物件。</summary>
        public LegacyRoundContext LegacyContext
        {
            get
            {
                EnsureActive();
                return new LegacyRoundContext(runtime.testChurchId, runtime.testContactId,
                    new HashSet<(string, Guid)>(attempted), tokenFields.Select(token =>
                    {
                        var record = records.Single(item => item.Alias == token.Alias).Record;
                        return new LegacyTokenTarget(record.LogicalName, record.Id, token.Attribute);
                    }).ToArray(), hardcoded);
            }
        }

        /// <summary>
        /// 一次送出核定 CRUD，開始時間先記錄；只有成功回應且 caller 未取消才允許觀察。
        /// 回應不明不重試 trigger，SDK 失敗仍由外層 owner 依清冊清理，避免重複觸發或假成功。
        /// </summary>
        public void Trigger(CancellationToken cancellationToken)
        {
            EnsureActive();
            if (!seeded || triggerAttempted || cleanupStarted) throw InvalidRuntime();
            cancellationToken.ThrowIfCancellationRequested();
            triggerAttempted = true;
            triggerStarted = runtime.clock.GetUtcNow().UtcDateTime;
            var target = records.Single(item => item.Alias == trigger.TargetAlias).Record;
            var patch = RecordingOrganizationService.CloneEntity(trigger.Fields, allowAliases: false);
            patch.Id = target.Id;
            if (trigger.Message == "Update") runtime.service.Update(patch);
            else if (trigger.Message == "Delete") runtime.service.Delete(target.LogicalName, target.Id);
            else
            {
                var candidate = RecordingOrganizationService.CloneEntity(target, allowAliases: false);
                foreach (var field in patch.Attributes) candidate[field.Key] = field.Value;
                CreateOwned(candidate);
            }
            cancellationToken.ThrowIfCancellationRequested();
            triggered = true;
        }

        /// <summary>等所有核定舊步驟的該次作業完成後才讀回指定欄位；失敗／逾時不當作空結果。</summary>
        public ShadowSnapshot ObserveLegacy(TimeSpan maximumWait, CancellationToken cancellationToken)
        {
            EnsureActive();
            if (!legacy || cleanupStarted) throw InvalidRuntime();
            Poll(maximumWait, () => CompletedOperations() is not null, cancellationToken);
            var rows = new List<ShadowRow>();
            foreach (var observation in observations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var record = records.Single(item => item.Alias == observation.Id).Record;
                var row = FindOwned(record.LogicalName, record.Id, new ColumnSet(observation.Fields.ToArray()));
                cancellationToken.ThrowIfCancellationRequested();
                if (row is not null) rows.Add(ShadowSdkProjection.Project(row));
            }
            return new ShadowSnapshot(true, rows.AsReadOnly());
        }

        /// <summary>
        /// 只取已完成且精確 owning step／目標／時間窗作業的 correlation，逐型別取唯一追蹤。
        /// 原始 messageblock 僅在方法內處理，抽出 SMV-SHADOW 行後限制八千字元並由 parser 驗完整。
        /// 同型別多次執行或多個 step 的 trace 尚不合併，歧義明確拒絕，避免序號覆寫。
        /// </summary>
        public string ReadShadowTrace(TimeSpan maximumWait, CancellationToken cancellationToken)
        {
            EnsureActive();
            if (legacy || cleanupStarted || stepIds.Length != 1 || types.Length != 1) throw InvalidRuntime();
            string? result = null;
            Poll(maximumWait, () =>
            {
                var operations = CompletedOperations();
                if (operations is null) return false;
                var candidates = new List<string>();
                foreach (var operation in operations)
                {
                    var correlation = operation.GetAttributeValue<Guid>("correlationid");
                    if (correlation == Guid.Empty) throw InvalidRuntime();
                    foreach (var type in types)
                    {
                        var query = new QueryExpression("plugintracelog") { ColumnSet = new ColumnSet("messageblock"), TopCount = 2 };
                        query.Criteria.AddCondition("correlationid", ConditionOperator.Equal, correlation);
                        query.Criteria.AddCondition("typename", ConditionOperator.Equal, type);
                        query.Criteria.AddCondition("createdon", ConditionOperator.OnOrAfter, triggerStarted);
                        var rows = ReadBounded(query, 1);
                        if (rows.Length == 0) continue;
                        var text = rows[0].GetAttributeValue<string>("messageblock");
                        if (text is null || text.Length > 32_768) throw InvalidRuntime();
                        var safe = new StringBuilder();
                        using var reader = new StringReader(text);
                        while (reader.ReadLine() is { } line)
                            if (line.StartsWith("SMV-SHADOW|1|", StringComparison.Ordinal))
                            {
                                if (line.Length + safe.Length + 2 > 8_000) throw InvalidRuntime();
                                safe.Append(line).Append("\r\n");
                            }
                        if (safe.Length == 0) continue;
                        _ = ShadowTraceParser.Parse(safe.ToString());
                        candidates.Add(safe.ToString());
                    }
                }
                if (candidates.Count > 1) throw InvalidRuntime();
                if (candidates.Count == 0) return false;
                result = candidates[0];
                return true;
            }, cancellationToken);
            return result ?? throw InvalidRuntime();
        }

        /// <summary>逆建立順序刪除，每列都獨立嘗試；已不存在允許重試，但任一查詢失敗保留。</summary>
        public void DeleteOwnedRecords()
        {
            EnsureActive();
            cleanupStarted = true;
            var failures = new List<Exception>();
            foreach (var key in createOrder.AsEnumerable().Reverse())
            {
                try
                {
                    if (FindOwned(key.Entity, key.Id, new ColumnSet(false)) is not null)
                        runtime.service.Delete(key.Entity, key.Id);
                }
                catch (Exception failure) { failures.Add(failure); }
            }
            if (failures.Count != 0) throw new AggregateException("Shadow record cleanup failed.", failures);
        }

        /// <summary>用本輪精確 ID 查詢殘留；不查或刪除相同名稱的其他業務資料。</summary>
        public void VerifyNoRemainingRecords()
        {
            EnsureActive();
            foreach (var item in attempted)
                if (FindOwned(item.Entity, item.Id, new ColumnSet(false)) is not null) throw InvalidRuntime();
        }

        /// <summary>清除 operation-local 原始欄位與 ID 參考；遠端刪除仍由明確收尾方法負責。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var item in records) { item.Record.Attributes.Clear(); item.Record.KeyAttributes.Clear(); }
            records.Clear();
            aliases.Clear();
            attempted.Clear();
            createOrder.Clear();
            trigger.Fields.Attributes.Clear();
            trigger.Fields.KeyAttributes.Clear();
            Array.Clear(observations);
            Array.Clear(tokenFields);
            Array.Clear(stepIds);
            Array.Clear(types);
            primaryIds.Clear();
        }

        /// <summary>預先登錄已知 ID，確保「伺服器建立但回應失敗」仍能以該 ID 查回清理。</summary>
        private void CreateOwned(Entity record)
        {
            if (!attempted.Add((record.LogicalName, record.Id))) throw InvalidRuntime();
            createOrder.Add((record.LogicalName, record.Id));
            // SDK 可保留或改寫傳入物件；只能取得 detached 副本，清冊身分與原始值保持 owner 獨占。
            var id = record.Id;
            if (runtime.service.Create(RecordingOrganizationService.CloneEntity(record, allowAliases: false)) != id)
                throw InvalidRuntime();
        }

        /// <summary>主鍵名稱必須由已驗 metadata 提供，任意表、活動與自訂實體都不能按名稱猜。</summary>
        private Entity? FindOwned(string entity, Guid id, ColumnSet columns)
        {
            if (!primaryIds.TryGetValue(entity, out var primary)) throw InvalidRuntime();
            var query = new QueryExpression(entity) { ColumnSet = columns, TopCount = 2 };
            query.Criteria.AddCondition(primary, ConditionOperator.Equal, id);
            var row = ReadBounded(query, 1).SingleOrDefault();
            if (row is not null && row.Id != id) throw InvalidRuntime();
            return row;
        }

        /// <summary>只查本次 target、逐一指定 step 及 trigger 開始時間；每個 step 恰好一個完成作業。</summary>
        private Entity[]? CompletedOperations()
        {
            if (!triggered) throw InvalidRuntime();
            var target = records.Single(item => item.Alias == trigger.TargetAlias).Record;
            var operations = new List<Entity>();
            foreach (var step in stepIds)
            {
                var query = new QueryExpression("asyncoperation")
                {
                    ColumnSet = new ColumnSet("correlationid", "statecode", "statuscode", "errorcode"), TopCount = 2
                };
                query.Criteria.AddCondition("regardingobjectid", ConditionOperator.Equal, target.Id);
                query.Criteria.AddCondition("owningextensionid", ConditionOperator.Equal, step);
                query.Criteria.AddCondition("createdon", ConditionOperator.OnOrAfter, triggerStarted);
                var rows = ReadBounded(query, 1);
                if (rows.Length == 0 || rows[0].GetAttributeValue<OptionSetValue>("statecode")?.Value != 3) return null;
                if (rows[0].GetAttributeValue<OptionSetValue>("statuscode")?.Value != 30 ||
                    rows[0].GetAttributeValue<int?>("errorcode") is not (null or 0)) throw InvalidRuntime();
                operations.Add(rows[0]);
            }
            return operations.ToArray();
        }

        /// <summary>回應頁面、筆數、空／重複資料庫 ID 都核對；不可只取第一筆。</summary>
        private Entity[] ReadBounded(QueryExpression query, int maximum)
        {
            var response = runtime.service.RetrieveMultiple(query);
            var ids = new HashSet<Guid>();
            if (response.MoreRecords || response.Entities.Count > maximum ||
                response.Entities.Any(row => row.Id == Guid.Empty || row.LogicalName != query.EntityName || !ids.Add(row.Id)))
                throw InvalidRuntime();
            return response.Entities.Select(row => RecordingOrganizationService.CloneEntity(row)).ToArray();
        }

        /// <summary>最多六十秒且每兩秒查一次；單調 clock、caller token 與延遲 timer 均操作區域擁有。</summary>
        private void Poll(TimeSpan maximumWait, Func<bool> read, CancellationToken cancellationToken)
        {
            if (maximumWait <= TimeSpan.Zero || maximumWait > TimeSpan.FromSeconds(60)) throw InvalidRuntime();
            var started = runtime.clock.GetTimestamp();
            for (var attempt = 0; attempt <= 30; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var complete = read();
                cancellationToken.ThrowIfCancellationRequested();
                if (complete) return;
                var remaining = maximumWait - runtime.clock.GetElapsedTime(started);
                if (remaining <= TimeSpan.Zero || attempt == 30) break;
                Task.Delay(remaining < TimeSpan.FromSeconds(2) ? remaining : TimeSpan.FromSeconds(2),
                    runtime.clock, cancellationToken).GetAwaiter().GetResult();
            }
            throw new TimeoutException("Shadow operation did not complete within its bounded wait.");
        }

        /// <summary>已釋放 owner 不能再持有或使用上一輪資料。</summary>
        private void EnsureActive() => ObjectDisposedException.ThrowIf(disposed, this);
    }
}

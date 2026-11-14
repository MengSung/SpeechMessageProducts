/*
 * 檔案整體說明
 *
 * 目的與定位：連接已驗證的固定情境檔、權威 metadata 與 SDK round owner，讓 CLI
 * 在第一筆業務寫入前完成整案轉換。本檔只建立 operation-local 候選，不取得連線、
 * 不讀 Secrets、不授權呼叫端提供的身分，也不啟用任何步驟。
 * 主要功能與流程：呼叫端先以 ShadowTestIdentity 核對教會及會友，並以受審盤點及
 * 正式註冊 metadata 建立 binding；factory 以明確別名綁定共享列，轉換自建 seed、
 * trigger 與 observe，經唯一 recorder 投影 expected，再將暫時 GUID 換回情境代號。
 * 使用邊界與維護：共享教會／會友在 seed 只能宣告空欄位，不能忽略其修改要求；
 * 它們不進自建清冊，也不可作 trigger 或 expected 寫入目標。owned lookup 以別名
 * 留待每輪配置新 GUID，不讓規劃用暫時 ID 被送到 SDK。欄位、精度與名稱前綴長度
 * 均由 metadata 驗證；尚未核對實機映像與關聯新增列的路徑，以及活動、推播、例外或
 * 輸出明確拒絕，不能略過後聲稱驗收；完整 CLI 與這些結果種類的接線仍須另外完成。
 * 擁有權與失敗：所有可變字典及 SDK Entity 都屬本次同步準備；JSON 文件與 recorder
 * 由方法 using 確定釋放，輸出不持有文件、服務或 delegate。沒有 static cache、
 * 背景工作、取消註冊或跨使用者狀態；完整候選成功才發布，錯誤不包含原始資料。
 */
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Xrm.Sdk;

namespace SpeechMessage.Dynamics.Deployment.Shadow;

/// <summary>
/// 呼叫端由核定盤點及當下註冊 metadata 建立的命令區域繫結；不能直接從情境 JSON 反序列化。
/// ChurchAlias／ContactAlias 明確指定共享身分，Entrypoint 對照受審的舊入口，
/// token 來源及不可避免的寫死 token 旗標仍由盤點負責，本 DTO 本身不構成授權。
/// </summary>
internal sealed record ShadowSdkBinding(string Entrypoint, string ChurchAlias, string ContactAlias,
    IReadOnlyList<Guid> LegacyStepIds, IReadOnlyList<Guid> ShadowStepIds,
    IReadOnlyList<string> ShadowTypes, IReadOnlyList<ShadowSdkTokenField> TokenFields,
    bool HasUnconditionalHardcodedToken)
{
    /// <summary>當下正式註冊為同步的 step 與實際型別，由 CLI metadata 核對提供，不讀情境旗標。</summary>
    public IReadOnlyDictionary<Guid, string> SynchronousStepTypes { get; init; } = new Dictionary<Guid, string>();
}

/// <summary>整案準備成功才發布的配對；Definition 與 Source 必須在同一命令使用，不得跨組織快取。</summary>
internal sealed record ShadowSdkPreparedScenario(ShadowScenarioDefinition Definition, ShadowSdkScenario Source);

/// <summary>僅建立完整候選的純接線入口；SDK 與身分檢查已由 caller 在命令區域完成。</summary>
internal static class ShadowSdkScenarioFactory
{
    /// <summary>
    /// 將 reader 的一個實機 CRUD 案轉為 runner／runtime 配對。所有 lookup 都必須來自
    /// 明列 seed 或核定共享別名；暫時 ID 只供純轉換及安全投影，絕不是業務身分。
    /// 不支援的結果種類在準備階段拒絕，不以空清單冒充成功。
    /// </summary>
    internal static ShadowSdkPreparedScenario Prepare(ShadowFileCase file, ShadowSdkMetadata metadata,
        ShadowVerifiedIdentity identity, ShadowSdkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(binding);
        if (!file.LiveShadow || file.IsActivity || file.Entrypoint != binding.Entrypoint ||
            file.Basis is not ("new" or "reading" or "legacy-golden" or "legacy-live" or "legacy-live-only") ||
            file.ExpectedLineCalls.ValueKind != JsonValueKind.Array || file.ExpectedLineCalls.GetArrayLength() != 0 ||
            file.Exception.ValueKind != JsonValueKind.Null || file.Outputs.ValueKind != JsonValueKind.Object ||
            file.Outputs.EnumerateObject().Any() || identity.OrganizationId == Guid.Empty ||
            (file.Trigger.TryGetProperty("images", out var images) && images.EnumerateObject().Any()) ||
            identity.ChurchId == Guid.Empty || identity.ContactId == Guid.Empty || identity.ChurchId == identity.ContactId ||
            !ShadowProjection.ValidIdentity(binding.ChurchAlias) || !ShadowProjection.ValidIdentity(binding.ContactAlias) ||
            binding.ChurchAlias == binding.ContactAlias ||
            file.Church.GetProperty("alias").GetString() != binding.ChurchAlias)
            throw Invalid();
        ValidateBinding(binding, file.Basis);
        var shared = new Dictionary<string, EntityReference>(StringComparer.Ordinal)
        {
            [binding.ChurchAlias] = new("account", identity.ChurchId),
            [binding.ContactAlias] = new("contact", identity.ContactId)
        };
        var aliases = new Dictionary<string, EntityReference>(shared, StringComparer.Ordinal);
        var rows = file.Seed.EnumerateArray().ToArray();
        if (rows.Length is < 3 or > 64) throw Invalid();
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var alias = Text(row, "alias");
            var entity = Text(row, "entity");
            if (!declared.Add(alias)) throw Invalid();
            _ = metadata.PrimaryId(entity);
            if (shared.TryGetValue(alias, out var reference))
            {
                if (reference.LogicalName != entity || row.GetProperty("fields").EnumerateObject().Any())
                    throw Invalid();
            }
            else if (!aliases.TryAdd(alias, new EntityReference(entity, Guid.NewGuid()))) throw Invalid();
        }
        if (!declared.Contains(binding.ChurchAlias) || !declared.Contains(binding.ContactAlias)) throw Invalid();

        // 所有規劃 ID 在單次準備中唯一；實際 round 另配 GUID，不能保留規劃 lookup。
        EntityReference Resolve(string alias, string entity) =>
            aliases.TryGetValue(alias, out var reference) && reference.LogicalName == entity
                ? new EntityReference(entity, reference.Id) : throw Invalid();
        var idAliases = aliases.ToDictionary(pair => pair.Value.Id.ToString("D"), pair => pair.Key, StringComparer.Ordinal);
        var seeds = new List<ShadowSdkSeed>();
        var links = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var alias = Text(row, "alias");
            if (shared.ContainsKey(alias)) continue;
            var entity = Text(row, "entity");
            var candidate = metadata.ConvertFields(entity, row.GetProperty("fields"), true, Resolve);
            ValidateNamePrefix(candidate, metadata, Resolve);
            links.Add(alias, ExtractReferences(candidate, idAliases));
            seeds.Add(new ShadowSdkSeed(alias, candidate));
        }
        var targetAlias = Text(file.Trigger, "target");
        var targetEntity = Text(file.Trigger, "entity");
        var message = Text(file.Trigger, "message");
        if (message is not ("Create" or "Update" or "Delete") || shared.ContainsKey(targetAlias) ||
            !aliases.TryGetValue(targetAlias, out var target) || target.LogicalName != targetEntity)
            throw Invalid();
        var patch = metadata.ConvertFields(targetEntity, file.Trigger.GetProperty("fields"), message == "Create", Resolve);
        if (message == "Delete" && patch.Attributes.Count != 0) throw Invalid();
        var patchLinks = ExtractReferences(patch, idAliases);
        foreach (var token in binding.TokenFields)
            if (!seeds.Any(seed => seed.Alias == token.Alias) ||
                (message == "Create" && token.Alias == targetAlias)) throw Invalid();

        var observations = new List<ShadowObservation>();
        foreach (var row in file.Observe.EnumerateArray())
        {
            var alias = Text(row, "alias");
            var entity = Text(row, "entity");
            if (shared.ContainsKey(alias) || !aliases.TryGetValue(alias, out var reference) ||
                reference.LogicalName != entity) throw Invalid();
            var fields = metadata.ValidateObservation(entity,
                row.GetProperty("fields").EnumerateArray().Select(field => field.GetString()!).ToArray());
            observations.Add(new ShadowObservation(entity, alias, fields));
        }
        var expected = PrepareExpected(file.ExpectedWrites, metadata, aliases, shared, idAliases, Resolve);
        var definition = new ShadowScenarioDefinition(expected, observations.AsReadOnly(),
            new HashSet<string>(file.Volatile, StringComparer.Ordinal)) { Basis = file.Basis };
        var source = new ShadowSdkScenario(seeds.AsReadOnly(), new ShadowSdkTrigger(message, targetAlias, patch),
            Array.AsReadOnly(binding.LegacyStepIds.ToArray()), Array.AsReadOnly(binding.ShadowStepIds.ToArray()),
            Array.AsReadOnly(binding.ShadowTypes.ToArray()), Array.AsReadOnly(binding.TokenFields.ToArray()),
            binding.HasUnconditionalHardcodedToken)
        {
            PrimaryIdAttributes = new ReadOnlyDictionary<string, string>(seeds.Select(seed => seed.Record.LogicalName)
                .Distinct(StringComparer.Ordinal).ToDictionary(entity => entity, metadata.PrimaryId, StringComparer.Ordinal)),
            SharedReferences = new ReadOnlyDictionary<string, EntityReference>(shared),
            ReferenceAliases = new ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>(links),
            TriggerReferenceAliases = patchLinks,
            SynchronousStepTypes = new ReadOnlyDictionary<Guid, string>(
                new Dictionary<Guid, string>(binding.SynchronousStepTypes))
        };
        return new ShadowSdkPreparedScenario(definition, source);
    }

    /// <summary>
    /// 完整預期寫入依同一 metadata 及 recorder 投影；目前只接受已宣告 owned 列的 Update／Delete。
    /// 未有關聯觀察繫結的 Create 不能猜新列身分，推播也不能混入 CRUD 最終值比較。
    /// </summary>
    private static ShadowTrace PrepareExpected(JsonElement writes, ShadowSdkMetadata metadata,
        Dictionary<string, EntityReference> aliases, Dictionary<string, EntityReference> shared,
        Dictionary<string, string> idAliases, Func<string, string, EntityReference> resolve)
    {
        if (writes.ValueKind != JsonValueKind.Array || writes.GetArrayLength() > 128) throw Invalid();
        var intents = new List<ShadowIntent>();
        foreach (var write in writes.EnumerateArray())
        {
            var operation = Text(write, "operation");
            var entity = Text(write, "entity");
            var alias = Text(write, "alias");
            if (operation is not ("Update" or "Delete") || shared.ContainsKey(alias) ||
                !aliases.TryGetValue(alias, out var target) || target.LogicalName != entity) throw Invalid();
            var candidate = metadata.ConvertFields(entity, write.GetProperty("fields"), false, resolve);
            if (operation == "Delete" && candidate.Attributes.Count != 0) throw Invalid();
            candidate.Id = target.Id;
            var projection = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["seq"] = intents.Count + 1, ["operation"] = operation, ["entity"] = entity, ["id"] = target.Id.ToString("D")
            };
            if (operation == "Update") projection.Add("fields", ShadowSdkProjection.Project(candidate).Fields);
            intents.Add(new ShadowIntent(intents.Count + 1, operation, entity, target.Id.ToString("D"),
                JsonSerializer.SerializeToElement(projection)));
        }
        return ShadowProjection.NormalizeTrace(new ShadowTrace(intents.AsReadOnly(), false, true, intents.Count), idAliases);
    }

    /// <summary>抽離 lookup 為顯式別名，避免暫時規劃 GUID 被 runtime 當成真正外部身分送出。</summary>
    private static ReadOnlyDictionary<string, string> ExtractReferences(Entity candidate, Dictionary<string, string> aliases)
    {
        var links = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in candidate.Attributes.ToArray())
        {
            if (pair.Value is not EntityReference reference) continue;
            if (!aliases.TryGetValue(reference.Id.ToString("D"), out var alias)) throw Invalid();
            links.Add(pair.Key, alias);
            candidate.Attributes.Remove(pair.Key);
        }
        return new ReadOnlyDictionary<string, string>(links);
    }

    /// <summary>runtime 的測試名稱前綴也須遵守正式欄位上限，不能在驗證後才製造超長字串。</summary>
    private static void ValidateNamePrefix(Entity candidate, ShadowSdkMetadata metadata,
        Func<string, string, EntityReference> resolve)
    {
        foreach (var name in new[] { "name", "subject" })
            if (candidate.Attributes.TryGetValue(name, out var value) && value is string text)
                _ = metadata.ConvertFields(candidate.LogicalName,
                    JsonSerializer.SerializeToElement(new Dictionary<string, string> { [name] = "SMV-TEST-" + text }), true, resolve);
    }

    /// <summary>正式註冊繫結須完整唯一；不可避免的寫死 token 在準備前就拒絕真實舊輪。</summary>
    private static void ValidateBinding(ShadowSdkBinding binding, string basis)
    {
        if (binding.LegacyStepIds is null || binding.ShadowStepIds is null || binding.ShadowTypes is null ||
            binding.TokenFields is null || binding.TokenFields.Count > 32 || binding.SynchronousStepTypes is null ||
            binding.SynchronousStepTypes.Count > 256 ||
            binding.SynchronousStepTypes.Any(pair => pair.Key == Guid.Empty ||
                !binding.LegacyStepIds.Concat(binding.ShadowStepIds).Contains(pair.Key) ||
                string.IsNullOrWhiteSpace(pair.Value) || pair.Value.Length > 512) ||
            !ValidSteps(binding.ShadowStepIds) ||
            ((basis is "legacy-live" or "legacy-live-only") &&
                (!ValidSteps(binding.LegacyStepIds) || binding.HasUnconditionalHardcodedToken)) ||
            binding.ShadowTypes.Count is < 1 or > 128 ||
            binding.ShadowTypes.Any(type => string.IsNullOrWhiteSpace(type) || type.Length > 512) ||
            binding.ShadowTypes.Distinct(StringComparer.Ordinal).Count() != binding.ShadowTypes.Count ||
            binding.TokenFields.Any(field => field is null || !ShadowProjection.ValidIdentity(field.Alias) ||
                string.IsNullOrWhiteSpace(field.Attribute)) ||
            binding.TokenFields.Distinct().Count() != binding.TokenFields.Count) throw Invalid();
    }

    /// <summary>步驟必須有正式唯一 ID 且有界，不採第一筆或以集合去重掩蓋衝突。</summary>
    private static bool ValidSteps(IReadOnlyList<Guid> ids) => ids.Count is > 0 and <= 128 &&
        ids.All(id => id != Guid.Empty) && ids.Distinct().Count() == ids.Count;

    /// <summary>固定 reader DTO 的必要字串不接受空值，錯誤不回顯原文。</summary>
    private static string Text(JsonElement value, string name) =>
        value.GetProperty(name).GetString() is { Length: > 0 } text ? text : throw Invalid();

    /// <summary>固定安全錯誤不含候選資料、秘密、GUID 或原始 metadata。</summary>
    private static InvalidDataException Invalid() => new("Shadow scenario binding failed.");
}

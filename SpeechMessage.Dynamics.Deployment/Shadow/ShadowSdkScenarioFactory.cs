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
 * 均由 metadata 驗證；related 僅可指向自建 seed，Create 期望須帶相同 lookup，
 * 同一 selector 不可宣告兩個別名。推播要求期望明列 shadow 摘要、單一權威測試
 * 聯絡人及 owned Retry Key；完整寫入後附推播意圖，符合正式 writer 的輸出順序。
 * 映像僅核對正式註冊及 seed／trigger 可產生的前後欄值，不注入平台 context；
 * 不能由可寫 seed 重現的唯讀／系統欄值仍拒絕，不假稱量測平台映像。
 * 本檔不傳送 LINE、不把新輪摘要當舊輪實際推播；活動、例外或輸出仍明確拒絕。
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
    /// <summary>受審盤點明確確認所有舊入口都沒有 token 相依，不能由空 tokenFields 自動推定。</summary>
    public bool TokenNotRequired { get; init; }
    /// <summary>每個實際執行步驟已核對的映像註冊；僅由 CLI 提供，不接受情境自行授權或注入映像。</summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<RegistrationImage>> StepImages { get; init; } =
        new Dictionary<Guid, IReadOnlyList<RegistrationImage>>();
    /// <summary>已由受審盤點及正式metadata核對的外部既有列範圍；不能由情境自行宣告。</summary>
    public IReadOnlyList<ShadowExternalScope> ExternalScopes { get; init; } = Array.Empty<ShadowExternalScope>();
    /// <summary>表示來源盤點明確包含externalWrites；不能以未接線的空集合宣稱沒有外部修改。</summary>
    public bool ExternalInventoryReviewed { get; init; }
}

/// <summary>整案準備成功才發布的配對；Definition 與 Source 必須在同一命令使用，不得跨組織快取。</summary>
internal sealed record ShadowSdkPreparedScenario(ShadowScenarioDefinition Definition, ShadowSdkScenario Source);

/// <summary>僅建立完整候選的純接線入口；SDK 與身分檢查已由 caller 在命令區域完成。</summary>
internal static class ShadowSdkScenarioFactory
{
    /// <summary>
    /// 將 reader 的一個實機 CRUD／Push 案轉為 runner／runtime 配對。所有 lookup 都必須來自
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
            file.ExpectedLineCalls.ValueKind != JsonValueKind.Array ||
            file.Exception.ValueKind != JsonValueKind.Null || file.Outputs.ValueKind != JsonValueKind.Object ||
            file.Outputs.EnumerateObject().Any() || identity.OrganizationId == Guid.Empty ||
            identity.ChurchId == Guid.Empty || identity.ContactId == Guid.Empty || identity.ChurchId == identity.ContactId ||
            !ShadowProjection.ValidIdentity(binding.ChurchAlias) || !ShadowProjection.ValidIdentity(binding.ContactAlias) ||
            binding.ChurchAlias == binding.ContactAlias ||
            file.Church.GetProperty("alias").GetString() != binding.ChurchAlias)
            throw Invalid();
        ValidateBinding(binding, file.Basis);
        ShadowScenarioFiles.LineCalls(file.ExpectedLineCalls);
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
        ValidateImages(file, binding, metadata, rows.Single(row => Text(row, "alias") == targetAlias), patch, Resolve);
        var patchLinks = ExtractReferences(patch, idAliases);
        foreach (var token in binding.TokenFields)
            if (!seeds.Any(seed => seed.Alias == token.Alias) ||
                (message == "Create" && token.Alias == targetAlias)) throw Invalid();
        var tokens = binding.TokenFields.Select(token =>
        {
            var entity = seeds.Single(seed => seed.Alias == token.Alias).Record.LogicalName;
            return token with { MaximumLength = metadata.TokenMaximumLength(entity, token.Attribute) };
        }).ToArray();

        var observations = new List<ShadowObservation>();
        var selectors = new HashSet<(string Entity, string Attribute, string Parent)>();
        foreach (var row in file.Observe.EnumerateArray())
        {
            var alias = Text(row, "alias");
            var entity = Text(row, "entity");
            ShadowRelatedObservation? selector = null;
            if (row.TryGetProperty("related", out var related))
            {
                var parentAlias = Text(related, "alias");
                var attribute = Text(related, "attribute");
                if (!ShadowProjection.ValidIdentity(alias) || aliases.ContainsKey(alias) ||
                    shared.ContainsKey(parentAlias) || !declared.Contains(parentAlias) ||
                    !aliases.TryGetValue(parentAlias, out var parent) ||
                    !selectors.Add((entity, attribute, parentAlias))) throw Invalid();
                metadata.ValidateRelatedLookup(entity, attribute, parent.LogicalName);
                selector = new ShadowRelatedObservation(attribute, parentAlias);
                var reference = new EntityReference(entity, Guid.NewGuid());
                aliases.Add(alias, reference);
                idAliases.Add(reference.Id.ToString("D"), alias);
            }
            else if (shared.ContainsKey(alias) || !declared.Contains(alias) ||
                !aliases.TryGetValue(alias, out var reference) || reference.LogicalName != entity) throw Invalid();
            var fields = metadata.ValidateObservation(entity,
                row.GetProperty("fields").EnumerateArray().Select(field => field.GetString()!).ToArray());
            observations.Add(new ShadowObservation(entity, alias, fields) { Related = selector });
        }
        if (aliases.Count > 64 || observations.Count is < 1 or > 64 ||
            observations.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != observations.Count) throw Invalid();
        var expected = PrepareExpected(file.ExpectedWrites, file.ExpectedLineCalls, metadata, aliases, shared, idAliases, observations, Resolve);
        var definition = new ShadowScenarioDefinition(expected, observations.AsReadOnly(),
            new HashSet<string>(file.Volatile, StringComparer.Ordinal)) { Basis = file.Basis };
        var source = new ShadowSdkScenario(seeds.AsReadOnly(), new ShadowSdkTrigger(message, targetAlias, patch),
            Array.AsReadOnly(binding.LegacyStepIds.ToArray()), Array.AsReadOnly(binding.ShadowStepIds.ToArray()),
            Array.AsReadOnly(binding.ShadowTypes.ToArray()), Array.AsReadOnly(tokens),
            binding.HasUnconditionalHardcodedToken)
        {
            PrimaryIdAttributes = new ReadOnlyDictionary<string, string>(seeds.Select(seed => seed.Record.LogicalName)
                .Concat(observations.Select(item => item.Entity))
                .Distinct(StringComparer.Ordinal).ToDictionary(entity => entity, metadata.PrimaryId, StringComparer.Ordinal)),
            SharedReferences = new ReadOnlyDictionary<string, EntityReference>(shared),
            ReferenceAliases = new ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>(links),
            TriggerReferenceAliases = patchLinks,
            SynchronousStepTypes = new ReadOnlyDictionary<Guid, string>(
                new Dictionary<Guid, string>(binding.SynchronousStepTypes)),
            TokenNotRequired = binding.TokenNotRequired,
            ExternalScopes = Array.AsReadOnly(binding.ExternalScopes.ToArray()),
            ExternalInventoryReviewed = binding.ExternalInventoryReviewed
        };
        return new ShadowSdkPreparedScenario(definition, source);
    }

    /// <summary>
    /// 依已核對註冊驗證宣告映像；平台自行建立 context，本方法不將宣告值送進 SDK。
    /// 前像來自未加測試名稱前綴的 seed，後像依 trigger 覆蓋；正式執行時名稱前綴一致套用。
    /// 僅支援可由明列可寫欄位重現的值，缺 seed 值、錯型別或未知別名一律失敗。
    /// live 舊輪必須對每個實際舊／新步驟核對，不能只驗新步驟而忽略舊註冊差異。
    /// </summary>
    private static void ValidateImages(ShadowFileCase file, ShadowSdkBinding binding, ShadowSdkMetadata metadata,
        JsonElement targetSeed, Entity patch, Func<string, string, EntityReference> resolve)
    {
        if (!file.Trigger.TryGetProperty("images", out var images)) return;
        if (images.ValueKind != JsonValueKind.Object || images.EnumerateObject().Count() > 2) throw Invalid();
        var requiredSteps = file.Basis is "legacy-live" or "legacy-live-only"
            ? binding.ShadowStepIds.Concat(binding.LegacyStepIds).ToArray() : binding.ShadowStepIds.ToArray();
        var entity = patch.LogicalName;
        var before = metadata.ConvertFields(entity, targetSeed.GetProperty("fields"), true, resolve);
        foreach (var phase in images.EnumerateObject())
        {
            if (phase.Name is not ("pre" or "post") || phase.Value.ValueKind != JsonValueKind.Object ||
                phase.Value.EnumerateObject().Count() > 32 ||
                (phase.Name == "pre" && Text(file.Trigger, "message") == "Create") ||
                (phase.Name == "post" && Text(file.Trigger, "message") == "Delete")) throw Invalid();
            var type = phase.Name == "pre" ? "PreImage" : "PostImage";
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var image in phase.Value.EnumerateObject())
            {
                if (!ShadowProjection.ValidIdentity(image.Name) || !names.Add(image.Name)) throw Invalid();
                var expected = metadata.ConvertFields(entity, image.Value, true, resolve);
                if (expected.Attributes.Count == 0) throw Invalid();
                foreach (var stepId in requiredSteps)
                {
                    if (!binding.StepImages.TryGetValue(stepId, out var registered)) throw Invalid();
                    var matches = registered.Where(item => item.Name == image.Name && item.Type == type).ToArray();
                    if (matches.Length != 1 || expected.Attributes.Keys.Any(field =>
                        !matches[0].Attributes.Contains(field, StringComparer.Ordinal))) throw Invalid();
                }
                foreach (var field in expected.Attributes)
                {
                    var source = phase.Name == "post" && patch.Contains(field.Key) ? patch : before;
                    if (!source.Contains(field.Key) || !Equals(source[field.Key], field.Value)) throw Invalid();
                }
            }
        }
    }

    /// <summary>
    /// 完整預期寫入依同一 metadata 及 recorder 投影；Create 只接受明列 related 的新別名，
    /// 必須帶相同父 lookup，並依序驗證尚未建立／重複建立及刪除後更新，不猜新列身分。
    /// 隨後按正式 writer 順序附加完整推播摘要；只有權威共享 contact 可收取、Retry Key 必須為本案 owned 別名。
    /// </summary>
    private static ShadowTrace PrepareExpected(JsonElement writes, JsonElement lineCalls, ShadowSdkMetadata metadata,
        Dictionary<string, EntityReference> aliases, Dictionary<string, EntityReference> shared,
        Dictionary<string, string> idAliases, IReadOnlyList<ShadowObservation> observations,
        Func<string, string, EntityReference> resolve)
    {
        if (writes.ValueKind != JsonValueKind.Array || writes.GetArrayLength() > 128) throw Invalid();
        var intents = new List<ShadowIntent>();
        var related = observations.Where(item => item.Related is not null).ToDictionary(item => item.Id, StringComparer.Ordinal);
        var present = aliases.Keys.Where(alias => !related.ContainsKey(alias)).ToHashSet(StringComparer.Ordinal);
        foreach (var write in writes.EnumerateArray())
        {
            var operation = Text(write, "operation");
            var entity = Text(write, "entity");
            var alias = Text(write, "alias");
            if (operation is not ("Create" or "Update" or "Delete") || shared.ContainsKey(alias) ||
                !aliases.TryGetValue(alias, out var target) || target.LogicalName != entity) throw Invalid();
            var candidate = metadata.ConvertFields(entity, write.GetProperty("fields"), operation == "Create", resolve);
            if (operation == "Create")
            {
                if (!related.TryGetValue(alias, out var observation) || !present.Add(alias) ||
                    candidate.GetAttributeValue<EntityReference>(observation.Related!.Attribute) is not { } parent ||
                    parent.Id != aliases[observation.Related.ParentAlias].Id) throw Invalid();
            }
            else if (!present.Contains(alias)) throw Invalid();
            if (operation == "Delete") present.Remove(alias);
            if (operation == "Delete" && candidate.Attributes.Count != 0) throw Invalid();
            candidate.Id = target.Id;
            var projection = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["seq"] = intents.Count + 1, ["operation"] = operation, ["entity"] = entity, ["id"] = target.Id.ToString("D")
            };
            if (operation != "Delete") projection.Add("fields", ShadowSdkProjection.Project(candidate).Fields);
            intents.Add(new ShadowIntent(intents.Count + 1, operation, entity, target.Id.ToString("D"),
                JsonSerializer.SerializeToElement(projection)));
        }
        // 正式 ShadowRecordWriter 先寫 CRUD，再寫推播；此處同序，完整比較不可只比最後欄值。
        foreach (var call in lineCalls.EnumerateArray())
        {
            if (intents.Count >= 128 || Text(call, "endpoint") != "/v2/bot/message/push" ||
                call.GetProperty("recipients").GetArrayLength() != 1 ||
                call.GetProperty("messageCount").GetInt32() == 0 ||
                !call.TryGetProperty("shadow", out var summary)) throw Invalid();
            var contactAlias = call.GetProperty("recipients")[0].GetString()!;
            var retryAlias = Text(summary, "retryKey");
            if (!shared.TryGetValue(contactAlias, out var contact) || contact.LogicalName != "contact" ||
                shared.ContainsKey(retryAlias) || !aliases.TryGetValue(retryAlias, out var retry)) throw Invalid();
            var projection = JsonSerializer.SerializeToElement(new
            {
                seq = intents.Count + 1, operation = "Push", contactId = contact.Id.ToString("D"),
                templateKey = Text(summary, "templateKey"), retryKey = retry.Id.ToString("D"),
                messageCount = call.GetProperty("messageCount").GetInt32(), contentSha256 = Text(summary, "contentSha256")
            });
            // Push 的身分在 contactId；Entity 依正式 parser 契約為空，不冒用 CRUD 實體欄位。
            intents.Add(new ShadowIntent(intents.Count + 1, "Push", "", contact.Id.ToString("D"), projection));
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

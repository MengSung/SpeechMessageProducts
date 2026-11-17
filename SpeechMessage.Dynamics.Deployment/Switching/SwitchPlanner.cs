/*
檔案整體說明
目的與定位：階段7純值切換規劃器，從完整且已驗證的現況候選建立可審核、有序、無秘密的計畫。
主要流程：驗DLL與正式Live證據、核對全部正式ID與新舊模組互斥，依唯一SwitchTarget計算設定／狀態操作。
流程只要含未移植或dormant舊活動就整個KeepLegacy；超1MiB或暫停流程列Manual，不部分改寫。
資源與隔離：不建立SDK或解析秘密，資料只屬本次命令；候選先複製、結果唯讀發布，沒有共享可變狀態。
失敗與驗證：固定安全錯誤不帶XAML；SwitchPlanTests以缺證據／缺DLL／混合引用及非法互斥驗fail closed。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace SpeechMessage.Dynamics.Deployment.Switching;

/// <summary>無副作用的完整計畫建構器；候選不代表授權，執行器仍須鎖內重讀來源及server身分。</summary>
internal static class SwitchPlanner
{
    /// <summary>以原況與有效分級建立七類核定操作；所有檢查成功才發布，從不修改輸入候選。</summary>
    internal static SwitchPlan Create(SwitchSnapshot source, string target)
    {
        ArgumentNullException.ThrowIfNull(source);
        var resources = new SwitchText();
        _ = SwitchTarget.Resolve(target, "live", false);
        var snapshot = JsonSerializer.Deserialize<SwitchSnapshot>(JsonSerializer.SerializeToUtf8Bytes(source)) ?? throw Invalid();
        Validate(snapshot);
        var config = new List<SwitchOperation>();
        var disable = new List<SwitchOperation>();
        var legacy = new List<SwitchOperation>();
        var workflows = new List<SwitchOperation>();
        var enable = new List<(int Mode, SwitchOperation Operation)>();
        var dormant = new List<string>();
        var currentlyEnabledModules = new HashSet<string>(StringComparer.Ordinal);
        var targetEnabledModules = new HashSet<string>(StringComparer.Ordinal);
        var legacyEnabledModules = new HashSet<string>(StringComparer.Ordinal);
        var keptEnabledModules = new HashSet<string>(StringComparer.Ordinal);
        var steps = snapshot.Steps.Where(row => row.Registration.Module != "diagnostics").OrderBy(row => row.Id).ToArray();
        foreach (var row in steps)
        {
            var item = row.Registration;
            var live = item.Live ?? throw Invalid();
            var tier = SwitchTarget.EffectiveTier(snapshot.Organization, item.Module, item.Type, live.Tier);
            var expected = SwitchTarget.Resolve(target, tier, live.TokenKey);
            CheckEvidence(snapshot.Organization, target, tier, live.Evidence);
            if (tier == "dormant") dormant.Add(item.Type + " " + live.Reason);
            if (row.Enabled) currentlyEnabledModules.Add(item.Module);
            if (expected.Enabled) targetEnabledModules.Add(item.Module);
            var configuration = Configuration(item.UnsecureConfig, expected.Mode);
            var differs = !ConfigurationEquals(row.Configuration, configuration) || (row.SecureId is not null) != (expected.SecureConfiguration.Length != 0);
            // 任何設定變更前先停新步驟；穩態off->live仍維持config→停舊→改指→啟新的固定順序。
            if (row.Enabled && (!expected.Enabled || differs)) disable.Add(Operation("DisableStep", row.Id, item.Module));
            if (differs) config.Add(Operation("SetStepConfiguration", row.Id, item.Module) with
                { Configuration = configuration, SecureConfiguration = expected.SecureConfiguration });
            if (expected.Enabled && (!row.Enabled || differs)) enable.Add((item.Mode == "Synchronous" ? 0 : 1,
                Operation("EnableStep", row.Id, item.Module) with { Enabled = true }));
            foreach (var reference in item.Legacy)
            {
                if (!Guid.TryParse(reference.StepId, out var id)) throw Invalid();
                var old = snapshot.LegacySteps.SingleOrDefault(candidate => candidate.Id == id) ?? throw Invalid();
                if (old.Type != reference.Type || !string.Equals(old.Assembly, reference.Assembly, StringComparison.OrdinalIgnoreCase)) throw Invalid();
                if (old.Enabled) legacyEnabledModules.Add(item.Module);
                if (tier == "dormant")
                {
                    legacy.Add(Operation("KeepLegacy", old.Id, item.Module) with { Reason = resources["DormantLegacyReason"] });
                    if (old.Enabled) keptEnabledModules.Add(item.Module);
                }
                else if (old.Enabled) legacy.Add(Operation("DisableLegacyStep", old.Id, item.Module));
            }
        }
        foreach (var activity in snapshot.Activities)
        {
            var item = activity.Registration;
            var live = item.Live ?? throw Invalid();
            var tier = SwitchTarget.EffectiveTier(snapshot.Organization, item.Module, item.Type, live.Tier);
            CheckEvidence(snapshot.Organization, target, tier, live.Evidence);
            if (tier == "dormant") dormant.Add(item.Type + " " + live.Reason);
        }
        foreach (var workflow in snapshot.Workflows.OrderBy(row => row.Id))
        {
            if (workflow.State is < 0 or > 2) throw Invalid();
            if (System.Text.Encoding.UTF8.GetByteCount(workflow.Xaml) > 1024 * 1024 || workflow.State == 2)
            {
                workflows.Add(Operation("Manual", workflow.Id, "") with { Reason = resources["WorkflowManualReason"] });
                // 原啟用且無法檢查的流程可能影響任一模組；不得猜測它與啟新無關。
                if (workflow.State == 1 && targetEnabledModules.Count > 0) throw Invalid();
                continue;
            }
            var references = WorkflowRepointer.ReadReferences(workflow.Xaml)
                .Where(reference => snapshot.LegacyActivities.Any(old => WorkflowRepointer.SameReference(reference, old.Assembly, old.Type))).ToArray();
            if (references.Length == 0) continue;
            var replacements = new List<WorkflowActivityReplacement>();
            var modules = new HashSet<string>(StringComparer.Ordinal);
            var keep = target != "live";
            var hasUnmappedActivity = false;
            foreach (var reference in references)
            {
                var matches = snapshot.Activities.Where(activity => activity.Registration.Legacy.Any(old =>
                    WorkflowRepointer.SameReference(reference, old.Assembly, old.Type))).ToArray();
                if (matches.Length > 1) throw Invalid();
                if (matches.Length == 0) { keep = true; hasUnmappedActivity = true; continue; }
                var activity = matches[0];
                var item = activity.Registration;
                modules.Add(item.Module);
                if (SwitchTarget.EffectiveTier(snapshot.Organization, item.Module, item.Type, item.Live!.Tier) != "live") keep = true;
                var replacement = new WorkflowActivityReplacement(reference.Assembly, reference.Type, activity.QualifiedAssembly, item.Type);
                if (!replacements.Contains(replacement)) replacements.Add(replacement);
            }
            if (workflow.State == 1 && hasUnmappedActivity && targetEnabledModules.Count > 0) throw Invalid();
            if (workflow.State == 1) foreach (var module in modules) legacyEnabledModules.Add(module);
            if (keep || replacements.Count == 0)
            {
                workflows.Add(Operation("KeepLegacy", workflow.Id, string.Join(",", modules.Order(StringComparer.Ordinal))) with
                    { Reason = resources["WorkflowKeepReason"] });
                if (workflow.State == 1) foreach (var module in modules) keptEnabledModules.Add(module);
            }
            else workflows.Add(Operation("RepointWorkflow", workflow.Id, string.Join(",", modules.Order(StringComparer.Ordinal))) with
                { Enabled = workflow.State == 1, Replacements = replacements.AsReadOnly() });
        }
        if (currentlyEnabledModules.Overlaps(legacyEnabledModules) || targetEnabledModules.Overlaps(keptEnabledModules)) throw Invalid();
        var mappedIds = steps.SelectMany(row => row.Registration.Legacy).Select(row => Guid.Parse(row.StepId)).ToHashSet();
        foreach (var old in snapshot.LegacySteps.Where(row => !mappedIds.Contains(row.Id)))
            legacy.Add(Operation("KeepLegacy", old.Id, "") with { Reason = resources["UnmappedStepReason"] });
        var body = target == "live" ? config.Concat(legacy).Concat(workflows) : workflows.Concat(legacy).Concat(config);
        var operations = disable.Concat(body)
            .Concat(enable.OrderBy(row => row.Mode).ThenBy(row => row.Operation.Id).Select(row => row.Operation)).ToArray();
        if (operations.Count(op => op.Kind is not ("KeepLegacy" or "Manual")) > 4096) throw Invalid();
        var plan = new SwitchPlan(1, snapshot.Organization.Name, snapshot.ServerOrganizationId, snapshot.Organization.Kind,
            snapshot.Organization.SwitchState, target, snapshot.Fingerprint, snapshot.ManifestSha256, snapshot.CatalogSha256,
            Array.AsReadOnly(snapshot.Assemblies.ToArray()), Array.AsReadOnly(operations), dormant.AsReadOnly(),
            Array.AsReadOnly(snapshot.RegisteredLegacyAssemblies.Concat(snapshot.LegacySteps.Select(row => row.Assembly)).Concat(snapshot.LegacyActivities.Select(row => row.Assembly))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()), "");
        return plan with { Sha256 = Hash(plan) };
    }

    /// <summary>verify僅回傳目標尚需的可執行差異；KeepLegacy/Manual是已列決定，不冒稱已移植或已驗證秘密實值。</summary>
    internal static IReadOnlyList<SwitchOperation> Verify(SwitchSnapshot current, SwitchPlan? approved = null)
    {
        if (approved is not null && (approved.SchemaVersion != 1 || approved.Organization != current.Organization.Name ||
            approved.ServerOrganizationId != current.ServerOrganizationId || approved.ManifestSha256 != current.ManifestSha256 ||
            approved.CatalogSha256 != current.CatalogSha256 || approved.Sha256 != Hash(approved with { Sha256 = "" }))) throw Invalid();
        var plan = Create(current, approved?.TargetState ?? current.Organization.SwitchState);
        return Array.AsReadOnly(plan.Operations.Where(operation => operation.Kind is not ("KeepLegacy" or "Manual")).ToArray());
    }

    /// <summary>保留核定options並只改mode；不能以字串Replace誤改選項文字，深度與容量均有界。</summary>
    internal static string Configuration(string template, string mode)
    {
        if (template.Length > 32768 || mode is not ("Shadow" or "Live")) throw Invalid();
        try
        {
            var node = JsonNode.Parse(template, documentOptions: new JsonDocumentOptions { MaxDepth = 16 }) as JsonObject ?? throw Invalid();
            node["mode"] = mode;
            return node.ToJsonString();
        }
        catch (JsonException) { throw Invalid(); }
    }

    /// <summary>純JSON語意相等，不因格式空白安排不必要遠端更新；不解析或比較secure實值。</summary>
    internal static bool ConfigurationEquals(string first, string second)
    {
        if (first.Length > 32768 || second.Length > 32768) throw Invalid();
        try { return JsonNode.DeepEquals(JsonNode.Parse(first), JsonNode.Parse(second)); }
        catch (JsonException) { throw Invalid(); }
    }

    /// <summary>完整性摘要只輸入純值安全DTO；plan自身雜湊由呼叫者清空後計算。</summary>
    internal static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    /// <summary>所有步驟／流程正式ID須非空且集合內唯一；組件缺失、內容不同或未知種類fail closed。</summary>
    private static void Validate(SwitchSnapshot snapshot)
    {
        if (snapshot.ServerOrganizationId == Guid.Empty || snapshot.Organization.Kind is not ("test" or "production") ||
            snapshot.Steps.Count > 256 || snapshot.Activities.Count > 256 || snapshot.LegacySteps.Count > 10000 || snapshot.Workflows.Count > 10000 ||
            snapshot.Assemblies.Count is < 1 or > 2 || snapshot.Assemblies.Select(row => row.Name).Distinct(StringComparer.Ordinal).Count() != snapshot.Assemblies.Count ||
            snapshot.Assemblies.Any(row => string.IsNullOrEmpty(row.RemoteSha256) || row.LocalSha256 != row.RemoteSha256)) throw Invalid();
        _ = SwitchTarget.Resolve(snapshot.Organization.SwitchState, "live", false);
        var ids = new HashSet<Guid>();
        foreach (var id in snapshot.Steps.Select(row => row.Id).Concat(snapshot.LegacySteps.Select(row => row.Id)))
            if (id == Guid.Empty || !ids.Add(id)) throw Invalid();
        ids.Clear();
        foreach (var id in snapshot.Workflows.Select(row => row.Id)) if (id == Guid.Empty || !ids.Add(id)) throw Invalid();
        var legacyIds = snapshot.Steps.SelectMany(row => row.Registration.Legacy).Select(row => row.StepId).ToArray();
        if (legacyIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != legacyIds.Length) throw Invalid();
    }

    /// <summary>正式Live只接受每個有效live項目已有pass證據；test演練仍允許產生尚待驗收的計畫。</summary>
    private static void CheckEvidence(OrganizationDefinition organization, string state, string tier, LiveEvidence evidence)
    {
        if (organization.Kind == "production" && state == "live" && tier == "live" &&
            (evidence.Result != "pass" || string.IsNullOrWhiteSpace(evidence.File) || evidence.Sha256.Length != 64 ||
             !evidence.Sha256.All(char.IsAsciiHexDigit) || string.IsNullOrWhiteSpace(evidence.At))) throw Invalid();
    }

    /// <summary>固定空欄位初始化，避免可執行操作意外帶有前次流程的設定或活動引用。</summary>
    private static SwitchOperation Operation(string kind, Guid id, string module) => new(kind, id, module, false, "", "", Array.Empty<WorkflowActivityReplacement>(), "");

    /// <summary>固定錯誤不保存來源XAML、欄位值或credential，由CLI統一回報。</summary>
    private static InvalidDataException Invalid() => new("Switch plan precondition failed.");
}

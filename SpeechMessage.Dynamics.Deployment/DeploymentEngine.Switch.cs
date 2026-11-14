/*
 * 檔案整體說明
 *
 * 目的與定位：提供階段五測試組織的模組互斥切換與舊輪生命週期。清單與盤點
 * 只提供候選，組織白名單、正式 lookup、D5-18 與新步驟模式均在當次操作驗證。
 * 主要功能與流程：先備份各組件；新輪只允許 Shadow（診斷維持 trace），舊輪須
 * 在同程序完成測試聯絡人核對與測試 token 注入，再於受控回呼內暫時啟用。
 * 使用邊界與維護：服務由主引擎租約擁有；無 static、快取或背景工作。舊輪無論
 * 成功、例外或取消都須停用並清除自建記錄上的 token。記錄刪除由 runner owner
 * 負責；本檔不能把任意呼叫者的 ID 或欄位視為已核定盤點，也不允許 CLI 傳 token。
 */
using System;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System.Runtime.ExceptionServices;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>盤點核定且由本次 runner 建立的 token 欄位，不保存秘密值或任意輸入路徑。</summary>
internal sealed record LegacyTokenTarget(string Entity, Guid Id, string Attribute);

/// <summary>
/// 程序內的舊輪核定範圍；只能由讀完盤點且擁有 seed 的 runner 建立，不能由 CLI JSON 反序列化。
/// 所有 ID 只活在該輪，TokenTargets 必須是 CreatedRecords 子集合；共用測試教會／會友不在清理範圍。
/// </summary>
internal sealed record LegacyRoundContext(Guid TestChurchId, Guid TestContactId,
    IReadOnlySet<(string Entity, Guid Id)> CreatedRecords, IReadOnlyList<LegacyTokenTarget> TokenTargets,
    bool HasUnconditionalHardcodedToken);

/// <summary>單一命令引擎的切換分部，所有狀態與 SDK 資源仍由呼叫端命令唯一擁有。</summary>
internal sealed partial class DeploymentEngine
{
    /// <summary>受控模組切換；legacy 只在同程序回呼期間啟用，呼叫結束必須停用。</summary>
    /// <param name="document">完整且已驗證的多組件清單。</param><param name="organization">唯一測試組織。</param>
    /// <param name="module">核定模組代號。</param><param name="target">enabled、legacy 或 off。</param>
    /// <param name="readLegacy">每次執行即時唯讀盤點的委派，不可回傳過期快照。</param>
    /// <param name="context">盤點及 seed owner 提供的舊輪範圍；其他模式可省略。</param>
    /// <param name="legacyBody">在舊步驟啟用期間執行第一輪，由本方法包住 finally。</param>
    /// <param name="cancellationToken">呼叫端取消，清理不沿用已取消的 token。</param>
    internal void SwitchModule(RegistrationDocument document, string organization, string module, string target,
        Func<LegacyRegistrationSnapshot> readLegacy, LegacyRoundContext? context = null,
        Action? legacyBody = null, CancellationToken cancellationToken = default)
    {
        ValidateOrganization(organization);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(readLegacy);
        cancellationToken.ThrowIfCancellationRequested();
        if (document.SchemaVersion != 2 || document.Assemblies is null || document.Assemblies.Count is < 1 or > 2 ||
            document.Assemblies.Select(assembly => assembly.AssemblyName).Distinct(StringComparer.Ordinal).Count() != document.Assemblies.Count ||
            target is not ("enabled" or "legacy" or "off")) throw InvalidSwitch();
        foreach (var assembly in document.Assemblies) RegistrationManifestLoader.Validate(assembly);
        var selected = document.Assemblies.SelectMany(assembly => assembly.Steps.Where(step => step.Module == module)
            .Select(step => (Assembly: assembly, Step: step))).ToArray();
        if (selected.Length == 0 && !document.Assemblies.Any(assembly => assembly.Activities.Any(activity => activity.Module == module)))
            throw InvalidSwitch();
        if (target == "enabled" && selected.Any(item => !AllowsShadowEnable(item.Step))) throw InvalidSwitch();
        var legacy = readLegacy();
        var legacyRows = ResolveLegacySwitchRows(document, module, legacy);
        if (target == "enabled" && (legacy.EnabledStepCount != 0 || legacy.EnabledWorkflowCount != 0)) throw InvalidSwitch();
        var rows = selected.Select(item => (item.Assembly, item.Step, Row: RegisteredStep(item.Assembly, item.Step))).ToArray();
        if (target == "enabled" && rows.Any(item => !NonSecretConfigurationMatches(item.Row, item.Step))) throw InvalidSwitch();
        if (target == "legacy")
        {
            if (context is null || legacyBody is null || context.HasUnconditionalHardcodedToken ||
                legacyRows.Steps.Length + legacyRows.Workflows.Length == 0 ||
                legacy.EnabledStepCount != 0 || legacy.EnabledWorkflowCount != 0 ||
                rows.Any(item => ReadOption(item.Row, "statecode") == 0)) throw InvalidSwitch();
            ValidateLegacyRoundContext(context);
        }
        foreach (var assembly in document.Assemblies)
            WriteExport(organization, Path.Combine(directory, "switch-before-" + assembly.AssemblyName + ".json"), assembly.AssemblyName);
        if (target == "legacy")
        {
            RunLegacyRound(legacyRows, rows.Select(item => item.Row.Id).ToArray(), context!, legacyBody!, cancellationToken);
            return;
        }
        if (target == "off")
        {
            var failures = new List<Exception>();
            DisableLegacyRows(legacyRows, failures);
            foreach (var item in rows)
                try { SetPluginStepEnabled(item.Row.Id, IsEnabled(item.Step, organization)); }
                catch (Exception cleanup) { failures.Add(cleanup); }
            if (failures.Count != 0) throw new AggregateException("Module cleanup failed.", failures);
            return;
        }
        try
        {
            foreach (var item in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SetPluginStepEnabled(item.Row.Id, target == "enabled" || IsEnabled(item.Step, organization));
            }
        }
        catch (Exception original)
        {
            var failures = new List<Exception> { original };
            foreach (var item in rows)
            {
                try { SetPluginStepEnabled(item.Row.Id, IsEnabled(item.Step, organization)); }
                catch (Exception cleanup) { failures.Add(cleanup); }
            }
            if (failures.Count > 1) throw new AggregateException("Module switch and cleanup failed.", failures);
            throw;
        }
    }
    /// <summary>依匯出的正式 lookup、清單 ID 與型別逐筆解析；流程若還引用其他活動則不允許單模組啟用。</summary>
    private static LegacySwitchRows ResolveLegacySwitchRows(RegistrationDocument document, string module, LegacyRegistrationSnapshot snapshot)
    {
        var steps = new HashSet<Guid>();
        var activityTypes = new HashSet<Guid>();
        foreach (var reference in document.Assemblies.SelectMany(assembly => assembly.Steps)
            .Where(step => step.Module == module).SelectMany(step => step.Legacy))
        {
            var row = snapshot.Steps.SingleOrDefault(step => step.Id == Guid.Parse(reference.StepId)) ?? throw InvalidSwitch();
            var type = snapshot.Types.SingleOrDefault(type => type.Id == row.TypeId) ?? throw InvalidSwitch();
            var assembly = snapshot.Assemblies.SingleOrDefault(assembly => assembly.Id == type.AssemblyId) ?? throw InvalidSwitch();
            if (type.TypeName != reference.Type || assembly.Name != reference.Assembly || !steps.Add(row.Id)) throw InvalidSwitch();
        }
        foreach (var reference in document.Assemblies.SelectMany(assembly => assembly.Activities)
            .Where(activity => activity.Module == module).SelectMany(activity => activity.Legacy))
        {
            var matches = snapshot.Types.Where(type => type.IsWorkflowActivity && type.TypeName == reference.Type &&
                snapshot.Assemblies.Any(assembly => assembly.Id == type.AssemblyId && assembly.Name == reference.Assembly)).ToArray();
            if (matches.Length != 1 || !activityTypes.Add(matches[0].Id)) throw InvalidSwitch();
        }
        var workflows = snapshot.Workflows.Where(workflow => workflow.ActivityTypeIds.Any(activityTypes.Contains)).ToArray();
        if (workflows.Any(workflow => workflow.ActivityTypeIds.Any(id => !activityTypes.Contains(id)))) throw InvalidSwitch();
        return new LegacySwitchRows(steps.OrderBy(id => id).ToArray(), workflows.Select(workflow => workflow.Id).OrderBy(id => id).ToArray());
    }

    /// <summary>
    /// D5-18：當下只允許核定測試 contact 留有 LINE ID，並驗證 parentcustomerid 是核定教會。
    /// token 只允許寫進本輪自建記錄的已盤點欄位；此方法不讀秘密、不寫任何記錄。
    /// </summary>
    private void ValidateLegacyRoundContext(LegacyRoundContext context)
    {
        if (context.TestChurchId == Guid.Empty || context.TestContactId == Guid.Empty || context.CreatedRecords is null ||
            context.CreatedRecords.Count is 0 or > 64 || context.TokenTargets is null || context.TokenTargets.Count is 0 or > 32)
            throw InvalidSwitch();
        var targets = new HashSet<(string Entity, Guid Id, string Attribute)>();
        foreach (var target in context.TokenTargets)
            if (target is null || target.Id == Guid.Empty || target.Id == context.TestChurchId || target.Id == context.TestContactId ||
                string.IsNullOrWhiteSpace(target.Entity) || string.IsNullOrWhiteSpace(target.Attribute) || target.Entity.Length > 128 || target.Attribute.Length > 128 ||
                target.Entity.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_') ||
                target.Attribute.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_') ||
                !context.CreatedRecords.Contains((target.Entity, target.Id)) || !targets.Add((target.Entity, target.Id, target.Attribute)))
                throw InvalidSwitch();
        var query = new QueryExpression("contact") { ColumnSet = new ColumnSet(false), TopCount = 2 };
        query.Criteria.AddCondition("new_lineid", ConditionOperator.NotNull);
        query.Criteria.AddCondition("new_lineid", ConditionOperator.NotEqual, "");
        var members = RequireUniqueRowsById(service.RetrieveMultiple(query).Entities, "D34 聯絡人");
        if (members.Length != 1 || members[0].Id != context.TestContactId) throw InvalidSwitch();
        var contact = service.Retrieve("contact", context.TestContactId, new ColumnSet("parentcustomerid"));
        var parent = contact.GetAttributeValue<EntityReference>("parentcustomerid");
        if (parent is null || parent.LogicalName != "account" || parent.Id != context.TestChurchId) throw InvalidSwitch();
    }

    /// <summary>同程序注入測試 token、暫時啟用及執行；finally 嘗試每個停用與清除，即使 caller 已取消亦收尾。</summary>
    private void RunLegacyRound(LegacySwitchRows rows, Guid[] newStepIds, LegacyRoundContext context, Action body, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var token = secret("TestChannelToken");
            if (string.IsNullOrWhiteSpace(token)) throw InvalidSwitch();
            foreach (var target in context.TokenTargets)
                service.Update(new Entity(target.Entity, target.Id) { [target.Attribute] = token });
            foreach (var id in rows.Steps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SetPluginStepEnabled(id, true);
            }
            foreach (var id in rows.Workflows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SetWorkflowEnabled(id, true);
            }
            body();
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception failure) { failures.Add(failure); }
        finally
        {
            DisableLegacyRows(rows, failures);
            foreach (var id in newStepIds)
                try { SetPluginStepEnabled(id, false); } catch (Exception cleanup) { failures.Add(cleanup); }
            foreach (var target in context.TokenTargets)
                try { service.Update(new Entity(target.Entity, target.Id) { [target.Attribute] = null }); }
                catch (Exception cleanup) { failures.Add(cleanup); }
        }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Legacy round or cleanup failed.", failures);
    }

    /// <summary>每個舊步驟與流程都獨立嘗試停用；任一失敗保留真實例外，不能假稱已清理。</summary>
    private void DisableLegacyRows(LegacySwitchRows rows, List<Exception> failures)
    {
        foreach (var id in rows.Workflows)
            try { SetWorkflowEnabled(id, false); } catch (Exception failure) { failures.Add(failure); }
        foreach (var id in rows.Steps)
            try { SetPluginStepEnabled(id, false); } catch (Exception failure) { failures.Add(failure); }
    }

    /// <summary>以正式 SetState 訊息啟停已核定流程 ID，不能以更新 XAML 或改名稱取得權限。</summary>
    private void SetWorkflowEnabled(Guid id, bool enabled) => service.Execute(new OrganizationRequest("SetState")
    {
        ["EntityMoniker"] = new EntityReference("workflow", id), ["State"] = new OptionSetValue(enabled ? 1 : 0),
        ["Status"] = new OptionSetValue(enabled ? 2 : 1)
    });

    /// <summary>本次已驗證的舊列 ID 集合；不含 SDK、token、設定或跨命令狀態。</summary>
    private sealed record LegacySwitchRows(Guid[] Steps, Guid[] Workflows);

    /// <summary>診斷只允許既有 trace，其他模組一律必須是 Shadow，不能以無 secure 值暗示安全。</summary>
    private static bool AllowsShadowEnable(StepRegistration step)
    {
        using var document = JsonDocument.Parse(step.UnsecureConfig);
        return document.RootElement.GetProperty("mode").GetString() == (step.Module == "diagnostics" ? "trace" : "Shadow") && step.SecureConfig.Length == 0;
    }

    /// <summary>只更新已核定正式步驟 ID 的狀態；不重建、改寫設定或解析秘密。</summary>
    private void SetPluginStepEnabled(Guid id, bool enabled) => service.Update(new Entity(StepLogicalName, id)
    {
        ["statecode"] = new OptionSetValue(enabled ? 0 : 1), ["statuscode"] = new OptionSetValue(enabled ? 1 : 2)
    });

    /// <summary>安全固定摘要；不含組織外資料、Token、設定或任意輸入值。</summary>
    private static InvalidDataException InvalidSwitch() => new("Module switch safety precondition failed.");

}

/*
檔案整體說明
目的與定位：P7受審計畫執行與回復owner，借用單命令SDK及組織寫入鎖，不自行連線或保留credential。
主要流程：同buffer驗檔SHA、重讀原況重建plan，先完整匯出，再每步write-ahead flush、SDK更新與完成flush。
故障／取消：任何已嘗試步驟均以None token反序回復，保留原例外；清理失敗聚合回報，不假稱已回off。
安全設定：只在寫入瞬間解析TokenKey，候選secure GUID先寫log；清空lookup後只刪無引用的精確secure列。
流程以SetState停用、精準改指再還原原啟用；rollback從log原字串恢復，KeepLegacy／Manual從不觸碰。
生命週期與隔離：所有stream由SwitchLog單次關閉、SDK由外層lease排空釋放；無static mutable、背景工作或跨組織快取。
驗證：SwitchExecutorTests注入送達後Create／Update／SetState故障及取消，讀回設定、狀態、secure列與XAML位元組。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
namespace SpeechMessage.Dynamics.Deployment.Switching;

/// <summary>命令區域同步執行器，所有借入依賴必須由外層先驗授權並持有獨占寫入鎖。</summary>
internal sealed class SwitchExecutor(IOrganizationService service, string directory, Func<CancellationToken, SwitchSnapshot> read,
    Action exportBefore, Func<string, string> secret)
{
    private const string StepTable = "sdkmessageprocessingstep";
    private const string SecureTable = "sdkmessageprocessingstepsecureconfig";
    private const string SecureLookup = "sdkmessageprocessingstepsecureconfigid";

    /// <summary>檔SHA及重算計畫完全相符才備份及寫入；caller取消後仍必須完成uncancelled回復。</summary>
    internal string Execute(SwitchPlan approved, string fileSha256, byte[] fileBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approved);
        ArgumentNullException.ThrowIfNull(fileBytes);
        if (fileBytes.Length > 4 * 1024 * 1024 || !string.Equals(Convert.ToHexString(SHA256.HashData(fileBytes)), fileSha256, StringComparison.OrdinalIgnoreCase) ||
            approved.Sha256 != SwitchPlanner.Hash(approved with { Sha256 = "" })) throw Invalid();
        // approved必須與核定buffer是同一份語意，不能由另一個DTO搭配合法檔hash冒充核定。
        var parsed = JsonSerializer.Deserialize<SwitchPlan>(fileBytes) ?? throw Invalid();
        if (SwitchPlanner.Hash(parsed) != SwitchPlanner.Hash(approved)) throw Invalid();
        cancellationToken.ThrowIfCancellationRequested();
        var before = read(cancellationToken);
        var actual = SwitchPlanner.Create(before, approved.TargetState);
        if (actual.Sha256 != approved.Sha256) throw Invalid();
        exportBefore();
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "switch-log.json");
        var log = SwitchLog.Persist(path, new SwitchLog(1, "switch-log", approved, before, Array.Empty<SwitchLogEntry>(), "", ""), true);
        try
        {
            for (var index = 0; index < approved.Operations.Count; index++)
            {
                var operation = approved.Operations[index];
                if (operation.Kind is "KeepLegacy" or "Manual") continue;
                cancellationToken.ThrowIfCancellationRequested();
                var entry = new SwitchLogEntry(index, Guid.NewGuid(), false, false) { BeforeState = CaptureState(operation, before) };
                log = SwitchLog.Persist(path, log with { Entries = Array.AsReadOnly(log.Entries.Append(entry).ToArray()) }, false);
                Apply(operation, entry, before, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                log = UpdateEntry(path, log, entry with { Completed = true, AfterState = CaptureState(operation, before) });
            }
            var after = read(cancellationToken);
            log = SwitchLog.Persist(path, log with { AfterFingerprint = after.Fingerprint }, false);
            return path;
        }
        catch (Exception original)
        {
            try { Restore(path, log, before.Organization.Name); }
            catch (Exception cleanup) { throw new AggregateException("Switch and rollback failed.", original, cleanup); }
            throw;
        }
    }

    /// <summary>只還原log列出的已嘗試操作；先核server組織與受管正式ID，不讀秘密內容或執行舊程式。</summary>
    internal void Rollback(string path, string organization)
    {
        var log = SwitchLog.Read(path);
        Restore(path, log, organization);
    }

    /// <summary>SDK操作之前後都查caller取消；工作流程state2與非核定動詞不能寫入。</summary>
    private void Apply(SwitchOperation operation, SwitchLogEntry entry, SwitchSnapshot before, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        switch (operation.Kind)
        {
            case "SetStepConfiguration":
                SetConfiguration(operation.Id, operation.Configuration, operation.SecureConfiguration.Length != 0, entry.SecureCandidateId, token);
                break;
            case "EnableStep": SetState(StepTable, operation.Id, true); break;
            case "DisableStep": case "DisableLegacyStep": SetState(StepTable, operation.Id, false); break;
            case "RepointWorkflow":
                var workflow = before.Workflows.Single(row => row.Id == operation.Id);
                SetState("workflow", operation.Id, false);
                token.ThrowIfCancellationRequested();
                service.Update(new Entity("workflow", operation.Id) { ["xaml"] = WorkflowRepointer.Rewrite(workflow.Xaml, operation.Replacements) });
                token.ThrowIfCancellationRequested();
                if (workflow.State == 1) SetState("workflow", operation.Id, true);
                break;
            default: throw Invalid();
        }
        token.ThrowIfCancellationRequested();
    }

    /// <summary>反向回復所有attempted項目，讀現況使用None，不能因caller已取消略過清理或讀回。</summary>
    private void Restore(string path, SwitchLog log, string organization)
    {
        var current = read(CancellationToken.None);
        if (log.Plan.Organization != organization || current.Organization.Name != organization ||
            current.ServerOrganizationId != log.Plan.ServerOrganizationId || log.Before.ServerOrganizationId != log.Plan.ServerOrganizationId ||
            log.Before.Organization.Name != organization || log.Before.ManifestSha256 != current.ManifestSha256 ||
            log.Before.CatalogSha256 != current.CatalogSha256 || log.Plan.ManifestSha256 != log.Before.ManifestSha256 ||
            log.Plan.CatalogSha256 != log.Before.CatalogSha256 || log.Plan.Fingerprint != log.Before.Fingerprint || log.Plan.Sha256 != SwitchPlanner.Hash(log.Plan with { Sha256 = "" }) ||
            log.Entries.Count > log.Plan.Operations.Count || log.Entries.Select(row => row.Index).Distinct().Count() != log.Entries.Count) throw Invalid();
        var failures = new List<Exception>();
        foreach (var entry in log.Entries.Reverse().ToArray())
        {
            if (entry.Restored) continue;
            try
            {
                if (entry.Index < 0 || entry.Index >= log.Plan.Operations.Count) throw Invalid();
                var operation = log.Plan.Operations[entry.Index];
                switch (operation.Kind)
                {
                    case "SetStepConfiguration":
                        var step = log.Before.Steps.Single(row => row.Id == operation.Id);
                        RequireStep(current, step);
                        SetConfiguration(step.Id, step.Configuration, step.SecureId.HasValue, step.SecureId ?? entry.SecureCandidateId, CancellationToken.None);
                        DeleteUnreferencedSecure(entry.SecureCandidateId);
                        break;
                    case "EnableStep": case "DisableStep":
                        var previous = log.Before.Steps.Single(row => row.Id == operation.Id);
                        RequireStep(current, previous);
                        // 設定尚未還原前不能提早啟用原Live步驟；前置DisableStep在反序最後才恢復原狀態。
                        SetState(StepTable, previous.Id, operation.Kind == "EnableStep" ? false : previous.Enabled);
                        break;
                    case "DisableLegacyStep":
                        var old = log.Before.LegacySteps.Single(row => row.Id == operation.Id);
                        if (!current.LegacySteps.Any(row => row.Id == old.Id && row.Assembly == old.Assembly && row.Type == old.Type)) throw Invalid();
                        SetState(StepTable, old.Id, old.Enabled);
                        break;
                    case "RepointWorkflow":
                        var workflow = log.Before.Workflows.Single(row => row.Id == operation.Id);
                        if (!current.Workflows.Any(row => row.Id == workflow.Id)) throw Invalid();
                        SetState("workflow", workflow.Id, false);
                        service.Update(new Entity("workflow", workflow.Id) { ["xaml"] = workflow.Xaml });
                        if (workflow.State == 1) SetState("workflow", workflow.Id, true);
                        break;
                    default: throw Invalid();
                }
                log = UpdateEntry(path, log, entry with { Restored = true });
            }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count != 0) throw new AggregateException("Switch rollback failed.", failures);
        var restored = read(CancellationToken.None);
        if (restored.Fingerprint != log.Before.Fingerprint) throw Invalid();
    }

    /// <summary>逐步讀回實際狀態；受管設定必須仍符合清單兩模式模板，舊步驟不讀configuration以免保存秘密。</summary>
    private SwitchOperationState CaptureState(SwitchOperation operation, SwitchSnapshot before)
    {
        var workflow = operation.Kind == "RepointWorkflow";
        var managed = before.Steps.SingleOrDefault(row => row.Id == operation.Id);
        var table = workflow ? "workflow" : StepTable;
        var columns = workflow ? new ColumnSet("statecode", "xaml") : managed is not null
            ? new ColumnSet("statecode", "configuration", SecureLookup) : new ColumnSet("statecode");
        var row = service.Retrieve(table, operation.Id, columns);
        var state = row.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? throw Invalid();
        if (state is not (0 or 1)) throw Invalid();
        var configuration = "";
        if (managed is not null)
        {
            configuration = row.GetAttributeValue<string>("configuration") ?? "";
            if (!SwitchPlanner.ConfigurationEquals(configuration, SwitchPlanner.Configuration(managed.Registration.UnsecureConfig, "Shadow")) &&
                !SwitchPlanner.ConfigurationEquals(configuration, SwitchPlanner.Configuration(managed.Registration.UnsecureConfig, "Live"))) throw Invalid();
        }
        var xaml = workflow ? row.GetAttributeValue<string>("xaml") ?? throw Invalid() : "";
        if (Encoding.UTF8.GetByteCount(xaml) > 1024 * 1024) throw Invalid();
        return new SwitchOperationState(state, configuration, managed is not null && row.GetAttributeValue<EntityReference>(SecureLookup) is not null,
            workflow ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xaml))) : "");
    }

    /// <summary>只允回復仍屬受管範圍的原正式步驟ID，不以名稱或序號尋找替代列。</summary>
    private static void RequireStep(SwitchSnapshot current, SwitchStep previous)
    {
        var present = current.Steps.SingleOrDefault(row => row.Id == previous.Id) ?? throw Invalid();
        if (RegistrationManifestLoader.StepKey(present.Registration) != RegistrationManifestLoader.StepKey(previous.Registration) ||
            !SwitchPlanner.ConfigurationEquals(previous.Configuration, SwitchPlanner.Configuration(present.Registration.UnsecureConfig, "Shadow")) &&
            !SwitchPlanner.ConfigurationEquals(previous.Configuration, SwitchPlanner.Configuration(present.Registration.UnsecureConfig, "Live"))) throw Invalid();
    }

    /// <summary>更新設定前確保新步驟停用，secure候選先已記錄；只刪無引用精確ID，金鑰不離開SDK寫入堆疊。</summary>
    private void SetConfiguration(Guid id, string configuration, bool needsSecure, Guid candidate, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var row = service.Retrieve(StepTable, id, new ColumnSet("statecode", SecureLookup));
        if (row.GetAttributeValue<OptionSetValue>("statecode")?.Value != 1) throw Invalid();
        var previous = row.GetAttributeValue<EntityReference>(SecureLookup)?.Id;
        Guid? secure = null;
        if (needsSecure)
        {
            if (previous.HasValue) secure = previous;
            else
            {
                var value = secret("TokenKey");
                if (string.IsNullOrWhiteSpace(value) || value.Length > 8192) throw Invalid();
                token.ThrowIfCancellationRequested();
                // 候選ID在write-ahead已flush；Create送達後throw仍由rollback精確清理。
                if (!SecureExists(candidate))
                {
                    var created = service.Create(new Entity(SecureTable, candidate) { ["secureconfig"] = value });
                    if (created != candidate) throw Invalid();
                }
                secure = candidate;
            }
        }
        token.ThrowIfCancellationRequested();
        service.Update(new Entity(StepTable, id) { ["configuration"] = configuration,
            [SecureLookup] = secure.HasValue ? new EntityReference(SecureTable, secure.Value) : null });
        if (previous.HasValue && previous != secure) DeleteUnreferencedSecure(previous.Value);
        token.ThrowIfCancellationRequested();
    }

    /// <summary>精確查secure ID但不讀明文；上限2並拒續頁／重複，不能把不完整查詢當不存在。</summary>
    private bool SecureExists(Guid id)
    {
        var query = new QueryExpression(SecureTable) { ColumnSet = new ColumnSet(false), TopCount = 2 };
        query.Criteria.AddCondition(SecureLookup, ConditionOperator.Equal, id);
        var rows = service.RetrieveMultiple(query);
        if (rows.MoreRecords || rows.Entities.Count > 1 || rows.Entities.Any(row => row.Id != id)) throw Invalid();
        return rows.Entities.Count == 1;
    }

    /// <summary>引用存在則由該步驟擁有，不可刪除；查詢完整且候選存在才刪除，不讀secure內容。</summary>
    private void DeleteUnreferencedSecure(Guid id)
    {
        var query = new QueryExpression(StepTable) { ColumnSet = new ColumnSet(false), TopCount = 1 };
        query.Criteria.AddCondition(SecureLookup, ConditionOperator.Equal, id);
        var rows = service.RetrieveMultiple(query);
        if (rows.Entities.Count != 0 || rows.MoreRecords) return;
        if (SecureExists(id)) service.Delete(SecureTable, id);
    }

    /// <summary>流程與步驟以正式SetState設定相容status；流程不透過改xaml暗中啟用。</summary>
    private void SetState(string table, Guid id, bool enabled) => service.Execute(new OrganizationRequest("SetState")
    {
        ["EntityMoniker"] = new EntityReference(table, id),
        ["State"] = new OptionSetValue(table == "workflow" ? enabled ? 1 : 0 : enabled ? 0 : 1),
        ["Status"] = new OptionSetValue(table == "workflow" ? enabled ? 2 : 1 : enabled ? 1 : 2)
    });

    /// <summary>發布全份新entry集合再flush，舊log不可原地修改而形成半成品。</summary>
    private static SwitchLog UpdateEntry(string path, SwitchLog log, SwitchLogEntry entry) =>
        SwitchLog.Persist(path, log with { Entries = Array.AsReadOnly(log.Entries.Select(row => row.Index == entry.Index ? entry : row).ToArray()) }, false);

    /// <summary>固定錯誤不含設定或XAML；原SDK例外由外層共用告警處理。</summary>
    private static InvalidDataException Invalid() => new("Switch execution or restoration validation failed.");
}

/*
檔案整體說明
目的與定位：P7切換完整現況唯讀投影，借用已授權SDK，將正式server ID、兩DLL、步驟、舊metadata及流程納入同一候選。
主要流程：WhoAmI核非空組織ID，沿原DeploymentEngine驗註冊與images，僅容許state/config差異；穩定身份找唯一step。
設定只接受manifest的Shadow/Live兩種模板，未知內容拒絕，不把可能含秘密的unsecure原文放log；secure只查lookup。
舊資料沿LegacyRegistrationExporter有界分頁；流程完整原字串保留至命令／本機log，總16MiB，超單份1MiB列Manual。
資源與隔離：不建立連線、不擁有SDK、不快取；同步呼叫前後檢查取消，client逾時由外層lease設定。所有純值集合唯讀發布。
驗證：SwitchSnapshotReaderTests用合成SDK驗Live差異非DLL差異、原XAML指紋及未知設定拒絕；不宣稱任何實機結果。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
namespace SpeechMessage.Dynamics.Deployment.Switching;

/// <summary>完整metadata取得與純值發布入口，不建立憑證或跨組織mutable狀態。</summary>
internal static class SwitchSnapshotReader
{
    /// <summary>全部唯讀完成及完整性檢查通過才返回；拒絕缺DLL、註冊缺漏、未知設定及不完整分頁。</summary>
    internal static SwitchSnapshot Read(IOrganizationService service, RegistrationDocument document, IReadOnlyList<AssemblyImage> images,
        OrganizationDefinition organization, string manifestHash, string catalogHash, string directory, OrganizationCatalog catalog, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var allowed = catalog.Get(organization.Name);
        if (allowed.Name != organization.Name || allowed.Host != organization.Host || allowed.Kind != organization.Kind ||
            allowed.SwitchState != organization.SwitchState || !allowed.TierOverrides.SequenceEqual(organization.TierOverrides) ||
            document.Assemblies.Count is < 1 or > 2 || images.Count != document.Assemblies.Count) throw Invalid();
        var response = service.Execute(new OrganizationRequest("WhoAmI"));
        if (!response.Results.TryGetValue("OrganizationId", out var value) || value is not Guid serverId || serverId == Guid.Empty) throw Invalid();
        token.ThrowIfCancellationRequested();
        var reader = new LegacyRegistrationExporter.MetadataReader(service, 500, 10000, 100, token);
        var assemblies = new List<SwitchAssembly>();
        var steps = new List<SwitchStep>();
        var activities = new List<SwitchActivity>();
        var fingerprints = new List<string>();
        foreach (var manifest in document.Assemblies.OrderBy(row => row.AssemblyName, StringComparer.Ordinal))
        {
            RegistrationManifestLoader.Validate(manifest);
            var image = images.Single(row => row.Name == manifest.AssemblyName);
            var engine = new DeploymentEngine(service, directory, TimeProvider.System, _ => throw Invalid(), catalog);
            var registration = engine.Plan(manifest, image, organization.Name);
            if (registration.Operations.Any(operation => operation.Kind is not ("UpdateStepConfiguration" or "UpdateStepState"))) throw Invalid();
            fingerprints.Add(registration.Fingerprint);
            var assembly = reader.Read("pluginassembly", ["name", "content"], "name", [manifest.AssemblyName]).SingleOrDefault() ?? throw Invalid();
            var content = assembly.GetAttributeValue<string>("content") ?? "";
            if (content.Length > 22369624) throw Invalid();
            string remoteHash;
            try { remoteHash = Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(content))); }
            catch (FormatException) { throw Invalid(); }
            assemblies.Add(new SwitchAssembly(image.Name, image.Sha256, remoteHash));
            if (remoteHash != image.Sha256) throw Invalid();
            var types = reader.Related("plugintype", ["typename", "pluginassemblyid"], "pluginassemblyid", [assembly.Id]).ToArray();
            if (types.GroupBy(row => row.GetAttributeValue<string>("typename"), StringComparer.Ordinal).Any(group => group.Count() > 1)) throw Invalid();
            foreach (var item in manifest.Steps.Where(row => row.Module != "diagnostics"))
            {
                var type = types.Single(row => row.GetAttributeValue<string>("typename") == item.Type);
                var messages = reader.Read("sdkmessage", ["name"], "name", [item.Message]).ToArray();
                if (messages.Length != 1) throw Invalid();
                var filters = reader.Related("sdkmessagefilter", ["sdkmessageid", "primaryobjecttypecode"], "sdkmessageid", [messages[0].Id])
                    .Where(row => row.GetAttributeValue<string>("primaryobjecttypecode") == item.Entity).ToArray();
                if (filters.Length != 1) throw Invalid();
                var candidates = reader.Related("sdkmessageprocessingstep", ["eventhandler", "sdkmessageid", "sdkmessagefilterid", "stage", "mode", "rank", "statecode", "configuration", "filteringattributes", "sdkmessageprocessingstepsecureconfigid"], "eventhandler", [type.Id])
                    .Where(row => Lookup(row, "sdkmessageid", "sdkmessage") == messages[0].Id && Lookup(row, "sdkmessagefilterid", "sdkmessagefilter") == filters[0].Id &&
                        Option(row, "stage") == (item.Stage switch { "PreValidation" => 10, "PreOperation" => 20, "PostOperation" => 40, _ => throw Invalid() }) &&
                        Option(row, "mode") == (item.Mode == "Synchronous" ? 0 : 1) && row.GetAttributeValue<int>("rank") == item.Rank).ToArray();
                if (candidates.Length != 1) throw Invalid();
                var row = candidates[0];
                var state = Option(row, "statecode");
                if (state is not (0 or 1) || (row.GetAttributeValue<string>("filteringattributes") ?? "") != string.Join(",", item.FilteringAttributes)) throw Invalid();
                var configuration = row.GetAttributeValue<string>("configuration") ?? "";
                if (!SwitchPlanner.ConfigurationEquals(configuration, SwitchPlanner.Configuration(item.UnsecureConfig, "Shadow")) &&
                    !SwitchPlanner.ConfigurationEquals(configuration, SwitchPlanner.Configuration(item.UnsecureConfig, "Live"))) throw Invalid();
                var secure = row.GetAttributeValue<EntityReference>("sdkmessageprocessingstepsecureconfigid");
                if (secure is not null && (secure.Id == Guid.Empty || secure.LogicalName != "sdkmessageprocessingstepsecureconfig")) throw Invalid();
                steps.Add(new SwitchStep(row.Id, item, state == 0, configuration, secure?.Id));
            }
            var identity = image.Name + ", Version=" + image.Version + ", Culture=neutral, PublicKeyToken=" + (image.PublicKeyToken.Length == 0 ? "null" : image.PublicKeyToken);
            activities.AddRange(manifest.Activities.Select(item => new SwitchActivity(item, identity)));
        }
        var legacy = LegacyRegistrationExporter.ExportForSwitch(service, token);
        var assemblyMap = legacy.Assemblies.ToDictionary(row => row.Id);
        var typeMap = legacy.Types.ToDictionary(row => row.Id);
        var oldSteps = legacy.Steps.Select(row => new SwitchLegacyStep(row.Id, assemblyMap[typeMap[row.TypeId].AssemblyId].Name,
            typeMap[row.TypeId].TypeName, row.Enabled)).OrderBy(row => row.Id).ToArray();
        var oldActivities = legacy.Types.Where(row => row.IsWorkflowActivity).Select(row => new WorkflowActivityReference(assemblyMap[row.AssemblyId].Name, row.TypeName)).ToArray();
        var workflows = new List<SwitchWorkflow>();
        var remaining = 16 * 1024 * 1024;
        foreach (var row in reader.Read("workflow", ["statecode", "xaml"], "type", [1]))
        {
            var xaml = row.GetAttributeValue<string>("xaml") ?? "";
            if (string.IsNullOrWhiteSpace(xaml)) continue;
            remaining -= Encoding.UTF8.GetByteCount(xaml);
            if (remaining < 0) throw Invalid();
            // 同時保留已改指的新流程；rollback後原況指紋才能證明逐位元組相同。
            // 其他平台／標準流程亦只讀metadata與hash，不執行或改寫。
            workflows.Add(new SwitchWorkflow(row.Id, Option(row, "statecode"), xaml));
        }
        token.ThrowIfCancellationRequested();
        var fingerprint = SwitchPlanner.Hash(new { ServerId = serverId, Managed = fingerprints, Legacy = legacy,
            Workflows = workflows.OrderBy(row => row.Id).Select(row => new { row.Id, row.State, Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.Xaml))) }).ToArray() });
        return new SwitchSnapshot(organization, serverId, manifestHash, catalogHash, fingerprint, assemblies.AsReadOnly(), steps.AsReadOnly(), activities.AsReadOnly(),
            Array.AsReadOnly(oldSteps), Array.AsReadOnly(workflows.OrderBy(row => row.Id).ToArray()), Array.AsReadOnly(oldActivities)) { RegisteredLegacyAssemblies = Array.AsReadOnly(legacy.Assemblies.Select(row => row.Name).Order(StringComparer.Ordinal).ToArray()) };
    }

    /// <summary>metadata只接受正式option或整數，缺少state等必要值不可默認停用。</summary>
    private static int Option(Entity row, string name) => row.Attributes.TryGetValue(name, out var value)
        ? value switch { OptionSetValue option => option.Value, int number => number, _ => throw Invalid() } : throw Invalid();

    /// <summary>lookup必須有正確logical name與正式ID，不讀顯示Name或用名稱推導父身分。</summary>
    private static Guid Lookup(Entity row, string name, string table) => row.GetAttributeValue<EntityReference>(name) is { } reference &&
        reference.LogicalName == table && reference.Id != Guid.Empty ? reference.Id : throw Invalid();

    /// <summary>固定錯誤不含原始設定、workflow或metadata內容。</summary>
    private static InvalidDataException Invalid() => new("Switch snapshot metadata is invalid or incomplete.");
}

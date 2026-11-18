/*
檔案整體說明
目的與定位：P7 三個工具的主 CLI 分部，沿共同 ProductionGuard，再嚴格核選項、固定路徑及同組織寫鎖。
主要流程：observe 包唯讀 SDK 並發布純計數；church-defaults 只讀計畫或同 buffer 核 SHA 後套用；
live-check 只在 test 組織，按即時 metadata 核啟用及模式、D34、準備情境後交唯一 runner。
安全與隔離：每命令獨占 lease，SDK 歸還後才釋放寫鎖；manifest/catalog handle 全程只讀持有，
檔案固定在本組織 artifacts。計畫有界、拒未知／重複欄位，不接受命令列秘密或任意輸出位置。
資源與失敗：HTTP safety owner 在 SDK 前 Dispose，所有真失敗在 lease 釋放後交共同告警，
seed 識別包裝會還原原始例外供 reporter 分類；安全摘要可攜帶固定 alias 脈絡，不含欄位值。
正常 caller 取消回130；Live 只用本輪正式 ID 清理，不改任何步驟。
驗證方式：指定核心測試與離線 CLI 循環；本檔的成功不取代每個型別的 Live 實機證據。
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Deployment.Live;
using SpeechMessage.Dynamics.Deployment.Shadow;
using SpeechMessage.Dynamics.Deployment.Switching;
namespace SpeechMessage.Dynamics.Deployment;

/// <summary>由主 CLI 授權後進入的三工具分部，不另建可繞過白名單的公開入口。</summary>
internal static partial class DeploymentCli
{
    private static readonly string[] DefaultsEntityNames = { "account" };
    /// <summary>完整解析先於 connector；未知、重複、缺值與多餘選項皆拒絕。</summary>
    private static int RunLiveCommands(string[] args, OrganizationDefinition target, OrganizationCatalog catalog,
        IOrganizationConnector connector, TextWriter output, Action<Exception> report, string? artifactRoot,
        Func<string, string>? secretReader, TimeSpan? writeLockTimeout, CancellationToken token)
    {
        try
        {
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            var apply = false;
            for (var index = 1; index < args.Length; index++)
            {
                var key = args[index];
                if (key == "--apply") { if (apply || args[0] != "church-defaults") return 2; apply = true; continue; }
                if (key is not ("--org" or "--module" or "--scenario-root" or "--since" or "--plan" or "--plan-sha256") ||
                    index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].StartsWith("--", StringComparison.Ordinal) ||
                    !options.TryAdd(key, args[++index])) return 2;
            }
            if (!options.TryGetValue("--org", out var organization) || organization != target.Name) return 2;
            bool Exactly(params string[] keys) => options.Count == keys.Length && keys.All(options.ContainsKey);
            DateTimeOffset since = default;
            var valid = args[0] switch
            {
                "observe" => Exactly("--org", "--since") && options["--since"].EndsWith('Z') &&
                    DateTimeOffset.TryParse(options["--since"], CultureInfo.InvariantCulture, DateTimeStyles.None, out since) && since.Offset == TimeSpan.Zero,
                "church-defaults" => apply ? Exactly("--org", "--plan", "--plan-sha256") &&
                    options["--plan-sha256"].Length == 64 && options["--plan-sha256"].All(char.IsAsciiHexDigit) : Exactly("--org"),
                "live-check" => target.Kind == "test" && Exactly("--org", "--module", "--scenario-root"),
                _ => false
            };
            if (!valid) return 2;
            var repository = FindRepositoryRoot();
            var root = Path.GetFullPath(Path.Combine(artifactRoot ?? Path.Combine(repository, "artifacts", "d365"), target.Name));
            if (apply && !IsContainedPath(root, options["--plan"], out _)) return 2;
            IReadOnlyList<ShadowFileCase>? files = null;
            if (args[0] == "live-check")
            {
                var folder = Path.GetFileName(Path.TrimEndingDirectorySeparator(options["--scenario-root"]));
                var module = options["--module"];
                if (!(module == folder || module == "lessons" && folder is "lessonsequipment" or "lessonslink" or "lessonsprogression" or "lessonssigning")) return 2;
                var expected = Path.GetFullPath(Path.Combine(repository, "SpeechMessageProducts.GoodShepherd.Core.Tests", "Golden", "Legacy", folder));
                var acceptanceRoot = Path.GetFullPath(Path.Combine(repository, LiveText.Get("AcceptanceFolder"),
                    LiveText.Get("ScenarioFolder"), module));
                if (!IsContainedPath(repository, options["--scenario-root"], out var full)) return 2;
                var normalizedFull = Path.TrimEndingDirectorySeparator(full);
                var acceptance = string.Equals(normalizedFull, Path.TrimEndingDirectorySeparator(acceptanceRoot), StringComparison.OrdinalIgnoreCase);
                if (!acceptance && !string.Equals(normalizedFull, Path.TrimEndingDirectorySeparator(expected), StringComparison.OrdinalIgnoreCase)) return 2;
                files = ShadowScenarioFiles.LoadLive(repository, folder, acceptance);
            }
            using var manifestHandle = OpenSource(Path.Combine(repository, "d365", "registration", "plugins.registration.json"));
            using var catalogHandle = OpenSource(Path.Combine(repository, "d365", "registration", "organizations.json"));
            using var writeLock = apply || args[0] == "live-check" ? OrganizationWriteLock.Acquire(root, writeLockTimeout ?? TimeSpan.FromMinutes(5), token) : null;
            using var lease = connector.Connect(target.Name);
            ProductionGuard.VerifyServer(lease.Service, target, token);
            var operation = CreateOperationDirectory(root);
            if (args[0] == "church-defaults")
            {
                Guid Server()
                {
                    token.ThrowIfCancellationRequested();
                    var response = lease.Service.Execute(new RetrieveCurrentOrganizationRequest()) as RetrieveCurrentOrganizationResponse;
                    if (response?.Detail is not { } detail || detail.UniqueName != target.Name || detail.OrganizationId == Guid.Empty) throw InvalidLive();
                    return detail.OrganizationId;
                }
                var engine = new ChurchDefaults(lease.Service, operation, Server);
                if (!apply)
                {
                    var plan = engine.Plan(target.Name, token);
                    WriteDocumentFile(operation, "church-defaults-plan.json", JsonSerializer.Serialize(plan, DocumentJson));
                    WriteDocumentFile(operation, "church-defaults-plan.md", LiveText.Get("Defaults") + plan.Entries.Count.ToString(CultureInfo.InvariantCulture) + "\r\n" +
                        string.Join("\r\n", plan.Entries.Select(row => row.Id.ToString("D") + " " + string.Join(",", row.Fields))) + "\r\nSHA-256: " + FileHash(Path.Combine(operation, "church-defaults-plan.json")));
                }
                else
                {
                    using var stream = OpenSource(options["--plan"]);
                    var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
                    if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), options["--plan-sha256"], StringComparison.OrdinalIgnoreCase)) throw InvalidLive();
                    using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
                    Schema.SchemaFiles.RejectDuplicateProperties(json.RootElement);
                    var plan = JsonSerializer.Deserialize<ChurchDefaultsPlan>(bytes, DocumentJson) ?? throw InvalidLive();
                    if (plan.Organization != target.Name) throw InvalidLive();
                    ValidateDefaultsMetadata(lease.Service, token);
                    engine.Apply(plan, plan.Sha256, token);
                }
                output.WriteLine("Church defaults completed; existing values were preserved."); return 0;
            }
            var service = args[0] == "observe" ? new ReadOnlyOrganizationService(lease.Service) : lease.Service;
            var document = RegistrationManifestLoader.LoadDocument(repository);
            var images = document.Assemblies.Select(assembly => AssemblyCatalog.Read(Path.Combine(repository, "artifacts", "plugins", assembly.AssemblyName + ".dll"), assembly)).ToArray();
            var snapshot = SwitchSnapshotReader.Read(service, document, images, target,
                Convert.ToHexString(SHA256.HashData(manifestHandle)), Convert.ToHexString(SHA256.HashData(catalogHandle)), operation, catalog, token);
            if (args[0] == "observe")
            {
                var mutual = true;
                try { _ = SwitchPlanner.Create(snapshot, "off"); } catch (InvalidDataException) { mutual = false; }
                var workflows = new LegacyRegistrationExporter.MetadataReader(service, 500, 20000, 100, token)
                    .Read("workflow", ["xaml"], "type", [1]).Where(row => !string.IsNullOrWhiteSpace(row.GetAttributeValue<string>("xaml"))).Where(row =>
                        WorkflowRepointer.ReadReferences(row.GetAttributeValue<string>("xaml") ?? "").Any(reference =>
                            snapshot.Activities.Any(activity => reference.Type == activity.Registration.Type))).Select(row => row.Id).ToArray();
                var observed = ObservationReader.Read(service, target.Kind, snapshot.ServerOrganizationId, since, DateTimeOffset.UtcNow,
                    snapshot.Steps.Select(row => new ObservationStep(row.Id, row.Registration.Type)).ToArray(), workflows, mutual, snapshot.Activities.Select(row => row.Registration.Type).ToArray(), token);
                WriteDocumentFile(operation, "observe.json", JsonSerializer.Serialize(observed, DocumentJson));
                WriteDocumentFile(operation, "observe.md", ObservationMarkdown(observed));
                output.WriteLine("Observation counts written; unavailable trace is not zero."); return observed.NoNewErrors ? 0 : 1;
            }
            return RunPreparedLive(target, options["--module"], files!, repository, operation, lease.Service, snapshot, output, secretReader, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { output.WriteLine("Command cancelled."); return 130; }
        catch (Exception failure)
        {
            try { report(ExceptionForReporting(failure)); } catch { WriteFixedError(output); }
            WriteSupplementalFailureDiagnostics(failure, output);
            output.WriteLine("Live tooling command failed; sensitive details suppressed."); return 1;
        }
    }
    /// <summary>整批現況核對及情境準備成功後才允許任何 seed，dormant 明列未檢查。</summary>
    private static int RunPreparedLive(OrganizationDefinition target, string module, IReadOnlyList<ShadowFileCase> files,
        string repository, string operation, IOrganizationService service, SwitchSnapshot snapshot,
        TextWriter output, Func<string, string>? secretReader, CancellationToken token)
    {
        _ = SwitchPlanner.Create(snapshot, "off");
        if (snapshot.LegacySteps.Any(row => row.Enabled) || snapshot.Workflows.Any(row => row.State != 0)) throw InvalidLive();
        using var safety = new LiveSafety(service, name => ResolveRequiredSecret(target.Name, name, secretReader));
        var identity = safety.Verify(token);
        var selected = files.Where(file => file.LiveShadow).ToArray();
        if (selected.Length == 0) throw InvalidLive();
        var prepared = new Dictionary<LiveCheckScenario, ShadowSdkPreparedScenario>();
        var activities = new Dictionary<LiveCheckScenario, LiveActivityTemplate>();
        var scenarios = new List<LiveCheckScenario>();
        foreach (var file in selected)
        {
            token.ThrowIfCancellationRequested();
            var steps = snapshot.Steps.Where(row => row.Registration.Module == module &&
                (row.Registration.Type == file.Entrypoint || row.Registration.Legacy.Any(old => old.Type == file.Entrypoint)) &&
                !file.IsActivity && row.Registration.Message == file.Trigger.GetProperty("message").GetString() &&
                row.Registration.Entity == file.Trigger.GetProperty("entity").GetString()).ToArray();
            var activityMatches = snapshot.Activities.Where(row => row.Registration.Module == module && file.IsActivity &&
                (row.Registration.Type == file.Entrypoint || row.Registration.Legacy.Any(old => old.Type == file.Entrypoint))).ToArray();
            if (steps.Length + activityMatches.Length != 1) throw InvalidLive();
            var step = steps.SingleOrDefault();
            var activity = activityMatches.SingleOrDefault();
            var type = step?.Registration.Type ?? activity!.Registration.Type;
            var tier = SwitchTarget.EffectiveTier(target, module, type, step?.Registration.Live?.Tier ?? activity!.Registration.Live!.Tier);
            var mode = step is null ? "Live" : JsonDocumentMode(step.Configuration);
            var empty = new ShadowSnapshot(false, Array.Empty<ShadowRow>());
            var scenario = new LiveCheckScenario(file.Name, type, module, tier, step?.Enabled ?? true, mode,
                file.ExpectedLineCalls.EnumerateArray().Sum(call => call.GetProperty("messageCount").GetInt32()), empty,
                Array.Empty<ShadowObservation>(), new HashSet<string>(file.Volatile, StringComparer.Ordinal));
            if (tier == "dormant") { scenarios.Add(scenario); continue; }
            if (!scenario.Enabled || mode != "Live") throw InvalidLive();
            var churchAlias = file.Church.GetProperty("alias").GetString()!;
            var contacts = file.Seed.EnumerateArray().Where(row => row.GetProperty("entity").GetString() == "contact" &&
                !row.GetProperty("fields").EnumerateObject().Any()).ToArray();
            if (contacts.Length != 1) throw InvalidLive();
            var names = file.Seed.EnumerateArray().Concat(file.Observe.EnumerateArray())
                .Concat(file.ExpectedWrites.EnumerateArray()).Select(row => row.GetProperty("entity").GetString()!).Distinct(StringComparer.Ordinal).ToArray();
            var metadata = new ShadowSdkMetadata(service, names, token);
            LiveActivityTemplate? template = null;
            if (activity is not null)
            {
                // 非空活動輸入須先製作並核對專用樣板，不能把檔案參數默默忽略。
                if (file.Trigger.GetProperty("inputs").EnumerateObject().Any()) throw InvalidLive();
                var workflows = new LegacyRegistrationExporter.MetadataReader(service, 500, 10000, 100, token)
                    .Read("workflow", ["xaml", "statecode", "primaryentity"], "type", [1]).Where(row =>
                    row.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0 &&
                    row.GetAttributeValue<string>("primaryentity") == file.Seed.EnumerateArray().Single(seed => seed.GetProperty("alias").GetString() == "record").GetProperty("entity").GetString())
                    .Select(row => (Row: row, References: WorkflowRepointer.ReadReferences(row.GetAttributeValue<string>("xaml") ?? "")))
                    .Where(item => item.References.Count != 0 && item.References.All(reference => reference.Type == type ||
                        activity.Registration.Legacy.Any(old => WorkflowRepointer.SameReference(reference, old.Assembly, old.Type))))
                    .OrderBy(item => item.Row.Id).ToArray();
                if (workflows.Length == 0) throw InvalidLive();
                var chosen = workflows[0];
                var reference = chosen.References[0];
                template = new(chosen.Row, new(reference.Assembly, reference.Type, activity.QualifiedAssembly, type));
            }
            var executionId = step?.Id ?? template!.Source.Id;
            var binding = new ShadowSdkBinding(file.Entrypoint, churchAlias, contacts[0].GetProperty("alias").GetString()!,
                Array.Empty<Guid>(), new[] { executionId }, new[] { type }, Array.Empty<ShadowSdkTokenField>(), false)
            {
                TokenNotRequired = true,
                SynchronousStepTypes = step?.Registration.Mode == "Synchronous" ? new Dictionary<Guid, string> { [executionId] = type } : new Dictionary<Guid, string>(),
                StepImages = step is null ? new Dictionary<Guid, IReadOnlyList<RegistrationImage>>() :
                    new Dictionary<Guid, IReadOnlyList<RegistrationImage>> { [executionId] = step.Registration.Images }
            };
            var candidate = ShadowSdkScenarioFactory.PrepareLive(file, metadata, identity, binding);
            scenario = scenario with { Observe = candidate.Definition.Observe };
            prepared.Add(scenario, candidate); scenarios.Add(scenario);
            if (template is not null) activities.Add(scenario, template);
        }
        LiveCheckResult result;
        if (prepared.Count == 0)
            result = new(scenarios.Select(scenario => new LiveCheckCase(scenario.Name, scenario.Type, "not-checked",
                new(false, Array.Empty<ShadowDifference>()), 0, 0)).ToArray());
        else
        {
            var sdk = new ShadowSdkRuntime(service, identity.OrganizationId, identity.ChurchId, identity.ContactId,
                prepared.Values.ToDictionary(item => item.Definition, item => item.Source), () => throw InvalidLive(), TimeProvider.System);
            result = new LiveCheckRunner(new LiveSdkRuntime(sdk, safety, prepared, activities)).Run(target, module, scenarios, token);
        }
        var text = string.Join("\r\n", result.Cases.Select(item => item.Type + " " + item.Name + " " + LiveText.Get(item.Status == "pass" ? "Pass" : item.Status == "not-checked" ? "NotChecked" : "Difference") +
            " comparable=" + item.Comparison.Comparable + " " + LiveText.Get("Differences") + item.Comparison.Differences.Count.ToString(CultureInfo.InvariantCulture) +
            " " + LiveText.Get("Pushes") + item.ActualPushes.ToString(CultureInfo.InvariantCulture) + " " + LiveText.Get("Tasks") + item.TaskEvidenceCount.ToString(CultureInfo.InvariantCulture)));
        var liveRoot = Path.Combine(repository, "artifacts", "live");
        Directory.CreateDirectory(liveRoot);
        Schema.SchemaDeploymentCli.EnsureNoLinks(liveRoot);
        WriteDocumentContent(liveRoot, DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture) + "-" + module + ".md", text);
        WriteDocumentFile(operation, "live-check.json", JsonSerializer.Serialize(result, DocumentJson));
        output.WriteLine("Live check completed after cleanup; see the safe report."); return result.Passed ? 0 : 1;
    }
    /// <summary>只讀受管 configuration 的模式，JSON owner 立即釋放。</summary>
    private static string JsonDocumentMode(string configuration)
    {
        using var json = JsonDocument.Parse(configuration);
        return json.RootElement.TryGetProperty("mode", out var mode) ? mode.GetString() ?? "" : "";
    }

    /// <summary>寫入前核四欄確為可更新字串及長度；核心另以官方 RowVersion 防並行覆寫。</summary>
    private static void ValidateDefaultsMetadata(IOrganizationService service, CancellationToken token)
    {
        var metadata = new ShadowSdkMetadata(service, DefaultsEntityNames, token);
        _ = metadata.ConvertFields("account", JsonSerializer.SerializeToElement(new {
            new_defaultlanguage = "zh-TW", new_enabledlanguages = "zh-TW", new_timezone = "Asia/Taipei", new_countrycode = "TW" }), false,
            (_, _) => throw InvalidLive());
    }
    /// <summary>安全計數的文字投影，不列記錄內容或正式ID。</summary>
    private static string ObservationMarkdown(ObservationReport report) => LiveText.Get("Trace") + (report.TraceAvailable ? report.TraceExceptions?.ToString(CultureInfo.InvariantCulture) : LiveText.Get("Unavailable")) +
        "\r\n" + string.Join("\r\n", report.Steps.Select(row => row.Type + " " + LiveText.Get("Failed") + row.Failed.ToString(CultureInfo.InvariantCulture) + " " + LiveText.Get("Waiting") + row.Waiting.ToString(CultureInfo.InvariantCulture))) +
        "\r\n" + string.Join("\r\n", report.Tasks.Select(row => row.Type.ToString(CultureInfo.InvariantCulture) + " " + LiveText.Get("TaskState") + row.State.ToString(CultureInfo.InvariantCulture) + " " + LiveText.Get("TaskCount") + row.Count.ToString(CultureInfo.InvariantCulture))) +
        "\r\n" + LiveText.Get("WorkflowFailures") + report.WorkflowFailures.ToString(CultureInfo.InvariantCulture) + "\r\n" + LiveText.Get("MutualExclusion") + report.MutualExclusionPassed +
        "\r\n" + LiveText.Get("OldestWaiting") + report.OldestWaitingMinutes?.ToString(CultureInfo.InvariantCulture);
    /// <summary>固定錯誤不包含輸入內容或秘密。</summary>
    private static InvalidDataException InvalidLive() => new("Live tooling safety contract rejected this operation.");
}

/*
檔案整體說明
目的與定位：P7 Live、觀察、教會預設與工作流程形狀工具的主 CLI 分部，沿共同 ProductionGuard，再嚴格核選項、固定路徑及同組織寫鎖。
主要流程：observe 與 workflow-shape 包唯讀 SDK；前者只發布計數，後者只輸出已脫敏停用流程結構；
church-defaults 只讀計畫或同 buffer 核 SHA 後套用；live-check 只在 test 組織，依即時 metadata、D34 與情境準備後交唯一 runner。
安全與隔離：每命令獨占 lease，SDK 歸還後才釋放寫鎖；manifest/catalog handle 全程只讀持有，
檔案固定在本組織 artifacts。計畫有界、拒未知／重複欄位，不接受命令列秘密或任意輸出位置。
資源與失敗：讀取工具不得呼叫任何寫入 SDK API；HTTP safety owner 在 SDK 前 Dispose，所有真失敗在 lease 釋放後交共同告警，
seed 識別包裝會還原原始例外供 reporter 分類；安全摘要可攜帶固定 alias 脈絡，不含欄位值。
準備失敗以不可重編 LIVE-PREP 原因碼定位，只列模組／情境／alias／實體；不把準備失敗當比對差異。
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
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
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
                if (key is not ("--org" or "--module" or "--scenario-root" or "--since" or "--plan" or "--plan-sha256" or "--activity-type") ||
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
                "workflow-shape" => target.Kind == "test" && Exactly("--org", "--activity-type"),
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
                files = LivePreparationFailure.Capture(LivePreparationReason.ScenarioSource, module, null, null, null,
                    () => ShadowScenarioFiles.LoadLive(repository, folder, acceptance), token);
            }
            using var manifestHandle = args[0] == "live-check"
                ? LivePreparationFailure.Capture(LivePreparationReason.RegistrationSnapshot, options["--module"], null, null, null,
                    () => OpenSource(Path.Combine(repository, "d365", "registration", "plugins.registration.json")), token)
                : OpenSource(Path.Combine(repository, "d365", "registration", "plugins.registration.json"));
            using var catalogHandle = args[0] == "live-check"
                ? LivePreparationFailure.Capture(LivePreparationReason.RegistrationSnapshot, options["--module"], null, null, null,
                    () => OpenSource(Path.Combine(repository, "d365", "registration", "organizations.json")), token)
                : OpenSource(Path.Combine(repository, "d365", "registration", "organizations.json"));
            using var writeLock = args[0] == "live-check"
                ? LivePreparationFailure.Capture(LivePreparationReason.LocalResources, options["--module"], null, null, null,
                    () => OrganizationWriteLock.Acquire(root, writeLockTimeout ?? TimeSpan.FromMinutes(5), token), token)
                : apply ? OrganizationWriteLock.Acquire(root, writeLockTimeout ?? TimeSpan.FromMinutes(5), token) : null;
            using var lease = args[0] == "live-check"
                ? LivePreparationFailure.Capture(LivePreparationReason.Connection, options["--module"], null, null, null,
                    () => connector.Connect(target.Name), token)
                : connector.Connect(target.Name);
            if (args[0] == "live-check")
                _ = LivePreparationFailure.Capture(LivePreparationReason.Safety, options["--module"], null, null, null,
                    () => { ProductionGuard.VerifyServer(lease.Service, target, token); return true; }, token);
            else ProductionGuard.VerifyServer(lease.Service, target, token);
            var operation = args[0] == "live-check"
                ? LivePreparationFailure.Capture(LivePreparationReason.LocalResources, options["--module"], null, null, null,
                    () => CreateOperationDirectory(root), token) : CreateOperationDirectory(root);
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
                    var updateValidity = ValidateDefaultsMetadata(lease.Service, token);
                    engine.Apply(plan, plan.Sha256, updateValidity, token);
                }
                output.WriteLine("Church defaults completed; existing values were preserved."); return 0;
            }
            var service = args[0] is "observe" or "workflow-shape" ? new ReadOnlyOrganizationService(lease.Service) : lease.Service;
            SwitchSnapshot ReadSnapshot()
            {
                var document = RegistrationManifestLoader.LoadDocument(repository);
                var images = document.Assemblies.Select(assembly => AssemblyCatalog.Read(Path.Combine(repository, "artifacts", "plugins", assembly.AssemblyName + ".dll"), assembly)).ToArray();
                return SwitchSnapshotReader.Read(service, document, images, target,
                    Convert.ToHexString(SHA256.HashData(manifestHandle)), Convert.ToHexString(SHA256.HashData(catalogHandle)), operation, catalog, token);
            }
            var snapshot = args[0] == "live-check"
                ? LivePreparationFailure.Capture(LivePreparationReason.RegistrationSnapshot, options["--module"], null, null, null, ReadSnapshot, token)
                : ReadSnapshot();
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
            if (args[0] == "workflow-shape")
            {
                var activityType = options["--activity-type"];
                var activity = snapshot.Activities.SingleOrDefault(item =>
                    string.Equals(item.Registration.Type, activityType, StringComparison.Ordinal));
                if (activity is null || activity.Registration.Legacy.Count == 0)
                    throw new InvalidDataException("No registered legacy activity is available for shape capture.");

                (SwitchWorkflow Workflow, string Shape) selected;
                try
                {
                    var requestedLegacy = activity.Registration.Legacy
                        .Select(item => new WorkflowActivityReference(item.Assembly, item.Type))
                        .ToArray();
                    selected = SelectWorkflowShapeSource(snapshot.Workflows, snapshot.LegacyActivities, requestedLegacy);
                }
                catch (InvalidDataException)
                {
                    var counts = CountWorkflowShapeCandidates(snapshot.Workflows, snapshot.LegacyActivities);
                    output.WriteLine(
                        $"Workflow-shape safe counts: snapshot={counts.Total}; notEnabled={counts.NotEnabled}; " +
                        $"projectableUniqueLegacyMatch={counts.ProjectableUniqueLegacyMatch}; " +
                        $"withFixedBinding={counts.WithFixedBinding}; withRecordFieldBinding={counts.WithRecordFieldBinding}; " +
                        $"withBothBindingKinds={counts.WithBothBindingKinds}; " +
                        $"legacyCatalogEntries={counts.LegacyCatalogEntries}; " +
                        $"identityPresent={counts.IdentityPresent}; typeMatched={counts.TypeMatched}; " +
                        $"assemblyMatched={counts.AssemblyMatched}; reversedMatched={counts.ReversedMatched}; " +
                        $"registeredLegacyMatch={counts.RegisteredLegacyMatch}.");
                    var requestedLegacy = activity.Registration.Legacy
                        .Select(item => new WorkflowActivityReference(item.Assembly, item.Type))
                        .ToArray();
                    var diagnostic = SelectWorkflowShapeDiagnosticSource(
                        snapshot.Workflows, snapshot.LegacyActivities, requestedLegacy);
                    WriteDocumentFile(operation, "workflow-shape-diagnostic.xml", diagnostic.Shape + "\r\n");
                    output.WriteLine("Sanitized diagnostic projection written; it is not an acceptance golden.");
                    return 0;
                }
                WriteDocumentFile(operation, "activity-reference-shape.xml", selected.Shape + "\r\n");
                output.WriteLine("Disabled legacy workflow shape projected; source values and identifiers were redacted.");
                return 0;
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
    /// <summary>
    /// 從同一份完整 SwitchSnapshot 選取停用舊流程，避免再次查詢造成兩份 metadata 快照不一致。
    /// 僅接受目前指定新活動明確對應的舊型別，且含兩種輸入繫結形狀的唯一候選；依正式流程 ID 排序使取樣可重現。
    /// </summary>
    /// <param name="workflows">由本命令唯一唯讀 snapshot 取得的完整流程集合。</param>
    /// <param name="legacyActivities">同一 snapshot 所核對的舊自訂活動 catalog。</param>
    /// <param name="requestedActivities">目前指定新活動在 registration 中明確宣告的舊活動映射。</param>
    /// <returns>來源流程與已脫敏結構；原始 XAML 不會離開此方法，也不會寫入檔案。</returns>
    /// <exception cref="InvalidDataException">沒有符合條件的停用流程或輸入集合無效。</exception>
    internal static (SwitchWorkflow Workflow, string Shape) SelectWorkflowShapeSource(
        IReadOnlyList<SwitchWorkflow> workflows,
        IReadOnlyList<WorkflowActivityReference> legacyActivities,
        IReadOnlyList<WorkflowActivityReference> requestedActivities)
    {
        ArgumentNullException.ThrowIfNull(workflows);
        ArgumentNullException.ThrowIfNull(legacyActivities);
        ArgumentNullException.ThrowIfNull(requestedActivities);
        if (requestedActivities.Count == 0 || requestedActivities.Any(requested =>
            !legacyActivities.Any(registered =>
                string.Equals(registered.Type, requested.Type, StringComparison.Ordinal) &&
                string.Equals(registered.Assembly, requested.Assembly, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException("Requested legacy activity mapping is not registered.");

        foreach (var workflow in workflows.Where(item => item.State != 1).OrderBy(item => item.Id))
        {
            try
            {
                var shape = WorkflowActivityShapeProjector.ProjectLegacyActivity(workflow.Xaml, requestedActivities);
                if (shape is not null && shape.Contains("[FIXED_VALUE]", StringComparison.Ordinal) &&
                    shape.Contains("[RECORD_FIELD]", StringComparison.Ordinal))
                    return (workflow, shape);
            }
            catch (InvalidDataException)
            {
                // 不輸出或保留候選本文；逐筆跳過後只回傳 catalog 命中的安全投影。
            }
        }

        throw new InvalidDataException("No disabled legacy activity workflow is available for shape capture.");
    }

    /// <summary>
    /// 取得指定新活動映射之任一停用舊流程的脫敏結構，供診斷未達 golden 條件時檢視格式。
    /// 結果只寫入明確標示為 diagnostic 的檔名，不可用作驗收 golden 或推定兩種 binding 均已取得。
    /// </summary>
    /// <param name="workflows">同一唯讀 snapshot 的流程集合。</param>
    /// <param name="legacyActivities">同一 snapshot 的完整舊活動 catalog。</param>
    /// <param name="requestedActivities">指定新活動 registration 明列的舊活動映射。</param>
    /// <returns>第一個依流程 ID 排序且唯一命中指定舊活動的安全投影。</returns>
    /// <exception cref="InvalidDataException">映射未登錄或沒有可投影的停用舊流程。</exception>
    internal static (SwitchWorkflow Workflow, string Shape) SelectWorkflowShapeDiagnosticSource(
        IReadOnlyList<SwitchWorkflow> workflows,
        IReadOnlyList<WorkflowActivityReference> legacyActivities,
        IReadOnlyList<WorkflowActivityReference> requestedActivities)
    {
        ArgumentNullException.ThrowIfNull(workflows);
        ArgumentNullException.ThrowIfNull(legacyActivities);
        ArgumentNullException.ThrowIfNull(requestedActivities);
        if (requestedActivities.Count == 0 || requestedActivities.Any(requested =>
            !legacyActivities.Any(registered =>
                string.Equals(registered.Type, requested.Type, StringComparison.Ordinal) &&
                string.Equals(registered.Assembly, requested.Assembly, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException("Requested legacy activity mapping is not registered.");

        foreach (var workflow in workflows.Where(item => item.State != 1).OrderBy(item => item.Id))
        {
            try
            {
                var shape = WorkflowActivityShapeProjector.ProjectLegacyActivity(workflow.Xaml, requestedActivities);
                if (shape is not null)
                    return (workflow, shape);
            }
            catch (InvalidDataException)
            {
                // 僅略過不合格來源；不輸出 parser 訊息或來源 XML。
            }
        }

        throw new InvalidDataException("No disabled legacy activity workflow is available for diagnostic projection.");
    }

    /// <summary>
    /// 產生不含流程ID、名稱、XAML或業務資料的選樣診斷計數，供唯讀命令定位結構與catalog差異。
    /// 無法安全投影的候選只計入未啟用總數，不保留 parser 例外或來源內容。
    /// </summary>
    /// <param name="workflows">同一命令已核對的流程 snapshot。</param>
    /// <param name="legacyActivities">同一 snapshot 的舊自訂活動 catalog。</param>
    /// <returns>流程結構計數與身份存在、型別、組件、方向、完整命中的數量。</returns>
    internal static (
        int Total,
        int NotEnabled,
        int ProjectableUniqueLegacyMatch,
        int WithFixedBinding,
        int WithRecordFieldBinding,
        int WithBothBindingKinds,
        int LegacyCatalogEntries,
        int IdentityPresent,
        int TypeMatched,
        int AssemblyMatched,
        int ReversedMatched,
        int RegisteredLegacyMatch)
        CountWorkflowShapeCandidates(
            IReadOnlyList<SwitchWorkflow> workflows,
            IReadOnlyList<WorkflowActivityReference> legacyActivities)
    {
        ArgumentNullException.ThrowIfNull(workflows);
        ArgumentNullException.ThrowIfNull(legacyActivities);
        var notEnabled = 0;
        var projectableUniqueLegacyMatch = 0;
        var withFixedBinding = 0;
        var withRecordFieldBinding = 0;
        var withBothBindingKinds = 0;
        var identityPresent = 0;
        var typeMatched = 0;
        var assemblyMatched = 0;
        var reversedMatched = 0;
        var registeredLegacyMatch = 0;
        foreach (var workflow in workflows)
        {
            if (workflow.State == 1 || string.IsNullOrWhiteSpace(workflow.Xaml))
                continue;
            notEnabled++;
            try
            {
                var shape = WorkflowActivityShapeProjector.ProjectLegacyActivity(workflow.Xaml, legacyActivities);
                if (shape is not null)
                {
                    projectableUniqueLegacyMatch++;
                    var hasFixedBinding = shape.Contains("[FIXED_VALUE]", StringComparison.Ordinal);
                    var hasRecordFieldBinding = shape.Contains("[RECORD_FIELD]", StringComparison.Ordinal);
                    if (hasFixedBinding) withFixedBinding++;
                    if (hasRecordFieldBinding) withRecordFieldBinding++;
                    if (hasFixedBinding && hasRecordFieldBinding) withBothBindingKinds++;
                }
                var match = WorkflowActivityShapeProjector.CompareLegacyActivityIdentity(workflow.Xaml, legacyActivities);
                if (match.HasIdentity) identityPresent++;
                if (match.TypeMatched) typeMatched++;
                if (match.AssemblyMatched) assemblyMatched++;
                if (match.ReversedMatched) reversedMatched++;
                if (match.RegisteredMatch) registeredLegacyMatch++;
            }
            catch (InvalidDataException)
            {
                // 只發布計數；不保留或回顯來源 XML 與 parser 診斷。
            }
        }

        return (
            workflows.Count,
            notEnabled,
            projectableUniqueLegacyMatch,
            withFixedBinding,
            withRecordFieldBinding,
            withBothBindingKinds,
            legacyActivities.Count,
            identityPresent,
            typeMatched,
            assemblyMatched,
            reversedMatched,
            registeredLegacyMatch);
    }

    /// <summary>整批現況核對及情境準備成功後才允許任何 seed，dormant 明列未檢查。</summary>
    private static int RunPreparedLive(OrganizationDefinition target, string module, IReadOnlyList<ShadowFileCase> files,
        string repository, string operation, IOrganizationService service, SwitchSnapshot snapshot,
        TextWriter output, Func<string, string>? secretReader, CancellationToken token)
    {
        _ = LivePreparationFailure.Capture(LivePreparationReason.OffPlan, module, null, null, null,
            () => SwitchPlanner.Create(snapshot, "off"), token);
        // Snapshot包含平台標準流程；不能把所有啟用流程都誤認成舊活動流程。
        // 仍拒絕任何啟用舊step或引用完整舊活動清冊的流程（含未移植活動），
        // 精確以assembly/type配對，不依名稱猜測；解析失敗也拒絕。樣板仍只接受停用流程。
        var legacyEnabled = LivePreparationFailure.Capture(LivePreparationReason.LegacyEnabled, module, null, null, null,
            () => snapshot.LegacySteps.Any(row => row.Enabled) || snapshot.Workflows.Any(row => row.State != 0 &&
                WorkflowRepointer.ReadReferences(row.Xaml).Any(reference => snapshot.LegacyActivities.Any(old =>
                    WorkflowRepointer.SameReference(reference, old.Assembly, old.Type)))), token);
        if (legacyEnabled) throw new LivePreparationFailure(LivePreparationReason.LegacyEnabled, module);
        using var safety = new LiveSafety(service, name => ResolveRequiredSecret(target.Name, name, secretReader));
        var identity = LivePreparationFailure.Capture(LivePreparationReason.Safety, module, null, null, null,
            () => safety.Verify(token, module), token);
        var selected = files.Where(file => file.LiveShadow).ToArray();
        if (selected.Length == 0) throw new LivePreparationFailure(LivePreparationReason.NoScenario, module);
        var prepared = new Dictionary<LiveCheckScenario, ShadowSdkPreparedScenario>();
        var activities = new Dictionary<LiveCheckScenario, LiveActivityTemplate>();
        var scenarios = new List<LiveCheckScenario>();
        var rejectedActivity = false;
        foreach (var file in selected)
        {
            token.ThrowIfCancellationRequested();
            var steps = snapshot.Steps.Where(row => row.Registration.Module == module &&
                (row.Registration.Type == file.Entrypoint || row.Registration.Legacy.Any(old => old.Type == file.Entrypoint)) &&
                !file.IsActivity && row.Registration.Message == file.Trigger.GetProperty("message").GetString() &&
                row.Registration.Entity == file.Trigger.GetProperty("entity").GetString()).ToArray();
            var activityMatches = snapshot.Activities.Where(row => row.Registration.Module == module && file.IsActivity &&
                (row.Registration.Type == file.Entrypoint || row.Registration.Legacy.Any(old => old.Type == file.Entrypoint))).ToArray();
            if (steps.Length + activityMatches.Length != 1)
                throw PreparationFailure(LivePreparationReason.AmbiguousBinding, module, file);
            var step = steps.SingleOrDefault();
            var activity = activityMatches.SingleOrDefault();
            var type = step?.Registration.Type ?? activity!.Registration.Type;
            var tier = SwitchTarget.EffectiveTier(target, module, type, step?.Registration.Live?.Tier ?? activity!.Registration.Live!.Tier);
            var mode = step is null ? "Live" : CapturePreparation(LivePreparationReason.StepState, module, file,
                () => JsonDocumentMode(step.Configuration), token);
            var scenario = new LiveCheckScenario(file.Name, type, module, tier, step?.Enabled ?? true, mode,
                file.ExpectedLineCalls.EnumerateArray().Sum(call => call.GetProperty("messageCount").GetInt32()),
                new(false, Array.Empty<ShadowRow>()), Array.Empty<ShadowObservation>(),
                new HashSet<string>(file.Volatile, StringComparer.Ordinal));
            if (tier == "dormant") { scenarios.Add(scenario); continue; }
            if (!scenario.Enabled || mode != "Live") throw PreparationFailure(LivePreparationReason.StepState, module, file);
            if (activity is not null)
            {
                var recordEntity = file.Seed.EnumerateArray()
                    .Single(seed => seed.GetProperty("alias").GetString() == "record")
                    .GetProperty("entity").GetString();
                var workflows = CapturePreparation(LivePreparationReason.ActivityTemplate, module, file,
                    () => new LegacyRegistrationExporter.MetadataReader(service, 500, 10000, 100, token)
                    .Read("workflow", ["xaml", "statecode", "primaryentity"], "type", [1]).Where(row =>
                        row.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0 &&
                        row.GetAttributeValue<string>("primaryentity") == recordEntity)
                    .Select(row => (Row: row, References: WorkflowRepointer.ReadReferences(row.GetAttributeValue<string>("xaml") ?? "")))
                    .Where(item => item.References.Count != 0 && item.References.All(reference => reference.Type == type ||
                        activity.Registration.Legacy.Any(old => WorkflowRepointer.SameReference(reference, old.Assembly, old.Type))))
                    .OrderBy(item => item.Row.Id).ToArray(), token);
                if (workflows.Length == 0) throw PreparationFailure(LivePreparationReason.ActivityTemplate, module, file);
                var failure = PreparationFailure(LivePreparationReason.ActivityInputs, module, file);
                rejectedActivity = true;
                output.WriteLine(failure.SafeSummary);
                continue;
            }
            var churchAlias = file.Church.GetProperty("alias").GetString()!;
            var contacts = file.Seed.EnumerateArray().Where(row => row.GetProperty("entity").GetString() == "contact" &&
                !row.GetProperty("fields").EnumerateObject().Any()).ToArray();
            if (contacts.Length != 1) throw PreparationFailure(LivePreparationReason.ContactCount, module, file);
            var names = file.Seed.EnumerateArray().Concat(file.Observe.EnumerateArray())
                .Concat(file.ExpectedWrites.EnumerateArray()).Select(row => row.GetProperty("entity").GetString()!).Distinct(StringComparer.Ordinal).ToArray();
            var metadata = CapturePreparation(LivePreparationReason.Metadata, module, file,
                () => new ShadowSdkMetadata(service, names, token), token);
            var executionId = step!.Id;
            var binding = new ShadowSdkBinding(file.Entrypoint, churchAlias, contacts[0].GetProperty("alias").GetString()!,
                Array.Empty<Guid>(), new[] { executionId }, new[] { type }, Array.Empty<ShadowSdkTokenField>(), false)
            {
                TokenNotRequired = true,
                // 沿R4已核定的WhoAmI繫結，owner只借用目前服務使用者，不建立或清除systemuser。
                SharedReferences = CapturePreparation(LivePreparationReason.PrepareScenario, module, file,
                    () => SharedReferencesForScenario(service, file), token),
                SynchronousStepTypes = step.Registration.Mode == "Synchronous" ? new Dictionary<Guid, string> { [executionId] = type } : new Dictionary<Guid, string>(),
                StepImages = new Dictionary<Guid, IReadOnlyList<RegistrationImage>> { [executionId] = step.Registration.Images }
            };
            var candidate = CapturePreparation(LivePreparationReason.PrepareScenario, module, file,
                () => ShadowSdkScenarioFactory.PrepareLive(file, metadata, identity, binding), token);
            scenario = scenario with { Observe = candidate.Definition.Observe };
            prepared.Add(scenario, candidate); scenarios.Add(scenario);
        }
        LiveCheckResult result;
        if (prepared.Count == 0)
            result = new(scenarios.Select(scenario => new LiveCheckCase(scenario.Name, scenario.Type, "not-checked",
                new(false, Array.Empty<ShadowDifference>()), 0, 0)).ToArray());
        else
        {
            var sdk = new ShadowSdkRuntime(service, identity.OrganizationId, identity.ChurchId, identity.ContactId,
                prepared.Values.ToDictionary(item => item.Definition, item => item.Source),
                () => throw new LivePreparationFailure(LivePreparationReason.ForbiddenShadowCapability, module), TimeProvider.System);
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
        output.WriteLine("Live check completed after cleanup; see the safe report."); return result.Passed && !rejectedActivity ? 0 : 1;
    }
    /// <summary>準備故障只取明列的目標識別；活動用固定record seed，不讀欄位值或記錄ID。</summary>
    private static LivePreparationFailure PreparationFailure(LivePreparationReason reason, string module, ShadowFileCase file, Exception? inner = null)
    {
        var context = PreparationContext(file);
        return new LivePreparationFailure(reason, module, file.Name, context.Alias, context.Entity, inner);
    }

    /// <summary>只在特定準備呼叫加碼，保留原始SDK故障與caller取消，不涵蓋runner清理範圍。</summary>
    private static T CapturePreparation<T>(LivePreparationReason reason, string module, ShadowFileCase file,
        Func<T> operation, CancellationToken token)
    {
        var context = PreparationContext(file);
        return LivePreparationFailure.Capture(reason, module, file.Name, context.Alias, context.Entity, operation, token);
    }

    /// <summary>來源已由strict reader驗證；TryGetProperty防止診斷本身遮蔽原錯誤，缺值保持空白不猜測。</summary>
    private static (string? Alias, string? Entity) PreparationContext(ShadowFileCase file)
    {
        static string? Text(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
        if (!file.IsActivity) return (Text(file.Trigger, "target"), Text(file.Trigger, "entity"));
        if (file.Seed.ValueKind == JsonValueKind.Array)
            foreach (var row in file.Seed.EnumerateArray())
                if (Text(row, "alias") == "record") return ("record", Text(row, "entity"));
        return ("record", null);
    }

    /// <summary>只讀受管 configuration 的模式，JSON owner 立即釋放。</summary>
    private static string JsonDocumentMode(string configuration)
    {
        using var json = JsonDocument.Parse(configuration);
        return json.RootElement.TryGetProperty("mode", out var mode) ? mode.GetString() ?? "" : "";
    }

    /// <summary>寫入前核四欄確為可更新字串及長度；核心另以官方 RowVersion 防並行覆寫。</summary>
    private static System.Collections.ObjectModel.ReadOnlyDictionary<string, bool?> ValidateDefaultsMetadata(IOrganizationService service, CancellationToken token)
    {
        var metadata = new ShadowSdkMetadata(service, DefaultsEntityNames, token);
        _ = metadata.ConvertFields("account", JsonSerializer.SerializeToElement(new {
            new_defaultlanguage = "zh-TW", new_enabledlanguages = "zh-TW", new_timezone = "Asia/Taipei", new_countrycode = "TW" }), false,
            (_, _) => throw InvalidLive());
        token.ThrowIfCancellationRequested();
        var response = service.Execute(new RetrieveEntityRequest
        {
            LogicalName = "account",
            EntityFilters = EntityFilters.Attributes
        }) as RetrieveEntityResponse;
        var entity = response?.EntityMetadata;
        if (entity?.Attributes is null) throw InvalidLive();
        var attributes = entity.Attributes.Where(attribute => attribute.LogicalName is
                "new_defaultlanguage" or "new_enabledlanguages" or "new_timezone" or "new_countrycode")
            .ToDictionary(attribute => attribute.LogicalName!, attribute => attribute.IsValidForUpdate, StringComparer.Ordinal);
        if (attributes.Count != 4) throw InvalidLive();
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, bool?>(attributes);
    }
    /// <summary>安全計數的文字投影，不列記錄內容或正式ID。</summary>
    private static string ObservationMarkdown(ObservationReport report) => LiveText.Get("Trace") + (report.TraceAvailable ? report.TraceExceptions?.ToString(CultureInfo.InvariantCulture) : LiveText.Get("Unavailable")) +
        "\r\n" + string.Join("\r\n", report.TraceExceptionGroups.Select(row => "TraceException plugin=" + row.PluginType +
            "; type=" + row.ExceptionType + "; count=" + row.Count.ToString(CultureInfo.InvariantCulture))) +
        "\r\n" + string.Join("\r\n", report.Steps.Select(row => row.Type + " " + LiveText.Get("Failed") + row.Failed.ToString(CultureInfo.InvariantCulture) + " " + LiveText.Get("Waiting") + row.Waiting.ToString(CultureInfo.InvariantCulture))) +
        "\r\n" + string.Join("\r\n", report.Tasks.Select(row => row.Type.ToString(CultureInfo.InvariantCulture) + " " + LiveText.Get("TaskState") + row.State.ToString(CultureInfo.InvariantCulture) + " " + LiveText.Get("TaskCount") + row.Count.ToString(CultureInfo.InvariantCulture))) +
        "\r\n" + LiveText.Get("WorkflowFailures") + report.WorkflowFailures.ToString(CultureInfo.InvariantCulture) + "\r\n" + LiveText.Get("MutualExclusion") + report.MutualExclusionPassed +
        "\r\n" + LiveText.Get("OldestWaiting") + report.OldestWaitingMinutes?.ToString(CultureInfo.InvariantCulture);
    /// <summary>固定錯誤不包含輸入內容或秘密。</summary>
    private static InvalidDataException InvalidLive() => new("Live tooling safety contract rejected this operation.");
}

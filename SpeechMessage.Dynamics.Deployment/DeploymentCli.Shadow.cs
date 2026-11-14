/*
 * 檔案整體說明
 *
 * 目的與定位：Deployment shadow 命令的真正組合根，將固定情境 reader、權威身分、
 * metadata、factory、SDK runtime、runner 與安全報告串在既有命令租約及寫鎖內。
 * 主要功能與流程：連線前檢查固定模組目錄；連線後核對完整部署 PE／註冊狀態、
 * User Secrets 中的測試 GUID 與權威 LINE 綁定，再準備整批情境，唯讀匯出後執行。
 * 每輪由 runner 停用、核對、清理自己的記錄並還原追蹤；成功返回後才寫安全報告。
 * 信任與適用邊界：CRUD live 基準由固定受審 token 盤點接通先舊後新兩輪；
 * new／reading／legacy-golden 仍只跑新輪，不假造舊證據。legacy switch 共用完整前置，
 * 只跑核定實機舊輪；報告明標範圍，不能把舊輪欄位通過寫成兩輪推播驗收。
 * 外部既有列範圍來自固定盤點與正式metadata，觀察只保存教會內ID／版本，報告只含筆數。
 * 同步 trigger 依正式 mode 帶獨立 RequestId，不以情境 JSON 指定同步追蹤。
 * 關聯觀察以明列 lookup 及本輪 owned 父列配對；缺盤點、未釐清／寫死 token
 * 及未接通活動在業務寫入前拒絕，不從情境 JSON 猜 token 安全性。
 * 使用邊界與維護：主入口唯一擁有 lease、組織鎖及告警；本檔只借用 SDK、writer、
 * 秘密讀取委派及取消 token，不另建連線、快取、背景工作或 timer。候選只活於命令，
 * 報告各存唯一操作子目錄防同日覆寫；真正例外交主入口在歸還租約後回報。
 * 驗證以 ShadowCliTests 的真 CLI／合成 SDK 故障與清理測試為準，不代表實機驗收。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Deployment.Shadow;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>共用主命令的授權、連線及終端錯誤生命週期，不另起可繞過鎖的入口。</summary>
internal static partial class DeploymentCli
{
    /// <summary>只允許 repo 內該模組固定 Golden 目錄，完整核對祖先 reparse point，不接受任意外部 JSON。</summary>
    private static bool IsShadowScenarioRoot(string repositoryRoot, string module, string candidate)
    {
        if (module is not ("diagnostics" or "localization" or "weeklyreports" or "attendance" or
            "lessons" or "appointments" or "qrcodes" or "care" or "fees" or "messaging")) return false;
        var expected = Path.GetFullPath(Path.Combine(repositoryRoot,
            "SpeechMessageProducts.GoodShepherd.Core.Tests", "Golden", "Legacy", module));
        return IsContainedPath(repositoryRoot, candidate, out var full) &&
            string.Equals(Path.TrimEndingDirectorySeparator(expected), Path.TrimEndingDirectorySeparator(full),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>
    /// 在既有租約內完成核定輪次；先完成整批準備才可寫 trace 或 seed。
    /// legacyOnly 專供公開 legacy switch，不接受閱讀基準，不執行新輪或宣稱完整比較。
    /// 尚無受審舊入口 inventory 的 live 基準不可降級為只跑新輪。
    /// 報告只在 runner 成功收尾後發布，非零差異回一而非丟棄失敗證據。
    /// </summary>
    private static int RunShadow(ParsedCommand command, IReadOnlyList<ShadowFileCase> cases,
        string repositoryRoot, string operationRoot, IOrganizationService service, TextWriter output,
        Func<string, string>? secretReader, CancellationToken cancellationToken, bool legacyOnly = false)
    {
        var selected = cases.Where(item => item.LiveShadow).ToArray();
        if (selected.Length == 0 || selected.Any(item => item.IsActivity ||
            item.Basis is not ("new" or "reading" or "legacy-golden" or "legacy-live" or "legacy-live-only")) ||
            selected.Select(item => item.Basis).Distinct(StringComparer.Ordinal).Count() != 1 ||
            (legacyOnly && selected.Any(item => item.Basis is not ("legacy-live" or "legacy-live-only"))))
            throw InvalidShadowCommand();
        var document = RegistrationManifestLoader.LoadDocument(repositoryRoot);
        var images = document.Assemblies.Select(manifest => AssemblyCatalog.Read(
            Path.Combine(repositoryRoot, "artifacts", "plugins", manifest.AssemblyName + ".dll"), manifest)).ToArray();
        var deployment = new DocumentDeploymentEngine(service, operationRoot, TimeProvider.System,
            name => ResolveRequiredSecret(command.Organization, name, secretReader));
        if (deployment.Verify(document, images, command.Organization) != 0) throw InvalidShadowCommand();
        var engine = new DeploymentEngine(service, operationRoot, TimeProvider.System,
            name => ResolveRequiredSecret(command.Organization, name, secretReader));
        // runner 的同一委派也會在 finally 停用時使用；不能捕捉已取消 caller token。
        // 正常操作的取消由 runner／SwitchModule 各 I/O 前後檢查，SDK 同步呼叫不會被強中斷。
        LegacyRegistrationSnapshot ReadLegacy() => LegacyRegistrationExporter.Export(service, cancellationToken: CancellationToken.None);
        // 清理使用未取消的唯讀查詢，不能因 caller 取消略過停用與殘留確認。
        void VerifyDisabled() => engine.ReadDisabledShadowSteps(document, command.Organization, command.Module!,
            () => LegacyRegistrationExporter.Export(service));
        var steps = engine.ReadDisabledShadowSteps(document, command.Organization, command.Module!, ReadLegacy);
        var liveLegacy = selected[0].Basis is "legacy-live" or "legacy-live-only";
        var legacySnapshot = liveLegacy ? ReadLegacy() : null;
        if (liveLegacy && document.Assemblies.Any(assembly =>
            assembly.Activities.Any(activity => activity.Module == command.Module))) throw InvalidShadowCommand();
        var inventory = liveLegacy ? LegacyTokenInventory.Load(repositoryRoot, command.Module!,
            steps.SelectMany(item => item.Step.Legacy).ToArray()) : Array.Empty<LegacyTokenRule>();
        var identity = ShadowTestIdentity.Verify(service,
            ResolveRequiredSecret(command.Organization, "TestLineUserId", secretReader),
            ResolveRequiredSecret(command.Organization, "ShadowTestChurchId", secretReader),
            ResolveRequiredSecret(command.Organization, "ShadowTestContactId", secretReader), cancellationToken);
        var entityNames = selected.SelectMany(item => item.Seed.EnumerateArray().Concat(item.Observe.EnumerateArray()))
            .Select(row => row.GetProperty("entity").GetString()!).Concat(inventory.SelectMany(rule => rule.ExternalWrites).Select(scope => scope.Entity))
            .Distinct(StringComparer.Ordinal).ToArray();
        var metadata = new ShadowSdkMetadata(service, entityNames, cancellationToken);
        var externalScopes = inventory.SelectMany(rule => rule.ExternalWrites).Distinct()
            .Select(scope => metadata.ExternalScope(scope.Entity, scope.ChurchAttribute)).ToArray();
        if (externalScopes.Length > 16 || externalScopes.Select(scope => scope.Entity).Distinct(StringComparer.Ordinal).Count() != externalScopes.Length)
            throw InvalidShadowCommand();
        var prepared = new List<ShadowSdkPreparedScenario>();
        foreach (var file in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matching = steps.Where(item => item.Step.Message == file.Trigger.GetProperty("message").GetString() &&
                item.Step.Entity == file.Trigger.GetProperty("entity").GetString()).ToArray();
            if (matching.Length != 1) throw InvalidShadowCommand();
            var step = matching[0];
            if (file.Basis == "new" ? file.Entrypoint != step.Step.Type :
                !step.Step.Legacy.Any(reference => reference.Type == file.Entrypoint)) throw InvalidShadowCommand();
            var churchAlias = file.Church.GetProperty("alias").GetString()!;
            var contacts = file.Seed.EnumerateArray().Where(row =>
                row.GetProperty("entity").GetString() == "contact" &&
                !row.GetProperty("fields").EnumerateObject().Any()).ToArray();
            if (contacts.Length != 1) throw InvalidShadowCommand();
            var legacySteps = liveLegacy ? step.Step.Legacy.Select(reference =>
                legacySnapshot!.Steps.Single(row => row.Id == Guid.Parse(reference.StepId))).ToArray() :
                Array.Empty<LegacyStepMetadata>();
            if (liveLegacy && (legacySteps.Length == 0 || legacySteps.Any(row => row.Message != step.Step.Message ||
                row.PrimaryEntity != step.Step.Entity))) throw InvalidShadowCommand();
            var tokens = new List<ShadowSdkTokenField>();
            foreach (var token in inventory.SelectMany(rule => rule.TokenFields).Distinct())
            {
                var seed = file.Seed.EnumerateArray().Where(row => row.GetProperty("alias").GetString() == token.Alias).ToArray();
                if (seed.Length != 1 || seed[0].GetProperty("entity").GetString() != token.Entity) throw InvalidShadowCommand();
                tokens.Add(new ShadowSdkTokenField(token.Alias, token.Attribute));
            }
            var synchronous = legacySteps.Where(row => row.Mode == 0).ToDictionary(row => row.Id,
                row => legacySnapshot!.Types.Single(type => type.Id == row.TypeId).TypeName);
            if (step.Step.Mode == "Synchronous") synchronous.Add(step.Id, step.Step.Type);
            // 新步驟映像已由 ReadDisabledShadowSteps 核對；舊映像僅採本次正式匯出，
            // ImageType=2 代表同別名前後皆有，不能把 Name 當成執行 context 的 EntityAlias。
            var stepImages = new Dictionary<Guid, IReadOnlyList<RegistrationImage>> { [step.Id] = step.Step.Images };
            foreach (var legacy in legacySteps)
            {
                var registered = new List<RegistrationImage>();
                foreach (var image in legacySnapshot!.Images.Where(row => row.StepId == legacy.Id))
                {
                    if (image.ImageType is < 0 or > 2 || string.IsNullOrWhiteSpace(image.EntityAlias) ||
                        string.IsNullOrWhiteSpace(image.Attributes)) throw InvalidShadowCommand();
                    var attributes = image.Attributes.Split(',').Select(value => value.Trim()).ToArray();
                    if (attributes.Any(string.IsNullOrWhiteSpace) || attributes.Distinct(StringComparer.Ordinal).Count() != attributes.Length)
                        throw InvalidShadowCommand();
                    foreach (var type in image.ImageType == 2 ? new[] { "PreImage", "PostImage" } :
                        new[] { image.ImageType == 0 ? "PreImage" : "PostImage" })
                        registered.Add(new RegistrationImage { Name = image.EntityAlias, Type = type, Attributes = attributes });
                }
                stepImages.Add(legacy.Id, registered.AsReadOnly());
            }
            var binding = new ShadowSdkBinding(file.Entrypoint, churchAlias,
                contacts[0].GetProperty("alias").GetString()!, legacySteps.Select(row => row.Id).ToArray(),
                [step.Id], [step.Step.Type], tokens.AsReadOnly(), false)
            {
                SynchronousStepTypes = synchronous,
                StepImages = stepImages,
                ExternalScopes = externalScopes,
                ExternalInventoryReviewed = liveLegacy,
                TokenNotRequired = liveLegacy && inventory.All(rule => rule.NoTokenDependency)
            };
            prepared.Add(ShadowSdkScenarioFactory.Prepare(file, metadata, identity, binding));
        }
        var reportRoot = Path.Combine(repositoryRoot, "artifacts", "shadow", Path.GetFileName(operationRoot));
        if (!IsContainedPath(repositoryRoot, reportRoot, out _)) throw InvalidShadowCommand();
        foreach (var assembly in document.Assemblies)
            engine.WriteExport(command.Organization, Path.Combine(operationRoot,
                "shadow-before-" + assembly.AssemblyName + ".json"), assembly.AssemblyName);
        var runtime = new ShadowSdkRuntime(service, identity.OrganizationId, identity.ChurchId, identity.ContactId,
            prepared.ToDictionary(item => item.Definition, item => item.Source), VerifyDisabled, TimeProvider.System);
        var runner = new ShadowScenarioRunner(engine, document, command.Organization, command.Module!, ReadLegacy, runtime);
        var definitions = prepared.Select(item => item.Definition).ToArray();
        var results = legacyOnly ? runner.RunLegacyOnly(definitions, cancellationToken) : runner.Run(definitions, cancellationToken);
        var report = legacyOnly
            ? ShadowReportWriter.WriteLegacyRound(command.Module!, selected[0].Basis, results, reportRoot, TimeProvider.System)
            : ShadowReportWriter.Write(command.Module!, selected[0].Basis, results, reportRoot, TimeProvider.System);
        if (legacyOnly)
            output.WriteLine(report.Passed ? "Legacy round field comparison passed; owned records cleaned and registrations disabled. No new round executed." :
                "Legacy round field comparison failed; owned records cleaned and registrations disabled. No new round executed.");
        else
            output.WriteLine(report.Passed ? "Shadow scenario comparison passed; owned records cleaned and registrations disabled." :
                "Shadow scenario comparison failed; owned records cleaned and registrations disabled.");
        return report.Passed ? 0 : 1;
    }

    /// <summary>固定錯誤不包含檔案內容、實際 GUID、設定或秘密。</summary>
    private static InvalidDataException InvalidShadowCommand() => new("Shadow command safety precondition failed.");
}

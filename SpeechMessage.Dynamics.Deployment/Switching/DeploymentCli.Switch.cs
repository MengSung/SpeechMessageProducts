/*
檔案整體說明
目的與定位：P7計畫切換CLI接線分部，沿主入口ProductionGuard、單組織寫鎖與獨占lease，保留舊module與export rollback。
主要流程：每次讀取當下manifest/catalog及全部本機PE，metadata reader核server現況；plan固定json/md，switch同buffer檔SHA。
rollback固定switch-log.json交專用有界reader；其他export格式沿原路由。每次寫入前完整export與legacy export並flush。
安全與生命週期：所有資料命令區域，秘密只由User Secrets委派在executor寫入時讀；不保留SDK或跨組織狀態。
故障由主CLI回報真exception並釋放lease再釋放寫鎖；SwitchCliTests以合成PE／SDK驗語法、輸出與租約清理。
*/
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Deployment.Switching;
namespace SpeechMessage.Dynamics.Deployment;

/// <summary>既有DeploymentCli分部，不建立額外命令或SDKowner。</summary>
internal static partial class DeploymentCli
{
    /// <summary>所有輸入先由主parser核對並限制同組織路徑，這裡重讀來源及server metadata，避免以過期候選套用。</summary>
    private static int RunSwitchPlan(ParsedCommand command, OrganizationDefinition target, OrganizationCatalog catalog, string repository,
        string organizationRoot, string operationRoot, IOrganizationService service, TextWriter output, Func<string, string>? secretReader, CancellationToken token)
    {
        // 兩個來源handle持有到整個命令完成，FileShare.Read阻止讀取至寫入期間被其他程序更換來源。
        using var manifestFile = OpenSource(Path.Combine(repository, "d365", "registration", "plugins.registration.json"));
        using var catalogFile = OpenSource(Path.Combine(repository, "d365", "registration", "organizations.json"));
        var manifestHash = Convert.ToHexString(SHA256.HashData(manifestFile));
        var catalogHash = Convert.ToHexString(SHA256.HashData(catalogFile));
        SwitchSnapshot Read(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var currentCatalog = OrganizationCatalog.Load(repository);
            var currentTarget = currentCatalog.Get(target.Name);
            if (currentTarget.Name != target.Name || currentTarget.Kind != target.Kind || currentTarget.Host != target.Host ||
                currentTarget.SwitchState != target.SwitchState || !currentTarget.TierOverrides.SequenceEqual(target.TierOverrides))
                throw new InvalidDataException("Organization catalog changed during command.");
            var document = RegistrationManifestLoader.LoadDocument(repository);
            var images = document.Assemblies.Select(manifest => AssemblyCatalog.Read(Path.Combine(repository, "artifacts", "plugins", manifest.AssemblyName + ".dll"), manifest)).ToArray();
            return SwitchSnapshotReader.Read(service, document, images, currentTarget, manifestHash, catalogHash, operationRoot, currentCatalog, cancellation);
        }
        void Backup()
        {
            var document = RegistrationManifestLoader.LoadDocument(repository);
            var images = document.Assemblies.Select(manifest => AssemblyCatalog.Read(Path.Combine(repository, "artifacts", "plugins", manifest.AssemblyName + ".dll"), manifest)).ToArray();
            var engine = new DocumentDeploymentEngine(service, organizationRoot, TimeProvider.System,
                name => ResolveRequiredSecret(target.Name, name, secretReader), catalog);
            var backup = engine.Export(document, images, target.Name);
            WriteDocumentFile(operationRoot, "backup.json", JsonSerializer.Serialize(backup, DocumentJson));
            var legacy = LegacyRegistrationExporter.ExportForSwitch(service, token);
            WriteDocumentFile(operationRoot, "legacy-before.json", JsonSerializer.Serialize(legacy, DocumentJson));
        }
        var executor = new SwitchExecutor(service, operationRoot, Read, Backup, name => ResolveRequiredSecret(target.Name, name, secretReader));
        switch (command.Name)
        {
            case "plan":
                var plan = SwitchPlanner.Create(Read(token), command.SwitchTargetState!);
                WriteDocumentFile(operationRoot, "switch-plan.json", JsonSerializer.Serialize(plan, DocumentJson));
                var hash = FileHash(Path.Combine(operationRoot, "switch-plan.json"));
                WriteDocumentFile(operationRoot, "switch-plan.md", Markdown(plan, hash));
                output.WriteLine("Switch plan created. Review switch-plan.md and approve the file SHA-256 before execution.");
                return 0;
            case "switch":
                var input = ReadSwitchPlan(command.InputPath!, organizationRoot);
                _ = executor.Execute(input.Plan, command.PlanSha256!, input.Bytes, token);
                output.WriteLine("Switch completed. Recovery evidence: switch-log.json");
                return 0;
            case "verify":
                var approved = command.InputPath is null ? null : ReadSwitchPlan(command.InputPath, organizationRoot).Plan;
                var differences = SwitchPlanner.Verify(Read(token), approved);
                output.WriteLine(differences.Count == 0 ? "Switch state matches the expected target." : "Switch state differs from the expected target.");
                output.WriteLine("Secure configuration presence was checked; secret values were not compared. This is not secret rotation.");
                return differences.Count == 0 ? 0 : 1;
            case "rollback":
                executor.Rollback(command.InputPath!, target.Name);
                output.WriteLine("Switch rollback completed.");
                return 0;
            default: throw new InvalidDataException("Invalid switch command.");
        }
    }

    /// <summary>從同一buffer解析plan與算檔SHA；拒絕未知／重複JSON欄位與四MiB超限，不再重開檔驗hash。</summary>
    private static (SwitchPlan Plan, byte[] Bytes) ReadSwitchPlan(string path, string organizationRoot)
    {
        if (!IsDocumentPath(organizationRoot, path)) throw new InvalidDataException("Switch plan path is invalid.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 1 or > 4 * 1024 * 1024) throw new InvalidDataException("Switch plan limit exceeded.");
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException("Switch plan changed during read.");
        try
        {
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            Schema.SchemaFiles.RejectDuplicateProperties(json.RootElement);
            return (JsonSerializer.Deserialize<SwitchPlan>(bytes, DocumentJson) ?? throw new InvalidDataException("Invalid switch plan."), bytes);
        }
        catch (JsonException) { throw new InvalidDataException("Invalid switch plan."); }
    }

    /// <summary>來源檔摘要有界並拒links，秘密不在來源檔；DLL摘要由AssemblyCatalog另驗。</summary>
    private static string FileHash(string path)
    {
        Schema.SchemaDeploymentCli.EnsureNoLinks(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 1 or > 4 * 1024 * 1024) throw new InvalidDataException("Switch source limit exceeded.");
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>來源只讀handle由整個切換命令持有，失敗建構時立即釋放，禁止寫入共享且容量限制四MiB。</summary>
    private static FileStream OpenSource(string path)
    {
        Schema.SchemaDeploymentCli.EnsureNoLinks(path);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 1 or > 4 * 1024 * 1024)
        { stream.Dispose(); throw new InvalidDataException("Switch source limit exceeded."); }
        return stream;
    }

    /// <summary>固定前三行與五段人審輸出，不包含XAML、原始設定或秘密；每項只列正式ID、模組、動詞與固定原因。</summary>
    private static string Markdown(SwitchPlan plan, string fileHash)
    {
        var resources = new SwitchText();
        var text = new StringBuilder();
        text.AppendLine(resources["Organization"] + plan.Organization);
        text.AppendLine(resources["State"] + plan.FromState + " → " + plan.TargetState);
        text.AppendLine(resources["PlanHash"] + fileHash);
        text.AppendLine("\n## " + resources["Actions"]);
        foreach (var operation in plan.Operations.Where(row => row.Kind is not ("KeepLegacy" or "Manual")))
            text.AppendLine("- " + operation.Kind + " " + operation.Module + " " + operation.Id.ToString("D"));
        text.AppendLine("\n## " + resources["KeepLegacy"]);
        foreach (var operation in plan.Operations.Where(row => row.Kind == "KeepLegacy")) text.AppendLine("- " + operation.Id.ToString("D") + " " + operation.Reason);
        text.AppendLine("\n## " + resources["Manual"]);
        foreach (var operation in plan.Operations.Where(row => row.Kind == "Manual")) text.AppendLine("- " + operation.Id.ToString("D") + " " + operation.Reason);
        text.AppendLine("\n## " + resources["Dormant"]);
        foreach (var item in plan.Dormant) text.AppendLine("- " + item);
        text.AppendLine("\n## " + resources["RegisteredLegacy"]);
        foreach (var assembly in plan.LegacyAssemblies) text.AppendLine("- " + assembly);
        foreach (var operation in plan.Operations.Where(row => row.Kind is "DisableLegacyStep" or "RepointWorkflow" or "KeepLegacy"))
            text.AppendLine("- " + operation.Kind + " " + operation.Id.ToString("D"));
        text.AppendLine("\n## " + resources["ApprovalSummary"]);
        text.AppendLine(SwitchText.Summary( plan.Operations.Count(row => row.Kind == "EnableStep"),
            plan.Operations.Count(row => row.Kind == "DisableLegacyStep"), plan.Operations.Count(row => row.Kind == "RepointWorkflow"),
            plan.Operations.Count(row => row.Kind is "KeepLegacy" or "Manual")));
        text.AppendLine(resources["Rollback"]);
        return text.ToString();
    }
}

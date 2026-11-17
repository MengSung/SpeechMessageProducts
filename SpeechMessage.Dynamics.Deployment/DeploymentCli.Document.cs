/*
 * 檔案整體說明
 *
 * 目的與定位：部署 CLI 的多組件清單接線層；完整文件交給 DocumentDeploymentEngine，
 * 不把多組件清單縮成第一筆。主入口負責白名單、組織寫鎖、租約及原始例外回報。
 * 主要功能與流程：逐一讀取固定 artifacts/plugins/<AssemblyName>.dll，以完整映像集合
 * 呼叫 Plan／Apply／Verify／Export／Rollback。計畫與備份索引固定為 plan.json、
 * backup.json；以 CreateNew 暫存、Flush(true)、關檔及禁止覆寫的 Move 發布。
 * 信任邊界與失敗處理：輸入最多四 MiB、深度三十二，拒絕未知及重複欄位；
 * 組織必須與命令相同，所有索引引用都須位於同組織根且祖先不可為 link／junction。
 * 核心不提供套用前備份回呼，因此先 Export 並發布可回復索引，再呼叫 Apply；
 * 核心另存的備份仍保留。失敗也不刪除已發布證據，不以跨組件交易宣稱原子套用。
 * 擁有權與維護驗證：本檔僅借用命令 SDK、writer 與秘密委派，串流由同步方法 Dispose；
 * 沒有背景工作或跨命令快取。候選只保留至命令結束，備份保留至操作者核定清理。
 * 以 DocumentDeploymentCliTests 的合成 PE、故障注入及跨命令回復驗證，不代表實機結果。
 */
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xrm.Sdk;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>完整文件命令的有界檔案與引擎接線；命令租約及寫鎖仍由主要 partial 唯一擁有。</summary>
internal static partial class DeploymentCli
{
    /// <summary>沿用核心 PascalCase DTO 名稱，拒絕未知成員且限制深度，不改變核心 SHA 的序列化規約。</summary>
    private static readonly JsonSerializerOptions DocumentJson = new()
    {
        WriteIndented = true,
        MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>
    /// 同一租約內載入全部固定 DLL；所有組件成功才發布計畫或備份索引。
    /// engine 使用組織根，讓後續 rollback 可讀先前操作的具名快照；操作目錄只存本命令索引。
    /// </summary>
    private static int RunDocument(ParsedCommand command, RegistrationDocument document, string repositoryRoot,
        string organizationRoot, string operationRoot, IOrganizationService service, TextWriter output,
        Func<string, string>? secretReader, OrganizationCatalog catalog)
    {
        var images = document.Assemblies.Select(manifest => AssemblyCatalog.Read(
            Path.Combine(repositoryRoot, "artifacts", "plugins", manifest.AssemblyName + ".dll"), manifest)).ToArray();
        var engine = new DocumentDeploymentEngine(service, organizationRoot, TimeProvider.System,
            name => ResolveRequiredSecret(command.Organization, name, secretReader), catalog);
        switch (command.Name)
        {
            case "plan":
            {
                var plan = engine.Plan(document, images, command.Organization);
                WriteDocumentFile(operationRoot, "plan.json", JsonSerializer.Serialize(plan, DocumentJson));
                WriteDocumentFile(operationRoot, "plan.md",
                    string.Join("\r\n", plan.Assemblies.Select(item => DeploymentEngine.Markdown(item.Plan))));
                output.WriteLine("Plan created.");
                if (plan.Assemblies.Any(item => item.Plan.SecretPlaceholders.Count != 0))
                    output.WriteLine("Secret values are not compared. This is not a secret-rotation plan.");
                return 0;
            }
            case "apply":
            {
                var plan = ReadDocumentFile<DocumentDeploymentPlan>(command.InputPath!, organizationRoot);
                if (plan.Organization != command.Organization)
                    throw new InvalidDataException("Plan organization does not match the command.");
                // 備份索引先落盤；即使核心 Apply 拋出例外，也能從固定 backup.json 找回整份前況。
                var backup = engine.Export(document, images, command.Organization);
                WriteDocumentFile(operationRoot, "backup.json", JsonSerializer.Serialize(backup, DocumentJson));
                _ = engine.Apply(plan, document, images);
                output.WriteLine("Plan applied. Current state export: backup.json");
                return 0;
            }
            case "verify":
                return WriteVerificationStatus(engine.Verify(document, images, command.Organization), output);
            case "export":
            {
                var backup = engine.Export(document, images, command.Organization);
                WriteDocumentFile(operationRoot, "backup.json", JsonSerializer.Serialize(backup, DocumentJson));
                output.WriteLine("Metadata export created.");
                return 0;
            }
            case "rollback":
            {
                var backup = ReadDocumentFile<DocumentDeploymentBackup>(command.InputPath!, organizationRoot);
                if (backup.Organization != command.Organization || backup.Assemblies is null ||
                    backup.Assemblies.Count is < 1 or > 2)
                    throw new InvalidDataException("Backup index organization or assembly is invalid.");
                foreach (var item in backup.Assemblies)
                    if (item is null || !IsDocumentPath(organizationRoot, item.Path))
                        throw new InvalidDataException("Backup path is outside the organization boundary.");
                engine.Rollback(backup, document, images);
                output.WriteLine("Rollback completed.");
                return 0;
            }
            default:
                throw new InvalidDataException("Deployment document command is invalid.");
        }
    }

    /// <summary>
    /// 對已開啟且禁止寫入替換的 handle 驗四 MiB 預算，嚴格解析完整候選後才回傳。
    /// JSON 解析失敗不保留原文或內層例外；結構及即時 SHA 仍由核心驗證。
    /// </summary>
    private static T ReadDocumentFile<T>(string path, string organizationRoot) where T : class
    {
        if (!IsDocumentPath(organizationRoot, path)) throw new InvalidDataException("Document path is invalid.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 or > 4 * 1024 * 1024) throw new InvalidDataException("Document exceeds the size limit.");
        try
        {
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 32 });
            Schema.SchemaFiles.RejectDuplicateProperties(document.RootElement);
            return document.RootElement.Deserialize<T>(DocumentJson) ?? throw new InvalidDataException("Document format is invalid.");
        }
        catch (JsonException) { throw new InvalidDataException("Document format is invalid."); }
    }

    /// <summary>
    /// 使用主要 partial 的完整包含關係及所有祖先 reparse point 檢查；
    /// 本機檔案樹須由部署操作者擁有，檢查不等同對可惡意並行置換祖先的 OS 沙箱。
    /// </summary>
    private static bool IsDocumentPath(string root, string path)
    {
        return !string.IsNullOrWhiteSpace(path) && IsContainedPath(root, path, out _);
    }

    /// <summary>
    /// 固定檔名、UTF-8 無 BOM 與 CRLF，最多四 MiB；flush 並關檔後才原子發布。
    /// 不覆寫舊證據，任何失敗由本操作清理唯一暫存檔；已發布索引留待明確回復或人工清理。
    /// </summary>
    private static void WriteDocumentFile(string directory, string name, string content)
    {
        if (name is not ("plan.json" or "plan.md" or "backup.json" or "switch-plan.json" or "switch-plan.md" or "legacy-before.json" or "church-defaults-plan.json" or "church-defaults-plan.md" or "observe.json" or "observe.md" or "live-check.json"))
            throw new InvalidDataException("Document name is invalid.");
        WriteDocumentContent(directory, name, content);
    }

    /// <summary>已核定名稱的共用原子發布；Live 日期檔名由 caller 產生，仍拒路徑片段及 links。</summary>
    private static void WriteDocumentContent(string directory, string name, string content)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || Path.GetFileName(name) != name ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new InvalidDataException("Document name is invalid.");
        Schema.SchemaDeploymentCli.EnsureNoLinks(Path.Combine(directory, name));
        var text = content.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n')
            .Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n";
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("Output document exceeds the size limit.");
        var path = Path.Combine(directory, name);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            System.IO.File.Move(temporary, path, overwrite: false);
        }
        finally
        {
            if (System.IO.File.Exists(temporary)) System.IO.File.Delete(temporary);
        }
    }
}

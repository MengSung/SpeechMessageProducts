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
        Func<string, string>? secretReader)
    {
        var images = document.Assemblies.Select(manifest => AssemblyCatalog.Read(
            Path.Combine(repositoryRoot, "artifacts", "plugins", manifest.AssemblyName + ".dll"), manifest)).ToArray();
        var engine = new DocumentDeploymentEngine(service, organizationRoot, TimeProvider.System,
            name => ResolveRequiredSecret(command.Organization, name, secretReader));
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
                    throw new InvalidDataException("計畫組織與命令不一致。");
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
                    throw new InvalidDataException("備份索引組織或組件無效。");
                foreach (var item in backup.Assemblies)
                    if (item is null || !IsDocumentPath(organizationRoot, item.Path))
                        throw new InvalidDataException("備份路徑超出組織邊界。");
                engine.Rollback(backup, document, images);
                output.WriteLine("Rollback completed.");
                return 0;
            }
            default:
                throw new InvalidDataException("完整文件命令無效。");
        }
    }

    /// <summary>
    /// 對已開啟且禁止寫入替換的 handle 驗四 MiB 預算，嚴格解析完整候選後才回傳。
    /// JSON 解析失敗不保留原文或內層例外；結構及即時 SHA 仍由核心驗證。
    /// </summary>
    private static T ReadDocumentFile<T>(string path, string organizationRoot) where T : class
    {
        if (!IsDocumentPath(organizationRoot, path)) throw new InvalidDataException("文件路徑無效。");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 or > 4 * 1024 * 1024) throw new InvalidDataException("文件超出大小上限。");
        try
        {
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 32 });
            Schema.SchemaFiles.RejectDuplicateProperties(document.RootElement);
            return document.RootElement.Deserialize<T>(DocumentJson) ?? throw new InvalidDataException("文件格式無效。");
        }
        catch (JsonException) { throw new InvalidDataException("文件格式無效。"); }
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
        if (name is not ("plan.json" or "plan.md" or "backup.json"))
            throw new InvalidDataException("文件名稱無效。");
        var text = content.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n')
            .Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n";
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("輸出文件超出大小上限。");
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

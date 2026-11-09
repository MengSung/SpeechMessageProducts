/*
檔案整體說明
目的與定位：將已完整驗證的路由及事件候選發布到本工具專用 Golden 目錄，避免中途失敗留下半份 JSON。
主要功能與流程：先在相鄰唯一 staging 寫齊十三檔並關閉 stream，再檢查舊目錄只有本工具擁有的檔案；以目錄搬移保存舊版、發布新版，發布失敗時還原舊版。輸出採 UTF-8 無 BOM、CRLF 與末尾 CRLF。
使用邊界與維護：這是離線維護工具，不供多程序同時寫入同一目的地；caller 必須序列化發布。所有 staging／backup 都由單次呼叫唯一擁有，不遞迴刪除來源或任何外部目錄。只有發布與 rollback 同時被外部故障阻擋時保留唯一 backup 交操作者復原；不得把唯一舊資料當暫存刪除。未知既有檔案、重複名稱或非法相對路徑必須拒絕；無網路或背景 task。
*/
using System.Text;

namespace LegacyGolden;

/// <summary>完整候選的檔案發布 owner；不會為了重生 fixture 刪除其他工作包產物。</summary>
internal static class GoldenOutput
{
    /// <summary>寫齊候選才替換專用目錄；失敗回復舊目錄，rollback 也失敗則保留唯一救援 backup。可選 move 委派只供內部故障注入，正式呼叫使用 Directory.Move。</summary>
    internal static void Publish(string destination, IReadOnlyDictionary<string, string> files, Action<string, string>? moveDirectory = null)
    {
        moveDirectory ??= Directory.Move;
        LegacySourceReader.ValidatePath(destination);
        var full = Path.GetFullPath(destination);
        var parent = Path.GetDirectoryName(full) ?? throw new InvalidDataException("Output needs a parent directory.");
        var keys = files.Keys.ToHashSet(StringComparer.Ordinal);
        if (keys.Count != files.Count || keys.Count is 0 or > 13 || keys.Any(path =>
            !(path == "Keywords/routing.json" || path.StartsWith("Events/synthetic-", StringComparison.Ordinal) && path.EndsWith(".json", StringComparison.Ordinal)) ||
            path.Contains("..", StringComparison.Ordinal) || path.Contains('\\', StringComparison.Ordinal)))
            throw new InvalidDataException("Output contains an unowned filename.");
        if (Directory.Exists(full))
        {
            var existing = ExistingFiles(full, full);
            if (existing.Any(path => !keys.Contains(Path.GetRelativePath(full, path).Replace('\\', '/'))))
                throw new InvalidDataException("Output contains files owned by another task.");
        }
        Directory.CreateDirectory(parent);
        var suffix = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(parent, ".legacy-golden-stage-" + suffix);
        var backup = Path.Combine(parent, ".legacy-golden-backup-" + suffix);
        var moved = false;
        var published = false;
        try
        {
            Directory.CreateDirectory(staging);
            foreach (var entry in files)
            {
                var target = Path.Combine(staging, entry.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false, true));
                writer.Write(entry.Value);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            if (Directory.Exists(full)) { moveDirectory(full, backup); moved = true; }
            try { moveDirectory(staging, full); published = true; }
            catch (Exception publishFailure)
            {
                if (moved)
                {
                    try { moveDirectory(backup, full); moved = false; }
                    catch (Exception restoreFailure)
                    {
                        // 唯一舊快照必須留下供操作者復原，同時保留原發布及還原兩個真實例外。
                        throw new AggregateException("Publication and rollback failed; previous snapshot retained in backup.", publishFailure, restoreFailure);
                    }
                }
                throw;
            }
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            if (moved && published) Directory.Delete(backup, recursive: true);
        }
    }

    /// <summary>逐目錄檢查 reparse 後才下降，避免遞迴掃描透過 junction 跨越所有權。</summary>
    private static List<string> ExistingFiles(string root, string current)
    {
        var files = new List<string>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(current))
        {
            LegacySourceReader.ValidatePath(entry);
            if (Directory.Exists(entry))
            {
                if (Path.GetRelativePath(root, entry) is not ("Events" or "Keywords"))
                    throw new InvalidDataException("Output contains a directory owned by another task.");
                files.AddRange(ExistingFiles(root, entry));
            }
            else files.Add(entry);
            if (files.Count > 13) throw new InvalidDataException("Output contains too many files.");
        }
        return files;
    }
}

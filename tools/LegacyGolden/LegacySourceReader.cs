/*
檔案整體說明
目的與定位：對可信舊方案的十個非設定來源建立無損轉碼快照，是工具唯一的外部來源讀取邊界。
主要功能與流程：逐路徑拒絕 reparse 與越界，單檔限制一 MiB；用 .NET CP950 exception fallback 解碼並逐 byte 往返。只複製白名單檔到本次唯一暫存目錄，呼叫既有 P0-05 converter，再驗證文字、行數與 hash，最後重新讀原檔確認未變。
使用邊界與維護：呼叫者必須提供已審核且可信的來源根；白名單不代表可執行不可信程式的沙箱。不讀 appsettings、私人識別或 CRM 資料，沒有網路。單次呼叫擁有轉碼程序、取消來源、stream 與暫存目錄，六十秒逾時先終止子樹並排空，再刪自有副本；所有成功與失敗均清理。輸出只有來源值與 metadata，不保留 process 或跨呼叫快取。
*/
using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LegacyGolden;

/// <summary>白名單快照與 P0-05 轉碼的唯一 owner；不接受任意額外來源或設定檔。</summary>
internal static class LegacySourceReader
{
    /// <summary>兩個必須原樣編譯的關鍵字來源之一；路徑以舊方案根為準。</summary>
    internal const string RoutingPath = "LineMessagePlugIn/Processors/Keywords/MessageRoutingKeywords.cs";
    /// <summary>六個真正 helper 的來源；不能換成另一副本或自行重寫 matcher。</summary>
    internal const string MatcherPath = "LineMessagePlugIn/Processors/Keywords/MessageKeywordMatcher.cs";
    /// <summary>唯讀白名單固定且不可變；其餘八檔只做靜態回覆與委派追溯，絕不載入執行。</summary>
    internal static ImmutableArray<string> AllowedPaths { get; } =
    [
        RoutingPath, MatcherPath,
        "LineMessagePlugIn/Processors/Core/IncomingMessageProcessor.Routing.cs",
        "LineMessagePlugIn/Processors/Core/IncomingMessageProcessor.Reply.cs",
        "LineMessagePlugIn/Processors/Handlers/IncomingMessageProcessor.Attendance.cs",
        "LineMessagePlugIn/Processors/Handlers/IncomingMessageProcessor.Enrollment.cs",
        "LineMessagePlugIn/Processors/Handlers/IncomingMessageProcessor.Misc.cs",
        "LineMessagePlugIn/Processors/Handlers/IncomingMessageProcessor.Prayer.cs",
        "LineApiWebhook/Handlers/LineBotMessageHandler.cs",
        "LineApiWebhook/Processors/WebhookMessageProcessor.cs"
    ];

    /// <summary>建立完整候選後才回傳；取消或任何轉碼錯誤不會發布部分來源，原檔永不寫入。</summary>
    internal static async Task<ImmutableArray<SourceDocument>> ReadAsync(string sourceRoot, string scratchRoot,
        string converterPath, CancellationToken cancellationToken)
    {
        ValidatePath(sourceRoot);
        ValidatePath(scratchRoot);
        ValidatePath(converterPath);
        var source = Path.GetFullPath(sourceRoot);
        var scratch = Path.GetFullPath(scratchRoot);
        if (IsWithin(source, scratch) || IsWithin(scratch, source))
            throw new InvalidDataException("Source and scratch roots must be separate.");
        var temporary = Path.Combine(scratch, "legacy-golden-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var originals = new Dictionary<string, (byte[] Bytes, string Text)>(StringComparer.Ordinal);
            var copies = new List<string>();
            foreach (var relative in AllowedPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = ReadAllowed(source, relative);
                var text = DecodeCp950(bytes);
                originals.Add(relative, (bytes, text));
                var target = Path.Combine(temporary, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, bytes);
                copies.Add(target);
            }
            var manifest = Path.Combine(temporary, "conversion.json");
            try { await ConvertAsync(converterPath, copies, manifest, cancellationToken).ConfigureAwait(false); }
            finally
            {
                // 原始 P0-05 manifest 只留在呼叫者指定的 ignored evidence；副本本體仍於 finally 刪除。
                if (File.Exists(manifest)) File.Copy(manifest, Path.Combine(scratch, Path.GetFileName(temporary) + "-conversion.json"), overwrite: false);
            }
            if (!File.Exists(manifest)) throw new InvalidDataException("P0-05 manifest is missing.");
            using var audit = ReadManifest(manifest);
            var records = audit.RootElement.GetProperty("files").EnumerateArray().ToArray();
            if (audit.RootElement.GetProperty("failureCount").GetInt32() != 0 || audit.RootElement.GetProperty("dryRun").GetBoolean() || records.Length != AllowedPaths.Length)
                throw new InvalidDataException("P0-05 manifest is incomplete.");
            var result = ImmutableArray.CreateBuilder<SourceDocument>();
            foreach (var relative in AllowedPaths)
            {
                var bytes = File.ReadAllBytes(Path.Combine(temporary, relative));
                var text = new UTF8Encoding(false, true).GetString(bytes);
                var original = originals[relative];
                var record = records.Single(item => string.Equals(item.GetProperty("path").GetString(), Path.GetFullPath(Path.Combine(temporary, relative)), StringComparison.OrdinalIgnoreCase));
                if (record.GetProperty("status").GetString() != "applied" || !record.GetProperty("stringLiteralsUnchanged").GetBoolean() ||
                    !string.Equals(record.GetProperty("beforeSha256").GetString(), Hash(original.Bytes), StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(record.GetProperty("afterSha256").GetString(), Hash(bytes), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("P0-05 manifest does not match the source bytes.");
                // P0-05 只允許換行正規化；除最後 CRLF 之外任何文字改動或行數漂移均拒絕。
                if (!string.Equals(text, Normalize(original.Text), StringComparison.Ordinal) ||
                    CountLines(text) != CountLines(original.Text) || !ReadAllowed(source, relative).AsSpan().SequenceEqual(original.Bytes))
                    throw new InvalidDataException("Source changed or conversion lost information.");
                result.Add(new SourceDocument(relative, text, Hash(original.Bytes), Hash(bytes), CountLines(text)));
            }
            return result.ToImmutable();
        }
        finally
        {
            // temporary 是本次產生的固定根下唯一子目錄，從不刪呼叫者的 scratchRoot。
            Directory.Delete(temporary, recursive: true);
        }
    }

    /// <summary>每次讀取前重新檢查完整路徑與檔案大小；設定、路徑穿越與不在名單的來源一律拒絕。</summary>
    internal static byte[] ReadAllowed(string root, string relative)
    {
        if (!AllowedPaths.Contains(relative, StringComparer.Ordinal)) throw new InvalidDataException("Source is not allowlisted.");
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!IsWithin(Path.GetFullPath(root), path)) throw new InvalidDataException("Source escaped its root.");
        ValidatePath(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 1024 * 1024) throw new InvalidDataException("Source exceeds one MiB.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>使用 .NET 本地 provider，避免修改程序全域編碼註冊；錯誤或非唯一 CP950 映射不得通過。</summary>
    internal static string DecodeCp950(byte[] bytes)
    {
        var encoding = CodePagesEncodingProvider.Instance.GetEncoding(950, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)!;
        var text = encoding.GetString(bytes);
        if (text.Contains('\uFFFD', StringComparison.Ordinal) || !encoding.GetBytes(text).AsSpan().SequenceEqual(bytes))
            throw new InvalidDataException("CP950 round trip is not byte-exact.");
        return text;
    }

    /// <summary>不改變非換行文字的固定 CRLF 表示；原行號另由 CountLines 驗證。</summary>
    internal static string Normalize(string text)
    {
        var normalized = text.ReplaceLineEndings("\r\n");
        return normalized.EndsWith("\r\n", StringComparison.Ordinal) ? normalized : normalized + "\r\n";
    }
    /// <summary>最後單一換行是檔案終止，不計額外空行；保留中間所有空行與原始位置。</summary>
    internal static int CountLines(string text)
    {
        var normalized = text.ReplaceLineEndings("\n");
        return normalized.Count(character => character == '\n') + (normalized.EndsWith('\n') ? 0 : 1);
    }
    /// <summary>固定大寫 SHA256 僅用於完整性，不保存來源本文或秘密。</summary>
    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    /// <summary>包含分隔符的完整根比較，防止相似名稱與路徑穿越混入。</summary>
    internal static bool IsWithin(string root, string path) => string.Equals(root, path, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>逐層拒絕符號連結與 junction；不存在的輸出末端允許建立，既有祖先仍全部驗證。</summary>
    internal static void ValidatePath(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Reparse points are not allowed.");
    }

    /// <summary>只執行受信任 P0-05 腳本；參數以 PowerShell literal 逸出再編碼，六十秒後殺整棵子程序且等待 stdout/stderr 排空。</summary>
    private static async Task ConvertAsync(string converter, List<string> copies, string manifest, CancellationToken cancellationToken)
    {
        var command = "& " + Quote(Path.GetFullPath(converter)) + " -Path @(" +
            string.Join(",", copies.Select(Quote)) + ") -ManifestPath " + Quote(manifest) + " -Apply";
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command)) })
            start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        process.Start();
        var stdout = DrainAsync(process.StandardOutput);
        var stderr = DrainAsync(process.StandardError);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        }
        if (process.ExitCode != 0) throw new InvalidDataException("P0-05 conversion failed.");
    }

    /// <summary>固定緩衝排空而不保存任意子程序輸出；診斷由轉碼 manifest 與上層固定錯誤提供。</summary>
    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[2048];
        while (await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false) != 0) { }
    }

    /// <summary>稽核 manifest 有一 MiB 與深度八上限；stream 先關閉，回傳 JsonDocument 由 ReadAsync 的 using 擁有。</summary>
    private static JsonDocument ReadManifest(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 1024 * 1024) throw new InvalidDataException("P0-05 manifest exceeds limit.");
        return JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 8 });
    }

    /// <summary>PowerShell 單引號 literal 內的引號加倍；不把路徑插入未逸出的命令語法。</summary>
    private static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}

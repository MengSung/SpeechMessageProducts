/*
 * 檔案整體說明
 *
 * 目的與定位：管理單一命令的本機檔案邊界與所有權；禁止外部路徑、重新解析點及過大輸入。 寫入只持有同目錄的唯一暫存檔，成功原子替換，失敗 finally 清除，不保留串流或背景工作。
 *
 * 主要功能與流程：
 * 本檔宣告 LocalFiles。
 * - 單一來源最大十六 MiB，限制惡意或誤選的大檔造成無界記憶體保留。
 * - 正規化相對路徑後確認仍在明確選定的 repo 內，不接受父 worktree 檔案。
 * - 在配置記憶體前拒絕過大檔與重新解析點，不在錯誤訊息輸出來源內容。
 * - 讀取既有來源；UTF-8 失敗才以嚴格 CP950 盤點，不改寫舊來源編碼。
 * 主要操作入口包含 Resolve、CheckFile、ReadSource、ReadXml、WriteAtomic。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 tools/I18nCheck.Tests/InputIntegrityTests.cs、tools/I18nCheck/CommandRunner.cs、
 * tools/I18nCheck/LengthLimits.cs、tools/I18nCheck/RepositoryScanner.cs、tools/I18nCheck/ResourceCatalog.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace I18nCheck;

/// <summary>
/// 管理單一命令的本機檔案邊界與所有權；禁止外部路徑、重新解析點及過大輸入。
/// 寫入只持有同目錄的唯一暫存檔，成功原子替換，失敗 finally 清除，不保留串流或背景工作。
/// </summary>
internal static class LocalFiles
{
    /// <summary>單一來源最大十六 MiB，限制惡意或誤選的大檔造成無界記憶體保留。</summary>
    internal const int MaxFileBytes = 16 * 1024 * 1024;

    /// <summary>正規化相對路徑後確認仍在明確選定的 repo 內，不接受父 worktree 檔案。</summary>
    /// <param name="root">本命令固定的方案根目錄。</param>
    /// <param name="path">相對或絕對本機路徑。</param>
    /// <returns>已確認所有既有父目錄均非重新解析點的絕對路徑。</returns>
    internal static string Resolve(string root, string path)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.GetFullPath(path, fullRoot);
        if (!full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("I18N201: path leaves the selected repository.");
        for (var cursor = full; cursor.Length >= fullRoot.Length; cursor = Path.GetDirectoryName(cursor)!)
        {
            if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("I18N202: reparse points are not allowed.");
            if (cursor.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)) break;
        }
        return full;
    }

    /// <summary>在配置記憶體前拒絕過大檔與重新解析點，不在錯誤訊息輸出來源內容。</summary>
    /// <param name="path">已選定的檔案。</param>
    internal static void CheckFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("I18N203: required input file is missing.");
        if (info.Length > MaxFileBytes || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("I18N204: input size or file boundary is invalid.");
    }

    /// <summary>讀取既有來源；UTF-8 失敗才以嚴格 CP950 盤點，不改寫舊來源編碼。</summary>
    /// <param name="path">受方案邊界約束的來源檔。</param>
    /// <returns>只供本次語法分析的文字。</returns>
    internal static string ReadSource(string path)
    {
        CheckFile(path);
        var bytes = File.ReadAllBytes(path);
        try
        {
            var offset = bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 }) ? 3 : 0;
            return new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
        }
        catch (DecoderFallbackException)
        {
            return CodePagesEncodingProvider.Instance.GetEncoding(950, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)!.GetString(bytes);
        }
    }

    /// <summary>共用受限 XML 讀取器，禁止 DTD 與外部解析；讀取失敗也會釋放全部 reader。</summary>
    /// <param name="path">已由呼叫者確認本機邊界的 XML 檔案。</param>
    /// <returns>由呼叫者持有、不再引用串流的 XML 文件。</returns>
    internal static XDocument ReadXml(string path)
    {
        using var text = new StringReader(ReadSource(path));
        using var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxFileBytes });
        return XDocument.Load(reader);
    }

    /// <summary>以唯一本命令暫存檔進行原子替換；所有寫入把手在移動前已關閉。</summary>
    /// <param name="path">由呼叫者 Resolve 過的目的地。</param>
    /// <param name="bytes">完整候選內容。</param>
    internal static void WriteAtomic(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".i18n-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

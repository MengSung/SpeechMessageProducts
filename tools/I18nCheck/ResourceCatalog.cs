/*
 * 檔案整體說明
 *
 * 目的與定位：驗證後的 resx 快照；載入期間以區域字典組裝，完整成功後才發布不可變集合。 XML reader 在成功與失敗路徑均立即 Dispose，禁止 DTD 與外部解析，單檔十六 MiB、
 * 每組最多64個原始語系／每檔20000鍵／每值32768個UTF16；快照不保留檔案把手。
 *
 * 主要功能與流程：
 * 本檔宣告 ResourceCatalog。
 * - 鍵必須分層且各段以大寫開頭；使用完整字串邊界，避免 $ 允許尾端換行。
 * - 固定基礎語系順序，不隨載入次數重複配置，也不向外部公開可變陣列。
 * - 每個語系的唯讀鍵值，只有靜態資源，不含執行中的使用者、教會或憑證。
 * - 產出語系的固定順序：zh-TW、en、其他語系 Ordinal 排序、qps-ploc。
 * 主要操作入口包含 Load、Read。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 tools/I18nCheck.Tests/BoundaryTests.cs、tools/I18nCheck.Tests/I18nContractsTests.cs、
 * tools/I18nCheck.Tests/InputIntegrityTests.cs、tools/I18nCheck.Tests/ResourceVerificationTests.cs、
 * tools/I18nCheck/CommandRunner.cs 追查目前工作樹中的合作／使用位置。
 */
using System.Collections.Frozen;
using System.Globalization;
using System.Text.RegularExpressions;

namespace I18nCheck;

/// <summary>
/// 驗證後的 resx 快照；載入期間以區域字典組裝，完整成功後才發布不可變集合。
/// XML reader 在成功與失敗路徑均立即 Dispose，禁止 DTD 與外部解析，單檔十六 MiB、
/// 每組最多64個原始語系／每檔20000鍵／每值32768個UTF16；快照不保留檔案把手。
/// </summary>
internal sealed class ResourceCatalog
{
    /// <summary>鍵必須分層且各段以大寫開頭；使用完整字串邊界，避免 $ 允許尾端換行。</summary>
    private static readonly Regex KeyPattern = new(@"\A[A-Z][A-Za-z0-9]*(\.[A-Z][A-Za-z0-9]*)+\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    /// <summary>固定基礎語系順序，不隨載入次數重複配置，也不向外部公開可變陣列。</summary>
    private static readonly string[] BaseCultures = ["zh-TW", "en"];

    /// <summary>每個語系的唯讀鍵值，只有靜態資源，不含執行中的使用者、教會或憑證。</summary>
    internal FrozenDictionary<string, FrozenDictionary<string, string>> Values { get; }

    /// <summary>產出語系的固定順序：zh-TW、en、其他語系 Ordinal 排序、qps-ploc。</summary>
    internal IReadOnlyList<string> Cultures { get; }

    /// <summary>只接受已驗證的區域候選，將集合凍結後才交給產生器。</summary>
    /// <param name="values">本次載入建立的全部語系。</param>
    private ResourceCatalog(Dictionary<string, FrozenDictionary<string, string>> values)
    {
        Values = values.ToFrozenDictionary(StringComparer.Ordinal);
        Cultures = Array.AsReadOnly(BaseCultures.Concat(values.Keys.Where(key => key is not "zh-TW" and not "en").Order(StringComparer.Ordinal)).Append("qps-ploc").ToArray());
    }

    /// <summary>
    /// 以中性resx為zh-TW，載入同名翻譯；即使顯式generate也先限制組級64MiB/64檔，
    /// 不讓單檔16MiB上限乘上大量語系後保留巨量候選。重複鍵／參數差異／保留語系拒絕。
    /// </summary>
    /// <param name="path">中性資源的本機路徑。</param>
    /// <returns>沒有開啟串流或可變來源參考的資源快照。</returns>
    internal static ResourceCatalog Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        var prefix = Path.GetFileNameWithoutExtension(fullPath) + ".";
        LocalFiles.CheckFile(fullPath);
        long bytes = new FileInfo(fullPath).Length;
        var translationPaths = new List<string>();
        int directoryFiles = 0;
        // 先有界收集再排序；直接對EnumerateFiles呼叫Order會先配置全部相鄰檔名，
        // 使顯式generate在大量無關resx的目錄中繞過本組64檔的記憶體預算。
        foreach (var translationPath in Directory.EnumerateFiles(directory, "*.resx"))
        {
            if (++directoryFiles > 20_000) throw new InvalidDataException("I18N617: resource directory entry budget exceeded.");
            var stem = Path.GetFileNameWithoutExtension(translationPath);
            if (!stem.StartsWith(prefix, StringComparison.Ordinal)) continue;
            LocalFiles.CheckFile(translationPath);
            bytes += new FileInfo(translationPath).Length;
            if (bytes > 64L * 1024 * 1024) throw new InvalidDataException("I18N616: resource group size budget exceeded.");
            if (translationPaths.Count >= 63) throw new InvalidDataException("I18N109: resource culture budget exceeded.");
            translationPaths.Add(translationPath);
        }
        translationPaths.Sort(StringComparer.Ordinal);
        var neutral = Read(fullPath);
        var values = new Dictionary<string, FrozenDictionary<string, string>>(StringComparer.Ordinal) { ["zh-TW"] = neutral };
        foreach (var translationPath in translationPaths)
        {
            var stem = Path.GetFileNameWithoutExtension(translationPath);
            var culture = CultureInfo.GetCultureInfo(stem[prefix.Length..]).Name;
            if (string.Equals(culture, "zh-TW", StringComparison.OrdinalIgnoreCase)
                || string.Equals(culture, "qps-ploc", StringComparison.OrdinalIgnoreCase) || values.ContainsKey(culture))
                throw new InvalidDataException("I18N101: reserved or duplicate resource culture.");
            var translated = Read(translationPath);
            foreach (var pair in translated)
            {
                if (!neutral.TryGetValue(pair.Key, out var original))
                    throw new InvalidDataException("I18N102: translation key has no neutral source.");
                if (!Placeholders.Names(original).SetEquals(Placeholders.Names(pair.Value)))
                    throw new InvalidDataException("I18N103: resource placeholder name sets differ.");
            }
            values.Add(culture, translated);
        }
        values.TryAdd("en", FrozenDictionary<string, string>.Empty);
        if (values.Count > 64) throw new InvalidDataException("I18N109: resource culture budget exceeded.");
        return new ResourceCatalog(values);
    }

    /// <summary>只讀字串 data/value，不反序列化 resx 型別；碰到不明內容時封閉失敗。</summary>
    /// <param name="path">單一資源檔案。</param>
    /// <returns>檢查唯一性及格式後的不可變鍵值。</returns>
    private static FrozenDictionary<string, string> Read(string path)
    {
        var document = LocalFiles.ReadXml(path);
        if (document.Root?.Name != "root") throw new InvalidDataException("I18N104: resource root is invalid.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var data in document.Root.Elements("data"))
        {
            var key = (string?)data.Attribute("name");
            var value = data.Element("value");
            if (key is null || !KeyPattern.IsMatch(key)) throw new InvalidDataException("I18N105: resource key format is invalid.");
            if (value is null || data.Attribute("type") is not null || data.Attribute("mimetype") is not null)
                throw new InvalidDataException("I18N106: resource must contain a plain string value.");
            if (!result.TryAdd(key, value.Value)) throw new InvalidDataException("I18N107: duplicate resource key.");
            if (result.Count > 20_000 || value.Value.Length > 32_768)
                throw new InvalidDataException("I18N110: resource value or key budget exceeded.");
        }
        return result.ToFrozenDictionary(StringComparer.Ordinal);
    }
}

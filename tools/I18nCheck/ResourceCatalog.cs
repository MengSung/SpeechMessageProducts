using System.Collections.Frozen;
using System.Globalization;
using System.Text.RegularExpressions;

namespace I18nCheck;

/// <summary>
/// 驗證後的 resx 快照；載入期間以區域字典組裝，完整成功後才發布不可變集合。
/// XML reader 在成功與失敗路徑均立即 Dispose，禁止 DTD 與外部解析，大小限制為十六 MiB。
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

    /// <summary>以中性 resx 為 zh-TW，載入同名的其他語系；重複鍵及參數差異直接拒絕。</summary>
    /// <param name="path">中性資源的本機路徑。</param>
    /// <returns>沒有開啟串流或可變來源參考的資源快照。</returns>
    internal static ResourceCatalog Load(string path)
    {
        var neutral = Read(path);
        var values = new Dictionary<string, FrozenDictionary<string, string>>(StringComparer.Ordinal) { ["zh-TW"] = neutral };
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        var prefix = Path.GetFileNameWithoutExtension(fullPath) + ".";
        foreach (var translationPath in Directory.EnumerateFiles(directory, "*.resx").Order(StringComparer.Ordinal))
        {
            var stem = Path.GetFileNameWithoutExtension(translationPath);
            if (!stem.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var culture = stem[prefix.Length..];
            _ = CultureInfo.GetCultureInfo(culture);
            if (culture is "zh-TW" or "qps-ploc" || values.ContainsKey(culture))
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
        }
        return result.ToFrozenDictionary(StringComparer.Ordinal);
    }
}

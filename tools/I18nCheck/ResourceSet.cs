using System.Collections.Frozen;
using System.Globalization;

namespace I18nCheck;

/// <summary>
/// 一組中性資源與其翻譯的操作區域快照；Path 是穩定的來源身分，不以顯示文字區分。
/// 僅由完整 repository 候選建立，沒有開啟串流、使用者資料或背景工作。
/// </summary>
/// <param name="Path">根內的中性 resx 絕對路徑，不對診斷輸出絕對機器路徑。</param>
/// <param name="Catalog">已驗證的不可變原始翻譯。</param>
internal sealed record ResourceSet(string Path, ResourceCatalog Catalog)
{
    /// <summary>只保存平台固定文化名稱，無來源或租戶資料；用以辨認缺中性來源的標準翻譯檔。</summary>
    private static readonly FrozenSet<string> KnownCultures = CultureInfo.GetCultures(CultureTypes.AllCultures)
        .Select(culture => culture.Name).ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 沿用正式方案非測試專案與剪枝邊界；先限制全部實體 resx 最多4096檔、合計64MiB，
    /// 再讀完整候選。相同實體檔被兩專案重述直接拒絕，不以Distinct或覆寫隱藏衝突。
    /// </summary>
    /// <param name="root">已選定方案根；不向父工作樹尋找缺失來源。</param>
    /// <returns>中性組依路徑固定排序；單次命令擁有的唯讀集合。</returns>
    internal static IReadOnlyList<ResourceSet> LoadRepository(string root)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long bytes = 0;
        foreach (var project in RepositoryScanner.ProjectPaths(root))
        foreach (var path in RepositoryScanner.Files(root, System.IO.Path.GetDirectoryName(project)!))
        {
            if (!path.EndsWith(".resx", StringComparison.OrdinalIgnoreCase)) continue;
            LocalFiles.CheckFile(path);
            if (!paths.Add(path)) throw new InvalidDataException("I18N604: resource file identity is repeated.");
            bytes += new FileInfo(path).Length;
            if (paths.Count > 4096 || bytes > 64L * 1024 * 1024)
                throw new InvalidDataException("I18N605: repository resource budget exceeded.");
        }
        var candidate = new List<ResourceSet>();
        foreach (var path in paths.Order(StringComparer.Ordinal))
        {
            var stem = System.IO.Path.GetFileNameWithoutExtension(path);
            int split = stem.LastIndexOf('.');
            if (split > 0 && paths.Contains(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, stem[..split] + ".resx"))) continue;
            if (split > 0 && KnownCultures.Contains(stem[(split + 1)..]))
                throw new InvalidDataException("I18N615: translation has no neutral resource.");
            candidate.Add(new ResourceSet(path, ResourceCatalog.Load(path)));
        }
        return candidate.AsReadOnly();
    }
}

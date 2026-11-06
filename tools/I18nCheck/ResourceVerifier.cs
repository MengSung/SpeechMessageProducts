/*
 * 檔案整體說明
 *
 * 目的與定位：P1-09完整資源守門；所有集合只屬於一次verify，全部成功組裝才輸出結果。 不保留來源到靜態狀態，不修改檔案、啟動Task或建立服務client；診斷不含資源文字與教會名稱。
 *
 * 主要功能與流程：
 * 本檔宣告 ResourceVerifier。
 * - 跨中性組驗穩定key、所有原始文化及pseudo長度、跨文化關鍵字；英文缺譯只報告不失敗。 每組已由ResourceCatalog驗證key/參數集合；本層不另寫第二份XML或參數解析器。
 * - 使用Sender相同UTF16單位而非Rune放寬上限；只計算替換後長度，不配置膨脹後字串。 受限模板只能使用已明確提供最長值的churchName，不能替其他參數猜短值而假通過。
 * - 與產品KeywordCatalog相同的NFKC/Unicode空白合併/Invariant小寫；不記錄輸入或永久保存。
 * 主要操作入口包含 Verify、CheckLength、NormalizeKeyword。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 tools/I18nCheck.Tests/ResourceVerificationTests.cs、tools/I18nCheck/CommandRunner.cs 追查目前工作樹中的合作／使用位置。
 */
using System.Globalization;
using System.Text;

namespace I18nCheck;

/// <summary>
/// P1-09完整資源守門；所有集合只屬於一次verify，全部成功組裝才輸出結果。
/// 不保留來源到靜態狀態，不修改檔案、啟動Task或建立服務client；診斷不含資源文字與教會名稱。
/// </summary>
internal static class ResourceVerifier
{
    /// <summary>
    /// 跨中性組驗穩定key、所有原始文化及pseudo長度、跨文化關鍵字；英文缺譯只報告不失敗。
    /// 每組已由ResourceCatalog驗證key/參數集合；本層不另寫第二份XML或參數解析器。
    /// </summary>
    /// <param name="root">選定方案根，只用於安全配置與相對路徑。</param>
    /// <param name="resources">同次命令完整且不可變的資源候選。</param>
    /// <param name="output">caller擁有的writer，只借用至同步方法結束。</param>
    /// <returns>規則全部通過為0，實質衝突為1；非法配置原樣交CLI轉2。</returns>
    internal static int Verify(string root, IReadOnlyList<ResourceSet> resources, TextWriter output)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var english = new HashSet<string>(StringComparer.Ordinal);
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var failures = new List<string>();
        var limits = resources.Count == 0 ? null : LengthLimits.Load(root);
        int aliasCount = 0;
        foreach (var resource in resources)
        {
            string source = RepositoryScanner.Relative(root, resource.Path);
            foreach (var pair in resource.Catalog.Values["zh-TW"])
            {
                if (!keys.Add(pair.Key)) failures.Add("I18N601: duplicate resource key " + pair.Key + " in " + source);
                if (keys.Count > 20_000) throw new InvalidDataException("I18N611: resource key budget exceeded.");
                CheckLength(pair.Key, "qps-ploc", PseudoLocale.Transform(pair.Value), limits!, source, failures);
            }
            foreach (var culture in resource.Catalog.Values)
            foreach (var pair in culture.Value)
            {
                if (culture.Key == "en" && !string.IsNullOrWhiteSpace(pair.Value)) english.Add(pair.Key);
                CheckLength(pair.Key, culture.Key, pair.Value, limits!, source, failures);
                if (!pair.Key.StartsWith("Keyword.", StringComparison.Ordinal)) continue;
                foreach (var alias in pair.Value.Split('|'))
                {
                    if (++aliasCount > 20_000) throw new InvalidDataException("I18N612: keyword alias budget exceeded.");
                    var normalized = NormalizeKeyword(alias);
                    if (normalized.Length == 0) throw new InvalidDataException("I18N613: keyword alias cannot be empty.");
                    if (aliases.TryGetValue(normalized, out var existing) && !string.Equals(existing, pair.Key, StringComparison.Ordinal))
                        failures.Add("I18N603: conflicting keyword intent " + pair.Key + " in " + source + " culture=" + culture.Key);
                    else aliases[normalized] = pair.Key;
                }
            }
        }
        // 候選全部完成後才發布報告；不可把半份成功報告當成已驗證的快照。
        foreach (var failure in failures.Order(StringComparer.Ordinal)) output.WriteLine(failure);
        decimal completion = keys.Count == 0 ? 0 : 100m * english.Count / keys.Count;
        output.WriteLine("en 完成度 " + completion.ToString("F2", CultureInfo.InvariantCulture) + "% (" +
            english.Count.ToString(CultureInfo.InvariantCulture) + "/" + keys.Count.ToString(CultureInfo.InvariantCulture) + ")");
        output.WriteLine("Resource verification: " + (failures.Count == 0 ? "passed" : "failed"));
        return failures.Count == 0 ? 0 : 1;
    }

    /// <summary>
    /// 使用Sender相同UTF16單位而非Rune放寬上限；只計算替換後長度，不配置膨脹後字串。
    /// 受限模板只能使用已明確提供最長值的churchName，不能替其他參數猜短值而假通過。
    /// </summary>
    private static void CheckLength(string key, string culture, string text, LengthLimits limits, string source, List<string> failures)
    {
        int maximum = key == "Line.Sender.Template" ? 20 : key.StartsWith("RichMenu.ChatBar.", StringComparison.Ordinal) ? 14 : 0;
        if (maximum == 0) return;
        long length = Placeholders.LengthWithChurchName(text, limits.LongestChurchName);
        if (length == 0 || length > maximum)
            failures.Add("I18N602: resource length " + key + " in " + source + " culture=" + culture +
                " length=" + length.ToString(CultureInfo.InvariantCulture) + " maximum=" + maximum.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>與產品KeywordCatalog相同的NFKC/Unicode空白合併/Invariant小寫；不記錄輸入或永久保存。</summary>
    private static string NormalizeKeyword(string input)
    {
        var normalized = input.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);
        bool pendingSpace = false;
        foreach (char character in normalized)
        {
            if (char.IsWhiteSpace(character)) { pendingSpace = builder.Length != 0; continue; }
            if (pendingSpace) builder.Append(' ');
            pendingSpace = false;
            builder.Append(character);
        }
        return builder.ToString().ToLowerInvariant();
    }
}

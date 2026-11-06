/*
 * 檔案整體說明
 *
 * 目的與定位：依 2026-11-03 使用者核定公式擴展文字，只在整段外包圍一次，不修改參數。
 *
 * 主要功能與流程：
 * 本檔宣告 Placeholders、PseudoLocale。
 * - 辨識一組大括號內的非空名稱，不把參數任意限縮為 ASCII；巢狀括號不合併。
 * - 僅移除識別成功的整個參數，保留標點、空白與其他 Unicode 字元。
 * - 產生 Ordinal 唯讀集合，重複參數及順序不影響跨語系契約。
 * - 使用同一參數文法計算最長教會名替換後的UTF16長度，不配置實際展開文字或重新解析插入值。 只允許已提供最長值的churchName；未知參數拒絕，最多256處替換以限制錯誤模板成本。
 * 主要操作入口包含 Remove、Names、LengthWithChurchName、Transform。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 tools/I18nCheck.Tests/BoundaryTests.cs、tools/I18nCheck.Tests/I18nContractsTests.cs、
 * tools/I18nCheck.Tests/ResourceVerificationTests.cs、tools/I18nCheck/ResourceCatalog.cs、
 * tools/I18nCheck/ResourceGenerator.cs 追查目前工作樹中的合作／使用位置。
 */
using System.Collections.Frozen;
using System.Text;
using System.Text.RegularExpressions;

namespace I18nCheck;

/// <summary>
/// 唯一的具名參數解析器；偽語系計數與 resx 集合驗證共用完全相同的辨識規則。
/// 靜態正則只保存固定文法，無回溯、無來源快取，不保留任何使用者或教會資料。
/// </summary>
internal static class Placeholders
{
    /// <summary>辨識一組大括號內的非空名稱，不把參數任意限縮為 ASCII；巢狀括號不合併。</summary>
    private static readonly Regex Pattern = new(@"\{(?<name>[^{}]+)\}", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    /// <summary>僅移除識別成功的整個參數，保留標點、空白與其他 Unicode 字元。</summary>
    /// <param name="text">本次資源文字，不存入任何靜態欄位。</param>
    /// <returns>可按 Rune 計數的非參數文字。</returns>
    internal static string Remove(string text) => Pattern.Replace(text, string.Empty);

    /// <summary>產生 Ordinal 唯讀集合，重複參數及順序不影響跨語系契約。</summary>
    /// <param name="text">本次資源文字。</param>
    /// <returns>只包含參數名稱的不可變集合。</returns>
    internal static FrozenSet<string> Names(string text) => Pattern.Matches(text).Select(match => match.Groups["name"].Value).ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// 使用同一參數文法計算最長教會名替換後的UTF16長度，不配置實際展開文字或重新解析插入值。
    /// 只允許已提供最長值的churchName；未知參數拒絕，最多256處替換以限制錯誤模板成本。
    /// </summary>
    /// <param name="text">原始或偽語系受限模板。</param>
    /// <param name="churchName">配置內完整最長名稱，不能依描述中的數字截短。</param>
    /// <returns>以long累計避免重複參數造成int溢位。</returns>
    internal static long LengthWithChurchName(string text, string churchName)
    {
        long length = text.Length;
        int count = 0;
        foreach (Match match in Pattern.Matches(text))
        {
            if (++count > 256 || match.Groups["name"].Value != "churchName")
                throw new InvalidDataException("I18N614: limited template parameter has no bounded value.");
            length += churchName.Length - match.Length;
        }
        return length;
    }
}

/// <summary>依 2026-11-03 使用者核定公式擴展文字，只在整段外包圍一次，不修改參數。</summary>
internal static class PseudoLocale
{
    /// <summary>以 Rune 計算非參數字數，再用整數 ceil(2n/5) 補字；不使用程序文化或時鐘。</summary>
    /// <param name="text">完整中性資源，包含必須原樣保留的參數。</param>
    /// <returns>原文後接至少一個 ǂ，最後才加入右全形方括號。</returns>
    internal static string Transform(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var count = 0;
        foreach (var rune in Placeholders.Remove(text).EnumerateRunes())
        {
            _ = rune;
            count++;
        }
        var padding = (int)Math.Max(1, ((2L * count) + 4) / 5);
        return "［" + text + new string('ǂ', padding) + "］";
    }
}

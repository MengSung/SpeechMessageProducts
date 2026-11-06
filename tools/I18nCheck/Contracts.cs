/*
 * 檔案整體說明
 *
 * 目的與定位：本檔整合下列相關契約：CjkRatchet：以各專案的基準計數限制新增硬編碼文字，減少時提供明確改善清單。；RatchetResult：棘輪的單次不可變判定，名稱清單由產生者獨立持有，沒有教會或使用者資料。
 *
 * 主要功能與流程：
 * 本檔宣告 CjkRatchet、RatchetResult。
 * - 新專案基準為零；比較只建立命令區域結果，不修改任何來源集合。
 * 主要操作入口包含 Compare、RatchetResult。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 tools/I18nCheck.Tests/BoundaryTests.cs、tools/I18nCheck.Tests/I18nContractsTests.cs、
 * tools/I18nCheck/CommandRunner.cs 追查目前工作樹中的合作／使用位置。
 */
namespace I18nCheck;

/// <summary>以各專案的基準計數限制新增硬編碼文字，減少時提供明確改善清單。</summary>
internal static class CjkRatchet
{
    /// <summary>新專案基準為零；比較只建立命令區域結果，不修改任何來源集合。</summary>
    /// <param name="current">本次掃描的各專案計數。</param>
    /// <param name="baseline">已審核的各專案基準。</param>
    /// <returns>判定與減少的專案名稱。</returns>
    internal static RatchetResult Compare(IReadOnlyDictionary<string, int> current, IReadOnlyDictionary<string, int> baseline)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(baseline);
        var passed = current.All(pair => pair.Value <= baseline.GetValueOrDefault(pair.Key));
        var improvements = baseline.Where(pair => current.GetValueOrDefault(pair.Key) < pair.Value)
            .Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray();
        return new RatchetResult(passed, Array.AsReadOnly(improvements));
    }
}

/// <summary>棘輪的單次不可變判定，名稱清單由產生者獨立持有，沒有教會或使用者資料。</summary>
/// <param name="Passed">是否沒有任何專案增加計數。</param>
/// <param name="Improvements">文字數下降的專案名稱。</param>
internal sealed record RatchetResult(bool Passed, IReadOnlyList<string> Improvements);

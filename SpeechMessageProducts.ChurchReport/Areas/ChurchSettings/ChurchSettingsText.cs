/*
 * 檔案整體說明
 *
 * 目的與定位：以 I18nCheck 產生的有限文字表，提供 ChurchSettings Area 的本地化固定文案。
 * 主要功能與流程：使用目前 request 的 UI culture 尋找該資源列；缺少翻譯時回退繁體中文，
 * qps-ploc 只會在啟動政策允許且 request culture 已解析為偽語系時取到生成文字。
 * 使用邊界與維護：文字表只含編譯期固定資源，靜態唯讀查找不保存 request、教會、登入者或 mutable state。
 * 呼叫端不得將使用者輸入當資源鍵，輸入型文字必須仍由 Razor 預設 HTML encoder 輸出。
 * 失敗與驗證：未知鍵明確拒絕；文化比對採序數忽略大小寫。resx 改動後由 I18nCheck 重生並逐位元組檢查。
 */
using System;
using System.Globalization;
using System.Linq;

namespace ChurchReport.Areas.ChurchSettings;

/// <summary>ChurchSettings 固定文字查找器，資料由同目錄 resx 生成。</summary>
internal static class ChurchSettingsText
{
    /// <summary>依目前 request UI culture 取得固定資源文字，沒有英文翻譯時回退繁中。</summary>
    /// <param name="key">只由程式指定的資源鍵。</param>
    /// <returns>對應語系的資源內容；不存在的鍵以 KeyNotFoundException 明確失敗。</returns>
    internal static string GetText(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var row = ChurchSettingsTextData.Rows.FirstOrDefault(
            candidate => string.Equals(candidate[0], key, StringComparison.Ordinal));
        if (row is null) throw new System.Collections.Generic.KeyNotFoundException(key);
        var culture = CultureInfo.CurrentUICulture.Name;
        for (var index = 0; index < ChurchSettingsTextData.Cultures.Length; index++)
        {
            if (string.Equals(ChurchSettingsTextData.Cultures[index], culture, StringComparison.OrdinalIgnoreCase)
                && row[index + 1] is { } translated)
                return translated;
        }
        return row[1] ?? throw new InvalidOperationException("ChurchSettings resource has no zh-TW value.");
    }
}

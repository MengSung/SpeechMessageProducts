using System;
using System.Collections.Generic;
using System.IO;
using SpeechMessage.Localization.Resources;
#if NET10_0_OR_GREATER
using System.Collections.Frozen;
#else
using System.Collections.ObjectModel;
#endif

namespace SpeechMessage.Localization;

/// <summary>
/// 持有每個語系的完整唯讀系統文字快照；建立時補齊 zh-TW 後備，再一次發布。
/// 沒有 I/O、教會覆寫或可變 request 狀態；每個實例只持有固定資源，生命週期由消費者擁有。
/// </summary>
public sealed class SystemTextTable : ISystemTextCatalog
{
#if NET10_0_OR_GREATER
    /// <summary>建立完成後才賦值的凍結語系表；容量只取決於固定資源，不保留查詢參數或外部陣列。</summary>
    private readonly FrozenDictionary<string, IReadOnlyDictionary<string, string>> tables;
#else
    /// <summary>包住本實例獨佔且不再修改的候選字典；不公開或保留其他可變擁有者。</summary>
    private readonly ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> tables;
#endif

    /// <summary>從生成的固定資料建立全部語系，建立失敗不發布半成品。</summary>
    public SystemTextTable() : this(SharedTextData.Cultures, SharedTextData.Rows) { }

    /// <summary>驗證及複製生成資料；不保留外部陣列，重複鍵／語系或不完整列直接拒絕。</summary>
    /// <param name="cultures">每欄的固定語系識別，必須包含 zh-TW。</param>
    /// <param name="rows">每列依序為鍵及各語系文字；缺翻譯為 null。</param>
    internal SystemTextTable(IReadOnlyList<string> cultures, IReadOnlyList<string?[]> rows)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(cultures);
        ArgumentNullException.ThrowIfNull(rows);
#else
        if (cultures is null) throw new ArgumentNullException(nameof(cultures));
        if (rows is null) throw new ArgumentNullException(nameof(rows));
#endif
        var names = new string[cultures.Count];
        var cultureIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, string>[cultures.Count];
        var neutralIndex = -1;
        for (var index = 0; index < cultures.Count; index++)
        {
            var culture = cultures[index];
            if (string.IsNullOrWhiteSpace(culture) || !cultureIdentities.Add(culture)) throw new InvalidDataException("Duplicate or empty system text culture.");
            names[index] = culture;
            values[index] = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.Equals(culture, "zh-TW", StringComparison.OrdinalIgnoreCase)) neutralIndex = index;
        }
        if (neutralIndex < 0) throw new InvalidDataException("System text requires a neutral zh-TW culture.");
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row is null || row.Length != names.Length + 1) throw new InvalidDataException("System text row width is invalid.");
            var keyValue = row[0];
            var neutral = row[neutralIndex + 1];
            if (keyValue is null || neutral is null) throw new InvalidDataException("System text row has no key or neutral value.");
            var key = new TextKey(keyValue);
            if (!identities.Add(key.Value)) throw new InvalidDataException("Duplicate system text key.");
            for (var index = 0; index < names.Length; index++) values[index].Add(key.Value, row[index + 1] ?? neutral);
        }
        // 先完成全部列與身分驗證，再封裝內層及外層；失敗時不會有任何可觀察的半成品。
        var candidate = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < names.Length; index++) candidate.Add(names[index], Seal(values[index]));
        tables = Seal(candidate);
    }

    /// <summary>取得已展開後備的完整文字；命中路徑不配置物件或修改字典。</summary>
    /// <param name="culture">文字語系；未知／空值回 zh-TW。</param>
    /// <param name="key">固定資源鍵。</param>
    /// <returns>唯讀資源中的原始字串。</returns>
    /// <exception cref="KeyNotFoundException">鍵不存在。</exception>
    public string GetText(string? culture, TextKey key)
    {
        if (GetTable(culture).TryGetValue(key.Value, out var text)) return text;
        throw new KeyNotFoundException("Unknown system text key: " + key.Value);
    }

    /// <summary>回傳無法由呼叫者修改的語系表，所有值都是不可變字串，不暴露生成陣列。</summary>
    /// <param name="culture">已知語系或任意未知值；未知時共用既有 zh-TW 表，不新增快取。</param>
    /// <returns>本實例生命週期內不變的完整字典。</returns>
    public IReadOnlyDictionary<string, string> GetTable(string? culture) => culture is not null && tables.TryGetValue(culture, out var selected) ? selected : tables["zh-TW"];

#if NET10_0_OR_GREATER
    /// <summary>將本次建構獨佔的候選轉為 FrozenDictionary，讀取路徑不再配置或鎖定。</summary>
    /// <typeparam name="TKey">不可為 null 的穩定鍵型別。</typeparam>
    /// <typeparam name="TValue">已建立完成且不再修改的值。</typeparam>
    /// <param name="candidate">單一建構式擁有的字典，呼叫後不再寫入。</param>
    /// <returns>沿用相同比較器的不可變字典。</returns>
    private static FrozenDictionary<TKey, TValue> Seal<TKey, TValue>(Dictionary<TKey, TValue> candidate) where TKey : notnull => candidate.ToFrozenDictionary(candidate.Comparer);
#else
    /// <summary>net48 以 ReadOnlyDictionary 封裝單一擁有者的候選，底層字典不再暴露或修改。</summary>
    /// <typeparam name="TKey">不可為 null 的穩定鍵型別。</typeparam>
    /// <typeparam name="TValue">不可變文字或已封裝的內層快照。</typeparam>
    /// <param name="candidate">本建構式獨佔且已完成的候選。</param>
    /// <returns>與 net10 共用公開唯讀介面的字典。</returns>
    private static ReadOnlyDictionary<TKey, TValue> Seal<TKey, TValue>(Dictionary<TKey, TValue> candidate) where TKey : notnull => new(candidate);
#endif
}

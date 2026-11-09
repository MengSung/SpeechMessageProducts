/*
 * 檔案整體說明
 *
 * 目的與定位：從系統資源建立不可變路由表；只保存固定關鍵字，不保存訊息、成員或教會資料。
 *
 * 主要功能與流程：
 * 本檔宣告 KeywordMatchMode、IKeywordCatalog、KeywordCatalog、KeywordRule、CultureKeywords。
 * - 依會友、教會其他語系及繁中順序比對；所有資料在建構時完成衝突驗證。
 * - 固定的兩個預設語系，僅作複製來源，不對外暴露或加入輸入文化。
 * - Framework 由本建構式獨佔的字典完成後封裝，後續不再修改。
 * - 一次建立全部可共同使用的語系，異意圖正規化衝突直接拒絕，不發布部分路由。
 * 主要操作入口包含 TryMatch、TryCulture、Normalize、KeywordRule。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Localization.Tests/KeywordCatalogTests.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
#if NET10_0_OR_GREATER
using System.Collections.Frozen;
#else
using System.Collections.ObjectModel;
#endif

namespace SpeechMessage.Localization;

/// <summary>由產品在啟動時明確指定的既有路由語意，不以顯示文字猜測比對模式。</summary>
public enum KeywordMatchMode
{
    /// <summary>整段正規化後完全相等。</summary>
    Exact,
    /// <summary>沿用產品已核定的前綴命令。</summary>
    Prefix,
    /// <summary>沿用產品已核定的包含式命令。</summary>
    Contains
}

/// <summary>
/// 關鍵字表在單一語系的輸入正規化策略；預設策略遵守通用 Localization 契約，
/// 舊產品相容策略則只移除首尾空白並保留原始大小寫與 Unicode 字面值。
/// </summary>
public enum KeywordNormalizationMode
{
    /// <summary>使用 NFKC、Unicode 空白合併及 Invariant 小寫。</summary>
    InvariantNfkc,
    /// <summary>只沿用舊路由的首尾空白移除，不執行 Unicode 轉換或大小寫折疊。</summary>
    LegacyTrimOrdinal
}

/// <summary>關鍵字只回傳穩定意圖代碼，不執行授權、產品業務或資料寫入。</summary>
public interface IKeywordCatalog
{
    /// <summary>依會友、教會其他語系及繁中順序比對；所有資料在建構時完成衝突驗證。</summary>
    /// <param name="memberCulture">已決定的會友語系。</param>
    /// <param name="churchCultures">已啟用語系。</param>
    /// <param name="input">單次文字輸入，不保留。</param>
    /// <param name="intentCode">穩定意圖或空字串。</param>
    /// <returns>成功分派時為真。</returns>
    bool TryMatch(string memberCulture, IReadOnlyList<string> churchCultures, string input, out string intentCode);
}

/// <summary>從系統資源建立不可變路由表；只保存固定關鍵字，不保存訊息、成員或教會資料。</summary>
public sealed class KeywordCatalog : IKeywordCatalog
{
    /// <summary>固定的兩個預設語系，僅作複製來源，不對外暴露或加入輸入文化。</summary>
    private static readonly string[] DefaultCultures = { "zh-TW", "en" };
#if NET10_0_OR_GREATER
    /// <summary>全部完成後一次發布的凍結語系路由，僅保留固定資源。</summary>
    private readonly FrozenDictionary<string, CultureKeywords> tables;
#else
    /// <summary>Framework 由本建構式獨佔的字典完成後封裝，後續不再修改。</summary>
    private readonly ReadOnlyDictionary<string, CultureKeywords> tables;
#endif
    /// <summary>一次建立全部可共同使用的語系，異意圖正規化衝突直接拒絕，不發布部分路由。</summary>
    /// <param name="system">resx 產生的系統表。</param>
    /// <param name="cultures">產品可能同時啟用的語系；缺省繁中與英文。</param>
    /// <param name="matchModes">穩定資源鍵對應的伺服器模式，缺省完全比對。</param>
    /// <param name="priorityOrder">產品明確核定的部分比對優先順序；由穩定 key 指定，不以翻譯文字排序取代業務優先序。</param>
    /// <param name="normalizationModes">依語系指定輸入正規化；未指定的語系使用 NFKC／Invariant 小寫。</param>
    public KeywordCatalog(SystemTextTable system, IReadOnlyList<string>? cultures = null,
        IReadOnlyDictionary<string, KeywordMatchMode>? matchModes = null, IReadOnlyList<TextKey>? priorityOrder = null,
        IReadOnlyDictionary<string, KeywordNormalizationMode>? normalizationModes = null)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(system);
#else
        if (system is null) throw new ArgumentNullException(nameof(system));
#endif
        var selectedCultures = CultureNames.Copy(cultures ?? DefaultCultures);
        var priorityRanks = new Dictionary<string, int>(StringComparer.Ordinal);
        if (priorityOrder is not null)
        {
            if (priorityOrder.Count > 20_000) throw new ArgumentException("Keyword priority exceeds its bounded count.", nameof(priorityOrder));
            var priorityIdentities = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < priorityOrder.Count; index++)
            {
                var key = priorityOrder[index].Value;
                if (key.Length == 0 || !priorityIdentities.Add(key))
                    throw new ArgumentException("Keyword priority contains an invalid or duplicate resource key.", nameof(priorityOrder));
                priorityRanks.Add(key, index);
            }
        }
        var candidate = new Dictionary<string, CultureKeywords>(StringComparer.OrdinalIgnoreCase);
        var allIntents = new Dictionary<string, string>(StringComparer.Ordinal);
        var usedModeKeys = new HashSet<string>(StringComparer.Ordinal);
        var availableKeys = new HashSet<string>(StringComparer.Ordinal);
        var usedNormalizationCultures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var aliasCount = 0;
        foreach (var culture in selectedCultures)
        {
            var normalizationMode = KeywordNormalizationMode.InvariantNfkc;
            if (normalizationModes is not null && normalizationModes.TryGetValue(culture, out var configuredNormalization))
            {
                if (configuredNormalization is < KeywordNormalizationMode.InvariantNfkc or > KeywordNormalizationMode.LegacyTrimOrdinal)
                    throw new ArgumentException("Unknown keyword normalization mode.", nameof(normalizationModes));
                normalizationMode = configuredNormalization;
                usedNormalizationCultures.Add(culture);
            }
            var rules = new List<KeywordRule>();
            foreach (var pair in system.GetTable(culture).OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (!pair.Key.StartsWith("Keyword.", StringComparison.Ordinal)
                    || !system.TryGetOriginalText(culture, new TextKey(pair.Key), out var original)) continue;
                availableKeys.Add(pair.Key);
                var intent = pair.Key.Substring("Keyword.".Length);
                var mode = KeywordMatchMode.Exact;
                if (matchModes is not null && matchModes.TryGetValue(pair.Key, out var configured))
                {
                    if (configured is < KeywordMatchMode.Exact or > KeywordMatchMode.Contains)
                        throw new ArgumentException("Unknown keyword match mode.", nameof(matchModes));
                    mode = configured;
                    usedModeKeys.Add(pair.Key);
                }
                foreach (var alias in original.Split('|'))
                {
                    if (++aliasCount > 20_000) throw new ArgumentException("Keyword table exceeds its bounded alias count.", nameof(system));
                    var normalized = Normalize(alias, normalizationMode);
                    if (normalized.Length == 0) throw new ArgumentException("Keyword aliases cannot be empty.", nameof(system));
                    var conflictKey = Normalize(alias, KeywordNormalizationMode.InvariantNfkc);
                    if (allIntents.TryGetValue(conflictKey, out var existing) && !string.Equals(existing, intent, StringComparison.Ordinal))
                        throw new ArgumentException("Normalized keyword maps to conflicting intents.", nameof(system));
                    allIntents[conflictKey] = intent;
                    rules.Add(new KeywordRule(normalized, intent, mode,
                        priorityRanks.TryGetValue(pair.Key, out var rank) ? rank : int.MaxValue));
                }
            }
            candidate.Add(culture, new CultureKeywords(rules, normalizationMode));
        }
        if (!candidate.ContainsKey("zh-TW")) throw new ArgumentException("Keyword cultures must include zh-TW.", nameof(cultures));
        if (matchModes is not null && matchModes.Keys.Any(key => !usedModeKeys.Contains(key)))
            throw new ArgumentException("Keyword mode references an unknown resource key.", nameof(matchModes));
        if (priorityRanks.Keys.Any(key => !availableKeys.Contains(key)))
            throw new ArgumentException("Keyword priority references an unknown resource key.", nameof(priorityOrder));
        if (normalizationModes is not null && normalizationModes.Keys.Any(key => !usedNormalizationCultures.Contains(key)))
            throw new ArgumentException("Keyword normalization references an unknown culture.", nameof(normalizationModes));
#if NET10_0_OR_GREATER
        tables = candidate.ToFrozenDictionary(candidate.Comparer);
#else
        tables = new ReadOnlyDictionary<string, CultureKeywords>(candidate);
#endif
    }
    /// <summary>只在單次操作正規化輸入並查固定表，未命中不洩露前次結果。</summary>
    /// <param name="memberCulture">會友語系。</param>
    /// <param name="churchCultures">教會啟用語系。</param>
    /// <param name="input">本次輸入。</param>
    /// <param name="intentCode">本次意圖或空字串。</param>
    /// <returns>明確命中時為真。</returns>
    public bool TryMatch(string memberCulture, IReadOnlyList<string> churchCultures, string input, out string intentCode)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(churchCultures);
#else
        if (churchCultures is null) throw new ArgumentNullException(nameof(churchCultures));
#endif
        intentCode = string.Empty;
        if (string.IsNullOrWhiteSpace(input) || input.Length > 16_384) return false;
        if (churchCultures.Count > 64) throw new ArgumentException("Church cultures exceed their bounded count.", nameof(churchCultures));
        if (TryCulture(memberCulture, input, out intentCode)) return true;
        foreach (var culture in churchCultures)
            if (!string.Equals(culture, memberCulture, StringComparison.OrdinalIgnoreCase)
                && TryCulture(culture, input, out intentCode)) return true;
        return TryCulture("zh-TW", input, out intentCode);
    }

    /// <summary>查固定語系表；未知語系不建立新表或永久保存字串。</summary>
    /// <param name="culture">本次候選語系。</param>
    /// <param name="input">本次未正規化輸入。</param>
    /// <param name="intent">本次意圖或空字串。</param>
    /// <returns>指定語系明確命中時為真。</returns>
    private bool TryCulture(string culture, string input, out string intent)
    {
        if (culture is not null && tables.TryGetValue(culture, out var selected)) return selected.TryMatch(input, out intent);
        intent = string.Empty;
        return false;
    }

    /// <summary>依語系策略建立本次查詢文字；配置只屬本次操作，不保留呼叫輸入。</summary>
    /// <param name="input">固定別名或已限長的輸入。</param>
    /// <param name="mode">本次語系使用的正規化策略。</param>
    /// <returns>供 Ordinal 比對的字串。</returns>
    private static string Normalize(string input, KeywordNormalizationMode mode)
    {
        if (input.Length > 16_384) throw new ArgumentException("Keyword input exceeds its bounded length.", nameof(input));
        if (mode == KeywordNormalizationMode.LegacyTrimOrdinal) return input.Trim();
        var normalized = input.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);
        var pendingSpace = false;
        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character)) { pendingSpace = builder.Length != 0; continue; }
            if (pendingSpace) builder.Append(' ');
            pendingSpace = false;
            builder.Append(character);
        }
        return builder.ToString().ToLowerInvariant();
    }

    /// <summary>單一固定規則只保存正規化資源文字、穩定意圖及明確模式，不保存呼叫輸入。</summary>
    /// <param name="text">正規化別名。</param>
    /// <param name="intent">資源後綴意圖。</param>
    /// <param name="mode">產品啟動時提供的比對模式。</param>
    /// <param name="priority">產品明確指定的同模式優先序；未指定時為最大值。</param>
    private sealed class KeywordRule(string text, string intent, KeywordMatchMode mode, int priority)
    {
        /// <summary>建構後不變的別名。</summary>
        internal string Text { get; } = text;
        /// <summary>不變的產品意圖識別。</summary>
        internal string Intent { get; } = intent;
        /// <summary>明確且驗證過的模式。</summary>
        internal KeywordMatchMode Mode { get; } = mode;
        /// <summary>建構時複製的固定優先序，之後不再修改或借用來源設定。</summary>
        internal int Priority { get; } = priority;
    }

    /// <summary>單一語系固定表，完全比對優先；部分比對依模式、明確業務優先序、長度及 Ordinal 排序，不在查詢時修改。</summary>
    private sealed class CultureKeywords
    {
        /// <summary>本語系建立與查詢共同使用的正規化策略，不保存呼叫輸入。</summary>
        private readonly KeywordNormalizationMode normalizationMode;
#if NET10_0_OR_GREATER
        /// <summary>唯讀完全比對熱路徑，不配置或鎖定。</summary>
        private readonly FrozenDictionary<string, string> exact;
#else
        /// <summary>Framework 完全比對表只封裝自有字典，完成後不再修改。</summary>
        private readonly ReadOnlyDictionary<string, string> exact;
#endif
        /// <summary>不對外暴露的固定前綴／包含規則陣列。</summary>
        private readonly KeywordRule[] partial;

        /// <summary>候選已完成跨語系衝突檢查；同一意圖同字的等價別名可共享路由項，但不處理資料庫列去重。</summary>
        /// <param name="rules">本次建構獨佔的規則。</param>
        /// <param name="normalizationMode">本語系固定的輸入正規化策略。</param>
        internal CultureKeywords(IReadOnlyList<KeywordRule> rules, KeywordNormalizationMode normalizationMode)
        {
            this.normalizationMode = normalizationMode;
            var candidate = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var rule in rules)
                if (rule.Mode == KeywordMatchMode.Exact) candidate[rule.Text] = rule.Intent;
#if NET10_0_OR_GREATER
            exact = candidate.ToFrozenDictionary(candidate.Comparer);
#else
            exact = new ReadOnlyDictionary<string, string>(candidate);
#endif
            partial = rules.Where(rule => rule.Mode != KeywordMatchMode.Exact).OrderBy(rule => rule.Mode)
                .ThenBy(rule => rule.Priority).ThenByDescending(rule => rule.Text.Length)
                .ThenBy(rule => rule.Text, StringComparer.Ordinal).ToArray();
        }

        /// <summary>依已建立的模式查詢；完全未命中確定清空輸出，永不記錄原始輸入。</summary>
        /// <param name="input">單次未正規化文字。</param>
        /// <param name="intent">本次意圖或空字串。</param>
        /// <returns>明確匹配時為真。</returns>
        internal bool TryMatch(string input, out string intent)
        {
            string normalized;
            try { normalized = Normalize(input, normalizationMode); }
            catch (ArgumentException) { intent = string.Empty; return false; }
            if (exact.TryGetValue(normalized, out var matched)) { intent = matched; return true; }
            foreach (var rule in partial)
            {
                if (rule.Mode == KeywordMatchMode.Prefix ? normalized.StartsWith(rule.Text, StringComparison.Ordinal)
#if NET10_0_OR_GREATER
                    : normalized.Contains(rule.Text, StringComparison.Ordinal))
#else
                    : normalized.IndexOf(rule.Text, StringComparison.Ordinal) >= 0)
#endif
                { intent = rule.Intent; return true; }
            }
            intent = string.Empty;
            return false;
        }
    }
}

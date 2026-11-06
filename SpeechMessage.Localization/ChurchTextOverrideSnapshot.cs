/*
 * 檔案整體說明
 *
 * 目的與定位：候選全驗證後發布的唯讀文字表，無 I/O 或資源擁有權，不保留來源集合。
 *
 * 主要功能與流程：
 * 本檔宣告 ChurchTextOverride、IChurchTextOverrideSnapshot、IChurchTextOverrideSnapshotProvider、
 * ChurchTextOverrideSnapshot。
 * - 完整伺服器範圍；消費端每次確認一致，不以快取命中取代授權。
 * - 沒有資料仍回傳帶原範圍的空快照；來源資源生命週期由實作 owner 負責。
 * - 兩層字典皆由建構式獨佔並在發布前封裝；最多五千列，不保留輸入集合或資料列物件。
 * - 檢查穩定識別及 scope，再複製全部純文字，失敗時不發布部分資料。
 * 主要操作入口包含 TryGetText。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/LineSharedWorkflow/OverrideCacheExceptionTests.cs、
 * SpeechMessage.Localization.Tests/ChurchTextOverrideValidatorTests.cs、
 * SpeechMessage.Localization.Tests/TextCatalogTests.cs、
 * SpeechMessage.Tenancy.Tests/ChurchTextOverrideCacheTests.cs、
 * SpeechMessage.Tenancy.Tests/OverrideCompositionTests.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SpeechMessage.Localization;

/// <summary>一列已持久化覆寫；正式資料庫 ID 與教會 ID 不可由顯示文字或暫存鍵取代。</summary>
public sealed class ChurchTextOverride
{
    /// <summary>資料庫穩定識別，不得為空。</summary>
    public Guid Id { get; }
    /// <summary>所屬教會識別。</summary>
    public Guid ChurchId { get; }
    /// <summary>系統資源的穩定鍵。</summary>
    public TextKey Key { get; }
    /// <summary>指定語系或所有語系星號。</summary>
    public string Culture { get; } = string.Empty;
    /// <summary>不含連線／request 引用的不可變文字。</summary>
    public string Text { get; } = string.Empty;
    /// <summary>只複製純值，不持有 SDK Entity、連線或身分。</summary>
    /// <param name="id">非空資料庫識別。</param>
    /// <param name="churchId">非空教會識別。</param>
    /// <param name="key">穩定資源鍵。</param>
    /// <param name="culture">文字語系或星號。</param>
    /// <param name="text">覆寫原文。</param>
    public ChurchTextOverride(Guid id, Guid churchId, TextKey key, string culture, string text)
    {
        if (id == Guid.Empty || churchId == Guid.Empty) throw new ArgumentException("Persisted override and church identities are required.");
        if (key.Value.Length == 0) throw new ArgumentException("A valid text key is required.", nameof(key));
        if (string.IsNullOrWhiteSpace(culture) || culture.Length > 128) throw new ArgumentException("An override culture is required.", nameof(culture));
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(text);
#else
        if (text is null) throw new ArgumentNullException(nameof(text));
#endif
        if (text.Length > 32_768) throw new ArgumentException("Override text exceeds the bounded template size.", nameof(text));
        Id = id;
        ChurchId = churchId;
        Key = key;
        Culture = culture;
        Text = text;
    }
}

/// <summary>已完整發布的純文字快照，不暴露可變集合或任何底層資源。</summary>
public interface IChurchTextOverrideSnapshot
{
    /// <summary>完整伺服器範圍；消費端每次確認一致，不以快取命中取代授權。</summary>
    TextContext Context { get; }
    /// <summary>只查原始覆寫，不自行進行語系後備。</summary>
    /// <param name="culture">語系或星號。</param>
    /// <param name="key">固定資源鍵。</param>
    /// <param name="text">命中原文，未命中為空。</param>
    /// <returns>只有明確存在該語系覆寫時為真。</returns>
    bool TryGetText(string culture, TextKey key, out string text);
}

/// <summary>提供已授權完整邊界的快照，不提供任意 Guid 跨 scope 取資料的捷徑。</summary>
public interface IChurchTextOverrideSnapshotProvider
{
    /// <summary>沒有資料仍回傳帶原範圍的空快照；來源資源生命週期由實作 owner 負責。</summary>
    /// <param name="context">伺服器授權後的範圍。</param>
    /// <returns>非 null、不可變的完整快照。</returns>
    IChurchTextOverrideSnapshot GetSnapshot(TextContext context);
}

/// <summary>候選全驗證後發布的唯讀文字表，無 I/O 或資源擁有權，不保留來源集合。</summary>
public sealed class ChurchTextOverrideSnapshot : IChurchTextOverrideSnapshot
{
    /// <summary>兩層字典皆由建構式獨佔並在發布前封裝；最多五千列，不保留輸入集合或資料列物件。</summary>
    private readonly ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> values;
    /// <summary>本快照的完整不可變範圍。</summary>
    public TextContext Context { get; } = null!;
    /// <summary>檢查穩定識別及 scope，再複製全部純文字，失敗時不發布部分資料。</summary>
    /// <param name="context">已授權範圍。</param>
    /// <param name="rows">本次完整候選。</param>
    public ChurchTextOverrideSnapshot(TextContext context, IReadOnlyList<ChurchTextOverride> rows)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(rows);
#else
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (rows is null) throw new ArgumentNullException(nameof(rows));
#endif
        if (rows.Count > 5000) throw new ArgumentException("Override snapshot exceeds its bounded row budget.", nameof(rows));
        var candidate = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var identities = new HashSet<Guid>();
        foreach (var row in rows)
        {
            if (row is null || row.ChurchId != context.ChurchId || !identities.Add(row.Id))
                throw new ArgumentException("Override identity is duplicate or belongs to another church.", nameof(rows));
            if (!candidate.TryGetValue(row.Culture, out var culture))
            {
                culture = new Dictionary<string, string>(StringComparer.Ordinal);
                candidate.Add(row.Culture, culture);
            }
            if (culture.ContainsKey(row.Key.Value)) throw new ArgumentException("Duplicate override key and culture.", nameof(rows));
            culture.Add(row.Key.Value, row.Text);
        }
        var sealedCandidate = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in candidate) sealedCandidate.Add(pair.Key, new ReadOnlyDictionary<string, string>(pair.Value));
        values = new ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>(sealedCandidate);
        Context = context;
    }
    /// <summary>以語系及穩定鍵查詢，不修改快照或保留呼叫參數。</summary>
    /// <param name="culture">原始語系或星號。</param>
    /// <param name="key">系統資源鍵。</param>
    /// <param name="text">命中值或空字串。</param>
    /// <returns>明確命中時為真。</returns>
    public bool TryGetText(string culture, TextKey key, out string text)
    {
        if (culture is not null && values.TryGetValue(culture, out var table) && table.TryGetValue(key.Value, out var found))
        {
            text = found;
            return true;
        }
        text = string.Empty;
        return false;
    }
}

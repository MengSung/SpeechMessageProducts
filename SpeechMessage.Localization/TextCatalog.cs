using System;
using System.Collections.Generic;
using System.Globalization;

namespace SpeechMessage.Localization;

/// <summary>具名格式參數只屬於單次呼叫，不存入 catalog 或任何跨使用者快取。</summary>
public readonly struct TextArg
{
    /// <summary>Ordinal 區分大小寫的參數名稱。</summary>
    public string Name { get; }
    /// <summary>只在本次格式化期間借用的值，不由 catalog 保留。</summary>
    public object? Value { get; }
    /// <summary>建立具名參數，格式化時驗證完整名稱集合。</summary>
    /// <param name="name">資源內的名稱。</param>
    /// <param name="value">明確格式化的值。</param>
    public TextArg(string name, object? value) { Name = name; Value = value; }
}

/// <summary>純記憶體五層取字契約；host 必須先授權 context，例外向上傳至共用失敗邊界。</summary>
public interface ITextCatalog
{
    /// <summary>以教會、系統及後備解析原文，再按明確文化格式化單次參數。</summary>
    /// <param name="context">伺服器授權後的完整範圍。</param>
    /// <param name="key">穩定資源鍵。</param>
    /// <param name="args">恰好符合原文的具名參數。</param>
    /// <returns>本次呼叫擁有的文字，不保留參數值。</returns>
    string GetText(TextContext context, TextKey key, params TextArg[] args);
}

/// <summary>五層解析器只借用系統表與快照提供者，沒有 I/O、計時器、背景工作或每請求快取。</summary>
public sealed class TextCatalog : ITextCatalog
{
    /// <summary>固定系統資料由外部 owner 建立，本實例只借用，不留每次查詢狀態。</summary>
    private readonly SystemTextTable system;
    /// <summary>快照提供者由外部 owner 管理；不得把 request 或可變快照保存在 catalog。</summary>
    private readonly IChurchTextOverrideSnapshotProvider overrides;
    /// <summary>借用已完成的系統表與提供者，不處置外部擁有的服務。</summary>
    /// <param name="system">保留原始語系存在性的系統表。</param>
    /// <param name="overrides">完整範圍的不可變快照提供者。</param>
    public TextCatalog(SystemTextTable system, IChurchTextOverrideSnapshotProvider overrides)
    {
        this.system = system ?? throw new ArgumentNullException(nameof(system));
        this.overrides = overrides ?? throw new ArgumentNullException(nameof(overrides));
    }
    /// <summary>取出第一個有效原文，缺鍵或參數集合錯誤向上拋出，不隱藏真正失敗。</summary>
    /// <param name="context">完整授權範圍。</param>
    /// <param name="key">穩定資源鍵。</param>
    /// <param name="args">本次參數，不保留引用。</param>
    /// <returns>完整格式化的字串。</returns>
    public string GetText(TextContext context, TextKey key, params TextArg[] args)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
#else
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (args is null) throw new ArgumentNullException(nameof(args));
#endif
        var snapshot = overrides.GetSnapshot(context);
        if (snapshot is null || !context.HasSameScope(snapshot.Context))
            throw new InvalidOperationException("Override snapshot does not match the authorized scope.");
        // 只查原文存在性；原完整表預展開的中性值不能搶在父語系之前命中。
        if (!snapshot.TryGetText(context.TextCulture, key, out var text)
            && !snapshot.TryGetText("*", key, out text)
            && !system.TryGetOriginalText(context.TextCulture, key, out text)
            && !TryGetParentText(context.TextCulture, key, out text)
            && !system.TryGetOriginalText("zh-TW", key, out text))
            throw new KeyNotFoundException("Unknown system text key: " + key.Value);
        return NamedTextFormatter.Format(text, CultureInfo.GetCultureInfo(context.FormatCulture), args);
    }

    /// <summary>先處理核定繁中別名，再逐層查父語系；使用 CultureInfo 的不可變文化定義，不快取外部語系字串。</summary>
    /// <param name="culture">本次文字語系。</param>
    /// <param name="key">系統鍵。</param>
    /// <param name="text">命中的原文。</param>
    /// <returns>父語系或別名有明確翻譯時為真。</returns>
    private bool TryGetParentText(string culture, TextKey key, out string text)
    {
        if ((string.Equals(culture, "zh-HK", StringComparison.OrdinalIgnoreCase)
                || string.Equals(culture, "zh-MO", StringComparison.OrdinalIgnoreCase)
                || string.Equals(culture, "zh-Hant", StringComparison.OrdinalIgnoreCase))
            && system.TryGetOriginalText("zh-TW", key, out text)) return true;
        // 未知文字語系依舊容許回繁中；格式文化則已由 context 在查詢前嚴格驗證。
        CultureInfo current;
        try { current = CultureInfo.GetCultureInfo(culture); }
        catch (CultureNotFoundException) { text = string.Empty; return false; }
        for (var depth = 0; depth < 8 && current.Parent.Name.Length != 0; depth++)
        {
            current = current.Parent;
            if (system.TryGetOriginalText(current.Name, key, out text)) return true;
        }
        text = string.Empty;
        return false;
    }
}

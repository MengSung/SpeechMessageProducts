/*
 * 檔案整體說明
 *
 * 目的與定位：以 Ordinal 識別固定系統文字的穩定鍵，不以翻譯後顯示文字判斷身分。 值不可變，不含教會、使用者或憑證；建立時驗證分層格式，預設值不會命中合法資源。
 *
 * 主要功能與流程：
 * 本檔宣告 TextKey。
 * - 只保留不可變的原始鍵，預設 struct 的 null 不會成為合法資源身分。
 * - 要求至少兩個以大寫開頭的英數分段，確保資源鍵格式可由工具與程式共同驗證。
 * - 只按鍵值作 Ordinal 比較，不參照顯示語系或程序文化。
 * - 只允許與另一個 TextKey 比較，避免其他型別的顯示字串冒充鍵。
 * 主要操作入口包含 Equals、GetHashCode、ToString、IsValid。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Localization.Tests/ChurchTextOverrideValidatorTests.cs、
 * SpeechMessage.Localization.Tests/KeywordCatalogTests.cs、
 * SpeechMessage.Localization.Tests/SystemTextCatalogTests.cs、SpeechMessage.Localization.Tests/TextCatalogTests.cs、
 * SpeechMessage.Tenancy.Tests/ChurchTextOverrideCacheTests.cs 追查目前工作樹中的合作／使用位置。
 */
using System;

namespace SpeechMessage.Localization;

/// <summary>
/// 以 Ordinal 識別固定系統文字的穩定鍵，不以翻譯後顯示文字判斷身分。
/// 值不可變，不含教會、使用者或憑證；建立時驗證分層格式，預設值不會命中合法資源。
/// </summary>
public readonly struct TextKey : IEquatable<TextKey>
{
    /// <summary>只保留不可變的原始鍵，預設 struct 的 null 不會成為合法資源身分。</summary>
    private readonly string? value;

    /// <summary>取得原始鍵；不建立翻譯、快取或任何呼叫者狀態。</summary>
    public string Value => value ?? string.Empty;

    /// <summary>要求至少兩個以大寫開頭的英數分段，確保資源鍵格式可由工具與程式共同驗證。</summary>
    /// <param name="value">例如 Common.Diagnostics.Ping 的穩定鍵。</param>
    /// <exception cref="ArgumentException">輸入不符合鍵格式。</exception>
    public TextKey(string value)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(value);
#else
        if (value is null) throw new ArgumentNullException(nameof(value));
#endif
        if (!IsValid(value)) throw new ArgumentException("Text key must contain uppercase-led alphanumeric segments separated by dots.", nameof(value));
        this.value = value;
    }

    /// <summary>只按鍵值作 Ordinal 比較，不參照顯示語系或程序文化。</summary>
    /// <param name="other">另一個不可變文字鍵。</param>
    /// <returns>兩個原始鍵完全相同時為真。</returns>
    public bool Equals(TextKey other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <summary>只允許與另一個 TextKey 比較，避免其他型別的顯示字串冒充鍵。</summary>
    /// <param name="obj">待比較的物件。</param>
    /// <returns>同型別且同鍵時為真。</returns>
    public override bool Equals(object? obj) => obj is TextKey other && Equals(other);

    /// <summary>使用與相等契約一致的 Ordinal 雜湊，不輸出或保存外部狀態。</summary>
    /// <returns>只供本程序字典使用的雜湊值。</returns>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    /// <summary>回傳穩定鍵本身，便於診斷，不包含翻譯或使用者資料。</summary>
    /// <returns>原始資源鍵。</returns>
    public override string ToString() => Value;

    /// <summary>以與 Equals 相同的 Ordinal 規則判定相等。</summary>
    /// <param name="left">左鍵。</param>
    /// <param name="right">右鍵。</param>
    /// <returns>兩鍵相同時為真。</returns>
    public static bool operator ==(TextKey left, TextKey right) => left.Equals(right);

    /// <summary>相等運算的反向，不使用顯示文字或文化排序。</summary>
    /// <param name="left">左鍵。</param>
    /// <param name="right">右鍵。</param>
    /// <returns>兩鍵不同時為真。</returns>
    public static bool operator !=(TextKey left, TextKey right) => !left.Equals(right);

    /// <summary>線性掃描完整鍵，不使用文化分類或會容許尾端換行的正則行尾語意，也不配置集合。</summary>
    /// <param name="candidate">已排除 null 的完整輸入。</param>
    /// <returns>至少兩段且每段首字大寫、其餘英數時為真。</returns>
    private static bool IsValid(string candidate)
    {
        var segmentStart = true;
        var hasSeparator = false;
        foreach (var character in candidate)
        {
            if (character == '.')
            {
                if (segmentStart) return false;
                hasSeparator = true;
                segmentStart = true;
            }
            else if (segmentStart)
            {
                if (character is < 'A' or > 'Z') return false;
                segmentStart = false;
            }
            else if (character is not (>= 'A' and <= 'Z') and not (>= 'a' and <= 'z') and not (>= '0' and <= '9')) return false;
        }
        return hasSeparator && !segmentStart;
    }
}

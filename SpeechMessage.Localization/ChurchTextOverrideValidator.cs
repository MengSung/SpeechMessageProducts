/*
 * 檔案整體說明
 *
 * 目的與定位：在發布前驗證完整教會覆寫候選；只借用固定系統資源，所有集合與列驗證皆屬一次操作。 不含 I/O、request、連線、計時器或跨教會可變狀態；失敗不得發布部分結果或覆蓋舊快照。
 *
 * 主要功能與流程：
 * 本檔宣告 ChurchTextOverrideValidator。
 * - 固定系統字典由 Host 擁有，可跨教會安全共用，沒有使用者覆寫。
 * - 僅保存核定合成最長名稱的 UTF-16 長度，不保存任何真實教會名稱。
 * - 固定來源與長度政策在建構驗證，不接受每次 request 改上限以繞過平台限制。
 * - 先驗 schema、系統鍵、參數集合及平台長度，再由既有快照建構式驗正式 ID／scope／重複 key+culture。 本文合計最多一百萬 UTF-16 字元且最多五千列，避免單教會用小列數保留無界記憶體。
 * 主要操作入口包含 Validate、RequireUnicode。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/LineSharedWorkflow/OverrideCacheExceptionTests.cs、
 * SpeechMessage.Localization.Tests/ChurchTextOverrideValidatorTests.cs、
 * SpeechMessage.Tenancy.Tests/ChurchTextOverrideCacheTests.cs、SpeechMessage.Tenancy/ChurchTextOverrideCache.cs、
 * SpeechMessage.Tenancy/OverrideServiceCollectionExtensions.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Globalization;

namespace SpeechMessage.Localization;

/// <summary>
/// 在發布前驗證完整教會覆寫候選；只借用固定系統資源，所有集合與列驗證皆屬一次操作。
/// 不含 I/O、request、連線、計時器或跨教會可變狀態；失敗不得發布部分結果或覆蓋舊快照。
/// </summary>
public sealed class ChurchTextOverrideValidator
{
    /// <summary>固定系統字典由 Host 擁有，可跨教會安全共用，沒有使用者覆寫。</summary>
    private readonly SystemTextTable system;
    /// <summary>僅保存核定合成最長名稱的 UTF-16 長度，不保存任何真實教會名稱。</summary>
    private readonly int churchNameLength;

    /// <summary>固定來源與長度政策在建構驗證，不接受每次 request 改上限以繞過平台限制。</summary>
    /// <param name="system">純記憶體固定系統表。</param>
    /// <param name="longestChurchName">部署選定的完整最長名稱樣本，須與 I18nCheck limits 一致；不是真實個資。</param>
    public ChurchTextOverrideValidator(SystemTextTable system, string longestChurchName)
    {
        this.system = system ?? throw new ArgumentNullException(nameof(system));
        if (string.IsNullOrWhiteSpace(longestChurchName) || longestChurchName.Length > 1024)
            throw new ArgumentException("A bounded complete church name sample is required.", nameof(longestChurchName));
        RequireUnicode(longestChurchName);
        churchNameLength = longestChurchName.Length;
    }

    /// <summary>
    /// 先驗 schema、系統鍵、參數集合及平台長度，再由既有快照建構式驗正式 ID／scope／重複 key+culture。
    /// 本文合計最多一百萬 UTF-16 字元且最多五千列，避免單教會用小列數保留無界記憶體。
    /// </summary>
    /// <param name="context">伺服器授權的完整範圍。</param>
    /// <param name="rows">來源完整純值候選；呼叫期間必須由操作獨佔，不可同時修改。</param>
    /// <returns>完全不可變且不保留來源集合／列物件的快照。</returns>
    public ChurchTextOverrideSnapshot Validate(TextContext context, IReadOnlyList<ChurchTextOverride> rows)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(rows);
#else
        if (context is null) throw new ArgumentNullException(nameof(context));
        if (rows is null) throw new ArgumentNullException(nameof(rows));
#endif
        if (rows.Count > 5000) throw new ArgumentException("Override row budget exceeded.", nameof(rows));
        var characters = 0;
        foreach (var row in rows)
        {
            if (row is null || row.Key.Value.Length > 200 || row.Culture.Length > 16 || row.Text.Length > 2000)
                throw new ArgumentException("Override schema budget exceeded.", nameof(rows));
            characters = checked(characters + row.Text.Length);
            if (characters > 1_000_000) throw new ArgumentException("Override character budget exceeded.", nameof(rows));
            RequireUnicode(row.Text);
            if (!string.Equals(row.Culture, "*", StringComparison.Ordinal))
            {
                if (CultureInfo.GetCultureInfo(row.Culture).Name.Length == 0)
                    throw new ArgumentException("Override culture must be explicit.", nameof(rows));
            }
            if (!system.TryGetOriginalText("zh-TW", row.Key, out var original))
                throw new ArgumentException("Override key does not exist in the system catalog.", nameof(rows));
            if (!NamedTextFormatter.ParameterNames(original).SetEquals(NamedTextFormatter.ParameterNames(row.Text)))
                throw new FormatException("Override parameter names do not match the system catalog.");
            var maximum = row.Key.Value == "Line.Sender.Template" ? 20
                : row.Key.Value.StartsWith("RichMenu.ChatBar.", StringComparison.Ordinal) ? 14 : 0;
            if (maximum != 0)
            {
                var length = NamedTextFormatter.MaximumLength(row.Text, churchNameLength);
                if (length == 0 || length > maximum)
                    throw new ArgumentException("Override text exceeds the platform length limit.", nameof(rows));
            }
        }
        return new ChurchTextOverrideSnapshot(context, rows);
    }

    /// <summary>拒絕不成對 UTF-16 surrogate，防止不同輸出器修補字元造成發布後才改變識別或長度。</summary>
    /// <param name="text">只在本次掃描期間借用，不記錄或保存。</param>
    private static void RequireUnicode(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (char.IsHighSurrogate(character))
            {
                if (++index >= text.Length || !char.IsLowSurrogate(text[index]))
                    throw new ArgumentException("Override contains invalid Unicode.");
            }
            else if (char.IsLowSurrogate(character)) throw new ArgumentException("Override contains invalid Unicode.");
        }
    }
}

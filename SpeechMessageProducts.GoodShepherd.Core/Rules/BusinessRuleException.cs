/*
 * 檔案整體說明
 *
 * 目的與定位：定義 GoodShepherd 核心規則用來拒絕不符合業務契約操作的例外，讓
 * Plugin 外殼與其他主機可以依固定 TextKey 取得在地化訊息，而不把使用者輸入或
 * 機密資料放入例外文字。
 *
 * 主要功能與流程：例外保存一個穩定的 TextKey 與有界參數快照；呼叫端捕捉後交由
 * 外殼以 SystemTextTable.GetFormattedText 及既有 NamedTextFormatter 格式化，或在外殼轉成標準 Plugin 例外。參數只屬於建立該例外的
 * 操作，不會寫入 static、Session、快取或背景工作。
 *
 * 使用邊界與維護：規則必須使用固定資源鍵，不得以原始輸入組成診斷訊息；參數陣列
 * 在建立時複製，避免呼叫端後續修改造成跨層資料洩漏。此檔不擁有連線、token、
 * stream 或其他需要 Dispose 的資源，失敗處理由上層共用錯誤入口負責。
 */
using System;
using System.Collections.Generic;
using SpeechMessage.Localization;

namespace SpeechMessageProducts.GoodShepherd.Core.Rules;

/// <summary>表示可預期且應由產品外殼轉為使用者可理解錯誤的業務規則拒絕。</summary>
public sealed class BusinessRuleException : Exception
{
    /// <summary>建立帶固定文字鍵及參數快照的規則例外，不格式化或保存外部可變集合。</summary>
    /// <param name="textKey">供主機字典查詢的穩定文字鍵。</param>
    /// <param name="arguments">字典格式化參數；建立後不再引用呼叫端陣列。</param>
    public BusinessRuleException(TextKey textKey, params TextArg[] arguments)
        : base(textKey.Value)
    {
        if (textKey.Value.Length == 0) throw new ArgumentException("A text key is required.", nameof(textKey));
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(arguments);
#else
        if (arguments is null) throw new ArgumentNullException(nameof(arguments));
#endif
        if (arguments.Length > 256) throw new ArgumentException("Text argument count exceeds its budget.", nameof(arguments));
        var snapshot = (TextArg[])arguments.Clone();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var arg in snapshot)
        {
            if (string.IsNullOrEmpty(arg.Name) || arg.Name.Length > 256 || !names.Add(arg.Name))
                throw new ArgumentException("Text arguments require unique bounded names.", nameof(arguments));
            if (!IsPureScalar(arg.Value) || arg.Value is string text && text.Length > 32768)
                throw new ArgumentException("Text argument values must be bounded immutable scalars.", nameof(arguments));
        }
        TextKey = textKey;
        this.arguments = snapshot;
    }

    /// <summary>只保存驗證後的具名純值快照，存活至呼叫端放棄例外參照。</summary>
    private readonly TextArg[] arguments;

    /// <summary>取得固定資源鍵；不包含翻譯結果、教會、會友或憑證。</summary>
    public TextKey TextKey { get; }

    /// <summary>每次讀取都複製 TextArg 陣列；純量值不可變，可直接交給既有具名 formatter。</summary>
    public TextArg[] Arguments => (TextArg[])arguments.Clone();

    /// <summary>只允許 null、字串及明列不可變純量，拒絕集合、自訂物件與延後執行的格式化行為。</summary>
    private static bool IsPureScalar(object? value) => value is null or string or char or bool or
        sbyte or byte or short or ushort or int or uint or long or ulong or decimal or float or double or
        Guid or DateTime or DateTimeOffset or TimeSpan;
}

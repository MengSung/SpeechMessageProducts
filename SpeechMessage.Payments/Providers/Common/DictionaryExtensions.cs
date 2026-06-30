namespace SpeechMessage.Payments.Providers.Common;

/// <summary>
/// provider parser 共用的小型 dictionary helper。
/// 缺少欄位時回傳空字串，讓 callback parser 可以集中做欄位驗證與錯誤正規化。
/// </summary>
internal static class DictionaryExtensions
{
    public static string GetValueOrEmpty(this IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) ? value : string.Empty;
    }
}

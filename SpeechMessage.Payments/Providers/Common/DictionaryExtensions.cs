namespace SpeechMessage.Payments.Providers.Common;

internal static class DictionaryExtensions
{
    public static string GetValueOrEmpty(this IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) ? value : string.Empty;
    }
}

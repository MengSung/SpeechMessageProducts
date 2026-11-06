/*
 * 檔案整體說明
 *
 * 目的與定位：provider parser 共用的小型 dictionary helper。 缺少欄位時回傳空字串，讓 callback parser 可以集中做欄位驗證與錯誤正規化。
 *
 * 主要功能與流程：
 * 本檔宣告 DictionaryExtensions。
 * 主要操作入口包含 GetValueOrEmpty。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
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

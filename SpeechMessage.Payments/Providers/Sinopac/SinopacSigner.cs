// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments/Providers/Sinopac/SinopacSigner.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class SinopacSigner
// 主要成員：GenerateSign、GetSigningString、IsScalarValue
// 引用命名空間：System.Globalization、System.Reflection、System.Security.Cryptography、System.Text
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace SpeechMessage.Payments.Providers.Sinopac;

/// <summary>
/// 永豐 QPay 簽章工具。
/// 只取公開純量屬性、排除 Sign 欄位，再依欄位名稱排序組字串，
/// 最後串接 nonce 與 AES key 做 SHA256。
/// </summary>
internal static class SinopacSigner
{
    public static string GenerateSign(object value, string aesKey, string nonce)
    {
        var source = GetSigningString(value) + nonce + aesKey;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToUpperInvariant();
    }

    public static string GetSigningString(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var fields = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var type = value.GetType();

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            // Sign 本身不能參與簽章，否則 request/response 都無法通過驗證。
            if (property.GetCustomAttribute<SinopacSignExcludeAttribute>() is not null)
            {
                continue;
            }

            var propertyValue = property.GetValue(value);
            if (propertyValue is null || !IsScalarValue(propertyValue))
            {
                continue;
            }

            fields[property.Name] = Convert.ToString(propertyValue, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return string.Join(
            "&",
            fields
                .Where(field => !string.IsNullOrWhiteSpace(field.Value))
                .Select(field => $"{field.Key}={field.Value}"));
    }

    private static bool IsScalarValue(object value)
    {
        var type = value.GetType();
        return type.IsPrimitive ||
            type.IsEnum ||
            type == typeof(string) ||
            type == typeof(decimal) ||
            type == typeof(DateTime) ||
            type == typeof(DateTimeOffset) ||
            type == typeof(Guid);
    }
}

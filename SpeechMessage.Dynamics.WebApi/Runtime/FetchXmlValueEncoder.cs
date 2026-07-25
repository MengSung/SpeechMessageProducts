// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Runtime/FetchXmlValueEncoder.cs
// 目的：把 typed 參數安全編碼進 server-owned FetchXML。
//
// 保母教學：
// - 絕對不要字串直接拼接未編碼參數。
// - GUID / 日期 / 文字都要走這裡，避免 XML 注入。
// - 呼叫端只能傳參數值，不能傳整段 FetchXML。
// ============================================================================

using System.Globalization;
using System.Security;
using System.Text;

namespace SpeechMessage.Dynamics.WebApi.Runtime;

/// <summary>
/// FetchXML 參數編碼工具。
/// </summary>
public static class FetchXmlValueEncoder
{
    /// <summary>
    /// 編碼成 XML attribute 安全字串（不含外層引號）。
    /// </summary>
    public static string EncodeAttributeValue(string? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        // SecurityElement.Escape 會處理 < > " ' &
        return SecurityElement.Escape(value) ?? string.Empty;
    }

    /// <summary>
    /// 將 GUID 編碼成 FetchXML condition 常見的 {guid} 形式。
    /// </summary>
    public static bool TryEncodeGuid(object? raw, out string encoded, out string error)
    {
        encoded = string.Empty;
        error = string.Empty;

        if (raw is null)
        {
            error = "GUID value is required.";
            return false;
        }

        if (raw is Guid guid)
        {
            encoded = EncodeAttributeValue("{" + guid.ToString("D") + "}");
            return true;
        }

        var text = Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "GUID value is empty.";
            return false;
        }

        text = text.Trim('{', '}');
        if (!Guid.TryParse(text, out var parsed))
        {
            error = "GUID value is invalid.";
            return false;
        }

        encoded = EncodeAttributeValue("{" + parsed.ToString("D") + "}");
        return true;
    }

    /// <summary>
    /// 將日期編碼成 yyyy-M-d（對齊既有 legacy fee 查詢格式）。
    /// </summary>
    public static bool TryEncodeDate(object? raw, out string encoded, out string error)
    {
        encoded = string.Empty;
        error = string.Empty;

        if (raw is null)
        {
            error = "Date value is required.";
            return false;
        }

        if (raw is DateTime dt)
        {
            encoded = EncodeAttributeValue(dt.ToString("yyyy-M-d", CultureInfo.InvariantCulture));
            return true;
        }

        if (raw is DateTimeOffset dto)
        {
            encoded = EncodeAttributeValue(dto.UtcDateTime.ToString("yyyy-M-d", CultureInfo.InvariantCulture));
            return true;
        }

        var text = Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Date value is empty.";
            return false;
        }

        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) &&
            !DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out parsed))
        {
            error = "Date value is invalid.";
            return false;
        }

        encoded = EncodeAttributeValue(parsed.ToString("yyyy-M-d", CultureInfo.InvariantCulture));
        return true;
    }

    /// <summary>
    /// 一般字串 attribute 編碼。
    /// </summary>
    public static bool TryEncodeString(object? raw, bool required, out string encoded, out string error)
    {
        encoded = string.Empty;
        error = string.Empty;

        if (raw is null || (raw is string s && string.IsNullOrWhiteSpace(s)))
        {
            if (required)
            {
                error = "String value is required.";
                return false;
            }

            encoded = string.Empty;
            return true;
        }

        encoded = EncodeAttributeValue(Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty);
        return true;
    }

    /// <summary>
    /// 建立 URL 查詢用的 fetchXml 參數（會做完整 URL 編碼）。
    /// </summary>
    public static string ToFetchXmlQueryParameter(string fetchXml)
    {
        // Web API 需要把整個 FetchXML 放進 query string。
        return Uri.EscapeDataString(fetchXml);
    }

    /// <summary>
    /// 診斷用：回傳參數名稱清單字串。
    /// </summary>
    public static string JoinNames(IEnumerable<string> names)
    {
        var sb = new StringBuilder();
        foreach (var name in names)
        {
            if (sb.Length > 0)
            {
                sb.Append(", ");
            }

            sb.Append(name);
        }

        return sb.ToString();
    }
}

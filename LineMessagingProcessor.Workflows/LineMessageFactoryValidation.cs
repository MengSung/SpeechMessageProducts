// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.Workflows/LineMessageFactoryValidation.cs
// 所屬區塊：LINE 共用 workflow 模組與測試，放置可跨產品重用的訊息處理流程。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class LineMessageFactoryValidation
// 主要成員：Required、HttpsUrl、ActionUri、Positive、Latitude、Longitude、Area、IsAllowedActionScheme
// 引用命名空間：Line.Messaging
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Line.Messaging;

namespace LineMessagingProcessor.Workflows;

/// <summary>
/// 共用 LINE 訊息工廠的同步輸入驗證。
/// 這裡只驗證本機可以確定的規則，例如必填、數量上限、URL scheme 與座標範圍；
/// 媒體尺寸、檔案格式是否真的可下載，仍交給 LINE API 做最終驗證。
/// </summary>
internal static class LineMessageFactoryValidation
{
    public static string Required(string value, string parameterName, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(message, parameterName);
        }

        return value;
    }

    public static string HttpsUrl(string value, string parameterName, string message)
    {
        Required(value, parameterName, message);

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("LINE URL must be an absolute HTTPS URL.", parameterName);
        }

        return value;
    }

    public static string ActionUri(string value, string parameterName, string message)
    {
        Required(value, parameterName, message);

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !IsAllowedActionScheme(uri.Scheme))
        {
            throw new ArgumentException("LINE action URI must use http, https, line, or tel scheme.", parameterName);
        }

        return value;
    }

    public static IReadOnlyList<T> RequiredRange<T>(
        IEnumerable<T> values,
        string parameterName,
        int min,
        int max,
        string noun)
    {
        if (values == null)
        {
            throw new ArgumentNullException(parameterName);
        }

        var list = values.ToList();
        if (list.Count < min || list.Count > max)
        {
            throw new ArgumentException($"{noun} count must be between {min} and {max}.", parameterName);
        }

        return list;
    }

    public static long Positive(long value, string parameterName, string noun)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, $"{noun} must be greater than zero.");
        }

        return value;
    }

    public static decimal Latitude(decimal value, string parameterName)
    {
        if (value < -90m || value > 90m)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Latitude must be between -90 and 90.");
        }

        return value;
    }

    public static decimal Longitude(decimal value, string parameterName)
    {
        if (value < -180m || value > 180m)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Longitude must be between -180 and 180.");
        }

        return value;
    }

    public static ImagemapArea Area(int x, int y, int width, int height)
    {
        if (x < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(x), x, "Imagemap area x must be zero or greater.");
        }

        if (y < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(y), y, "Imagemap area y must be zero or greater.");
        }

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Imagemap area width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Imagemap area height must be greater than zero.");
        }

        return new ImagemapArea(x, y, width, height);
    }

    private static bool IsAllowedActionScheme(string scheme)
        => string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
           || string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
           || string.Equals(scheme, "line", StringComparison.OrdinalIgnoreCase)
           || string.Equals(scheme, "tel", StringComparison.OrdinalIgnoreCase);
}


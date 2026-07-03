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


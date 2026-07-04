namespace LineMessagingProcessor.Workflows;

/// <summary>
/// SendOrThrowAsync 使用的例外。例外內保留標準化結果，產品端可以記錄同一份錯誤資訊。
/// </summary>
public sealed class LineNotificationException : Exception
{
    public LineNotificationException(LineNotificationResult result)
        : base(result.ErrorMessage)
    {
        Result = result;
    }

    public LineNotificationResult Result { get; }
}

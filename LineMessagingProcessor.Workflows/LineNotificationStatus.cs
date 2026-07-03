namespace LineMessagingProcessor.Workflows;

/// <summary>
/// 共用通知流程的標準化結果，讓產品端不用直接解析 LINE SDK 的例外型別。
/// </summary>
public enum LineNotificationStatus
{
    Succeeded,
    ValidationFailed,
    ProviderRejected,
    ProviderUnavailable,
    UnexpectedError
}

// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.Workflows/LineNotificationResult.cs
// 所屬區塊：LINE 共用 workflow 模組與測試，放置可跨產品重用的訊息處理流程。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：class LineNotificationResult
// 主要成員：Success、Failure、Succeeded、Status、Recipient、RetryKey、ErrorCode、ErrorMessage、Exception
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
namespace LineMessagingProcessor.Workflows;

/// <summary>
/// 共用 LINE 通知結果。SendAsync 永遠回傳這個模型，讓呼叫端可以自行決定失敗是否阻斷產品流程。
/// </summary>
public sealed class LineNotificationResult
{
    private LineNotificationResult(
        bool succeeded,
        LineNotificationStatus status,
        LineNotificationRecipient? recipient,
        string? retryKey,
        string? errorCode,
        string? errorMessage,
        Exception? exception,
        IReadOnlyDictionary<string, string> metadata)
    {
        Succeeded = succeeded;
        Status = status;
        Recipient = recipient;
        RetryKey = retryKey;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Exception = exception;
        Metadata = metadata;
    }

    public bool Succeeded { get; }

    public LineNotificationStatus Status { get; }

    public LineNotificationRecipient? Recipient { get; }

    public string? RetryKey { get; }

    public string? ErrorCode { get; }

    public string? ErrorMessage { get; }

    public Exception? Exception { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }

    public static LineNotificationResult Success(LineNotificationRequest request)
        => new(true, LineNotificationStatus.Succeeded, request.Recipient, request.RetryKey, null, null, null, request.Metadata);

    public static LineNotificationResult Failure(
        LineNotificationRequest? request,
        LineNotificationStatus status,
        string errorCode,
        string errorMessage,
        Exception? exception = null)
        => new(
            false,
            status,
            request?.Recipient,
            request?.RetryKey,
            errorCode,
            errorMessage,
            exception,
            request?.Metadata ?? new Dictionary<string, string>());
}

/*
 * 檔案整體說明
 *
 * 目的與定位：共用通知流程的標準化結果，讓產品端不用直接解析 LINE SDK 的例外型別。
 *
 * 主要功能與流程：
 * 本檔宣告 LineNotificationStatus。
 * 資料／選項欄位包含 LineNotificationStatus，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.Workflows.Tests/LineNotificationWorkflowTests.cs、
 * LineMessagingProcessor.Workflows/LineNotificationResult.cs、
 * LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs、
 * LineMessagingProcessor.Workflows/LineReplyResult.cs、LineMessagingProcessor.Workflows/LineReplyWorkflow.cs
 * 追查目前工作樹中的合作／使用位置。
 */
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

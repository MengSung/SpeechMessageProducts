/*
 * 檔案整體說明
 *
 * 目的與定位：產品端送 LINE 通知時唯一需要組出的共用請求。 Metadata 保留給產品端追蹤 order id、contact id 等資料，但 workflow 不解讀這些產品語意。
 *
 * 主要功能與流程：
 * 本檔宣告 LineNotificationRequest。
 * 資料／選項欄位包含 LineNotificationRequest、Recipient、Content、RetryKey、Metadata，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.Workflows.Tests/LineNotificationWorkflowTests.cs、
 * LineMessagingProcessor.Workflows/ILineNotificationWorkflow.cs、
 * LineMessagingProcessor.Workflows/LineNotificationResult.cs、
 * LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs 追查目前工作樹中的合作／使用位置。
 */
namespace LineMessagingProcessor.Workflows;

/// <summary>
/// 產品端送 LINE 通知時唯一需要組出的共用請求。
/// Metadata 保留給產品端追蹤 order id、contact id 等資料，但 workflow 不解讀這些產品語意。
/// </summary>
public sealed class LineNotificationRequest
{
    public required LineNotificationRecipient Recipient { get; init; }

    public required LineNotificationContent Content { get; init; }

    public string? RetryKey { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

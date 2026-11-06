/*
 * 檔案整體說明
 *
 * 目的與定位：SendOrThrowAsync 使用的例外。例外內保留標準化結果，產品端可以記錄同一份錯誤資訊。
 *
 * 主要功能與流程：
 * 本檔宣告 LineNotificationException。
 * 資料／選項欄位包含 Result，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.Workflows.Tests/LineNotificationWorkflowTests.cs、
 * LineMessagingProcessor.Workflows/LineNotificationWorkflow.cs 追查目前工作樹中的合作／使用位置。
 */
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

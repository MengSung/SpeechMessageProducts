/*
 * 檔案整體說明
 *
 * 目的與定位：共用 LINE reply-token workflow 的輸入模型。 這裡只描述 LINE reply 所需的 protocol 資料：reply token、SDK message 清單、 以及診斷用 
 * metadata；不得放入 ChurchReport 的 CRM、Controller 或產品流程狀態。
 *
 * 主要功能與流程：
 * 本檔宣告 LineReplyRequest。
 * 資料／選項欄位包含 LineReplyRequest、ReplyToken、Messages、Metadata，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/LineSharedWorkflow/LineUtilityClassWorkflowTests.cs、
 * ChurchReport.MemberInfo.Tests/ReplyUtilityGroupRoomProfileAdapterTests.cs、
 * LineMessagingProcessor.Workflows/ILineReplyWorkflow.cs、LineMessagingProcessor.Workflows/LineReplyResult.cs、
 * LineMessagingProcessor.Workflows/LineReplyWorkflow.cs 追查目前工作樹中的合作／使用位置。
 */
using Line.Messaging;

namespace LineMessagingProcessor.Workflows;

/// <summary>
/// 共用 LINE reply-token workflow 的輸入模型。
/// 這裡只描述 LINE reply 所需的 protocol 資料：reply token、SDK message 清單、
/// 以及診斷用 metadata；不得放入 ChurchReport 的 CRM、Controller 或產品流程狀態。
/// </summary>
public sealed class LineReplyRequest
{
    public string? ReplyToken { get; init; }

    public IReadOnlyList<ISendMessage>? Messages { get; init; }

    public IDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

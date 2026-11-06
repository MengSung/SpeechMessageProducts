/*
 * 檔案整體說明
 *
 * 目的與定位：LINE reply-token 發送流程的共用介面。 reply-token 來自 webhook 事件，只能用來回覆該事件； 它和 push notification 的 user id / group 
 * id / room id 是不同語意， 因此獨立成 reply workflow，不混入 ILineNotificationWorkflow。
 *
 * 主要功能與流程：
 * 本檔宣告 ILineReplyWorkflow。
 * 資料／選項欄位包含 ILineReplyWorkflow，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/LineSharedWorkflow/LineUtilityClassWorkflowTests.cs、
 * ChurchReport.MemberInfo.Tests/ReplyUtilityGroupRoomProfileAdapterTests.cs、
 * LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs、
 * LineMessagingProcessor.Workflows/LineReplyWorkflow.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs 追查目前工作樹中的合作／使用位置。
 */
namespace LineMessagingProcessor.Workflows;

/// <summary>
/// LINE reply-token 發送流程的共用介面。
/// reply-token 來自 webhook 事件，只能用來回覆該事件；
/// 它和 push notification 的 user id / group id / room id 是不同語意，
/// 因此獨立成 reply workflow，不混入 <see cref="ILineNotificationWorkflow"/>。
/// </summary>
public interface ILineReplyWorkflow
{
    Task<LineReplyResult> ReplyAsync(LineReplyRequest request);

    Task ReplyOrThrowAsync(LineReplyRequest request);
}

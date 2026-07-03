using Line.Messaging;

namespace LineMessagingProcessor.Workflows;

/// <summary>
/// 共用 RichMenu 工作流介面。
/// 這一層只描述 LINE RichMenu 的可重用操作，不描述任何產品身分、CRM、付款或頁面流程。
/// 未來產品若要依身分、輸入文字、會員狀態切換 RichMenu，應該在產品端先決定要套用哪個 RichMenu，
/// 再呼叫這個 workflow 執行 LINE API 編排。
/// </summary>
public interface ILineRichMenuWorkflow
{
    Task<LineRichMenuResult> CreateUploadAndLinkAsync(LineRichMenuCreateUploadAndLinkRequest request);

    Task CreateUploadAndLinkOrThrowAsync(LineRichMenuCreateUploadAndLinkRequest request);

    Task<LineRichMenuResult> DeleteLinkedRichMenuAsync(LineRichMenuDeleteLinkedRequest request);

    Task DeleteLinkedRichMenuOrThrowAsync(LineRichMenuDeleteLinkedRequest request);
}

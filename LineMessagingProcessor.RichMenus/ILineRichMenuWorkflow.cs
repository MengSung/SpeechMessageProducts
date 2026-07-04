using Line.Messaging;

namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// RichMenu 建立、上傳、連結與解除連結的共用流程介面。
/// 呼叫端只需要提供標準請求；實作會統一處理 LINE API 呼叫、錯誤轉換與結果包裝。
/// </summary>
public interface ILineRichMenuWorkflow
{
    Task<LineRichMenuResult> CreateUploadAndLinkAsync(LineRichMenuCreateUploadAndLinkRequest request);

    Task CreateUploadAndLinkOrThrowAsync(LineRichMenuCreateUploadAndLinkRequest request);

    Task<LineRichMenuResult> DeleteLinkedRichMenuAsync(LineRichMenuDeleteLinkedRequest request);

    Task DeleteLinkedRichMenuOrThrowAsync(LineRichMenuDeleteLinkedRequest request);
}


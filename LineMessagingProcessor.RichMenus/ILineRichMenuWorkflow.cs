using Line.Messaging;

namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// RichMenu 建立、上傳、連結與解除連結的共用流程介面。
/// 呼叫端只需要提供標準請求；實作會統一處理 LINE API 呼叫、錯誤轉換與結果包裝。
/// </summary>
public interface ILineRichMenuWorkflow
{
    /// <summary>
    /// 建立 LINE RichMenu、上傳圖片，並直接連結到一位使用者。
    /// </summary>
    /// <param name="request">此操作需要的 user id、選單版面、圖片 stream factory 與 metadata。</param>
    Task<LineRichMenuResult> CreateUploadAndLinkAsync(LineRichMenuCreateUploadAndLinkRequest request);

    /// <summary>
    /// 執行 <see cref="CreateUploadAndLinkAsync"/>；若失敗則丟出 <see cref="LineRichMenuException"/>。
    /// </summary>
    /// <param name="request">建立、上傳與連結的 request。</param>
    Task CreateUploadAndLinkOrThrowAsync(LineRichMenuCreateUploadAndLinkRequest request);

    /// <summary>
    /// 解除使用者目前 RichMenu 連結，並刪除該連結指向的 provider RichMenu。
    /// </summary>
    /// <param name="request">刪除與解除連結操作需要的 user id 與 metadata。</param>
    Task<LineRichMenuResult> DeleteLinkedRichMenuAsync(LineRichMenuDeleteLinkedRequest request);

    /// <summary>
    /// 執行 <see cref="DeleteLinkedRichMenuAsync"/>；若失敗則丟出 <see cref="LineRichMenuException"/>。
    /// </summary>
    /// <param name="request">刪除與解除連結 request。</param>
    Task DeleteLinkedRichMenuOrThrowAsync(LineRichMenuDeleteLinkedRequest request);
}


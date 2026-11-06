/*
 * 檔案整體說明
 *
 * 目的與定位：RichMenu 建立、上傳、連結與解除連結的共用流程介面。 呼叫端只需要提供標準請求；實作會統一處理 LINE API 呼叫、錯誤轉換與結果包裝。
 *
 * 主要功能與流程：
 * 本檔宣告 ILineRichMenuWorkflow。
 * - 建立 LINE RichMenu、上傳圖片，並直接連結到一位使用者。
 * - 執行 CreateUploadAndLinkAsync；若失敗則丟出 LineRichMenuException。
 * - 解除使用者目前 RichMenu 連結，並刪除該連結指向的 provider RichMenu。
 * - 執行 DeleteLinkedRichMenuAsync；若失敗則丟出 LineRichMenuException。
 * 資料／選項欄位包含 ILineRichMenuWorkflow，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PushUtilityWorkflowTests.cs、
 * LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessorServiceCollectionExtensionsTests.cs、
 * LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs、
 * LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs、
 * SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs 追查目前工作樹中的合作／使用位置。
 */
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

/*
 * 檔案整體說明
 *
 * 目的與定位：協調單次使用者互動中的 policy 評估與 RichMenu 指派。
 *
 * 主要功能與流程：
 * 本檔宣告 IRichMenuOrchestrator。
 * - 依傳入 context 套用最佳 RichMenu decision。
 * 資料／選項欄位包含 IRichMenuOrchestrator，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessorServiceCollectionExtensionsTests.cs、
 * LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs、
 * LineMessagingProcessor.RichMenus/RichMenuOrchestrator.cs 追查目前工作樹中的合作／使用位置。
 */
namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 協調單次使用者互動中的 policy 評估與 RichMenu 指派。
/// </summary>
public interface IRichMenuOrchestrator
{
    /// <summary>
    /// 依傳入 context 套用最佳 RichMenu decision。
    /// </summary>
    /// <param name="context">policy 評估所需的 LINE 使用者 context 與訊息事實。</param>
    /// <param name="cancellationToken">傳入 policies 與 assignment workflows 的取消權杖。</param>
    Task<LineRichMenuAssignmentResult> ApplyAsync(RichMenuContext context, CancellationToken cancellationToken = default);
}

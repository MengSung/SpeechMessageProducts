/*
 * 檔案整體說明
 *
 * 目的與定位：定義一條可判斷使用者 RichMenu 是否應改變的規則。 policy 刻意保持小而可組合；orchestrator 會評估所有 policy，並只套用強度最高的 decision。
 *
 * 主要功能與流程：
 * 本檔宣告 IRichMenuPolicy。
 * - 評估傳入 context，並回傳 RichMenu decision。
 * 資料／選項欄位包含 IRichMenuPolicy，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.RichMenus.Tests/Orchestration/RichMenuOrchestratorTests.cs、
 * LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerPolicy.cs、
 * LineMessagingProcessor.RichMenus/RichMenuOrchestrator.cs 追查目前工作樹中的合作／使用位置。
 */
namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 定義一條可判斷使用者 RichMenu 是否應改變的規則。
/// policy 刻意保持小而可組合；orchestrator 會評估所有 policy，並只套用強度最高的 decision。
/// </summary>
public interface IRichMenuPolicy
{
    /// <summary>
    /// 評估傳入 context，並回傳 RichMenu decision。
    /// </summary>
    /// <param name="context">policy 可使用的使用者與訊息 context。</param>
    /// <param name="cancellationToken">供需要非同步資料的 policy 使用的取消權杖。</param>
    Task<RichMenuDecision> DecideAsync(RichMenuContext context, CancellationToken cancellationToken = default);
}

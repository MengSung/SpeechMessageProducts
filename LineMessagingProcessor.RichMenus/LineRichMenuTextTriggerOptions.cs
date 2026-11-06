/*
 * 檔案整體說明
 *
 * 目的與定位：設定文字觸發的 RichMenu 切換。 dictionary 中每筆資料都將一段精確的 LINE inbound message 對應到應指派的應用程式 menu key。
 *
 * 主要功能與流程：
 * 本檔宣告 LineRichMenuTextTriggerOptions。
 * - 取得精確文字到 menu key 的對照表。 預設 comparer 不分大小寫；resolver 查詢前仍會先 trim 前後空白。
 * 資料／選項欄位包含 LineRichMenuTextTriggerOptions、ExactTextToMenuKey，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.RichMenus.Tests/Orchestration/RichMenuOrchestratorTests.cs、
 * LineMessagingProcessor.RichMenus.Tests/Triggers/LineRichMenuTextTriggerResolverTests.cs、
 * LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerResolver.cs 追查目前工作樹中的合作／使用位置。
 */
namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 設定文字觸發的 RichMenu 切換。
/// dictionary 中每筆資料都將一段精確的 LINE inbound message 對應到應指派的應用程式 menu key。
/// </summary>
public sealed class LineRichMenuTextTriggerOptions
{
    /// <summary>
    /// 取得精確文字到 menu key 的對照表。
    /// 預設 comparer 不分大小寫；resolver 查詢前仍會先 trim 前後空白。
    /// </summary>
    public Dictionary<string, string> ExactTextToMenuKey { get; } = new(StringComparer.OrdinalIgnoreCase);
}

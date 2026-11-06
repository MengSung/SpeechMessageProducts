/*
 * 檔案整體說明
 *
 * 目的與定位：將 LINE 傳入文字解析成應用程式 RichMenu key。
 *
 * 主要功能與流程：
 * 本檔宣告 ILineRichMenuTextTriggerResolver。
 * - 回傳 received text 對應的 menu key；若沒有 trigger 命中則回傳 null。
 * - 嘗試將 received text 解析成 menu key。
 * 資料／選項欄位包含 ILineRichMenuTextTriggerResolver，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerPolicy.cs、
 * LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerResolver.cs 追查目前工作樹中的合作／使用位置。
 */
namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 將 LINE 傳入文字解析成應用程式 RichMenu key。
/// </summary>
public interface ILineRichMenuTextTriggerResolver
{
    /// <summary>
    /// 回傳 received text 對應的 menu key；若沒有 trigger 命中則回傳 null。
    /// </summary>
    /// <param name="receivedText">LINE 收到的原始文字。</param>
    string? ResolveMenuKey(string? receivedText);

    /// <summary>
    /// 嘗試將 received text 解析成 menu key。
    /// </summary>
    /// <param name="receivedText">LINE 收到的原始文字。</param>
    /// <param name="menuKey">方法回傳 true 時為解析出的 menu key；否則為空字串。</param>
    bool TryResolve(string? receivedText, out string menuKey);
}

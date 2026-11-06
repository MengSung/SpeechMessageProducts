/*
 * 檔案整體說明
 *
 * 目的與定位：定義 RichMenu policy decision 覆蓋其他 decisions 的強度。 orchestrator 評估同一使用者事件的多個 policies 時，數值較高者勝出。
 *
 * 主要功能與流程：
 * 本檔宣告 RichMenuDecisionPriority。
 * - 使用者文字命中已設定 trigger，應覆蓋預設或角色型選單。
 * - 明確命令或直接 workflow request，應優先於其他 policies。
 * 資料／選項欄位包含 RichMenuDecisionPriority，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerPolicy.cs、
 * LineMessagingProcessor.RichMenus/RichMenuDecision.cs、LineMessagingProcessor.RichMenus/RichMenuOrchestrator.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 定義 RichMenu policy decision 覆蓋其他 decisions 的強度。
/// orchestrator 評估同一使用者事件的多個 policies 時，數值較高者勝出。
/// </summary>
public enum RichMenuDecisionPriority
{
    /// <summary>
    /// 沒有任何 policy decision。
    /// </summary>
    None = 0,

    /// <summary>
    /// 預設或基準選單選擇。
    /// </summary>
    Default = 10,

    /// <summary>
    /// 依角色選擇，例如為會員、同工或管理者指派選單。
    /// </summary>
    Role = 50,

    /// <summary>
    /// 使用者文字命中已設定 trigger，應覆蓋預設或角色型選單。
    /// </summary>
    TextTrigger = 80,

    /// <summary>
    /// 明確命令或直接 workflow request，應優先於其他 policies。
    /// </summary>
    Explicit = 100
}

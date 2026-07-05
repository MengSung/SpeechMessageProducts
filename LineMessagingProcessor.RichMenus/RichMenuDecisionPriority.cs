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

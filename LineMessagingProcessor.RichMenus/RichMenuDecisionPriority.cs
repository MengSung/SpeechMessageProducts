// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：LineMessagingProcessor.RichMenus/RichMenuDecisionPriority.cs
// 所屬區塊：LINE RichMenu 共用編排、佈署、指派、狀態與測試流程模組。
// 檔案責任：此檔案位於 LINE 或 RichMenu 相關流程，註解重點在說明 LINE API 契約、使用者狀態、通知副作用與 workflow 串接方式。
// 主要型別：enum RichMenuDecisionPriority
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：未宣告 using；請由命名空間、同檔型別或完全限定名稱判讀相依性。
// 閱讀路徑：閱讀此檔案時應先確認 LINE userId/groupId/roomId、replyToken、push/reply API、RichMenu alias 與使用者狀態是否保持正確對應。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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

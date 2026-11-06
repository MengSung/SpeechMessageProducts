/*
 * 檔案整體說明
 *
 * 目的與定位：刪除已連結 RichMenu 的標準請求。 呼叫端只提供 LINE user id 與必要追蹤資料；解除連結與刪除遠端選單由共用 workflow 負責。
 *
 * 主要功能與流程：
 * 本檔宣告 LineRichMenuDeleteLinkedRequest。
 * - 目標 LINE 使用者 id。 workflow 會先用它查詢目前連結的 richMenuId，再解除連結並刪除該遠端 RichMenu。
 * - 呼叫端提供的追蹤資料。 這些資料不會送到 LINE，只會原樣回填到結果，方便管理端或日誌對照來源流程。
 * 資料／選項欄位包含 LineRichMenuDeleteLinkedRequest、UserId、Metadata，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PushUtilityWorkflowTests.cs、
 * LineMessagingProcessor.RichMenus.Tests/LineRichMenuWorkflowTests.cs、
 * LineMessagingProcessor.RichMenus/ILineRichMenuWorkflow.cs、
 * LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs 追查目前工作樹中的合作／使用位置。
 */
namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 刪除已連結 RichMenu 的標準請求。
/// 呼叫端只提供 LINE user id 與必要追蹤資料；解除連結與刪除遠端選單由共用 workflow 負責。
/// </summary>
public sealed class LineRichMenuDeleteLinkedRequest
{
    /// <summary>
    /// 目標 LINE 使用者 id。
    /// workflow 會先用它查詢目前連結的 richMenuId，再解除連結並刪除該遠端 RichMenu。
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// 呼叫端提供的追蹤資料。
    /// 這些資料不會送到 LINE，只會原樣回填到結果，方便管理端或日誌對照來源流程。
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

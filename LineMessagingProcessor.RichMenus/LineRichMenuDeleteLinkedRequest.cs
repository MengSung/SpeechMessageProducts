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


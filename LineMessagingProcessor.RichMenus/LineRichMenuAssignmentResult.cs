namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 回報使用者 RichMenu 被指派、保留或移除後的結果。
/// 此結果同時攜帶業務層狀態（例如已指派的 menu key）與 provider 層狀態（例如 richMenuId），
/// 讓呼叫端不必查看 workflow 內部細節也能做後續判斷。
/// </summary>
public sealed class LineRichMenuAssignmentResult
{
    /// <summary>
    /// 建立成功的 RichMenu 指派結果。
    /// </summary>
    private LineRichMenuAssignmentResult(bool changed, string? previousMenuKey, string? assignedMenuKey, string? richMenuId)
    {
        Succeeded = true;
        Status = LineRichMenuStatus.Succeeded;
        Changed = changed;
        PreviousMenuKey = previousMenuKey;
        AssignedMenuKey = assignedMenuKey;
        RichMenuId = richMenuId;
    }

    /// <summary>
    /// 建立失敗的 RichMenu 指派結果，並保留標準化錯誤資訊。
    /// </summary>
    private LineRichMenuAssignmentResult(LineRichMenuStatus status, string errorCode, string errorMessage)
    {
        Succeeded = false;
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// 取得 assignment workflow 是否成功完成。
    /// </summary>
    public bool Succeeded { get; }

    /// <summary>
    /// 取得標準化 workflow 狀態。
    /// </summary>
    public LineRichMenuStatus Status { get; }

    /// <summary>
    /// 當 <see cref="Succeeded"/> 為 false 時，取得穩定的應用程式錯誤代碼。
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// workflow 失敗時，取得可讀的錯誤訊息。
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// 取得實際 LINE 指派是否有變更。
    /// 沒有變更仍代表 workflow 成功完成，而不是失敗。
    /// </summary>
    public bool Changed { get; }

    /// <summary>
    /// 取得操作前已知的作用中應用程式 menu key。
    /// </summary>
    public string? PreviousMenuKey { get; }

    /// <summary>
    /// 取得操作後指派的應用程式 menu key；若操作為 unlink 則為 null。
    /// </summary>
    public string? AssignedMenuKey { get; }

    /// <summary>
    /// 操作指派選單時，取得連結到使用者的 LINE richMenuId。
    /// </summary>
    public string? RichMenuId { get; }

    /// <summary>
    /// 建立成功的 link 結果。
    /// </summary>
    /// <param name="previousMenuKey">先前的應用程式 menu key；若無資料則為 null。</param>
    /// <param name="assignedMenuKey">workflow 指派的應用程式 menu key。</param>
    /// <param name="richMenuId">連結到使用者的 LINE richMenuId。</param>
    /// <param name="changed">此次 link 是否改變使用者實際生效的選單。</param>
    public static LineRichMenuAssignmentResult Linked(string? previousMenuKey, string assignedMenuKey, string richMenuId, bool changed)
        => new(changed, previousMenuKey, assignedMenuKey, richMenuId);

    /// <summary>
    /// 建立成功但刻意不變更目前選單的結果。
    /// </summary>
    /// <param name="currentMenuKey">目前已知的應用程式 menu key。</param>
    public static LineRichMenuAssignmentResult NoChange(string? currentMenuKey)
        => new(false, currentMenuKey, currentMenuKey, null);

    /// <summary>
    /// 建立成功的 unlink 結果。
    /// </summary>
    /// <param name="previousMenuKey">從使用者身上移除的應用程式 menu key；若無資料則為 null。</param>
    /// <param name="changed">是否真的移除了既有指派。</param>
    public static LineRichMenuAssignmentResult Unlinked(string? previousMenuKey, bool changed)
        => new(changed, previousMenuKey, null, null);

    /// <summary>
    /// 建立失敗的 RichMenu 指派結果。
    /// </summary>
    /// <param name="status">標準化失敗狀態。</param>
    /// <param name="errorCode">穩定的應用程式錯誤代碼。</param>
    /// <param name="errorMessage">可讀的失敗細節。</param>
    public static LineRichMenuAssignmentResult Failure(LineRichMenuStatus status, string errorCode, string errorMessage)
        => new(status, errorCode, errorMessage);
}

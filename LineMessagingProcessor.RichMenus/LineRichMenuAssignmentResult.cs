namespace LineMessagingProcessor.RichMenus;

public sealed class LineRichMenuAssignmentResult
{
    private LineRichMenuAssignmentResult(bool changed, string? previousMenuKey, string? assignedMenuKey, string? richMenuId)
    {
        Succeeded = true;
        Status = LineRichMenuStatus.Succeeded;
        Changed = changed;
        PreviousMenuKey = previousMenuKey;
        AssignedMenuKey = assignedMenuKey;
        RichMenuId = richMenuId;
    }

    private LineRichMenuAssignmentResult(LineRichMenuStatus status, string errorCode, string errorMessage)
    {
        Succeeded = false;
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public bool Succeeded { get; }

    public LineRichMenuStatus Status { get; }

    public string? ErrorCode { get; }

    public string? ErrorMessage { get; }

    public bool Changed { get; }

    public string? PreviousMenuKey { get; }

    public string? AssignedMenuKey { get; }

    public string? RichMenuId { get; }

    public static LineRichMenuAssignmentResult Linked(string? previousMenuKey, string assignedMenuKey, string richMenuId, bool changed)
        => new(changed, previousMenuKey, assignedMenuKey, richMenuId);

    public static LineRichMenuAssignmentResult NoChange(string? currentMenuKey)
        => new(false, currentMenuKey, currentMenuKey, null);

    public static LineRichMenuAssignmentResult Unlinked(string? previousMenuKey, bool changed)
        => new(changed, previousMenuKey, null, null);

    public static LineRichMenuAssignmentResult Failure(LineRichMenuStatus status, string errorCode, string errorMessage)
        => new(status, errorCode, errorMessage);
}

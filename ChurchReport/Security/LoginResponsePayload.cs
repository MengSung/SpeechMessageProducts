namespace ChurchReport.Security
{
    public sealed record LoginResponsePayload(
        string DisplayViewType,
        string ActiveListId,
        string message,
        string fullname);

    public static class LoginResponseFactory
    {
        public static LoginResponsePayload Build(string displayViewType, string activeListId, string fullName)
        {
            var safeFullName = fullName ?? string.Empty;

            return new LoginResponsePayload(
                displayViewType ?? string.Empty,
                activeListId ?? string.Empty,
                $"登入 {safeFullName} 成功!",
                safeFullName);
        }
    }
}

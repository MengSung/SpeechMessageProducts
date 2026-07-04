namespace LineMessagingProcessor.AspNetCore;

/// <summary>
/// ASP.NET Core 專案註冊 LINE processor 時使用的選項。只放 LINE 技術設定，不放任何產品業務設定。
/// </summary>
public sealed class LineMessagingProcessorOptions
{
    public string ChannelAccessToken { get; set; } = string.Empty;

    public string ApiBaseUri { get; set; } = "https://api.line.me/v2";
}

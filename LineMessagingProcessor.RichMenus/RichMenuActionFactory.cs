using Line.Messaging;

namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// 建立可在 RichMenu aliases 之間切換使用者選單的 LINE template actions。
/// 將 action 建立集中在此處，可讓需要 LINE <c>richmenuswitch</c> action type 的應用程式選單，
/// 共用一致的 alias 驗證與 postback data 驗證規則。
/// </summary>
public static class RichMenuActionFactory
{
    /// <summary>
    /// 建立指向指定 alias id 的 RichMenu switch action。
    /// </summary>
    /// <param name="aliasId">provisioning 期間設定的 LINE RichMenu alias id。</param>
    /// <param name="data">使用者點擊 action 時，LINE webhook 回傳的 postback data。</param>
    /// <param name="label">選填標籤，供會顯示 action 文字的 client 使用。</param>
    public static RichMenuSwitchTemplateAction SwitchToAlias(string aliasId, string data, string? label = null)
        => Switch(aliasId, data, label);

    /// <summary>
    /// 建立已驗證的 <see cref="RichMenuSwitchTemplateAction"/>。
    /// </summary>
    /// <param name="aliasId">LINE 會解析成目前 richMenuId 的 alias id。</param>
    /// <param name="data">必要的 postback data payload。</param>
    /// <param name="label">選填顯示標籤；未提供時會送出空字串。</param>
    public static RichMenuSwitchTemplateAction Switch(string aliasId, string data, string? label = null)
    {
        if (string.IsNullOrWhiteSpace(aliasId))
        {
            throw new ArgumentException("Alias id is required.", nameof(aliasId));
        }

        if (string.IsNullOrWhiteSpace(data))
        {
            throw new ArgumentException("Postback data is required.", nameof(data));
        }

        return new RichMenuSwitchTemplateAction(aliasId.Trim(), data.Trim(), label ?? string.Empty);
    }
}

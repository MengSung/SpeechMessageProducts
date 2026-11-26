/*
檔案整體說明
目的與定位：F6登入驗證與產品Channel映射的唯讀部署快照，只包含公開識別，不含Channel secret、access token或使用者值。
主要功能與流程：組合根在啟動讀入Login Channel、固定Messaging Channel及LIFF app ID；filter與產品resolver只讀這個快照，沒有配置時拒絕登入，不從前端補值。
使用邊界與維護：快照最長活至Host關閉，重新部署才改映射；Product及Channel不是query authority，resolver仍須逐次核對D365產品歸屬。本檔不建立資源、讀秘密或自行證明Channel對應有效，測試必須分別驗映射錯誤與缺設定拒絕。
*/
namespace SpeechMessage.Liff.AspNetCore;

/// <summary>固定伺服器部署映射，不從任何request、cookie或Session覆寫；空值表示尚未啟用。</summary>
/// <param name="LoginChannelId">官方驗證aud所需的LINE Login Channel公開ID。</param>
/// <param name="MessagingChannelKey">本LIFF入口對應的Messaging Channel公開ID，仍由D365逐次驗證產品。</param>
/// <param name="LiffId">LINE核發的LIFF app ID，空值時頁面只顯示尚未設定。</param>
public sealed record LiffOptions(string LoginChannelId, string MessagingChannelKey, string LiffId)
{
    /// <summary>兩種Channel皆已配置才允許驗證；不代表已驗證token或D365權限。</summary>
    public bool IsConfigured => IsChannelId(LoginChannelId) && IsChannelId(MessagingChannelKey);

    /// <summary>公開Channel識別只允許有界ASCII數字，拒絕空白、路徑或混淆內容，不正規化呼叫值。</summary>
    /// <param name="value">部署值，不是來自前端的權限聲稱。</param>
    /// <returns>是否符合固定ID格式。</returns>
    private static bool IsChannelId(string value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 32 && value.All(c => c is >= '0' and <= '9');
}

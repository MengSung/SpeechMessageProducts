/*
檔案整體說明
目的與定位：提供 Webhook 啟動時的不可變非秘密選項，固定一 MB body 上限及舊路由明確授權。
主要功能與流程：組合根只從部署設定讀取 Legacy 開關與頻道，啟用時必須是明確的有效 LINE 數字 ID；固定容量不接受 caller 覆寫。不保存 IConfiguration 或 reload watcher。
使用邊界與維護：LegacyChannelId 只是指定使用哪個待驗簽 channel，不是權限證明。新舊路由都必須通過同一 verifier；選項只活至目前 Host 結束，不保存 request、租戶 cache、連線或 credential，錯誤一律固定文字。
*/
namespace LineMessagingProcessor.AspNetCore.Webhooks;

/// <summary>單一 Host 的唯讀非秘密啟動政策；舊路由缺明確 channel 時 fail closed，不猜測預設租戶。</summary>
public sealed class WebhookOptions
{
    /// <summary>每個 request 的原始 body 上限為一 MiB，恰好上限允許，超過一個 byte 就拒絕。</summary>
    public const int MaximumBodyBytes = 1024 * 1024;
    /// <summary>舊 /api/callback 的明確啟用旗標，缺省關閉。</summary>
    public bool EnableLegacyCallback { get; }
    /// <summary>受信任部署指定的 legacy channel；只有啟用時才使用，仍須驗簽。</summary>
    public string? LegacyChannelId { get; }

    /// <summary>建立獨立快照；啟用但未配置有效 channel 或提供非法 channel 時拒絕，避免路由落入其他租戶。</summary>
    /// <param name="enableLegacyCallback">明確 opt-in，不能從 query 或 body 取得。</param>
    /// <param name="legacyChannelId">部署提供的數字 channel，最多二十位。</param>
    public WebhookOptions(bool enableLegacyCallback = false, string? legacyChannelId = null)
    {
        if ((enableLegacyCallback && legacyChannelId is null) ||
            (legacyChannelId is not null && !IsValidChannelId(legacyChannelId)))
            throw new InvalidOperationException("Legacy webhook requires an explicit valid channel.");
        EnableLegacyCallback = enableLegacyCallback;
        LegacyChannelId = legacyChannelId;
    }

    /// <summary>限制 route／設定形狀符合 A.8 varchar(20)，此純檢查不授權、不解析或保存任何設定。</summary>
    /// <param name="channelId">待驗證純字串，null、非 ASCII 數字或超長皆拒絕。</param>
    /// <returns>僅代表形狀有效，不能代替 verifier 的伺服器 channel／tenant 核對。</returns>
    public static bool IsValidChannelId(string? channelId) =>
        channelId is { Length: > 0 and <= 20 } && channelId.All(char.IsAsciiDigit);
}

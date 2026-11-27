/*
檔案整體說明
目的與定位：F3 部署快照的輸入守門，集中驗證組織、金鑰及禁止的舊式 Channel 設定，不參與 HTTP 或資料庫查詢。
主要功能與流程：成對缺席表示封閉模式；部分設定、未知欄位、非精確 D 格式組織、不同 Scheduling 組織及非 canonical 六十四 bytes Base64 一律拒絕。驗證暫存陣列在 finally 清零，快照只保存單次消費的字串參照，建立目錄時轉成暫存 key 後立即清除該參照。
使用邊界與維護：IConfiguration 只在 Read 堆疊借用，不訂閱 reload 或保存 provider；CLR 字串不能保證覆寫，原始字串由部署組態 owner 持有至 Host 結束，快照自身於首次建立目錄或 Dispose 時清參照。沒有未移交給 Host 的 key 陣列，也沒有連線、timer 或背景 task；失敗訊息固定且不附原始 Base64 或組織值。合成設定矩陣與位元組檢查保護此契約。
*/
using System.Security.Cryptography;

namespace SpeechMessageProducts.GoodShepherd2.Web.Channels;

/// <summary>單一部署的受信任快照；只允許一次交出金鑰，不保存設定根或使用者狀態。</summary>
internal sealed class GoodShepherd2ChannelConfiguration : IDisposable
{
    private string? encodedKey;
    /// <summary>完整設定才為 true；此值固定於 Add 時，不接受 HTTP 啟用功能。</summary>
    internal bool IsConfigured { get; }
    /// <summary>固定部署正式組織 GUID，空值只代表整對設定缺席，不能建立目錄。</summary>
    internal Guid OrganizationId { get; }

    /// <summary>保存已驗證的純部署值與一次性字串參照，不持有解碼陣列或借入組態。</summary>
    private GoodShepherd2ChannelConfiguration(Guid organizationId, string? encodedKey)
    {
        OrganizationId = organizationId;
        this.encodedKey = encodedKey;
        IsConfigured = encodedKey is not null;
    }

    /// <summary>完成全部輸入驗證才回快照；不建線、不改服務集合，不讓錯誤訊息帶入設定值。</summary>
    internal static GoodShepherd2ChannelConfiguration Read(IConfiguration configuration)
    {
        RejectForeignChannelConfiguration(configuration);
        var channels = configuration.GetSection("Channels");
        var line = configuration.GetSection("LineChannels");
        if (channels.Value is not null || channels.GetChildren().Any(child => !string.Equals(child.Key, "OrganizationId", StringComparison.OrdinalIgnoreCase)) ||
            line.Value is not null || line.GetChildren().Any(child => !string.Equals(child.Key, "TokenKey", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Invalid channel deployment configuration.");
        var organization = channels.GetSection("OrganizationId");
        var token = line.GetSection("TokenKey");
        if (!organization.Exists() && !token.Exists()) return new(Guid.Empty, null);
        var organizationValue = organization.Value;
        var encodedValue = token.Value;
        if (organization.GetChildren().Any() || token.GetChildren().Any() ||
            !Guid.TryParseExact(organizationValue, "D", out var organizationId) || organizationId == Guid.Empty)
            throw new InvalidOperationException("Channel organization must be an explicit non-empty deployment GUID.");
        var scheduling = configuration.GetSection("Scheduling:OrganizationId");
        if (scheduling.Exists() && (scheduling.GetChildren().Any() ||
            !Guid.TryParseExact(scheduling.Value, "D", out var schedulingId) || schedulingId != organizationId))
            throw new InvalidOperationException("Channel and scheduling organizations must match.");
        var validation = DecodeKey(encodedValue);
        try { return new(organizationId, encodedValue); }
        finally { CryptographicOperations.ZeroMemory(validation); }
    }

    /// <summary>禁止從設定映射或舊路由取得另一個產品的 secret；明確 false 的 legacy 開關可保留。</summary>
    private static void RejectForeignChannelConfiguration(IConfiguration configuration)
    {
        var mapping = configuration.GetSection("LineWebhook:Channels");
        var legacy = configuration.GetSection("LineWebhook:EnableLegacyCallback");
        if (mapping.Exists() || configuration.GetSection("LineWebhook:LegacyChannelId").Exists() ||
            legacy.GetChildren().Any() || (legacy.Value is not null && (!bool.TryParse(legacy.Value, out var enabled) || enabled)))
            throw new InvalidOperationException("Channel hosting does not allow legacy or configuration channel mappings.");
    }

    /// <summary>目錄 factory 單次取用；先清字串參照，解碼後由呼叫端 finally 清零暫存陣列。</summary>
    internal byte[] TakeKey() => DecodeKey(Interlocked.Exchange(ref encodedKey, null));

    /// <summary>只接受精確 canonical Base64，所有被拒絕的解碼結果也清零；不保留 FormatException 的輸入上下文。</summary>
    private static byte[] DecodeKey(string? value)
    {
        if (value is not { Length: 88 }) throw new InvalidOperationException("Channel token key must be canonical Base64 for 64 bytes.");
        byte[] candidate;
        try { candidate = Convert.FromBase64String(value); }
        catch (FormatException) { throw new InvalidOperationException("Channel token key must be canonical Base64 for 64 bytes."); }
        if (candidate.Length == 64 && string.Equals(Convert.ToBase64String(candidate), value, StringComparison.Ordinal)) return candidate;
        CryptographicOperations.ZeroMemory(candidate);
        throw new InvalidOperationException("Channel token key must be canonical Base64 for 64 bytes.");
    }

    /// <summary>Host 關閉時清除尚未消費的字串參照；沒有陣列、訂閱或借入設定需要釋放。</summary>
    public void Dispose() => Interlocked.Exchange(ref encodedKey, null);
}

/*
檔案整體說明
目的與定位：定義階段三原始位元組驗簽的唯一注入邊界，正式 HMAC 與 Tenancy 查詢留 P3-03，不提供任何預設成功實作。
主要功能與流程：HTTP adapter 先限制一 MB，再借出同一份 body 與唯一 X-Line-Signature；驗證器依伺服器部署的頻道白名單及真實 secret 判定，不得由 route 授予租戶權限。
使用邊界與維護：channelId 是不受信任的數字識別，必須在固定產品部署內解析。destination 是 bot user ID，不是數字 channel ID；只有部署已提供可信 bot mapping 時才核對，不捏造或強迫未知 Bot ID。false 表示預期拒絕，基礎設施故障拋原例外。body／signature 僅在 await 完成前借用，不得快取、排入背景或保存 credential；caller token 原樣傳遞。
*/
namespace LineMessagingProcessor.AspNetCore.Webhooks;

/// <summary>以受信任部署資料驗證指定頻道及原始 body；不將憑證或 request 狀態交給下游。</summary>
public interface IWebhookSignatureVerifier
{
    /// <summary>只對原始 bytes 驗簽，先拒絕未配置頻道及空 secret，再核對簽章；只有可信 bot mapping 存在時才核對 destination，禁止把它直接與數字 channel ID 比較。</summary>
    /// <param name="channelId">尚未授權的 route channel，成功必須證明其屬於目前產品部署。</param>
    /// <param name="body">adapter 短期借出的原始 bytes；不得在方法返回後保存參照。</param>
    /// <param name="signature">單一有界標頭值；不能寫入任何日誌或傳入 queue。</param>
    /// <param name="cancellationToken">HTTP caller 取消，不是內部逾時的替代分類。</param>
    /// <returns>成功才允許解析／去重；預期拒絕回 false，真正故障保留原始例外。</returns>
    ValueTask<bool> VerifyAsync(string channelId, ReadOnlyMemory<byte> body, string signature, CancellationToken cancellationToken);
}

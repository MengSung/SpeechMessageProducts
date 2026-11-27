/*
檔案整體說明
目的與定位：定義三產品共用 Channel 目錄供傳輸出口使用的最小憑證解析契約，位於雙目標純核心，不查詢 D365、不保存憑證，也不建立 HTTP client。
主要功能與流程：呼叫端以已完成伺服器授權的 channelKey 向固定產品來源解析既有 LineChannelCredential；未知或不在該產品目錄的頻道回 false。來源的更新與清理由具體目錄 owner 負責，介面不提供列舉 token 或序列化秘密的能力。
使用邊界與維護：公開路徑值不是權限，呼叫端仍須驗證租戶與 Channel 關聯。憑證只供本次傳輸借用，不得記錄、回傳瀏覽器、交給排程持久化或放入跨產品快取。字串無法可靠清零，實作必須有界保留、在停止時清除參照；本介面自身沒有可釋放資源或背景工作。
*/
namespace LineMessagingProcessor.Core.ChannelPool;

/// <summary>
/// 由固定產品的受信任來源提供當次傳輸憑證；解析成功不取代租戶授權或收件人隔離。
/// 實作不可因未知頻道而回落到預設產品、舊組織或其他 Channel。
/// </summary>
public interface IChannelCredentialSource
{
    /// <summary>
    /// 取得目前產品目錄內的憑證；未知頻道回 false，真正來源故障必須交由具有診斷責任的邊界處理。
    /// 回傳值僅供呼叫端本次操作借用，不得保存到使用者 Session、工作佇列或日誌。
    /// </summary>
    /// <param name="channelKey">伺服器已驗證所屬範圍的 Channel 識別，不是產品選擇或授權輸入。</param>
    /// <param name="credential">成功時的既有核心憑證型別；失敗時為 null，不包含其他頻道後備值。</param>
    /// <returns>來源當前有完整且符合產品邊界的憑證時為 true。</returns>
    bool TryGetCredential(string channelKey, out LineChannelCredential? credential);
}

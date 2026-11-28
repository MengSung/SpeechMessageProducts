/*
檔案整體說明
目的與定位：F7圖片發布的中立非同步契約，由已完成授權的伺服器流程傳入scope與借入Stream，回傳獨立公開PNG描述，不參考HTTP或產品流程。
主要功能與流程：實作驗證有界PNG／JPEG、中心裁切640方形、限制300KiB並原子發布新檔名；只有完整成功才返回，預期輸入拒絕與真正I/O故障分開，呼叫端負責共用告警。
使用邊界與維護：來源Stream始終由呼叫端擁有；單次影像與暫存不可跨scope保存，取消必須等待清理，不遺棄仍在寫入的工作。store owner的停止與Dispose依實作契約由Host負責，介面本身沒有全域狀態。
*/
namespace SpeechMessage.Assets;

/// <summary>已授權伺服器流程使用的公開Logo發布入口；實作不代替使用者、產品或租戶授權。</summary>
public interface IPublicAssetStore
{
    /// <summary>借讀有界影像並發布新的不可變PNG；正常取消須清理候選，真正故障原樣交呼叫端告警。</summary>
    /// <param name="scope">已由伺服器授權且具有權威租戶ID的scope。</param>
    /// <param name="image">目前位置開始的PNG／JPEG來源；呼叫端唯一擁有，方法不得Dispose。</param>
    /// <param name="ct">明確caller取消；返回前須完成所有自有清理，不可遺棄工作。</param>
    /// <returns>寫入及flush完成才返回的新網址與最終SHA，沒有影像或Session參照。</returns>
    Task<PublicAsset> PutTenantLogoAsync(AssetScope scope, Stream image, CancellationToken ct);
}

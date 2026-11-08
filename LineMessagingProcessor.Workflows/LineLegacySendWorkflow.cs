/*
 * 檔案整體說明
 *
 * 目的與定位：P2-06 舊 ToolUtility 推播相容流程，將任意 SDK 訊息與 multicast 轉交
 * net10 processor；不是正式會友授權流程，也不替換 P2-04 Core 的教會／頻道核對。
 * 主要功能與流程：單次 push 沿既有 SendMessagesAsync，多播沿專用 MultiCastMessagesAsync；
 * 舊 RichMenu 的 create／upload／link／notify 與 query／unlink／delete 保持次序，圖片串流
 * 在 upload 範圍由本流程 using 擁有；不自行解析 token、建立 HTTP、序列化 body 或加 key/sender，讓黃金保持
 * 相同 URL、遮罩標頭與本文。失敗不轉型、不重試，交原產品相容殼決定 best-effort 或重拋。
 * 使用邊界與維護：只借用 processor 與其 SDK client，沒有額外 IDisposable、queue、cache、
 * 計時器或背景作業；Host/呼叫端是有限 timeout、頻道及 client 的唯一 owner。傳入參數只
 * 屬於該次作業，不存到欄位或與其他使用者共享；呼叫者須完成伺服器授權並等待排空。
 * 多播／產品樣板／舊同步別名不能被用作略過新正式會友流程的安全檢查。
 */
using Line.Messaging;
using System.IO;

namespace LineMessagingProcessor.Workflows;

/// <summary>
/// 僅承接已存在的 arbitrary SDK 相容送訊，不讀取產品 CRM 或自行建立授權值；
/// 一個實例限定借用同一頻道 processor，只有每次方法的區域參數帶入收件人資料。
/// </summary>
public sealed class LineLegacySendWorkflow
{
    private readonly LineMessagingProcessorClass processor;

    /// <summary>借用既有 processor；不取得 client 所有權，也不建立隱藏的傳輸或通知資源。</summary>
    /// <param name="processor">原頻道 owner 已建立的 net10 processor，不可跨頻道重綁。</param>
    public LineLegacySendWorkflow(LineMessagingProcessorClass processor) =>
        this.processor = processor ?? throw new ArgumentNullException(nameof(processor));

    /// <summary>保留 legacy 單次 push；不改訊息、不加 sender/key，原 SDK 例外由產品處理。</summary>
    /// <param name="userId">產品已授權的當次收件 ID。</param>
    /// <param name="messages">操作限定的 SDK 訊息集合。</param>
    /// <returns>原單次傳輸，不建立遺留背景工作。</returns>
    public Task SendAsync(string userId, IList<ISendMessage> messages) => processor.SendMessagesAsync(userId, messages);

    /// <summary>保持 multicast HTTP 與完整收件序列，不在相容流程偷偷拆包或按顯示值去重。</summary>
    /// <param name="userIds">操作限定、產品先授權的收件集合。</param>
    /// <param name="messages">傳給同次 SDK serializer 的訊息集合。</param>
    /// <returns>完成或失敗即排空的唯一作業。</returns>
    public Task MultiCastAsync(IList<string> userIds, IList<ISendMessage> messages) => processor.MultiCastMessagesAsync(userIds, messages);

    /// <summary>
    /// 保留 ToolUtility 主方案已有的 create/upload/link/notify 次序，不重建 HTTP。
    /// 圖片來源在 create 後才開啟，串流由本流程唯一擁有，upload 成功或失敗均 Dispose。
    /// 此入口不是 ChurchReport catalog/assignment，不得取代正式會友授權或版本管理。
    /// </summary>
    /// <param name="userId">原產品已授權的當次收件人。</param>
    /// <param name="menu">產品原有選單定義，共用層不加入文案。</param>
    /// <param name="imageSource">產生有限長度、可讀且操作限定串流的來源；所有權移交本流程，不得回傳共享串流。</param>
    /// <param name="notification">原有成功通知訊息，不加 sender/key。</param>
    /// <returns>四步完成的作業；原失敗保持可見，不另補 retry 或刪除 provider 的業務決策。</returns>
    public async Task CreateAndAssignLegacyRichMenuAsync(
        string userId, RichMenu menu, Func<Stream> imageSource, IList<ISendMessage> notification)
    {
        ArgumentNullException.ThrowIfNull(imageSource);
        var id = await processor.CreateRichMenuAsync(menu).ConfigureAwait(false);
        using (var image = imageSource())
        {
            await processor.UploadRichMenuPngImageAsync(id, image).ConfigureAwait(false);
        }
        await processor.LinkRichMenuToUserAsync(userId, id).ConfigureAwait(false);
        await processor.SendMessagesAsync(userId, notification).ConfigureAwait(false);
    }

    /// <summary>
    /// 保留主方案 ToolUtility 的 query→unlink→delete 次序與失敗傳遞。
    /// 此舊入口仍假設選單獨佔，不是共用選單刪除策略；不得用於 ChurchReport assignment
    /// 管理的共用 menu，是否呼叫須由原產品 owner 確認。
    /// </summary>
    /// <param name="userId">原產品已驗證且確認選單獨佔的使用者。</param>
    /// <returns>全部完成才結束，不額外重試或背景清理。</returns>
    public async Task DeleteAssignedLegacyRichMenuAsync(string userId)
    {
        var id = await processor.GetRichMenuIdOfUserAsync(userId).ConfigureAwait(false);
        await processor.UnlinkRichMenuFromUserAsync(userId).ConfigureAwait(false);
        await processor.DeleteRichMenuAsync(id).ConfigureAwait(false);
    }
}

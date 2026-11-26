/*
檔案整體說明
目的與定位：共用 LIFF 模組的目前租戶選擇索引契約，提供產品、Messaging Channel 與已驗證 LINE 主體對應到 ContactId 的純 ID 儲存。
主要功能與流程：產品 resolver 讀取索引後重新驗證 Contact 的有效租戶關係；切換端點在寫入之前重新驗本人 Contact、Channel 與租戶。未設定選擇回 null，儲存失敗向上拋出，不能把故障誤認成未選擇。
使用邊界與維護：介面不代表授權，也不保存姓名、Token、Session 或 HttpContext。每次呼叫只借用有界純值與 caller token；實作負責連線及交易排空，最終 HTTP 邊界負責共用 diagnostics 通知，資料在資料庫保留至更新或由部署維運刪除。
*/
namespace SpeechMessage.Liff.AspNetCore;

/// <summary>保存完整已驗證主體鍵的目前 Contact 選擇；呼叫者仍須在每次使用與寫入之前重新授權。</summary>
public interface ICurrentTenantSelectionStore
{
    /// <summary>讀取完整三段鍵的 ContactId；只有成功查詢且沒有資料列時才回 null，取消及 SQL 故障原樣向上傳播。</summary>
    /// <param name="product">部署確定的產品識別，長度一至 128，不能含空白或控制字元。</param>
    /// <param name="channelKey">伺服器驗證的 Messaging Channel 識別，採區分大小寫的完整比對。</param>
    /// <param name="lineUserId">經驗證的 LINE 主體，採區分大小寫的完整比對。</param>
    /// <param name="ct">呼叫端取消來源；實作必須等待已啟動 I/O 排空後才完成。</param>
    /// <returns>不持有任何登入狀態的 ContactId 純值或沒有選擇。</returns>
    Task<Guid?> GetAsync(string product, string channelKey, string lineUserId, CancellationToken ct);

    /// <summary>在資料庫唯一鍵及交易內替換目前選擇；成功提交不能因晚到取消被改報為尚未寫入。</summary>
    /// <param name="product">部署確定的產品識別，不得由任意路由值取代。</param>
    /// <param name="channelKey">伺服器重新驗證的 Messaging Channel 識別。</param>
    /// <param name="lineUserId">已驗證且與目標 Contact 相同的 LINE 主體。</param>
    /// <param name="contactId">呼叫者重新授權後的非空 Contact 資料庫 ID；本介面不執行該授權。</param>
    /// <param name="ct">呼叫端取消來源，不得遺棄未排空的 SQL 交易。</param>
    /// <returns>確認提交後完成的工作。</returns>
    Task SetAsync(string product, string channelKey, string lineUserId, Guid contactId, CancellationToken ct);
}

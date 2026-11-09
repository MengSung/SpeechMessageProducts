/*
檔案整體說明
目的與定位：定義 P3-05 有界 queue 的兩階段接收契約，將 queue 容量與去重提交接在同一個明確交接點。此包不提供正式 Channel 或記憶體 queue。
主要功能與流程：先等待有界發布槽位，提交借用 registration，然後在關機保留規則內不可失敗地發布 envelope；只有 true 才代表 queue 已接管 immutable 事件。false、取消或 exception 不得留下已提交 claim 或已發布事件。
使用邊界與維護：預設容量一千、入列最多兩秒；不得在等待時持有其他 request 的狀態。提交後不能再觀察 caller 取消而撤回成功；停止需等待在途保留發布完成，再 drain 最多三十秒、取消餘項及 Dispose。無法滿足此原子性時必須先提出協調，不得以先 INSERT commit 再 WriteAsync 代替。
*/
namespace LineMessagingProcessor.AspNetCore.Webhooks;

/// <summary>只保存 detached 事件的有界 owner；接收期間不保存 HttpContext、request、signature 或 channel credential。</summary>
public interface IWebhookEventQueue
{
    /// <summary>
    /// 保留容量後才提交 registration，提交後必須發布且回 true。回 false 或拋錯時 registration 仍未提交，
    /// 由 endpoint Dispose rollback；queue 不得保存 registration 參照。成功重送依 SQL 已提交記錄去重。
    /// 此契約不提供程序崩潰的 durable outbox 保證；如需此保證須另核定，不可暗增 A.8 欄位。
    /// </summary>
    /// <param name="envelope">唯一移交 queue 的不可變事件；不得引用 HTTP 或 credential。</param>
    /// <param name="registration">只借用至方法返回的 claim，endpoint 仍是 Dispose owner。</param>
    /// <param name="cancellationToken">提交之前有效的 caller 取消；內部兩秒上限另行分類。</param>
    /// <returns>true 表示已提交且接管；false 表示預期容量／停止拒絕，允許重送。</returns>
    ValueTask<bool> TryEnqueueAsync(WebhookEnvelope envelope, IWebhookEventRegistration registration, CancellationToken cancellationToken);
}

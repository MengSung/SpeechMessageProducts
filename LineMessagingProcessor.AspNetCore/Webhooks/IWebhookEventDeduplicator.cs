/*
檔案整體說明
目的與定位：定義平台 SQL 去重與可回滾 registration lease，讓 queue 滿載或失敗不會永久吞掉事件。此檔只有契約，P3-04 實作 SQL。
主要功能與流程：TryRegisterAsync 對頻道及事件複合鍵取得獨佔 claim；已提交的重複回 null，新事件回 registration。queue 成功交接才 Commit；endpoint 以 await using 唯一持有 registration，最後 Dispose。
使用邊界與維護：A.8 僅 ChannelId、WebhookEventId、ReceivedUtc、IsRedelivery 四欄；可使用未提交 INSERT transaction，不可擅加 ownership 欄位。並行 claim 的未決／逾時不能當已完成重複；SQL 操作及取消後 rollback 均最多五秒，失敗拋原例外交主機。
*/
namespace LineMessagingProcessor.AspNetCore.Webhooks;

/// <summary>依已驗證 channel 與 provider 事件 ID 去重；正式實作須有跨程序唯一性與有界交易生命週期。</summary>
public interface IWebhookEventDeduplicator
{
    /// <summary>新 claim 只在本次交接期間保留；null 只代表已確定提交的重複，不能以 SQL 不確定狀態冒充重複。</summary>
    /// <param name="envelope">已驗簽且 detached 的單筆不可變事件。</param>
    /// <param name="cancellationToken">caller 取消，SQL 另有最多五秒的內部操作上限。</param>
    /// <returns>本次唯一擁有的 registration；已提交重複回 null，失敗拋原例外。</returns>
    ValueTask<IWebhookEventRegistration?> TryRegisterAsync(WebhookEnvelope envelope, CancellationToken cancellationToken);
}

/// <summary>
/// 一次事件交接的可回滾 claim；endpoint 唯一負責 Dispose。queue 只在呼叫期間借用，
/// 未提交的 Dispose 必須以獨立有界清理取消來源 rollback，不使用已取消的 HTTP token。
/// 已提交的 Dispose 只釋放交易／連線，不刪除已完成記錄；成功與失敗皆不可保留 envelope 或 credential。
/// </summary>
public interface IWebhookEventRegistration : IAsyncDisposable
{
    /// <summary>
    /// 僅 queue 取得不可撤銷的發布保留容量後呼叫；成功後 queue 必須完成發布且不可再因 caller 取消拒絕。
    /// 提交失敗／結果不確定時拋原例外並停止發布，SQL owner 必須能診斷該不確定狀態而非宣稱成功。
    /// </summary>
    /// <param name="cancellationToken">提交前的取消訊號；交易開始提交後須遵守正式 SQL 的確定性清理契約。</param>
    /// <returns>提交完成工作；之後 Dispose 不得回滾已完成事件。</returns>
    ValueTask CommitAsync(CancellationToken cancellationToken);
}

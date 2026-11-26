/*
檔案整體說明
目的與定位：F4跨產品排程服務、產品處理器及執行前重驗的公開契約，讓後續健康檢查與租戶清理只依賴中立純值。
主要功能與流程：同一工作鍵覆寫未執行候選；週期使用明確IANA時區；取消阻擋舊generation。每次工作建立獨立DI scope並先重查租戶，再呼叫同名handler。
使用邊界與維護：payload只能包含可重驗的有界JSON純值，不得放秘密、HttpContext或連線。handler必須以BusinessKey實作資料庫原子冪等；程序中斷後不明外部副作用不能由本介面宣稱恰好一次。
*/
namespace SpeechMessage.Scheduling;

/// <summary>同一部署產品的持久化排程入口；SQL故障向上傳播，不能以記憶體後備冒充成功。</summary>
public interface IJobScheduler
{
    /// <summary>覆寫相同鍵的尚未執行工作；scope不同時拒絕，不覆蓋其他租戶。</summary>
    Task ScheduleAsync(JobKey key, DateTimeOffset runAtUtc, JobScope scope, string payloadJson, CancellationToken ct);
    /// <summary>建立或更新明確時區的週期工作；錯過任意次僅補一次。</summary>
    Task ScheduleRecurringAsync(JobKey key, string cron, string timeZoneId, JobScope scope, string payloadJson, CancellationToken ct);
    /// <summary>在同一SQL工作鎖內取消目前版本，已完成副作用不能以取消回復。</summary>
    Task CancelAsync(JobKey key, CancellationToken ct);
}

/// <summary>產品註冊的單次scoped處理器；借用依賴不能逃離本次執行或保留到下一租戶。</summary>
public interface IScheduledJobHandler
{
    /// <summary>固定有界ASCII處理器名稱，與JobKey.Name相同。</summary>
    string Name { get; }
    /// <summary>重驗通過後執行；正常取消應排空借入工作，失敗保留真實Exception。</summary>
    Task ExecuteAsync(JobScope scope, string payloadJson, CancellationToken ct);
}

/// <summary>每次執行向權威來源重查scope；查詢故障必須拋出，不能假裝租戶已停用。</summary>
public interface IScheduledJobScopeValidator
{
    /// <summary>只在固定產品、組織與可執行租戶皆一致時回true；false跳過且不執行handler。</summary>
    Task<bool> CanExecuteAsync(JobScope scope, CancellationToken ct);
}

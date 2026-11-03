namespace LineMessagingProcessor.Core;

/// <summary>HTTP 與傳輸層結果分類；此階段只分類，不執行重試、業務拒絕或管理者通知。</summary>
public enum LinePushOutcome
{
    /// <summary>HTTP 2xx 表示平台接受本次請求。</summary>
    Success,
    /// <summary>HTTP 409 表示相同 Retry Key 已接受，呼叫端不得換鍵重送。</summary>
    AlreadyAccepted,
    /// <summary>429、5xx 或未取得回應的傳輸失敗，是否重試由外層決定。</summary>
    Retryable,
    /// <summary>不應盲目重試的 HTTP 錯誤，先修正憑證或輸入。</summary>
    Permanent,
    /// <summary>保留供階段五的業務拒絕；目前 Core 沒有會產生此值的路徑。</summary>
    Rejected
}

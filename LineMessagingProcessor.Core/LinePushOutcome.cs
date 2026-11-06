/*
 * 檔案整體說明
 *
 * 目的與定位：HTTP 與傳輸層結果分類；此階段只分類，不執行重試、業務拒絕或管理者通知。
 *
 * 主要功能與流程：
 * 本檔宣告 LinePushOutcome。
 * - HTTP 409 表示相同 Retry Key 已接受，呼叫端不得換鍵重送。
 * - 429、5xx 或未取得回應的傳輸失敗，是否重試由外層決定。
 * - 保留供階段五的業務拒絕；目前 Core 沒有會產生此值的路徑。
 * 資料／選項欄位包含 LinePushOutcome，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 LineMessagingProcessor.Core/LinePushClient.cs、LineMessagingProcessor.Core/LinePushResult.cs 追查目前工作樹中的合作／使用位置。
 */
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

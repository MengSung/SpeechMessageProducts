namespace LineMessagingProcessor.RichMenus;

/// <summary>
/// RichMenu workflows 回傳的標準化狀態值。
/// 這些值讓呼叫端不必直接依賴 LINE SDK exception 類型。
/// </summary>
public enum LineRichMenuStatus
{
    /// <summary>
    /// workflow 已成功完成。
    /// </summary>
    Succeeded,

    /// <summary>
    /// 在嘗試呼叫 LINE API 前，本機 request 驗證已失敗。
    /// </summary>
    ValidationFailed,

    /// <summary>
    /// LINE 以 provider response 拒絕 request，例如 payload 錯誤或授權失敗。
    /// </summary>
    ProviderRejected,

    /// <summary>
    /// LINE 或網路路徑無法使用、逾時，或在取得可信 provider response 前失敗。
    /// </summary>
    ProviderUnavailable,

    /// <summary>
    /// 已知 provider 或 validation 分類以外的非預期應用程式錯誤。
    /// </summary>
    UnexpectedError
}

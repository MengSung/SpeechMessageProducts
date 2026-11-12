/*
 * 檔案整體說明
 *
 * 目的與定位：定義共用金流回呼向產品查找已授權訂單的唯一掛勾。此 contract 位於 workflow 層，只交換中立付款狀態與產品提供的訂單快照，不參考 ASP.NET Core 或產品資料庫。
 * 主要流程：付款狀態已由 provider 查詢並驗簽後，回呼處理器呼叫 FindAsync；產品端依其伺服器端授權規則回傳 profile、幣別、訂單編號、結清狀態及付款後流程資料，或以 null 表示查無訂單。
 * 使用邊界與維護：實作端必須依可信伺服器資料確認訂單及 profile，不得以 callback 欄位授權；每次呼叫的結果不得跨使用者、教會或 request 共用。取消權杖由 HTTP request 傳入，查詢資源由產品 DI scope 擁有並於 request 結束釋放。此介面不執行寫回；AlreadySettled 是讀取快照而非原子認領。產品 updater 與 notifier 必須以權威資料儲存對同一業務事件採持久化冪等，避免併發重送造成重複寫入或通知；若更新後通知可能失敗，通知重試也須由耐久 outbox／等價機制承接，不能依賴此讀取結果或共用記憶體鎖。失敗由呼叫端保留原例外並交共用診斷，契約由 Payments callback host 測試驗證。
 */
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Workflows.Callbacks;

/// <summary>
/// 由產品端依伺服器授權資料查找付款訂單；此掛勾不執行收款寫回或付款者通知。
/// </summary>
public interface IPaymentCallbackOrderLookup
{
    /// <summary>
    /// 依已查詢的 provider 付款結果尋找唯一產品訂單；產品必須自行驗證資料範圍並回傳 request 專屬快照。
    /// </summary>
    /// <param name="status">已經 provider 查詢及驗簽的付款狀態，不可將 callback 內文視為付款成功證明。</param>
    /// <param name="cancellationToken">呼叫端 request 的取消權杖，需傳遞至產品資料存取。</param>
    /// <returns>授權訂單快照；找不到訂單時回傳 null。</returns>
    Task<PaymentCallbackOrder?> FindAsync(
        PaymentStatusResult status,
        CancellationToken cancellationToken);
}

/// <summary>
/// 產品 lookup 為單次回呼建立的訂單快照；共用處理器只讀取驗證所需欄位並把 Items 傳給既有付款後 workflow。
/// </summary>
public sealed record PaymentCallbackOrder
{
    /// <summary>由產品資料確認的伺服器端付款 profile 名稱。</summary>
    public string ProfileName { get; init; } = string.Empty;

    /// <summary>產品訂單採用的 ISO 4217 幣別代碼。</summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>此訂單允許由 provider 回報的訂單編號集合；空值不得與空白付款訂單號相符。</summary>
    public IReadOnlyList<string> OrderNumbers { get; init; } = Array.Empty<string>();

    /// <summary>產品資料讀取時指出此訂單已完成收款則為 true；此快照不是併發鎖，產品 updater／notifier 仍須以權威持久資料冪等。</summary>
    public bool AlreadySettled { get; init; }

    /// <summary>產品付款後 updater／notifier 所需的 request 專屬資料；共用層不解讀其型別或保存於跨 request 狀態。</summary>
    public IReadOnlyDictionary<string, object?> Items { get; init; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);
}

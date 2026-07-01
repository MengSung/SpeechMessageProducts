using System.Collections.Generic;

namespace ChurchReport.Payments;

/// <summary>
/// ChurchReport 奉獻付款回傳流程使用的產品層結果 DTO。
///
/// 這個 DTO 的位置很重要：
/// - 它不在 SpeechMessage.Payments，因為裡面包含 ChurchReport 產品流程會用到的欄位，例如
///   CRM fee id、奉獻類別、信用卡 token、付款組織代碼。
/// - 它也不是 provider protocol DTO，因為它不應該直接代表永豐、高鉅或台新的原始 API response。
/// - 它是「金流核心已經解析/查詢完成後，ChurchReport 後續流程需要的資料包」。
///
/// 你可以把這個類別理解成付款回傳流程的交接單：
/// 金流核心負責回答「這筆付款在 provider 那邊的狀態是什麼」；
/// ChurchReport workflow 則拿這張交接單去更新 CRM 收費單、產生畫面訊息、發 LINE 通知。
/// </summary>
public record DonationPaymentWorkflowResult
{
    /// <summary>
    /// 商店或合約代碼。
    /// 對不同 provider 來說名稱可能不同，但 ChurchReport workflow 只需要知道這是目前付款設定使用的商店識別。
    /// </summary>
    public string ShopNo { get; init; } = string.Empty;

    /// <summary>
    /// provider 回傳或查詢付款時使用的訂單參考值。
    /// 在舊永豐流程中常見名稱是 PayToken；在中性流程中它只是 provider order reference。
    /// </summary>
    public string PayToken { get; init; } = string.Empty;

    /// <summary>
    /// ChurchReport 端建立的產品訂單編號，例如奉獻收費單或定期定額設定所對應的訂單號。
    /// </summary>
    public string OrderNo { get; init; } = string.Empty;

    /// <summary>
    /// provider 端交易編號。
    /// 這個值通常用來在問題追蹤時和銀行或第三方金流客服對帳。
    /// </summary>
    public string ProviderTransactionId { get; init; } = string.Empty;

    /// <summary>
    /// 付款金額，使用一般 decimal 元單位，例如 1200 代表新台幣 1200 元。
    /// </summary>
    public decimal? Amount { get; init; }

    /// <summary>
    /// provider 需要或回傳的最小幣別單位字串。
    /// 例如某些 provider 會用 120000 代表 1200.00 元，ChurchReport 舊流程仍可能需要這種格式。
    /// </summary>
    public string AmountMinorUnits { get; init; } = string.Empty;

    /// <summary>
    /// ChurchReport 產品資料的 CRM entity id。
    /// 例如奉獻收費單 id 或定期定額奉獻設定 id，後續更新 CRM 時會用到。
    /// </summary>
    public string ProductEntityId { get; init; } = string.Empty;

    /// <summary>
    /// ChurchReport 付款組織代碼。
    /// 這是產品流程辨識資料，不是 provider protocol 名稱；舊設定 key 可以暫時保留，但程式內用中性名稱表達。
    /// </summary>
    public string PaymentOrganization { get; init; } = string.Empty;

    /// <summary>
    /// ChurchReport 產品層付款分類，例如 fee、dedication_booking、recurring_dedication。
    /// Workflow 會用它決定要更新一般收費單，還是定期定額奉獻設定。
    /// </summary>
    public string PaymentCategory { get; init; } = string.Empty;

    /// <summary>
    /// 付款方式代碼。
    /// 這個欄位只作為 ChurchReport 舊流程的判斷資料，不應被當成某一家 provider 的專屬 enum。
    /// </summary>
    public string PayType { get; init; } = string.Empty;

    /// <summary>
    /// ChurchReport workflow 使用的簡化狀態。
    /// 目前舊流程主要使用 S/F，代表成功或失敗；詳細 provider 狀態仍可從 Description 或 ProviderData 取得。
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// 可顯示或記錄的狀態描述。
    /// 這裡會放 provider message、錯誤訊息或付款狀態文字，讓 CRM/LINE/頁面能提供較清楚的說明。
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// 信用卡前段遮罩資訊。
    /// 只保存 provider 回傳且允許保存的部分，不保存完整卡號。
    /// </summary>
    public string LeftCCNo { get; init; } = string.Empty;

    /// <summary>
    /// 信用卡後段遮罩資訊。
    /// 通常用於讓奉獻者或同工辨識是哪張卡，但不能用來還原完整卡號。
    /// </summary>
    public string RightCCNo { get; init; } = string.Empty;

    /// <summary>
    /// 信用卡有效年月。
    /// 這是舊 ChurchReport 流程更新 CRM 可能需要的欄位，仍留在產品層 DTO。
    /// </summary>
    public string CCExpDate { get; init; } = string.Empty;

    /// <summary>
    /// 信用卡 token。
    /// token 是否可用、如何使用，取決於 provider 與 ChurchReport 的後續流程；這裡只負責傳遞結果。
    /// </summary>
    public string CCToken { get; init; } = string.Empty;

    /// <summary>
    /// provider 回傳後仍需要交給產品流程判斷的補充資料。
    ///
    /// 使用 dictionary 是為了避免 ChurchReport 產品層為每一家 provider 建立一堆只有一兩個欄位不同的 DTO。
    /// 但重要資料仍應該盡量投影到上方強型別屬性；ProviderData 只放補充或診斷欄位。
    /// </summary>
    public IReadOnlyDictionary<string, string> ProviderData { get; init; } = new Dictionary<string, string>();
}

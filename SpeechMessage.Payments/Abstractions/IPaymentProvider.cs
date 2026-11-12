/*
 * 檔案整體說明
 *
 * 目的與定位：單一金流 provider 的內部合約。此介面維持 internal，宿主產品只能經 Gateway 使用 provider-neutral API。
 *
 * 主要功能與流程：
 * 本檔宣告 IPaymentProvider。
 * 資料／選項欄位包含 IPaymentProvider，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * SupportedCurrencies 是 provider 能力白名單，Gateway 在呼叫前比對 Money.Currency；不支援時拒絕，不換匯。
 * profile 由 resolver 選定，只能於單次呼叫借用；provider 實作不得跨請求保存憑證、付款人或商店狀態。
 * cancellationToken 必須傳遞至非同步 I/O；typed HttpClient 與 provider 生命週期由 DI Host 管理。
 * 可沿 SpeechMessage.Payments.Tests/Gateway/PaymentGatewayTests.cs、
 * SpeechMessage.Payments.Tests/Providers/MyPay/MyPayProviderTests.cs、
 * SpeechMessage.Payments.Tests/Providers/Sinopac/SinopacProviderTests.cs、
 * SpeechMessage.Payments.Tests/Providers/Taishin/TaishinProviderTests.cs、
 * SpeechMessage.Payments/DependencyInjection/ServiceCollectionExtensions.cs 追查目前工作樹中的合作／使用位置。
 */
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Abstractions;

/// <summary>
/// 單一金流 provider 的內部合約。
/// 這個介面刻意維持 internal，避免宿主產品繞過
/// <see cref="IPaymentGateway"/> 直接呼叫 provider 實作，造成抽象邊界外漏。
/// </summary>
internal interface IPaymentProvider
{
    /// <summary>
    /// provider 可建立付款的 ISO 幣別白名單；集合由 provider 以唯讀方式提供，Gateway 在任何 provider I/O 前比對。
    /// 不支援的幣別不會自動換算，也不會藉由 profile 或 request 欄位略過此檢查。
    /// </summary>
    IReadOnlyCollection<string> SupportedCurrencies { get; }

    /// <summary>取得此實作處理的固定 provider 種類，供 Gateway 對應已解析的 profile。</summary>
    PaymentProviderKind ProviderKind { get; }

    /// <summary>
    /// 依已解析的單一商店 profile 建立付款；金額及幣別由 request.Total 提供，
    /// 呼叫端先由 Gateway 確認其幣別受此 provider 支援。
    /// </summary>
    /// <param name="profile">由 profile resolver 選出的商店設定，只供本次呼叫使用。</param>
    /// <param name="request">含訂單金額、付款方式及 callback 資料的中立請求。</param>
    /// <param name="cancellationToken">呼叫端取消權杖，provider 必須傳遞至非同步傳輸。</param>
    /// <returns>正規化的建立結果，不把 provider 協定物件交給產品層。</returns>
    Task<PaymentCreateResult> CreatePaymentAsync(
        PaymentMerchantProfile profile,
        PaymentCreateRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// 依已解析的商店 profile 查詢付款；不保存跨請求的訂單識別或 profile 狀態。
    /// </summary>
    /// <param name="profile">由 profile resolver 選出的商店設定，只供本次呼叫使用。</param>
    /// <param name="request">包含產品訂單號及 provider-neutral 交易參照的查詢請求。</param>
    /// <param name="cancellationToken">呼叫端取消權杖，provider 必須傳遞至非同步傳輸。</param>
    /// <returns>正規化的狀態與 Money 金額結果。</returns>
    Task<PaymentStatusResult> QueryPaymentAsync(
        PaymentMerchantProfile profile,
        PaymentQueryRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// 將已由宿主轉成中立型別的回呼欄位解析為 provider-neutral 結果；不更新產品資料或發送通知。
    /// </summary>
    /// <param name="profile">由 profile resolver 選出的商店設定，只供本次解析使用。</param>
    /// <param name="request">包含 HTTP 中立 body、query、form 與 header 投影的回呼資料。</param>
    /// <param name="cancellationToken">呼叫端取消權杖；若解析不執行 I/O，仍不得改為共享取消狀態。</param>
    /// <returns>正規化 callback 結果及 provider 所需 acknowledgement 描述。</returns>
    Task<PaymentCallbackResult> ParseCallbackAsync(
        PaymentMerchantProfile profile,
        PaymentCallbackRequest request,
        CancellationToken cancellationToken);
}

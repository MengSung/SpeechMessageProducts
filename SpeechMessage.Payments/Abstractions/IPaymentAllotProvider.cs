/*
 * 檔案整體說明
 *
 * 目的與定位：宣告僅供 Payments 核心內部實作使用的撥款查詢 provider 合約；宿主產品必須經 IPaymentAllotGateway 呼叫。
 *
 * 主要功能與流程：Gateway 將已由 resolver 選出的單一商店 profile、查詢日期及付款類型交給 provider；
 * provider 以 PaymentAllotQueryResult 回傳標準化批次與錯誤，禁止把銀行協定 DTO 跨越此內部邊界。
 *
 * 使用邊界與維護：傳入 profile 僅供本次操作使用，不得快取 credential 或把其欄位與其他 profile 混用。
 * CancellationToken 由呼叫端擁有且必須傳遞至非同步 I/O；nonce、HTTP request/response 及加解密暫存由 provider 在單次操作中釋放。
 * 此合約不擁有或保存外部資源；修改時以 provider 假 HTTP 測試驗證路由、錯誤分類及驗簽後發布。
 */
using SpeechMessage.Payments.Configuration;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Abstractions;

/// <summary>
/// 金流核心內部的撥款查詢 provider 合約，避免產品層直接呼叫銀行協定實作。
/// </summary>
internal interface IPaymentAllotProvider
{
    /// <summary>取得此實作支援的固定 provider 種類，供 gateway 與已解析的 profile 比對。</summary>
    PaymentProviderKind ProviderKind { get; }

    /// <summary>
    /// 依單次 profile 輸入查詢撥款批次；憑證僅可在此操作中使用，回應須在通過簽章驗證後才可加入結果。
    /// </summary>
    /// <param name="profile">由核心 resolver 選定的單一商店 profile，不由呼叫端 payload 自行構造或授權。</param>
    /// <param name="request">中立的撥款查詢日期與付款類型。</param>
    /// <param name="cancellationToken">呼叫端取消權杖，必須傳遞至 nonce 及訂單 HTTP I/O。</param>
    /// <returns>provider-neutral 批次結果；失敗以 PaymentError 表示，不回傳未驗簽資料。</returns>
    Task<PaymentAllotQueryResult> QueryAllotAsync(
        PaymentMerchantProfile profile,
        PaymentAllotQueryRequest request,
        CancellationToken cancellationToken);
}

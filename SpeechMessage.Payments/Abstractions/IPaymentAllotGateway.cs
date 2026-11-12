/*
 * 檔案整體說明
 *
 * 目的與定位：定義產品層查詢永豐撥款資料時使用的 provider-neutral gateway 合約，與一般付款狀態查詢分開，避免產品依賴永豐封包。
 *
 * 主要功能與流程：呼叫端提供具名 profile、日期區間及核定的付款類型；gateway 解析 server configuration 中的 profile，
 * 將請求委派給支援撥款查詢的 provider，並回傳含批次、Money 金額與標準錯誤分類的純值結果。
 *
 * 使用邊界與維護：ProfileName 只選擇已設定 profile，並非授權或憑證來源；request、憑證及回應不由介面保存。
 * 呼叫端取消權杖必須傳遞至 provider I/O；設定錯誤、provider 拒絕、網路失敗、反序列化失敗及簽章錯誤由 PaymentError 分類。
 * 本介面不擁有連線或背景資源；傳輸資源由 DI 建立的 provider 與 Host 管理，變更契約須以 gateway/provider 測試驗證。
 */
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Abstractions;

/// <summary>
/// 金流核心對外的撥款查詢入口。
/// 產品只傳入已選定的設定檔名稱與查詢範圍，不能直接依賴銀行 DTO、憑證欄位或加解密協定。
/// </summary>
public interface IPaymentAllotGateway
{
    /// <summary>
    /// 依具名設定檔查詢指定日期及付款類型的撥款批次；profile 由核心 resolver 解析，request 不授權商店或租戶。
    /// 呼叫端取消權杖會一路傳遞至 provider 的 nonce 與訂單傳輸。
    /// </summary>
    /// <param name="request">包含 profile 名稱、起訖日期與 C/M/A 類型的中立查詢資料。</param>
    /// <param name="cancellationToken">呼叫端取消權杖，不由 gateway 保存或替換。</param>
    /// <returns>經 provider 簽章驗證後發布的撥款批次與標準錯誤分類。</returns>
    Task<PaymentAllotQueryResult> QueryAllotAsync(
        PaymentAllotQueryRequest request,
        CancellationToken cancellationToken = default);
}

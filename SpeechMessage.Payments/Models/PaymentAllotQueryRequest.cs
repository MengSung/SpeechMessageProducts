/*
 * 檔案整體說明
 *
 * 目的與定位：保存一次撥款查詢所需的 provider-neutral 輸入，讓產品層不必引用永豐的 QryAllotReq 或字串日期協定。
 *
 * 主要功能與流程：ProfileName 指向已存在的 Payment 設定檔；FromDate、ToDate 使用純日曆日期；PaymentType 僅接受 C、M 或 A。
 * SinopacRequestMapper 會將日期轉為 yyyyMMdd，並映射為銀行規定的 AllotDateS、AllotDateE、PayType 欄位。
 *
 * 使用邊界與維護：ProfileName 只用於 server-side 設定查找，不承載憑證或租戶授權；本模型不保存呼叫者或共享 mutable state。
 * 日期範圍及付款類型錯誤須在傳輸前拒絕；本模型沒有 I/O、快取或需 Dispose 的資源，契約由 mapper 與 provider 測試驗證。
 */
namespace SpeechMessage.Payments.Models;

/// <summary>
/// 一次撥款查詢的 provider-neutral 請求。
/// 具名 profile 只選取伺服器端設定；日期為純日曆日，不帶時區或瞬間語意。
/// </summary>
public sealed record PaymentAllotQueryRequest
{
    /// <summary>要由 server-side resolver 解析的 profile 名稱，不是憑證或授權依據。</summary>
    public string ProfileName { get; init; } = string.Empty;

    /// <summary>撥款查詢起日，轉成 provider 要求的 yyyyMMdd 格式。</summary>
    public DateOnly FromDate { get; init; }

    /// <summary>撥款查詢迄日，轉成 provider 要求的 yyyyMMdd 格式。</summary>
    public DateOnly ToDate { get; init; }

    /// <summary>付款類型，限 C（信用卡）、M（行動支付）或 A（ATM）。</summary>
    public string PaymentType { get; init; } = string.Empty;
}

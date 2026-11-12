/*
 * 檔案整體說明
 *
 * 目的與定位：呈現單次 provider 撥款查詢的中立結果，不讓銀行原始 response DTO 離開 Payments provider 邊界。
 *
 * 主要功能與流程：Error 表示標準錯誤分類；ProviderStatus 與 ProviderDescription 保留已通過回應簽章驗證的 provider 狀態文字；
 * Batches 僅在回應成功且完成驗簽後填入，批次明細金額已換成主要單位的 TWD Money。
 *
 * 使用邊界與維護：未驗簽、無法解密或不可讀回應不得發布批次；失敗時 Batches 維持空集合。
 * provider 建立結果時提供唯讀集合，不保存 request、profile、憑證、response envelope 或連線；模型自身沒有 I/O 與 Dispose 責任。
 * 呼叫端不得將 provider description 當作授權資料或寫入敏感診斷；測試以假 HTTP 回應驗證其成功與失敗分支。
 */
namespace SpeechMessage.Payments.Models;

/// <summary>
/// 一次撥款查詢的標準化結果；批次資料只有在 provider response 完成簽章驗證後才會發布。
/// </summary>
public sealed record PaymentAllotQueryResult
{
    /// <summary>撥款查詢標準錯誤；成功時為 <see cref="PaymentError.None"/>。</summary>
    public PaymentError Error { get; init; } = PaymentError.None;

    /// <summary>已驗簽 provider 回應中的狀態碼；回應無法驗證時為空字串。</summary>
    public string ProviderStatus { get; init; } = string.Empty;

    /// <summary>已驗簽 provider 回應中的說明；不得將其視為授權或未清理的診斷文字。</summary>
    public string ProviderDescription { get; init; } = string.Empty;

    /// <summary>已驗簽成功後映射的撥款批次；錯誤結果必須是空集合。</summary>
    public IReadOnlyList<PaymentAllotBatch> Batches { get; init; } = Array.Empty<PaymentAllotBatch>();
}

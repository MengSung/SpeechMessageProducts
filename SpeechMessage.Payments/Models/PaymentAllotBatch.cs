/*
 * 檔案整體說明
 *
 * 目的與定位：描述一筆已驗簽的撥款批次及其 provider-neutral 明細，供 reconciliation 規則讀取。
 *
 * 主要功能與流程：AllotNo 是 provider 撥款識別；AllotDate 是純日曆日期；Items 是該批次各訂單的金額與手續費。
 * provider 由已簽章的 Sinopac response 建構此模型，將明細集合複製並以唯讀包裝後交給呼叫端。
 *
 * 使用邊界與維護：批次僅可從 PaymentAllotQueryResult 的成功結果消費，不能以未驗簽 DTO 建立正式 reconciliation 輸入。
 * 本模型不持有 profile、credential、HTTP 資源或可變 response 物件；無資料時由查詢結果回傳空批次集合，無需 Dispose。
 */
namespace SpeechMessage.Payments.Models;

/// <summary>
/// 一筆 provider 撥款批次及其訂單明細。
/// </summary>
public sealed record PaymentAllotBatch
{
    /// <summary>provider 指派的批次編號，不作為商店或教會授權依據。</summary>
    public string AllotNo { get; init; } = string.Empty;

    /// <summary>批次的純日曆撥款日期，不含時區轉換。</summary>
    public DateOnly AllotDate { get; init; }

    /// <summary>此批次已驗簽的訂單明細；provider 輸出時以唯讀集合隔離其內部暫存。</summary>
    public IReadOnlyList<PaymentAllotItem> Items { get; init; } = Array.Empty<PaymentAllotItem>();
}

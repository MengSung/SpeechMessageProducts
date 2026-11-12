/*
 * 檔案整體說明
 *
 * 目的與定位：提供撥款批次內單一訂單明細的中立純值模型，避免將永豐 OrderNO 或 decimal minor-unit 值直接交給產品規則。
 *
 * 主要功能與流程：OrderNo 保留 provider 訂單識別；Amount 與 Fee 均為 TWD Money，建立來源是永豐數值分值除以 100m，
 * 不經浮點數、格式化字串、文化設定或額外捨入。
 *
 * 使用邊界與維護：此模型不執行訂單比對、授權或資料回寫；只有已驗簽的批次結果可作為正式輸入。
 * 它不保留 profile、付款人或 mutable response state，也不持有任何外部資源；金額幣別由 Money 驗證器固定為 TWD。
 */
using SpeechMessage.Localization;

namespace SpeechMessage.Payments.Models;

/// <summary>
/// 一筆已驗簽的撥款訂單明細，金額以主要貨幣單位的 TWD Money 表示。
/// </summary>
public sealed record PaymentAllotItem
{
    /// <summary>provider 回報的訂單識別，消費端仍須按產品規則比對而不得推定授權。</summary>
    public string OrderNo { get; init; } = string.Empty;

    /// <summary>由 provider 整數分值除以 100m 建立的 TWD 金額，保留 decimal 精度。</summary>
    public Money Amount { get; init; } = new(0m, "TWD");

    /// <summary>由 provider 整數分值除以 100m 建立的 TWD 手續費，保留 decimal 精度。</summary>
    public Money Fee { get; init; } = new(0m, "TWD");
}

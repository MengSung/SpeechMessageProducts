/*
 * 檔案整體說明
 *
 * 目的與定位：建立付款商品明細，以 Money 保存單價與 ISO 幣別。MyPay 等 provider 需要 items 陣列；宿主產品未提供時 adapter 會補一筆預設明細。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentLineItem。
 * 資料／選項欄位包含 PaymentLineItem、Name、Quantity、Price（Money），名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * Money 是純值，不保存使用者、租戶或外部資源；舊 UnitPrice／Currency 僅為 Obsolete 轉接，不換匯、不捨入。
 * 修改欄位時須核對訂單草稿 mapper 與 provider mapper；此模型不執行授權、I/O 或資源管理。
 * 可沿 SpeechMessage.Payments.Tests/Providers/MyPay/MyPayProviderTests.cs、
 * SpeechMessage.Payments/Models/PaymentCreateRequest.cs 追查目前工作樹中的合作／使用位置。
 */
using SpeechMessage.Localization;

namespace SpeechMessage.Payments.Models;

/// <summary>
/// 建立付款的商品明細。
/// MyPay 等 provider 需要 items 陣列；宿主產品未提供時 adapter 會補一筆預設明細。
/// </summary>
public sealed record PaymentLineItem
{
    private Money price = new(0m, "TWD");

    /// <summary>顯示於付款明細的商品名稱，不是商品或資料列的穩定識別。</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>商品數量；provider mapper 依協定轉換，模型本身不重新計算總額。</summary>
    public int Quantity { get; init; }

    /// <summary>商品單價及其 ISO 幣別；完整保存 decimal 金額。</summary>
    public Money Price
    {
        get => price;
        init => price = value;
    }

    /// <summary>相容既有呼叫端的單價轉接；新程式應設定 <see cref="Price"/>。</summary>
    [Obsolete("Use Price instead.")]
    public decimal UnitPrice
    {
        get => price.Amount;
        init => price = new Money(value, price.Currency);
    }

    /// <summary>相容既有呼叫端的幣別轉接；新程式應設定 <see cref="Price"/>。</summary>
    [Obsolete("Use Price instead.")]
    public string Currency
    {
        get => price.Currency;
        init => price = new Money(price.Amount, value);
    }
}

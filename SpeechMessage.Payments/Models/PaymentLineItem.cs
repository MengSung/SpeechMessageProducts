namespace SpeechMessage.Payments.Models;

/// <summary>
/// 建立付款的商品明細。
/// MyPay 等 provider 需要 items 陣列；宿主產品未提供時 adapter 會補一筆預設明細。
/// </summary>
public sealed record PaymentLineItem
{
    public string Name { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public string Currency { get; init; } = "TWD";
}

namespace SpeechMessage.Payments.Models;

/// <summary>
/// 建立付款時可提供給 provider 的付款人資訊。
/// 僅保存 provider 建單必要的基本資料，不承載宿主產品 contact/entity。
/// </summary>
public sealed record PaymentCustomer
{
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
}

namespace SpeechMessage.Payments.Configuration;

/// <summary>
/// 對應 appsettings.json 的 Payment 區塊。
/// DefaultProfile 提供預設商店設定，Profiles 允許同一個產品同時配置
/// 永豐、高鉅、台新或多組不同組織的商店資料。
/// </summary>
public sealed class PaymentOptions
{
    public string DefaultProfile { get; set; } = string.Empty;
    public Dictionary<string, PaymentMerchantProfile> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

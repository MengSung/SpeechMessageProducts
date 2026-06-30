using SpeechMessage.Payments.Configuration;

namespace SpeechMessage.Payments.Abstractions;

/// <summary>
/// 依 profile name 解析具體商店設定。
/// Profile 是「產品/組織選擇哪一家金流與哪組憑證」的邊界，
/// provider 實作只接收解析後的設定，不自行讀取宿主產品的設定來源。
/// </summary>
public interface IPaymentProfileResolver
{
    PaymentMerchantProfile Resolve(string? profileName);
}

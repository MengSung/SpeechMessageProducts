/*
 * 檔案整體說明
 *
 * 目的與定位：單一金流商店 profile。 Credentials 放金流憑證，Endpoints 放 API 網址，Settings 放 provider 非敏感選項。 這個模型刻意不包含宿主產品的 CRM、通知、
 * 奉獻類別或資料庫資訊。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentMerchantProfile。
 * 資料／選項欄位包含 PaymentMerchantProfile、Name、Provider、Environment、Credentials、Endpoints、Settings，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Payments.Tests/Gateway/PaymentGatewayTests.cs、
 * SpeechMessage.Payments.Tests/Providers/MyPay/MyPayProviderTests.cs、
 * SpeechMessage.Payments.Tests/Providers/Sinopac/SinopacProviderTests.cs、
 * SpeechMessage.Payments.Tests/Providers/Taishin/TaishinProviderTests.cs、
 * SpeechMessage.Payments/Abstractions/IPaymentProfileResolver.cs 追查目前工作樹中的合作／使用位置。
 */
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Configuration;

/// <summary>
/// 單一金流商店 profile。
/// Credentials 放金流憑證，Endpoints 放 API 網址，Settings 放 provider 非敏感選項。
/// 這個模型刻意不包含宿主產品的 CRM、通知、奉獻類別或資料庫資訊。
/// </summary>
public sealed record PaymentMerchantProfile
{
    public string Name { get; set; } = string.Empty;
    public PaymentProviderKind Provider { get; set; } = PaymentProviderKind.Unknown;
    public PaymentEnvironment Environment { get; set; } = PaymentEnvironment.Sandbox;
    public Dictionary<string, string> Credentials { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Endpoints { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

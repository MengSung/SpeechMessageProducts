/*
 * 檔案整體說明
 *
 * 目的與定位：對應 appsettings.json 的 Payment 區塊。 DefaultProfile 提供預設商店設定，Profiles 允許同一個產品同時配置 永豐、高鉅、台新或多組不同組織的商店資料。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentOptions。
 * 資料／選項欄位包含 PaymentOptions、DefaultProfile、Profiles，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Payments.Tests/Configuration/PaymentOptionsTests.cs、
 * SpeechMessage.Payments/Configuration/OptionsPaymentProfileResolver.cs、
 * SpeechMessage.Payments/Configuration/PaymentOptionsValidator.cs、
 * SpeechMessage.Payments/DependencyInjection/ServiceCollectionExtensions.cs 追查目前工作樹中的合作／使用位置。
 */
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

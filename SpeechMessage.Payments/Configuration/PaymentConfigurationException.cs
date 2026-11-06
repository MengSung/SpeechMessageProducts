/*
 * 檔案整體說明
 *
 * 目的與定位：表示金流 profile 或 provider 設定不完整。 這類錯誤會被 gateway/provider 轉成 normalized PaymentError， 讓產品層可以顯示可診斷的失敗訊息，
 * 而不是拋出未處理例外。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentConfigurationException。
 * 功能由下方宣告及其呼叫端共同構成；沒有方法入口的契約／常數檔不自行執行業務作業。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Payments.Tests/Configuration/PaymentOptionsTests.cs、
 * SpeechMessage.Payments/Configuration/OptionsPaymentProfileResolver.cs、
 * SpeechMessage.Payments/Gateway/PaymentGateway.cs、SpeechMessage.Payments/Providers/MyPay/MyPayPaymentProvider.cs、
 * SpeechMessage.Payments/Providers/MyPay/MyPayRequestMapper.cs 追查目前工作樹中的合作／使用位置。
 */
namespace SpeechMessage.Payments.Configuration;

/// <summary>
/// 表示金流 profile 或 provider 設定不完整。
/// 這類錯誤會被 gateway/provider 轉成 normalized PaymentError，
/// 讓產品層可以顯示可診斷的失敗訊息，而不是拋出未處理例外。
/// </summary>
public sealed class PaymentConfigurationException : Exception
{
    public PaymentConfigurationException(string message)
        : base(message)
    {
    }
}

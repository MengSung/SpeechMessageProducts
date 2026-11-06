/*
 * 檔案整體說明
 *
 * 目的與定位：依 profile name 解析具體商店設定。 Profile 是「產品/組織選擇哪一家金流與哪組憑證」的邊界， provider 實作只接收解析後的設定，不自行讀取宿主產品的設定來源。
 *
 * 主要功能與流程：
 * 本檔宣告 IPaymentProfileResolver。
 * 資料／選項欄位包含 IPaymentProfileResolver，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Payments.Tests/Gateway/PaymentGatewayTests.cs、
 * SpeechMessage.Payments/Configuration/OptionsPaymentProfileResolver.cs、
 * SpeechMessage.Payments/DependencyInjection/ServiceCollectionExtensions.cs、
 * SpeechMessage.Payments/Gateway/PaymentGateway.cs 追查目前工作樹中的合作／使用位置。
 */
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

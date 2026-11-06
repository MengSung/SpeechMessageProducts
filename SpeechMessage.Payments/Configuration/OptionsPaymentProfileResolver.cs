/*
 * 檔案整體說明
 *
 * 目的與定位：從 DI options 解析 PaymentMerchantProfile。 若呼叫端沒有指定 profile，才使用 Payment:DefaultProfile； 找不到 profile 時以設定錯誤失敗，
 * 不再落回舊程式的硬編碼憑證表。
 *
 * 主要功能與流程：
 * 本檔宣告 OptionsPaymentProfileResolver。
 * 主要操作入口包含 Resolve。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Payments.Tests/Configuration/PaymentOptionsTests.cs、
 * SpeechMessage.Payments/DependencyInjection/ServiceCollectionExtensions.cs 追查目前工作樹中的合作／使用位置。
 */
using Microsoft.Extensions.Options;
using SpeechMessage.Payments.Abstractions;

namespace SpeechMessage.Payments.Configuration;

/// <summary>
/// 從 DI options 解析 PaymentMerchantProfile。
/// 若呼叫端沒有指定 profile，才使用 Payment:DefaultProfile；
/// 找不到 profile 時以設定錯誤失敗，不再落回舊程式的硬編碼憑證表。
/// </summary>
public sealed class OptionsPaymentProfileResolver : IPaymentProfileResolver
{
    private readonly IOptions<PaymentOptions> _options;

    public OptionsPaymentProfileResolver(IOptions<PaymentOptions> options)
    {
        _options = options;
    }

    public PaymentMerchantProfile Resolve(string? profileName)
    {
        var options = _options.Value;
        // profileName 是產品層傳入的選擇；空值時才使用 DefaultProfile，避免 provider 自行猜測商店。
        var resolvedName = string.IsNullOrWhiteSpace(profileName)
            ? options.DefaultProfile
            : profileName;

        if (string.IsNullOrWhiteSpace(resolvedName))
        {
            throw new PaymentConfigurationException("Payment profile was not specified and no default profile is configured.");
        }

        if (!options.Profiles.TryGetValue(resolvedName, out var profile))
        {
            throw new PaymentConfigurationException($"Payment profile '{resolvedName}' is not configured.");
        }

        return profile with { Name = resolvedName };
    }
}

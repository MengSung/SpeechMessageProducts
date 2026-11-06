/*
 * 檔案整體說明
 *
 * 目的與定位：啟動期檢查 Payment 設定的最低必要條件。 真正的 provider 憑證欄位仍由各 provider mapper 檢查，因為不同金流需要的欄位不同。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentOptionsValidator。
 * 主要操作入口包含 Validate。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Payments/DependencyInjection/ServiceCollectionExtensions.cs 追查目前工作樹中的合作／使用位置。
 */
using Microsoft.Extensions.Options;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Configuration;

/// <summary>
/// 啟動期檢查 Payment 設定的最低必要條件。
/// 真正的 provider 憑證欄位仍由各 provider mapper 檢查，因為不同金流需要的欄位不同。
/// </summary>
public sealed class PaymentOptionsValidator : IValidateOptions<PaymentOptions>
{
    public ValidateOptionsResult Validate(string? name, PaymentOptions options)
    {
        if (options.Profiles.Count == 0)
        {
            return ValidateOptionsResult.Fail("At least one payment profile is required.");
        }

        if (!string.IsNullOrWhiteSpace(options.DefaultProfile) &&
            !options.Profiles.ContainsKey(options.DefaultProfile))
        {
            return ValidateOptionsResult.Fail($"Default payment profile '{options.DefaultProfile}' is not configured.");
        }

        var invalidProfiles = options.Profiles
            .Where(profile => profile.Value.Provider == PaymentProviderKind.Unknown)
            .Select(profile => profile.Key)
            .ToArray();

        return invalidProfiles.Length == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"Payment profiles must specify a provider: {string.Join(", ", invalidProfiles)}.");
    }
}

/*
 * 檔案整體說明
 *
 * 目的與定位：在 Payment options 被消費前驗證具名 profile 的基本完整性；所有 provider 都須明確宣告類型，永豐另須具備協定必要設定。
 *
 * 主要功能與流程：
 * Validate 先確認 profile 集合與 DefaultProfile 指向有效 profile，再檢查各 profile 的 provider。
 * 對永豐只檢查必要鍵是否存在且非空白，接受 XKeyId／XKeyID 相容別名；錯誤只輸出 profile 名稱與缺少的鍵名，不讀回或記錄設定值。
 *
 * 使用邊界與維護：
 * 本 validator 不驗證憑證值真偽、金流端點連線或付款結果，也不修改 options；provider mapper 仍負責單次呼叫的最終欄位需求。
 * 設定物件由 options 管線提供並由該管線管理生命週期，本檔不保存 profile、建立資源或把值放入診斷。
 * 以 PaymentProfileTests 驗證 fail-closed、缺鍵回報與別名相容，並核對 ServiceCollectionExtensions 的 options 註冊。
 */
using Microsoft.Extensions.Options;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Configuration;

/// <summary>
/// 驗證具名付款設定檔的基本完整性，並針對永豐確認協定必要鍵存在且非空白。
/// 不驗證憑證真偽或連線；診斷只能包含 profile 名稱與缺少的鍵名，不包含設定值。
/// </summary>
public sealed class PaymentOptionsValidator : IValidateOptions<PaymentOptions>
{
    /// <summary>
    /// 驗證 profile 集合、預設名稱、provider 類型及永豐必要設定。
    /// profile 與設定字典可能由外部組態繫結，因此 null 或缺鍵均以安全的驗證失敗處理。
    /// </summary>
    /// <param name="name">Options 管線提供的選項名稱；本驗證器僅處理傳入的 Payment options。</param>
    /// <param name="options">本次驗證的付款選項；不保留或修改此物件。</param>
    /// <returns>完整時回傳成功；錯誤摘要不包含任何設定值。</returns>
    public ValidateOptionsResult Validate(string? name, PaymentOptions options)
    {
        if (options is null)
        {
            return ValidateOptionsResult.Fail("Payment options are unavailable.");
        }

        if (options.Profiles is null || options.Profiles.Count == 0)
        {
            return ValidateOptionsResult.Fail("At least one payment profile is required.");
        }

        if (!string.IsNullOrWhiteSpace(options.DefaultProfile) &&
            (!options.Profiles.TryGetValue(options.DefaultProfile, out var defaultProfile) || defaultProfile is null))
        {
            return ValidateOptionsResult.Fail($"Default payment profile '{options.DefaultProfile}' is not configured.");
        }

        var invalidProfiles = new List<string>();
        var missingSettings = new List<string>();
        foreach (var (profileName, profile) in options.Profiles)
        {
            if (profile is null)
            {
                invalidProfiles.Add(profileName);
                continue;
            }

            if (profile.Provider == PaymentProviderKind.Unknown)
            {
                invalidProfiles.Add(profileName);
                continue;
            }

            if (profile.Provider != PaymentProviderKind.Sinopac)
            {
                continue;
            }

            var missing = new List<string>();
            AddIfMissing(profile.Credentials, "ShopNo", missing);
            AddIfMissing(profile.Credentials, "A1", missing);
            AddIfMissing(profile.Credentials, "A2", missing);
            AddIfMissing(profile.Credentials, "B1", missing);
            AddIfMissing(profile.Credentials, "B2", missing);
            if (!HasValue(profile.Credentials, "XKeyId") && !HasValue(profile.Credentials, "XKeyID"))
            {
                missing.Add("XKeyId");
            }
            if (!HasValue(profile.Endpoints, "ApiBaseUrl"))
            {
                missing.Add("Endpoints:ApiBaseUrl");
            }

            if (missing.Count > 0)
            {
                missingSettings.Add($"Sinopac profile '{profileName}' is missing required settings: {string.Join(", ", missing)}.");
            }
        }

        if (invalidProfiles.Count > 0)
        {
            return ValidateOptionsResult.Fail($"Payment profiles must specify a provider: {string.Join(", ", invalidProfiles)}.");
        }

        return missingSettings.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(missingSettings);
    }

    /// <summary>只判斷指定設定鍵是否有非空白值，不回傳或格式化該值。</summary>
    private static bool HasValue(IDictionary<string, string>? values, string key) =>
        values is not null && values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);

    /// <summary>將缺失鍵名加入本次局部診斷候選；設定值不離開查詢運算。</summary>
    private static void AddIfMissing(IDictionary<string, string>? values, string key, ICollection<string> missing)
    {
        if (!HasValue(values, key))
        {
            missing.Add(key);
        }
    }
}

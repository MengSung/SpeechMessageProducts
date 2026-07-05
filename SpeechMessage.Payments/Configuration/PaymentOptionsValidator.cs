// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments/Configuration/PaymentOptionsValidator.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class PaymentOptionsValidator
// 主要成員：Validate
// 引用命名空間：Microsoft.Extensions.Options、SpeechMessage.Payments.Models
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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

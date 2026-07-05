// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：SpeechMessage.Payments/Configuration/OptionsPaymentProfileResolver.cs
// 所屬區塊：可重用付款核心、付款 provider、ASP.NET Core 整合、workflow 與測試模組。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class OptionsPaymentProfileResolver
// 主要成員：Resolve
// 引用命名空間：Microsoft.Extensions.Options、SpeechMessage.Payments.Abstractions
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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

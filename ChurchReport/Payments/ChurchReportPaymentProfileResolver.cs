using Microsoft.Extensions.Configuration;

namespace ChurchReport.Payments;

/// <summary>
/// ChurchReport 專用的金流 profile 選擇器。
/// <c>SpeechMessage.Payments</c> 只認得 named profile，不認得 ChurchReport 歷史設定鍵；
/// 因此 <c>PAY_PROVIDER</c> 到 <c>Payment:Profiles</c> 的轉換留在產品層，避免通用核心綁死 ChurchReport 設定格式。
/// </summary>
public sealed class ChurchReportPaymentProfileResolver
{
    private readonly IConfiguration _configuration;

    public ChurchReportPaymentProfileResolver(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string ResolveProfileName(string? requestedProfileName = null)
    {
        // 呼叫端明確指定 profile 時優先使用；這讓測試、單一路由或未來產品可以跳過 PAY_PROVIDER 預設值。
        if (!string.IsNullOrWhiteSpace(requestedProfileName))
        {
            return requestedProfileName;
        }

        // 這裡保留舊版 appsettings.json 的 PAY_PROVIDER 對應關係。
        // provider 名稱沿用 appsettings.json 的可選值；未來若要改設定文字，
        // 必須同步更新這裡的 mapping 與部署設定，避免 PAY_PROVIDER 對不到正確 profile。
        var providerProfile = _configuration["PAY_PROVIDER"] switch
        {
            "永豐金流" => "JesusTest",
            "高鉅金流" => "MyPayProduction",
            "台新金流" => "TaishinSandbox",
            _ => string.Empty
        };

        if (!string.IsNullOrWhiteSpace(providerProfile))
        {
            return providerProfile;
        }

        // 若舊 PAY_PROVIDER 沒有命中，改讀新核心的 Payment:DefaultProfile；
        // 最後退回 JesusTest 是為了維持舊永豐測試站預設行為。
        var defaultProfile = _configuration["Payment:DefaultProfile"];
        return !string.IsNullOrWhiteSpace(defaultProfile)
            ? defaultProfile
            : "JesusTest";
    }
}

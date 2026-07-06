// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Payments/ChurchReportPaymentProfileResolver.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於付款相關流程，註解重點在說明 provider 邊界、金流狀態、錯誤處理與不可改變的外部契約。
// 主要型別：class ChurchReportPaymentProfileResolver
// 主要成員：ResolveProfileName
// 引用命名空間：Microsoft.Extensions.Configuration
// 閱讀路徑：閱讀此檔案時應先確認金額、訂單編號、付款狀態、provider profile、callback acknowledgement 與錯誤訊息是否跨層保持一致。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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

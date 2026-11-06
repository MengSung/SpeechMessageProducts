/*
 * 檔案整體說明
 *
 * 目的與定位：ChurchReport 舊靜態設定入口的失敗邊界；不建立 client、不快取 token、不保留 HttpContext，所有解析委派 Tenancy。
 *
 * 主要功能與流程：
 * 本檔宣告 ChurchReportLineChannelAccess。
 * - 舊 Lazy 設定的建構也屬失敗邊界；只在本次呼叫執行 factory，不保存 delegate、設定或例外。
 * - 以已通過啟動安全檢查的 CRM 部署組織解析；未配置回空，真正設定故障交共用例外 owner 後維持舊空結果。
 * 主要操作入口包含 GetOptionalTokenFromFactory、GetOptionalToken。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/Contracts/TenancyCallSiteTests.cs、
 * ChurchReport.MemberInfo.Tests/LineSharedWorkflow/TenancyFailureBoundaryTests.cs、
 * SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs、
 * SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs、
 * SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using Microsoft.Extensions.Configuration;
using SpeechMessage.Tenancy;

namespace ChurchReport.Services;

/// <summary>ChurchReport 舊靜態設定入口的失敗邊界；不建立 client、不快取 token、不保留 HttpContext，所有解析委派 Tenancy。</summary>
public static class ChurchReportLineChannelAccess
{
    /// <summary>舊 Lazy 設定的建構也屬失敗邊界；只在本次呼叫執行 factory，不保存 delegate、設定或例外。</summary>
    /// <param name="configurationFactory">無 request 捕捉的靜態 factory；仍由原 host 擁有設定根及其 provider。</param>
    /// <returns>同既有可選入口的 raw token，真正建構／讀取故障上報原例外後回空。</returns>
    public static string GetOptionalTokenFromFactory(Func<IConfiguration> configurationFactory)
    {
        ArgumentNullException.ThrowIfNull(configurationFactory);
        try
        {
            return GetOptionalToken(configurationFactory());
        }
        catch (Exception exception)
        {
            ChurchReportLineAdminNotificationService.ReportException("ChurchReportLineChannelAccess.GetOptionalTokenFromFactory", exception);
            return string.Empty;
        }
    }

    /// <summary>以已通過啟動安全檢查的 CRM 部署組織解析；未配置回空，真正設定故障交共用例外 owner 後維持舊空結果。</summary>
    /// <param name="configuration">Host 擁有的部署設定；缺少設定時不啟動送訊。</param>
    /// <param name="resolver">已有 DI 範圍時借用的固定產品入口；舊靜態入口可省略，不保存或釋放它。</param>
    /// <returns>本次操作的 raw token，禁止寫入日誌或共享結果快取。</returns>
    public static string GetOptionalToken(IConfiguration? configuration, ILineChannelResolver? resolver = null)
    {
        if (configuration is null) return string.Empty;
        try
        {
            var organization = configuration["CrmConnection:Organization"];
            if (resolver is not null && !string.IsNullOrEmpty(organization))
                return resolver.TryResolve(new TenantId(organization, "ChurchReport"), out var channel)
                    ? channel.GetAccessTokenForLegacyClient() : string.Empty;
            return LegacyLineChannelAccess.GetOptionalAccessToken(configuration, organization);
        }
        catch (Exception exception)
        {
            // 原始故障只能交給共用 owner；不得再輸出 Message、token 或來源值，且不能自行建立告警 client。
            ChurchReportLineAdminNotificationService.ReportException("ChurchReportLineChannelAccess.GetOptionalToken", exception);
            return string.Empty;
        }
    }
}

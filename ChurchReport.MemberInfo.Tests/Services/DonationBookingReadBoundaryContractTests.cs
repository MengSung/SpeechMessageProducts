// ============================================================================
// 檔案路徑：ChurchReport.MemberInfo.Tests/Services/DonationBookingReadBoundaryContractTests.cs
// 檔案責任：鎖定 P7.4 認獻單 read boundary 的三種 host route 與 Embedded RequestGuard allowlist。
// 測試型態：只讀 repository source contract；不啟動 host、不建立 transport、不連接 CE，也不改變
//           feature gate、流量、週報或任何共享資料。
// ============================================================================

using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Services;

/// <summary>
/// 驗證 capability composition 不會因新增 typed read 而遺漏 Embedded route 或 operation allowlist。
/// 這些是 deployment composition 的靜態安全契約：若 route 缺失，Embedded + Data8 會在 runtime 以前
/// 失敗；若 allowlist 缺失，RequestGuard 會在任何 connector/admission 資源建立前拒絕合法 read。
/// </summary>
public sealed class DonationBookingReadBoundaryContractTests
{
    /// <summary>
    /// 驗證 factory 依 ConnectionMode 將認獻單 read 導向 Embedded host 或 Gateway host，且不以 Gateway-only
    /// 檢查錯誤地阻擋專案要求的 Embedded + Data8 產品模式。
    /// </summary>
    [Fact]
    public void Bootstrap_contains_all_three_supported_connection_mode_routes_for_dedication_booking_read()
    {
        var source = ReadBootstrapSource();

        source.Should().Contain("ConnectionMode.Embedded => processHost.GetOrCreateEmbeddedExecutor(productOptions, configuration)");
        source.Should().Contain("ConnectionMode.DedicatedGateway or ConnectionMode.CentralGateway =>");
        source.Should().Contain("Package01 dedication booking read requires a supported Dynamics connection mode.");
    }

    /// <summary>
    /// 驗證 Embedded host 的共用 RequestGuard allowlist 包含已登錄 operation。此檢查不呼叫 CE；它只防止
    /// composition code 建立看似可用的 Embedded executor，卻在 request guard 階段永遠拒絕合法 read。
    /// </summary>
    [Fact]
    public void Embedded_request_guard_allowlist_contains_the_dedication_booking_read_operation()
    {
        var source = ReadBootstrapSource();

        source.Should().Contain("OperationIds.PaymentsDedicationRetrieveByContact");
    }

    /// <summary>
    /// 讀取目前 test assembly 對應的 repository source；找不到檔案即 fail closed，而不是跳過契約驗證。
    /// 此 helper 只使用受測工作樹檔案，不列舉 CRM、端點、帳密、token 或使用者資料。
    /// </summary>
    private static string ReadBootstrapSource()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
             current is not null;
             current = current.Parent)
        {
            var path = Path.Combine(
                current.FullName,
                "SpeechMessageProducts.ChurchReport",
                "Services",
                "DonationDynamicsAccessBootstrap.cs");
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        throw new FileNotFoundException("The ChurchReport bootstrap source was not found.");
    }
}

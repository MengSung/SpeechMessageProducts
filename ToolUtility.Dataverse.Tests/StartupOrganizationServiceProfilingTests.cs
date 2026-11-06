/*
 * 檔案整體說明
 *
 * 目的與定位：保護 ChurchReport 組合根在 Debug 建構時，先裝飾 IOrganizationService 再交給 ToolUtilityClass 與其 facade 的服務圖契約。故障注入為假的 CRM 
 * 連線服務；測試只解析 scoped 服務，不呼叫 CRM，避免網路副作用，同時以決定性斷言確認 wrapper 型別、inner 非空以及同一 scope 的解析快取語意。
 *
 * 主要功能與流程：
 * 本檔宣告 StartupOrganizationServiceProfilingTests。
 * - 驗證 Startup.ConfigureServices 產生的 IOrganizationService 是 scoped 計時裝飾器， 且同一 scope 重複解析回傳同一 wrapper。測試不執行任何 CRM 
 * operation；若組合根仍 使用未裝飾的 GatewayOrganizationService，第一個型別斷言會以測試失敗而非編譯錯誤呈現。
 * 測試案例入口包含 ConfigureServices_registers_timed_organization_service_per_scope。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using Moq;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace ToolUtility.Dataverse.Tests;

/// <summary>
/// 保護 ChurchReport 組合根在 Debug 建構時，先裝飾 IOrganizationService 再交給
/// ToolUtilityClass 與其 facade 的服務圖契約。故障注入為假的 CRM 連線服務；測試只解析
/// scoped 服務，不呼叫 CRM，避免網路副作用，同時以決定性斷言確認 wrapper 型別、inner
/// 非空以及同一 scope 的解析快取語意。
/// </summary>
public sealed class StartupOrganizationServiceProfilingTests
{
    /// <summary>
    /// 驗證 Startup.ConfigureServices 產生的 IOrganizationService 是 scoped 計時裝飾器，
    /// 且同一 scope 重複解析回傳同一 wrapper。測試不執行任何 CRM operation；若組合根仍
    /// 使用未裝飾的 GatewayOrganizationService，第一個型別斷言會以測試失敗而非編譯錯誤呈現。
    /// </summary>
    [Fact]
    public void ConfigureServices_registers_timed_organization_service_per_scope()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Theme:Current"] = "藍色",
                ["ASPNETCORE_ENVIRONMENT"] = "Test",
                ["CrmConnection:ServerUrl"] = "https://org.test/XRMServices/2011/Organization.svc",
                ["CrmConnection:Username"] = "test-service",
                ["CrmConnection:Password"] = "test-secret",
                ["Dataverse:Pool:MinSize"] = "1",
                ["Dataverse:Pool:MaxN"] = "1",
                ["Dataverse:Pool:AcquireTimeout"] = "00:00:05",
                ["Dataverse:Pool:IdleTimeout"] = "00:01:00",
                ["Dataverse:Pool:HealthInterval"] = "00:01:00"
            })
            .Build();
        var diagnosticOptions = DiagnosticTraceOptions.Create(
            Environment.CurrentDirectory,
            enabled: false);
        var connectionService = new Mock<ICrmConnectionService>(MockBehavior.Strict);
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<ICrmConnectionService>(connectionService.Object);

        var startup = new ChurchReport.Startup(configuration, diagnosticOptions);
        startup.ConfigureServices(services);

        var descriptor = services.Last(service =>
            service.ServiceType == typeof(IOrganizationService));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var first = scope.ServiceProvider.GetRequiredService<IOrganizationService>();
        var second = scope.ServiceProvider.GetRequiredService<IOrganizationService>();
        var utility = scope.ServiceProvider.GetRequiredService<ToolUtilityClass>();

        Assert.IsType<ChurchReport.Diagnostics.Profiling.TimedOrganizationService>(first);
        var timed = (ChurchReport.Diagnostics.Profiling.TimedOrganizationService)first;
        Assert.NotNull(timed.Inner);
        Assert.Same(first, second);
        Assert.Same(first, utility.m_Crm2011OrganizationService);
    }
}
#endif

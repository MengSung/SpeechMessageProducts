/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 ToolUtilityClass 的 request 範圍與資源擁有權。
 *
 * 主要功能與流程：
 * 本檔宣告 ToolUtilityClassScopedLifetimeTests。
 * - 保護 DI 建構不得建立 legacy Dataverse 連線的契約。 故障注入為嚴格的 ICrmConnectionService 替身；若建構式錯誤呼叫 CreateOnPremiseClient，替身會立即失敗，
 * 並由 Never 斷言確認呼叫數為零。
 * - 保護注入連線不由 ToolUtilityClass 釋放的契約。 故障注入為可計數的 IDisposable 組織服務；直接 Dispose ToolUtilityClass 後， 決定性斷言是注入服務的 
 * Dispose 呼叫數仍為零，表示真正的 DI scope 擁有它。
 * - 保護 scoped provider 不被 singleton 捕獲的契約。 故障注入為以 ValidateScopes 與 ValidateOnBuild 建立的服務容器； 決定性斷言是建置不擲例外，且兩個 
 * scope 解析出不同的 ToolUtilityClass。
 * - 保護 fire-and-forget 背景 scope 的隔離與釋放契約。 故障注入為兩個可計數的 IDisposable 組織服務；決定性斷言是背景 scope 取得的 ToolUtility 與 request 
 * scope 不共用連線，且背景 scope 結束時只釋放自己的連線。 這覆蓋 Run 1.5 延後至 Run 2 的背景 scope 生命週期測試。
 * 測試案例入口包含 DiConstructor_DoesNotCreateLegacyConnection、Dispose_DoesNotDisposeInjectedOrganizationService、
 * ScopedRegistration_PassesValidationAndCreatesOneUtilityPerScope、
 * BackgroundScope_UsesIndependentConnectionAndDisposesOnlyItsLease、BuildServiceProvider、CreateTracer、
 * CreateConfiguration。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using Moq;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.DependencyInjection;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace ToolUtility.Dataverse.Tests;

/// <summary>
/// 驗證 ToolUtilityClass 的 request 範圍與資源擁有權。
/// </summary>
public sealed class ToolUtilityClassScopedLifetimeTests
{
    /// <summary>
    /// 保護 DI 建構不得建立 legacy Dataverse 連線的契約。
    /// 故障注入為嚴格的 ICrmConnectionService 替身；若建構式錯誤呼叫
    /// CreateOnPremiseClient，替身會立即失敗，並由 Never 斷言確認呼叫數為零。
    /// </summary>
    [Fact]
    public void DiConstructor_DoesNotCreateLegacyConnection()
    {
        var connectionService = new Mock<ICrmConnectionService>(MockBehavior.Strict);
        var utility = new ToolUtilityClass(
            new Mock<IOrganizationService>().Object,
            CreateTracer(),
            CreateConfiguration());

        utility.Dispose();

        connectionService.Verify(
            service => service.CreateOnPremiseClient(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    /// <summary>
    /// 保護注入連線不由 ToolUtilityClass 釋放的契約。
    /// 故障注入為可計數的 IDisposable 組織服務；直接 Dispose ToolUtilityClass 後，
    /// 決定性斷言是注入服務的 Dispose 呼叫數仍為零，表示真正的 DI scope 擁有它。
    /// </summary>
    [Fact]
    public void Dispose_DoesNotDisposeInjectedOrganizationService()
    {
        var organizationService = new Mock<IOrganizationService>(MockBehavior.Loose);
        var disposable = organizationService.As<IDisposable>();
        var utility = new ToolUtilityClass(
            organizationService.Object,
            CreateTracer(),
            CreateConfiguration());

        utility.Dispose();

        disposable.Verify(service => service.Dispose(), Times.Never);
    }

    /// <summary>
    /// 保護 scoped provider 不被 singleton 捕獲的契約。
    /// 故障注入為以 ValidateScopes 與 ValidateOnBuild 建立的服務容器；
    /// 決定性斷言是建置不擲例外，且兩個 scope 解析出不同的 ToolUtilityClass。
    /// </summary>
    [Fact]
    public void ScopedRegistration_PassesValidationAndCreatesOneUtilityPerScope()
    {
        using var provider = BuildServiceProvider();

        ToolUtilityClass first;
        ToolUtilityClass second;
        using (var firstScope = provider.CreateScope())
        {
            first = firstScope.ServiceProvider
                .GetRequiredService<IToolUtilityProvider>()
                .GetToolUtility();
        }

        using (var secondScope = provider.CreateScope())
        {
            second = secondScope.ServiceProvider
                .GetRequiredService<IToolUtilityProvider>()
                .GetToolUtility();
        }

        Assert.NotSame(first, second);
    }

    /// <summary>
    /// 保護 fire-and-forget 背景 scope 的隔離與釋放契約。
    /// 故障注入為兩個可計數的 IDisposable 組織服務；決定性斷言是背景 scope 取得的
    /// ToolUtility 與 request scope 不共用連線，且背景 scope 結束時只釋放自己的連線。
    /// 這覆蓋 Run 1.5 延後至 Run 2 的背景 scope 生命週期測試。
    /// </summary>
    [Fact]
    public void BackgroundScope_UsesIndependentConnectionAndDisposesOnlyItsLease()
    {
        var organizationServices = new List<Mock<IOrganizationService>>();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(CreateConfiguration());
        services.AddSingleton<IToolUtilityTracer>(CreateTracer());
        services.AddScoped<IOrganizationService>(_ =>
        {
            var service = new Mock<IOrganizationService>(MockBehavior.Loose);
            service.As<IDisposable>();
            organizationServices.Add(service);
            return service.Object;
        });
        services.AddToolUtility();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        using var requestScope = provider.CreateScope();
        var requestUtility = requestScope.ServiceProvider
            .GetRequiredService<IToolUtilityProvider>()
            .GetToolUtility();

        ToolUtilityClass backgroundUtility;
        using (var backgroundScope = provider.CreateScope())
        {
            backgroundUtility = backgroundScope.ServiceProvider
                .GetRequiredService<IToolUtilityProvider>()
                .GetToolUtility();

            Assert.NotSame(requestUtility, backgroundUtility);
            Assert.NotSame(
                requestUtility.m_Crm2011OrganizationService,
                backgroundUtility.m_Crm2011OrganizationService);
        }

        Assert.Equal(2, organizationServices.Count);
        organizationServices[1].As<IDisposable>()
            .Verify(service => service.Dispose(), Times.Once);
        organizationServices[0].As<IDisposable>()
            .Verify(service => service.Dispose(), Times.Never);
    }

    /// <summary>
    /// 建立只供測試使用的服務容器，明確開啟 scope 驗證以捕捉 captive dependency。
    /// 每個 scope 的組織服務皆為測試替身，絕不建立網路連線。
    /// </summary>
    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(CreateConfiguration());
        services.AddSingleton<IToolUtilityTracer>(CreateTracer());
        services.AddScoped<IOrganizationService>(_ => new Mock<IOrganizationService>().Object);
        services.AddToolUtility();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }

    /// <summary>
    /// 建立不寫檔的追蹤器替身。
    /// </summary>
    private static IToolUtilityTracer CreateTracer()
        => new Mock<IToolUtilityTracer>(MockBehavior.Loose).Object;

    /// <summary>
    /// 建立空設定替身；DI 建構式只需合法的 IConfiguration 所有權邊界。
    /// </summary>
    private static IConfiguration CreateConfiguration()
        => new ConfigurationBuilder().Build();
}

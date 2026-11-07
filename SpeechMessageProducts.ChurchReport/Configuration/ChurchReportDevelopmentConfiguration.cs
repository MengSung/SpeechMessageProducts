/*
檔案整體說明

目的與定位：保留 ChurchReport 既有 Development 設定入口與環境選擇規則；真正的
安全驗證及 provider 組合已移入 SpeechMessage.Hosting，不再保留第二套憑證或端點政策。

主要功能與流程：
ResolveEnvironmentName 維持 DOTNET→ASPNETCORE→Production 的純值選擇；無參數舊入口
只取得既有程序環境及主機組件，再把部署根、環境與 secrets 組件委派共用方法。
其餘三個入口一律委派 StartupGuards，保留 Development JSON／User Secrets 順序、
非 Development 的 LINE__ provider、指定測試組織與安全 HTTPS／token 來源檢查。

資源與隔離：
本檔不保存 IConfiguration、request、Session、教會、token 或解析結果，也不再讀基底檔、
複製 token 選擇或端點驗證。傳入 builder／root 仍由原 Host 擁有；共用 guard 的區域
stream／baseline root 在驗證結束即釋放。例外只含固定鍵名與原固定理由，不含設定值。
修改須執行既有 DevelopmentSafetyTests 與 Hosting 委派守門；不以離線結果替代實機。
*/
using System;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace ChurchReport.Configuration;

/// <summary>
/// 舊產品的無狀態相容入口；只決定既有環境名稱並委派 Hosting，不擁有 provider 或安全政策。
/// </summary>
internal static class ChurchReportDevelopmentConfiguration
{
    /// <summary>依 Host 原優先序選擇環境，不存取或修改程序環境。</summary>
    /// <param name="dotnetEnvironment">已取得的 DOTNET 名稱，null 才使用下一來源。</param>
    /// <param name="aspnetcoreEnvironment">已取得的 ASPNETCORE 名稱。</param>
    /// <returns>選定環境，皆未指定時為 Production。</returns>
    internal static string ResolveEnvironmentName(string dotnetEnvironment, string aspnetcoreEnvironment)
        => dotnetEnvironment ?? aspnetcoreEnvironment ?? "Production";

    /// <summary>讓既有靜態讀取點沿用部署目錄、Host 環境及相同 UserSecretsId，不建立第二個 owner。</summary>
    /// <param name="builder">原 caller 擁有的 builder，其原始 optional／reload 設定不變。</param>
    /// <returns>追加共用來源後的同一 builder。</returns>
    internal static IConfigurationBuilder AddChurchReportDevelopmentOverrides(this IConfigurationBuilder builder)
        => builder.AddChurchReportDevelopmentOverrides(Directory.GetCurrentDirectory(),
            ResolveEnvironmentName(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"), Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")),
            typeof(global::ChurchReport.Program).Assembly);

    /// <summary>只委派共用 Development 來源組合，provider 仍由 caller 建立的 root 擁有。</summary>
    /// <param name="builder">原組態建構器。</param>
    /// <param name="basePath">受信任部署根，不能由 request 決定。</param>
    /// <param name="environmentName">Host 選定的環境。</param>
    /// <param name="secretsAssembly">Host 或隔離測試的 UserSecretsId 組件。</param>
    /// <returns>原 builder，不替換或 Dispose 外部來源。</returns>
    internal static IConfigurationBuilder AddChurchReportDevelopmentOverrides(this IConfigurationBuilder builder,
        string basePath, string environmentName, Assembly secretsAssembly)
        => global::SpeechMessage.Hosting.StartupGuards.AddDevelopmentConfigurationOverrides(
            builder, basePath, environmentName, secretsAssembly);

    /// <summary>委派非 Development 的專用 LINE__ provider，Development 不追加正式來源。</summary>
    /// <param name="builder">原組態建構器。</param>
    /// <param name="environmentName">Host 選定的環境。</param>
    /// <returns>原 builder，生命週期仍由組態 root 管理。</returns>
    internal static IConfigurationBuilder AddChurchReportDeploymentLineEnvironmentOverrides(
        this IConfigurationBuilder builder, string environmentName)
        => global::SpeechMessage.Hosting.StartupGuards.AddDeploymentLineEnvironmentOverrides(builder, environmentName);

    /// <summary>委派共用 fail-closed 驗證；不重複讀值，不建立 CRM／LINE client 或通知。</summary>
    /// <param name="basePath">受信任的 Host content root。</param>
    /// <param name="environmentName">Host 選定的環境。</param>
    /// <param name="configuration">僅在驗證期間借用的合併組態。</param>
    internal static void ValidateDevelopmentConfiguration(string basePath, string environmentName, IConfiguration configuration)
        => global::SpeechMessage.Hosting.StartupGuards.ValidateDevelopmentConfiguration(basePath, environmentName, configuration);
}

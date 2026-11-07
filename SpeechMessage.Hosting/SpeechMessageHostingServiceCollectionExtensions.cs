/*
檔案整體說明

目的與定位：提供 Host 組合根註冊既有 ExceptionDiagnostics owner 與
ExceptionReportingAdapter 的唯一入口。此 registration 不複製 diagnostics 實作，也
不將 HttpContext、Session、tenant、credential 或 logger state 帶入 singleton。

主要功能與流程：
本檔宣告 AddSpeechMessageHosting 的兩個 overload。呼叫端可傳入已由組合根建立的
owner，或傳入受信任的部署目錄與啟動快照；服務容器只保存不可變 options、owner
與 adapter。adapter 解析時綁定程序級相容入口，容器關閉時先解除 registration，
再由既有 diagnostics owner drain／Dispose queue、sender、CTS、mutex 與檔案資源。

使用邊界與維護：
diagnosticsDirectory 只能來自部署組合根，不能由 request 或使用者輸入提供。外部
sender／HttpClient 不由本檔建立；產品 Host 仍負責建立、取消、drain 與 Dispose
其 sender，避免 Hosting 取得錯誤的 credential 所有權。修改服務註冊時須以真實 DI
container 驗證重入、重複 owner、關機順序與資源基線。
*/
using System;
using Microsoft.Extensions.DependencyInjection;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessage.Hosting;

/// <summary>集中註冊既有 diagnostics owner 與 Host 相容 adapter 的 extension methods。</summary>
public static class SpeechMessageHostingServiceCollectionExtensions
{
    /// <summary>
    /// 將組合根已建立的唯一 diagnostics owner 加入 DI，並註冊相容 adapter。
    /// </summary>
    /// <param name="services">Host 服務集合。</param>
    /// <param name="diagnostics">由組合根擁有的 diagnostics owner。</param>
    /// <param name="adapter">可選的既有 adapter；省略時由 DI 建立並負責解除。</param>
    /// <returns>同一個服務集合。</returns>
    public static IServiceCollection AddSpeechMessageHosting(
        this IServiceCollection services,
        ExceptionDiagnostics diagnostics,
        ExceptionReportingAdapter? adapter = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(diagnostics);
        services.AddSingleton(diagnostics);
        if (adapter is null)
            services.AddSingleton<ExceptionReportingAdapter>();
        else
            services.AddSingleton(adapter);
        return services;
    }

    /// <summary>
    /// 由受信任部署目錄與不可變輸出快照建立唯一 diagnostics owner，並註冊相容 adapter。
    /// </summary>
    /// <param name="services">Host 服務集合。</param>
    /// <param name="diagnosticsDirectory">部署者控制的診斷目錄，不可由 request 提供。</param>
    /// <param name="outputOptions">啟動時固定的兩個輸出開關。</param>
    /// <returns>同一個服務集合。</returns>
    public static IServiceCollection AddSpeechMessageHosting(
        this IServiceCollection services,
        string diagnosticsDirectory,
        ExceptionOutputOptions? outputOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticsDirectory);
        var options = outputOptions ?? new ExceptionOutputOptions();
        services.AddSingleton(options);
        services.AddSingleton<ExceptionDiagnostics>(_ =>
            new ExceptionDiagnostics(diagnosticsDirectory, outputOptions: options));
        services.AddSingleton<ExceptionReportingAdapter>();
        return services;
    }
}

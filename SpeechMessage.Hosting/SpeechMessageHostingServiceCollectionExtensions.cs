/*
檔案整體說明

目的與定位：提供 Host 組合根註冊 Hosting 內唯一 ExceptionDiagnostics owner 與
ExceptionReportingAdapter 的入口。相同啟動快照重入冪等，衝突在發布描述子前拒絕；也
不將 HttpContext、Session、tenant、credential 或 logger state 帶入 singleton。

主要功能與流程：
本檔宣告 AddSpeechMessageHosting 的兩個 overload。呼叫端可傳入已由組合根建立的
owner，或傳入受信任的部署目錄與啟動快照；服務容器只保存不可變 options、owner
與 adapter。服務集合只在組合根單一寫入者、Build前使用，不支援並行修改描述子。
adapter 解析時綁定程序級相容入口，容器關閉時先解除 registration，
再由diagnostics排空queue、釋放CTS／mutex與檔案資源，最後由sender的原owner釋放client。

使用邊界與維護：
diagnosticsDirectory 只能來自部署組合根，不能由 request 或使用者輸入提供。外部
sender／HttpClient 不由本檔建立；產品 Host 仍負責建立、取消、drain 與 Dispose
其 sender，避免 Hosting 取得錯誤的 credential 所有權。修改服務註冊時須以真實 DI
container 驗證重入、重複 owner、關機順序與資源基線。
*/
using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessage.Hosting;

/// <summary>以不可變啟動身分驗證重入及衝突，集中註冊唯一診斷owner與相容adapter。</summary>
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
        if (adapter is not null && !ReferenceEquals(adapter.Diagnostics, diagnostics))
            throw new ArgumentException("The adapter must borrow the registered diagnostics owner.", nameof(adapter));
        var state = new RegistrationState(null, null, diagnostics, adapter);
        if (ReuseOrReject(services, state))
            return services;

        services.AddSingleton(state);
        // 外部instance是借用：Microsoft DI不Dispose由呼叫端建立的instance。
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
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(diagnosticsDirectory));
        var options = outputOptions ?? new ExceptionOutputOptions();
        var state = new RegistrationState(directory, options, null, null);
        if (ReuseOrReject(services, state))
            return services;

        services.AddSingleton(state);
        services.AddSingleton(options);
        services.AddSingleton<ExceptionDiagnostics>(_ =>
            new ExceptionDiagnostics(directory, outputOptions: options));
        services.AddSingleton<ExceptionReportingAdapter>();
        return services;
    }

    /// <summary>
    /// 驗證整份註冊身分才允許描述子發布；非本入口的保留服務、重複標記或不同快照均拒絕。
    /// 不解析factory、不建立owner；失敗時原集合保持不變，例外不包含部署目錄或憑證。
    /// </summary>
    private static bool ReuseOrReject(IServiceCollection services, RegistrationState candidate)
    {
        var registrations = services.Where(item => item.ServiceType == typeof(RegistrationState)).ToArray();
        if (registrations.Length != 0)
        {
            if (registrations.Length == 1 &&
                registrations[0].ImplementationInstance is RegistrationState existing &&
                existing.Matches(candidate))
                return true;
            throw new InvalidOperationException("Conflicting Hosting diagnostics registration.");
        }

        if (services.Any(item => item.ServiceType == typeof(ExceptionDiagnostics) ||
            item.ServiceType == typeof(ExceptionReportingAdapter) || item.ServiceType == typeof(ExceptionOutputOptions)))
            throw new InvalidOperationException("Hosting diagnostics services must use the shared registration entrypoint.");
        return false;
    }

    /// <summary>
    /// 單一容器建構期的不可變標記；自建模式只保留標準部署路徑及布林快照，
    /// 借用模式只保存同一owner／adapter參考，不延長至其他容器，也不擁有其資源。
    /// </summary>
    private sealed class RegistrationState(
        string? directory, ExceptionOutputOptions? options,
        ExceptionDiagnostics? owner, ExceptionReportingAdapter? adapter)
    {
        private readonly string? _directory = directory;
        private readonly ExceptionOutputOptions? _options = options;
        private readonly ExceptionDiagnostics? _owner = owner;
        private readonly ExceptionReportingAdapter? _adapter = adapter;

        /// <summary>依平台路徑語意及完整輸出策略比對；外部owner／adapter僅以參考身分判斷。</summary>
        internal bool Matches(RegistrationState other) =>
            string.Equals(_directory, other._directory,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
            _options?.WriteExceptionLog == other._options?.WriteExceptionLog &&
            _options?.SendLine == other._options?.SendLine &&
            ReferenceEquals(_owner, other._owner) && ReferenceEquals(_adapter, other._adapter);
    }
}

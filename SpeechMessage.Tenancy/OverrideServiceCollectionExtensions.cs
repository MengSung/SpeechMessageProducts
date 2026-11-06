/*
 * 檔案整體說明
 *
 * 目的與定位：完整覆寫地基的顯式 Host 組合根；不自行建立來源、連線或 notifier，不注入隱藏 no-op 或 request-scoped 服務。
 *
 * 主要功能與流程：
 * 本檔宣告 OverrideServiceCollectionExtensions、Registration。
 * - 註冊單一 cache owner、純系統表、validator 及 catalog。Host 必須先註冊恰一個無 request 狀態的 singleton 來源， 把 reportFailure 接到既有共用 
 * ExceptionDiagnostics，且 await 容器 DisposeAsync 後才關閉來源／manager。
 * 主要操作入口包含 AddChurchTextOverrides、Registration。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SpeechMessage.Localization;

namespace SpeechMessage.Tenancy;

/// <summary>完整覆寫地基的顯式 Host 組合根；不自行建立來源、連線或 notifier，不注入隱藏 no-op 或 request-scoped 服務。</summary>
public static class OverrideServiceCollectionExtensions
{
    /// <summary>
    /// 註冊單一 cache owner、純系統表、validator 及 catalog。Host 必須先註冊恰一個無 request 狀態的 singleton 來源，
    /// 把 reportFailure 接到既有共用 ExceptionDiagnostics，且 await 容器 DisposeAsync 後才關閉來源／manager。
    /// </summary>
    /// <param name="services">Host 擁有且尚未建構的集合，不在內部 BuildServiceProvider。</param>
    /// <param name="longestChurchName">與 I18nCheck limits 相同的完整合成最長樣本。</param>
    /// <param name="reportFailure">必要共用失敗入口；不得另建 LINE client，也不得捕捉 request、使用者或 credential。</param>
    /// <returns>原服務集合；相同樣本及同一 notifier 委派才可冪等重入。</returns>
    public static IServiceCollection AddChurchTextOverrides(this IServiceCollection services, string longestChurchName,
        Action<Exception, string> reportFailure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(reportFailure);
        var sources = 0;
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(Registration) && descriptor.ImplementationInstance is Registration existing)
            {
                if (!string.Equals(existing.LongestName, longestChurchName, StringComparison.Ordinal)
                    || !ReferenceEquals(existing.Report, reportFailure))
                    throw new InvalidOperationException("Override services already have a different owner policy.");
                return services;
            }
            if (descriptor.ServiceType == typeof(IChurchTextOverrideSource))
            {
                if (descriptor.Lifetime != ServiceLifetime.Singleton)
                    throw new InvalidOperationException("Override source must not capture a scoped or transient owner.");
                sources++;
            }
            if (descriptor.ServiceType == typeof(ChurchTextOverrideCache)
                || descriptor.ServiceType == typeof(IChurchTextOverrideSnapshotProvider)
                || descriptor.ServiceType == typeof(ITextCatalog)
                || descriptor.ServiceType == typeof(ChurchTextOverrideValidator))
                throw new InvalidOperationException("Override services already have an explicit owner.");
        }
        if (sources != 1) throw new InvalidOperationException("Exactly one explicit override source is required.");
        // 在新增任何服務之前驗固定樣本，錯配置不能留下半套註冊；生成系統表無 I/O 或資源擁有權。
        _ = new ChurchTextOverrideValidator(new SystemTextTable(), longestChurchName);
        services.AddSingleton(new Registration(longestChurchName, reportFailure));
        services.TryAddSingleton<SystemTextTable>();
        services.AddSingleton(provider => new ChurchTextOverrideValidator(provider.GetRequiredService<SystemTextTable>(), longestChurchName));
        services.AddSingleton(provider => new ChurchTextOverrideCache(provider.GetRequiredService<IChurchTextOverrideSource>(),
            provider.GetRequiredService<ChurchTextOverrideValidator>(), reportFailure, provider.GetService<TimeProvider>()));
        services.AddSingleton<IChurchTextOverrideSnapshotProvider>(provider => provider.GetRequiredService<ChurchTextOverrideCache>());
        services.AddSingleton<ITextCatalog>(provider => new TextCatalog(provider.GetRequiredService<SystemTextTable>(),
            provider.GetRequiredService<IChurchTextOverrideSnapshotProvider>()));
        return services;
    }

    /// <summary>純 Host 政策標記，不保存服務容器、request 或使用者；委派與樣本最長只隨容器存活。</summary>
    /// <param name="LongestName">固定合成最長名稱樣本。</param>
    /// <param name="Report">Host 提供的唯一共用 notifier 轉接。</param>
    private sealed record Registration(string LongestName, Action<Exception, string> Report);
}

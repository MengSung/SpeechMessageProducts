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
        services.TryAddSingleton<SystemTextTable>();
        return AddChurchTextOverridesCore(services,
            new ChurchTextOverrideValidator(new SystemTextTable(), longestChurchName),
            provider => new TextCatalog(provider.GetRequiredService<SystemTextTable>(),
                provider.GetRequiredService<IChurchTextOverrideSnapshotProvider>()),
            reportFailure, longestChurchName, isDefaultPolicy: true);
    }

    /// <summary>
    /// 使用產品字典的 validator 與 catalog 註冊唯一快取 owner，避免產品鍵被錯當共享字典而拒絕合法覆寫。
    /// Host 仍須先註冊唯一 singleton source；呼叫端委派只建純 catalog，不能捕捉 request 或外部資源。
    /// </summary>
    /// <param name="services">Host 擁有且尚未建置的服務集合，不在內部建立容器。</param>
    /// <param name="validator">以實際產品 system table 建立的不可變覆寫驗證器。</param>
    /// <param name="createCatalog">以本 Host 唯一 snapshot provider 建立產品 catalog。</param>
    /// <param name="reportFailure">Host 共用的原例外通知轉接。</param>
    /// <param name="registrationIdentity">固定產品註冊標記，用於拒絕不同 owner 政策重入。</param>
    /// <returns>已註冊唯一覆寫來源、快取與 catalog 的原服務集合。</returns>
    public static IServiceCollection AddChurchTextOverrides(this IServiceCollection services,
        ChurchTextOverrideValidator validator, Func<IServiceProvider, ITextCatalog> createCatalog,
        Action<Exception, string> reportFailure, string registrationIdentity)
        => AddChurchTextOverridesCore(services, validator, createCatalog, reportFailure, registrationIdentity,
            isDefaultPolicy: false);

    private static IServiceCollection AddChurchTextOverridesCore(IServiceCollection services,
        ChurchTextOverrideValidator validator, Func<IServiceProvider, ITextCatalog> createCatalog,
        Action<Exception, string> reportFailure, string registrationIdentity, bool isDefaultPolicy)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(createCatalog);
        ArgumentNullException.ThrowIfNull(reportFailure);
        if (string.IsNullOrWhiteSpace(registrationIdentity))
            throw new ArgumentException("A fixed registration identity is required.", nameof(registrationIdentity));
        var sources = 0;
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(Registration) && descriptor.ImplementationInstance is Registration existing)
            {
                if (!string.Equals(existing.Identity, registrationIdentity, StringComparison.Ordinal)
                    || existing.IsDefaultPolicy != isDefaultPolicy
                    || (!isDefaultPolicy && !ReferenceEquals(existing.Validator, validator))
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
        services.AddSingleton(new Registration(registrationIdentity, validator, reportFailure, isDefaultPolicy));
        services.AddSingleton(validator);
        services.AddSingleton(provider => new ChurchTextOverrideCache(provider.GetRequiredService<IChurchTextOverrideSource>(),
            validator, reportFailure, provider.GetService<TimeProvider>()));
        services.AddSingleton<IChurchTextOverrideSnapshotProvider>(provider => provider.GetRequiredService<ChurchTextOverrideCache>());
        services.AddSingleton(createCatalog);
        return services;
    }

    /// <summary>純 Host 政策標記，只供同一容器比對註冊 owner，不保存 request 或使用者狀態。</summary>
    /// <param name="Identity">固定產品註冊識別。</param>
    /// <param name="Validator">唯一 cache 使用的產品字典驗證器。</param>
    /// <param name="Report">Host 提供的唯一共用 notifier 轉接。</param>
    /// <param name="IsDefaultPolicy">是否為使用固定系統字典與最長名稱樣本的預設註冊路徑。</param>
    private sealed record Registration(string Identity, ChurchTextOverrideValidator Validator,
        Action<Exception, string> Report, bool IsDefaultPolicy);
}

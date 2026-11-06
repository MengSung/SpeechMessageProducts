/*
 * 檔案整體說明
 *
 * 目的與定位：產品級 ToolUtility 組合根；產品與組態只能由受信任的 host 提供，不能來自 request。 一個容器只屬於一個產品，manager／pool 是容器擁有的 singleton，資料操作維持 
 * scoped。
 *
 * 主要功能與流程：
 * 本檔宣告 ServiceCollectionExtensions、ProductRegistration。
 * - 註冊 ToolUtility 服務到 DI 容器 ToolUtilityClass 與其 Provider 都使用 Scoped 生命週期；每個 request 取得自己的 Dataverse 連線租約，request 
 * 結束時由 DI 確定性釋放。
 * - 以明確的產品與組態註冊資料服務，讓實際 DataverseConnectionKey 包含正確產品。 組態只活至 host 容器結束，不保存 HttpContext、Session 或個別使用者值。 
 * 同一容器的衝突產品／組態會在任何新增註冊前拒絕，避免第一份 singleton 靜默勝出。
 * - 共用新舊 API 的唯一註冊流程，不建立隱藏容器或捕獲 scoped service。 null 組態只供無參數相容入口，所有需組態的 factory 透過同一解析函式取得來源。
 * - 保存首次註冊的受信任邊界，供後續註冊比對，不讀取或複製機密內容。
 * 主要操作入口包含 AddToolUtility、RegisterServices、ResolveEnvironmentName。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using System;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Diagnostics;

namespace ToolUtilityNameSpace.DependencyInjection
{
    /// <summary>
    /// 產品級 ToolUtility 組合根；產品與組態只能由受信任的 host 提供，不能來自 request。
    /// 一個容器只屬於一個產品，manager／pool 是容器擁有的 singleton，資料操作維持 scoped。
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 註冊 ToolUtility 服務到 DI 容器
        /// ToolUtilityClass 與其 Provider 都使用 Scoped 生命週期；每個 request
        /// 取得自己的 Dataverse 連線租約，request 結束時由 DI 確定性釋放。
        /// </summary>
        /// <param name="services">服務集合</param>
        /// <returns>服務集合</returns>
        [Obsolete("請改用 AddToolUtility(configuration, product) 明確指定產品；此相容多載預定於階段 6 移除。")]
        public static IServiceCollection AddToolUtility(this IServiceCollection services)
        {
            // 舊 API 沒有組態參數，延至解析時才從原容器取得組態；不可為取得設定另建 ServiceProvider。
            return RegisterServices(services, null, "ChurchReport");
        }

        /// <summary>
        /// 以明確的產品與組態註冊資料服務，讓實際 DataverseConnectionKey 包含正確產品。
        /// 組態只活至 host 容器結束，不保存 HttpContext、Session 或個別使用者值。
        /// 同一容器的衝突產品／組態會在任何新增註冊前拒絕，避免第一份 singleton 靜默勝出。
        /// </summary>
        /// <param name="services">由 host 擁有且尚在建構中的服務集合；非並行註冊 API。</param>
        /// <param name="configuration">此產品的 host 組態；讀取必要連線設定時仍須通過 manager 驗證。</param>
        /// <param name="product">伺服器組合根選定的非空產品識別，不接受呼叫端路由作為權限。</param>
        /// <returns>同一服務集合；相同組態實例與產品重複註冊為冪等。</returns>
        public static IServiceCollection AddToolUtility(
            this IServiceCollection services, IConfiguration configuration, string product)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            return RegisterServices(services, configuration, product);
        }

        /// <summary>
        /// 共用新舊 API 的唯一註冊流程，不建立隱藏容器或捕獲 scoped service。
        /// null 組態只供無參數相容入口，所有需組態的 factory 透過同一解析函式取得來源。
        /// </summary>
        private static IServiceCollection RegisterServices(
            IServiceCollection services, IConfiguration configuration, string product)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }
            if (string.IsNullOrWhiteSpace(product))
                throw new ArgumentException("A non-empty product identifier is required.", nameof(product));
            foreach (var descriptor in services)
            {
                if (descriptor.ServiceType != typeof(ProductRegistration)) continue;
                var previous = (ProductRegistration)descriptor.ImplementationInstance;
                if (!string.Equals(previous.Product, product, StringComparison.Ordinal)
                    || !ReferenceEquals(previous.Configuration, configuration))
                    throw new InvalidOperationException("ToolUtility is already registered for a different product or configuration.");
                return services;
            }

            services.AddSingleton(new ProductRegistration(product, configuration));
            if (configuration != null) services.TryAddSingleton<IConfiguration>(configuration);

            // 此委派只捕獲 host 組態，不保存 provider；每個 factory 仍在原容器依原生命週期執行。
            IConfiguration ResolveConfiguration(IServiceProvider provider) =>
                configuration ?? provider.GetRequiredService<IConfiguration>();

            // Manager 與 pool 都是程序級資源擁有者；它們只能依賴 Singleton，
            // 絕不捕獲 request-scoped service。TryAdd 讓測試或其他產品可先提供
            // 自己的假連線服務，而不會意外建立第二個組合根實例。
            services.TryAddSingleton<ICrmConnectionService, CrmConnectionService>();
            // 未由產品組合根明確 opt-in 時，統一診斷一律 fail closed。這個 fallback
            // 不讀取 appsettings 的 Enabled，避免共用 ToolUtility 被其他 Host 誤開檔案追蹤。
            services.TryAddSingleton<DiagnosticTraceOptions>(_ =>
                DiagnosticTraceOptions.CreateDisabled(Environment.CurrentDirectory));
            services.TryAddSingleton<DataverseTraceOptions>(sp =>
            {
                var options = DataverseTraceOptions.FromDiagnosticOptions(
                    sp.GetRequiredService<DiagnosticTraceOptions>());
                options.Validate();
                return options;
            });
            // Trace 是唯一擁有背景佇列、檔案與程序內 HMAC salt 的 singleton；它不依賴
            // scoped service，也不保存 HttpContext，request 關聯只在 middleware 的 AsyncLocal 範圍內存在。
            services.TryAddSingleton<DataverseTrace>();
            services.TryAddSingleton<DataversePoolOptions>(sp =>
            {
                var options = new DataversePoolOptions();
                ResolveConfiguration(sp)
                    .GetSection("Dataverse:Pool")
                    .Bind(options);
                options.Validate();
                return options;
            });
            services.TryAddSingleton<DataverseConnectionManager>(sp =>
            {
                var effectiveConfiguration = ResolveConfiguration(sp);
                return new DataverseConnectionManager(
                    sp.GetRequiredService<ICrmConnectionService>(),
                    effectiveConfiguration,
                    product,
                    ResolveEnvironmentName(effectiveConfiguration),
                    sp.GetRequiredService<DataversePoolOptions>());
            });
            services.TryAddSingleton<IDataverseConnectionManager>(sp =>
                sp.GetRequiredService<DataverseConnectionManager>());
            services.TryAddSingleton<IBoundedClientPool>(sp =>
                sp.GetRequiredService<DataverseConnectionManager>().Pool);
            services.TryAddSingleton<IToolUtilityTracer, NullToolUtilityTracer>();
            services.TryAddSingleton<ICrmConnectionPool>(sp =>
                new ConnectionPoolStatsAdapter(sp.GetRequiredService<IDataverseConnectionManager>()));
            services.TryAddScoped<IDataverseGateway, DataverseGateway>();
            services.TryAddScoped<IOrganizationService, GatewayOrganizationService>();

            services.AddScoped<ToolUtilityClass>(sp => new ToolUtilityClass(
                sp.GetRequiredService<IOrganizationService>(),
                sp.GetRequiredService<IToolUtilityTracer>(),
                ResolveConfiguration(sp)));
            services.AddScoped<IToolUtilityProvider, ToolUtilityProvider>();
            return services;
        }

        /// <summary>
        /// 註冊期間的有限不可變標記，只含 host 擁有的產品與組態參照，不是使用者狀態快取。
        /// 與 IServiceCollection／容器同生命週期，不擁有組態或任何 IDisposable。
        /// </summary>
        private sealed class ProductRegistration
        {
            /// <summary>保存首次註冊的受信任邊界，供後續註冊比對，不讀取或複製機密內容。</summary>
            internal ProductRegistration(string product, IConfiguration configuration)
            {
                Product = product;
                Configuration = configuration;
            }

            /// <summary>唯一的 host 產品識別，不由 request 改寫。</summary>
            internal string Product { get; }

            /// <summary>原 host 組態；null 表示延用既有 DI 解析，不代表缺少連線驗證。</summary>
            internal IConfiguration Configuration { get; }
        }

        /// <summary>
        /// 解析主機環境名稱而不讓 ToolUtility 組件直接依賴 ASP.NET Core。
        /// Web Host 會把環境名稱放入組態；測試與非 Web 組合根則使用明確的
        /// DOTNET/ASPNETCORE 環境變數，最後才回到 Production。這個值只進入
        /// Pool Key，不承載使用者身分，也不會使不同環境共用 client。
        /// </summary>
        private static string ResolveEnvironmentName(IConfiguration configuration)
        {
            return configuration["ASPNETCORE_ENVIRONMENT"]
                ?? configuration["DOTNET_ENVIRONMENT"]
                ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                ?? "Production";
        }
    }
}

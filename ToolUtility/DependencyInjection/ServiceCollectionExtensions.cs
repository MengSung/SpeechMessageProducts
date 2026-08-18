// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class ServiceCollectionExtensions
// 主要成員：AddToolUtility
// 引用命名空間：Microsoft.Extensions.DependencyInjection、System
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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
    /// ServiceCollection 擴展方法
    /// 用於在 ASP.NET Core 中註冊 ToolUtility 服務
    /// 實現 Dependency Injection 模式
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
        public static IServiceCollection AddToolUtility(this IServiceCollection services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            // Manager 與 pool 都是程序級資源擁有者；它們只能依賴 Singleton，
            // 絕不捕獲 request-scoped service。TryAdd 讓測試或其他產品可先提供
            // 自己的假連線服務，而不會意外建立第二個組合根實例。
            services.TryAddSingleton<ICrmConnectionService, CrmConnectionService>();
            services.TryAddSingleton<DataverseTraceOptions>(sp =>
            {
                var options = DataverseTraceOptions.FromConfiguration(
                    sp.GetRequiredService<IConfiguration>());
                options.Validate();
                return options;
            });
            // Trace 是唯一擁有背景佇列、檔案與程序內 HMAC salt 的 singleton；它不依賴
            // scoped service，也不保存 HttpContext，request 關聯只在 middleware 的 AsyncLocal 範圍內存在。
            services.TryAddSingleton<DataverseTrace>();
            services.TryAddSingleton<DataversePoolOptions>(sp =>
            {
                var options = new DataversePoolOptions();
                sp.GetRequiredService<IConfiguration>()
                    .GetSection("Dataverse:Pool")
                    .Bind(options);
                options.Validate();
                return options;
            });
            services.TryAddSingleton<DataverseConnectionManager>(sp =>
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                return new DataverseConnectionManager(
                    sp.GetRequiredService<ICrmConnectionService>(),
                    configuration,
                    "ChurchReport",
                    ResolveEnvironmentName(configuration),
                    sp.GetRequiredService<DataversePoolOptions>());
            });
            services.TryAddSingleton<IDataverseConnectionManager>(sp =>
                sp.GetRequiredService<DataverseConnectionManager>());
            services.TryAddSingleton<IBoundedClientPool>(sp =>
                sp.GetRequiredService<DataverseConnectionManager>().Pool);
            services.TryAddSingleton<ICrmConnectionPool>(sp =>
                new ConnectionPoolStatsAdapter(sp.GetRequiredService<IDataverseConnectionManager>()));
            services.TryAddScoped<IDataverseGateway, DataverseGateway>();
            services.TryAddScoped<IOrganizationService, GatewayOrganizationService>();

            services.AddScoped<ToolUtilityClass>(sp => new ToolUtilityClass(
                sp.GetRequiredService<IOrganizationService>(),
                sp.GetRequiredService<IToolUtilityTracer>(),
                sp.GetRequiredService<IConfiguration>()));
            services.AddScoped<IToolUtilityProvider, ToolUtilityProvider>();
            return services;
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

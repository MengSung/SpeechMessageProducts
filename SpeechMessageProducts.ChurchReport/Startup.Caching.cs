/*
 * 檔案整體說明
 *
 * 目的與定位：集中註冊記憶體快取、記憶體型IDistributedCache與CRM快取服務，供Startup組合根建立容器所有權；目前不是Redis持久化實作，快取資料的scope與期限仍由使用服務定義。
 *
 * 主要功能與流程：
 * 本檔宣告 Startup。
 * 主要操作入口包含 AddCachingServices。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/Contracts/ChurchClockStartupTests.cs、
 * ChurchReport.MemberInfo.Tests/Contracts/TenancyCallSiteTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/DonationPaymentFormModelNamingTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/DonationPaymentViewDefaultsTests.cs、
 * SpeechMessage.Tenancy.Tests/ConfigurationResolverTests.cs 追查目前工作樹中的合作／使用位置。
 */
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using ToolUtility.Caching;

namespace ChurchReport
{
    public partial class Startup
    {
        private void AddCachingServices(IServiceCollection services)
        {
            // Memory cache
            services.AddMemoryCache();

            // Distributed cache（如需 Redis，請改為 AddStackExchangeRedisCache）
            // 範例：
            // services.AddStackExchangeRedisCache(options =>
            // {
            //     options.Configuration = Configuration["Redis:ConnectionString"]; // e.g. localhost:6379
            //     options.InstanceName = Configuration["Redis:InstanceName"]; // optional
            // });
            services.AddDistributedMemoryCache();

            // CRM Cache Service
            services.AddSingleton<CrmCacheService>();
        }
    }
}

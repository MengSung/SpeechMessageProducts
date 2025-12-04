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

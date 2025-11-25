using Microsoft.Extensions.DependencyInjection;
using System;

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
        /// 使用 Singleton 生命週期
        /// </summary>
        /// <param name="services">服務集合</param>
        /// <returns>服務集合</returns>
        public static IServiceCollection AddToolUtility(this IServiceCollection services)
        {
            // 註冊為 Singleton，確保整個應用程式生命週期只有一個實例
            services.AddSingleton<IToolUtilityProvider, ToolUtilityProvider>();
            return services;
        }
    }
}

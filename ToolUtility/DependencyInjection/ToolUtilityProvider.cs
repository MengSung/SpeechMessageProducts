using System;
using ToolUtilityNameSpace.Factory;

namespace ToolUtilityNameSpace.DependencyInjection
{
    /// <summary>
    /// ToolUtility 服務提供者實現
    /// 透過 Factory 模式提供單一實例
    /// 結合 Singleton、Factory 和 Dependency Injection 模式
    /// </summary>
    public class ToolUtilityProvider : IToolUtilityProvider
    {
        /// <summary>
        /// 取得 ToolUtilityClass 實例（通過 Factory）
        /// </summary>
        /// <returns>ToolUtilityClass 實例</returns>
        public ToolUtilityClass GetToolUtility()
        {
            return ToolUtilityFactory.GetInstance();
        }
    }
}

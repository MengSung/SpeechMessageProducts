using System;

namespace ToolUtilityNameSpace.DependencyInjection
{
    /// <summary>
    /// ToolUtility 服務提供者接口
    /// 用於 Dependency Injection 模式
    /// 遵循依賴反轉原則 (Dependency Inversion Principle)
    /// </summary>
    public interface IToolUtilityProvider
    {
        /// <summary>
        /// 取得 ToolUtilityClass 實例
        /// </summary>
        /// <returns>ToolUtilityClass 實例</returns>
        ToolUtilityClass GetToolUtility();
    }
}

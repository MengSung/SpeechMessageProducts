/*
 * 檔案整體說明
 *
 * 目的與定位：提供目前 request scope 的 ToolUtilityClass。 Provider 本身必須是 Scoped，避免把持有 scoped CRM 租約的工具提升為 Singleton。
 *
 * 主要功能與流程：
 * 本檔宣告 ToolUtilityProvider。
 * - 建立目前 scope 的提供者。 由 DI 注入同一 scope 的 ToolUtilityClass；provider 不擁有也不另行釋放它， 由外層 DI scope 統一完成 ToolUtility、Facade 
 * 與 CRM 租約的釋放。
 * - 取得目前 request scope 的 ToolUtilityClass。
 * 主要操作入口包含 GetToolUtility。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs、
 * ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs 追查目前工作樹中的合作／使用位置。
 */
using System;

namespace ToolUtilityNameSpace.DependencyInjection
{
    /// <summary>
    /// 提供目前 request scope 的 ToolUtilityClass。
    /// Provider 本身必須是 Scoped，避免把持有 scoped CRM 租約的工具提升為 Singleton。
    /// </summary>
    public class ToolUtilityProvider : IToolUtilityProvider
    {
        private readonly ToolUtilityClass _toolUtility;

        /// <summary>
        /// 建立目前 scope 的提供者。
        /// 由 DI 注入同一 scope 的 ToolUtilityClass；provider 不擁有也不另行釋放它，
        /// 由外層 DI scope 統一完成 ToolUtility、Facade 與 CRM 租約的釋放。
        /// </summary>
        /// <param name="toolUtility">目前 request scope 的 ToolUtilityClass。</param>
        public ToolUtilityProvider(ToolUtilityClass toolUtility)
        {
            _toolUtility = toolUtility ?? throw new ArgumentNullException(nameof(toolUtility));
        }

        /// <summary>
        /// 取得目前 request scope 的 ToolUtilityClass。
        /// </summary>
        /// <returns>ToolUtilityClass 實例</returns>
        public ToolUtilityClass GetToolUtility()
        {
            return _toolUtility;
        }
    }
}

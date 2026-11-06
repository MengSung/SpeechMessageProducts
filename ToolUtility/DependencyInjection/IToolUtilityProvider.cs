/*
 * 檔案整體說明
 *
 * 目的與定位：ToolUtility 服務提供者接口 用於 Dependency Injection 模式 遵循依賴反轉原則 (Dependency Inversion Principle)
 *
 * 主要功能與流程：
 * 本檔宣告 IToolUtilityProvider。
 * 資料／選項欄位包含 IToolUtilityProvider，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs、
 * ToolUtility/DependencyInjection/ToolUtilityProvider.cs 追查目前工作樹中的合作／使用位置。
 */
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

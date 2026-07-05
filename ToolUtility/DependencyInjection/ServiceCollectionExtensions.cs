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

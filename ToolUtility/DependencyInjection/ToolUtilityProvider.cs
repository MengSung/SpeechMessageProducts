// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/DependencyInjection/ToolUtilityProvider.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class ToolUtilityProvider
// 主要成員：ToolUtilityProvider、GetToolUtility
// 引用命名空間：System
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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

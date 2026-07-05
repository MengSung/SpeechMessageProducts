// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/ToolUtilityPartials/ToolUtilityClass.Metadata.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class ToolUtilityClass
// 主要成員：GetEntityAttributeNames
// 引用命名空間：System、System.Collections.Generic
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;

namespace ToolUtilityNameSpace
{
    public partial class ToolUtilityClass
    {
        /// <summary>
        /// Proxy to ToolUtilityFacade.GetEntityAttributeNames
        /// </summary>
        public HashSet<string> GetEntityAttributeNames(string entityLogicalName)
        {
            try
            {
                return _facade?.GetEntityAttributeNames(entityLogicalName) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Proxy to ToolUtilityFacade.GetEntityAttributeTypes
        /// </summary>
        public Dictionary<string, string> GetEntityAttributeTypes(string entityLogicalName)
        {
            try
            {
                return _facade?.GetEntityAttributeTypes(entityLogicalName) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Proxy to ToolUtilityFacade.GetEntityAttributeMetadata
        /// </summary>
        public Dictionary<string, Microsoft.Xrm.Sdk.Metadata.AttributeMetadata> GetEntityAttributeMetadata(string entityLogicalName)
        {
            try
            {
                return _facade?.GetEntityAttributeMetadata(entityLogicalName) ?? new Dictionary<string, Microsoft.Xrm.Sdk.Metadata.AttributeMetadata>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, Microsoft.Xrm.Sdk.Metadata.AttributeMetadata>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}

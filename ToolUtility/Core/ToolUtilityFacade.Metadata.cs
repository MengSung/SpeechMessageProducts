// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/Core/ToolUtilityFacade.Metadata.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class ToolUtilityFacade
// 主要成員：GetEntityAttributeNames
// 引用命名空間：Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Metadata、System、System.Collections.Generic
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using System;
using System.Collections.Generic;

namespace ToolUtilityNameSpace.Core
{
    public partial class ToolUtilityFacade
    {
        /// <summary>
        /// Retrieve attribute logical names for an entity from CRM metadata
        /// </summary>
        public HashSet<string> GetEntityAttributeNames(string entityLogicalName)
        {
            try
            {
                var req = new Microsoft.Xrm.Sdk.Messages.RetrieveEntityRequest
                {
                    EntityFilters = Microsoft.Xrm.Sdk.Metadata.EntityFilters.Attributes,
                    LogicalName = entityLogicalName
                };

                var resp = (Microsoft.Xrm.Sdk.Messages.RetrieveEntityResponse)_organizationService.Execute(req);
                var attrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var a in resp.EntityMetadata.Attributes)
                {
                    if (!string.IsNullOrEmpty(a.LogicalName)) attrs.Add(a.LogicalName);
                }
                return attrs;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"GetEntityAttributeNames failed for '{entityLogicalName}': {ex.Message}");
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Retrieve attribute logical names and their attribute type strings for an entity from CRM metadata
        /// </summary>
        public Dictionary<string, string> GetEntityAttributeTypes(string entityLogicalName)
        {
            try
            {
                var req = new Microsoft.Xrm.Sdk.Messages.RetrieveEntityRequest
                {
                    EntityFilters = Microsoft.Xrm.Sdk.Metadata.EntityFilters.Attributes,
                    LogicalName = entityLogicalName
                };

                var resp = (Microsoft.Xrm.Sdk.Messages.RetrieveEntityResponse)_organizationService.Execute(req);
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var a in resp.EntityMetadata.Attributes)
                {
                    if (!string.IsNullOrEmpty(a.LogicalName))
                    {
                        var typeName = a.AttributeType?.ToString() ?? a.GetType().Name;
                        dict[a.LogicalName] = typeName;
                    }
                }
                return dict;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"GetEntityAttributeTypes failed for '{entityLogicalName}': {ex.Message}");
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Retrieve AttributeMetadata map for an entity from CRM metadata
        /// </summary>
        public Dictionary<string, AttributeMetadata> GetEntityAttributeMetadata(string entityLogicalName)
        {
            try
            {
                var req = new Microsoft.Xrm.Sdk.Messages.RetrieveEntityRequest
                {
                    EntityFilters = Microsoft.Xrm.Sdk.Metadata.EntityFilters.Attributes,
                    LogicalName = entityLogicalName
                };

                var resp = (Microsoft.Xrm.Sdk.Messages.RetrieveEntityResponse)_organizationService.Execute(req);
                var dict = new Dictionary<string, AttributeMetadata>(StringComparer.OrdinalIgnoreCase);
                foreach (var a in resp.EntityMetadata.Attributes)
                {
                    if (!string.IsNullOrEmpty(a.LogicalName))
                    {
                        dict[a.LogicalName] = a;
                    }
                }
                return dict;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"GetEntityAttributeMetadata failed for '{entityLogicalName}': {ex.Message}");
                return new Dictionary<string, AttributeMetadata>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}

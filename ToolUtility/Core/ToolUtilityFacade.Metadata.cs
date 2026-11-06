/*
 * 檔案整體說明
 *
 * 目的與定位：提供Facade對資料實體欄位名稱的metadata查詢，讓主工具partial保持薄委派；查詢使用目前已授權連線，metadata查閱不構成對業務記錄的存取授權。
 *
 * 主要功能與流程：
 * 本檔宣告 ToolUtilityFacade。
 * 主要操作入口包含 GetEntityAttributeNames、GetEntityAttributeTypes、GetEntityAttributeMetadata。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
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

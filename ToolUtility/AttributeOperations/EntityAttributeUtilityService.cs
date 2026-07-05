// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/AttributeOperations/EntityAttributeUtilityService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class EntityAttributeUtilityService
// 主要成員：GetAttributeValue、RemoveAttribute、SetEntityAttributeToNull
// 引用命名空間：Microsoft.Xrm.Sdk、System、ToolUtilityNameSpace.Interfaces
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Xrm.Sdk;
using System;
using ToolUtilityNameSpace.Interfaces;

namespace ToolUtilityNameSpace.AttributeOperations
{
    /// <summary>
    /// 實體屬性工具服務實作
    /// 處理實體屬性的通用操作
    /// </summary>
    public class EntityAttributeUtilityService : IEntityAttributeUtilityService
    {
        private readonly object _logger;

        public EntityAttributeUtilityService(object logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 取得屬性值（支援 AliasedValue）
        /// </summary>
        public string GetAttributeValue(Entity targetEntity, string attributeName)
        {
            try
            {
                if (targetEntity == null)
                    throw new ArgumentNullException(nameof(targetEntity));

                if (string.IsNullOrEmpty(attributeName))
                    return string.Empty;

                if (!targetEntity.Contains(attributeName))
                    return string.Empty;

                var value = targetEntity[attributeName];

                if (value is AliasedValue aliasedValue)
                {
                    return aliasedValue.Value?.ToString() ?? string.Empty;
                }
                else
                {
                    return value?.ToString() ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Error getting attribute value '{attributeName}' from entity '{targetEntity?.LogicalName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 移除實體的指定屬性
        /// </summary>
        public void RemoveAttribute(ref Entity entity, string propertyName)
        {
            try
            {
                if (entity == null)
                    throw new ArgumentNullException(nameof(entity));

                if (string.IsNullOrWhiteSpace(propertyName))
                    throw new ArgumentNullException(nameof(propertyName));

                if (entity.Attributes.Contains(propertyName))
                {
                    entity.Attributes.Remove(propertyName);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Error removing attribute '{propertyName}' from entity '{entity?.LogicalName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 將實體的指定屬性設為 null
        /// </summary>
        public void SetEntityAttributeToNull(ref Entity entity, string propertyName)
        {
            try
            {
                if (entity == null)
                    throw new ArgumentNullException(nameof(entity));

                if (string.IsNullOrWhiteSpace(propertyName))
                    throw new ArgumentNullException(nameof(propertyName));

                if (entity.Attributes.Contains(propertyName))
                {
                    entity.Attributes[propertyName] = null;
                }
                else
                {
                    entity.Attributes.Add(propertyName, null);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Error setting attribute '{propertyName}' to null for entity '{entity?.LogicalName}': {ex.Message}", ex);
            }
        }
    }
}

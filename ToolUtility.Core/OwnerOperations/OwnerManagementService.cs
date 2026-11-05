// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/OwnerOperations/OwnerManagementService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class OwnerManagementService
// 主要成員：GetOwnerId、GetOwnerName、AssignOwner
// 引用命名空間：Microsoft.Crm.Sdk.Messages、Microsoft.Xrm.Sdk、System、ToolUtilityNameSpace.Interfaces
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using System;
using ToolUtilityNameSpace.Interfaces;

namespace ToolUtilityNameSpace.OwnerOperations
{
    /// <summary>
    /// 負責人管理服務實作
    /// 處理實體的 Owner 相關操作
    /// </summary>
    public class OwnerManagementService : IOwnerManagementService
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;

        public OwnerManagementService(object logger, IOrganizationService organizationService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        }

        /// <summary>
        /// 取得實體的負責人 ID
        /// </summary>
        public Guid GetOwnerId(Entity entity)
        {
            try
            {
                if (entity == null)
                    throw new ArgumentNullException(nameof(entity));

                var ownerRef = entity.GetAttributeValue<EntityReference>("ownerid");
                if (ownerRef == null)
                    return Guid.Empty;

                return ownerRef.Id;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Error getting owner ID for entity '{entity?.LogicalName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 取得實體的負責人名稱
        /// </summary>
        public string GetOwnerName(Entity entity)
        {
            try
            {
                if (entity == null)
                    throw new ArgumentNullException(nameof(entity));

                var ownerRef = entity.GetAttributeValue<EntityReference>("ownerid");
                if (ownerRef == null)
                    return string.Empty;

                return ownerRef.Name ?? string.Empty;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Error getting owner name for entity '{entity?.LogicalName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 指派負責人給實體
        /// </summary>
        public void AssignOwner(string entityName, Entity entity, Guid ownerId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(entityName))
                    throw new ArgumentNullException(nameof(entityName));

                if (entity == null)
                    throw new ArgumentNullException(nameof(entity));

                if (ownerId == Guid.Empty)
                    throw new ArgumentException("Owner ID cannot be empty", nameof(ownerId));

                var assign = new AssignRequest
                {
                    Assignee = new EntityReference("systemuser", ownerId),
                    Target = new EntityReference(entityName, entity.Id)
                };

                _organizationService.Execute(assign);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Error assigning owner for entity '{entityName}' (ID: {entity?.Id}): {ex.Message}", ex);
            }
        }
    }
}

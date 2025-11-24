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

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

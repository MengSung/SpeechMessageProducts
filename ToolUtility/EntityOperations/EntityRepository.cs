using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Diagnostics;

namespace ToolUtilityNameSpace.EntityOperations
{
    /// <summary>
    /// Entity Repository 介面
    /// 遵循 Repository Pattern，專責 Entity CRUD 操作
    /// </summary>
    public interface IEntityRepository
    {
        /// <summary>建立實體</summary>
        Guid Create(Entity entity);

        /// <summary>更新實體</summary>
        void Update(Entity entity);

        /// <summary>刪除實體</summary>
        void Delete(string entityName, Guid entityId);

        /// <summary>檢索單一實體</summary>
        Entity Retrieve(string entityName, Guid entityId, ColumnSet columnSet = null);

        /// <summary>檢索多個實體</summary>
        EntityCollection RetrieveMultiple(QueryBase query);

        /// <summary>指派實體擁有者</summary>
        void AssignOwner(string entityName, Entity entity, Guid newOwnerId);
    }

    /// <summary>
    /// Entity Repository 實現
    /// 使用 Dependency Injection 注入 IOrganizationService
    /// </summary>
    public class EntityRepository : IEntityRepository
    {
        private readonly IOrganizationService _organizationService;

        /// <summary>
        /// 建構函數 - 注入 IOrganizationService
        /// </summary>
        public EntityRepository(IOrganizationService organizationService)
        {
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        }

        /// <summary>
        /// 建立實體
        /// </summary>
        public Guid Create(Entity entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            try
            {
                Trace.WriteLine($"[EntityRepository] Creating {entity.LogicalName}");
                var id = _organizationService.Create(entity);
                Trace.WriteLine($"[EntityRepository] Created successfully. ID: {id}");
                return id;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EntityRepository] Create failed: {ex.Message}");
                throw new InvalidOperationException($"建立 {entity.LogicalName} 失敗", ex);
            }
        }

        /// <summary>
        /// 更新實體
        /// </summary>
        public void Update(Entity entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            if (entity.Id == Guid.Empty)
                throw new ArgumentException("Entity ID 不可為空", nameof(entity));

            try
            {
                Trace.WriteLine($"[EntityRepository] Updating {entity.LogicalName} (ID: {entity.Id})");
                _organizationService.Update(entity);
                Trace.WriteLine($"[EntityRepository] Updated successfully");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EntityRepository] Update failed: {ex.Message}");
                throw new InvalidOperationException($"更新 {entity.LogicalName} 失敗", ex);
            }
        }

        /// <summary>
        /// 刪除實體
        /// </summary>
        public void Delete(string entityName, Guid entityId)
        {
            if (string.IsNullOrWhiteSpace(entityName))
                throw new ArgumentException("Entity name 不可為空", nameof(entityName));

            if (entityId == Guid.Empty)
                throw new ArgumentException("Entity ID 不可為空", nameof(entityId));

            try
            {
                Trace.WriteLine($"[EntityRepository] Deleting {entityName} (ID: {entityId})");
                _organizationService.Delete(entityName, entityId);
                Trace.WriteLine($"[EntityRepository] Deleted successfully");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EntityRepository] Delete failed: {ex.Message}");
                throw new InvalidOperationException($"刪除 {entityName} 失敗", ex);
            }
        }

        /// <summary>
        /// 檢索單一實體
        /// </summary>
        public Entity Retrieve(string entityName, Guid entityId, ColumnSet columnSet = null)
        {
            if (string.IsNullOrWhiteSpace(entityName))
                throw new ArgumentException("Entity name 不可為空", nameof(entityName));

            if (entityId == Guid.Empty)
                throw new ArgumentException("Entity ID 不可為空", nameof(entityId));

            try
            {
                columnSet ??= new ColumnSet(true);
                Trace.WriteLine($"[EntityRepository] Retrieving {entityName} (ID: {entityId})");
                var entity = _organizationService.Retrieve(entityName, entityId, columnSet);
                Trace.WriteLine($"[EntityRepository] Retrieved successfully");
                return entity;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EntityRepository] Retrieve failed: {ex.Message}");
                throw new InvalidOperationException($"檢索 {entityName} 失敗", ex);
            }
        }

        /// <summary>
        /// 檢索多個實體
        /// </summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            if (query == null)
                throw new ArgumentNullException(nameof(query));

            try
            {
                Trace.WriteLine($"[EntityRepository] RetrieveMultiple executing query");
                var results = _organizationService.RetrieveMultiple(query);
                Trace.WriteLine($"[EntityRepository] Retrieved {results.Entities.Count} entities");
                return results;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EntityRepository] RetrieveMultiple failed: {ex.Message}");
                throw new InvalidOperationException("檢索多個實體失敗", ex);
            }
        }

        /// <summary>
        /// 指派實體擁有者
        /// </summary>
        public void AssignOwner(string entityName, Entity entity, Guid newOwnerId)
        {
            if (string.IsNullOrWhiteSpace(entityName))
                throw new ArgumentException("Entity name 不可為空", nameof(entityName));

            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            if (newOwnerId == Guid.Empty)
                throw new ArgumentException("New Owner ID 不可為空", nameof(newOwnerId));

            try
            {
                Trace.WriteLine($"[EntityRepository] Assigning {entityName} to owner {newOwnerId}");
                
                var assignRequest = new Microsoft.Crm.Sdk.Messages.AssignRequest
                {
                    Assignee = new EntityReference("systemuser", newOwnerId),
                    Target = new EntityReference(entityName, entity.Id)
                };
                
                _organizationService.Execute(assignRequest);
                Trace.WriteLine($"[EntityRepository] Owner assigned successfully");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[EntityRepository] AssignOwner failed: {ex.Message}");
                throw new InvalidOperationException($"指派 {entityName} 擁有者失敗", ex);
            }
        }
    }
}

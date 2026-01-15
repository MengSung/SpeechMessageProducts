using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Client;
using System;
using System.Threading.Tasks;

namespace ToolUtilityNameSpace
{
    /// <summary>
    /// ToolUtilityClass - 實體操作 (Partial Class 6/10)
    /// 包含：Create, Retrieve, Update, Delete 方法
    /// </summary>
    public partial class ToolUtilityClass
    {
        #region 實體檢索
        public Entity RetrieveEntity(String EntityName, Guid EntityId)
            => _facade.RetrieveEntity(EntityName, EntityId);

        public Entity RetrieveEntityDynamics365(String EntityName, Guid EntityId)
            => _facade.RetrieveEntity(EntityName, EntityId);

        public Entity RetrieveEntityCrm2011(String EntityName, Guid EntityId)
            => _facade.RetrieveEntity(EntityName, EntityId);

        public Guid GetEntityId(Entity aEntity) => aEntity.Id;
        #endregion

        #region 實體建立
        public Guid CreateEntity(Entity aEntityTobeToCreate)
            => _facade.CreateEntity(aEntityTobeToCreate);

        public Guid CreateEntityDynamics365(ref OrganizationServiceProxy aOrganizationService, Entity aEntityTobeToCreate)
        {
            try
            {
                return EXCUTION_FLAG ? aOrganizationService.Create(aEntityTobeToCreate) : Guid.Empty;
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        public Guid CreateEntityCrm2011(ref IOrganizationService aCrm2011OrganizationService, Entity aEntityTobeToCreate)
        {
            try
            {
                return EXCUTION_FLAG ? aCrm2011OrganizationService.Create(aEntityTobeToCreate) : Guid.Empty;
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        public async Task<Guid> CreateEntityAsync(IOrganizationService aOrganizationService, Entity aEntityTobeToCreate)
        {
            try
            {
                return EXCUTION_FLAG ? aOrganizationService.Create(aEntityTobeToCreate) : Guid.Empty;
            }
            catch (Exception e)
            {
                throw e;
            }
        }
        #endregion

        #region 實體更新
        public void UpdateEntity(ref Entity aEntityTobeUpdated)
            => _facade.UpdateEntity(aEntityTobeUpdated);

        public void UpdateEntity(Entity aEntityTobeUpdated)
            => _facade.UpdateEntity(aEntityTobeUpdated);

        public void UpdateEntity(ref IOrganizationService aOrganizationService, ref Entity aEntityTobeUpdated)
        {
            try
            {
                if (EXCUTION_FLAG) aOrganizationService.Update(aEntityTobeUpdated);
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        public void UpdateEntity(ref IOrganizationService aOrganizationService, Entity aEntityTobeUpdated)
        {
            try
            {
                if (EXCUTION_FLAG) aOrganizationService.Update(aEntityTobeUpdated);
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        public void UpdateEntityCrm2011(ref IOrganizationService aOrganizationService, ref Entity aEntityTobeUpdated)
        {
            try
            {
                if (EXCUTION_FLAG) aOrganizationService.Update(aEntityTobeUpdated);
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        public void UpdateEntityCrm2011(ref IOrganizationService aOrganizationService, Entity aEntityTobeUpdated)
        {
            try
            {
                if (EXCUTION_FLAG) aOrganizationService.Update(aEntityTobeUpdated);
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        public void UpdateEntityDynamics365(ref OrganizationServiceProxy aOrganizationService, ref Entity aEntityTobeUpdated)
        {
            try
            {
                if (aOrganizationService == null)
                    throw new ArgumentNullException(nameof(aOrganizationService), "OrganizationServiceProxy 不能為 null");
                if (aEntityTobeUpdated == null)
                    throw new ArgumentNullException(nameof(aEntityTobeUpdated), "Entity 不能為 null");
                
                if (EXCUTION_FLAG) aOrganizationService.Update(aEntityTobeUpdated);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, $"UpdateEntityDynamics365 錯誤: {e.Message}");
                throw;
            }
        }

        public void UpdateEntityDynamics365(ref OrganizationServiceProxy aOrganizationService, Entity aEntityTobeUpdated)
        {
            try
            {
                if (aOrganizationService == null)
                    throw new ArgumentNullException(nameof(aOrganizationService), "OrganizationServiceProxy 不能為 null");
                if (aEntityTobeUpdated == null)
                    throw new ArgumentNullException(nameof(aEntityTobeUpdated), "Entity 不能為 null");
                
                if (EXCUTION_FLAG) aOrganizationService.Update(aEntityTobeUpdated);
            }
            catch (Exception e)
            {
                TraceByLevel(TOTAL_LEVEL, LEVEL_1, $"UpdateEntityDynamics365 錯誤: {e.Message}");
                throw;
            }
        }

        public async Task UpdateEntityAsync(IOrganizationService aOrganizationService, Entity aEntityTobeUpdated)
        {
            try
            {
                if (EXCUTION_FLAG) aOrganizationService.Update(aEntityTobeUpdated);
            }
            catch (Exception e)
            {
                throw e;
            }
        }
        #endregion

        #region 實體刪除
        public void DeleteEntity(ref IOrganizationService aOrganizationService, String aEntityName, Guid aEntityId)
        {
            try
            {
                if (EXCUTION_FLAG) aOrganizationService.Delete(aEntityName, aEntityId);
            }
            catch (Exception e)
            {
                throw e;
            }
        }

        public void DeleteEntity(String aEntityName, Guid aEntityId)
            => _facade.DeleteEntity(aEntityName, aEntityId);
        #endregion
    }
}

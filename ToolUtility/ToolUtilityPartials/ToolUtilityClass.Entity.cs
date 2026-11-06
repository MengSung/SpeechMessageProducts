/*
 * 檔案整體說明
 *
 * 目的與定位：ToolUtilityClass - 實體操作 (Partial Class 6/10) 包含：Create, Retrieve, Update, Delete 方法
 *
 * 主要功能與流程：
 * 本檔宣告 ToolUtilityClass。
 * 主要操作入口包含 RetrieveEntity、RetrieveEntityDynamics365、RetrieveEntityCrm2011、GetEntityId、CreateEntity、
 * CreateEntityDynamics365、CreateEntityCrm2011、CreateEntityAsync；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
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

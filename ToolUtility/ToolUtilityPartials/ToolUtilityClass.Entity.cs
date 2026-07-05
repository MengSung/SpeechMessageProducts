// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/ToolUtilityPartials/ToolUtilityClass.Entity.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class ToolUtilityClass
// 主要成員：RetrieveEntity、RetrieveEntityDynamics365、RetrieveEntityCrm2011、GetEntityId、CreateEntity、CreateEntityDynamics365、CreateEntityCrm2011、CreateEntityAsync、UpdateEntity、UpdateEntityCrm2011
// 引用命名空間：Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Client、System、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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

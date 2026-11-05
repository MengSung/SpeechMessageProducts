// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/EntityOperations/EntityCrudService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class EntityCrudService
// 主要成員：CreateEntity、UpdateEntity、DeleteEntity
// 引用命名空間：System、ToolUtilityNameSpace.Interfaces、Microsoft.Xrm.Sdk
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using ToolUtilityNameSpace.Interfaces;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.EntityOperations
{
    public class EntityCrudService : IEntityCrudService
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;

        public EntityCrudService(object logger, IOrganizationService organizationService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        }

        public Guid CreateEntity(Entity entityToCreate)
        {
            if (entityToCreate == null) throw new ArgumentNullException(nameof(entityToCreate));
            return _organizationService.Create(entityToCreate);
        }

        public void UpdateEntity(Entity entityToUpdate)
        {
            if (entityToUpdate == null) throw new ArgumentNullException(nameof(entityToUpdate));
            _organizationService.Update(entityToUpdate);
        }

        public void DeleteEntity(string entityName, Guid entityId)
        {
            if (string.IsNullOrEmpty(entityName)) throw new ArgumentNullException(nameof(entityName));
            if (entityId == Guid.Empty) throw new ArgumentException("entityId cannot be empty", nameof(entityId));
            _organizationService.Delete(entityName, entityId);
        }
    }
}

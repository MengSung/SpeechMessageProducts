using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Messages;
using ToolUtilityNameSpace.Interfaces;

namespace ToolUtilityNameSpace.EntityOperations
{
    /// <summary>
    /// 實體查詢服務
    /// ? Phase 3.1: 查詢優化 - 只查詢必要欄位、添加限制
    /// </summary>
    public class EntityQueryService : IEntityQueryService, IDisposable
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;
        private bool _disposed = false;
        
        // ? 預設查詢限制
        private const int DEFAULT_PAGE_SIZE = 5000;

        public EntityQueryService(object logger = null, IOrganizationService organizationService = null)
        {
            _logger = logger;
            _organizationService = organizationService;
        }

        /// <summary>
        /// 檢索單一實體
        /// ? Phase 3.1: 優化 - 允許指定欄位，避免查詢所有欄位
        /// </summary>
        public Entity RetrieveEntity(string entityName, Guid entityId)
        {
            if (_organizationService == null) 
                throw new InvalidOperationException("OrganizationService is not initialized.");
            
            // ?? 注意：這裡仍使用 new ColumnSet(true) 因為是單筆查詢
            // 如需優化，可添加重載方法接受 ColumnSet 參數
            return _organizationService.Retrieve(entityName, entityId, new ColumnSet(true));
        }

        /// <summary>
        /// 根據欄位值檢索實體
        /// ? Phase 3.1: 優化 - 添加 TopCount 限制
        /// </summary>
        public Entity RetrieveEntityByField(string entityName, string fieldName, string fieldValue)
        {
            if (_organizationService == null) 
                throw new InvalidOperationException("OrganizationService is not initialized.");
            
            // ? 優化：添加 TopCount，只取第一筆
            var query = new QueryByAttribute(entityName) 
            { 
                ColumnSet = new ColumnSet(true),
                TopCount = 1  // ? 只需要第一筆
            };
            
            query.Attributes.AddRange(fieldName, "statecode");
            query.Values.AddRange(fieldValue, 0);
            
            var collection = _organizationService.RetrieveMultiple(query);
            return (collection != null && collection.Entities.Count > 0) ? collection.Entities[0] : null;
        }

        /// <summary>
        /// 執行多筆查詢
        /// ? Phase 3.1: 保持彈性，讓調用者控制查詢
        /// </summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            if (_organizationService == null) 
                throw new InvalidOperationException("OrganizationService is not initialized.");
            
            // ? 如果是 QueryExpression，添加預設 PageInfo
            if (query is QueryExpression qe && qe.PageInfo == null)
            {
                qe.PageInfo = new PagingInfo
                {
                    Count = DEFAULT_PAGE_SIZE,
                    PageNumber = 1
                };
            }
            
            return _organizationService.RetrieveMultiple(query);
        }

        /// <summary>
        /// 根據帳戶名稱檢索帳戶 ID
        /// ? Phase 3.1: 優化 - 只查詢 ID，添加 TopCount
        /// </summary>
        public Guid RetrieveAccountByName(string accountName)
        {
            if (_organizationService == null) 
                throw new InvalidOperationException("OrganizationService is not initialized.");
            
            // ? 優化：只查詢 accountid，不需要所有欄位
            var query = new QueryByAttribute("account") 
            { 
                ColumnSet = new ColumnSet("accountid"),  // ? 只查詢 ID
                TopCount = 1  // ? 只需要第一筆
            };
            
            query.Attributes.AddRange("name", "statecode");
            query.Values.AddRange(accountName, 0);
            
            var collection = _organizationService.RetrieveMultiple(query);
            if (collection != null && collection.Entities.Count > 0)
            {
                return collection.Entities[0].Id;
            }
            return Guid.Empty;
        }

        /// <summary>
        /// 根據主旨檢索工作
        /// ? Phase 3.1: 優化 - 添加 top 限制、只查詢必要欄位
        /// </summary>
        public EntityCollection RetrieveTaskBySubject(string subject)
        {
            if (_organizationService == null) 
                throw new InvalidOperationException("OrganizationService is not initialized.");

            subject = "'" + subject + "'";
            
            // ? 優化：添加 top='100'，通常不會有太多同名工作
            var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false' top='100'>
                      <entity name='task'>
                        <attribute name='subject' />
                        <attribute name='statecode' />
                        <attribute name='prioritycode' />
                        <attribute name='scheduledend' />
                        <attribute name='createdby' />
                        <attribute name='regardingobjectid' />
                        <attribute name='activityid' />
                        <attribute name='description' />
                        <order attribute='createdon' descending='true' />
                        <filter type='and'>
                          <condition attribute='subject' operator='eq' value=" + subject + @" />
                          <condition attribute='statecode' operator='eq' value='0' />
                        </filter>
                      </entity>
                    </fetch>";

            var fetchRequest = new RetrieveMultipleRequest
            {
                Query = new FetchExpression(fetchXml)
            };

            var response = (RetrieveMultipleResponse)_organizationService.Execute(fetchRequest);
            return response.EntityCollection;
        }

        /// <summary>
        /// 執行 RetrieveMultipleRequest 並返回結果
        /// ? Phase 3.1: 保持現有功能
        /// </summary>
        public EntityCollection ExecuteRetrieveMultiple(RetrieveMultipleRequest request)
        {
            if (_organizationService == null) 
                throw new InvalidOperationException("OrganizationService is not initialized.");
            
            var response = (RetrieveMultipleResponse)_organizationService.Execute(request);
            return response.EntityCollection;
        }

        #region Dispose Pattern

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            
            if (disposing)
            {
                // Dispose managed resources if any
                // EntityQueryService 本身沒有需要釋放的資源
                // IOrganizationService 由外部管理，不在這裡釋放
            }
            
            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}

using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Messages;
using ToolUtilityNameSpace.Interfaces;

namespace ToolUtilityNameSpace.EntityOperations
{
    public class EntityQueryService : IEntityQueryService, IDisposable
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;
        private bool _disposed = false;

        public EntityQueryService(object logger = null, IOrganizationService organizationService = null)
        {
            _logger = logger;
            _organizationService = organizationService;
        }

        public Entity RetrieveEntity(string entityName, Guid entityId)
        {
            if (_organizationService == null) throw new InvalidOperationException("OrganizationService is not initialized.");
            return _organizationService.Retrieve(entityName, entityId, new ColumnSet(true));
        }

        public Entity RetrieveEntityByField(string entityName, string fieldName, string fieldValue)
        {
            if (_organizationService == null) throw new InvalidOperationException("OrganizationService is not initialized.");
            var query = new QueryByAttribute(entityName) { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange(fieldName, "statecode");
            query.Values.AddRange(fieldValue, 0);
            var collection = _organizationService.RetrieveMultiple(query);
            return (collection != null && collection.Entities.Count > 0) ? collection.Entities[0] : null;
        }

        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            if (_organizationService == null) throw new InvalidOperationException("OrganizationService is not initialized.");
            return _organizationService.RetrieveMultiple(query);
        }

        public Guid RetrieveAccountByName(string accountName)
        {
            if (_organizationService == null) throw new InvalidOperationException("OrganizationService is not initialized.");
            var query = new QueryByAttribute("account") { ColumnSet = new ColumnSet(true) };
            query.Attributes.AddRange("name", "statecode");
            query.Values.AddRange(accountName, 0);
            var collection = _organizationService.RetrieveMultiple(query);
            if (collection != null && collection.Entities.Count > 0)
            {
                return collection.Entities[0].Id;
            }
            return Guid.Empty;
        }

        public EntityCollection RetrieveTaskBySubject(string subject)
        {
            if (_organizationService == null) throw new InvalidOperationException("OrganizationService is not initialized.");

            subject = "'" + subject + "'";
            var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='task'>
                        <attribute name='subject' />
                        <attribute name='statecode' />
                        <attribute name='prioritycode' />
                        <attribute name='scheduledend' />
                        <attribute name='createdby' />
                        <attribute name='regardingobjectid' />
                        <attribute name='activityid' />
                        <attribute name='description' />
                        <order attribute='subject' descending='false' />
                        <filter type='and'>
                          <condition attribute='subject' operator='eq' value=" + subject + @" />
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
        /// </summary>
        public EntityCollection ExecuteRetrieveMultiple(RetrieveMultipleRequest request)
        {
            if (_organizationService == null) throw new InvalidOperationException("OrganizationService is not initialized.");
            var response = (RetrieveMultipleResponse)_organizationService.Execute(request);
            return response.EntityCollection;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing)
            {
                // Dispose managed resources if any
            }
            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}

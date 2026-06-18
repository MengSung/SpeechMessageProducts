#if DEBUG
using System;
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ChurchReport.Diagnostics.Profiling
{
    /// <summary>IOrganizationService 計時裝飾器（僅 Debug）。記錄 entity/操作/ticks 到當前請求 profiler。</summary>
    public sealed class TimedOrganizationService : IOrganizationService
    {
        private readonly IOrganizationService _inner;
        private readonly IHttpContextAccessor _http;

        public TimedOrganizationService(IOrganizationService inner, IHttpContextAccessor http)
        {
            _inner = inner;
            _http = http;
        }

        /// <summary>供 ReleaseConnection 解包，歸還真正連線給連線池。</summary>
        public IOrganizationService Inner => _inner;

        private RequestProfiler Profiler =>
            _http?.HttpContext?.Items != null
            && _http.HttpContext.Items.TryGetValue(RequestProfiler.ItemsKey, out var p)
                ? p as RequestProfiler
                : null;

        private T Time<T>(string entity, string op, Func<T> call)
        {
            var start = Stopwatch.GetTimestamp();
            try { return call(); }
            finally { Profiler?.RecordCrmCall(entity, op, Stopwatch.GetTimestamp() - start); }
        }

        private void Time(string entity, string op, Action call)
        {
            var start = Stopwatch.GetTimestamp();
            try { call(); }
            finally { Profiler?.RecordCrmCall(entity, op, Stopwatch.GetTimestamp() - start); }
        }

        private static string QueryEntity(QueryBase q) =>
            q is QueryExpression qe ? qe.EntityName :
            q is QueryByAttribute qa ? qa.EntityName : "?";

        public Guid Create(Entity entity) =>
            Time(entity?.LogicalName, "Create", () => _inner.Create(entity));

        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) =>
            Time(entityName, "Retrieve", () => _inner.Retrieve(entityName, id, columnSet));

        public EntityCollection RetrieveMultiple(QueryBase query) =>
            Time(QueryEntity(query), "RetrieveMultiple", () => _inner.RetrieveMultiple(query));

        public void Update(Entity entity) =>
            Time(entity?.LogicalName, "Update", () => _inner.Update(entity));

        public void Delete(string entityName, Guid id) =>
            Time(entityName, "Delete", () => _inner.Delete(entityName, id));

        public OrganizationResponse Execute(OrganizationRequest request) =>
            Time(request?.RequestName ?? "Execute", "Execute", () => _inner.Execute(request));

        public void Associate(string entityName, Guid entityId, Relationship relationship,
            EntityReferenceCollection relatedEntities) =>
            Time(entityName, "Associate", () => _inner.Associate(entityName, entityId, relationship, relatedEntities));

        public void Disassociate(string entityName, Guid entityId, Relationship relationship,
            EntityReferenceCollection relatedEntities) =>
            Time(entityName, "Disassociate", () => _inner.Disassociate(entityName, entityId, relationship, relatedEntities));
    }
}
#endif

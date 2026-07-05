// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Diagnostics/Profiling/TimedOrganizationService.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案提供 TimedOrganizationService 相關功能，註解重點在說明檔案責任、上游/下游依賴與維護時不可破壞的行為假設。
// 主要型別：class TimedOrganizationService
// 主要成員：Time、QueryEntity、Create、Retrieve、RetrieveMultiple、Update、Delete、Execute、Associate、Disassociate
// 引用命名空間：System、System.Diagnostics、Microsoft.AspNetCore.Http、Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Query
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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

// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/QueryOperations/QueryService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：interface IQueryService、class QueryService
// 主要成員：RetrieveEntityByField、RetrieveEntityCollectionByField、QueryByFetchXml、QueryBloodReportByContactId、QueryPresentRecordByContactIdAndSunday
// 引用命名空間：Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Query、System、System.Diagnostics、ToolUtilityNameSpace.EntityOperations
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Diagnostics;
using ToolUtilityNameSpace.EntityOperations;

namespace ToolUtilityNameSpace.QueryOperations
{
    /// <summary>
    /// 查詢服務介面
    /// 專責處理各種複雜查詢操作
    /// </summary>
    public interface IQueryService
    {
        /// <summary>透過欄位值檢索實體</summary>
        Entity RetrieveEntityByField(string entityName, string fieldName, string fieldValue);

        /// <summary>透過欄位值檢索實體集合</summary>
        EntityCollection RetrieveEntityCollectionByField(string entityName, string fieldName, string fieldValue);

        /// <summary>透過 FetchXml 查詢</summary>
        EntityCollection QueryByFetchXml(string fetchXml);

        /// <summary>查詢血液報告</summary>
        Entity QueryBloodReportByContactId(Guid contactId);

        /// <summary>查詢出席記錄</summary>
        EntityCollection QueryPresentRecordByContactIdAndSunday(Guid listEntityId, Guid contactId, int monthPeriod);
    }

    /// <summary>
    /// 查詢服務實現
    /// 使用 Repository Pattern 進行資料存取
    /// </summary>
    public class QueryService : IQueryService
    {
        private readonly IEntityRepository _repository;
        private readonly IOrganizationService _organizationService;

        /// <summary>
        /// 建構函數 - 注入依賴
        /// </summary>
        public QueryService(IEntityRepository repository, IOrganizationService organizationService)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        }

        /// <summary>
        /// 透過欄位值檢索實體
        /// </summary>
        public Entity RetrieveEntityByField(string entityName, string fieldName, string fieldValue)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(entityName))
                    throw new ArgumentException("Entity name 不可為空", nameof(entityName));

                if (string.IsNullOrWhiteSpace(fieldName))
                    throw new ArgumentException("Field name 不可為空", nameof(fieldName));

                Trace.WriteLine($"[QueryService] Retrieving {entityName} by {fieldName}={fieldValue}");

                var query = new QueryExpression(entityName)
                {
                    ColumnSet = new ColumnSet(true),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression(fieldName, ConditionOperator.Equal, fieldValue)
                        }
                    },
                    TopCount = 1
                };

                var results = _repository.RetrieveMultiple(query);
                return results.Entities.Count > 0 ? results.Entities[0] : null;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[QueryService] RetrieveEntityByField failed: {ex.Message}");
                throw new InvalidOperationException($"透過 {fieldName} 檢索 {entityName} 失敗", ex);
            }
        }

        /// <summary>
        /// 透過欄位值檢索實體集合
        /// </summary>
        public EntityCollection RetrieveEntityCollectionByField(string entityName, string fieldName, string fieldValue)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(entityName))
                    throw new ArgumentException("Entity name 不可為空", nameof(entityName));

                if (string.IsNullOrWhiteSpace(fieldName))
                    throw new ArgumentException("Field name 不可為空", nameof(fieldName));

                Trace.WriteLine($"[QueryService] Retrieving collection of {entityName} by {fieldName}={fieldValue}");

                var query = new QueryExpression(entityName)
                {
                    ColumnSet = new ColumnSet(true),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression(fieldName, ConditionOperator.Equal, fieldValue)
                        }
                    }
                };

                return _repository.RetrieveMultiple(query);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[QueryService] RetrieveEntityCollectionByField failed: {ex.Message}");
                throw new InvalidOperationException($"透過 {fieldName} 檢索 {entityName} 集合失敗", ex);
            }
        }

        /// <summary>
        /// 透過 FetchXml 查詢
        /// </summary>
        public EntityCollection QueryByFetchXml(string fetchXml)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fetchXml))
                    throw new ArgumentException("FetchXml 不可為空", nameof(fetchXml));

                Trace.WriteLine($"[QueryService] Executing FetchXml query");

                var query = new FetchExpression(fetchXml);
                return _repository.RetrieveMultiple(query);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[QueryService] QueryByFetchXml failed: {ex.Message}");
                throw new InvalidOperationException("FetchXml 查詢失敗", ex);
            }
        }

        /// <summary>
        /// 查詢血液報告
        /// </summary>
        public Entity QueryBloodReportByContactId(Guid contactId)
        {
            try
            {
                if (contactId == Guid.Empty)
                    throw new ArgumentException("Contact ID 不可為空", nameof(contactId));

                Trace.WriteLine($"[QueryService] Querying blood report for contact: {contactId}");

                var query = new QueryExpression
                {
                    EntityName = "new_blood_report",
                    ColumnSet = new ColumnSet(true),
                    Criteria = new FilterExpression(LogicalOperator.And)
                    {
                        Conditions =
                        {
                            new ConditionExpression("new_blood_contact_relation", ConditionOperator.Equal, contactId)
                        }
                    }
                };
                query.AddOrder("createdon", OrderType.Descending);

                var results = _repository.RetrieveMultiple(query);
                return results.Entities.Count > 0 ? results.Entities[0] : null;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[QueryService] QueryBloodReportByContactId failed: {ex.Message}");
                throw new InvalidOperationException("查詢血液報告失敗", ex);
            }
        }

        /// <summary>
        /// 查詢出席記錄
        /// </summary>
        public EntityCollection QueryPresentRecordByContactIdAndSunday(Guid listEntityId, Guid contactId, int monthPeriod)
        {
            try
            {
                if (listEntityId == Guid.Empty)
                    throw new ArgumentException("List Entity ID 不可為空", nameof(listEntityId));

                if (contactId == Guid.Empty)
                    throw new ArgumentException("Contact ID 不可為空", nameof(contactId));

                Trace.WriteLine($"[QueryService] Querying present records for contact: {contactId}, period: {monthPeriod} months");

                var startDate = DateTime.Now.AddMonths(-monthPeriod);

                var query = new QueryExpression("new_individual_meeting_and_spiritual_meditation_records")
                {
                    ColumnSet = new ColumnSet(true),
                    Criteria = new FilterExpression(LogicalOperator.And)
                    {
                        Conditions =
                        {
                            new ConditionExpression("new_contact_present_record", ConditionOperator.Equal, contactId),
                            new ConditionExpression("new_sunday_date", ConditionOperator.GreaterEqual, startDate)
                        }
                    }
                };
                query.AddOrder("new_sunday_date", OrderType.Descending);

                return _repository.RetrieveMultiple(query);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[QueryService] QueryPresentRecordByContactIdAndSunday failed: {ex.Message}");
                throw new InvalidOperationException("查詢出席記錄失敗", ex);
            }
        }
    }
}

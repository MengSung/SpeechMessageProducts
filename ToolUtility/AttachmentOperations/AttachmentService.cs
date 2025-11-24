using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.Interfaces;

namespace ToolUtilityNameSpace.AttachmentOperations
{
    /// <summary>
    /// 附件操作服務 - 處理 CRM 附件的上傳和下載
    /// </summary>
    public class AttachmentService : IAttachmentService
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;

        public AttachmentService(object logger, IOrganizationService organizationService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        }

        /// <summary>
        /// 下載附件 - 根據實體 ID 查詢所有關聯的附註（annotation）
        /// </summary>
        public EntityCollection DownloadAttachment(ref IOrganizationService crmService, Guid entityId)
        {
            try
            {
                // 建立查詢條件：查詢 objectid 等於指定實體 ID 的所有附註
                ConditionExpression condition = new ConditionExpression
                {
                    AttributeName = "objectid",
                    Operator = ConditionOperator.Equal
                };
                condition.Values.Add(entityId.ToString());

                // 建立過濾器
                FilterExpression filter = new FilterExpression
                {
                    FilterOperator = LogicalOperator.And
                };
                filter.Conditions.Add(condition);

                // 建立查詢表達式
                QueryExpression query = new QueryExpression
                {
                    EntityName = "annotation",
                    ColumnSet = new ColumnSet(true),
                    Criteria = filter
                };

                // 執行查詢
                return crmService.RetrieveMultiple(query);
            }
            catch (Exception ex)
            {
                string errorMessage = $"ERROR: DownloadAttachment failed for entity {entityId}. Time: {DateTime.Now}, Description: {ex.Message}";
                throw new InvalidOperationException(errorMessage, ex);
            }
        }

        /// <summary>
        /// 上傳附件 - 創建新的附註（annotation）實體並附加到指定實體
        /// </summary>
        public void UploadAttachment(ref IOrganizationService crmService, string entityName, string subject, string noteText, string fileName, string mimeType, byte[] documentBody, Guid toBeAttachedEntityId)
        {
            try
            {
                // 將文件內容轉換為 Base64 編碼
                string encodedData = Convert.ToBase64String(documentBody);

                // 創建附註實體
                Entity annotation = new Entity("annotation");
                annotation.Attributes["objectid"] = new EntityReference(entityName, toBeAttachedEntityId);
                annotation.Attributes["objecttypecode"] = entityName;
                annotation.Attributes["subject"] = subject;
                annotation.Attributes["notetext"] = noteText;
                annotation.Attributes["documentbody"] = encodedData;
                annotation.Attributes["mimetype"] = mimeType;
                annotation.Attributes["filename"] = fileName;

                // 創建附註
                crmService.Create(annotation);
            }
            catch (Exception ex)
            {
                string errorMessage = $"ERROR: UploadAttachment failed for entity {entityName} ({toBeAttachedEntityId}). Time: {DateTime.Now}, Description: {ex.Message}";
                throw new InvalidOperationException(errorMessage, ex);
            }
        }
    }
}

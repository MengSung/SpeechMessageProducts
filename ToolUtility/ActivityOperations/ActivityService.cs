// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/ActivityOperations/ActivityService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class ActivityService
// 主要成員：GetActivityPartyList、GetActivityPartyIdList、SetActivityStatusToCompleted、SetAppointmentStatusToScheduled
// 引用命名空間：Microsoft.Crm.Sdk.Messages、Microsoft.Xrm.Sdk、Microsoft.Xrm.Sdk.Messages、System、System.Collections、System.Collections.Generic、System.Linq
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace ToolUtilityNameSpace.ActivityOperations
{
    /// <summary>
    /// 活動實體操作服務實作
    /// 處理 CRM 活動實體的常見操作
    /// </summary>
    public class ActivityService : IActivityService
    {
        private readonly object _logger;
        private readonly IOrganizationService _organizationService;

        public ActivityService(object logger, IOrganizationService organizationService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _organizationService = organizationService ?? throw new ArgumentNullException(nameof(organizationService));
        }

        /// <summary>
        /// 取得活動的參與者列表（寄件人或收件人）
        /// </summary>
        public void GetActivityPartyList(Entity activityEntity, string fromOrTo, ArrayList partyList, ArrayList partyTypeList)
        {
            if (activityEntity == null)
                throw new ArgumentNullException(nameof(activityEntity));

            if (partyList == null)
                throw new ArgumentNullException(nameof(partyList));

            if (partyTypeList == null)
                throw new ArgumentNullException(nameof(partyTypeList));

            try
            {
                if (!activityEntity.Contains(fromOrTo))
                    return;

                EntityCollection partyCollection = activityEntity.GetAttributeValue<EntityCollection>(fromOrTo);
                if (partyCollection == null || partyCollection.Entities.Count == 0)
                    return;

                // 先收集所有參與者的引用，避免在迴圈內逐筆呼叫 Retrieve（N+1 問題）
                var partyRefs = new List<EntityReference>();
                foreach (Entity partyEntity in partyCollection.Entities)
                {
                    if (!partyEntity.Contains("partyid"))
                        continue;

                    EntityReference partyReference = (EntityReference)partyEntity["partyid"];
                    partyTypeList.Add(partyReference.LogicalName);
                    partyRefs.Add(partyReference);
                }

                // 按實體類型分組，每組只發一次批次查詢（N 次 → 最多幾次）
                var resultMap = new Dictionary<Guid, Entity>(partyRefs.Count);
                foreach (var group in partyRefs.GroupBy(r => r.LogicalName))
                {
                    var ids = group.Select(r => r.Id).ToList();
                    var query = new Microsoft.Xrm.Sdk.Query.QueryExpression(group.Key)
                    {
                        ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet(true)
                    };
                    query.Criteria.AddCondition(
                        $"{group.Key}id",
                        Microsoft.Xrm.Sdk.Query.ConditionOperator.In,
                        ids.Cast<object>().ToArray());

                    foreach (var entity in _organizationService.RetrieveMultiple(query).Entities)
                        resultMap[entity.Id] = entity;
                }

                // 依原始順序填入結果
                foreach (var reference in partyRefs)
                {
                    if (resultMap.TryGetValue(reference.Id, out var entity))
                        partyList.Add(entity);
                }
            }
            catch (Exception ex)
            {
                string errorMessage = $"ERROR: GetActivityPartyList failed. Time: {DateTime.Now}, Description: {ex.Message}";
                // Log error if logger supports it
                throw new InvalidOperationException(errorMessage, ex);
            }
        }

        /// <summary>
        /// 取得活動的參與者 ID 列表（寄件人或收件人）
        /// </summary>
        public void GetActivityPartyIdList(Entity activityEntity, string fromOrTo, ArrayList partyIdList, ArrayList partyTypeList)
        {
            if (activityEntity == null)
                throw new ArgumentNullException(nameof(activityEntity));

            if (partyIdList == null)
                throw new ArgumentNullException(nameof(partyIdList));

            if (partyTypeList == null)
                throw new ArgumentNullException(nameof(partyTypeList));

            try
            {
                if (!activityEntity.Contains(fromOrTo))
                    return;

                EntityCollection partyCollection = activityEntity.GetAttributeValue<EntityCollection>(fromOrTo);
                if (partyCollection == null || partyCollection.Entities.Count == 0)
                    return;

                foreach (Entity partyEntity in partyCollection.Entities)
                {
                    if (!partyEntity.Contains("partyid"))
                        continue;

                    EntityReference partyReference = (EntityReference)partyEntity["partyid"];

                    // 記錄參與者類型和 ID
                    partyTypeList.Add(partyReference.LogicalName);
                    partyIdList.Add(partyReference.Id);
                }
            }
            catch (Exception ex)
            {
                string errorMessage = $"ERROR: GetActivityPartyIdList failed. Time: {DateTime.Now}, Description: {ex.Message}";
                throw new InvalidOperationException(errorMessage, ex);
            }
        }

        /// <summary>
        /// 將活動狀態設為已完成
        /// </summary>
        public void SetActivityStatusToCompleted(string activityName, Guid activityId)
        {
            SetActivityStatusToCompleted(activityName, activityId, _organizationService);
        }

        /// <summary>
        /// 將活動狀態設為已完成（使用外部服務）
        /// </summary>
        public void SetActivityStatusToCompleted(string activityName, Guid activityId, IOrganizationService organizationService)
        {
            if (string.IsNullOrEmpty(activityName))
                throw new ArgumentNullException(nameof(activityName));

            if (organizationService == null)
                throw new ArgumentNullException(nameof(organizationService));

            try
            {
                // 建立 SetState 請求
                SetStateRequest setStateRequest = new SetStateRequest
                {
                    State = new OptionSetValue(1),      // 1 = Completed
                    Status = new OptionSetValue(4),     // 4 = Completed status code (may vary by activity type)
                    EntityMoniker = new EntityReference(activityName, activityId)
                };

                // 執行請求
                SetStateResponse response = (SetStateResponse)organizationService.Execute(setStateRequest);
            }
            catch (Exception ex)
            {
                string errorMessage = $"ERROR: SetActivityStatusToCompleted failed for {activityName} ({activityId}). Time: {DateTime.Now}, Description: {ex.Message}";
                throw new InvalidOperationException(errorMessage, ex);
            }
        }

        /// <summary>
        /// 將約會狀態設為已排程
        /// </summary>
        public void SetAppointmentStatusToScheduled(Guid activityId)
        {
            SetAppointmentStatusToScheduled(activityId, _organizationService);
        }

        /// <summary>
        /// 將約會狀態設為已排程（使用外部服務）
        /// </summary>
        public void SetAppointmentStatusToScheduled(Guid activityId, IOrganizationService organizationService)
        {
            if (organizationService == null)
                throw new ArgumentNullException(nameof(organizationService));

            try
            {
                // 建立 SetState 請求
                SetStateRequest setStateRequest = new SetStateRequest
                {
                    State = new OptionSetValue(3),      // 3 = Scheduled
                    Status = new OptionSetValue(5),     // 5 = Scheduled status code
                    EntityMoniker = new EntityReference("appointment", activityId)
                };

                // 執行請求
                SetStateResponse response = (SetStateResponse)organizationService.Execute(setStateRequest);
            }
            catch (Exception ex)
            {
                string errorMessage = $"ERROR: SetAppointmentStatusToScheduled failed for appointment ({activityId}). Time: {DateTime.Now}, Description: {ex.Message}";
                throw new InvalidOperationException(errorMessage, ex);
            }
        }
    }
}

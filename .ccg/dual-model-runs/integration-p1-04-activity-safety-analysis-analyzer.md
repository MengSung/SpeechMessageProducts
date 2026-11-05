# CCG analyzer Task: integration-p1-04-activity-safety-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
僅分析 Activity 修正方案，不是 review，不修改檔案或讀取其他檔案。主會話 inline 實作。用戶已免除後續雙模型 REVIEW，分析仍依開發規範嘗試雙後端。既有安全測試51案全紅，覆蓋跨表同GUID、部分發布、錯頁、缺列、分頁及固定例外。方案：保留六API/namespace/ctor/合法append；身份為(logicalname,GUID)，驗整個prefix+new集合；只接受獨占普通ArrayList(拒絕alias/subclass/fixed/read-only)，預配置兩Capacity後用兩個object[] AddRange，無I/O後部分發布，仍不聲稱兩物件跨執行緒原子。最多5000來源容器及輸出列，每批500 IDs，預算非D365實測。保留舊單頁無排序golden；如MoreRecords，先驗probe不能empty/全數仍More，再丟棄probe，重啟正式主鍵升冪有界分頁，PageNumber/cookie正確前進，每頁必有唯一且requested的實體，缺/重/錯表/無進度全拒絕；最多每批ID數頁。不重試SDK例外或寫入。用EntityQueryService驗正式ID/根表；不保存logger，SDK限scope借入，IDisposable只clear不Dispose SDK，開始的同步call由host drain。SetState保留1/4,3/5，拒空ID/null或非SetStateResponse，固定英文wrapper+同原InnerException，交上層共用notifier。不改VM/LINE。請指出方案是否有尚未覆蓋的安全或兼容問題，不能建議清除前綴或降低驗收。來源僅以下安全Activity檔案，不可讀Facade、ToolUtilityClass.Core、appsettings或任何秘密：
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


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.

P1-04下一個Activity切片，唯讀analyzer，繁中輸出可搬條件、需先鎖定的成功行為、新安全差異與測試。只看本文兩個完整來源，不探索Facade/設定/憑證/其他工作樹；不修改程式、不spawn。當前產品尚未更動或搬移。P1仍2/12。
現有授權inline實作，既定雙目標Core(net462/net10)；保留namespace/API與舊ctor，移除未用logger，SDK借用不能Dispose；若需保存SDK只限host授權scope且明確生命週期。參數錯誤維持顯式try前拒絕；真SDK自身ArgumentException不能被catch型別特判誤放行，固定wrapper保留同一InnerException。
原GetActivityPartyList批次group但Dictionary<Guid,Entity>跨表覆寫，先改caller partyTypeList再I/O，缺查詢結果導致type/value不對齊；查詢結果沒驗根表/完整ID/重複/未請求ID/分頁。不可以按名稱Distinct/GroupBy刪資料。from/to中缺partyid可能合法addressused外部收件者，不能未研究就改全部拒絕。兩個ArrayList出參不能原子swap參照，應明載caller獨占/不可並行enumerate，先建局部candidate再正常成功append；不能宣稱對任意自訂ArrayList/OOM也可跨兩物件原子發布。
請評估異質參與者logicalname+GUID的穩定身份（同GUID不同表不可覆寫），重複ref對同一消費集合的拒絕，回傳SDK Entity caller所有權/是否共用別名/原本前綴資料驗證。限量、batch與MoreRecords策略要有可核實依據而不是無界分頁或任意猜D365限制。不得為維持舊成功路徑接受缺ID/重複/partial或私密診斷。
SetState兩入口都有default及externalSDK overload，保護EntityMoniker/State=1 Status=4與appointment State=3 Status=5，並保留真故障。不要臆改CRM狀態碼、取代業務冪等或做實機寫入。
附件切片已31ab8cea保存，2272/2272、Core99.48/100；本次不是附件重新審查。Claude週額度至2026-11-07 16:00台北，入口仍嘗試兩模型，若Gemini成功可標降級，但不得稱完整雙模型。

## ToolUtility/ActivityOperations/ActivityService.cs
```csharp
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

```

## ToolUtility/ActivityOperations/IActivityService.cs
```csharp
// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/ActivityOperations/IActivityService.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：interface IActivityService
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：Microsoft.Xrm.Sdk、System、System.Collections
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Xrm.Sdk;
using System;
using System.Collections;

namespace ToolUtilityNameSpace.ActivityOperations
{
    /// <summary>
    /// 活動實體操作服務介面 (Activity Operations)
    /// 處理 CRM 活動實體的常見操作，如取得收件人/寄件人、變更狀態等
    /// </summary>
    public interface IActivityService
    {
        /// <summary>
        /// 取得活動的參與者列表（寄件人或收件人）
        /// </summary>
        /// <param name="activityEntity">活動實體</param>
        /// <param name="fromOrTo">欄位名稱 (from 或 to)</param>
        /// <param name="partyList">輸出參數：參與者實體列表</param>
        /// <param name="partyTypeList">輸出參數：參與者類型列表</param>
        void GetActivityPartyList(Entity activityEntity, string fromOrTo, ArrayList partyList, ArrayList partyTypeList);

        /// <summary>
        /// 取得活動的參與者 ID 列表（寄件人或收件人）
        /// </summary>
        /// <param name="activityEntity">活動實體</param>
        /// <param name="fromOrTo">欄位名稱 (from 或 to)</param>
        /// <param name="partyIdList">輸出參數：參與者 ID 列表</param>
        /// <param name="partyTypeList">輸出參數：參與者類型列表</param>
        void GetActivityPartyIdList(Entity activityEntity, string fromOrTo, ArrayList partyIdList, ArrayList partyTypeList);

        /// <summary>
        /// 將活動狀態設為已完成
        /// </summary>
        /// <param name="activityName">活動邏輯名稱（如 phonecall, email, appointment 等）</param>
        /// <param name="activityId">活動 ID</param>
        void SetActivityStatusToCompleted(string activityName, Guid activityId);

        /// <summary>
        /// 將約會狀態設為已排程
        /// </summary>
        /// <param name="activityId">約會 ID</param>
        void SetAppointmentStatusToScheduled(Guid activityId);

        /// <summary>
        /// 將活動狀態設為已完成（使用外部服務）
        /// </summary>
        /// <param name="activityName">活動邏輯名稱</param>
        /// <param name="activityId">活動 ID</param>
        /// <param name="organizationService">外部組織服務</param>
        void SetActivityStatusToCompleted(string activityName, Guid activityId, IOrganizationService organizationService);

        /// <summary>
        /// 將約會狀態設為已排程（使用外部服務）
        /// </summary>
        /// <param name="activityId">約會 ID</param>
        /// <param name="organizationService">外部組織服務</param>
        void SetAppointmentStatusToScheduled(Guid activityId, IOrganizationService organizationService);
    }
}

```

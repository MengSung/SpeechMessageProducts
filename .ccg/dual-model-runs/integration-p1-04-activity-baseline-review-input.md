唯讀reviewer審查Activity抽取前的特性測試準備，不探索其他檔案、設定或憑證、不修改程式。這是主P1-04接續，不宣告Activity已搬或已修安全缺陷。
六案仍在原hostActivityService執行綠：group/query golden及回應逆序還原、同名不同ID保留與現有前綴不Clear、無partyid地址skip；純ID無I/O；活動1/4及約會3/5，各default/external SDK一組。只替代SDK，不測實機或授權。
既有已知跨表同GUID覆寫、先改type再I/O部分發布仍待安全紅綠修復；不得把這些缺陷鎖進golden或把新測試綠當修復。
同時CoreTargetFrameworkTests只修XML文件使之符合已驗證nullable行為；執行碼不變。局部15/15=6Activity+9CoreTarget，net48CoreTarget1/1。
請核對新測試是否真正驗產品query與結果（非只驗mock呼叫）、是否洩漏／額外I/O、是否無意把缺陷當相容；預設完整review仍嘗試Gemini/Claude，Claude週額度時僅單模型降級。

## ToolUtility/ActivityOperations/ActivityService.cs
```
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

## ToolUtility.Tests/ActivityOperations/ActivityCharacterizationTests.cs
```
using System.Collections;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.ActivityOperations;
using Xunit;

namespace ToolUtility.Tests.ActivityOperations
{
    /// <summary>
    /// 在Activity仍位於原host時鎖定三類成功行為：分組查詢／順序、純ID解析、兩組狀態請求。
    /// 只替代SDK，所有ID與內容為合成值，不連D365、不發LINE；不把已知安全缺陷當作相容行為。
    /// </summary>
    public sealed class ActivityCharacterizationTests
    {
        private static readonly Guid FirstId = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid UserId = new Guid("22222222-2222-2222-2222-222222222222");
        private static readonly Guid LastId = new Guid("33333333-3333-3333-3333-333333333333");

        /// <summary>
        /// contact/systemuser分組各查一次AllColumns，回應逆序仍依原參與者順序append並保留既有前綴。
        /// 沒有partyid但帶addressused的外部收件者沿用略過語義；同名不同正式ID都保留，借入SDK不Dispose。
        /// </summary>
        [Fact]
        public void PartyList_PreservesBatchGoldenOrderAndExistingPrefix()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var disposable = sdk.As<IDisposable>();
            var first = new Entity("contact", FirstId) { ["fullname"] = "合成同名" };
            var user = new Entity("systemuser", UserId) { ["fullname"] = "合成使用者" };
            var last = new Entity("contact", LastId) { ["fullname"] = "合成同名" };
            var queries = new List<XElement>();
            sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase input) =>
            {
                var query = Assert.IsType<QueryExpression>(input);
                var condition = Assert.Single(query.Criteria.Conditions);
                queries.Add(new XElement("query", new XAttribute("entity", query.EntityName),
                    new XAttribute("all-columns", query.ColumnSet.AllColumns),
                    new XAttribute("columns", query.ColumnSet.Columns.Count),
                    new XElement("condition", new XAttribute("field", condition.AttributeName),
                        new XAttribute("operator", condition.Operator),
                        condition.Values.Select(value => new XElement("id", Assert.IsType<Guid>(value))))));
                Assert.Empty(query.LinkEntities);
                Assert.Empty(query.Orders);
                Assert.Null(query.TopCount);
                return query.EntityName == "contact"
                    ? new EntityCollection(new[] { last, first }) : new EntityCollection(new[] { user });
            });
            var prefix = new Entity("contact", new Guid("44444444-4444-4444-4444-444444444444"));
            var parties = new ArrayList { prefix };
            var types = new ArrayList { "contact" };
            new ActivityService(new object(), sdk.Object).GetActivityPartyList(Activity(), "to", parties, types);
            GoldenQueryAssertions.Equal("activity-party-query.xml", new XElement("queries", queries).ToString());
            Assert.Equal(new[] { prefix, first, user, last }, parties.Cast<Entity>());
            Assert.Equal(new[] { "contact", "contact", "systemuser", "contact" }, types.Cast<string>());
            Assert.Same(first, parties[1]);
            Assert.Same(last, parties[3]);
            sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Exactly(2));
            disposable.Verify(d => d.Dispose(), Times.Never);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 純ID入口不查SDK，順序及型別與ID一一對齊，原前綴不被Clear。
        /// 缺partyid的外部地址不偽造資料庫ID；這不表示本文已驗證寄送或外部收件者授權。
        /// </summary>
        [Fact]
        public void PartyIds_PreserveOrderPrefixAndSkipUnresolvedAddressWithoutIo()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var prefix = new Guid("44444444-4444-4444-4444-444444444444");
            var ids = new ArrayList { prefix };
            var types = new ArrayList { "contact" };
            new ActivityService(new object(), sdk.Object).GetActivityPartyIdList(Activity(), "to", ids, types);
            Assert.Equal(new[] { prefix, FirstId, UserId, LastId }, ids.Cast<Guid>());
            Assert.Equal(new[] { "contact", "contact", "systemuser", "contact" }, types.Cast<string>());
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// default/external兩多載實際只呼叫所選SDK一次；鎖定活動1/4、約會3/5與完整EntityMoniker。
        /// 不臆測不同組織的狀態碼，不更動任何真實活動，不把SDK所有權轉交給工具。
        /// </summary>
        /// <param name="appointment">是否使用約會排程請求。</param>
        /// <param name="external">是否使用明確外部SDK多載。</param>
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void SetState_PreservesGoldenAndSelectedSdk(bool appointment, bool external)
        {
            var ctorSdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var externalSdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var selected = external ? externalSdk : ctorSdk;
            var other = external ? ctorSdk : externalSdk;
            var disposable = selected.As<IDisposable>();
            selected.Setup(s => s.Execute(It.IsAny<OrganizationRequest>())).Callback<OrganizationRequest>(input =>
            {
                var request = Assert.IsType<SetStateRequest>(input);
                Assert.Equal(3, request.Parameters.Count);
                var xml = new XElement("set-state", new XAttribute("request", request.RequestName),
                    new XAttribute("entity", request.EntityMoniker.LogicalName),
                    new XAttribute("id", request.EntityMoniker.Id),
                    new XAttribute("state", request.State.Value),
                    new XAttribute("status", request.Status.Value));
                GoldenQueryAssertions.Equal(appointment ? "activity-schedule.xml" : "activity-complete.xml", xml.ToString());
            }).Returns(new SetStateResponse());
            var service = new ActivityService(new object(), ctorSdk.Object);
            if (appointment)
            {
                if (external) service.SetAppointmentStatusToScheduled(FirstId, externalSdk.Object);
                else service.SetAppointmentStatusToScheduled(FirstId);
            }
            else
            {
                if (external) service.SetActivityStatusToCompleted("email", FirstId, externalSdk.Object);
                else service.SetActivityStatusToCompleted("email", FirstId);
            }
            selected.Verify(s => s.Execute(It.IsAny<OrganizationRequest>()), Times.Once);
            disposable.Verify(d => d.Dispose(), Times.Never);
            selected.VerifyNoOtherCalls();
            other.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 合成參與者含同名但不同ID聯絡人及外部地址；partyid參照既有資料庫記錄，
        /// activityparty容器只供解析、不作持久化列發布或補造ID。
        /// </summary>
        private static Entity Activity()
        {
            return new Entity("email", new Guid("55555555-5555-5555-5555-555555555555"))
            {
                ["to"] = new EntityCollection(new[]
                {
                    new Entity("activityparty") { ["partyid"] = new EntityReference("contact", FirstId) },
                    new Entity("activityparty") { ["addressused"] = "synthetic@example.invalid" },
                    new Entity("activityparty") { ["partyid"] = new EntityReference("systemuser", UserId) },
                    new Entity("activityparty") { ["partyid"] = new EntityReference("contact", LastId) }
                })
            };
        }
    }
}

```

## ToolUtility.Tests\Golden\ToolUtility\activity-complete.xml
```
<set-state request="SetState" entity="email" id="11111111-1111-1111-1111-111111111111" state="1" status="4" />

```

## ToolUtility.Tests\Golden\ToolUtility\activity-party-query.xml
```
<queries>
  <query entity="contact" all-columns="true" columns="0">
    <condition field="contactid" operator="In">
      <id>11111111-1111-1111-1111-111111111111</id>
      <id>33333333-3333-3333-3333-333333333333</id>
    </condition>
  </query>
  <query entity="systemuser" all-columns="true" columns="0">
    <condition field="systemuserid" operator="In">
      <id>22222222-2222-2222-2222-222222222222</id>
    </condition>
  </query>
</queries>

```

## ToolUtility.Tests\Golden\ToolUtility\activity-schedule.xml
```
<set-state request="SetState" entity="appointment" id="11111111-1111-1111-1111-111111111111" state="3" status="5" />

```

## 文件校正diff
```diff
diff --git a/ToolUtility.Tests/CoreTargetFrameworkTests.cs b/ToolUtility.Tests/CoreTargetFrameworkTests.cs
index c6fca3f8..324ac9da 100644
--- a/ToolUtility.Tests/CoreTargetFrameworkTests.cs
+++ b/ToolUtility.Tests/CoreTargetFrameworkTests.cs
@@ -58,7 +58,8 @@ namespace ToolUtility.Tests
         /// <summary>
         /// 實際反射抽取介面的公開 nullable 中繼資料，而非以檔頭是否有 pragma 判斷。
         /// Core strict 專案統一啟用 Nullable；若後續設定漂移成 oblivious，任一字串參數即決定性失敗。
-        /// 屬性讀取的 attributeName 依舊允許 null，其餘字串必須維持非 null 契約。
+        /// 屬性讀取的 attributeName 與附件四個原樣傳遞 metadata 依舊允許 null；
+        /// 其他字串（包含附件 parent entityName）必須維持非 null 契約。
         /// 不需修改產品公開 API 或新增重複 pragma 來配合靜態審查推測。
         /// </summary>
         [Theory]

```

/*
檔案整體說明
目的與定位：保護 FS 只有 UI 差異時仍須核對同實體的已發布結構，不能因 schema 操作為零，
就以實體粒度 PublishXml 順帶發布未受審的欄位變更。
主要流程：先用既有嚴格記憶體 Store 完成合成結構，再加一個 account 公用檢視；
以正式 SchemaDeploymentEngine 產生 UI 計畫，獨立隱藏已發布的既有欄位作故障注入。
決定性斷言：原 plan 結構操作確實為零、UI 必須寫入，apply 卻必須在任何 UI 建立、
metadata 寫入或定向發布前以 InvalidDataException 拒絕，且不得先建立 before 檔案。
空 UI 來源在假 SDK 邊界辨識表單 FetchXML 並依原始查詢包裝 alias，其他查詢保留
原 Store 委派與平台方案條件；不新增元件或改變未發布結構故障，避免查詢轉型造成假拒絕。
資源與隔離：每案唯一 Fixture 擁有暫存根及合成資料庫；服務轉接器只借用記憶體 Store，
不讀 Secrets、不連組織、不持有 client、快取、Task 或 timer，所有檔案隨 using Fixture 清除。
限制與驗證：這是發布邊界的離線故障案例，不是已發布元件或測試環境部署成功的證據；
真正 Live3P 與 UI 匯出入必須由主流程在獨占測試環境鎖下另行執行。
*/
using System;
using System.IO;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Schema;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Ui;

/// <summary>UI 發布與 schema metadata 的共同信任邊界；不因 UI 來源完整便視為結構已受審。</summary>
public sealed class SchemaUiPublicationBoundaryTests
{
    /// <summary>
    /// 已發布 account 缺少候選已有的欄位，原 schema plan 仍零操作。
    /// UI-only apply 必須在第一個 UI 寫入前拒絕，保留原未發布狀態及既有正式 metadata。
    /// </summary>
    [Fact]
    public void Apply_OnlyUiChanges_UnpublishedSchemaOnPublishedEntity_RefusesEveryWrite()
    {
        using var fixture = new SchemaEngineTests.Fixture();
        _ = fixture.Engine.Apply(fixture.Engine.Plan(fixture.Definition, default), fixture.Definition, default);
        fixture.Service.MissingPublishedField = true;
        var service = new UiOnlyService(fixture.Service);
        var catalog = OrganizationCatalog.LoadDefault();
        var definition = fixture.Definition with
        {
            Views = new[]
            {
                new SchemaView
                {
                    Entity = "account", Name = "Synthetic account view",
                    Columns = new[] { "new_defaultlanguage" }
                }
            }
        };
        var directory = Path.Combine(fixture.Directory, "ui-only");
        var engine = new SchemaDeploymentEngine(service, directory, new SchemaEngineTests.Clock(),
            catalog.GetTest("construction"), catalog);
        var plan = engine.Plan(definition, default);
        Assert.Empty(plan.Operations);
        Assert.True(plan.Ui!.Changes > 0);
        Assert.Contains(fixture.Service.Entities.Single(row => row.LogicalName == "account").Attributes,
            field => field.LogicalName == "new_defaultlanguage");
        var writes = fixture.Service.WriteCount;
        var publishes = fixture.Service.Publishes;

        Assert.Throws<InvalidDataException>(() => engine.Apply(plan, definition, default));

        Assert.Equal(0, service.UiWrites);
        Assert.Equal(writes, fixture.Service.WriteCount);
        Assert.Equal(publishes, fixture.Service.Publishes);
        Assert.False(Directory.Exists(directory));
    }

    /// <summary>
    /// 只有 metadata 與空 UI 盤點能力的合成轉接器；任何平台 UI 建立立即留下可觀察計數再故障，
    /// 使案例不會把晚於寫入的其他錯誤誤認為正確的發布前拒絕。沒有外部資源所有權。
    /// </summary>
    private sealed class UiOnlyService(SchemaEngineTests.Store inner) : IOrganizationService
    {
        /// <summary>第一個 UI 副作用的決定性計數；成功拒絕必須為零。</summary>
        internal int UiWrites { get; private set; }

        /// <summary>
        /// 精確合成新組織身分與空未發布 UI；表單FetchXML先經共用轉接器辨識，再按原始查詢包裝空頁。
        /// 其他metadata保留Store的真實投影與故障，回應僅屬本次呼叫，不建立額外服務或保留來源。
        /// </summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            // 官方唯讀轉換只模擬型別對應；原讀取／分頁／故障注入仍由既有替身負責。
            if (UiFormFetchTestAdapter.Convert(request) is { } converted) return converted;
            if (request is RetrieveCurrentOrganizationRequest)
            {
                var response = (RetrieveCurrentOrganizationResponse)inner.Execute(request);
                response.Detail.UniqueName = "construction";
                return response;
            }
            if (request is RetrieveUnpublishedMultipleRequest unpublished)
            {
                var query = unpublished.Query;
                _ = UiFormFetchTestAdapter.Query(query);
                return new RetrieveUnpublishedMultipleResponse
                { Results = { ["EntityCollection"] = UiFormFetchTestAdapter.Wrap(query, new EntityCollection()) } };
            }
            return inner.Execute(request);
        }

        /// <summary>
        /// 只提供本案例未存在的 UI 與平台 UI 方案；表單FetchXML還原後沿用原表名及方案條件，
        /// 空回應按原始查詢包裝。原core方案及其他metadata繼續由Store決定，不能擴大空資料白名單。
        /// </summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var expression = UiFormFetchTestAdapter.Query(query);
            if (expression.EntityName is "savedquery" or "systemform" ||
                expression.EntityName == "solution" && expression.Criteria.Conditions.Any(condition =>
                    condition.AttributeName == "uniquename" && condition.Values.Contains(UiViewDeployment.SolutionName)))
                return UiFormFetchTestAdapter.Wrap(query, new EntityCollection());
            return inner.RetrieveMultiple(query);
        }

        /// <summary>預期產品在此之前拒絕；若抵達即記錄副作用，不能以後續失敗掩蓋。</summary>
        public Guid Create(Entity entity)
        {
            UiWrites++;
            throw new InvalidOperationException("Unexpected synthetic UI write.");
        }

        /// <summary>保留組織語言與正式 ID 讀取，不攔截 metadata 失敗。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => inner.Retrieve(entityName, id, columnSet);
        /// <summary>預期沒有更新，意外寫入仍交嚴格 Store 拒絕。</summary>
        public void Update(Entity entity) => inner.Update(entity);
        /// <summary>本案不允許刪除現有元件。</summary>
        public void Delete(string entityName, Guid id) => inner.Delete(entityName, id);
        /// <summary>本案不增加 SDK 關聯能力。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship,
            EntityReferenceCollection relatedEntities) => inner.Associate(entityName, entityId, relationship, relatedEntities);
        /// <summary>本案不增加 SDK 解除關聯能力。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship,
            EntityReferenceCollection relatedEntities) => inner.Disassociate(entityName, entityId, relationship, relatedEntities);
    }
}

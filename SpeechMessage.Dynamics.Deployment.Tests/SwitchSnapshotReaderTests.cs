/*
檔案整體說明
目的與定位：P7完整metadata讀取的離線契約，以既有部署fixture建立真受管步驟，再用唯讀SDK轉接補WhoAmI及舊活動metadata。
主要流程：reader不得把Live設定／enabled差異誤判成DLL未部署；必須拒絕任意未知設定，指紋涵蓋舊state及原XAML。
資源與隔離：fixture持有唯一temp根與假SDK，沒有網路或真秘密；測試轉接只供合成metadata，不擴張既有替身期望。
決定性斷言：實際reader產出的正式ID、完整指紋變化、secure不讀值及未知設定拒絕；讀取流程不允許SDK寫入。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Switching;
using Xunit;
namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>真reader與正式lookup的離線測試，不以純DTO冒充SDK完整性。</summary>
public sealed class SwitchSnapshotReaderTests
{
    /// <summary>Live狀態本來就不同manifest的off，reader仍接受已就緒DLL；原XAML改字會改指紋。</summary>
    [Fact]
    public void Reader_LiveStateAndWorkflowChanges_UsesCompleteFingerprint()
    {
        using var f = new Fixture();
        var manifest = f.Manifest with { Steps = new[] { f.Manifest.Steps[0] with { Module = "attendance", Live = new StepLiveRegistration(), UnsecureConfig = "{\"mode\":\"Shadow\"}" } } };
        f.Engine.Apply(f.Engine.Plan(manifest, f.Image, Fixture.Organization), manifest, f.Image);
        var step = f.Service.Rows.Single(row => row.LogicalName == "sdkmessageprocessingstep");
        step["configuration"] = "{\"mode\":\"Live\"}";
        step["statecode"] = new OptionSetValue(0);
        var sdk = new ReadService(f.Service);
        var workflow = new Entity("workflow", Guid.NewGuid()) { ["type"] = new OptionSetValue(1), ["statecode"] = new OptionSetValue(0),
            ["xaml"] = "<Activity><Node AssemblyQualifiedName=\"Old.Activity, Old.Assembly\"/></Activity>" };
        f.Service.Seed(workflow);
        var definition = OrganizationCatalog.LoadDefault().TestOrganization;
        var document = new RegistrationDocument { Assemblies = new[] { manifest } };
        var first = SwitchSnapshotReader.Read(sdk, document, new[] { f.Image }, definition, "manifest", "catalog", f.Root, OrganizationCatalog.LoadDefault(), CancellationToken.None);
        Assert.Equal(step.Id, Assert.Single(first.Steps).Id);
        Assert.True(first.Steps[0].Enabled);
        workflow["xaml"] = "<Activity> <Node AssemblyQualifiedName=\"Old.Activity, Old.Assembly\"/></Activity>";
        var second = SwitchSnapshotReader.Read(sdk, document, new[] { f.Image }, definition, "manifest", "catalog", f.Root, OrganizationCatalog.LoadDefault(), CancellationToken.None);
        Assert.NotEqual(first.Fingerprint, second.Fingerprint);
    }

    /// <summary>未知unsecure設定可能含秘密，必須拒絕且不把原值寫log，而非備份原文後繼續。</summary>
    [Fact]
    public void Reader_UnmanagedConfiguration_RefusedBeforePublication()
    {
        using var f = new Fixture();
        var manifest = f.Manifest with { Steps = new[] { f.Manifest.Steps[0] with { Module = "attendance", Live = new StepLiveRegistration(), UnsecureConfig = "{\"mode\":\"Shadow\"}" } } };
        f.Engine.Apply(f.Engine.Plan(manifest, f.Image, Fixture.Organization), manifest, f.Image);
        f.Service.Rows.Single(row => row.LogicalName == "sdkmessageprocessingstep")["configuration"] = "{\"mode\":\"Shadow\",\"unknown\":\"SYNTHETIC-SECRET\"}";
        Assert.Throws<InvalidDataException>(() => SwitchSnapshotReader.Read(new ReadService(f.Service), new RegistrationDocument { Assemblies = new[] { manifest } },
            new[] { f.Image }, OrganizationCatalog.LoadDefault().TestOrganization, "manifest", "catalog", f.Root, OrganizationCatalog.LoadDefault(), CancellationToken.None));
    }

    /// <summary>只讀轉接：WhoAmI提供合成ID，plugintype的activity欄位由此案例明確補false，其他行為沿原SDK。</summary>
    internal sealed class ReadService(MemoryOrganization inner) : IOrganizationService
    {
        internal Guid ServerId { get; set; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
        /// <summary>內層獨立schema未涵蓋isworkflowactivity；此轉接只對該metadata查詢投影完整合成列。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            if (query is QueryExpression expression && expression.EntityName is "plugintype" or "workflow")
            {
                var candidates = inner.Rows.Where(row => row.LogicalName == expression.EntityName).Where(row => expression.Criteria.Conditions.All(condition =>
                {
                    object? actual = row.Contains(condition.AttributeName) ? row[condition.AttributeName] : condition.AttributeName == row.LogicalName + "id" ? row.Id : null;
                    if (actual is EntityReference reference) actual = reference.Id;
                    if (actual is OptionSetValue option) actual = option.Value;
                    return condition.Operator switch { ConditionOperator.Equal => Equals(actual, condition.Values[0]), ConditionOperator.In => condition.Values.Contains(actual), _ => throw new NotSupportedException() };
                })).Select(row =>
                {
                    var copy = new Entity(row.LogicalName, row.Id);
                    foreach (var field in expression.ColumnSet.Columns) if (row.Contains(field)) copy[field] = row[field];
                    return copy;
                }).ToList();
                return new EntityCollection(candidates);
            }
            return inner.RetrieveMultiple(query);
        }
        /// <summary>WhoAmI只回正式org ID，其餘執行仍由原假SDK處理。</summary>
        public OrganizationResponse Execute(OrganizationRequest request) => request.RequestName == "WhoAmI"
            ? new OrganizationResponse { Results = { ["OrganizationId"] = ServerId } } : inner.Execute(request);
        /// <summary>精確讀取沿假SDK；不存在即失敗。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => inner.Retrieve(entityName, id, columnSet);
        /// <summary>唯讀reader若意外建立就失敗。</summary>
        public Guid Create(Entity entity) => throw new NotSupportedException();
        /// <summary>唯讀reader若意外更新就失敗。</summary>
        public void Update(Entity entity) => throw new NotSupportedException();
        /// <summary>唯讀reader若意外刪除就失敗。</summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        /// <summary>唯讀reader不得關聯。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>唯讀reader不得解除關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}

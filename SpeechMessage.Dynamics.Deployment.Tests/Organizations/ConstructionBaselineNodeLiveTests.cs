/*
檔案整體說明
目的與定位：FS construction 2026-11-28 23:10失敗apply的固定唯讀診斷入口，完整使用
首寫前67,291節點baseline，不重設基準、不發布、不修guard、不自動授權任何後續apply。
主要功能與流程：綁原plan／before／完整baseline三份SHA與組織ID及schema hash，重驗原計畫；
依原清單用原引擎純工廠重建新增欄／關聯／鍵候選，只以正式元件ID補入，讀兩來源完整
guard投影後比較原baseline＋核定增量。輸出僅metadata路徑、型別、種類與是否計畫元件。
使用邊界與維護：只固定construction測試組織；拒絕其他線正在D365寫入時驗收。ProductionGuard
驗真身分與一致性鎖後才讀資料，service包裝只准RetrieveEntity(All)，所有寫入立即拒絕。
每案五分鐘CTS，SDK兩分鐘傳輸上限；同步返回後依序釋放租約、組織鎖、組態、CTS及
共用診斷owner，沒有背景工作、共享可變快取或跨組織憑證。只記本機安全錯誤、不發LINE。
失敗與驗證：SHA、範圍、型別、正式ID或預算不符皆整案失敗，不回部分成功。baseline純值
還原已有離線RED→GREEN；實機只在明確Category=Live3P、WP=FSConstructionDiagnostics執行，
成功只代表安全差異報告產生，不代表FS完成、原apply成功或具有更多寫入額度。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using PowerPlatform.Dataverse.Client;
using SpeechMessage.Dynamics.Deployment.Schema;
using SpeechMessage.Dynamics.Deployment.Tests.Ui;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;
using Xunit.Abstractions;
namespace SpeechMessage.Dynamics.Deployment.Tests.Organizations;

/// <summary>固定construction事件唯讀入口，測試發現及建構沒有網路、秘密讀取或檔案副作用。</summary>
[Trait("Category", "Live3P")]
[Trait("WP", "FSConstructionDiagnostics")]
public sealed class ConstructionBaselineNodeLiveTests
{
    /// <summary>框架擁有的安全摘要能力，不能保存SDK、組織狀態或機密。</summary>
    private readonly ITestOutputHelper output;
    /// <summary>只借用框架輸出，所有執行期資源由測試案例唯一擁有並排空。</summary>
    /// <param name="output">框架管理的輸出通道。</param>
    public ConstructionBaselineNodeLiveTests(ITestOutputHelper output) => this.output = output;

    /// <summary>讀真完整原基準加候選的節點差異，零SDK寫入且不作授權判定。</summary>
    /// <returns>完整安全報告flush後完成，任一故障保留真實例外交共用本機診斷。</returns>
    [Fact]
    public async Task ReadOnly_ConstructionSavedBaseline_ListsExactPlanDeltaDifferences()
    {
        var common = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SpeechMessage");
        var evidence = Path.Combine(common, "evidence", "FS-construction-node-readonly", Guid.NewGuid().ToString("N"));
        ExceptionDiagnostics? diagnostics = null; Exception? failure = null;
        try
        {
            diagnostics = new ExceptionDiagnostics(Path.Combine(evidence, "Logs"),
                outputOptions: new ExceptionOutputOptions(writeExceptionLog: true, sendLine: false));
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5)); var ct = timeout.Token;
            if (File.Exists(Path.Combine(common, "locks", "test-env-d365.lock")) || File.Exists(Path.Combine(common, "locks", "test-env.lock")))
                throw new InvalidOperationException("Readonly evidence refused during D365 writes.");
            var repository = DeploymentCli.FindRepositoryRoot();
            var orgArtifacts = Path.Combine(repository, "artifacts", "d365", "construction");
            var original = Path.Combine(orgArtifacts, "20261128-151031-5798217-5365171249c6484abf8545143062cad5");
            var planPath = Path.Combine(orgArtifacts, "20261128-144138-9696776-fd11a0e3fe964f55992ded00b0e1ec0e", "schema-plan.json");
            var beforePath = Path.Combine(original, "before-metadata.json"); var baselinePath = Path.Combine(original, "before-ui-metadata-baseline.json");
            const string planHash = "19D6AF4D72203A3F0428FC074ECD573864796476AF8126C8AC8E5EB1F621B61C";
            const string beforeHash = "C9E8A7E3518B5780921934DD20F98FF5A9B2C49003819CC2BC12DE9438F13817";
            const string baselineHash = "B77BBB161BBD6A93E0EB53C3B9D9176A7140F01ADCC0F6B6F3AC02A2C43D863A";
            UiMetadataBaselineFile.VerifySha256(planPath, planHash, ct);
            UiMetadataBaselineFile.VerifySha256(beforePath, beforeHash, ct);
            UiMetadataBaselineFile.VerifySha256(baselinePath, baselineHash, ct);
            var plan = SchemaFiles.ReadPlan(planPath); var before = SchemaFiles.ReadBeforeSnapshot(beforePath);
            var definition = SchemaFiles.LoadForOrganization(repository, "construction"); var catalog = OrganizationCatalog.LoadDefault();
            SchemaPlanner.ValidatePlan(plan, definition, before, catalog);
            using var document = JsonDocument.Parse(File.ReadAllBytes(baselinePath)); var saved = document.RootElement;
            if (plan.Organization != "construction" || before.Organization != "construction" || plan.Operations.Count != 274 ||
                saved.GetProperty("formatVersion").GetInt32() != 1 || !saved.GetProperty("publishedAndUnpublishedEquivalent").GetBoolean() ||
                saved.GetProperty("organizationId").GetGuid() != plan.OrganizationId ||
                saved.GetProperty("planSha256").GetString() != plan.Sha256 || saved.GetProperty("schemaHash").GetString() != plan.SchemaHash)
                throw new InvalidDataException("Historical construction binding rejected.");
            var baseline = new UiMetadataPublicationGuard.Snapshot(BaselineNodeReplayProbe.RestoreScalars(saved.GetProperty("values")));
            var roots = baseline.Values.Keys.Where(key => !key.Contains('.', StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
            if (baseline.Values.Count != 67291 || !roots.SequenceEqual(new[] { "account", "contact" }, StringComparer.Ordinal))
                throw new InvalidDataException("Historical full baseline scope rejected.");
            var target = catalog.GetTest("construction");
            _ = ProductionGuard.Authorize(["verify", "--org", target.Name], catalog, null, ct);
            var configuration = DeploymentConfiguration.Create(typeof(UserSecretsOrganizationConnector).Assembly);
            using var configurationOwner = (IDisposable)configuration;
            using var consistency = OrganizationWriteLock.Acquire(orgArtifacts, TimeSpan.FromSeconds(15), DeploymentCli.GetSharedOrganizationLockDirectory(), ct);
            using var lease = new UserSecretsOrganizationConnector(configuration).Connect(target.Name);
            if (lease.Service is not OnPremiseClient client) throw new InvalidOperationException("Controlled SDK owner required.");
            client.Timeout = TimeSpan.FromMinutes(2); ProductionGuard.VerifyServer(lease.Service, target, ct);
            var delta = RebuildDelta(lease.Service, plan, before, definition, roots, ct);
            var observations = new List<object>();
            Dictionary<string, UiMetadataPublicationGuard.Scalar>? published = null;
            foreach (var unpublished in new[] { false, true })
            {
                var actual = new Dictionary<string, UiMetadataPublicationGuard.Scalar>(
                    UiMetadataPublicationGuard.Check(new ReadOnlySource(lease.Service, unpublished, ct), roots, ct).Values, StringComparer.Ordinal);
                var expected = BaselineNodeReplayProbe.Expected(baseline, delta, actual, unpublished, ct);
                var differences = BaselineNodeReplayProbe.Compare(expected, actual, ct);
                var annotated = differences.Select(item => new
                {
                    item.Path, item.Kind, item.ExpectedType, item.ActualType,
                    PlannedComponents = delta.Additions.Where(addition => item.Path.StartsWith(
                        addition.Entity + "." + addition.Collection + "[" + addition.Identity + "]", StringComparison.Ordinal))
                        .Select(addition => new { addition.Entity, addition.Collection, addition.Identity, addition.IsNewPlannedRelationship }).ToArray()
                }).ToArray();
                var attributeRoots = differences.Select(item => item.Path).Where(path => path.Contains(".Attributes[id:", StringComparison.Ordinal))
                    .Select(path => path[..(path.IndexOf(']', StringComparison.Ordinal) + 1)]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                var components = attributeRoots.Select(root =>
                {
                    var entity = root[..root.IndexOf('.', StringComparison.Ordinal)];
                    var logicalName = actual.TryGetValue(root + ".LogicalName", out var name) ? name.Value as string : null;
                    var attributeOf = actual.TryGetValue(root + ".AttributeOf", out var parent) ? parent.Value as string : null;
                    var planned = plan.Operations.Any(operation => operation.Entity == entity && operation.Name == logicalName &&
                        operation.Kind is "CreateAttribute" or "CreateLookup");
                    var derivedFromPlan = attributeOf is not null && plan.Operations.Any(operation => operation.Entity == entity &&
                        operation.Name == attributeOf && operation.Kind is "CreateAttribute" or "CreateLookup");
                    return new { Root = root, Entity = entity, LogicalName = logicalName, AttributeOf = attributeOf,
                        PresentInOriginalBaseline = baseline.Values.ContainsKey(root), PlannedExplicitAttribute = planned,
                        DerivedFromPlannedAttribute = derivedFromPlan, DifferenceNodes = differences.Count(item => item.Path.StartsWith(root, StringComparison.Ordinal)) };
                }).ToArray();
                observations.Add(new { Source = unpublished ? "Unpublished" : "Published", Nodes = actual.Count, Differences = annotated, Components = components });
                if (!unpublished) published = actual;
                else observations.Add(new { Source = "CurrentPublishedVsUnpublished", Differences = BaselineNodeReplayProbe.Compare(published!, actual, ct) });
            }
            var report = new { Organization = "construction", plan.OrganizationId, OriginalPlanFileSha256 = planHash,
                OriginalBaselineSha256 = baselineHash, FullOriginalBaselineAvailable = true, CanAuthorizeApply = false,
                BaselineNodes = baseline.Values.Count, PlannedAdditions = delta.Additions.Count, Observations = observations };
            if (JsonSerializer.SerializeToUtf8Bytes(report, SchemaFiles.Json).Length > 4 * 1024 * 1024)
                throw new InvalidDataException("Diagnostic output budget rejected.");
            ct.ThrowIfCancellationRequested(); var path = SchemaFiles.WriteNew(evidence, "node-differences.json", report);
            using var stream = File.OpenRead(path); var hash = Convert.ToHexString(SHA256.HashData(stream));
            output.WriteLine("FS-CONSTRUCTION-NODE report=" + path + " SHA256=" + hash);
            output.WriteLine("FS-CONSTRUCTION-NODE originalBaselineNodes=" + baseline.Values.Count + " writeAuthorized=false");
        }
        catch (Exception exception) { failure = exception; }
        finally
        {
            if (diagnostics is not null)
            {
                try { if (failure is not null) _ = diagnostics.Report(failure, "Live3P.FS.ConstructionNodeDiagnostic", notify: false); }
                catch { Console.Error.WriteLine("[Live3P.FS.ConstructionNodeDiagnostic] ReportingFailed"); }
                finally { try { await diagnostics.DisposeAsync(); } catch (Exception exception) { failure ??= exception; } }
            }
        }
        if (failure is not null) throw new InvalidOperationException("FS readonly construction diagnostic failed; " + UiSavedQueryValidationProbe.DescribeFailure(failure));
    }

    /// <summary>逐項重建原新增候選，只讀當前正式ID；原before已存在欄／鍵不可冒稱本次新增。</summary>
    /// <param name="service">外層唯一租約借用的SDK。</param><param name="plan">原SHA綁定plan。</param>
    /// <param name="before">原規劃before。</param><param name="definition">原schema hash對應清單。</param>
    /// <param name="roots">原完整baseline固定實體。</param><param name="ct">同步owner取消。</param>
    /// <returns>僅本案純值增量，不保存SDK或metadata回應。</returns>
    private static UiMetadataPublicationGuard.SchemaDelta RebuildDelta(IOrganizationService service, SchemaPlan plan,
        SchemaSnapshot before, SchemaDefinition definition, string[] roots, CancellationToken ct)
    {
        var delta = new UiMetadataPublicationGuard.SchemaDelta(); var metadata = new Dictionary<string, EntityMetadata>(StringComparer.Ordinal);
        foreach (var operation in plan.Operations.Where(item => item.Kind is "CreateAttribute" or "CreateLookup" or "CreateKey"))
        {
            ct.ThrowIfCancellationRequested(); var entity = definition.Entities.Single(item => item.LogicalName == operation.Entity);
            if (!metadata.TryGetValue(entity.LogicalName, out var actual))
            {
                actual = ((RetrieveEntityResponse)service.Execute(new RetrieveEntityRequest
                    { LogicalName = entity.LogicalName, EntityFilters = EntityFilters.All, RetrieveAsIfPublished = true })).EntityMetadata;
                ct.ThrowIfCancellationRequested(); metadata.Add(entity.LogicalName, actual);
            }
            var old = before.Entities.SingleOrDefault(item => item.LogicalName == entity.LogicalName);
            if (operation.Kind == "CreateKey")
            {
                if (old?.Keys.Any(item => item.LogicalName == operation.Name) == true) throw new InvalidDataException("Original key was not new.");
                var key = entity.Keys.Single(item => item.LogicalName == operation.Name);
                var id = RequireId(actual.Keys.Single(item => item.LogicalName == operation.Name).MetadataId);
                var candidate = new EntityKeyMetadata { SchemaName = key.LogicalName,
                    DisplayName = new Label(key.LogicalName, before.LanguageCode), KeyAttributes = key.Attributes.ToArray() };
                delta.AddKey(delta.CaptureKey(entity.LogicalName, candidate, ct), id); continue;
            }
            if (old?.Fields.Any(item => item.LogicalName == operation.Name) == true) throw new InvalidDataException("Original attribute was not new.");
            var field = entity.Fields.Single(item => item.LogicalName == operation.Name);
            var attribute = BaselineNodeReplayProbe.Candidate(field, entity.IsNew && entity.Audit, before.LanguageCode);
            var attributeId = RequireId(actual.Attributes.Single(item => item.LogicalName == field.LogicalName).MetadataId);
            if (operation.Kind == "CreateLookup")
            {
                var relation = new OneToManyRelationshipMetadata
                {
                    SchemaName = "new_" + entity.LogicalName + "_" + field.LogicalName, ReferencedEntity = field.Target,
                    ReferencedAttribute = field.Target + "id", ReferencingEntity = entity.LogicalName,
                    CascadeConfiguration = new CascadeConfiguration { Delete = CascadeType.Restrict, Assign = CascadeType.NoCascade,
                        Merge = field.CascadeMerge == "Cascade" ? CascadeType.Cascade : CascadeType.NoCascade,
                        Reparent = CascadeType.NoCascade, Share = CascadeType.NoCascade, Unshare = CascadeType.NoCascade }
                };
                var relationId = RequireId(actual.ManyToOneRelationships.Single(item => item.SchemaName == relation.SchemaName).MetadataId);
                delta.AddLookup(delta.CapturePlannedLookup(entity.LogicalName, (LookupAttributeMetadata)attribute, relation, ct), attributeId, relationId);
            }
            else if (roots.Contains(entity.LogicalName, StringComparer.Ordinal))
                delta.Add(delta.CaptureAttribute(entity.LogicalName, attribute, ct), attributeId);
        }
        return delta;
    }

    /// <summary>缺正式元件ID立即拒絕，不能以新Guid、邏輯名或操作token代替資料庫身分。</summary>
    /// <param name="id">當前實際metadata元件ID。</param><returns>非空正式ID。</returns>
    private static Guid RequireId(Guid? id) => id is { } value && value != Guid.Empty ? value : throw new InvalidDataException("No formal metadata identity.");

    /// <summary>只讀指定來源完整metadata，不接管SDK；每個呼叫前後檢查同一token，寫入永遠拒絕。</summary>
    private sealed class ReadOnlySource : IOrganizationService
    {
        private readonly IOrganizationService service; private readonly bool unpublished; private readonly CancellationToken ct;
        /// <summary>只借用外層SDK及固定來源，沒有獨立通道、取消註冊或跨呼叫狀態。</summary>
        internal ReadOnlySource(IOrganizationService service, bool unpublished, CancellationToken ct)
        { this.service = service; this.unpublished = unpublished; this.ct = ct; }
        /// <summary>唯一允許訊息是RetrieveEntity(All)，複製請求避免改動guard原參數，返回後先檢查取消。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            ct.ThrowIfCancellationRequested();
            if (request is not RetrieveEntityRequest read || read.EntityFilters != EntityFilters.All) throw new InvalidOperationException("Readonly metadata only.");
            var result = service.Execute(new RetrieveEntityRequest { LogicalName = read.LogicalName, EntityFilters = EntityFilters.All, RetrieveAsIfPublished = unpublished });
            ct.ThrowIfCancellationRequested(); return result;
        }
        /// <summary>不允許建立業務資料或metadata。</summary>
        public Guid Create(Entity entity) => throw new InvalidOperationException("Readonly metadata only.");
        /// <summary>不允許更新業務資料或metadata。</summary>
        public void Update(Entity entity) => throw new InvalidOperationException("Readonly metadata only.");
        /// <summary>不允許刪除任何正式列。</summary>
        public void Delete(string entityName, Guid id) => throw new InvalidOperationException("Readonly metadata only.");
        /// <summary>禁止業務列查詢，保護唯讀診斷不接觸個資。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new InvalidOperationException("Readonly metadata only.");
        /// <summary>禁止業務集合查詢，不讀憑證或個資。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => throw new InvalidOperationException("Readonly metadata only.");
        /// <summary>不允許建立任何關聯資料。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new InvalidOperationException("Readonly metadata only.");
        /// <summary>不允許移除任何關聯資料。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new InvalidOperationException("Readonly metadata only.");
    }
}

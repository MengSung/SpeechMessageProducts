/*
檔案整體說明
目的與定位：02A §10.4／2026-11-28 18:55 決策的 FS 唯讀診斷入口，限定 goodshepherd20
失敗 apply2/2；既有完整 guard 與 schema 寫入路徑一律不修改，不發送任何寫入或發布訊息。
主要流程：SHA 綁定原 plan／before，使用原定義重驗原 plan，重建兩個寫前 lookup 候選，
對 account／new_role 的正式關聯分別讀 published／unpublished，以同一投影器比較節點。
缺口：原完整 baseline 未落檔，不能還原舊標籤、cascade 等原節點；本工具永遠標示
FullOriginalBaselineAvailable=false、CanAuthorizeThirdApply=false，不以當前兩來源相等冒充原基準。
輸出：唯一 evidence 目錄列差異路徑、SDK 型別、正式元件身份與固定分類；22:05決策的兩新關聯另輸出經封閉格式驗證的預設純值，不輸出
XML、標籤、秘密或 LINE userId；完整候選驗證後才原子寫檔，主輸出只含數量和 SHA。
隔離與生命週期：固定 User Secrets connector、測試組織 ProductionGuard、三分鐘 CTS 與獨占
lease；同步 SDK 排空後依序歸還 lease、組織一致性鎖、組態及例外 owner，沒有共享 mutable state。
測試環境正在寫入時不進場；本地一致性鎖只防止部署並行，不提供第三次寫入授權。
故障：原 Exception 交共用診斷一次、關閉 LINE 通知；取消／故障不回傳部分成功。離線差異器
另以合成測試驗證，這個入口只有明確 Category=Live3P／WP=FSNodeDiagnostics 才應執行。
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
using PowerPlatform.Dataverse.Client;
using SpeechMessage.Dynamics.Deployment.Schema;
using SpeechMessage.Dynamics.Deployment.Tests.Ui;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace SpeechMessage.Dynamics.Deployment.Tests.Organizations;

/// <summary>固定事件的唯讀實機入口；建構及測試發現不讀秘密、不連線或建立背景資源。</summary>
[Trait("Category", "Live3P")]
[Trait("WP", "FSNodeDiagnostics")]
public sealed class UiSchemaDeltaNodeLiveTests
{
    /// <summary>不含組織或使用者狀態的固定計畫欄名；以唯讀包裝避免跨測試變更。</summary>
    private static readonly IReadOnlyList<string> PlannedLookupNames = Array.AsReadOnly(new[] { "new_role", "new_tenant" });

    /// <summary>原 apply 的既有發布範圍；只作目前兩來源檢查，絕不當作原 baseline。</summary>
    private static readonly IReadOnlyList<string> ExistingPublishing = Array.AsReadOnly(new[] { "account", "new_role", "new_linechannel" });

    /// <summary>只借用 xUnit 輸出；不保存 SDK、組態、例外或組織身分。</summary>
    private readonly ITestOutputHelper output;

    /// <summary>保存框架的輸出能力，所有執行期資源都由單次測試方法擁有並清理。</summary>
    /// <param name="output">框架擁有的安全摘要輸出。</param>
    public UiSchemaDeltaNodeLiveTests(ITestOutputHelper output) => this.output = output;

    /// <summary>
    /// 診斷原計畫關聯候選和當前正式元件的節點差異；成功只表示報告完整產生。
    /// 不恢復原 baseline、不調整 guard、不執行或消耗任何第三次 apply。
    /// </summary>
    /// <returns>所有同步 SDK、租約及 owner 都排空後完成；故障不產生部分報告。</returns>
    [Fact]
    public async Task ReadOnly_KnownDeltaNodes_ReportsMissingOriginalBaseline()
    {
        const string organization = "goodshepherd20";
        var common = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SpeechMessage");
        var evidence = Path.Combine(common, "evidence", "FS-node-readonly", Guid.NewGuid().ToString("N"));
        ExceptionDiagnostics? diagnostics = null;
        Exception? failure = null;
        try
        {
            diagnostics = new ExceptionDiagnostics(Path.Combine(evidence, "Logs"),
                outputOptions: new ExceptionOutputOptions(writeExceptionLog: true, sendLine: false));
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var ct = timeout.Token;
            if (File.Exists(Path.Combine(common, "locks", "test-env-d365.lock")) ||
                File.Exists(Path.Combine(common, "locks", "test-env.lock")))
                throw new InvalidOperationException("FS readonly diagnostic refused while shared writes are active.");
            var repository = DeploymentCli.FindRepositoryRoot();
            var original = Path.Combine(common, "worktrees", "cl-lane-a-fs-rebase-20261128", "artifacts", "d365", organization);
            var planPath = Path.Combine(original, "20261128-084236-0466096-bdb4e365506449b8818265a0afb491cd", "schema-plan.json");
            var beforePath = Path.Combine(original, "20261128-085402-9526822-1bdc5eceb0f747f1a52dbe1d3b913280", "before-metadata.json");
            var planHash = RequireHash(planPath, "337EBA5A81AE2B014E88386FC3853FD19335B3EFFDB03EED15B511FC1FAEE997");
            var beforeHash = RequireHash(beforePath, "DD245E69D6977209EDD1F89588D203B26146373628DDB0BAE3FB400465718B64");
            var definition = SchemaFiles.LoadForOrganization(Path.Combine(original, "diagnostic-original-definition-a1eebc7b"), organization);
            var plan = SchemaFiles.ReadPlan(planPath);
            var before = SchemaFiles.ReadBeforeSnapshot(beforePath);
            SchemaPlanner.ValidatePlan(plan, definition, before);
            var table = definition.Entities.Single(entity => entity.LogicalName == "new_testinvite");
            if (plan.Organization != organization || before.Organization != organization ||
                plan.OrganizationId != before.OrganizationId || plan.Operations.Count != 15 ||
                plan.Operations.Any(operation => operation.Entity != table.LogicalName) ||
                before.Entities.Any(entity => entity.LogicalName == table.LogicalName))
                throw new InvalidDataException("FS diagnostic historical scope rejected.");
            var lookups = plan.Operations.Where(operation => operation.Kind == "CreateLookup").ToArray();
            if (lookups.Length != 2 || !lookups.Select(operation => operation.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(PlannedLookupNames, StringComparer.Ordinal))
                throw new InvalidDataException("FS diagnostic planned lookup scope rejected.");
            var catalog = OrganizationCatalog.LoadDefault();
            var target = catalog.GetTest(organization);
            _ = ProductionGuard.Authorize(["verify", "--org", organization], catalog, null, ct);
            var configuration = DeploymentConfiguration.Create(typeof(UserSecretsOrganizationConnector).Assembly);
            using var configurationOwner = (IDisposable)configuration;
            using var consistency = OrganizationWriteLock.Acquire(Path.Combine(repository, "artifacts", "d365", organization),
                TimeSpan.FromSeconds(15), DeploymentCli.GetSharedOrganizationLockDirectory(), ct);
            using var lease = new UserSecretsOrganizationConnector(configuration).Connect(organization);
            if (lease.Service is not OnPremiseClient client)
                throw new InvalidOperationException("FS diagnostic requires the controlled SDK owner.");
            client.Timeout = TimeSpan.FromMinutes(2);
            ProductionGuard.VerifyServer(lease.Service, target, ct);
            var current = SchemaMetadata.Read(lease.Service, target, ct, retrieveAsIfPublished: false);
            if (current.OrganizationId != before.OrganizationId)
                throw new InvalidDataException("FS diagnostic organization identity changed.");
            var observations = new List<RelationshipObservation>();
            foreach (var unpublished in new[] { false, true })
            {
                var source = ReadEntity(lease.Service, table.LogicalName, unpublished, ct);
                foreach (var operation in lookups)
                {
                    var field = table.Fields.Single(field => field.LogicalName == operation.Name);
                    var targetMetadata = ReadEntity(lease.Service, field.Target!, unpublished, ct);
                    var name = "new_" + table.LogicalName + "_" + field.LogicalName;
                    var relation = targetMetadata.OneToManyRelationships.Single(item => item.SchemaName == name);
                    var attribute = source.Attributes.OfType<LookupAttributeMetadata>()
                        .Single(item => item.LogicalName == field.LogicalName);
                    if (relation.MetadataId is not { } relationId || relationId == Guid.Empty || relation.IsManaged != false ||
                        relation.ReferencingEntity != table.LogicalName || relation.ReferencedEntity != field.Target ||
                        relation.ReferencingAttribute != field.LogicalName || attribute.MetadataId is not { } attributeId ||
                        attributeId == Guid.Empty || attribute.IsManaged != false)
                        throw new InvalidDataException("FS diagnostic formal component identity rejected.");
                    var expected = Expected(field, table.Audit, before.LanguageCode, attributeId, relationId, ct);
                    var actualDelta = new UiMetadataPublicationGuard.SchemaDelta();
                    actualDelta.AddLookup(actualDelta.CaptureLookup(table.LogicalName, attribute, relation, ct), attributeId, relationId);
                    var actual = actualDelta.Additions.Single(item =>
                        item.Entity == field.Target && item.Collection == "OneToManyRelationships").Values;
                    var differences = SchemaDeltaNodeProbe.CompareNodes(expected, actual, ct);
                    var safeDefaults = differences.ToDictionary(item => item.Path,
                        item => SchemaDeltaNodeProbe.DescribeSafeDefault(item.Path, actual[item.Path]), StringComparer.Ordinal);
                    observations.Add(new RelationshipObservation(unpublished ? "Unpublished" : "Published",
                        field.Target!, name, relationId, false, differences, safeDefaults));
                }
            }
            var currentFullSourcesMatch = true;
            try
            {
                _ = UiMetadataPublicationGuard.Check(lease.Service, ExistingPublishing, ct);
            }
            catch (InvalidDataException)
            {
                // 完整比較的預期拒絕只作診斷分類；不能把未知 pending／容量／完整性問題視為成功。
                currentFullSourcesMatch = false;
            }
            ct.ThrowIfCancellationRequested();
            var report = new
            {
                Organization = organization, BeforeSnapshotSha256 = beforeHash, OriginalPlanSha256 = planHash,
                FullOriginalBaselineAvailable = false, CanAuthorizeThirdApply = false,
                Coverage = "OnlyTwoExplicitPlannedRelationshipSubtrees",
                CurrentPublishedAndUnpublishedFullGuardPassed = currentFullSourcesMatch,
                MissingCoverage = new[] { "OriginalLabels", "OriginalDescriptions", "OriginalRelationshipsAndCascade",
                    "OriginalPrivilegesAndManagedProperties", "OtherOriginalFullDataMemberNodes" },
                Observations = observations
            };
            var json = JsonSerializer.Serialize(report);
            if (json.Length > 2 * 1024 * 1024) throw new InvalidDataException("FS diagnostic output budget exceeded.");
            var path = SchemaFiles.WriteNew(evidence, "node-differences.json", report);
            var hash = RequireHash(path, null);
            output.WriteLine("FS-NODE report=" + path + " sha256=" + hash);
            output.WriteLine("FS-NODE observations=" + observations.Count + " differingNodes=" +
                observations.Sum(item => item.Differences.Count) + " originalBaselineProven=false thirdApplyAuthorized=false");
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            if (diagnostics is not null)
            {
                try
                {
                    if (failure is not null) _ = diagnostics.Report(failure, "Live3P.FS.NodeDiagnostic", notify: false);
                }
                catch { Console.Error.WriteLine("[Live3P.FS.NodeDiagnostic] ReportingFailed"); }
                finally
                {
                    try { await diagnostics.DisposeAsync(); }
                    catch (Exception exception) { failure ??= exception; }
                }
            }
        }
        if (failure is not null)
            throw new InvalidOperationException("FS readonly node diagnostic failed; "
                + UiSavedQueryValidationProbe.DescribeFailure(failure));
    }

    /// <summary>重建原引擎確定性的寫前候選，僅以本次正式元件 ID 綁定；不取用 server 預設值。</summary>
    /// <param name="field">原 hash 綁定定義的 lookup。</param><param name="audit">原新實體稽核要求。</param>
    /// <param name="language">原 before 基礎 LCID。</param><param name="attributeId">當前正式欄位 ID。</param>
    /// <param name="relationshipId">當前正式關聯 ID。</param><param name="ct">同步 owner 取消。</param>
    /// <returns>與原 SchemaDelta 相同的 referenced 端純值候選，不擁有 SDK 來源。</returns>
    private static IReadOnlyDictionary<string, UiMetadataPublicationGuard.Scalar> Expected(
        SchemaField field, bool audit, int language, Guid attributeId, Guid relationshipId, CancellationToken ct)
    {
        var attribute = new LookupAttributeMetadata
        {
            SchemaName = field.LogicalName, DisplayName = new Label(field.DisplayName, language),
            RequiredLevel = new AttributeRequiredLevelManagedProperty(field.Required
                ? AttributeRequiredLevel.ApplicationRequired : AttributeRequiredLevel.None),
            IsSecured = field.Secured, IsAuditEnabled = new BooleanManagedProperty(audit)
        };
        var relation = new OneToManyRelationshipMetadata
        {
            SchemaName = "new_new_testinvite_" + field.LogicalName,
            ReferencedEntity = field.Target, ReferencedAttribute = field.Target + "id", ReferencingEntity = "new_testinvite",
            CascadeConfiguration = new CascadeConfiguration
            {
                Delete = CascadeType.Restrict, Assign = CascadeType.NoCascade, Merge = CascadeType.NoCascade,
                Reparent = CascadeType.NoCascade, Share = CascadeType.NoCascade, Unshare = CascadeType.NoCascade
            }
        };
        var delta = new UiMetadataPublicationGuard.SchemaDelta();
        delta.AddLookup(delta.CaptureLookup("new_testinvite", attribute, relation, ct), attributeId, relationshipId);
        return delta.Additions.Single(item => item.Entity == field.Target && item.Collection == "OneToManyRelationships").Values;
    }

    /// <summary>只送 RetrieveEntity(All)，前後檢查取消、正式身分及集合，不保存 service 或 SDK 回應。</summary>
    /// <param name="service">外層租約借用的已核身服務。</param><param name="name">固定計畫實體名稱。</param>
    /// <param name="unpublished">是否包含未發布的來源。</param><param name="ct">同步 owner 取消。</param>
    /// <returns>僅供本次同步比較的完整 SDK metadata，不交背景工作或跨操作快取。</returns>
    private static EntityMetadata ReadEntity(IOrganizationService service, string name, bool unpublished, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var response = service.Execute(new RetrieveEntityRequest
        {
            LogicalName = name, EntityFilters = EntityFilters.All, RetrieveAsIfPublished = unpublished
        }) as RetrieveEntityResponse;
        ct.ThrowIfCancellationRequested();
        var entity = response?.EntityMetadata;
        if (entity?.LogicalName != name || entity.MetadataId is not { } id || id == Guid.Empty ||
            entity.Attributes is null || entity.Attributes.Length > 2048 ||
            entity.OneToManyRelationships is null || entity.OneToManyRelationships.Length > 4096)
            throw new InvalidDataException("FS diagnostic metadata response rejected.");
        return entity;
    }

    /// <summary>串流計算固定檔案 SHA，using 決定性關閉；原輸入須符合事件綁定，輸出只取安全 hash。</summary>
    /// <param name="path">固定歷史輸入或本次唯一輸出。</param><param name="expected">原 SHA；輸出為 null。</param>
    /// <returns>大寫 SHA256，不回顯檔案內容或秘密。</returns>
    private static string RequireHash(string path, string? expected)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 100 * 1024 * 1024) throw new InvalidDataException("FS diagnostic input budget exceeded.");
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        if (expected is not null && hash != expected) throw new InvalidDataException("FS diagnostic historical input hash changed.");
        return hash;
    }

    /// <summary>完整驗證後發布的操作區域純觀察，不含原始 scalar、連線、委派或可變 SDK。</summary>
    /// <param name="Source">Published 或 Unpublished。</param><param name="Entity">所屬既有端點。</param>
    /// <param name="LogicalName">原計畫明列的關聯名稱。</param><param name="MetadataId">正式關聯 ID。</param>
    /// <param name="AutomaticSystemComponentProven">本工具沒有原基準，固定 false，不替代決策。</param>
    /// <param name="Differences">完整節點差異；沒有標籤或XML本文。</param>
    /// <param name="NormalizationDefaults">只含明列路徑與安全預設純值，不代表護欄已核准它們。</param>
    private sealed record RelationshipObservation(string Source, string Entity, string LogicalName, Guid MetadataId,
        bool AutomaticSystemComponentProven, IReadOnlyList<SchemaDeltaNodeProbe.NodeDifference> Differences,
        IReadOnlyDictionary<string, UiMetadataPublicationGuard.Scalar> NormalizationDefaults);
}

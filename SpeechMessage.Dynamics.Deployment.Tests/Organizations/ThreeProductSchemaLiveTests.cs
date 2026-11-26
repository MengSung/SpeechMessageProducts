/*
檔案整體說明
目的與定位：FS 三產品測試組織的真正唯讀 Live3P 驗收，補足離線 SDK 替身無法證明的
已發布結構、Active 索引鍵與平台公用檢視。本檔不執行部署、停用流程或 UI 匯出入。
主要功能與流程：明確篩選 Category=Live3P、WP=FS 後，先離線核對 legacy schema 雜湊，
再由正式 OrganizationCatalog、DeploymentConfiguration 與 UserSecretsOrganizationConnector
依序連 goodshepherd20、construction、association。每次連線先經 ProductionGuard 驗真身分，
才呼叫 SchemaDeploymentEngine.Verify(LoadForOrganization)，要求 operations、pending keys 與
UI changes 全為零；另以正式 SavedQueryId 讀回已發布 XML、啟用狀態及實體 metadata，
由各組織正式主鍵與 ObjectTypeCode 產生期望，再比較排除組織專屬物件代碼後的平台邏輯。
construction、association 另盤點包含未發布候選的真實 entity metadata，拒絕四張教會專屬表。
信任邊界與隔離：只接受程式內固定的三個 test 名稱，不以參數、環境變數或測試替身擴權。
goodshepherdback 僅作本機檔案 hash 檢查，永不建立其連線；SDK 能力轉接器拒絕所有寫入、
業務實體查詢及未列出的訊息，不讀 Channel 紀錄、token、個資或付款資料。
擁有權與生命週期：單一案例擁有三分鐘取消來源、User Secrets 組態與本機安全診斷 owner；
using lease 逐組織獨占 client，前一租約 Dispose 後才進下一組織，沒有共享池或背景工作。
client 傳輸 timeout 固定三分鐘，SDK 前後均檢查同一 token；既有同步 connector 初始化與
已入場 SDK 沒有可中斷介面，須等待返回後排空，不能用遺留 Task 冒充硬中止或提早歸還。
失敗與驗證：缺依賴、逾時、重複 ID、未發布或不一致皆失敗，不 Skip、不產生成功證據。
真正 Exception 經共用 ExceptionDiagnostics 安全記錄，明確關閉 LINE；測試失敗只顯示固定
階段與型別，不附原 message、inner exception 或秘密。診斷在唯一 artifacts 子目錄排空後釋放。
執行前置由主代理負責：完成實際 quiesce、受審部署並持 test-env 鎖後才執行此類別；
本檔不能取代 FS 要求的真正 ui-export／ui-import，編譯與列出測試亦不等於實機通過。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using PowerPlatform.Dataverse.Client;
using SpeechMessage.Dynamics.Deployment.Schema;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Organizations;

/// <summary>
/// 真實 FS 已發布結構與平台檢視驗收；沿用既有 Live 測試的明確 Category 篩選方式，
/// 發現測試時沒有組態、網路或檔案寫入副作用，執行時缺少依賴則必須失敗。
/// </summary>
[Trait("Category", "Live3P")]
[Trait("WP", "FS")]
public sealed class ThreeProductSchemaLiveTests
{
    /// <summary>整案內部取消與單次 SDK 傳輸的固定上限；同步呼叫返回後才允許釋放租約。</summary>
    private static readonly TimeSpan LiveTimeout = TimeSpan.FromMinutes(3);

    /// <summary>
    /// 在已 quiesce、部署並由呼叫端持有 test-env 鎖的環境，逐組織驗真身分與已發布 verify，
    /// 再獨立讀正式 savedquery 與 metadata。任何缺漏都使整案失敗；本案不建立測試業務列，
    /// 不呼叫自動化或外部 Channel，也不把本案成功解讀為 UI 匯出入驗收。
    /// </summary>
    /// <returns>僅於三個組織全部符合且所有租約／組態／診斷 owner 已排空後完成。</returns>
    [Fact]
    public async Task ThreeProducts_PublishedSchemaAndPlatformViews_MatchApprovedLayers()
    {
        var directory = Path.Combine(System.AppContext.BaseDirectory, "artifacts", "Live3P", "FS", Guid.NewGuid().ToString("N"));
        ExceptionDiagnostics? diagnostics = null;
        Exception? failure = null;
        var stage = "FS-LIVE-01";
        try
        {
            diagnostics = new ExceptionDiagnostics(Path.Combine(directory, "Logs"),
                outputOptions: new ExceptionOutputOptions(writeExceptionLog: true, sendLine: false));
            using var timeout = new CancellationTokenSource(LiveTimeout);
            var ct = timeout.Token;
            var root = FindRepositoryRoot();
            CheckLegacySchemaOffline(root, ct);
            var catalog = OrganizationCatalog.LoadDefault();
            string[] names = ["goodshepherd20", "construction", "association"];
            var definitions = names.Select(name => SchemaFiles.LoadForOrganization(root, name)).ToArray();
            // FS 僅定義平台公用檢視；要求完整清單相同且非空，避免用交集掩蓋某組織漏掉檢視。
            Require(definitions[0].Views.Count > 0);
            Require(definitions.All(definition =>
                SchemaFiles.HashValue(definition.Views) == SchemaFiles.HashValue(definitions[0].Views)));
            foreach (var name in names) _ = catalog.GetTest(name);
            ct.ThrowIfCancellationRequested();

            stage = "FS-LIVE-02";
            var configuration = DeploymentConfiguration.Create(typeof(UserSecretsOrganizationConnector).Assembly);
            using var configurationOwner = (IDisposable)configuration;
            var connector = new UserSecretsOrganizationConnector(configuration);
            var baseline = new Dictionary<(string Entity, string Name), string>();
            var organizationIds = new HashSet<Guid>();
            for (var index = 0; index < names.Length; index++)
            {
                stage = "FS-LIVE-03";
                var target = catalog.GetTest(names[index]);
                var authorization = ProductionGuard.Authorize(["verify", "--org", target.Name], catalog, null, ct);
                Require(authorization.Organization == target);
                ct.ThrowIfCancellationRequested();
                using var lease = connector.Connect(target.Name);
                Require(lease.Service is OnPremiseClient);
                ((OnPremiseClient)lease.Service).Timeout = LiveTimeout;
                var service = new ReadOnlySchemaService(lease.Service, ct);
                ProductionGuard.VerifyServer(service, target, ct);

                stage = "FS-LIVE-04";
                var definition = definitions[index];
                var plan = new SchemaDeploymentEngine(service, Path.Combine(directory, target.Name),
                    TimeProvider.System, target, catalog).Verify(definition, ct);
                Require(plan.Organization == target.Name && plan.OrganizationId != Guid.Empty
                    && organizationIds.Add(plan.OrganizationId));
                Require(plan.Operations.Count == 0 && plan.PendingKeys.Count == 0);
                Require(plan.Ui is { Changes: 0, SolutionId: var solutionId } && solutionId != Guid.Empty);

                stage = "FS-LIVE-05";
                CheckPublishedPlatformViews(service, definition, plan.Ui!, baseline, index == 0, ct);
                if (target.Name is "construction" or "association")
                {
                    stage = "FS-LIVE-06";
                    CheckNoChurchEntities(service, ct);
                }
                ct.ThrowIfCancellationRequested();
                // using 宣告位於迴圈本文；client 必定在下一個組織 Connect 前釋放。
            }
            Require(organizationIds.Count == names.Length);
            ct.ThrowIfCancellationRequested();
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            if (diagnostics is not null)
            {
                // timeout 是本案內部失敗，不把已取消的內部 token 傳成「正常 caller 取消」。
                if (failure is not null)
                    _ = diagnostics.Report(failure, "ThreeProductSchemaLiveTests.FS", notify: false);
                try { await diagnostics.DisposeAsync(); }
                catch (Exception exception)
                {
                    failure = failure is null ? exception : new AggregateException(failure, exception);
                    Console.Error.WriteLine("[Live3P.FS] DiagnosticOwnerDisposeFailed");
                }
            }
        }
        if (failure is not null)
            throw new InvalidOperationException("FS Live3P failed at " + stage + " (" + failure.GetType().Name
                + "); sensitive details suppressed.");
    }

    /// <summary>
    /// 對每個 plan 的正式 SavedQueryId 再作 SDK 精確讀回；XML 必須完全等於以已發布 metadata
    /// 生成的期望。跨組織僅保存邏輯 hash，不保留 SDK 物件、XML、client 或組織專屬物件代碼。
    /// </summary>
    /// <param name="service">當前獨占租約借出的受限唯讀服務。</param>
    /// <param name="definition">正式 LoadForOrganization 的完整 FS 定義。</param>
    /// <param name="plan">已發布 Verify 所讀回的 UI 計畫，不能用本機候選 ID 代替。</param>
    /// <param name="baseline">本案例區域平台邏輯摘要，第一組織完成後僅作比對。</param>
    /// <param name="first">是否建立第一組織的完整摘要；後續組織不得新增或覆寫基準。</param>
    /// <param name="ct">本案例三分鐘內部取消訊號。</param>
    private static void CheckPublishedPlatformViews(IOrganizationService service, SchemaDefinition definition,
        UiViewPlan plan, Dictionary<(string Entity, string Name), string> baseline, bool first, CancellationToken ct)
    {
        Require(plan.Solution == UiViewDeployment.SolutionName && plan.Views.Count == definition.Views.Count);
        var ids = new HashSet<Guid>();
        var identities = new HashSet<(string Entity, string Name)>();
        foreach (var target in plan.Views)
        {
            ct.ThrowIfCancellationRequested();
            var view = target.Definition;
            Require(target.Id != Guid.Empty && ids.Add(target.Id) && identities.Add((view.Entity, view.Name))
                && !target.NeedsWrite && target.Included);
            Require(definition.Views.Any(item => SchemaFiles.HashValue(item) == SchemaFiles.HashValue(view)));
            var response = service.Execute(new RetrieveEntityRequest
            {
                LogicalName = view.Entity, EntityFilters = EntityFilters.Entity, RetrieveAsIfPublished = false
            }) as RetrieveEntityResponse;
            var metadata = response?.EntityMetadata;
            Require(metadata is { MetadataId: var metadataId, ObjectTypeCode: > 0 }
                && metadataId is { } entityId && entityId != Guid.Empty && metadata.LogicalName == view.Entity
                && !string.IsNullOrWhiteSpace(metadata.PrimaryIdAttribute));
            var expected = ViewDefinitions.Generate(definition, view, primaryId: metadata!.PrimaryIdAttribute,
                objectTypeCode: metadata.ObjectTypeCode!.Value);
            var row = service.Retrieve("savedquery", target.Id, new ColumnSet("savedqueryid", "returnedtypecode",
                "name", "querytype", "isdefault", "statecode", "statuscode", "ismanaged", "fetchxml", "layoutxml"));
            Require(row is not null && row.LogicalName == "savedquery" && row.Id == target.Id
                && row.GetAttributeValue<Guid>("savedqueryid") == target.Id);
            Require(row!.GetAttributeValue<string>("returnedtypecode") == view.Entity
                && row.GetAttributeValue<string>("name") == view.Name
                && row.Attributes.TryGetValue("querytype", out var queryType) && queryType is 0
                && row.Attributes.TryGetValue("isdefault", out var isDefault) && isDefault is bool defaultValue
                && defaultValue == view.IsDefault
                && row.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0
                && row.GetAttributeValue<OptionSetValue>("statuscode")?.Value == 1
                && row.Attributes.TryGetValue("ismanaged", out var managed) && managed is false);
            var fetch = row.GetAttributeValue<string>("fetchxml");
            var layout = row.GetAttributeValue<string>("layoutxml");
            Require(fetch == expected.FetchXml && layout == expected.LayoutXml);
            var logic = SchemaFiles.HashValue(new
            {
                FetchXml = fetch, LayoutXml = PlatformLayout(layout!), view.IsDefault, QueryType = expected.QueryType
            });
            var key = (view.Entity, view.Name);
            if (first) Require(baseline.TryAdd(key, logic));
            else Require(baseline.TryGetValue(key, out var previous) && previous == logic);
            ct.ThrowIfCancellationRequested();
        }
        Require(identities.Count == baseline.Count);
    }

    /// <summary>
    /// 真 metadata 已先參與逐組織 XML 精確驗證；跨組織比較只移除根 grid 的 ObjectTypeCode，
    /// 其餘主鍵、欄位、排序與結構完整保留。禁止 DTD／外部 resolver，XML 與 reader 於方法內釋放。
    /// </summary>
    /// <param name="xml">已符合當前組織正式 metadata 的 LayoutXml。</param>
    /// <returns>不含組織專屬物件代碼、可比較平台邏輯的區域字串。</returns>
    private static string PlatformLayout(string xml)
    {
        using var text = new StringReader(xml);
        using var reader = XmlReader.Create(text, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1_048_576
        });
        var document = XDocument.Load(reader);
        Require(document.Root?.Name == "grid" && document.Root.Attribute("object") is not null);
        document.Root!.Attribute("object")!.Remove();
        return document.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// 從服務端完整讀取包含未發布候選的 entity 清冊，拒絕缺回應、過量、空／重複正式 ID
    /// 或邏輯名稱，以及四張教會專屬表；不能只查本機 schema 就宣稱非教會組織乾淨。
    /// </summary>
    /// <param name="service">已經 ProductionGuard 核對的當前組織唯讀服務。</param>
    /// <param name="ct">所有同步 I/O 前後與投影期間均需尊重的內部取消。</param>
    private static void CheckNoChurchEntities(IOrganizationService service, CancellationToken ct)
    {
        var response = service.Execute(new RetrieveAllEntitiesRequest
        {
            EntityFilters = EntityFilters.Entity, RetrieveAsIfPublished = true
        }) as RetrieveAllEntitiesResponse;
        var entities = response?.EntityMetadata;
        Require(entities is { Length: > 0 and <= 4096 });
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in entities!)
        {
            ct.ThrowIfCancellationRequested();
            Require(entity is not null && entity.MetadataId is { } id && id != Guid.Empty && ids.Add(id)
                && !string.IsNullOrWhiteSpace(entity.LogicalName) && names.Add(entity.LogicalName));
            Require(entity!.LogicalName is not ("new_fee" or "new_offeringcategory"
                or "new_dedication_booking" or "new_stor_lessons"));
        }
    }

    /// <summary>
    /// 沿用既有離線 legacy hash 契約，只讀本機定義；這不是 goodshepherdback 的 live plan
    /// 零差異證據，且此方法不接受 connector、service 或任何憑證。
    /// </summary>
    /// <param name="root">組件所在工作樹的正式根目錄。</param>
    /// <param name="ct">本案內部取消，不能取消後仍開始後續連線。</param>
    private static void CheckLegacySchemaOffline(string root, CancellationToken ct)
    {
        var path = Path.Combine(root, "d365", "schema", "speechmessagecore.schema.json");
        var before = SchemaFiles.Load(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Require(stream.Length is > 0 and <= 1_048_576);
        Require(Convert.ToHexString(SHA256.HashData(stream))
            == "AE008B2B75F9AB35EAFC78A18197CC9300676AA5A53272D33AC0B05BE3185310");
        Require(SchemaFiles.Hash(before) == SchemaFiles.Hash(SchemaFiles.LoadForOrganization(root, "goodshepherdback")));
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// 與 OrganizationCatalog.LoadDefault 相同，從測試組件位置向上尋找方案根；
    /// 不信任可能由其他測試暫時切換的 CurrentDirectory，也不以環境變數重導到其他工作樹。
    /// </summary>
    /// <returns>目前組件所屬工作樹；缺少正式方案則失敗，不建立合成 repository。</returns>
    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(System.AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln"))) return directory.FullName;
        throw new InvalidDataException("FS Live3P repository is unavailable.");
    }

    /// <summary>契約失敗一律停止整案，固定訊息不展開 SDK XML、資料值或原始設定。</summary>
    /// <param name="condition">當前真實讀回結果必須符合的不變條件。</param>
    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidDataException("FS Live3P contract mismatch.");
    }

    /// <summary>
    /// 只借用當前租約的 SDK 能力邊界；允許結構驗證必要的 metadata 與 UI 查詢，
    /// 拒絕業務表、全欄位、關聯查詢與任何寫入。沒有連線、快取、計時器或 Dispose 所有權。
    /// </summary>
    /// <param name="inner">using lease 唯一擁有的 client，不得跨組織保留。</param>
    /// <param name="ct">同一案例的固定三分鐘內部取消訊號。</param>
    private sealed class ReadOnlySchemaService(IOrganizationService inner, CancellationToken ct) : IOrganizationService
    {
        /// <summary>只放行三種已知唯讀 metadata 訊息；同步完成並檢查取消後才交出回應。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            ct.ThrowIfCancellationRequested();
            if (request is not (RetrieveCurrentOrganizationRequest or RetrieveAllEntitiesRequest or RetrieveEntityRequest))
                throw Rejected();
            var response = inner.Execute(request);
            ct.ThrowIfCancellationRequested();
            return response;
        }

        /// <summary>只讀組織語言與正式 savedquery；不允許全欄位或任何業務資料表。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            ct.ThrowIfCancellationRequested();
            if (entityName is not ("organization" or "savedquery") || id == Guid.Empty || columnSet.AllColumns)
                throw Rejected();
            var row = inner.Retrieve(entityName, id, columnSet);
            ct.ThrowIfCancellationRequested();
            return row;
        }

        /// <summary>只允許引擎必要的有限欄位 metadata／UI 查詢；完整續頁及容量由正式投影器驗證。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            ct.ThrowIfCancellationRequested();
            if (query is not QueryExpression expression || expression.ColumnSet.AllColumns || expression.LinkEntities.Count != 0
                || expression.EntityName is not ("solution" or "publisher" or "solutioncomponent"
                    or "sdkmessagefilter" or "sdkmessageprocessingstep" or "systemform" or "savedquery"))
                throw Rejected();
            var rows = inner.RetrieveMultiple(query);
            ct.ThrowIfCancellationRequested();
            return rows;
        }

        /// <summary>實機驗收不建立業務列、檢視、自動化或其他資料。</summary>
        public Guid Create(Entity entity) => throw Rejected();
        /// <summary>實機驗收不更新結構、業務列、狀態或 Channel。</summary>
        public void Update(Entity entity) => throw Rejected();
        /// <summary>唯讀驗收不刪除任何既有或外部擁有的列。</summary>
        public void Delete(string entityName, Guid id) => throw Rejected();
        /// <summary>唯讀驗收不建立關聯，不藉此觸發自動化。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship,
            EntityReferenceCollection relatedEntities) => throw Rejected();
        /// <summary>唯讀驗收不解除關聯，不修改其他使用者的資料。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship,
            EntityReferenceCollection relatedEntities) => throw Rejected();
        /// <summary>拒絕訊息固定，不序列化 request、欄位值、端點或秘密。</summary>
        private static InvalidOperationException Rejected() => new("FS Live3P SDK capability rejected.");
    }
}

/*
檔案整體說明
目的與定位：FS 分層檢視表的真正 SDK 部署流程，將 SchemaView 接到 plan／apply／verify，
保存正式 SavedQueryId 與 XML 指紋，並把檢視加入 SpeechMessagePlatformUi 供定向匯出入。
主要流程：只讀指定結構範圍的已發布／未發布表單與檢視，以及平台 UI 方案元件；完整有界
盤點後才建立純值計畫。套用前重算指紋；未知未發布變更拒絕。新檢視的 ID 只取 SDK 回應，
既有檢視按計畫正式 ID 更新；同名多 ID 拒絕選取，不靜默覆蓋。QueryType 固定為零，
預設檢視若與既有其他元件衝突即停止，不以取消別人的預設值取得成功。
依賴與生命週期：借用命令擁有的 SDK，沒有 client／timer／背景工作或跨組織快取。
每集合最多四千零九十六列、每頁二百五十六列、每查詢最多十六頁，cookie 的 UTF-8 編碼最多十六 KiB；
XML 單欄最多一 MiB、完整盤點總計十六 MiB；兩來源正式型別／ID／所屬實體必須完整一致，
同一查詢的已發布／未發布來源各只使用一次正式主鍵排序，不因第二次讀取累加 ORDER BY。
新增／刪除的自動發布不作缺失來源的豁免。新表原生元件沿用引擎擷取的 nativeIds，不擴大授權。
發布前再次逐個核對本次寫入檢視的正式 ID、XML、啟用狀態及方案歸屬，不能只按 ID 放行漂移。續頁、重複 ID、
未知實體、取消與 SDK 故障均拒絕整個候選。證據只保存元件身份與 XML hash，沒有表單本文。
檢視 XML 的主鍵與物件類型來自本次正式 metadata，不能硬編 account 的物件類型給自訂表。
表單 formactivationstate 依官方 Picklist 契約只接受 SDK OptionSetValue，再複製其數值；
缺值、null 或其他型別整份拒絕，不以整數拆箱、文字轉換或預設狀態掩蓋不完整回應。
發布由外層結構引擎持同一寫鎖完成；本檔回傳本次擁有的正式 ID，不呼叫 PublishAllXml。
驗證方式：公開命令與 UI 故障測試驗零差異重跑、指紋漂移、未發布表單、部分建立失敗與重讀；
所有合成證據均屬離線，真實已發布結果仍須 Live3P SDK 讀回。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace SpeechMessage.Dynamics.Deployment.Schema;

/// <summary>單一檢視的計畫身份與差異；新元件的空 ID 只代表尚未建立，不作資料庫身份使用。</summary>
internal sealed record UiViewTarget(SchemaView Definition, Guid Id, string Fingerprint, bool NeedsWrite, bool Included);

/// <summary>綁入完整 schema-plan 雜湊的 UI 純值計畫；不含 SDK、表單本文或任何秘密。</summary>
internal sealed record UiViewPlan(string Solution, string Product, string Version, Guid SolutionId,
    string Fingerprint, IReadOnlyList<UiViewTarget> Views, int Changes);

/// <summary>已完整盤點的單一 UI 元件，僅在命令內保留 XML，檔案證據只使用其 hash。</summary>
internal sealed record UiViewState(Guid Id, string Type, string Entity, string Name, int QueryType,
    bool IsDefault, int State, bool Managed, string FetchXml, string LayoutXml, string Hash);

/// <summary>一次盤點的完整純值範圍；沒有連線、lease、request 或背景工作所有權。</summary>
internal sealed record UiViewScope(Guid SolutionId, string SolutionVersion, Guid PublisherId,
    IReadOnlyList<Guid> IncludedIds, IReadOnlyList<UiViewState> Published, IReadOnlyList<UiViewState> Unpublished);

/// <summary>FS 公用檢視 SDK 邊界；所有 mutable 候選均隨單一命令釋放，不跨使用者或組織重用。</summary>
internal static class UiViewDeployment
{
    internal const string SolutionName = "SpeechMessagePlatformUi";
    private const string Version = "1.0.0.0";
    private const int MaximumPages = 16;
    private const int MaximumCookieBytes = 16 * 1024;
    private static readonly string[] ViewColumns =
        { "savedqueryid", "returnedtypecode", "name", "querytype", "isdefault", "statecode", "ismanaged", "fetchxml", "layoutxml" };
    private static readonly string[] FormColumns = { "formid", "objecttypecode", "name", "type", "formactivationstate", "ismanaged", "formxml" };

    /// <summary>由 SDK 現況產生 UI 計畫；verify 僅用已發布資料，plan 同時記下未發布範圍指紋。</summary>
    internal static UiViewPlan Plan(IOrganizationService service, SchemaDefinition definition, SchemaSnapshot metadata,
        CancellationToken ct, bool publishedOnly = false)
    {
        var scope = ReadScope(service, definition, metadata, ct, publishedOnly);
        var approvedPublisher = metadata.Publishers.SingleOrDefault(row => row.Name == definition.Publisher);
        if (scope.SolutionId != Guid.Empty && (scope.SolutionVersion != Version
            || approvedPublisher is null || scope.PublisherId != approvedPublisher.Id))
            throw new InvalidDataException("UI solution version or publisher does not match the approved layer.");
        var source = publishedOnly ? scope.Published : scope.Unpublished;
        var pending = publishedOnly ? new HashSet<Guid>() : Pending(scope).ToHashSet();
        var views = new List<UiViewTarget>();
        foreach (var view in definition.Views)
        {
            var matches = source.Where(row => row.Type == "savedquery" && row.Entity == view.Entity && row.Name == view.Name).ToArray();
            if (matches.Length > 1 || matches.Any(row => row.QueryType != 0 || row.Managed))
                throw new InvalidDataException("The approved public view has ambiguous or managed identity.");
            var existing = matches.SingleOrDefault();
            if (view.IsDefault && source.Any(row => row.Type == "savedquery" && row.Entity == view.Entity
                    && row.QueryType == 0 && row.IsDefault && row.Id != existing?.Id))
                throw new InvalidDataException("A different default public view already exists.");
            var matchesXml = false;
            if (existing is not null)
            {
                var generated = Generate(service, definition, view, ct);
                matchesXml = existing.FetchXml == generated.FetchXml && existing.LayoutXml == generated.LayoutXml
                    && existing.State == 0 && existing.IsDefault == view.IsDefault;
            }
            var included = existing is not null && scope.IncludedIds.Contains(existing.Id);
            views.Add(new UiViewTarget(view, existing?.Id ?? Guid.Empty, existing?.Hash ?? "",
                !matchesXml || existing is not null && pending.Contains(existing.Id), included));
        }
        return new UiViewPlan(SolutionName, Product(metadata.Organization), Version, scope.SolutionId, ScopeHash(scope),
            views.AsReadOnly(), (scope.SolutionId == Guid.Empty ? 1 : 0)
                + views.Count(view => view.NeedsWrite) + views.Count(view => !view.Included));
    }

    /// <summary>完整重算原 UI 計畫與未發布差異，任何漂移或計畫外元件在結構與 UI 首次寫入前拒絕。</summary>
    internal static void Validate(IOrganizationService service, SchemaDefinition definition, SchemaSnapshot metadata,
        UiViewPlan? plan, CancellationToken ct)
    {
        if (plan is null || SchemaFiles.HashValue(plan) != SchemaFiles.HashValue(Plan(service, definition, metadata, ct)))
            throw new InvalidDataException("UI plan no longer matches its approved component fingerprints.");
        var scope = ReadScope(service, definition, metadata, ct);
        UiPublishScope.Check(Pending(scope), plan.Views.Where(view => view.Id != Guid.Empty && view.NeedsWrite).Select(view => view.Id));
    }

    /// <summary>
    /// 結構新增後以正式 metadata 產生檢視，只寫計畫元件並加入平台 UI 方案；發布由呼叫端統一處理。
    /// 回傳本次正式元件 ID，建立失敗保留已完成元件，重新 plan 才能接續缺項。
    /// </summary>
    internal static IReadOnlyList<Guid> Apply(IOrganizationService service, SchemaDefinition definition, SchemaSnapshot metadata,
        UiViewPlan plan, string directory, CancellationToken ct)
    {
        if (plan.Solution != SolutionName || plan.Product != Product(metadata.Organization) || plan.Version != Version)
            throw new InvalidDataException("UI plan scope is invalid.");
        var scope = ReadScope(service, definition, metadata, ct);
        if (scope.SolutionId != plan.SolutionId) throw new InvalidDataException("UI solution identity drifted.");
        if (scope.SolutionId == Guid.Empty)
        {
            var publisher = metadata.Publishers.SingleOrDefault(row => row.Name == definition.Publisher)
                ?? throw new InvalidDataException("The approved UI publisher is unavailable.");
            ct.ThrowIfCancellationRequested();
            RequireId(service.Create(new Entity("solution")
            {
                ["uniquename"] = SolutionName, ["friendlyname"] = SolutionName, ["version"] = Version,
                ["publisherid"] = new EntityReference("publisher", publisher.Id)
            }));
            ct.ThrowIfCancellationRequested();
        }
        var owned = new List<Guid>();
        var ledger = new List<UiComponentLedgerEntry>();
        var includedEntities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in plan.Views)
        {
            ct.ThrowIfCancellationRequested();
            var view = target.Definition;
            var generated = Generate(service, definition, view, ct);
            var row = new Entity("savedquery", target.Id)
            {
                ["name"] = view.Name, ["returnedtypecode"] = view.Entity, ["querytype"] = 0,
                ["fetchxml"] = generated.FetchXml, ["layoutxml"] = generated.LayoutXml, ["isdefault"] = view.IsDefault
            };
            var id = target.Id;
            if (target.NeedsWrite)
            {
                if (id == Guid.Empty) id = RequireId(service.Create(row));
                else
                {
                    row["statecode"] = new OptionSetValue(0);
                    row["statuscode"] = new OptionSetValue(1);
                    service.Update(row);
                }
                ct.ThrowIfCancellationRequested();
            }
            RequireId(id);
            owned.Add(id);
            if (!target.Included)
            {
                if (includedEntities.Add(view.Entity))
                {
                    var entity = metadata.Entities.Single(item => item.LogicalName == view.Entity);
                    Include(service, 1, entity.Id, ct);
                }
                Include(service, 26, id, ct);
            }
            ledger.Add(UiComponentLedger.Capture(SolutionName, plan.Product, Version, view.Entity, "savedquery", id,
                generated.FetchXml + "\n" + generated.LayoutXml));
        }
        _ = SchemaFiles.WriteNew(directory, "ui-components.json", ledger);
        return owned.AsReadOnly();
    }

    /// <summary>
    /// 兩來源已由 ReadRows 完整盤點且身份唯一；比較正式型別／ID 集合與所屬實體後才回傳 XML 等內容差異。
    /// 平台新增／刪除自動發布，任一側缺失仍屬盤點不完整或期間漂移，不能當成零差異或任意新元件放行。
    /// 新表原生元件須同時存在兩來源，其內容差異仍交引擎既有 nativeIds 核對，不在此放寬授權。
    /// </summary>
    /// <exception cref="InvalidDataException">兩來源身份集合或同一正式元件所屬實體不一致，拒絕發布部分候選。</exception>
    internal static IReadOnlyList<Guid> Pending(UiViewScope scope)
    {
        var published = scope.Published.ToDictionary(row => (row.Type, row.Id));
        var unpublished = scope.Unpublished.ToDictionary(row => (row.Type, row.Id));
        if (published.Count != unpublished.Count || published.Any(pair =>
                !unpublished.TryGetValue(pair.Key, out var row) || row.Entity != pair.Value.Entity))
            throw new InvalidDataException("Published and unpublished UI identities are incomplete or changed.");
        return scope.Unpublished.Where(row => published[(row.Type, row.Id)].Hash != row.Hash)
            .Select(row => row.Id).ToArray();
    }

    /// <summary>
    /// 發布前核對本次實際 savedquery；即使 ID 在許可集合，XML 漂移仍須拒絕。
    /// scope 來自同一命令的最後一次完整盤點，正式 ID 只能來自原 plan 或本次 SDK 建立回應。
    /// </summary>
    internal static void CheckBeforePublish(IOrganizationService service, SchemaDefinition definition,
        SchemaSnapshot metadata, UiViewPlan plan, IReadOnlyList<Guid> writtenIds, UiViewScope scope, CancellationToken ct)
    {
        var publisher = metadata.Publishers.SingleOrDefault(row => row.Name == definition.Publisher);
        if (scope.SolutionId == Guid.Empty || scope.SolutionVersion != Version
            || publisher is null || scope.PublisherId != publisher.Id || writtenIds.Count != plan.Views.Count)
            throw new InvalidDataException("Written UI solution scope is inconsistent.");
        for (var index = 0; index < plan.Views.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var target = plan.Views[index];
            var id = RequireId(writtenIds[index]);
            if (target.Id != Guid.Empty && target.Id != id)
                throw new InvalidDataException("Written UI identity does not match the approved plan.");
            var row = scope.Unpublished.SingleOrDefault(item => item.Type == "savedquery" && item.Id == id);
            var expected = Generate(service, definition, target.Definition, ct);
            if (row is null || row.Entity != target.Definition.Entity || row.Name != target.Definition.Name
                || row.QueryType != 0 || row.Managed || row.State != 0 || row.IsDefault != target.Definition.IsDefault
                || row.FetchXml != expected.FetchXml || row.LayoutXml != expected.LayoutXml || !scope.IncludedIds.Contains(id))
                throw new InvalidDataException("Written UI component changed before publication.");
        }
    }

    /// <summary>完整讀取已存在受管實體的 UI 定義及平台方案元件；不存在的新表不送出無效 objecttype 查詢。</summary>
    internal static UiViewScope ReadScope(IOrganizationService service, SchemaDefinition definition, SchemaSnapshot metadata,
        CancellationToken ct, bool publishedOnly = false)
    {
        var entities = definition.Entities.Select(row => row.LogicalName)
            .Intersect(metadata.Entities.Select(row => row.LogicalName), StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var published = new List<UiViewState>();
        var unpublished = new List<UiViewState>();
        long xmlBytes = 0;
        foreach (var type in new[] { "systemform", "savedquery" })
        {
            if (entities.Length == 0) break;
            var query = new QueryExpression(type) { ColumnSet = new ColumnSet(type == "savedquery" ? ViewColumns : FormColumns) };
            query.Criteria.AddCondition(type == "savedquery" ? "returnedtypecode" : "objecttypecode", ConditionOperator.In, entities.Cast<object>().ToArray());
            published.AddRange(ReadRows(service, query, false, ct, ref xmlBytes).Select(row => Project(row, entities)));
            if (!publishedOnly)
                unpublished.AddRange(ReadRows(service, query, true, ct, ref xmlBytes).Select(row => Project(row, entities)));
        }
        if (publishedOnly) unpublished.AddRange(published);
        var solutions = ReadRows(service, new QueryExpression("solution")
        {
            ColumnSet = new ColumnSet("solutionid", "uniquename", "version", "publisherid", "ismanaged"),
            Criteria = new FilterExpression { Conditions = { new ConditionExpression("uniquename", ConditionOperator.Equal, SolutionName) } }
        }, false, ct, ref xmlBytes);
        if (solutions.Count > 1 || solutions.Any(row => row.GetAttributeValue<string>("uniquename") != SolutionName
                || row.GetAttributeValue<bool>("ismanaged")))
            throw new InvalidDataException("UI solution identity is ambiguous or managed.");
        var solution = solutions.SingleOrDefault();
        var included = solution is null ? new List<Entity>() : ReadRows(service, new QueryExpression("solutioncomponent")
        {
            ColumnSet = new ColumnSet("solutioncomponentid", "objectid", "componenttype"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("solutionid", ConditionOperator.Equal, solution.Id),
                    new ConditionExpression("componenttype", ConditionOperator.Equal, 26)
                }
            }
        }, false, ct, ref xmlBytes);
        var includedIds = included.Select(row => row.GetAttributeValue<Guid>("objectid")).ToArray();
        if (includedIds.Any(id => id == Guid.Empty) || includedIds.Distinct().Count() != includedIds.Length)
            throw new InvalidDataException("UI solution component identity is invalid.");
        return new UiViewScope(solution?.Id ?? Guid.Empty, solution?.GetAttributeValue<string>("version") ?? "",
            solution?.GetAttributeValue<EntityReference>("publisherid")?.Id ?? Guid.Empty,
            Array.AsReadOnly(includedIds.Order().ToArray()), published.OrderBy(row => row.Type, StringComparer.Ordinal).ThenBy(row => row.Id).ToArray(),
            unpublished.OrderBy(row => row.Type, StringComparer.Ordinal).ThenBy(row => row.Id).ToArray());
    }

    /// <summary>
    /// 每頁最多二百五十六列、每查詢十六頁，續頁 cookie 最多十六 KiB UTF-8；即使每頁僅一列也不得送第十七次 SDK 查詢。
    /// 先限 cookie 字元數再計位元組，避免超大回應造成額外無界掃描；末頁 cookie 亦受同一上限。
    /// 候選、身份與 cookie 集合只活到本次讀取結束，空續頁、重複 cookie／ID 或任何超量整份拒絕，不回部分結果。
    /// </summary>
    private static List<Entity> ReadRows(IOrganizationService service, QueryExpression query, bool unpublished,
        CancellationToken ct, ref long xmlBytes)
    {
        var rows = new List<Entity>();
        var ids = new HashSet<Guid>();
        var cookies = new HashSet<string>(StringComparer.Ordinal);
        query.PageInfo = new PagingInfo { Count = 256, PageNumber = 1 };
        // query 僅由本類建立；第二個發布來源沿用原範圍與唯一排序，不能重複追加相同欄位。
        if (query.Orders.Count == 0)
            query.AddOrder(query.EntityName == "systemform" ? "formid" : query.EntityName + "id", OrderType.Ascending);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = unpublished
                ? (service.Execute(new RetrieveUnpublishedMultipleRequest { Query = query }) as RetrieveUnpublishedMultipleResponse)?.EntityCollection
                : service.RetrieveMultiple(query);
            ct.ThrowIfCancellationRequested();
            if (page is null || page.Entities.Count > 256 || page.PagingCookie is { } cookie
                && (cookie.Length > MaximumCookieBytes || Encoding.UTF8.GetByteCount(cookie) > MaximumCookieBytes))
                throw new InvalidDataException("Invalid UI metadata page.");
            foreach (var row in page.Entities)
            {
                if (row.Id == Guid.Empty || row.LogicalName != query.EntityName || !ids.Add(row.Id) || rows.Count >= 4096)
                    throw new InvalidDataException("Invalid, duplicate or excessive UI component identity.");
                foreach (var column in new[] { "formxml", "fetchxml", "layoutxml" })
                {
                    var count = Encoding.UTF8.GetByteCount(row.GetAttributeValue<string>(column) ?? "");
                    xmlBytes += count;
                    if (count > 1_048_576 || xmlBytes > 16 * 1_048_576)
                        throw new InvalidDataException("UI XML exceeds its per-column or complete-scope budget.");
                }
                rows.Add(row);
            }
            if (!page.MoreRecords) return rows;
            if (page.Entities.Count == 0 || query.PageInfo.PageNumber >= MaximumPages
                || string.IsNullOrWhiteSpace(page.PagingCookie) || !cookies.Add(page.PagingCookie))
                throw new InvalidDataException("Incomplete UI metadata paging.");
            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }
    }

    /// <summary>
    /// 複製 UI metadata 到命令純值；完整 XML 僅供比對，證據與 ledger 使用其 SHA-256。
    /// systemform.formactivationstate 是 Picklist，SDK 使用 OptionSetValue；僅複製 Value，
    /// 不保留 SDK 參照，缺值或錯型別立即拒絕，避免以未知狀態繼續規劃或掩蓋回應契約錯誤。
    /// </summary>
    /// <exception cref="InvalidDataException">範圍、XML 預算或表單狀態型別不符，整個候選不可發布。</exception>
    private static UiViewState Project(Entity row, string[] scope)
    {
        var isView = row.LogicalName == "savedquery";
        var entity = row.GetAttributeValue<string>(isView ? "returnedtypecode" : "objecttypecode") ?? "";
        if (!scope.Contains(entity, StringComparer.Ordinal)) throw new InvalidDataException("UI metadata returned an out-of-scope entity.");
        var fetch = row.GetAttributeValue<string>(isView ? "fetchxml" : "formxml") ?? "";
        var layout = isView ? row.GetAttributeValue<string>("layoutxml") ?? "" : "";
        if (fetch.Length > 1_048_576 || layout.Length > 1_048_576) throw new InvalidDataException("UI XML exceeds its budget.");
        var state = isView ? row.GetAttributeValue<OptionSetValue>("statecode")?.Value ?? -1
            : row.Attributes.TryGetValue("formactivationstate", out var rawState) && rawState is OptionSetValue activationState
                ? activationState.Value
                : throw new InvalidDataException("Form activation state must be an SDK choice value.");
        var candidate = new UiViewState(row.Id, row.LogicalName, entity, row.GetAttributeValue<string>("name") ?? "",
            isView ? row.GetAttributeValue<int>("querytype") : row.GetAttributeValue<OptionSetValue>("type")?.Value ?? -1,
            isView && row.GetAttributeValue<bool>("isdefault"), state, row.GetAttributeValue<bool>("ismanaged"), fetch, layout, "");
        return candidate with { Hash = SchemaFiles.HashValue(candidate) };
    }

    /// <summary>scope 指紋只存元件正式身份／hash，不把表單本文複製到計畫或紀錄。</summary>
    private static string ScopeHash(UiViewScope scope) => SchemaFiles.HashValue(new
    {
        scope.SolutionId, scope.SolutionVersion, scope.PublisherId, scope.IncludedIds,
        Published = scope.Published.Select(row => new { row.Id, row.Type, row.Entity, row.Hash }),
        Unpublished = scope.Unpublished.Select(row => new { row.Id, row.Type, row.Entity, row.Hash })
    });

    /// <summary>每次使用 server 正式主鍵及物件類型產生 LayoutXml，不以新建時的暫存值代替。</summary>
    private static GeneratedView Generate(IOrganizationService service, SchemaDefinition definition, SchemaView view, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var response = service.Execute(new RetrieveEntityRequest { LogicalName = view.Entity, EntityFilters = EntityFilters.Entity, RetrieveAsIfPublished = true });
        ct.ThrowIfCancellationRequested();
        var entity = (response as RetrieveEntityResponse)?.EntityMetadata;
        if (entity?.MetadataId is not { } id || id == Guid.Empty || entity.LogicalName != view.Entity
            || entity.ObjectTypeCode is not > 0 || string.IsNullOrWhiteSpace(entity.PrimaryIdAttribute))
            throw new InvalidDataException("View entity metadata has no authoritative identity.");
        return ViewDefinitions.Generate(definition, view, primaryId: entity.PrimaryIdAttribute, objectTypeCode: entity.ObjectTypeCode.Value);
    }

    /// <summary>只把本次已確認的正式元件加入平台 UI 方案，不自動加入其他必要元件或擴大發布範圍。</summary>
    private static void Include(IOrganizationService service, int type, Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (service.Execute(new AddSolutionComponentRequest
        {
            ComponentType = type, ComponentId = RequireId(id), SolutionUniqueName = SolutionName,
            AddRequiredComponents = false, DoNotIncludeSubcomponents = true
        }) is not AddSolutionComponentResponse) throw new InvalidDataException("Unexpected UI component response.");
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>產品來自已驗證 server 組織；未知組織拒絕，不能拿呼叫端任意 Product 作授權。</summary>
    private static string Product(string organization) => organization switch
    {
        "goodshepherd20" => "GoodShepherd2", "construction" => "GoodButler", "association" => "GoodGuild",
        _ => throw new InvalidDataException("UI layer organization is not approved.")
    };

    /// <summary>元件身份必須取 SDK 正式回應；缺少時停止而不製造臨時替代 ID。</summary>
    private static Guid RequireId(Guid id) => id != Guid.Empty ? id : throw new InvalidDataException("UI component has no stable identity.");
}

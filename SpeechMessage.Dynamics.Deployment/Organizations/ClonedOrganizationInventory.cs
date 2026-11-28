/*
 * 檔案整體說明
 *
 * 目的與定位：線 A 的 F0 Step 3 唯讀盤點邊界，供 FS quiesce 分類前使用；
 * 僅接受伺服器 UniqueName 精確等於 goodshepherd20 且 OrganizationId 符合預期的測試複本。
 * 主要流程：先核實 server 身分，再讀已發布 entity／attribute metadata；
 * 以程式內固定白名單與實際 metadata 交集選表，業務表只送 FetchXML aggregate count，
 * 管理表只讀明列的正式 ID、管理名稱、狀態及管理關聯。不存在的表以 Exists=false、
 * RecordCount=null 回報，不能把缺表、缺欄、故障或不完整查詢解釋成零筆。
 * 管理範圍：流程僅定義 type=1，非同步工作僅等待／暫停 state=0/1；
 * Plugin assembly→type→step 及 message/filter、信箱三種同步狀態、方案／表單／公用檢視
 * 的存在性及 managed 屬性。不讀業務 ID／內容、會友欄值、token、LINE ID、configuration、
 * XAML、trace、郵件位址、外部網址、FormXml、FetchXml 或 LayoutXml 的定義值。
 * 資料形狀：sdkmessageprocessingstep.name 只是可空白的管理顯示值，列的正式身份始終是
 * server primary ID；savedquery.statuscode 依 Dataverse metadata 為選用欄位，缺少時不推造預設值。
 * 隔離與發布：只回不可變純值；所有候選、ID 集合與 cookie 都屬單次 Read；
 * 全部 I/O、身分、聚合及關聯驗證成功後才發布，沒有 cache、static mutable state 或背景 Task。
 * 有界契約：metadata 至多4096表、每表2048欄、總共100000欄；管理每表最多十頁、
 * 內建欄位允許 ASCII 大小寫混合並精確保留原身分；可查詢的表與欄仍只由固定白名單決定。
 * 每頁5000筆，所有管理列合計最多50000筆；cookie 至多65536字元並禁止 DTD／外部解析。
 * server aggregate 的筆數上限與權限錯誤直接保留原 SDK 例外，不改抓資料列計數。
 * 聚合相容：依 Microsoft FetchXML SDK 範例，允許固定 inventory_count alias 選帶
 * 同鍵的一筆非空 FormattedValues（最多64字元），僅驗證外形後捨棄，不解析或發布展示文字。
 * AliasedValue 的來源表與原始主鍵名稱仍精確核對；缺來源、改用結果 alias、額外資料、
 * 非整數、範圍或容量故障不因展示註記而放行。這項離線契約不證明實機失敗根因。
 * 使用邊界與維護：IOrganizationService 只向外層獨占 lease 借用，transport timeout 由其設定；
 * 同步 SDK 呼叫前後檢查 caller 取消，入場後先返回／drain，外層才可 Dispose lease。
 * 本檔不建立連線、timer、工作或通知器；真正失敗保留原例外交由命令共用告警處理，
 * 本地格式拒絕只用固定訊息，不回顯 SDK 內容、cookie 或來源字串。
 * 驗證方式：ClonedOrganizationInventoryTests 以合成 SDK 保護核身順序、零寫入、alias
 * 獨立驗證、完整分頁、同名不同 ID、缺表、取消、mutation 隔離及回收；不代表實機驗收。
 */
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Organization;
using Microsoft.Xrm.Sdk.Query;

namespace SpeechMessage.Dynamics.Deployment.Organizations;

/// <summary>
/// 一次核身後的完整純值盤點；本型別不授權 quiesce，也不證明複本已隔離或已獲准寫入。
/// 集合由 reader 複製並封裝，不帶 SDK、lease、credential 或可跨操作重用的候選。
/// </summary>
/// <param name="OrganizationId">伺服器正式組織 ID，已與明確預期值比對。</param>
/// <param name="UniqueName">經精確白名單驗證的伺服器名稱。</param>
/// <param name="Tables">每個固定候選的存在性、欄位 metadata 與適用的聚合筆數。</param>
/// <param name="Management">完整讀取並驗證後的管理列；非同步工作及流程採檔首明列範圍。</param>
internal sealed record ClonedOrganizationInventoryResult(
    Guid OrganizationId,
    string UniqueName,
    IReadOnlyList<ClonedInventoryTable> Tables,
    IReadOnlyList<ClonedInventoryManagementRow> Management);

/// <summary>
/// 固定白名單表的已發布 schema 與計數；Attributes 只有欄位邏輯名稱，絕不是欄位值。
/// 業務表存在時 RecordCount 必有值；管理表的數量可由其完整管理清冊得出，這裡不冒充業務聚合。
/// </summary>
/// <param name="LogicalName">程式固定核定的候選表。</param>
/// <param name="Exists">server metadata 中是否存在；false 與零筆不同。</param>
/// <param name="MetadataId">存在時的正式 metadata ID；不代表任何業務記錄 ID。</param>
/// <param name="IsManaged">server managed 屬性，未知維持 null，不猜為 false。</param>
/// <param name="PrimaryIdAttribute">只供伺服器聚合及管理身分驗證的主鍵欄位名稱。</param>
/// <param name="Attributes">獨立唯讀的已發布欄位名稱集合，不含 label 或定義 XML。</param>
/// <param name="RecordCount">僅業務候選的 server aggregate count；缺表及管理候選為 null。</param>
/// <param name="PendingOutboundCount">只在 outbound 表及 new_taskstatus 欄存在時聚合 Pending=100000000；其他情況 null。</param>
internal sealed record ClonedInventoryTable(
    string LogicalName,
    bool Exists,
    Guid? MetadataId,
    bool? IsManaged,
    string? PrimaryIdAttribute,
    IReadOnlyList<string> Attributes,
    long? RecordCount,
    long? PendingOutboundCount);

/// <summary>管理關聯的正式目標；只存白名單管理表名稱與 ID，刻意不保存 EntityReference.Name 或 SDK 參照。</summary>
/// <param name="Entity">該欄位允許的管理目標邏輯名稱。</param>
/// <param name="Id">非空正式管理 ID；不能是業務 ID、呼叫端索引或臨時 draft key。</param>
internal sealed record ClonedInventoryReference(string Entity, Guid Id);

/// <summary>
/// 固定管理欄位的明確純值投影；欄位鍵由下方各表的白名單定義，不接受任意 object 或 SDK 值。
/// 缺少選填值代表 server 未提供，必填值缺少則整份拒絕；管理名稱僅用於人工分類，不能作身分或去重鍵。
/// </summary>
/// <param name="Entity">workflow、asyncoperation、Plugin、mailbox、solution、systemform 或 savedquery 等白名單管理表。</param>
/// <param name="Id">該管理表的正式主鍵；同名不同 ID 保留。</param>
/// <param name="Text">管理名稱、型別名稱、版本、實體名稱及 filteringattributes，不含設定本文或定義 XML。</param>
/// <param name="Options">state/status/mode/stage/type、querytype、rank 及同步方式等整數純值。</param>
/// <param name="Flags">只保存白名單中的 ismanaged 布林值。</param>
/// <param name="References">Plugin 管理關聯及選填 workflowactivationid；不允許使用者、會友、租戶或業務關聯。</param>
internal sealed record ClonedInventoryManagementRow(
    string Entity,
    Guid Id,
    IReadOnlyDictionary<string, string> Text,
    IReadOnlyDictionary<string, int> Options,
    IReadOnlyDictionary<string, bool> Flags,
    IReadOnlyDictionary<string, ClonedInventoryReference> References);

/// <summary>
/// F0 專用同步唯讀 reader。API 不接受任意組織名稱、欄位、FetchXML 或 table 清單，
/// 外層負責既有連線 lease、逾時與錯誤通知；本類不接管、不保留或 Dispose 借來的 service。
/// </summary>
internal static class ClonedOrganizationInventory
{
    /// <summary>
    /// 核身後完整盤點複製測試組織；任一步失敗不回部分結果，也不做重試、寫入或名稱推測。
    /// 本方法僅用管理資料與伺服器聚合，不能藉此證明目前登入者能看見權限以外的全組織資料。
    /// </summary>
    /// <param name="service">外層獨占 lease 借出的服務；呼叫期間不可切換身分或與其他命令共用。</param>
    /// <param name="cancellationToken">caller 明確取消；同步 SDK 已入場時必須先 drain 才返回。</param>
    /// <param name="expectedOrganizationId">省略時釘選 ec4d281b-bbe7-f111-8cce-00155d007916；明傳只能調整預期 ID，不能放寬 goodshepherd20 名稱。</param>
    /// <returns>全部驗證成功才發布的單次純值結果；缺表不回零，沒有未觀測背景工作。</returns>
    internal static ClonedOrganizationInventoryResult Read(
        IOrganizationService service,
        CancellationToken cancellationToken,
        Guid? expectedOrganizationId = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        var expected = expectedOrganizationId ?? new Guid("ec4d281b-bbe7-f111-8cce-00155d007916");
        if (expected == Guid.Empty)
            throw new ArgumentException("An expected organization identity is required.", nameof(expectedOrganizationId));
        var identity = Execute<RetrieveCurrentOrganizationResponse>(service,
            new RetrieveCurrentOrganizationRequest { AccessType = EndpointAccessType.Default }, cancellationToken);
        if (!identity.Results.TryGetValue("Detail", out var detail) || detail is not OrganizationDetail current
            || current.OrganizationId != expected
            || !string.Equals(current.UniqueName, "goodshepherd20", StringComparison.Ordinal))
            throw new InvalidDataException("Inventory server identity does not match the authorized test clone.");

        var response = Execute<RetrieveAllEntitiesResponse>(service, new RetrieveAllEntitiesRequest
        {
            EntityFilters = EntityFilters.Entity | EntityFilters.Attributes,
            RetrieveAsIfPublished = false
        }, cancellationToken);
        if (!response.Results.TryGetValue("EntityMetadata", out var raw) || raw is not EntityMetadata[] metadata)
            throw new InvalidDataException("Inventory metadata response is incomplete.");
        var schemas = ValidateMetadata(metadata, cancellationToken);

        // 這些名稱來自既有產品／schema；不依 display name、模糊字串或未知 new_ 前綴擴大讀取。
        // list 只盤點 schema 與筆數，不能讀其動態名單 query、會員 ID 或任何名單資料。
        string[] businessTables =
        [
            "account", "contact", "list", "annotation", "new_present_record",
            "new_group_present_weekly_report", "new_stor_lessons", "new_fee",
            "new_dedication_booking", "new_offeringcategory", "new_linechannel",
            "new_outboundtask", "new_localizedtext"
        ];
        var specs = ManagementSpecs();
        var tables = new List<ClonedInventoryTable>();
        var management = new List<ClonedInventoryManagementRow>();
        foreach (var name in businessTables.Concat(specs.Select(spec => spec.Entity)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!schemas.TryGetValue(name, out var schema))
            {
                tables.Add(new ClonedInventoryTable(name, false, null, null, null, Array.Empty<string>(), null, null));
                continue;
            }
            var attributes = Array.AsReadOnly(schema.Attributes.Select(field => field.LogicalName)
                .Order(StringComparer.Ordinal).ToArray());
            long? count = null;
            long? pending = null;
            if (Array.IndexOf(businessTables, name) >= 0)
            {
                count = ReadCount(service, schema, false, cancellationToken);
                if (name == "new_outboundtask" && attributes.Contains("new_taskstatus", StringComparer.Ordinal))
                    pending = ReadCount(service, schema, true, cancellationToken);
            }
            tables.Add(new ClonedInventoryTable(name, true, schema.MetadataId, schema.IsManaged,
                schema.PrimaryIdAttribute, attributes, count, pending));
        }
        foreach (var spec in specs)
            if (schemas.TryGetValue(spec.Entity, out var schema))
                ReadManagement(service, schema, spec, management, cancellationToken);
        ValidateRelationships(management);
        cancellationToken.ThrowIfCancellationRequested();
        return new ClonedOrganizationInventoryResult(current.OrganizationId, current.UniqueName,
            tables.AsReadOnly(), management.AsReadOnly());
    }

    /// <summary>
    /// metadata 亦須有真實、唯一、容量有界的身分；驗證整個回應，防止重複表遮蓋或缺主鍵造成模糊選表。
    /// 只在本次呼叫保留 SDK metadata，發布時另複製名稱純值，不保存 schema／Label 參照。
    /// </summary>
    private static Dictionary<string, EntityMetadata> ValidateMetadata(EntityMetadata[] metadata, CancellationToken cancellationToken)
    {
        if (metadata.Length is < 1 or > 4096)
            throw new InvalidDataException("Inventory metadata is empty or exceeds its table budget.");
        var schemas = new Dictionary<string, EntityMetadata>(StringComparer.Ordinal);
        var tableIds = new HashSet<Guid>();
        var fieldIds = new HashSet<Guid>();
        var totalFields = 0;
        foreach (var table in metadata)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (table is null || !LogicalName(table.LogicalName) || table.MetadataId is not { } id
                || id == Guid.Empty || !tableIds.Add(id) || !schemas.TryAdd(table.LogicalName, table)
                || !LogicalName(table.PrimaryIdAttribute) || table.Attributes is null
                || table.Attributes.Length > 2048 || table.Attributes.Length > 100000 - totalFields)
                throw new InvalidDataException("Inventory table metadata is invalid or exceeds its budget.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in table.Attributes)
                if (field is null || !MetadataAttributeName(field.LogicalName) || !names.Add(field.LogicalName)
                    || field.MetadataId is not { } fieldId || fieldId == Guid.Empty || !fieldIds.Add(fieldId))
                    throw new InvalidDataException("Inventory field metadata identity is missing or duplicated.");
            if (!names.Contains(table.PrimaryIdAttribute))
                throw new InvalidDataException("Inventory metadata has no authoritative primary key.");
            totalFields += table.Attributes.Length;
        }
        return schemas;
    }

    /// <summary>
    /// 業務數量只向 server 聚合主鍵，不取回主鍵值；聚合結果沒有 RecordId，必須獨立驗證單列 alias。
    /// 缺列、多列、截斷、額外欄位、錯來源或非非負整數均拒絕；SDK aggregate limit fault 原樣向外傳遞。
    /// 同 alias 可選帶一筆最多64字元的非空格式註記；展示值受伺服器使用者格式設定影響，
    /// 不用來修補、解析或取代原始整數，也不複製到結果，因此不引入跨操作保留或額外業務欄位。
    /// </summary>
    /// <remarks>
    /// 官方 FetchXML SDK 範例按回應 alias 選用
    /// <see href="https://learn.microsoft.com/en-us/power-apps/developer/data-platform/fetchxml/select-columns#aliased-and-aggregated-values">FormattedValues</see>；
    /// <see href="https://learn.microsoft.com/en-us/dotnet/api/microsoft.xrm.sdk.aliasedvalue.attributelogicalname">AttributeLogicalName</see>
    /// 仍代表來源欄位，沒有省略或改成結果 alias 的相容推論。64字元是本 reader 的本地容量上限。
    /// </remarks>
    /// <param name="service">向外層獨占 lease 借用的同步 SDK；本方法不接管或釋放連線。</param>
    /// <param name="schema">本次已完整驗證的白名單表與正式主鍵 metadata，不接受呼叫端任意 FetchXML。</param>
    /// <param name="pending">僅由 outbound 白名單分支使用，將固定待送狀態條件留在伺服器聚合。</param>
    /// <param name="cancellationToken">同步 SDK 呼叫前後的 caller 取消；已入場時先 drain 再離開。</param>
    /// <returns>原始 AliasedValue 中的非負 Int32／Int64；回應缺失或形狀衝突一律拋出固定錯誤。</returns>
    private static long ReadCount(IOrganizationService service, EntityMetadata schema, bool pending, CancellationToken cancellationToken)
    {
        var entity = new XElement("entity", new XAttribute("name", schema.LogicalName),
            new XElement("attribute", new XAttribute("name", schema.PrimaryIdAttribute),
                new XAttribute("alias", "inventory_count"), new XAttribute("aggregate", "count")));
        if (pending)
            entity.Add(new XElement("filter", new XAttribute("type", "and"),
                new XElement("condition", new XAttribute("attribute", "new_taskstatus"),
                    new XAttribute("operator", "eq"), new XAttribute("value", "100000000"))));
        var fetch = new FetchExpression(new XElement("fetch", new XAttribute("aggregate", "true"), entity)
            .ToString(SaveOptions.DisableFormatting));
        cancellationToken.ThrowIfCancellationRequested();
        var page = service.RetrieveMultiple(fetch);
        cancellationToken.ThrowIfCancellationRequested();
        if (page is null || page.MoreRecords || page.TotalRecordCountLimitExceeded
            || !string.IsNullOrEmpty(page.PagingCookie) || page.Entities.Count != 1
            || !CollectionName(page, schema.LogicalName))
            throw new InvalidDataException("Inventory aggregate is incomplete.");
        var row = page.Entities[0];
        if (row is null || row.Id != Guid.Empty || row.Attributes.Count != 1
            || !string.IsNullOrEmpty(row.LogicalName) && row.LogicalName != schema.LogicalName
            || row.RelatedEntities.Count != 0 || row.KeyAttributes.Count != 0
            || !row.Attributes.TryGetValue("inventory_count", out var raw) || raw is not AliasedValue alias
            || alias.EntityLogicalName != schema.LogicalName || alias.AttributeLogicalName != schema.PrimaryIdAttribute)
            throw new InvalidDataException("Inventory count alias has an invalid identity or shape.");
        var count = alias.Value switch
        {
            int value when value >= 0 => value,
            long value when value >= 0 => value,
            _ => throw new InvalidDataException("Inventory count alias is not a nonnegative integer.")
        };
        if (row.FormattedValues.Count > 1 || row.FormattedValues.Count == 1
            && (!row.FormattedValues.TryGetValue("inventory_count", out var formatted)
                || formatted is not { Length: > 0 and <= 64 } || string.IsNullOrWhiteSpace(formatted)))
            throw new InvalidDataException("Inventory count formatting is invalid or exceeds its budget.");
        return count;
    }

    /// <summary>
    /// 管理查詢只投影固定欄位並依正式主鍵排序；每表十頁、合計五萬列內完整讀取才發布。
    /// ID／欄位／cookie 故障或空續頁拒絕整份候選，不能以 Distinct 或只取第一頁掩蓋衝突。
    /// </summary>
    private static void ReadManagement(IOrganizationService service, EntityMetadata schema, ManagementSpec spec,
        List<ClonedInventoryManagementRow> destination, CancellationToken cancellationToken)
    {
        var columns = spec.Fields.Select(field => field.Name).Prepend(schema.PrimaryIdAttribute).ToArray();
        if (columns.Any(name => !schema.Attributes.Any(field => field.LogicalName == name)))
            throw new InvalidDataException("Inventory administrative schema lacks an allowlisted column.");
        var query = new QueryExpression(spec.Entity)
        {
            ColumnSet = new ColumnSet(columns),
            PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
        };
        query.AddOrder(schema.PrimaryIdAttribute, OrderType.Ascending);
        if (spec.Entity == "workflow") query.Criteria.AddCondition("type", ConditionOperator.Equal, 1);
        if (spec.Entity == "asyncoperation") query.Criteria.AddCondition("statecode", ConditionOperator.In, 0, 1);
        var ids = new HashSet<Guid>();
        var cookies = new HashSet<string>(StringComparer.Ordinal);
        for (var number = 1; number <= 10; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = service.RetrieveMultiple(query);
            cancellationToken.ThrowIfCancellationRequested();
            if (page is null || !CollectionName(page, spec.Entity) || page.TotalRecordCountLimitExceeded
                || page.Entities.Count > 5000 || page.Entities.Count > 50000 - destination.Count
                || page.Entities.Count == 0 && (page.MoreRecords || number > 1))
                throw new InvalidDataException("Inventory administrative page is incomplete or exceeds its budget.");
            foreach (var row in page.Entities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (row is null || row.LogicalName != spec.Entity || row.Id == Guid.Empty || !ids.Add(row.Id)
                    || !row.Attributes.TryGetValue(schema.PrimaryIdAttribute, out var id) || id is not Guid key || key != row.Id
                    || row.RelatedEntities.Count != 0 || row.KeyAttributes.Count != 0
                    || row.Attributes.Keys.Any(name => Array.IndexOf(columns, name) < 0))
                    throw new InvalidDataException("Inventory administrative row identity or columns are invalid.");
                var projected = Project(row, spec);
                if (spec.Entity == "workflow" && projected.Options["type"] != 1
                    || spec.Entity == "asyncoperation" && projected.Options["statecode"] is not (0 or 1))
                    throw new InvalidDataException("Inventory administrative row is outside its requested scope.");
                destination.Add(projected);
            }
            if (!string.IsNullOrEmpty(page.PagingCookie))
            {
                ValidateCookie(page, schema.PrimaryIdAttribute, number);
                if (!cookies.Add(page.PagingCookie))
                    throw new InvalidDataException("Inventory continuation cookie is repeated.");
            }
            if (!page.MoreRecords) return;
            if (number == 10 || string.IsNullOrWhiteSpace(page.PagingCookie))
                throw new InvalidDataException("Inventory continuation is missing or exceeds its page budget.");
            query.PageInfo.PageNumber = number + 1;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }
    }

    /// <summary>
    /// cookie 僅能是本表主鍵排序的有界 XML，頁碼及首尾 ID 要與本頁一致；不讀 DTD、外部 entity 或任意檔案。
    /// 驗證失敗只回固定錯誤，不回顯 cookie；來源 cookie 僅保留到本表讀取結束。
    /// </summary>
    private static void ValidateCookie(EntityCollection page, string primaryId, int number)
    {
        var cookie = page.PagingCookie;
        if (cookie.Length > 65536 || page.Entities.Count == 0)
            throw new InvalidDataException("Inventory continuation cookie exceeds its budget.");
        try
        {
            using var text = new StringReader(cookie);
            using var reader = XmlReader.Create(text, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = 65536, MaxCharactersFromEntities = 1
            });
            var root = XElement.Load(reader);
            var sort = root.Elements().SingleOrDefault();
            if (root.Name != "cookie" || root.Attributes().Count() != 1
                || !int.TryParse(root.Attribute("page")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var actualPage)
                || actualPage != number || sort is null || sort.Name != primaryId || sort.HasElements
                || sort.Attributes().Count() != 2
                || !Guid.TryParse(sort.Attribute("first")?.Value, out var first) || first != page.Entities[0].Id
                || !Guid.TryParse(sort.Attribute("last")?.Value, out var last) || last != page.Entities[^1].Id
                || root.Nodes().Any(node => node is not XElement && node is not XText { Value: "" }))
                throw new InvalidDataException("Inventory continuation cookie does not match its page.");
        }
        catch (XmlException)
        {
            throw new InvalidDataException("Inventory continuation cookie is malformed.");
        }
        catch (InvalidOperationException)
        {
            throw new InvalidDataException("Inventory continuation cookie has ambiguous ordering.");
        }
    }

    /// <summary>逐欄複製純值；必填值不使用 SDK 的預設 0／false，lookup 只保留允許的管理目標及正式 ID。</summary>
    private static ClonedInventoryManagementRow Project(Entity row, ManagementSpec spec)
    {
        var text = new Dictionary<string, string>(StringComparer.Ordinal);
        var options = new Dictionary<string, int>(StringComparer.Ordinal);
        var flags = new Dictionary<string, bool>(StringComparer.Ordinal);
        var references = new Dictionary<string, ClonedInventoryReference>(StringComparer.Ordinal);
        foreach (var field in spec.Fields)
        {
            if (!row.Attributes.TryGetValue(field.Name, out var raw) || raw is null)
            {
                if (field.Optional) continue;
                throw new InvalidDataException("Inventory administrative row lacks a required value.");
            }
            switch (field.Kind)
            {
                case ValueKind.Text when raw is string value && value.Length <= (field.Name == "filteringattributes" ? 8192 : 512)
                    && (field.Optional || !string.IsNullOrWhiteSpace(value)):
                    if (field.Name == "filteringattributes" && value.Length != 0 && value.Split(',').Any(name => !LogicalName(name)))
                        throw new InvalidDataException("Inventory filtering attributes are invalid.");
                    text.Add(field.Name, value);
                    break;
                case ValueKind.LogicalName when raw is string value && (LogicalName(value) || field.Optional && value.Length == 0):
                    text.Add(field.Name, value);
                    break;
                case ValueKind.Option when raw is OptionSetValue value:
                    options.Add(field.Name, value.Value);
                    break;
                case ValueKind.Integer when raw is int value:
                    options.Add(field.Name, value);
                    break;
                case ValueKind.Boolean when raw is bool value:
                    flags.Add(field.Name, value);
                    break;
                case ValueKind.Reference when raw is EntityReference value && value.Id != Guid.Empty
                    && AllowedReference(field.Name, value.LogicalName):
                    references.Add(field.Name, new ClonedInventoryReference(value.LogicalName, value.Id));
                    break;
                default:
                    throw new InvalidDataException("Inventory administrative value has an invalid type or target.");
            }
        }
        return new ClonedInventoryManagementRow(row.LogicalName, row.Id,
            new ReadOnlyDictionary<string, string>(text), new ReadOnlyDictionary<string, int>(options),
            new ReadOnlyDictionary<string, bool>(flags), new ReadOnlyDictionary<string, ClonedInventoryReference>(references));
    }

    /// <summary>
    /// Plugin 鏈與 message/filter 必須出現在完整清冊，同一步驟及其 filter 的 message 必須一致；
    /// workflowactivationid 可指 activation，
    /// 不把 type=1 定義清冊誤當所有 activation。serviceendpoint 僅保留正式管理 ID，不讀 URL／設定。
    /// </summary>
    private static void ValidateRelationships(List<ClonedInventoryManagementRow> rows)
    {
        var identities = rows.Select(row => (row.Entity, row.Id)).ToHashSet();
        foreach (var row in rows)
            foreach (var reference in row.References.Values)
                if (reference.Entity is "pluginassembly" or "plugintype" or "sdkmessage" or "sdkmessagefilter"
                    && !identities.Contains((reference.Entity, reference.Id)))
                    throw new InvalidDataException("Inventory administrative relationship is incomplete.");
        var messagesByFilter = rows.Where(row => row.Entity == "sdkmessagefilter")
            .ToDictionary(row => row.Id, row => row.References["sdkmessageid"].Id);
        foreach (var step in rows.Where(row => row.Entity == "sdkmessageprocessingstep"))
            if (step.References.TryGetValue("sdkmessagefilterid", out var filter)
                && (!messagesByFilter.TryGetValue(filter.Id, out var message) || message != step.References["sdkmessageid"].Id))
                throw new InvalidDataException("Inventory step and filter have conflicting message identities.");
    }

    /// <summary>管理關聯的精確目標白名單，拒絕 contact/account 等業務 ID 藉 EntityReference 混入結果。</summary>
    private static bool AllowedReference(string field, string target) => field switch
    {
        "pluginassemblyid" => target == "pluginassembly",
        "eventhandler" => target is "plugintype" or "serviceendpoint",
        "sdkmessageid" => target == "sdkmessage",
        "sdkmessagefilterid" => target == "sdkmessagefilter",
        "workflowactivationid" => target == "workflow",
        _ => false
    };

    /// <summary>server collection 可省略 EntityName，但若提供就必須與本次精確表一致，不能把其他集合誤發布。</summary>
    private static bool CollectionName(EntityCollection page, string table) =>
        string.IsNullOrEmpty(page.EntityName) || string.Equals(page.EntityName, table, StringComparison.Ordinal);

    /// <summary>
    /// 內建屬性 metadata（例如 resourcesJson）可含大寫 ASCII；僅保留已驗證 schema 的原識別，
    /// 不把它加入管理或業務查詢白名單，也不改大小寫後合併不同身分。
    /// </summary>
    private static bool MetadataAttributeName(string? value) => value is { Length: > 0 and <= 128 }
        && value[0] is >= 'a' and <= 'z' or >= 'A' and <= 'Z'
        && value.All(character => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');

    /// <summary>限制可查詢表與管理欄值為有界小寫識別字，不接受 XML、顯示名稱、路徑或 caller 注入字串。</summary>
    private static bool LogicalName(string? value) => value is { Length: > 0 and <= 128 }
        && value[0] is >= 'a' and <= 'z'
        && value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');

    /// <summary>借用同步 SDK 前後檢查 caller 取消；保留原始 SDK 例外，不建立 Task 或在返回前提早釋放 service。</summary>
    private static T Execute<T>(IOrganizationService service, OrganizationRequest request, CancellationToken cancellationToken)
        where T : OrganizationResponse
    {
        cancellationToken.ThrowIfCancellationRequested();
        var response = service.Execute(request);
        cancellationToken.ThrowIfCancellationRequested();
        return response as T ?? throw new InvalidDataException("Inventory SDK response type is invalid.");
    }

    /// <summary>
    /// 每次呼叫建立不可外傳的固定管理欄位描述；沒有 AllColumns、configuration、
    /// owner／regarding 等業務 lookup，也不查任何表單、流程或檢視的定義本文。
    /// </summary>
    private static ManagementSpec[] ManagementSpecs() =>
    [
        new("workflow",
        [
            new("name", ValueKind.Text), new("type", ValueKind.Option), new("category", ValueKind.Option),
            new("mode", ValueKind.Option), new("statecode", ValueKind.Option), new("statuscode", ValueKind.Option),
            new("primaryentity", ValueKind.LogicalName, true), new("ismanaged", ValueKind.Boolean)
        ]),
        new("asyncoperation",
        [
            new("statecode", ValueKind.Option), new("statuscode", ValueKind.Option),
            new("operationtype", ValueKind.Option), new("workflowactivationid", ValueKind.Reference, true)
        ]),
        new("pluginassembly",
        [
            new("name", ValueKind.Text), new("version", ValueKind.Text), new("ismanaged", ValueKind.Boolean),
            new("isolationmode", ValueKind.Option)
        ]),
        new("plugintype", [new("typename", ValueKind.Text), new("pluginassemblyid", ValueKind.Reference)]),
        new("sdkmessage", [new("name", ValueKind.Text)]),
        new("sdkmessagefilter",
        [
            new("sdkmessageid", ValueKind.Reference), new("primaryobjecttypecode", ValueKind.LogicalName, true),
            new("secondaryobjecttypecode", ValueKind.LogicalName, true)
        ]),
        new("sdkmessageprocessingstep",
        [
            new("name", ValueKind.Text, true), new("eventhandler", ValueKind.Reference), new("sdkmessageid", ValueKind.Reference),
            new("sdkmessagefilterid", ValueKind.Reference, true), new("statecode", ValueKind.Option),
            new("statuscode", ValueKind.Option), new("mode", ValueKind.Option), new("stage", ValueKind.Option),
            new("rank", ValueKind.Integer), new("filteringattributes", ValueKind.Text, true)
        ]),
        new("mailbox",
        [
            new("statecode", ValueKind.Option), new("statuscode", ValueKind.Option),
            new("incomingemaildeliverymethod", ValueKind.Option), new("outgoingemaildeliverymethod", ValueKind.Option),
            new("actdeliverymethod", ValueKind.Option)
        ]),
        new("solution", [new("uniquename", ValueKind.Text), new("ismanaged", ValueKind.Boolean)]),
        new("systemform",
        [
            new("name", ValueKind.Text), new("objecttypecode", ValueKind.LogicalName), new("type", ValueKind.Option),
            new("formactivationstate", ValueKind.Option), new("ismanaged", ValueKind.Boolean)
        ]),
        new("savedquery",
        [
            new("name", ValueKind.Text), new("returnedtypecode", ValueKind.LogicalName), new("querytype", ValueKind.Integer),
            new("statecode", ValueKind.Option), new("statuscode", ValueKind.Option, true), new("ismanaged", ValueKind.Boolean)
        ])
    ];

    /// <summary>固定管理欄位的 SDK 型別；不包含任意 object、byte[]、Entity、Money 或業務資料容器。</summary>
    private enum ValueKind { Text, LogicalName, Option, Integer, Boolean, Reference }

    /// <summary>單一欄位的私有純描述；Optional 僅允許空值，不允許漏掉 server schema 或改變型別。</summary>
    /// <param name="Name">固定欄位名稱，不能從 caller 或 schema 推生新的查詢欄。</param>
    /// <param name="Kind">預期的精確 SDK 型別，回應錯型別時拒絕。</param>
    /// <param name="Optional">是否允許 server 不回傳值；不代表可省略 schema 存在性檢查。</param>
    private sealed record ManagementField(string Name, ValueKind Kind, bool Optional = false);

    /// <summary>單表的操作區域白名單；Fields 只用於這一次 Read，不發布陣列、不在命令之間共享。</summary>
    /// <param name="Entity">固定管理表名稱。</param>
    /// <param name="Fields">不包含主鍵的明列欄位；主鍵只能從已驗證 metadata 決定。</param>
    private sealed record ManagementSpec(string Entity, ManagementField[] Fields);
}

// ============================================================================
// 檔案：SpeechMessage.Dynamics.Connectors.Data8/Package02Data8ContactProfileOperations.cs
// 用途：擁有 P7.2 Slice B 的 Data8 contact profile 固定模板；目前先提供 B1 LINE profile write。
//
// 信任、隔離與生命週期：
// 1. Entity、欄位名、CE 版本、mutation mode、response discriminator 與 byte ceiling 都是 server-owned 常數；
//    request 不能提供 LINE token、LINE user ID、欄位 map、FetchXML、endpoint、credential 或 connector selector。
// 2. 每次呼叫只在目前 connector lease 的同步 scope 建立一個 update Entity 與一個 read-back Entity；不快取、
//    不建立 timer／CTS／background task，也不把 SDK graph、URL、文字、contact identity 或 service 保存到 static。
// 3. write 後 read-back 任一不符即拋出，使 lease owner 淘汰 client；unknown outcome 不會在此處盲目重送，
//    baseline reconciliation 與 cleanup 仍由 task-owned live fixture flow 負責。
// ============================================================================

using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Connectors.Data8;

/// <summary>
/// P7.2 Slice B 的 connector-internal capability owner。此類別不是 generic CRUD adapter；每個公開給 connector
/// 的 method 都先重驗 operation registry、CE 版本、固定 scalar schema 與 response branch，再執行單一受控
/// CRM template。任何直接 connector caller 企圖繞過 executor 時仍會 fail closed。
/// </summary>
internal static class Package02Data8ContactProfileOperations
{
    private const string ContactEntityName = "contact";
    private const string ContactIdAttribute = "contactid";
    private const string LinePictureUrlAttribute = "new_line_picture_url";
    private const string LineStatusMessageAttribute = "new_line_status_message";
    private const string LineDisplayNameAttribute = "new_line_displayname";
    private const int MaximumPictureUrlBytes = 1024;
    private const int MaximumProfileTextBytes = 512;
    private const int MaximumSearchBytes = 256;
    private const int MaximumMetadataOptions = 4096;
    private const int MaximumSmallGroupLists = 500;
    private const int MaximumGroupedContacts = 4096;
    private const int QueryPageSize = 500;
    private const int MaximumMembershipPages = 9;
    private const int InClauseChunkSize = 500;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>
    /// 執行 B1 LINE profile 固定寫入。picture/status 的 clear 會明確寫入 null；display preserve 則完全不把
    /// display 欄位加入 Entity。成功前固定讀回三欄並只比對本次 mutation，回應不含任何欄位值或 identity。
    /// </summary>
    /// <param name="service">目前 lease 唯一擁有的 Data8 organization service；本方法不 Dispose 或保存它。</param>
    /// <param name="operation">executor 已複製的 bounded scalar operation；本方法仍完整重驗。</param>
    /// <param name="ceVersion">immutable profile 的 CE 版本；B1 只允許 9.1。</param>
    /// <returns>只含 Changed／ReadBackConfirmed 的封閉 response branch。</returns>
    internal static OperationResponseData ExecuteLineProfileUpdate(
        IOrganizationService service,
        ConnectorOperation operation,
        string ceVersion)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(operation);
        if (!string.Equals(operation.OperationId, OperationIds.MemberInfoContactUpdateLineProfile, StringComparison.Ordinal) ||
            !string.Equals(ceVersion, "9.1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Data8 contact LINE profile operation is not permitted.");
        }

        ValidateLineProfileDefinition(operation.OperationId);
        RejectUnexpectedLineProfileParameters(operation.Parameters);
        var contactId = ReadRequiredContactId(operation.Parameters);
        var picture = ReadNullableMutation(
            operation.Parameters,
            "pictureMode",
            "pictureUrl",
            MaximumPictureUrlBytes,
            validateHttpsUrl: true);
        var status = ReadNullableMutation(
            operation.Parameters,
            "statusMode",
            "statusMessage",
            MaximumProfileTextBytes,
            validateHttpsUrl: false);
        var display = ReadDisplayNameMutation(operation.Parameters);

        var update = new Entity(ContactEntityName, contactId)
        {
            [LinePictureUrlAttribute] = picture,
            [LineStatusMessageAttribute] = status
        };
        if (display.Apply)
        {
            update[LineDisplayNameAttribute] = display.Value;
        }

        service.Update(update);
        var readBack = service.Retrieve(
            ContactEntityName,
            contactId,
            new ColumnSet(LinePictureUrlAttribute, LineStatusMessageAttribute, LineDisplayNameAttribute));
        ValidateLineProfileReadBack(readBack, contactId, picture, status, display);

        return OperationResponseData.ForContactLineProfileUpdate(
            operation.OperationId,
            ceVersion,
            ContactLineProfileUpdateDisposition.Changed,
            ContactLineProfileUpdateCorrelationCategory.ReadBackConfirmed);
    }

    /// <summary>
    /// 執行 B2 未分組 commitment aggregate。caller 只能提供 optional bounded search；「結案」值、label
    /// matches、active app-named 小組、目前 membership、grouped contact graph 與 aggregate FetchXML 全部在目前
    /// connector request scope 由固定規則建立。任何 metadata 歧義、超過 list／membership／row／byte 上限、
    /// malformed SDK row 或 paging 不完整都 fail closed，不回傳 partial count。
    /// </summary>
    /// <param name="service">目前 lease 唯一擁有的 organization service；不會被保存或 Dispose。</param>
    /// <param name="operation">只含 optional search 的 executor-owned scalar copy。</param>
    /// <param name="ceVersion">immutable profile CE 版本；B2 只允許 9.1。</param>
    /// <returns>bounded、去 SDK 化的 raw OptionSet value/count 集合。</returns>
    internal static OperationResponseData ExecuteUngroupedCommitmentCount(
        IOrganizationService service,
        ConnectorOperation operation,
        string ceVersion)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(operation);
        if (!string.Equals(
                operation.OperationId,
                OperationIds.MemberInfoContactCountUngroupedCommitment,
                StringComparison.Ordinal) ||
            !string.Equals(ceVersion, "9.1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Data8 ungrouped commitment operation is not permitted.");
        }

        var definition = ValidateUngroupedCommitmentDefinition(operation.OperationId);
        var search = ReadOptionalSearch(operation.Parameters);
        var metadata = ReadCustomerTypeMetadata(service);
        var closedStatus = ResolveUniqueClosedStatus(metadata);
        var matchingStatusValues = ResolveMatchingStatusValues(metadata, search);
        var listIds = RetrieveSmallGroupListIds(service);
        var groupedContactIds = RetrieveGroupedContactIds(service, listIds, closedStatus);
        var aggregate = BuildUngroupedAggregateFetch(search, matchingStatusValues, groupedContactIds, closedStatus);
        var rows = service.RetrieveMultiple(new FetchExpression(aggregate))
            ?? throw new InvalidOperationException("The Data8 ungrouped commitment aggregate response is missing.");
        var counts = ProjectUngroupedCounts(rows, definition);

        return OperationResponseData.ForUngroupedCommitmentCounts(operation.OperationId, ceVersion, counts);
    }

    /// <summary>
    /// 重驗 B2 registry 的 function、template、response、read-audit 與 read-only policy，並回傳 result limits owner。
    /// registry 不符時不查 metadata 或資料，因此不會在錯誤 capability 下消耗 CRM session／connection。
    /// </summary>
    private static OperationDefinition ValidateUngroupedCommitmentDefinition(string operationId)
    {
        if (!Package01OperationRegistry.TryGet(operationId, out var definition) ||
            definition is null ||
            !string.Equals(definition.OperationKind, "function", StringComparison.Ordinal) ||
            !string.Equals(definition.TemplateId, "memberinfo.contact.ungrouped.commitment.count.v1", StringComparison.Ordinal) ||
            definition.ResponseKind != OperationResponseKind.UngroupedCommitmentCounts ||
            !string.Equals(definition.AuditRequirement, "read-audit", StringComparison.Ordinal) ||
            !string.Equals(definition.IdempotencyClass, "read-only", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Data8 ungrouped commitment registry definition is invalid.");
        }

        return definition;
    }

    /// <summary>
    /// 讀取 B2 唯一 optional search。空字典或缺欄表示無搜尋；存在時只接受 trim 後 1-256 UTF-8 bytes。
    /// 任何第二個參數、null、錯型別或 invalid UTF-16 都在 metadata／query 前拒絕。
    /// </summary>
    private static string? ReadOptionalSearch(IReadOnlyDictionary<string, object?> parameters)
    {
        if (parameters is null || parameters.Count > 1 ||
            parameters.Keys.Any(static name => !string.Equals(name, "search", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("The Data8 ungrouped commitment parameters are invalid.");
        }

        if (!parameters.TryGetValue("search", out var value))
        {
            return null;
        }

        if (value is not string text)
        {
            throw new InvalidOperationException("The Data8 ungrouped commitment search is invalid.");
        }

        return NormalizeRequiredText(text, MaximumSearchBytes);
    }

    /// <summary>
    /// 以固定 RetrieveAttributeRequest 讀取 contact.customertypecode。回應必須是 bounded Picklist metadata；
    /// metadata 只在目前 method stack 使用，不進 static cache、產品結果或跨 request state。
    /// </summary>
    private static IReadOnlyList<OptionMetadata> ReadCustomerTypeMetadata(IOrganizationService service)
    {
        var response = service.Execute(new RetrieveAttributeRequest
        {
            EntityLogicalName = ContactEntityName,
            LogicalName = "customertypecode",
            RetrieveAsIfPublished = true
        }) as RetrieveAttributeResponse;
        if (response?.AttributeMetadata is not PicklistAttributeMetadata picklist ||
            picklist.OptionSet?.Options is null ||
            picklist.OptionSet.Options.Count is < 1 or > MaximumMetadataOptions)
        {
            throw new InvalidOperationException("The Data8 contact customer-type metadata is invalid.");
        }

        return picklist.OptionSet.Options.ToArray();
    }

    /// <summary>
    /// 從所有 localized labels 找到唯一 normalized「結案」值。正規化只移除 legacy 數字順位與單一標點前綴；
    /// 找不到、Option value 缺失或多個不同值同時匹配都 fail closed，避免把結案會員放入結果。
    /// </summary>
    private static int ResolveUniqueClosedStatus(IReadOnlyList<OptionMetadata> options)
    {
        var matches = options
            .Where(static option => option?.Value is not null &&
                                    EnumerateLabels(option).Any(label =>
                                        string.Equals(NormalizeOptionLabel(label), "結案", StringComparison.OrdinalIgnoreCase)))
            .Select(static option => option.Value!.Value)
            .Distinct()
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException("The Data8 closed customer-type metadata is ambiguous.");
    }

    /// <summary>
    /// 將 bounded search 與 metadata labels 作 ordinal-ignore-case contains，比對結果只保留 distinct int value。
    /// search 未提供時回傳空陣列；label 不會被送回產品或保存在 connector client。
    /// </summary>
    private static IReadOnlyList<int> ResolveMatchingStatusValues(
        IReadOnlyList<OptionMetadata> options,
        string? search)
    {
        if (search is null)
        {
            return Array.Empty<int>();
        }

        return options
            .Where(static option => option?.Value is not null)
            .Where(option => EnumerateLabels(option).Any(label =>
                label.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .Select(static option => option.Value!.Value)
            .Distinct()
            .OrderBy(static value => value)
            .ToArray();
    }

    /// <summary>列舉 metadata 的 bounded localized labels；忽略空白，沒有 fallback 字串配置或 log。</summary>
    private static IEnumerable<string> EnumerateLabels(OptionMetadata option)
        => option.Label?.LocalizedLabels?
               .Select(static label => label?.Label)
               .Where(static label => !string.IsNullOrWhiteSpace(label))
               .Select(static label => label!)
           ?? Enumerable.Empty<string>();

    /// <summary>移除 legacy label 開頭的數字、空白及 .、．、- 標點，保留其餘文字作精確比較。</summary>
    private static string NormalizeOptionLabel(string label)
    {
        var span = label.AsSpan().Trim();
        var index = 0;
        while (index < span.Length && char.IsAsciiDigit(span[index]))
        {
            index++;
        }

        while (index < span.Length && char.IsWhiteSpace(span[index]))
        {
            index++;
        }

        if (index < span.Length && span[index] is '.' or '、' or '．' or '-')
        {
            index++;
        }

        return span[index..].Trim().ToString();
    }

    /// <summary>
    /// 讀取最多 500 筆 active、purpose=小組名單、new_app_named=true 的固定 list IDs。若 CRM 宣告還有下一頁，
    /// 代表 scope 超出目前 contract，立即 fail closed 而不截斷；不把 list name 或其他群組資料帶入結果。
    /// </summary>
    private static IReadOnlyList<Guid> RetrieveSmallGroupListIds(IOrganizationService service)
    {
        var query = new QueryExpression("list")
        {
            ColumnSet = new ColumnSet("listid"),
            PageInfo = new PagingInfo
            {
                Count = MaximumSmallGroupLists,
                PageNumber = 1,
                PagingCookie = null,
                ReturnTotalRecordCount = false
            }
        };
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        query.Criteria.AddCondition("purpose", ConditionOperator.Equal, "小組名單");
        query.Criteria.AddCondition("new_app_named", ConditionOperator.Equal, true);
        query.AddOrder("listid", OrderType.Ascending);

        var page = service.RetrieveMultiple(query)
            ?? throw new InvalidOperationException("The Data8 small-group list response is missing.");
        if (page.MoreRecords || page.Entities.Count > MaximumSmallGroupLists)
        {
            throw new InvalidOperationException("The Data8 small-group list scope exceeds its bound.");
        }

        return page.Entities
            .Select(static entity => ReadEntityGuid(entity, "list", "listid"))
            .Distinct()
            .OrderBy(static id => id)
            .ToArray();
    }

    /// <summary>
    /// 依固定 list IDs 讀取目前有效 membership，並把 grouped contact identity 限制在 4,096 筆與九頁內。
    /// 每頁 cookie 只用於下一次同步 CRM request，迴圈結束即釋放；不進 cache、session 或產品回應。
    /// </summary>
    private static IReadOnlyList<Guid> RetrieveGroupedContactIds(
        IOrganizationService service,
        IReadOnlyList<Guid> listIds,
        int closedStatus)
    {
        if (listIds.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        var result = new HashSet<Guid>();
        string? pagingCookie = null;
        for (var pageNumber = 1; pageNumber <= MaximumMembershipPages; pageNumber++)
        {
            var query = new QueryExpression("listmember")
            {
                ColumnSet = new ColumnSet("listid", "entityid"),
                PageInfo = new PagingInfo
                {
                    Count = QueryPageSize,
                    PageNumber = pageNumber,
                    PagingCookie = pagingCookie,
                    ReturnTotalRecordCount = false
                }
            };
            query.Criteria.AddCondition(
                "listid",
                ConditionOperator.In,
                listIds.Select(static id => (object)id).ToArray());
            query.AddOrder("listmemberid", OrderType.Ascending);

            var contactLink = new LinkEntity(
                "listmember",
                ContactEntityName,
                "entityid",
                ContactIdAttribute,
                JoinOperator.Inner)
            {
                EntityAlias = "member",
                Columns = new ColumnSet(false)
            };
            contactLink.LinkCriteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            var current = new FilterExpression(LogicalOperator.Or);
            current.AddCondition("customertypecode", ConditionOperator.Null);
            current.AddCondition("customertypecode", ConditionOperator.NotEqual, closedStatus);
            contactLink.LinkCriteria.Filters.Add(current);
            query.LinkEntities.Add(contactLink);

            var page = service.RetrieveMultiple(query)
                ?? throw new InvalidOperationException("The Data8 small-group membership response is missing.");
            foreach (var row in page.Entities)
            {
                var listId = ReadAttributeGuid(row, "listid");
                var contactId = ReadAttributeGuid(row, "entityid");
                if (!listIds.Contains(listId))
                {
                    throw new InvalidOperationException("The Data8 small-group membership row is invalid.");
                }

                result.Add(contactId);

                if (result.Count > MaximumGroupedContacts)
                {
                    throw new InvalidOperationException("The Data8 grouped contact scope exceeds its bound.");
                }
            }

            if (!page.MoreRecords)
            {
                return result.OrderBy(static id => id).ToArray();
            }

            if (pageNumber == MaximumMembershipPages || string.IsNullOrWhiteSpace(page.PagingCookie))
            {
                throw new InvalidOperationException("The Data8 grouped contact paging contract is incomplete.");
            }

            pagingCookie = page.PagingCookie;
        }

        throw new InvalidOperationException("The Data8 grouped contact paging contract exceeded its bound.");
    }

    /// <summary>
    /// 以 XElement 建立固定 aggregate FetchXML。所有 caller 文字與 GUID 都作為 attribute/value node 寫入，
    /// 不進入 XML markup；grouped IDs 每 500 筆一個 not-in condition，避免 caller-supplied FetchXML 與無界 IN。
    /// </summary>
    private static string BuildUngroupedAggregateFetch(
        string? search,
        IReadOnlyList<int> matchingStatusValues,
        IReadOnlyList<Guid> groupedContactIds,
        int closedStatus)
    {
        var rootFilter = new XElement(
            "filter",
            new XAttribute("type", "and"),
            CreateScalarCondition("statecode", "eq", "0"),
            // Legacy projection 最終不會計入 null commitment type；若 aggregate 仍保留 null 群組，
            // CE 會回傳缺少 commitmenttype alias 的 row，使安全投影無法區分「合法 null」與畸形回應。
            // 因此在 server-owned template 先排除 null，確保每個 group 都有可驗證的 OptionSet value。
            new XElement(
                "condition",
                new XAttribute("attribute", "customertypecode"),
                new XAttribute("operator", "not-null")),
            CreateScalarCondition(
                "customertypecode",
                "ne",
                closedStatus.ToString(CultureInfo.InvariantCulture)));

        if (search is not null)
        {
            var searchFilter = new XElement(
                "filter",
                new XAttribute("type", "or"),
                CreateScalarCondition("fullname", "like", "%" + search + "%"),
                CreateScalarCondition("mobilephone", "like", "%" + search + "%"));
            if (matchingStatusValues.Count > 0)
            {
                searchFilter.Add(CreateValuesCondition(
                    "customertypecode",
                    "in",
                    matchingStatusValues.Select(static value => value.ToString(CultureInfo.InvariantCulture))));
            }

            rootFilter.Add(searchFilter);
        }

        foreach (var chunk in groupedContactIds.Chunk(InClauseChunkSize))
        {
            rootFilter.Add(CreateValuesCondition(
                ContactIdAttribute,
                "not-in",
                chunk.Select(static id => id.ToString("D"))));
        }

        var document = new XDocument(
            new XElement(
                "fetch",
                new XAttribute("aggregate", "true"),
                new XElement(
                    "entity",
                    new XAttribute("name", ContactEntityName),
                    new XElement(
                        "attribute",
                        new XAttribute("name", "customertypecode"),
                        new XAttribute("alias", "commitmenttype"),
                        new XAttribute("groupby", "true")),
                    new XElement(
                        "attribute",
                        new XAttribute("name", ContactIdAttribute),
                        new XAttribute("alias", "rowcount"),
                        new XAttribute("aggregate", "countcolumn")),
                    rootFilter)));
        return document.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>建立一個 scalar condition；XAttribute 自動 XML escape，caller text 不會成為 markup。</summary>
    private static XElement CreateScalarCondition(string attribute, string operation, string value)
        => new(
            "condition",
            new XAttribute("attribute", attribute),
            new XAttribute("operator", operation),
            new XAttribute("value", value));

    /// <summary>建立 bounded multi-value condition；輸入在 caller 已限制數量後立即 materialize 為 XML nodes。</summary>
    private static XElement CreateValuesCondition(
        string attribute,
        string operation,
        IEnumerable<string> values)
        => new(
            "condition",
            new XAttribute("attribute", attribute),
            new XAttribute("operator", operation),
            values.Select(static value => new XElement("value", value)));

    /// <summary>
    /// 將 aggregate SDK rows 投影成 bounded pure-value records。每列必須同時有合法 OptionSet/int value 與
    /// 非負 integral count；duplicate value 以 checked 合計。任一錯列、overflow、row/byte 超限都拒絕整批。
    /// </summary>
    private static IReadOnlyList<UngroupedCommitmentCountRecord> ProjectUngroupedCounts(
        EntityCollection rows,
        OperationDefinition definition)
    {
        if (rows.MoreRecords || rows.Entities.Count > definition.MaximumResultItemCount)
        {
            throw new InvalidOperationException("The Data8 ungrouped commitment result is incomplete or exceeds its row bound.");
        }

        var counts = new Dictionary<int, int>();
        var estimatedBytes = 0;
        foreach (var row in rows.Entities)
        {
            if (row is null || !string.Equals(row.LogicalName, ContactEntityName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The Data8 ungrouped commitment aggregate row is invalid.");
            }

            var rawValue = ReadAliasedValue(row, "commitmenttype");
            var value = rawValue switch
            {
                OptionSetValue option => option.Value,
                int integer => integer,
                _ => throw new InvalidOperationException("The Data8 ungrouped commitment value is invalid.")
            };
            var count = ReadNonNegativeCount(ReadAliasedValue(row, "rowcount"));
            try
            {
                counts[value] = checked(counts.GetValueOrDefault(value) + count);
                estimatedBytes = checked(estimatedBytes + 32);
            }
            catch (OverflowException)
            {
                throw new InvalidOperationException("The Data8 ungrouped commitment result overflowed its bound.");
            }

            if (estimatedBytes > definition.MaximumCumulativeResponseBytes)
            {
                throw new InvalidOperationException("The Data8 ungrouped commitment result exceeds its byte bound.");
            }
        }

        return counts
            .OrderBy(static pair => pair.Key)
            .Select(static pair => new UngroupedCommitmentCountRecord { Value = pair.Key, Count = pair.Value })
            .ToArray();
    }

    /// <summary>讀取 required alias 並移除單層 AliasedValue；缺欄或 null 一律拒絕，不略過錯列。</summary>
    private static object ReadAliasedValue(Entity? row, string alias)
    {
        if (row is null || !row.Attributes.TryGetValue(alias, out var value) || value is null)
        {
            throw new InvalidOperationException("The Data8 ungrouped commitment aggregate row is incomplete.");
        }

        return value is AliasedValue aliased ? aliased.Value : value;
    }

    /// <summary>只接受可無損轉為 Int32 的非負 integral count；decimal 必須沒有小數，overflow fail closed。</summary>
    private static int ReadNonNegativeCount(object value)
    {
        try
        {
            var count = value switch
            {
                byte number => number,
                short number => number,
                int number => number,
                long number when number is >= 0 and <= int.MaxValue => checked((int)number),
                decimal number when number >= 0 && number <= int.MaxValue && decimal.Truncate(number) == number =>
                    decimal.ToInt32(number),
                _ => -1
            };
            return count >= 0
                ? count
                : throw new InvalidOperationException("The Data8 ungrouped commitment count is invalid.");
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException("The Data8 ungrouped commitment count is invalid.");
        }
    }

    /// <summary>讀取固定 entity 的 primary GUID；Entity.Id 或 attribute 任一合法即可，但兩者若同時存在須一致。</summary>
    private static Guid ReadEntityGuid(Entity? entity, string expectedEntityName, string attributeName)
    {
        if (entity is null || !string.Equals(entity.LogicalName, expectedEntityName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Data8 small-group entity is invalid.");
        }

        var attributeId = entity.Attributes.TryGetValue(attributeName, out _) ? ReadAttributeGuid(entity, attributeName) : Guid.Empty;
        var id = entity.Id != Guid.Empty ? entity.Id : attributeId;
        if (id == Guid.Empty || attributeId != Guid.Empty && attributeId != id)
        {
            throw new InvalidOperationException("The Data8 small-group entity identity is invalid.");
        }

        return id;
    }

    /// <summary>只接受 Guid 或 EntityReference 的非空 attribute identity；不使用 ToString 寬鬆轉型。</summary>
    private static Guid ReadAttributeGuid(Entity entity, string attributeName)
    {
        if (!entity.Attributes.TryGetValue(attributeName, out var value))
        {
            throw new InvalidOperationException("The Data8 small-group identity is missing.");
        }

        var id = value switch
        {
            Guid guid => guid,
            EntityReference reference => reference.Id,
            _ => Guid.Empty
        };
        return id != Guid.Empty
            ? id
            : throw new InvalidOperationException("The Data8 small-group identity is invalid.");
    }

    /// <summary>
    /// 重驗 B1 registry 契約。registry presence 本身不是寫入授權；operation kind、template、response、audit 與
    /// caller idempotency policy 必須全部一致，避免 registry 漂移時舊 template 仍然對 CRM 寫入。
    /// </summary>
    private static void ValidateLineProfileDefinition(string operationId)
    {
        if (!Package01OperationRegistry.TryGet(operationId, out var definition) ||
            definition is null ||
            !string.Equals(definition.OperationKind, "write", StringComparison.Ordinal) ||
            !string.Equals(definition.TemplateId, "memberinfo.contact.line.profile.patch.v1", StringComparison.Ordinal) ||
            definition.ResponseKind != OperationResponseKind.ContactLineProfileUpdate ||
            !string.Equals(definition.AuditRequirement, "write-audit", StringComparison.Ordinal) ||
            !string.Equals(definition.IdempotencyClass, "caller-idempotency-key-required", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Data8 contact LINE profile registry definition is invalid.");
        }
    }

    /// <summary>
    /// 拒絕 B1 七個固定 scalar 以外的任何值；四個 required scalar 必須存在，總數不得超過七。
    /// 此檢查阻止直接 connector caller 傳入 Entity、欄位名、LINE token、URL probe result 或任意 CRM command。
    /// </summary>
    private static void RejectUnexpectedLineProfileParameters(IReadOnlyDictionary<string, object?> parameters)
    {
        if (parameters is null || parameters.Count is < 4 or > 7)
        {
            throw new InvalidOperationException("The Data8 contact LINE profile parameters are invalid.");
        }

        foreach (var parameter in parameters.Keys)
        {
            if (parameter is not "contactId" and
                not "pictureMode" and not "pictureUrl" and
                not "statusMode" and not "statusMessage" and
                not "displayNameMode" and not "displayName")
            {
                throw new InvalidOperationException("The Data8 contact LINE profile parameters are invalid.");
            }
        }
    }

    /// <summary>讀取 executor-normalized 非空 contact Guid；不接受文字、EntityReference 或其他 SDK graph。</summary>
    private static Guid ReadRequiredContactId(IReadOnlyDictionary<string, object?> parameters)
    {
        if (!parameters.TryGetValue("contactId", out var value) || value is not Guid contactId || contactId == Guid.Empty)
        {
            throw new InvalidOperationException("The Data8 contact LINE profile contact ID is invalid.");
        }

        return contactId;
    }

    /// <summary>
    /// 讀取 picture/status 的固定 set/clear mutation。set 回傳 bounded trimmed string，clear 回傳 null；
    /// mode/value 不一致、錯型別、invalid UTF-16 或超限都 fail closed。URL 驗證只有 parsing，沒有網路 I/O。
    /// </summary>
    private static string? ReadNullableMutation(
        IReadOnlyDictionary<string, object?> parameters,
        string modeName,
        string valueName,
        int maximumBytes,
        bool validateHttpsUrl)
    {
        if (!parameters.TryGetValue(modeName, out var modeValue) || modeValue is not string mode)
        {
            throw new InvalidOperationException("The Data8 contact LINE profile mutation mode is invalid.");
        }

        if (string.Equals(mode, "clear", StringComparison.Ordinal))
        {
            if (parameters.ContainsKey(valueName))
            {
                throw new InvalidOperationException("The Data8 contact LINE profile clear mutation is invalid.");
            }

            return null;
        }

        if (!string.Equals(mode, "set", StringComparison.Ordinal) ||
            !parameters.TryGetValue(valueName, out var value) || value is not string text)
        {
            throw new InvalidOperationException("The Data8 contact LINE profile set mutation is invalid.");
        }

        var normalized = NormalizeRequiredText(text, maximumBytes);
        if (validateHttpsUrl && !IsSafeHttpsPictureUrl(normalized))
        {
            throw new InvalidOperationException("The Data8 contact LINE profile picture URL is invalid.");
        }

        return normalized;
    }

    /// <summary>讀取 display set/preserve；回傳 immutable tuple-like record，沒有外部資源或 caller reference。</summary>
    private static DisplayNameMutation ReadDisplayNameMutation(IReadOnlyDictionary<string, object?> parameters)
    {
        if (!parameters.TryGetValue("displayNameMode", out var modeValue) || modeValue is not string mode)
        {
            throw new InvalidOperationException("The Data8 contact LINE profile display mode is invalid.");
        }

        if (string.Equals(mode, "preserve", StringComparison.Ordinal))
        {
            if (parameters.ContainsKey("displayName"))
            {
                throw new InvalidOperationException("The Data8 contact LINE profile preserve mutation is invalid.");
            }

            return new DisplayNameMutation(false, null);
        }

        if (!string.Equals(mode, "set", StringComparison.Ordinal) ||
            !parameters.TryGetValue("displayName", out var value) || value is not string text)
        {
            throw new InvalidOperationException("The Data8 contact LINE profile display mutation is invalid.");
        }

        return new DisplayNameMutation(true, NormalizeRequiredText(text, MaximumProfileTextBytes));
    }

    /// <summary>以嚴格 UTF-8 計數正規化 required 文字；回傳新 trimmed string，不保存原始 caller value。</summary>
    private static string NormalizeRequiredText(string value, int maximumBytes)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0 || normalized.Length > maximumBytes)
        {
            throw new InvalidOperationException("The Data8 contact LINE profile text is invalid.");
        }

        try
        {
            if (StrictUtf8.GetByteCount(normalized) > maximumBytes)
            {
                throw new InvalidOperationException("The Data8 contact LINE profile text is invalid.");
            }
        }
        catch (EncoderFallbackException)
        {
            throw new InvalidOperationException("The Data8 contact LINE profile text is invalid.");
        }

        return normalized;
    }

    /// <summary>驗證 picture URL 是 absolute HTTPS 且沒有 user-info、fragment 或自訂 port；不解析 DNS。</summary>
    private static bool IsSafeHttpsPictureUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
           !string.IsNullOrWhiteSpace(uri.Host) &&
           string.IsNullOrEmpty(uri.UserInfo) &&
           string.IsNullOrEmpty(uri.Fragment) &&
           uri.IsDefaultPort;

    /// <summary>
    /// 驗證 B1 bounded read-back 的 entity identity 與本次 mutation。preserve 的 display 欄位刻意不比較，
    /// picture/status clear 則要求欄位不存在或為 null；任何錯型別或不符都視為 ambiguous write outcome。
    /// </summary>
    private static void ValidateLineProfileReadBack(
        Entity? entity,
        Guid expectedContactId,
        string? expectedPicture,
        string? expectedStatus,
        DisplayNameMutation expectedDisplay)
    {
        if (entity is null ||
            !string.Equals(entity.LogicalName, ContactEntityName, StringComparison.Ordinal) ||
            entity.Id != expectedContactId ||
            !HasMatchingPrimaryId(entity, expectedContactId) ||
            !string.Equals(ReadOptionalString(entity, LinePictureUrlAttribute), expectedPicture, StringComparison.Ordinal) ||
            !string.Equals(ReadOptionalString(entity, LineStatusMessageAttribute), expectedStatus, StringComparison.Ordinal) ||
            (expectedDisplay.Apply &&
             !string.Equals(ReadOptionalString(entity, LineDisplayNameAttribute), expectedDisplay.Value, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("The Data8 contact LINE profile read-back is invalid.");
        }
    }

    /// <summary>若 CRM 回傳 primary ID attribute，要求它與 Entity.Id 完全一致；省略則接受既有 Retrieve shape。</summary>
    private static bool HasMatchingPrimaryId(Entity entity, Guid expectedContactId)
        => !entity.Attributes.TryGetValue(ContactIdAttribute, out var value) ||
           value is Guid id && id == expectedContactId;

    /// <summary>只接受缺欄／null／string 三種 read-back shape，拒絕 OptionSet、AliasedValue 或其他 SDK graph。</summary>
    private static string? ReadOptionalString(Entity entity, string attributeName)
    {
        if (!entity.Attributes.TryGetValue(attributeName, out var value) || value is null)
        {
            return null;
        }

        return value as string
            ?? throw new InvalidOperationException("The Data8 contact LINE profile read-back attribute is invalid.");
    }

    /// <summary>表示 display name 是否應更新及預期值；只在單次同步 method scope 存活。</summary>
    private readonly record struct DisplayNameMutation(bool Apply, string? Value);
}

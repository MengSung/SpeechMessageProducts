// ============================================================================
// 檔案：SpeechMessage.Dynamics.Connectors.Data8/Package02Data8MemberInfoAuthorizationAssignmentOperations.cs
// 用途：執行 P7 MemberInfo 伺服器擁有的 subject assignment evidence 固定唯讀查詢。
//
// 信任與生命週期邊界：
// 1. 唯一 input 是 executor 已驗證並複製的 subject contact GUID；不接受 browser list ID、role、日期、
//    profile、endpoint、credential、FetchXML、排序或 CRM SDK graph。
// 2. contact Entity、list EntityCollection、QueryExpression 與 EntityReference 只存在目前 Data8 lease 的同步
//    範圍。任何取消、schema 漂移、paging、overflow、duplicate 或不完整資料都拋出，讓 lease owner fault/dispose
//    client；不發布 partial evidence、不重試且不把狀態存入 Session、cache 或 static field。
// 3. 成功 response 僅含 subject GUID、封閉 access mode 與防禦性複製的 list GUID。它是 local-only data plane，
//    不接線 MemberInfoController、feature gate、traffic、CE mutation、P7.5 或 P8。
// ============================================================================

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Connectors.Data8;

/// <summary>
/// 以固定 contact/list schema 建立 MemberInfo assignment evidence 的唯一 Data8 owner。
/// Church-wide job title 優先於任何 list；非 Church subject 僅由六個 lookup、active、小組名單、App 點名與有效
/// 日期共同決定 request-local list snapshot。此 internal helper 不持有 service、profile、credential、lease、
/// cancellation registration 或結果 cache，所有外部資源仍由呼叫端的 connector lease 在 finally 中釋放。
/// </summary>
internal static class Package02Data8MemberInfoAuthorizationAssignmentOperations
{
    private const string OperationId = OperationIds.MemberInfoAuthorizationAssignmentResolveBySubject;
    private const string ContactEntityName = "contact";
    private const string ContactIdAttribute = "contactid";
    private const string ContactJobTitleAttribute = "new_church_jobtitle";
    private const string ListEntityName = "list";
    private const string ListIdAttribute = "listid";
    private const string ListStateCodeAttribute = "statecode";
    private const string ListPurposeAttribute = "purpose";
    private const string ListAppNamedAttribute = "new_app_named";
    private const string ListHappyStartDateAttribute = "new_happy_start_date";
    private const string ListHappyEndDateAttribute = "new_happy_end_date";
    private const string SmallGroupPurpose = "小組名單";
    private const int MaximumPublishedListIds = 512;
    private const int OverflowSentinelTopCount = MaximumPublishedListIds + 1;
    private static readonly string[] AssignmentLookupAttributes =
    [
        "new_contact_list_vice_family_leader",
        "new_contact_family_leader_list",
        "new_contact_co_race_leager_list",
        "new_contact_race_leager_list",
        "new_contact_list_arealeader",
        "new_contact_list_co_arealeader"
    ];

    /// <summary>
    /// 執行唯一允許的 CE 9.1 assignment evidence read。固定順序是 contact job-title direct retrieve，若非 Church-wide
    /// 才執行一次六 lookup OR list query；這使 Church-wide path 不會不必要讀取 list，也讓所有 CRM I/O 都由 subject
    /// 已驗證的 server-owned operation 決定。同步 SDK I/O 無法接受 cancellation token，故在每一次外送前後檢查，
    /// 且永不以取消、timeout 或 fault 觸發 retry/fallback。
    /// </summary>
    /// <param name="service">目前 Data8 lease 唯一擁有的 Organization service；helper 不保存或釋放它。</param>
    /// <param name="operation">executor 建立的固定 operation，僅含一個非空 subject GUID。</param>
    /// <param name="ceVersion">deployment-owned profile 所決定的固定 CE version，必須為 9.1。</param>
    /// <param name="cancellationToken">目前 request cancellation；僅於同步 I/O 邊界觀察，絕不註冊或保留。</param>
    /// <returns>完整驗證後才建立的 immutable assignment evidence response branch。</returns>
    internal static OperationResponseData Execute(
        IOrganizationService service,
        ConnectorOperation operation,
        string ceVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(operation.OperationId, OperationId, StringComparison.Ordinal) ||
            !string.Equals(ceVersion, "9.1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Data8 MemberInfo assignment evidence operation is not permitted.");
        }

        _ = GetDefinition(operation.OperationId);
        var subjectContactId = ReadExactSubjectContactId(operation.Parameters);

        cancellationToken.ThrowIfCancellationRequested();
        var subject = service.Retrieve(
            ContactEntityName,
            subjectContactId,
            new ColumnSet(ContactIdAttribute, ContactJobTitleAttribute))
            ?? throw new InvalidOperationException("The Data8 MemberInfo assignment subject was not returned.");
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSubjectIdentity(subject, subjectContactId);

        if (IsChurchWideJobTitle(ReadOptionalJobTitle(subject)))
        {
            return OperationResponseData.ForMemberInfoAuthorizationAssignmentEvidence(
                operation.OperationId,
                ceVersion,
                new MemberInfoAuthorizationAssignmentEvidenceResponseData(
                    subjectContactId,
                    MemberInfoAuthorizationAssignmentAccessMode.ChurchWide,
                    Array.Empty<Guid>()));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var page = service.RetrieveMultiple(CreateAssignedListQuery(subjectContactId))
            ?? throw new InvalidOperationException("The Data8 MemberInfo assignment list query returned no page.");
        cancellationToken.ThrowIfCancellationRequested();
        if (page.MoreRecords || page.PagingCookie is not null || page.Entities.Count > MaximumPublishedListIds)
        {
            throw new InvalidOperationException("The Data8 MemberInfo assignment list query is incomplete or exceeds its bound.");
        }

        var localNow = TimeProvider.System.GetLocalNow().DateTime;
        var uniqueIds = new HashSet<Guid>();
        var listIds = new List<Guid>(page.Entities.Count);
        foreach (var entity in page.Entities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var listId = ProjectValidAssignedListId(entity, subjectContactId, localNow);
            if (!uniqueIds.Add(listId))
            {
                throw new InvalidOperationException("The Data8 MemberInfo assignment list query returned duplicate IDs.");
            }

            listIds.Add(listId);
        }

        return OperationResponseData.ForMemberInfoAuthorizationAssignmentEvidence(
            operation.OperationId,
            ceVersion,
            new MemberInfoAuthorizationAssignmentEvidenceResponseData(
                subjectContactId,
                MemberInfoAuthorizationAssignmentAccessMode.AssignedLists,
                listIds));
    }

    /// <summary>
    /// 交叉驗證 registry 仍維持固定 queryexpression、獨立 response branch、單頁與 512 published-ID 上限。
    /// registry/connector 漂移時拒絕執行，避免尚未審查的 template 或回應 shape 取得 CRM query authority。
    /// </summary>
    private static OperationDefinition GetDefinition(string operationId)
    {
        if (!Package01OperationRegistry.TryGet(operationId, out var definition) ||
            definition is null ||
            !string.Equals(definition.TemplateKind, "queryexpression", StringComparison.Ordinal) ||
            definition.ResponseKind != OperationResponseKind.MemberInfoAssignmentEvidence ||
            definition.MaximumPageCount != 1 ||
            definition.MaximumResultItemCount != MaximumPublishedListIds ||
            definition.Parameters.Count != 1)
        {
            throw new InvalidOperationException("The Data8 MemberInfo assignment evidence definition is invalid.");
        }

        return definition;
    }

    /// <summary>
    /// 只接受 executor 已建立的 <c>{ subjectContactId: Guid }</c> schema。禁止 string、JSON、EntityReference、
    /// lookup、browser object 或第二個參數，防止任何 caller 在固定 query 之前改寫 subject 或注入選擇權。
    /// </summary>
    private static Guid ReadExactSubjectContactId(IReadOnlyDictionary<string, object?> parameters)
    {
        if (parameters is null ||
            parameters.Count != 1 ||
            !parameters.TryGetValue("subjectContactId", out var value) ||
            value is not Guid subjectContactId ||
            subjectContactId == Guid.Empty)
        {
            throw new InvalidOperationException("The Data8 MemberInfo assignment evidence parameters are invalid.");
        }

        return subjectContactId;
    }

    /// <summary>
    /// 驗證 direct retrieve 回傳的固定 contact identity。logical name、primary key 投影或 entity ID 的任一不符都表示
    /// source 不完整；不得轉而查詢不同 contact、猜選第一筆或以 legacy Session 補足。
    /// </summary>
    private static void ValidateSubjectIdentity(Entity subject, Guid subjectContactId)
    {
        if (!string.Equals(subject.LogicalName, ContactEntityName, StringComparison.Ordinal) ||
            subject.Id != subjectContactId ||
            (subject.Attributes.TryGetValue(ContactIdAttribute, out var projectedId) &&
             projectedId is not null &&
             (projectedId is not Guid attributeId || attributeId != subjectContactId)))
        {
            throw new InvalidOperationException("The Data8 MemberInfo assignment subject identity is invalid.");
        }
    }

    /// <summary>
    /// 讀取 subject job title 的嚴格 pure-string projection。缺欄/null 代表非 Church-wide；其他型別與空白文字一律
    /// schema failure，而不是以 ToString、metadata 或另一個 request 的登入狀態推導角色。
    /// </summary>
    private static string? ReadOptionalJobTitle(Entity subject)
    {
        if (!subject.Attributes.TryGetValue(ContactJobTitleAttribute, out var value) || value is null)
        {
            return null;
        }

        return value is string title && !string.IsNullOrWhiteSpace(title)
            ? title
            : throw new InvalidOperationException("The Data8 MemberInfo assignment job title is invalid.");
    }

    /// <summary>
    /// 實作既有 MemberInfo Church-wide 優先字串語意。這是 server-owned contact field 判斷，不接受 login type、
    /// browser role 或 ListManager；空值僅代表需走固定 assignment list query。
    /// </summary>
    private static bool IsChurchWideJobTitle(string? jobTitle)
        => jobTitle?.Contains("牧師傳道", StringComparison.Ordinal) == true ||
           jobTitle?.Contains("牧養主任", StringComparison.Ordinal) == true ||
           jobTitle?.Contains("檢視全教會照片資訊", StringComparison.Ordinal) == true;

    /// <summary>
    /// 建立唯一固定的 six-lookup OR QueryExpression。所有欄位、active、小組名單、App 點名、TopCount 513 與 order
    /// 都是 server-owned；呼叫端不能加入 FetchXML、selector、sort、日期、profile 或 endpoint。有效日期必須留在
    /// server local-time 投影判斷，因 legacy 缺日期代表 unbounded，而非可由 CRM null filter 簡化。
    /// </summary>
    private static QueryExpression CreateAssignedListQuery(Guid subjectContactId)
    {
        var query = new QueryExpression(ListEntityName)
        {
            ColumnSet = CreateAssignedListColumnSet(),
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = OverflowSentinelTopCount
        };
        query.Criteria.AddCondition(ListStateCodeAttribute, ConditionOperator.Equal, 0);
        query.Criteria.AddCondition(ListPurposeAttribute, ConditionOperator.Equal, SmallGroupPurpose);
        query.Criteria.AddCondition(ListAppNamedAttribute, ConditionOperator.Equal, true);
        var assignments = query.Criteria.AddFilter(LogicalOperator.Or);
        foreach (var assignmentLookupAttribute in AssignmentLookupAttributes)
        {
            assignments.AddCondition(assignmentLookupAttribute, ConditionOperator.Equal, subjectContactId);
        }

        query.AddOrder(ListIdAttribute, OrderType.Ascending);
        return query;
    }

    /// <summary>
    /// 建立固定且每次呼叫皆獨立配置的小組名單欄位集合。
    /// 此集合只包含後續逐列驗證所需的主鍵、篩選回顯、有效日期與六個指派 lookup；
    /// 不允許從呼叫端加入任何欄位，以免將 CRM 查詢形狀或資料範圍交還給瀏覽器。
    /// 以新的陣列傳給 <see cref="ColumnSet"/>，避免可變集合跨請求共用或在非同步 CRM I/O
    /// 期間被其他請求改寫；此 helper 不持有 CRM entity、連線、lease 或取消註冊，資源生命週期仍由
    /// 外層 connector lease 的既有 finally/dispose 契約負責。
    /// </summary>
    /// <returns>供單一 request-local <see cref="QueryExpression"/> 使用的固定投影。</returns>
    private static ColumnSet CreateAssignedListColumnSet()
    {
        var columnNames = new string[6 + AssignmentLookupAttributes.Length];
        columnNames[0] = ListIdAttribute;
        columnNames[1] = ListStateCodeAttribute;
        columnNames[2] = ListPurposeAttribute;
        columnNames[3] = ListAppNamedAttribute;
        columnNames[4] = ListHappyStartDateAttribute;
        columnNames[5] = ListHappyEndDateAttribute;
        Array.Copy(AssignmentLookupAttributes, 0, columnNames, 6, AssignmentLookupAttributes.Length);
        return new ColumnSet(columnNames);
    }

    /// <summary>
    /// 驗證一筆 query row 的固定 entity identity、filter echoes、六 lookup match 與 legacy inclusive date rule。
    /// start/end 缺值或 Year=1 代表未設定邊界；其餘 DateTime 轉為 local 後與 server-owned now 比較。型別、logical
    /// name、空 ID、filter drift、非 contact lookup 或非 subject lookup 一律 fail closed，不發布 partial list。
    /// </summary>
    private static Guid ProjectValidAssignedListId(Entity entity, Guid subjectContactId, DateTime localNow)
    {
        if (entity is null ||
            !string.Equals(entity.LogicalName, ListEntityName, StringComparison.Ordinal) ||
            entity.Id == Guid.Empty ||
            (entity.Attributes.TryGetValue(ListIdAttribute, out var projectedId) &&
             projectedId is not null &&
             (projectedId is not Guid attributeId || attributeId != entity.Id)) ||
            ReadRequiredOptionSetValue(entity, ListStateCodeAttribute) != 0 ||
            !string.Equals(ReadRequiredString(entity, ListPurposeAttribute), SmallGroupPurpose, StringComparison.Ordinal) ||
            !ReadRequiredBoolean(entity, ListAppNamedAttribute) ||
            !HasSubjectAssignmentLookup(entity, subjectContactId) ||
            !IsWithinLegacyHappyDateRange(entity, localNow))
        {
            throw new InvalidOperationException("The Data8 MemberInfo assignment list row is invalid.");
        }

        return entity.Id;
    }

    /// <summary>
    /// 確認六個 lookup 至少一個是 non-empty <c>contact</c> reference 且精確等於 subject；每個存在欄位也都必須是
    /// 同一嚴格型別。這防止 query/projection drift 將另一位使用者、未知 entity 或空 lookup 變成 assignment evidence。
    /// </summary>
    private static bool HasSubjectAssignmentLookup(Entity entity, Guid subjectContactId)
    {
        var matched = false;
        foreach (var attributeName in AssignmentLookupAttributes)
        {
            if (!entity.Attributes.TryGetValue(attributeName, out var value) || value is null)
            {
                continue;
            }

            if (value is not EntityReference reference ||
                !string.Equals(reference.LogicalName, ContactEntityName, StringComparison.Ordinal) ||
                reference.Id == Guid.Empty)
            {
                throw new InvalidOperationException("The Data8 MemberInfo assignment lookup is invalid.");
            }

            matched |= reference.Id == subjectContactId;
        }

        return matched;
    }

    /// <summary>
    /// 保留 legacy <c>MergeCollectionSmallGroupAhead</c> 的日期語意：開始／結束皆採 local inclusive comparison；
    /// 缺欄、null 或 Year=1 為未設定邊界，其他型別則拒絕。此 helper 使用由執行環境擁有的 TimeProvider，不讀取
    /// browser date、Session timezone 或前一次 request 的時鐘狀態。
    /// </summary>
    private static bool IsWithinLegacyHappyDateRange(Entity entity, DateTime localNow)
    {
        var start = ReadOptionalLegacyLocalDate(entity, ListHappyStartDateAttribute);
        var end = ReadOptionalLegacyLocalDate(entity, ListHappyEndDateAttribute);
        return (!start.HasValue || localNow >= start.Value) && (!end.HasValue || localNow <= end.Value);
    }

    /// <summary>
    /// 讀取 optional legacy date，並把有效值轉為 local time 以與既有畫面一致。此方法不快取時區或 DateTime object；
    /// 轉換後純值只在目前 row validation 存活，Year=1 表示未設定而非可被另一欄位或 fallback 替代。
    /// </summary>
    private static DateTime? ReadOptionalLegacyLocalDate(Entity entity, string attributeName)
    {
        if (!entity.Attributes.TryGetValue(attributeName, out var value) || value is null)
        {
            return null;
        }

        if (value is not DateTime dateTime)
        {
            throw new InvalidOperationException("The Data8 MemberInfo assignment date is invalid.");
        }

        return dateTime.Year <= 1 ? null : dateTime.ToLocalTime();
    }

    /// <summary>
    /// 讀取固定 filter echo 的必要 string，拒絕 null、空白與非 string 型別，避免上游傳回的 row 在未完整驗證時
    /// 被當作小組名單。這個 helper 不使用 ToString fallback，也不保留 Entity attribute graph。
    /// </summary>
    private static string ReadRequiredString(Entity entity, string attributeName)
        => entity.Attributes.TryGetValue(attributeName, out var value) && value is string text && !string.IsNullOrWhiteSpace(text)
            ? text
            : throw new InvalidOperationException("The Data8 MemberInfo assignment string projection is invalid.");

    /// <summary>
    /// 讀取固定 filter echo 的必要 bool。只有真正 <see cref="bool"/> 值可通過；OptionSet、string 或缺欄不會被
    /// 寬鬆轉換，避免 schema drift 開啟未經證實的 assignment list。
    /// </summary>
    private static bool ReadRequiredBoolean(Entity entity, string attributeName)
        => entity.Attributes.TryGetValue(attributeName, out var value) && value is bool flag
            ? flag
            : throw new InvalidOperationException("The Data8 MemberInfo assignment boolean projection is invalid.");

    /// <summary>
    /// 讀取固定 filter echo 的必要 OptionSet raw value。active state 必須精確為零；任何缺欄、裸 int 或其他 SDK
    /// 型別都拒絕，避免只相信 query filter 而忽略 connector 替身或上游 schema 漂移。
    /// </summary>
    private static int ReadRequiredOptionSetValue(Entity entity, string attributeName)
        => entity.Attributes.TryGetValue(attributeName, out var value) && value is OptionSetValue optionSet
            ? optionSet.Value
            : throw new InvalidOperationException("The Data8 MemberInfo assignment option-set projection is invalid.");
}

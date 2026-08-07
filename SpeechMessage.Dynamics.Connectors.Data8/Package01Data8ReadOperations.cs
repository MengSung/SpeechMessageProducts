// ============================================================================
// 檔案：SpeechMessage.Dynamics.Connectors.Data8/Package01Data8ReadOperations.cs
// 責任：在 Data8 connector 內部將 P7.1 的六項固定讀取 capability 轉為受控
//       QueryExpression，並將 CRM EntityCollection 投影為不含 SDK 物件的 DTO。
//
// 此檔是 D365 查詢細節的唯一 owner。ProductClient、Embedded、Dedicated Gateway
// 與未來 Central Gateway 只能傳遞已登錄的 operation ID 與型別化 scalar；它們
// 不可傳入 FetchXML、Entity、QueryBase、endpoint、credential 或任何 CRM SDK 型別。
// 每次呼叫的 Entity、page 與投影資料只活在 connector lease 的同步執行範圍；回傳
// 的 OperationResponseData 僅含不可變純值，故不會把 profile、session、credential
// 或 WCF/CRM 資源延長到下一個 request 或另一個 profile。
// ============================================================================

using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Connectors.Data8;

/// <summary>
/// 集中擁有 P7.1 六項 Package01 唯讀 capability 的 Data8 查詢與投影。
///
/// 此類別刻意不公開，也不接受泛型 CRM 指令；唯一入口以 operation allowlist 決定
/// 固定 entity、欄位、條件、排序與回應分支。呼叫端提供的名稱只會在上游相容層驗證後
/// 丟棄，查詢權威永遠是 Guid、UTC 日期範圍與已核准的有限字串。任何 operation、
/// CE version、參數、page、Entity 型別或回應大小不符時都會失敗，讓 lease owner
/// 將 client 標記為 faulted，而不會把不可信的 session 放回 pool。
/// </summary>
internal static class Package01Data8ReadOperations
{
    private const string FeeEntityName = "new_fee";
    private const string FeeIdAttribute = "new_feeid";
    private const string FeeNameAttribute = "new_name";
    private const string FeeCreatedOnAttribute = "createdon";
    private const string FeePayDateAttribute = "new_pay_date";
    private const string FeeAmountAttribute = "new_fee_really_paid";
    private const string FeePayWayAttribute = "new_pay_way";
    private const string FeeCategoryAttribute = "new_category";
    private const string FeeOthersAttribute = "new_others";
    private const string FeePaidPeriodAttribute = "new_paid_period";
    private const string FeeContactAttribute = "new_contact_new_fee";
    private const string FeeDedicationBookingAttribute = "new_dedication_booking_new_fee";
    private const string FeePayStatusAttribute = "new_pay_status";
    private const string StateCodeAttribute = "statecode";

    private const string StorLessonEntityName = "new_stor_lessons";
    private const string StorLessonIdAttribute = "new_stor_lessonsid";
    private const string StorLessonContactAttribute = "new_contact_new_stor_lessons";
    private const string StorLessonFeeAmountAttribute = "new_fee";
    private const string StorLessonPayDateAttribute = "new_pay_date";
    private const string StorLessonCurrentCompleteAttribute = "new_current_complete";
    private const string StorLessonDiscipleLessonAttribute = "new_new_disciple_lessons_new_stor_les";
    private const string StorLessonEnrollmentStatusAttribute = "new_enroll_status";
    private const string StorLessonStatusCodeAttribute = "statuscode";
    private const string ContactEntityName = "contact";
    private const string ContactIdAttribute = "contactid";
    private const string ContactNameAttribute = "fullname";
    private const string ContactMobileAttribute = "mobilephone";
    private const string DiscipleLessonEntityName = "new_disciple_lessons";
    private const string DiscipleLessonIdAttribute = "new_disciple_lessonsid";
    private const string DiscipleLessonNameAttribute = "new_name";
    private const string ContactAlias = "contact";
    private const string LessonAlias = "lesson";

    private const int MaximumRowsPerPage = 128;
    private const int MaximumStringParameterBytes = 256;
    private static readonly object[] AcceptedDateRangePayStatuses =
    [
        100000001,
        100000002,
        100000003,
        100000004,
        100000006
    ];
    private static readonly object[] ExcludedStorLessonEnrollmentStatuses =
    [
        100000007,
        100000009,
        100000003
    ];
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>
    /// 依已核准 operation 建立固定 QueryExpression、執行有限頁數讀取並建立封閉回應。
    ///
    /// <paramref name="service"/> 由目前 Data8 connector client 唯一持有，最終由 Pool/Lease
    /// 在成功、例外、取消或 fault 時釋放；此方法不快取 service、Entity、query 或任何結果。
    /// <paramref name="operation"/> 已由 executor 在第一次非同步等待前複製及驗證，但此處仍對
    /// 關鍵 scalar 重驗，確保直接 connector 呼叫也無法繞過固定查詢邊界。
    /// </summary>
    /// <param name="service">目前 lease 專屬且由 connector 擁有的 Data8 Organization Service。</param>
    /// <param name="operation">只含 allowlisted operation 與已型別化 scalar 的短生命週期請求。</param>
    /// <param name="ceVersion">由 immutable resolved profile 決定的 CE 版本，不得由 request 改寫。</param>
    /// <returns>只含 fee 或 stor-lesson 純值記錄的封閉 response branch。</returns>
    internal static OperationResponseData Execute(
        IOrganizationService service,
        ConnectorOperation operation,
        string ceVersion)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(operation);
        if (ceVersion is not ("8.2" or "9.1"))
        {
            throw new InvalidOperationException("The Data8 CE version is not supported.");
        }

        return operation.OperationId switch
        {
            OperationIds.FeeDedicationRetrieveByContact => ExecuteFeeDedicationByContact(service, operation, ceVersion),
            OperationIds.FeeDedicationRetrieveByContactDateRange => ExecuteFeeDedicationByContactDateRange(service, operation, ceVersion),
            OperationIds.FeesRetrieveByDedicationPeriod => ExecuteFeesByDedicationPeriod(service, operation, ceVersion),
            OperationIds.FeesEditorLoadByDiscipleLesson => ExecuteStorLessonsByDiscipleLesson(service, operation, ceVersion),
            OperationIds.LessonsStorRetrieveByContact => ExecuteStorLessonsByContact(service, operation, ceVersion),
            OperationIds.LessonsStorRetrieveByDiscipleLesson => ExecuteStorLessonsByDiscipleLesson(service, operation, ceVersion),
            _ => throw new InvalidOperationException("The Data8 Package01 operation is not permitted.")
        };
    }

    /// <summary>
    /// 執行依 contact 取得 dedication fee 的固定查詢。contact 名稱不是查詢條件，藉此避免同名
    /// 資料或 legacy display 值改變結果；只允許非空 Guid 進入 CRM filter。
    /// </summary>
    private static OperationResponseData ExecuteFeeDedicationByContact(
        IOrganizationService service,
        ConnectorOperation operation,
        string ceVersion)
    {
        var definition = GetDefinition(operation.OperationId, OperationResponseKind.Package01FeeRecords);
        var contactId = ReadRequiredGuid(operation.Parameters, "contactId");
        var records = RetrieveFeeRecords(service, CreateFeeDedicationByContactQuery(contactId), definition);
        return OperationResponseData.ForPackage01FeeRecords(operation.OperationId, ceVersion, records);
    }

    /// <summary>
    /// 執行依 contact 與 UTC 日期區間取得已核准付款狀態 fee 的固定查詢。
    /// 日期端點採 OnOrAfter/OnOrBefore，保留舊查詢的包含式區間語意；上游已拒絕反向區間，
    /// 這裡不依本機時區重新解讀輸入。
    /// </summary>
    private static OperationResponseData ExecuteFeeDedicationByContactDateRange(
        IOrganizationService service,
        ConnectorOperation operation,
        string ceVersion)
    {
        var definition = GetDefinition(operation.OperationId, OperationResponseKind.Package01FeeRecords);
        var contactId = ReadRequiredGuid(operation.Parameters, "contactId");
        var startDate = ReadRequiredUtcDateTime(operation.Parameters, "startDate");
        var endDate = ReadRequiredUtcDateTime(operation.Parameters, "endDate");
        if (startDate > endDate)
        {
            throw new InvalidOperationException("The Data8 fee date range is invalid.");
        }

        var records = RetrieveFeeRecords(
            service,
            CreateFeeDedicationByContactDateRangeQuery(contactId, startDate, endDate),
            definition);
        return OperationResponseData.ForPackage01FeeRecords(operation.OperationId, ceVersion, records);
    }

    /// <summary>
    /// 執行依 dedication booking 與 paid period 取得 active fee 的固定查詢。
    /// paid period 是有限字串 scalar，不採 caller 提供的 XML fragment 或欄位名稱；排序完全固定，
    /// 使 Embedded 與 Gateway mode 的同一 profile 得到可比較的順序。
    /// </summary>
    private static OperationResponseData ExecuteFeesByDedicationPeriod(
        IOrganizationService service,
        ConnectorOperation operation,
        string ceVersion)
    {
        var definition = GetDefinition(operation.OperationId, OperationResponseKind.Package01FeeRecords);
        var dedicationBookingId = ReadRequiredGuid(operation.Parameters, "dedicationBookingId");
        var paidPeriod = ReadRequiredBoundedString(operation.Parameters, "paidPeriod");
        var records = RetrieveFeeRecords(
            service,
            CreateFeesByDedicationPeriodQuery(dedicationBookingId, paidPeriod),
            definition);
        return OperationResponseData.ForPackage01FeeRecords(operation.OperationId, ceVersion, records);
    }

    /// <summary>
    /// 執行依 contact 取得 active stor-lesson 的固定查詢。聯絡人與課程 join 都由伺服器定義；
    /// link 的 alias 與投影欄位是本類別的私有細節，絕不跨出 SDK-free connector 邊界。
    /// </summary>
    private static OperationResponseData ExecuteStorLessonsByContact(
        IOrganizationService service,
        ConnectorOperation operation,
        string ceVersion)
    {
        var definition = GetDefinition(operation.OperationId, OperationResponseKind.Package01StorLessonRecords);
        var contactId = ReadRequiredGuid(operation.Parameters, "contactId");
        var records = RetrieveStorLessonRecords(service, CreateStorLessonsByContactQuery(contactId), definition);
        return OperationResponseData.ForPackage01StorLessonRecords(operation.OperationId, ceVersion, records);
    }

    /// <summary>
    /// 執行 fee editor 與 lesson lookup 共用的依 disciple lesson 固定查詢。
    /// 兩個 capability 擁有不同 operation ID 與授權／觀測生命週期，但現階段採用相同舊版 read
    /// 語意；未來其中一方需要改變時，必須先分離 registry revision、測試與 P7 matrix row。
    /// </summary>
    private static OperationResponseData ExecuteStorLessonsByDiscipleLesson(
        IOrganizationService service,
        ConnectorOperation operation,
        string ceVersion)
    {
        var definition = GetDefinition(operation.OperationId, OperationResponseKind.Package01StorLessonRecords);
        var discipleLessonId = ReadRequiredGuid(operation.Parameters, "discipleLessonId");
        var records = RetrieveStorLessonRecords(
            service,
            CreateStorLessonsByDiscipleLessonQuery(discipleLessonId),
            definition);
        return OperationResponseData.ForPackage01StorLessonRecords(operation.OperationId, ceVersion, records);
    }

    /// <summary>
    /// 建立依 contact 讀取 dedication fee 的 server-owned QueryExpression。設定 deterministic paging
    /// 與 secondary ID order，避免 CRM 預設排序造成跨頁遺漏或讓不同 hosting mode 產生不穩定結果。
    /// </summary>
    private static QueryExpression CreateFeeDedicationByContactQuery(Guid contactId)
    {
        var query = CreateFeeQuery();
        query.Criteria.AddCondition(FeeContactAttribute, ConditionOperator.Equal, contactId);
        query.Criteria.AddCondition(FeeCategoryAttribute, ConditionOperator.NotNull);
        query.AddOrder(FeeNameAttribute, OrderType.Ascending);
        query.AddOrder(FeeIdAttribute, OrderType.Ascending);
        return query;
    }

    /// <summary>
    /// 建立依 contact 和付款日期讀取 fee 的 server-owned QueryExpression。付款狀態集合、日期欄位及
    /// 包含式範圍均固定為舊費用查詢的已核准語意，不能由產品 request 自訂。
    /// </summary>
    private static QueryExpression CreateFeeDedicationByContactDateRangeQuery(
        Guid contactId,
        DateTimeOffset startDate,
        DateTimeOffset endDate)
    {
        var query = CreateFeeQuery();
        query.Criteria.AddCondition(FeeContactAttribute, ConditionOperator.Equal, contactId);
        query.Criteria.AddCondition(FeeCategoryAttribute, ConditionOperator.NotNull);
        query.Criteria.AddCondition(FeePayStatusAttribute, ConditionOperator.In, AcceptedDateRangePayStatuses);
        query.Criteria.AddCondition(FeePayDateAttribute, ConditionOperator.OnOrAfter, startDate.UtcDateTime);
        query.Criteria.AddCondition(FeePayDateAttribute, ConditionOperator.OnOrBefore, endDate.UtcDateTime);
        query.AddOrder(FeeNameAttribute, OrderType.Ascending);
        query.AddOrder(FeeIdAttribute, OrderType.Ascending);
        return query;
    }

    /// <summary>
    /// 建立依 dedication booking 與 paid period 讀取 active fee 的固定 QueryExpression。
    /// 以 createdon 加 primary ID 的明確排序取代 CRM 未指定排序，避免 page 邊界重複或漏讀。
    /// </summary>
    private static QueryExpression CreateFeesByDedicationPeriodQuery(Guid dedicationBookingId, string paidPeriod)
    {
        var query = CreateFeeQuery();
        query.Criteria.AddCondition(FeeDedicationBookingAttribute, ConditionOperator.Equal, dedicationBookingId);
        query.Criteria.AddCondition(FeePaidPeriodAttribute, ConditionOperator.Equal, paidPeriod);
        query.Criteria.AddCondition(StateCodeAttribute, ConditionOperator.Equal, 0);
        query.AddOrder(FeeCreatedOnAttribute, OrderType.Descending);
        query.AddOrder(FeeIdAttribute, OrderType.Ascending);
        return query;
    }

    /// <summary>
    /// 建立所有 fee capability 共用的最小 ColumnSet 與有界第一頁。此共用工廠避免六項 operation
    /// 漸漸分歧為不同的 projection 或無界 page policy；query 本身仍只在目前 connector call 中存在。
    /// </summary>
    private static QueryExpression CreateFeeQuery()
        => new(FeeEntityName)
        {
            ColumnSet = new ColumnSet(
                FeeIdAttribute,
                FeeNameAttribute,
                FeeCreatedOnAttribute,
                FeePayDateAttribute,
                FeeAmountAttribute,
                FeePayWayAttribute,
                FeeCategoryAttribute,
                FeeOthersAttribute,
                FeePaidPeriodAttribute),
            Criteria = new FilterExpression(LogicalOperator.And),
            PageInfo = CreateInitialPageInfo()
        };

    /// <summary>
    /// 建立依 contact 取得 active stor lesson 的固定查詢。contact join 為 left outer，課程 join 保持
    /// inner，對應舊路徑「有有效課程才顯示」的資料語意；沒有任何 caller 欄位可改寫 join。
    /// </summary>
    private static QueryExpression CreateStorLessonsByContactQuery(Guid contactId)
    {
        var query = CreateStorLessonQuery();
        query.Criteria.AddCondition(StorLessonContactAttribute, ConditionOperator.Equal, contactId);
        query.Criteria.AddCondition(StateCodeAttribute, ConditionOperator.Equal, 0);
        AddContactLink(query);
        AddDiscipleLessonLink(query);
        return query;
    }

    /// <summary>
    /// 建立依 disciple lesson 取得可用 stor lesson 的固定查詢。排除 enrollment status、statuscode 與
    /// statecode 的規則來自既有費用編輯／課程畫面 read 語意；不以「非正式環境」為由放寬條件。
    /// </summary>
    private static QueryExpression CreateStorLessonsByDiscipleLessonQuery(Guid discipleLessonId)
    {
        var query = CreateStorLessonQuery();
        query.Criteria.AddCondition(
            StorLessonEnrollmentStatusAttribute,
            ConditionOperator.NotIn,
            ExcludedStorLessonEnrollmentStatuses);
        query.Criteria.AddCondition(StorLessonDiscipleLessonAttribute, ConditionOperator.Equal, discipleLessonId);
        query.Criteria.AddCondition(StorLessonStatusCodeAttribute, ConditionOperator.NotEqual, 2);
        query.Criteria.AddCondition(StateCodeAttribute, ConditionOperator.Equal, 0);
        AddContactLink(query);
        return query;
    }

    /// <summary>
    /// 建立 stor lesson operation 共用的受限投影與排序。每頁最多 128 筆，而 registry 仍會限制總頁數、
    /// 總列數與累積回應 bytes；不能因個別 query 看似小而取消這些記憶體與 lease 壽命上限。
    /// </summary>
    private static QueryExpression CreateStorLessonQuery()
    {
        var query = new QueryExpression(StorLessonEntityName)
        {
            ColumnSet = new ColumnSet(
                StorLessonIdAttribute,
                FeeCreatedOnAttribute,
                StorLessonContactAttribute,
                StorLessonFeeAmountAttribute,
                StorLessonPayDateAttribute,
                StorLessonCurrentCompleteAttribute,
                StorLessonDiscipleLessonAttribute),
            Criteria = new FilterExpression(LogicalOperator.And),
            PageInfo = CreateInitialPageInfo()
        };
        query.AddOrder(FeeCreatedOnAttribute, OrderType.Descending);
        query.AddOrder(StorLessonIdAttribute, OrderType.Ascending);
        return query;
    }

    /// <summary>
    /// 將 contact 顯示欄位加入既定 alias；alias 是 connector-private projection contract，若 CRM 回傳
    /// 不同型別則投影失敗，不會以 ToString 或 SDK object 洩漏的方式勉強成功。
    /// </summary>
    private static void AddContactLink(QueryExpression query)
    {
        var contact = query.AddLink(
            ContactEntityName,
            StorLessonContactAttribute,
            ContactIdAttribute,
            JoinOperator.LeftOuter);
        contact.EntityAlias = ContactAlias;
        contact.Columns = new ColumnSet(ContactNameAttribute, ContactMobileAttribute);
    }

    /// <summary>
    /// 將 disciple lesson 名稱加入既定 alias。僅依 contact 查詢需要這個 inner join；依 lesson 的 operation
    /// 已由輸入 Guid 限定集合，仍保留 contact join 以投影姓名／手機，而不信任 legacy lesson name。
    /// </summary>
    private static void AddDiscipleLessonLink(QueryExpression query)
    {
        var lesson = query.AddLink(
            DiscipleLessonEntityName,
            StorLessonDiscipleLessonAttribute,
            DiscipleLessonIdAttribute,
            JoinOperator.Inner);
        lesson.EntityAlias = LessonAlias;
        lesson.Columns = new ColumnSet(DiscipleLessonNameAttribute);
    }

    /// <summary>
    /// 產生每個 operation 都使用的第一頁設定。PagingCookie 一律由上一個 CRM response 取得，不能由 request
    /// 注入；若 CRM 聲稱還有下一頁卻未給 cookie，後續讀取會 fail closed 而不回傳不完整資料。
    /// </summary>
    private static PagingInfo CreateInitialPageInfo()
        => new()
        {
            Count = MaximumRowsPerPage,
            PageNumber = 1,
            PagingCookie = null,
            ReturnTotalRecordCount = false
        };

    /// <summary>
    /// 在固定最大頁數內讀取並投影 fee EntityCollection。任何 page 超量、MoreRecords/cookie 不一致、
    /// 無效 Entity 或 byte budget 溢位都中止，不回傳 partial success；外層 executor 會在同一 lease 範圍
    /// 將這類 connector 失敗變成 client eviction。
    /// </summary>
    private static IReadOnlyList<Package01FeeRecord> RetrieveFeeRecords(
        IOrganizationService service,
        QueryExpression query,
        OperationDefinition definition)
    {
        var records = new List<Package01FeeRecord>(MaximumRowsPerPage);
        var totalBytes = 0;
        string? pagingCookie = null;
        for (var pageNumber = 1; pageNumber <= definition.MaximumPageCount; pageNumber++)
        {
            // 單頁與全回應預算各自獨立。先驗證 pageBytes，避免一個 CRM page 雖未超過 4-page
            // cumulative ceiling 卻瞬間佔用過多 lease 記憶體；兩者任一失敗都不回傳 partial DTO。
            var pageBytes = 0;
            query.PageInfo.PageNumber = pageNumber;
            query.PageInfo.PagingCookie = pagingCookie;
            var page = service.RetrieveMultiple(query)
                ?? throw new InvalidOperationException("The Data8 fee query returned no page.");
            if (page.Entities.Count > MaximumRowsPerPage ||
                checked(records.Count + page.Entities.Count) > definition.MaximumResultItemCount)
            {
                throw new InvalidOperationException("The Data8 fee query exceeded its result limit.");
            }

            foreach (var entity in page.Entities)
            {
                var record = ProjectFeeRecord(entity);
                if (!TryAddFeeRecordBytes(ref pageBytes, record, definition.MaximumPageBytes) ||
                    !TryAddFeeRecordBytes(ref totalBytes, record, definition.MaximumCumulativeResponseBytes))
                {
                    throw new InvalidOperationException("The Data8 fee query exceeded its response budget.");
                }

                records.Add(record);
            }

            if (!page.MoreRecords)
            {
                return records;
            }

            if (pageNumber == definition.MaximumPageCount || string.IsNullOrWhiteSpace(page.PagingCookie))
            {
                throw new InvalidOperationException("The Data8 fee query paging contract is invalid.");
            }

            pagingCookie = page.PagingCookie;
        }

        throw new InvalidOperationException("The Data8 fee query exceeded its page limit.");
    }

    /// <summary>
    /// 在與 fee 相同的 bounded paging 規則下讀取 stor lesson。兩種 record 的投影分開實作，讓 response
    /// discriminator、欄位型別與 byte accounting 都有單一 owner，避免用 object 或 raw Entity 模糊邊界。
    /// </summary>
    private static IReadOnlyList<Package01StorLessonRecord> RetrieveStorLessonRecords(
        IOrganizationService service,
        QueryExpression query,
        OperationDefinition definition)
    {
        var records = new List<Package01StorLessonRecord>(MaximumRowsPerPage);
        var totalBytes = 0;
        string? pagingCookie = null;
        for (var pageNumber = 1; pageNumber <= definition.MaximumPageCount; pageNumber++)
        {
            // 與 fee path 相同，stor-lesson page 不得以四頁累積上限取代單頁記憶體邊界。
            // pageBytes 僅存在目前同步 SDK page 的 scope，不能跨 page 或 request 保留。
            var pageBytes = 0;
            query.PageInfo.PageNumber = pageNumber;
            query.PageInfo.PagingCookie = pagingCookie;
            var page = service.RetrieveMultiple(query)
                ?? throw new InvalidOperationException("The Data8 stor-lesson query returned no page.");
            if (page.Entities.Count > MaximumRowsPerPage ||
                checked(records.Count + page.Entities.Count) > definition.MaximumResultItemCount)
            {
                throw new InvalidOperationException("The Data8 stor-lesson query exceeded its result limit.");
            }

            foreach (var entity in page.Entities)
            {
                var record = ProjectStorLessonRecord(entity);
                if (!TryAddStorLessonRecordBytes(ref pageBytes, record, definition.MaximumPageBytes) ||
                    !TryAddStorLessonRecordBytes(ref totalBytes, record, definition.MaximumCumulativeResponseBytes))
                {
                    throw new InvalidOperationException("The Data8 stor-lesson query exceeded its response budget.");
                }

                records.Add(record);
            }

            if (!page.MoreRecords)
            {
                return records;
            }

            if (pageNumber == definition.MaximumPageCount || string.IsNullOrWhiteSpace(page.PagingCookie))
            {
                throw new InvalidOperationException("The Data8 stor-lesson query paging contract is invalid.");
            }

            pagingCookie = page.PagingCookie;
        }

        throw new InvalidOperationException("The Data8 stor-lesson query exceeded its page limit.");
    }

    /// <summary>
    /// 將單一 fee Entity 投影為純值 record。不得以 ToString、dynamic 或未知型別 fallback 掩蓋 CRM schema
    /// 漂移；可選欄位可缺，但已存在的欄位必須是預期 CLR 型別，否則 client 應由 lease 淘汰。
    /// </summary>
    private static Package01FeeRecord ProjectFeeRecord(Entity entity)
    {
        ValidateEntityIdentity(entity, FeeEntityName, FeeIdAttribute);
        var payWay = ReadOptionalValue<OptionSetValue>(entity, FeePayWayAttribute);
        _ = ReadOptionalValue<OptionSetValue>(entity, FeeCategoryAttribute);
        return new Package01FeeRecord
        {
            FeeId = entity.Id,
            CreatedOn = ReadOptionalUtcDateTime(entity, FeeCreatedOnAttribute),
            PayDate = ReadOptionalUtcDateTime(entity, FeePayDateAttribute),
            Amount = ReadOptionalValue<Money>(entity, FeeAmountAttribute)?.Value ?? 0m,
            PayWayOption = payWay?.Value,
            PayWayLabel = ReadOptionalFormattedValue(entity, FeePayWayAttribute),
            CategoryLabel = ReadOptionalFormattedValue(entity, FeeCategoryAttribute),
            Others = ReadOptionalString(entity, FeeOthersAttribute),
            PaidPeriod = ReadOptionalString(entity, FeePaidPeriodAttribute),
            Name = ReadOptionalString(entity, FeeNameAttribute)
        };
    }

    /// <summary>
    /// 將單一 stor lesson Entity 及固定 join alias 投影為純值 record。lookup、Money、boolean 與 aliased
    /// 值均採精確型別讀取；沒有任何 CRM Entity、EntityReference、AliasedValue 或 formatted dictionary
    /// 會被存入 response、cache 或下一個 request。
    /// </summary>
    private static Package01StorLessonRecord ProjectStorLessonRecord(Entity entity)
    {
        ValidateEntityIdentity(entity, StorLessonEntityName, StorLessonIdAttribute);
        var contact = ReadOptionalValue<EntityReference>(entity, StorLessonContactAttribute);
        var discipleLesson = ReadOptionalValue<EntityReference>(entity, StorLessonDiscipleLessonAttribute);
        return new Package01StorLessonRecord
        {
            StorLessonId = entity.Id,
            ContactId = ReadEntityReferenceId(contact),
            DiscipleLessonId = ReadEntityReferenceId(discipleLesson),
            CreatedOn = ReadOptionalUtcDateTime(entity, FeeCreatedOnAttribute),
            PayDate = ReadOptionalUtcDateTime(entity, StorLessonPayDateAttribute),
            CurrentComplete = ReadOptionalBoolean(entity, StorLessonCurrentCompleteAttribute),
            ContactName = ReadOptionalAliasedString(entity, ContactAlias + "." + ContactNameAttribute),
            ContactMobile = ReadOptionalAliasedString(entity, ContactAlias + "." + ContactMobileAttribute),
            DiscipleLessonName = ReadOptionalAliasedString(entity, LessonAlias + "." + DiscipleLessonNameAttribute)
                ?? ReadOptionalFormattedValue(entity, StorLessonDiscipleLessonAttribute),
            FeeAmount = ReadOptionalValue<Money>(entity, StorLessonFeeAmountAttribute)?.Value
        };
    }

    /// <summary>
    /// 讀取 operation registry 定義並確認回應分支。registry 宣告本身不是 connector 實作授權；此檢查
    /// 防止未來錯將 fee query 投影成 stor branch，或因 registry 漂移而降低目前 operation 的資源上限。
    /// </summary>
    private static OperationDefinition GetDefinition(string operationId, OperationResponseKind expectedResponseKind)
    {
        if (!Package01OperationRegistry.TryGet(operationId, out var definition) ||
            definition is null ||
            definition.ResponseKind != expectedResponseKind)
        {
            throw new InvalidOperationException("The Data8 operation registry definition is invalid.");
        }

        return definition;
    }

    /// <summary>
    /// 讀取必填 Guid。此重驗可防止直接 connector 使用者以空 Guid 或不正確 CLR 型別繞過 executor；
    /// 失敗發生在 service 呼叫前，因此不會建立額外 CRM session 或造成不明資料範圍。
    /// </summary>
    private static Guid ReadRequiredGuid(IReadOnlyDictionary<string, object?> parameters, string name)
    {
        if (!parameters.TryGetValue(name, out var value) || value is not Guid guid || guid == Guid.Empty)
        {
            throw new InvalidOperationException("The Data8 operation parameter is invalid.");
        }

        return guid;
    }

    /// <summary>
    /// 讀取必填 UTC 日期。connector 僅接受 executor 已正規化的 DateTimeOffset，避免直接呼叫將 host
    /// local time 混入同一 capability；UTC DateTime 只在 QueryExpression 邊界短暫建立。
    /// </summary>
    private static DateTimeOffset ReadRequiredUtcDateTime(
        IReadOnlyDictionary<string, object?> parameters,
        string name)
    {
        if (!parameters.TryGetValue(name, out var value) || value is not DateTimeOffset dateTime)
        {
            throw new InvalidOperationException("The Data8 operation date parameter is invalid.");
        }

        return dateTime.ToUniversalTime();
    }

    /// <summary>
    /// 讀取必填、有限的純文字 scalar。此欄位僅可作固定 condition value，不能包含 XML、欄位名稱或
    /// 查詢運算子；嚴格 UTF-8 計數同時避免不合法 UTF-16 或過長文字造成無界配置。
    /// </summary>
    private static string ReadRequiredBoundedString(
        IReadOnlyDictionary<string, object?> parameters,
        string name)
    {
        if (!parameters.TryGetValue(name, out var value) || value is not string text)
        {
            throw new InvalidOperationException("The Data8 operation string parameter is invalid.");
        }

        var normalized = text.Trim();
        try
        {
            if (normalized.Length == 0 || StrictUtf8.GetByteCount(normalized) > MaximumStringParameterBytes)
            {
                throw new InvalidOperationException("The Data8 operation string parameter is invalid.");
            }
        }
        catch (EncoderFallbackException)
        {
            throw new InvalidOperationException("The Data8 operation string parameter is invalid.");
        }

        return normalized;
    }

    /// <summary>
    /// 驗證 CRM Entity logical name、primary id 與可選 primary-id attribute 相互一致。SDK materialization
    /// 不一致代表上游回應或 test double 已失去可信度；不允許用 Entity.Id 以外的值繼續投影。
    /// </summary>
    private static void ValidateEntityIdentity(Entity entity, string expectedLogicalName, string primaryIdAttribute)
    {
        if (entity is null ||
            !string.Equals(entity.LogicalName, expectedLogicalName, StringComparison.Ordinal) ||
            entity.Id == Guid.Empty)
        {
            throw new InvalidOperationException("The Data8 entity is invalid.");
        }

        if (!entity.Attributes.TryGetValue(primaryIdAttribute, out var value))
        {
            return;
        }

        if (value is not Guid id || id == Guid.Empty || id != entity.Id)
        {
            throw new InvalidOperationException("The Data8 entity primary id is invalid.");
        }
    }

    /// <summary>
    /// 讀取可選 class 型 CRM 欄位並要求精確 CLR 型別。若 CRM 未回傳欄位可安全投影為 null；若回傳
    /// 非預期物件則 fail closed，避免 SDK graph 或 lookup 被意外序列化到產品邊界。
    /// </summary>
    private static T? ReadOptionalValue<T>(Entity entity, string attributeName)
        where T : class
    {
        if (!entity.Attributes.TryGetValue(attributeName, out var value) || value is null)
        {
            return null;
        }

        return value as T
            ?? throw new InvalidOperationException("The Data8 entity attribute type is invalid.");
    }

    /// <summary>
    /// 讀取可選 boolean，拒絕 string、OptionSet 或 nullable wrapper 的隱式轉換。這保持 DTO 的三態
    /// 語意：欄位不存在為 null，存在 false/true 仍能與 legacy 畫面區分。
    /// </summary>
    private static bool? ReadOptionalBoolean(Entity entity, string attributeName)
    {
        if (!entity.Attributes.TryGetValue(attributeName, out var value) || value is null)
        {
            return null;
        }

        return value is bool boolean
            ? boolean
            : throw new InvalidOperationException("The Data8 entity boolean attribute type is invalid.");
    }

    /// <summary>
    /// 將可選 CRM DateTime 正規化成 UTC DateTimeOffset。CRM 的 Unspecified 值依既有 connector
    /// 契約視為 UTC；不讀取目前主機時區，故 Lenovo、Dedicated 與 Central 的相同資料不會產生時差。
    /// </summary>
    private static DateTimeOffset? ReadOptionalUtcDateTime(Entity entity, string attributeName)
    {
        if (!entity.Attributes.TryGetValue(attributeName, out var value) || value is null)
        {
            return null;
        }

        if (value is not DateTime dateTime)
        {
            throw new InvalidOperationException("The Data8 entity date attribute type is invalid.");
        }

        var utcDateTime = dateTime.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
            : dateTime.ToUniversalTime();
        return new DateTimeOffset(utcDateTime).ToUniversalTime();
    }

    /// <summary>
    /// 讀取可選純文字欄位；不採 ToString fallback，因錯誤型別可能包含 SDK、session 或 metadata graph。
    /// </summary>
    private static string? ReadOptionalString(Entity entity, string attributeName)
    {
        if (!entity.Attributes.TryGetValue(attributeName, out var value) || value is null)
        {
            return null;
        }

        return value as string
            ?? throw new InvalidOperationException("The Data8 entity string attribute type is invalid.");
    }

    /// <summary>
    /// 讀取 CRM formatted label。FormattedValues 僅在 connector scope 內取用，回傳後只保留新的 string
    /// 參考；若 provider 以非 string 型別提供 label，視為 schema/connector fault 而不猜測轉換方式。
    /// </summary>
    private static string? ReadOptionalFormattedValue(Entity entity, string attributeName)
    {
        if (!entity.FormattedValues.TryGetValue(attributeName, out var value) || value is null)
        {
            return null;
        }

        return value;
    }

    /// <summary>
    /// 讀取固定 link alias 的字串值。AliasedValue 只容許 string payload，不會被保留在 DTO 或 pool；
    /// 缺少 outer-join row 時回傳 null，保留資料缺失而非虛構名稱。
    /// </summary>
    private static string? ReadOptionalAliasedString(Entity entity, string aliasAttributeName)
    {
        if (!entity.Attributes.TryGetValue(aliasAttributeName, out var value) || value is null)
        {
            return null;
        }

        if (value is not AliasedValue { Value: string text })
        {
            throw new InvalidOperationException("The Data8 aliased attribute type is invalid.");
        }

        return text;
    }

    /// <summary>
    /// 從 EntityReference 取出非空 Guid 後立即丟棄 reference。這防止 EntityReference.Name、logical name
    /// 或其他 SDK 資訊沿 response 留存，並維持產品端只依 DTO GUID 作後續受控讀取。
    /// </summary>
    private static Guid? ReadEntityReferenceId(EntityReference? reference)
    {
        if (reference is null)
        {
            return null;
        }

        if (reference.Id == Guid.Empty)
        {
            throw new InvalidOperationException("The Data8 entity reference is invalid.");
        }

        return reference.Id;
    }

    /// <summary>
    /// 累積 fee response 的保守序列化大小。每筆先保留固定結構開銷，再加所有可顯示字串；超限時
    /// 整個 operation 失敗，避免部分 financial 結果被誤視為完整成功。
    /// </summary>
    private static bool TryAddFeeRecordBytes(ref int totalBytes, Package01FeeRecord record, int maximumBytes)
    {
        return TryAddBytes(ref totalBytes, 256, maximumBytes) &&
               TryAddStringBytes(ref totalBytes, record.PayWayLabel, maximumBytes) &&
               TryAddStringBytes(ref totalBytes, record.CategoryLabel, maximumBytes) &&
               TryAddStringBytes(ref totalBytes, record.Others, maximumBytes) &&
               TryAddStringBytes(ref totalBytes, record.PaidPeriod, maximumBytes) &&
               TryAddStringBytes(ref totalBytes, record.Name, maximumBytes);
    }

    /// <summary>
    /// 累積 stor lesson response 的保守序列化大小。contact 與 lesson 顯示字串同樣受累積限制，防止
    /// join 結果藉長字串佔用 connector request memory 或跨越 pool lease 的資源上限。
    /// </summary>
    private static bool TryAddStorLessonRecordBytes(
        ref int totalBytes,
        Package01StorLessonRecord record,
        int maximumBytes)
    {
        return TryAddBytes(ref totalBytes, 256, maximumBytes) &&
               TryAddStringBytes(ref totalBytes, record.ContactName, maximumBytes) &&
               TryAddStringBytes(ref totalBytes, record.ContactMobile, maximumBytes) &&
               TryAddStringBytes(ref totalBytes, record.DiscipleLessonName, maximumBytes);
    }

    /// <summary>
    /// 以 checked 算術累積 response bytes。整數溢位與超過 registry 上限都回傳 false，而不是 wrap
    /// 成較小數字後放行；caller 會以固定錯誤結束 request 並讓 lease 進行 cleanup。
    /// </summary>
    private static bool TryAddBytes(ref int totalBytes, int additionalBytes, int maximumBytes)
    {
        try
        {
            totalBytes = checked(totalBytes + additionalBytes);
            return totalBytes <= maximumBytes;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>
    /// 以嚴格 UTF-8 計入 nullable 顯示字串。無效 UTF-16 不以 replacement character 偷偷放寬預算，
    /// 而是讓 operation 失敗並由外層 lease owner 淘汰可能不可信的 connector client。
    /// </summary>
    private static bool TryAddStringBytes(ref int totalBytes, string? value, int maximumBytes)
    {
        if (value is null)
        {
            return true;
        }

        try
        {
            return TryAddBytes(ref totalBytes, StrictUtf8.GetByteCount(value), maximumBytes);
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }
}

// ============================================================================
// 檔案：SpeechMessage.Dynamics.Connectors.Data8/Package02Data8PresentRecordReadOperations.cs
// 用途：實作 ORG-CALL-00026 的 CE 9.1 固定出席紀錄唯讀查詢。
//
// 安全、隔離與生命週期邊界：
// 1. 此檔只接受 executor 已依 registry 複製的非空 contactId；不能接收 profile、owner、endpoint、connector、
//    QueryExpression、欄位、排序或 paging cookie，因此瀏覽器 locator 不能成為 CRM routing authority。
// 2. IOrganizationService 與 CRM EntityCollection 由呼叫端的 Data8 lease 擁有。本檔不快取、static 保存或 dispose
//    它們；暫存 Entity、HashSet、List 與 byte counter 都只在此次同步呼叫存活。
// 3. 查詢嚴格限制一頁。MoreRecords、schema 漂移、重複 ID、超限或取消都以例外 fail closed；外層 executor
//    會將 lease 標為 faulted，故不會發布 partial rows 或讓不確定 session 重返同一 profile/generation pool。
// ============================================================================

using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Connectors.Data8;

/// <summary>
/// 擁有 ORG-CALL-00026 的唯一固定 Data8 QueryExpression 與 CRM-to-scalar 投影。
/// 類別沒有可變靜態資料、cache、背景工作、計時器或連線所有權；每次呼叫只建立一份 request-local 純量結果。
/// </summary>
internal static class Package02Data8PresentRecordReadOperations
{
    private const string OperationId = "memberinfo.present.retrieve.by.contact";
    private const string PresentRecordEntityName = "new_present_record";
    private const string PresentRecordIdAttribute = "new_present_recordid";
    private const string SundayPresentAttribute = "new_sunday_present_this_week";
    private const string SmallGroupPresentAttribute = "new_group_present_this_week";
    private const string ExplanationAttribute = "new_explanation";
    private const string SundayDateAttribute = "new_sunday_date";
    private const string ContactLookupAttribute = "new_contact_new_present_record";
    private const string ContactEntityName = "contact";
    private const string ContactIdAttribute = "contactid";
    private const string ContactFullNameAttribute = "fullname";
    private const string ContactAlias = "presentcontact";

    // 128 刻意遠低於 registry 的硬性結果上限。固定單頁同時限制 CRM 實體投影與 request-local HashSet／List 的大小，
    // 並保留既有的日期遞減歷程畫面；不得加入 continuation loop，避免資料或記憶體生命週期失去界限。
    private const int MaximumRowsInFixedPage = 128;
    private const int MaximumTextCharacters = 512;
    private const int MaximumTextBytes = MaximumTextCharacters * 4;
    private const int FixedRecordBudgetBytes = 96;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>
    /// 執行唯一的 CE 9.1 出席紀錄唯讀操作。
    /// 先驗證 operation、版本、registry template kind／response branch 與精確的 <c>contactId</c> schema，再執行固定一頁查詢；
    /// 任何不完整頁面、取消或資料列錯誤都不建立 response envelope。contact fullname 由同一固定 inner link 的 alias 必要投影，
    /// 不做第二次 SDK lookup；這同時維持 legacy 每列 FullName 與最小化 CRM Entity 的生命週期。
    /// </summary>
    /// <param name="service">由目前 Data8 lease 擁有的 Organization Service；本方法不保存或釋放它。</param>
    /// <param name="operation">executor 建立的 immutable server-owned operation；只允許一個 nonempty GUID 參數。</param>
    /// <param name="ceVersion">由已解析 deployment profile 決定的 CE 版本；只接受精確的 <c>9.1</c>。</param>
    /// <param name="cancellationToken">目前 request 的取消訊號；只在同步 CRM I/O 前後觀察，絕不註冊或延長其生命週期。</param>
    /// <returns>經完整 page、schema 與 byte 驗證後才建立的 bounded scalar response branch。</returns>
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
            throw new InvalidOperationException("The Data8 MemberInfo present-record operation is not permitted.");
        }

        var definition = GetDefinition(operation.OperationId);
        var contactId = ReadExactContactId(operation.Parameters);
        var query = CreateFixedQuery(contactId);

        // Data8 SDK 方法為同步呼叫，不能接收取消權杖。因此只在唯一 outbound 呼叫的前後觀察取消；
        // 取消不得觸發第二頁、重試、fallback 或部分結果發佈，例外會讓 lease 走既有的 fault/dispose 路徑。
        cancellationToken.ThrowIfCancellationRequested();
        var page = service.RetrieveMultiple(query)
            ?? throw new InvalidOperationException("The Data8 present-record query returned no page.");
        cancellationToken.ThrowIfCancellationRequested();

        if (page.MoreRecords)
        {
            throw new InvalidOperationException("The Data8 present-record query exceeded its fixed single-page contract.");
        }

        if (page.Entities.Count > MaximumRowsInFixedPage ||
            page.Entities.Count > definition.MaximumResultItemCount)
        {
            throw new InvalidOperationException("The Data8 present-record query exceeded its row limit.");
        }

        var records = new List<MemberInfoPresentRecordReadRecord>(page.Entities.Count);
        var identifiers = new HashSet<Guid>();
        var totalBytes = 0;
        foreach (var entity in page.Entities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = ProjectRecord(entity);
            if (!identifiers.Add(record.PresentRecordId) ||
                !TryAddRecordBytes(ref totalBytes, record, definition.MaximumPageBytes) ||
                totalBytes > definition.MaximumCumulativeResponseBytes)
            {
                throw new InvalidOperationException("The Data8 present-record response is invalid or exceeds its budget.");
            }

            records.Add(record);
        }

        return OperationResponseData.ForMemberInfoPresentRecordReadRecords(
            operation.OperationId,
            ceVersion,
            records);
    }

    /// <summary>
    /// 取得並交叉檢查 registry 的 template kind、response branch 與單頁政策。registry 若與 connector drift，寧可拒絕而不執行
    /// 未被正式描述的 CRM query；本 helper 固定使用 <c>queryexpression</c>，因此不得接受宣告為 <c>fetchxml</c> 或其他模板的
    /// definition，確保 CE version、查詢語意、資料列數與資料上限仍由同一 server-owned contract 管理。
    /// </summary>
    /// <param name="operationId">要查核的 server-owned operation ID。</param>
    /// <returns>已確認為 ORG-CALL-00026 的 immutable registry 定義。</returns>
    private static OperationDefinition GetDefinition(string operationId)
    {
        if (!Package01OperationRegistry.TryGet(operationId, out var definition) ||
            definition is null ||
            !string.Equals(definition.TemplateKind, "queryexpression", StringComparison.Ordinal) ||
            definition.ResponseKind != OperationResponseKind.MemberInfoPresentRecordReadRecords ||
            definition.MaximumPageCount != 1 ||
            definition.Parameters.Count != 1)
        {
            throw new InvalidOperationException("The Data8 present-record operation definition is invalid.");
        }

        return definition;
    }

    /// <summary>
    /// 驗證唯一允許的參數集合恰為 <c>{ contactId: Guid }</c>。這裡不做 string、JSON、lookup 或 caller object 的
    /// 寬鬆轉換，因為正常化是 executor 的唯一責任；重複驗證防止替身或未來 transport 繞過 executor 後取得 query authority。
    /// </summary>
    /// <param name="parameters">executor 建立的參數字典；不會被此方法保存或修改。</param>
    /// <returns>可安全置入固定 lookup condition 的非空 contact GUID。</returns>
    private static Guid ReadExactContactId(IReadOnlyDictionary<string, object?> parameters)
    {
        if (parameters is null ||
            parameters.Count != 1 ||
            !parameters.TryGetValue("contactId", out var value) ||
            value is not Guid contactId ||
            contactId == Guid.Empty)
        {
            throw new InvalidOperationException("The Data8 present-record operation parameters are invalid.");
        }

        return contactId;
    }

    /// <summary>
    /// 建立不可由呼叫端改寫的單頁 QueryExpression：五個出席欄位、唯一 contact lookup 相等條件、固定 inner contact
    /// link 的 <c>fullname</c> alias 與 Sunday date descending。fullname 必須在同一 CRM request 投影，禁止第二次 SDK
    /// lookup；沒有 TopCount、cookie、續頁、第二排序、動態欄位或任意條件，故所有資料列數行為可審計。
    /// </summary>
    /// <param name="contactId">已由 server authorization 與 executor schema 驗證的目標 contact GUID。</param>
    /// <returns>只供本 connector request scope 使用的固定 query。</returns>
    private static QueryExpression CreateFixedQuery(Guid contactId)
    {
        var query = new QueryExpression(PresentRecordEntityName)
        {
            ColumnSet = new ColumnSet(
                PresentRecordIdAttribute,
                SundayPresentAttribute,
                SmallGroupPresentAttribute,
                ExplanationAttribute,
                SundayDateAttribute),
            Criteria = new FilterExpression(LogicalOperator.And),
            PageInfo = new PagingInfo
            {
                Count = MaximumRowsInFixedPage,
                PageNumber = 1
            }
        };
        query.Criteria.AddCondition(ContactLookupAttribute, ConditionOperator.Equal, contactId);
        query.AddOrder(SundayDateAttribute, OrderType.Descending);
        AddRequiredContactFullNameLink(query);
        return query;
    }

    /// <summary>
    /// 將 legacy 每列必須具備的 contact fullname 併入同一份固定 QueryExpression。inner join 同時保證找不到 contact 或
    /// fullname 投影時不會產生可被誤認為完整的出席列；alias 與欄位均為 connector 私有常數，不能由 browser locator、
    /// Profile 或呼叫端參數改寫。此方法只建立 request-local SDK query，沒有建立、保存或釋放第二個 service。
    /// </summary>
    /// <param name="query">目前呼叫建立、尚未交給 <c>IOrganizationService</c> 的固定出席查詢。</param>
    private static void AddRequiredContactFullNameLink(QueryExpression query)
    {
        var contact = query.AddLink(
            ContactEntityName,
            ContactLookupAttribute,
            ContactIdAttribute,
            JoinOperator.Inner);
        contact.EntityAlias = ContactAlias;
        contact.Columns = new ColumnSet(ContactFullNameAttribute);
    }

    /// <summary>
    /// 將單列 CRM Entity 投影為 immutable response scalar。任何 logical name、identity、型別或文字限制不符都會拋出，
    /// 呼叫端因此不會把部分 Entity、lookup、formatted value 或不受信任的 ToString 結果發布到產品層。
    /// </summary>
    /// <param name="entity">目前固定頁中的 CRM Entity；只在本方法內存取其 SDK attributes。</param>
    /// <returns>不含 SDK object、contact lookup、Profile、session 或 transport state 的純量列。</returns>
    private static MemberInfoPresentRecordReadRecord ProjectRecord(Entity entity)
    {
        if (entity is null ||
            !string.Equals(entity.LogicalName, PresentRecordEntityName, StringComparison.Ordinal) ||
            entity.Id == Guid.Empty)
        {
            throw new InvalidOperationException("The Data8 present-record entity identity is invalid.");
        }

        if (entity.Attributes.TryGetValue(PresentRecordIdAttribute, out var projectedId) &&
            projectedId is not null &&
            (projectedId is not Guid attributeId || attributeId != entity.Id))
        {
            throw new InvalidOperationException("The Data8 present-record primary identifier is invalid.");
        }

        return new MemberInfoPresentRecordReadRecord
        {
            PresentRecordId = entity.Id,
            // fullname 必須由同一固定 inner link 投影；禁止第二次 contact Retrieve，避免擴大個資讀取或 Entity 生命週期。
            // alias 缺失、null、型別或大小不符都使整頁失敗，絕不以 null 悄悄降級 legacy 每列 FullName 契約。
            ContactFullName = ReadRequiredAliasedBoundedString(
                entity,
                ContactAlias + "." + ContactFullNameAttribute),
            SundayDate = ReadLegacySundayDate(entity),
            Sunday = ReadLegacyPresenceFlag(entity, SundayPresentAttribute),
            SmallGroup = ReadLegacyPresenceFlag(entity, SmallGroupPresentAttribute),
            PrayItem = ReadBoundedOptionalString(entity, ExplanationAttribute)
        };
    }

    /// <summary>
    /// 保留 legacy Sunday-date 顯示語意：欄位缺失、null 或年份小於等於 1 皆發布 null；其餘值原樣複製，絕不呼叫
    /// <c>ToUniversalTime</c>、<c>SpecifyKind</c> 或讀取本機時區。這避免在未證明欄位時區語意前改寫使用者既有畫面日期。
    /// </summary>
    /// <param name="entity">目前列的 CRM Entity。</param>
    /// <returns>legacy-compatible nullable DateTime scalar。</returns>
    private static DateTime? ReadLegacySundayDate(Entity entity)
    {
        if (!entity.Attributes.TryGetValue(SundayDateAttribute, out var value) || value is null)
        {
            return null;
        }

        if (value is not DateTime dateTime)
        {
            throw new InvalidOperationException("The Data8 present-record Sunday date type is invalid.");
        }

        return dateTime.Year <= 1 ? null : dateTime;
    }

    /// <summary>
    /// 保留 legacy <c>GetEntityIntAttribute(...) &gt; 0</c> 旗標語意：欄位缺失/null 視為 false，任何 int（包含負數）
    /// 依大於零判定；但 OptionSetValue、bool、string、long 等不適合的 SDK 型別一律拒絕，避免隱式轉換掩蓋 schema drift。
    /// </summary>
    /// <param name="entity">目前列的 CRM Entity。</param>
    /// <param name="attributeName">固定允許的 attendance integer attribute。</param>
    /// <returns>與既有畫面一致的 closed boolean。</returns>
    private static bool ReadLegacyPresenceFlag(Entity entity, string attributeName)
    {
        if (!entity.Attributes.TryGetValue(attributeName, out var value) || value is null)
        {
            return false;
        }

        return value is int integerValue
            ? integerValue > 0
            : throw new InvalidOperationException("The Data8 present-record attendance flag type is invalid.");
    }

    /// <summary>
    /// 讀取可選說明文字且限制 UTF-16 字元數與嚴格 UTF-8 byte 數。不可用 ToString fallback，因為錯誤型別可能攜帶
    /// SDK metadata 或其他物件圖；文字超限、無效 surrogate 或非 string 都使整個固定頁失敗，而非截斷個資後發布。
    /// </summary>
    /// <param name="entity">目前列的 CRM Entity。</param>
    /// <param name="attributeName">固定允許的文字欄位名稱。</param>
    /// <returns>通過大小與編碼檢查的 nullable pure string。</returns>
    private static string? ReadBoundedOptionalString(Entity entity, string attributeName)
    {
        if (!entity.Attributes.TryGetValue(attributeName, out var value) || value is null)
        {
            return null;
        }

        if (value is not string text || text.Length > MaximumTextCharacters)
        {
            throw new InvalidOperationException("The Data8 present-record explanation is invalid.");
        }

        try
        {
            if (StrictUtf8.GetByteCount(text) > MaximumTextBytes)
            {
                throw new InvalidOperationException("The Data8 present-record explanation exceeds its limit.");
            }
        }
        catch (EncoderFallbackException exception)
        {
            throw new InvalidOperationException("The Data8 present-record explanation encoding is invalid.", exception);
        }

        return text;
    }

    /// <summary>
    /// 讀取固定 inner contact link 的必要 fullname alias。CRM linked column 必須以單層 <see cref="AliasedValue"/>
    /// 攜帶純 <see cref="string"/>；缺欄、null、巢狀 alias、空白、錯誤型別、超出字元或嚴格 UTF-8 byte 上限一律
    /// fail closed。這保證 legacy <c>ContactPresentRecordRow.FullName</c> 每列有值，且不需要 controller 或產品層
    /// 重新使用 CRM SDK 查詢 contact。
    /// </summary>
    /// <param name="entity">目前固定頁的出席紀錄 Entity；alias 只在此同步投影範圍內讀取。</param>
    /// <param name="alias">固定 <c>presentcontact.fullname</c> attribute key。</param>
    /// <returns>通過型別、非空與長度驗證的 fullname 純量。</returns>
    private static string ReadRequiredAliasedBoundedString(Entity entity, string alias)
    {
        if (!entity.Attributes.TryGetValue(alias, out var value) ||
            value is not AliasedValue { Value: string text } ||
            string.IsNullOrWhiteSpace(text) ||
            text.Length > MaximumTextCharacters)
        {
            throw new InvalidOperationException("The Data8 present-record contact fullname alias is invalid.");
        }

        try
        {
            if (StrictUtf8.GetByteCount(text) > MaximumTextBytes)
            {
                throw new InvalidOperationException("The Data8 present-record contact fullname exceeds its limit.");
            }
        }
        catch (EncoderFallbackException exception)
        {
            throw new InvalidOperationException("The Data8 present-record contact fullname encoding is invalid.", exception);
        }

        return text;
    }

    /// <summary>
    /// 以 checked arithmetic 對此次固定頁的純量輸出累積保守大小。固定成本包含 GUID、日期、兩個旗標與 JSON
    /// 結構成本；只有已通過嚴格 UTF-8 檢查的 fullname 與說明會加入。計數器局部於本呼叫，不能跨使用者、Profile 或 request 保留。
    /// </summary>
    /// <param name="totalBytes">目前 request-local response budget。</param>
    /// <param name="record">已完成 schema 投影的純量列。</param>
    /// <param name="maximumBytes">registry 宣告的單頁上限。</param>
    /// <returns>累積值不溢位且未超限時為 true。</returns>
    private static bool TryAddRecordBytes(
        ref int totalBytes,
        MemberInfoPresentRecordReadRecord record,
        int maximumBytes)
    {
        if (!TryAddBytes(ref totalBytes, FixedRecordBudgetBytes, maximumBytes) ||
            !TryAddRequiredTextBytes(ref totalBytes, record.ContactFullName, maximumBytes))
        {
            return false;
        }

        if (record.PrayItem is null)
        {
            return true;
        }

        try
        {
            return TryAddBytes(ref totalBytes, StrictUtf8.GetByteCount(record.PrayItem), maximumBytes);
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// 將必要 fullname 加入目前單頁的嚴格 UTF-8 預算。此防禦性重驗證讓未來變更即使錯過 alias reader，也不能將
    /// null、空白或超限名稱放進 response；計數器只在當次呼叫存活，不會跨 request、Profile 或使用者累積。
    /// </summary>
    /// <param name="totalBytes">目前 request-local 頁面 byte 計數。</param>
    /// <param name="value">每列必要的 fullname 純量。</param>
    /// <param name="maximumBytes">registry 定義的單頁上限。</param>
    /// <returns>名稱合法且累積值仍受限時為 true。</returns>
    private static bool TryAddRequiredTextBytes(ref int totalBytes, string? value, int maximumBytes)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumTextCharacters)
        {
            return false;
        }

        try
        {
            var valueBytes = StrictUtf8.GetByteCount(value);
            return valueBytes <= MaximumTextBytes && TryAddBytes(ref totalBytes, valueBytes, maximumBytes);
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// 使用 checked 加法更新局部 response budget；overflow 與超限都回傳 false 給呼叫端的 fail-closed 路徑。
    /// </summary>
    /// <param name="totalBytes">目前局部 byte count。</param>
    /// <param name="additionalBytes">要加入的已驗證純量成本。</param>
    /// <param name="maximumBytes">不可跨越的 registry 上限。</param>
    /// <returns>新總量有效時為 true。</returns>
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
}

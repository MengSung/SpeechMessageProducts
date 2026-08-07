// ============================================================================
// 檔案：SpeechMessage.Dynamics.Connectors.Data8/Package01Data8ReadOperations.cs
// 用途：在 Data8 Connector 內建立 Package01 唯讀能力的 server-owned CRM 查詢與安全 DTO 投影。
//
// 信任、隔離與生命週期契約：
// 1. 本檔案只接收 executor 已正規化的 ConnectorOperation；不接受 caller FetchXML、QueryBase、Entity、端點、
//    credential、connector 或 CE 版本選擇。每個 QueryExpression 都在單次同步呼叫 stack 建立，絕不放入 static cache。
// 2. IOrganizationService 由 OnPremiseData8ConnectorClient 唯一擁有，Pool/Lease 決定其 Dispose 與 fault eviction；
//    本檔案只在 Execute 期間暫借 service，不能 Dispose、快取、平行使用或將 EntityCollection 帶出方法範圍。
// 3. CRM Entity、Money、OptionSetValue、FormattedValues、paging cookie 與 QueryExpression 都在此邊界完成投影；
//    對外只回傳 bounded OperationResponseData。任一頁、列、欄位型別、cookie 或大小違規都 fail closed，不回傳 partial data。
// ============================================================================

using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Connectors.Data8;

/// <summary>
/// Package01 Data8 唯讀操作的內部唯一 owner。它將 immutable registry contract 翻譯為固定 QueryExpression，
/// 並在 Data8 client 的同步 request scope 內把 CRM SDK 型別投影為共享安全 wire records。類別只有常數與純方法，
/// 不保存 Profile、Organization、credential、service、query、page、Entity、cookie、結果或使用者狀態，因此不同
/// Pool generation／Profile／Organization 不可能透過本類別共享 mutable session 或資料。
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
    private const int MaximumRowsPerPage = 128;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>
    /// 執行已 allowlist 的 Package01 operation。這個 switch 是 Data8 端唯一 capability dispatch table；目前僅
    /// 實作依 contact 讀取 dedication fee，其他 P7.1 operation 在各自具備固定 query、projection 與測試前
    /// 必須拒絕，不能退回 generic CRM Execute、FetchXML 或另一種 Connector。service 的生命週期不在此方法，
    /// caller 若遇例外會由 Lease 標記 faulted 並確定 Dispose client。
    /// </summary>
    /// <param name="service">由目前 Pool generation 唯一擁有且只在本次 lease 同步借用的 Data8 service。</param>
    /// <param name="operation">已被 executor 複製/正規化的固定 capability operation。</param>
    /// <param name="ceVersion">由 immutable resolved Profile 決定的公開 CE version 字串。</param>
    /// <returns>僅含 Package01 安全 fee/stor branch 的封閉 response envelope。</returns>
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
            OperationIds.FeeDedicationRetrieveByContact => ExecuteFeeDedicationByContact(
                service,
                operation,
                ceVersion),
            _ => throw new InvalidOperationException("The Data8 Package01 operation is not permitted.")
        };
    }

    /// <summary>
    /// 依固定 contact GUID 讀取 dedication fee。查詢的 entity、columns、filter、ordering 與 paging 都由程式碼
    /// 獨佔；optional legacy contactName 已在 executor 驗證後丟棄，不能影響 CRM 語意。每頁最多 128 列、最多
    /// registry 宣告的四頁，並在每一列投影後累積保守 byte budget，避免同步 SDK 回傳使 lease 記憶體無界成長。
    /// </summary>
    private static OperationResponseData ExecuteFeeDedicationByContact(
        IOrganizationService service,
        ConnectorOperation operation,
        string ceVersion)
    {
        var definition = GetDefinition(operation.OperationId, OperationResponseKind.Package01FeeRecords);
        var contactId = ReadRequiredGuid(operation.Parameters, "contactId");
        var query = CreateFeeDedicationByContactQuery(contactId);
        var records = RetrieveFeeRecords(service, query, definition);
        return OperationResponseData.ForPackage01FeeRecords(operation.OperationId, ceVersion, records);
    }

    /// <summary>
    /// 建立唯一合法的 dedication-fee-by-contact QueryExpression。使用 QueryExpression 只是 Connector 內部的
    /// server-owned SDK implementation detail；它不會穿越到 ProductClient/Gateway，也不提供呼叫端指定 entity、
    /// field、order、status、paging 或 FetchXML 的入口。第二排序鍵使多頁排序穩定，避免同名 fee 造成重複/遺漏。
    /// </summary>
    private static QueryExpression CreateFeeDedicationByContactQuery(Guid contactId)
    {
        var query = new QueryExpression(FeeEntityName)
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
            PageInfo = new PagingInfo
            {
                Count = MaximumRowsPerPage,
                PageNumber = 1,
                PagingCookie = null,
                ReturnTotalRecordCount = false
            }
        };
        query.Criteria.AddCondition(FeeContactAttribute, ConditionOperator.Equal, contactId);
        query.Criteria.AddCondition(FeeCategoryAttribute, ConditionOperator.NotNull);
        query.AddOrder(FeeNameAttribute, OrderType.Ascending);
        query.AddOrder(FeeIdAttribute, OrderType.Ascending);
        return query;
    }

    /// <summary>
    /// 逐頁同步取回固定 fee query，並在每頁離開前完成 SDK-free projection。Data8 IOrganizationService 不提供
    /// 可安全取消的非同步 RetrieveMultiple，因此本方法不建立 Task.Run 或背景重試；外層 Lease 的 deadline/cancel
    /// 會在同步呼叫返回時決定 client 是否 faulted。MoreRecords 沒有 cookie、超過頁數/列數或 byte budget 時立即
    /// 丟出受控例外，禁止回傳前幾頁 partial success。
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
                if (!TryAddFeeRecordBytes(ref totalBytes, record, definition.MaximumCumulativeResponseBytes))
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
    /// 將單筆 CRM fee Entity 投影成封閉 record。Entity logical name、primary id、attribute CLR types 與 formatted
    /// label 都逐一驗證；不合法資料一律讓整個 lease 失敗，不能略過壞列、保留 raw Entity 或混合舊頁的部分結果。
    /// 所有回傳值都是 value/string，沒有 SDK object、lookup graph、formatted dictionary 或 CRM session reference。
    /// </summary>
    private static Package01FeeRecord ProjectFeeRecord(Entity entity)
    {
        if (entity is null ||
            !string.Equals(entity.LogicalName, FeeEntityName, StringComparison.Ordinal) ||
            entity.Id == Guid.Empty)
        {
            throw new InvalidOperationException("The Data8 fee entity is invalid.");
        }

        ValidatePrimaryId(entity, FeeIdAttribute);
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
    /// 讀取 operation 已複製的必要 Guid scalar。connector 仍再驗證一次，不信任直接單元測試、未來 adapter 或
    /// 任意 IConnectorClient 呼叫者繞過 executor；缺失/錯型別/空值都不能形成 CRM filter。
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
    /// 取得並驗證 operation registry definition 與預期 response branch。這保證內部 switch 即使未來被錯誤修改，
    /// 也不能把一個 fee query 假裝成 stor response；registry 是 page/row/byte policy 的唯一來源，不在 connector
    /// 另行複製上限。
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
    /// 驗證 CRM primary id attribute 若存在必須是與 Entity.Id 相同的 Guid。CRM 有時不 materialize primary-id
    /// attribute，但 Entity.Id 仍是必要值；若 attribute 存在卻與 entity id 不同，代表資料/adapter 不可信，不能
    /// 讓資料列進入產品端或 idle client 的下一次 lease。
    /// </summary>
    private static void ValidatePrimaryId(Entity entity, string attributeName)
    {
        if (!entity.Attributes.TryGetValue(attributeName, out var value))
        {
            return;
        }

        if (value is not Guid id || id == Guid.Empty || id != entity.Id)
        {
            throw new InvalidOperationException("The Data8 entity primary id is invalid.");
        }
    }

    /// <summary>
    /// 讀取 nullable CRM attribute 並驗證其 exact CLR type。attribute 缺失代表 nullable wire value；存在但型別
    /// 不符則拒絕，避免 Convert/ToString 將 Money、OptionSet、EntityReference 或任意 SDK object 靜默投影成錯誤資料。
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
    /// 讀取 nullable DateTime，並以與 executor 相同的 deterministic UTC 規則轉為 DTO。CRM 回傳 Unspecified 時
    /// 明確視為 UTC，避免執行 Gateway 的 Windows 時區改變報表資料；其他 non-DateTime 型別直接 fail closed。
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
    /// 讀取 nullable plain string attribute；存在但非 string 的 CRM 值不做 ToString fallback，避免把 SDK/lookup
    /// 物件或文化相依格式穿越產品邊界。回傳字串之後仍由 per-row UTF-8 budget 檢查，沒有長生命週期 cache。
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
    /// 讀取 nullable CRM formatted-value label。FormattedValues 是目前 Entity 的短生命週期 dictionary；方法只複製
    /// string reference 到即將建立的 DTO，沒有將整個 dictionary、metadata 或 option-set definition 帶出 connector。
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
    /// 以保守 256 bytes structural overhead 加上所有 fee display strings 的嚴格 UTF-8 bytes 更新目前結果預算。
    /// 每筆加入後立即比較 registry maximum，讓 caller 可在繼續讀下一列/下一頁前停止；任何編碼或整數錯誤均拒絕。
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
    /// 安全累積固定 DTO overhead。checked 防止 overflow 繞過上限，這比容許一個理論上永遠不會出現的大回應更重要，
    /// 因為 Connector 必須在所有 CE/transport 錯誤情境維持 bounded request memory。
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
    /// 安全累積 nullable display string 的嚴格 UTF-8 bytes。invalid UTF-16 不會被 replacement character 靜默接受，
    /// 因為那會使 byte budget 與實際序列化資料不同；字串為 null 時不額外配置或計數。
    /// </summary>
    private static bool TryAddStringBytes(ref int totalBytes, string? value, int maximumBytes)
    {
        if (value is null)
        {
            return true;
        }

        try
        {
            totalBytes = checked(totalBytes + StrictUtf8.GetByteCount(value));
            return totalBytes <= maximumBytes;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}

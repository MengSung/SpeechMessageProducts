using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.WorkerHost;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.Crm82Worker;

/// <summary>
/// 執行 CE 8.2 Package01 fee 的單一固定 QueryExpression operation。
/// 每次呼叫建立自己的 query、paging state、page/row collection，並以同步 SDK 呼叫依序取頁；
/// 型別不含 static mutable cache、parallel paging、Task.Run、Session 或跨 request Entity retention。
/// 所有 page 完整通過 page/row/type 上限後才可能回傳 SDK-free <see cref="WorkerValue"/>。
/// </summary>
internal static class Package01FeeQueryOperation
{
    private const string EntityLogicalName = "new_fee";
    private const string FeeIdAttribute = "new_feeid";
    private const string NameAttribute = "new_name";
    private const string CreatedOnAttribute = "createdon";
    private const string PayDateAttribute = "new_pay_date";
    private const string AmountAttribute = "new_fee_really_paid";
    private const string PayWayAttribute = "new_pay_way";
    private const string CategoryAttribute = "new_category";
    private const string OthersAttribute = "new_others";
    private const string PaidPeriodAttribute = "new_paid_period";

    /// <summary>
    /// 驗證並正規化 request，建立固定查詢，依 paging cookie 同步取最多四頁，
    /// 再將每個 Entity 投影成十欄 SDK-free row。任何 page、total 或 SDK attribute 違規都
    /// fail closed；方法不截斷、不回傳 partial success，也不把 mutable query 保存到呼叫外。
    /// </summary>
    /// <param name="client">本 worker generation 唯一擁有的同步 CE 8.2 SDK client。</param>
    /// <param name="request">Package01 typed request；contactName 只會被驗證後丟棄。</param>
    /// <returns>Array&lt;Page&lt;Row[10]&gt;&gt; 的 SDK-free WorkerValue。</returns>
    internal static WorkerValue Execute(ICrm82SdkClient client, WorkerRequestV1 request)
    {
        if (client is null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        var normalizedRequest = Package01FeeWorkerContract.ValidateAndNormalizeRequest(
            request ?? throw new ArgumentNullException(nameof(request)));
        var contactId = ReadRequiredGuid(normalizedRequest.Parameters["contactId"]);
        var startDate = ReadRequiredUtcDateTime(normalizedRequest.Parameters["startDate"]);
        var endDate = ReadRequiredUtcDateTime(normalizedRequest.Parameters["endDate"]);
        var query = BuildQuery(contactId, startDate, endDate);
        var pages = new List<WorkerValue>(Package01FeeWorkerContract.MaximumPageCount);
        var totalRows = 0;
        string? pagingCookie = null;

        for (var pageNumber = 1;
             pageNumber <= Package01FeeWorkerContract.MaximumPageCount;
             pageNumber++)
        {
            query.PageInfo.PageNumber = pageNumber;
            query.PageInfo.PagingCookie = pagingCookie;
            var page = client.RetrieveMultiple(query) ??
                throw new InvalidOperationException(
                    "The CE 8.2 Package01 fee query returned no page.");
            var rowCount = page.Entities.Count;
            var nextTotalRows = checked(totalRows + rowCount);
            if (nextTotalRows > Package01FeeWorkerContract.MaximumTotalRows)
            {
                throw new OfficialWorkerResultLimitExceededException();
            }

            if (rowCount > Package01FeeWorkerContract.MaximumRowsPerPage)
            {
                throw new OfficialWorkerResultLimitExceededException();
            }

            pages.Add(ProjectPage(page.Entities));
            totalRows = nextTotalRows;
            var boundedResult = WorkerValue.FromArray(pages);
            // 每頁投影後立即驗證 canonical bytes，避免已知超限的結果繼續取得並累積後續頁面。
            OfficialWorkerOperations.ValidateResult(
                Package01FeeWorkerContract.CapabilityOperationId,
                boundedResult);
            if (!page.MoreRecords)
            {
                return boundedResult;
            }

            if (pageNumber == Package01FeeWorkerContract.MaximumPageCount)
            {
                // 第四頁仍有資料代表完整結果超過固定 contract；不得發第五次 CRM 呼叫，
                // 也不得回傳目前方法區域內已投影的任何前置 page。
                throw new OfficialWorkerResultLimitExceededException();
            }

            if (string.IsNullOrWhiteSpace(page.PagingCookie))
            {
                // CE server 必須提供下一頁 cookie；只遞增 page number 可能造成重複或漏資料，
                // 因此此 upstream paging shape 一律 fail closed，不猜測替代狀態。
                throw new InvalidOperationException(
                    "The CE 8.2 Package01 fee paging cookie is unavailable.");
            }

            pagingCookie = page.PagingCookie;
        }

        throw new OfficialWorkerResultLimitExceededException();
    }

    /// <summary>
    /// 建立固定 new_fee query；所有欄位、條件、status 與排序 token 都由 worker 擁有，
    /// caller 只能提供已驗證的 contact GUID 與 UTC 日期界線。
    /// </summary>
    /// <param name="contactId">必須為非空、由 typed request 驗證過的 contact ID。</param>
    /// <param name="startDate">Kind=Utc 的包含式起始日期界線。</param>
    /// <param name="endDate">Kind=Utc 的包含式結束日期界線。</param>
    /// <returns>本次 operation 區域擁有、含固定 paging 與 deterministic order 的 query。</returns>
    private static QueryExpression BuildQuery(
        Guid contactId,
        DateTime startDate,
        DateTime endDate)
    {
        var query = new QueryExpression(EntityLogicalName)
        {
            ColumnSet = new ColumnSet(
                FeeIdAttribute,
                NameAttribute,
                CreatedOnAttribute,
                PayDateAttribute,
                AmountAttribute,
                PayWayAttribute,
                CategoryAttribute,
                OthersAttribute,
                PaidPeriodAttribute),
            Criteria = new FilterExpression(LogicalOperator.And),
            PageInfo = new PagingInfo
            {
                Count = Package01FeeWorkerContract.MaximumRowsPerPage,
                PageNumber = 1,
                PagingCookie = null,
                ReturnTotalRecordCount = false
            }
        };
        query.Criteria.AddCondition(
            "new_contact_new_fee",
            ConditionOperator.Equal,
            contactId);
        query.Criteria.AddCondition(CategoryAttribute, ConditionOperator.NotNull);
        query.Criteria.AddCondition(
            "new_pay_status",
            ConditionOperator.In,
            new object[]
            {
                100000001,
                100000002,
                100000003,
                100000004,
                100000006
            });
        query.Criteria.AddCondition(
            PayDateAttribute,
            ConditionOperator.OnOrAfter,
            startDate);
        query.Criteria.AddCondition(
            PayDateAttribute,
            ConditionOperator.OnOrBefore,
            endDate);
        query.AddOrder(NameAttribute, OrderType.Ascending);
        query.AddOrder(FeeIdAttribute, OrderType.Ascending);
        return query;
    }

    /// <summary>
    /// 將單一 SDK page 完整投影成新的 WorkerValue array；page 返回後不保存 EntityCollection。
    /// </summary>
    /// <param name="entities">已通過 page/total count gate 的本頁 Entity snapshot。</param>
    /// <returns>同列數、同順序的 SDK-free row array。</returns>
    private static WorkerValue ProjectPage(IReadOnlyCollection<Entity> entities)
    {
        var rows = new List<WorkerValue>(entities.Count);
        foreach (var entity in entities)
        {
            rows.Add(ProjectRow(entity));
        }

        return WorkerValue.FromArray(rows);
    }

    /// <summary>
    /// 以固定十欄順序投影一筆 new_fee。每個已知 attribute 都先驗證精確 SDK type；
    /// amount 缺失為 0m，其餘缺失值為 Null，任何錯誤型別都使整個 operation fail closed。
    /// </summary>
    /// <param name="entity">RetrieveMultiple 回傳且應為 new_fee 的單一 Entity。</param>
    /// <returns>固定十欄的 SDK-free row。</returns>
    private static WorkerValue ProjectRow(Entity entity)
    {
        if (entity is null ||
            !string.Equals(entity.LogicalName, EntityLogicalName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The CE 8.2 Package01 fee entity is invalid.");
        }

        ValidateFeeIdAttribute(entity);
        _ = ReadNullableOptionSet(entity, CategoryAttribute);
        return WorkerValue.FromArray(new WorkerValue[]
        {
            entity.Id == Guid.Empty ? WorkerValue.Null() : WorkerValue.FromGuid(entity.Id),
            ReadNullableDateTime(entity, CreatedOnAttribute),
            ReadNullableDateTime(entity, PayDateAttribute),
            ReadAmount(entity),
            ReadNullableOptionSet(entity, PayWayAttribute),
            ReadFormattedValue(entity, PayWayAttribute),
            ReadFormattedValue(entity, CategoryAttribute),
            ReadNullableString(entity, OthersAttribute),
            ReadNullableString(entity, PaidPeriodAttribute),
            ReadNullableString(entity, NameAttribute)
        });
    }

    /// <summary>從 normalized WorkerValue 讀取非空 GUID；不接受其他 scalar shape。</summary>
    /// <param name="value">contactId 的 normalized WorkerValue。</param>
    /// <returns>非空 contact GUID。</returns>
    private static Guid ReadRequiredGuid(WorkerValue value)
    {
        if (value.Kind != WorkerValueKind.Guid ||
            !Guid.TryParseExact(value.Scalar, "N", out var parsed) ||
            parsed == Guid.Empty)
        {
            throw new InvalidOperationException(
                "The CE 8.2 Package01 fee contact identifier is invalid.");
        }

        return parsed;
    }

    /// <summary>
    /// 從 normalized WorkerValue 讀取 UTC ticks 並建立 Kind=Utc 的 DateTime，
    /// 避免 worker machine local timezone 在 QueryExpression 建立時隱式改寫輸入。
    /// </summary>
    /// <param name="value">startDate 或 endDate 的 normalized WorkerValue。</param>
    /// <returns>Kind=Utc 且 ticks 完全相同的 DateTime。</returns>
    private static DateTime ReadRequiredUtcDateTime(WorkerValue value)
    {
        if (value.Kind != WorkerValueKind.UtcDateTime ||
            !long.TryParse(
                value.Scalar,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var ticks) ||
            ticks < DateTime.MinValue.Ticks ||
            ticks > DateTime.MaxValue.Ticks)
        {
            throw new InvalidOperationException(
                "The CE 8.2 Package01 fee date boundary is invalid.");
        }

        return new DateTime(ticks, DateTimeKind.Utc);
    }

    /// <summary>
    /// 投影 nullable CRM DateTime。Utc 保持不變、Local 明確轉 UTC；Unspecified 依 CRM SDK
    /// Organization response 慣例視為 UTC，避免套用 worker 主機時區。真機仍須驗證欄位 Behavior。
    /// </summary>
    /// <param name="entity">目前投影的 fee Entity。</param>
    /// <param name="attributeName">createdon 或 new_pay_date logical name。</param>
    /// <returns>缺失時為 Null，存在時為 canonical UTC ticks。</returns>
    private static WorkerValue ReadNullableDateTime(Entity entity, string attributeName)
    {
        if (!TryReadAttribute(entity, attributeName, out var value))
        {
            return WorkerValue.Null();
        }

        if (value is not DateTime dateTime)
        {
            throw InvalidAttributeType(attributeName);
        }

        var utcDateTime = dateTime.Kind switch
        {
            DateTimeKind.Utc => dateTime,
            DateTimeKind.Local => dateTime.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc),
            _ => throw InvalidAttributeType(attributeName)
        };
        return WorkerValue.FromUtcDateTime(new DateTimeOffset(utcDateTime));
    }

    /// <summary>投影 Money.Value；attribute 缺失或 null 時依 contract 回傳 0m。</summary>
    /// <param name="entity">目前投影的 fee Entity。</param>
    /// <returns>永不為 Null 的 Decimal WorkerValue。</returns>
    private static WorkerValue ReadAmount(Entity entity)
    {
        if (!TryReadAttribute(entity, AmountAttribute, out var value))
        {
            return WorkerValue.FromDecimal(0m);
        }

        if (value is not Money money)
        {
            throw InvalidAttributeType(AmountAttribute);
        }

        return WorkerValue.FromDecimal(money.Value);
    }

    /// <summary>
    /// 驗證 SDK 若同時回傳 primary-id attribute，其型別與值必須和 <see cref="Entity.Id"/> 一致；
    /// row 仍只從 Entity.Id 投影，避免兩個互相矛盾的 ID 來源被靜默接受。
    /// </summary>
    /// <param name="entity">目前投影的 fee Entity。</param>
    private static void ValidateFeeIdAttribute(Entity entity)
    {
        if (!TryReadAttribute(entity, FeeIdAttribute, out var value))
        {
            return;
        }

        if (value is not Guid feeId || feeId != entity.Id)
        {
            throw InvalidAttributeType(FeeIdAttribute);
        }
    }

    /// <summary>投影 nullable OptionSetValue.Value；存在但型別錯誤時 fail closed。</summary>
    /// <param name="entity">目前投影的 fee Entity。</param>
    /// <param name="attributeName">new_pay_way 或 new_category logical name。</param>
    /// <returns>缺失時為 Null，存在時為 Int64 WorkerValue。</returns>
    private static WorkerValue ReadNullableOptionSet(Entity entity, string attributeName)
    {
        if (!TryReadAttribute(entity, attributeName, out var value))
        {
            return WorkerValue.Null();
        }

        if (value is not OptionSetValue option)
        {
            throw InvalidAttributeType(attributeName);
        }

        return WorkerValue.FromInt64(option.Value);
    }

    /// <summary>只讀取 SDK FormattedValues 的固定顯示字串；缺失時回傳 Null。</summary>
    /// <param name="entity">目前投影的 fee Entity。</param>
    /// <param name="attributeName">new_pay_way 或 new_category logical name。</param>
    /// <returns>顯示文字或 Null，不回傳 raw OptionSet metadata。</returns>
    private static WorkerValue ReadFormattedValue(Entity entity, string attributeName)
    {
        return entity.FormattedValues.TryGetValue(attributeName, out var value) && value is not null
            ? WorkerValue.FromString(value)
            : WorkerValue.Null();
    }

    /// <summary>投影 nullable string；存在但不是 string 時拒絕整個結果。</summary>
    /// <param name="entity">目前投影的 fee Entity。</param>
    /// <param name="attributeName">固定 string 欄位 logical name。</param>
    /// <returns>原字串（包含空字串）或 Null。</returns>
    private static WorkerValue ReadNullableString(Entity entity, string attributeName)
    {
        if (!TryReadAttribute(entity, attributeName, out var value))
        {
            return WorkerValue.Null();
        }

        if (value is not string text)
        {
            throw InvalidAttributeType(attributeName);
        }

        return WorkerValue.FromString(text);
    }

    /// <summary>
    /// 以一次 dictionary lookup 區分缺失／null 與實際值；null 不被保留為 SDK object。
    /// </summary>
    /// <param name="entity">目前投影的 fee Entity。</param>
    /// <param name="attributeName">固定欄位 logical name。</param>
    /// <param name="value">存在且非 null 時回傳的 SDK value。</param>
    /// <returns>attribute 存在且非 null 時為 true。</returns>
    private static bool TryReadAttribute(
        Entity entity,
        string attributeName,
        out object value)
    {
        if (entity.Attributes.TryGetValue(attributeName, out var candidate) &&
            candidate is not null)
        {
            value = candidate;
            return true;
        }

        value = null!;
        return false;
    }

    /// <summary>建立不含 attribute value 或原始 SDK exception 的固定型別錯誤。</summary>
    /// <param name="attributeName">發生型別違規的固定 logical name。</param>
    /// <returns>只含 allowlisted attribute 名稱的 fail-closed exception。</returns>
    private static InvalidOperationException InvalidAttributeType(string attributeName)
    {
        return new InvalidOperationException(
            "The CE 8.2 Package01 fee attribute type is invalid: " + attributeName + ".");
    }
}

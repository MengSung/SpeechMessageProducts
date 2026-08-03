using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.WorkerHost;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.Crm91Worker;

/// <summary>
/// 執行 CE 9.1 Package01 fee 的單一固定 QueryExpression operation。
/// 每次呼叫建立自己的 query、paging cookie、page/row collection，並以同一 worker-generation client
/// 依序同步取頁；不使用 static mutable cache、parallel paging、Task.Run、Session 或跨 request Entity retention。
/// Entity、Money、OptionSetValue 等 SDK 型別都在 worker 內投影成固定十欄 <see cref="WorkerValue"/>，
/// caller 不能提供 FetchXML、entity/column/order/status token、endpoint 或 routing hint。
/// page、row、total row 與 canonical result bytes 任一超限即拒絕整體結果，不截斷也不回傳 partial success。
/// QueryExpression、PagingInfo、EntityCollection、Entity 與 attribute SDK object 不實作本 operation 擁有的
/// disposable handle；其 owner 是單次同步 call stack，只有有界 SDK-free projection 會加入結果集合。
/// 本型別不建立 stream、process、timer、cancellation registration、async callback 或 background task。
/// </summary>
internal static class Package01FeeQueryOperation
{
    private const string EntityName = "new_fee";

    /// <summary>
    /// 驗證並正規化 request，建立 server-owned query，再依 paging cookie 同步取得有限頁數。
    /// 此方法只在 worker session 的單一在途 operation 內重複使用 generation-owned client；
    /// 每頁最多 400 列、全部最多 1,600 列／4 頁，且每次累積後立即驗證 wire byte 上限。
    /// SDK 呼叫本身無法由 CancellationToken 中斷；deadline 超過時 Supervisor 會停止等待並依有限
    /// drain deadline 強制終止 worker process，確保 WCF、handle 與記憶體有最終 cleanup owner。
    /// 每次迴圈只暫時保留目前 EntityCollection；完成十欄投影後，跨迴圈保存的只有 paging cookie 與
    /// 有上限的 WorkerValue pages，不會讓 SDK Entity graph 隨 request 或 worker lifetime 累積。
    /// </summary>
    /// <param name="client">由 adapter 唯一擁有、此 operation 僅同步借用且不得 Dispose 的 CE 9.1 SDK client。</param>
    /// <param name="request">Package01 typed request；optional contactName 驗證後不進入 CRM query。</param>
    /// <returns>Array&lt;Page&lt;Row[10]&gt;&gt; 的完整 SDK-free WorkerValue。</returns>
    internal static WorkerValue Execute(
        ICrm91SdkClient client,
        WorkerRequestV1 request)
    {
        if (client is null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        var normalized = Package01FeeWorkerContract.ValidateAndNormalizeRequest(request);
        var query = CreateQuery(
            ReadGuid(normalized, "contactId"),
            ReadUtcDateTime(normalized, "startDate"),
            ReadUtcDateTime(normalized, "endDate"));
        var pages = new List<WorkerValue>(Package01FeeWorkerContract.MaximumPageCount);
        var totalRows = 0;
        string? pagingCookie = null;

        // 同一 worker message loop 僅允許一個在途 operation；逐頁同步呼叫可避免同一
        // CrmServiceClient 上的並行 SDK state，並讓 query／cookie／Entity retention 有明確方法範圍。
        for (var pageNumber = 1;
             pageNumber <= Package01FeeWorkerContract.MaximumPageCount;
             pageNumber++)
        {
            query.PageInfo.PageNumber = pageNumber;
            query.PageInfo.PagingCookie = pagingCookie;
            var page = client.RetrieveMultiple(query) ??
                throw InvalidSdkResult();

            totalRows = checked(totalRows + page.Entities.Count);
            if (totalRows > Package01FeeWorkerContract.MaximumTotalRows)
            {
                throw new OfficialWorkerResultLimitExceededException();
            }

            if (page.Entities.Count > Package01FeeWorkerContract.MaximumRowsPerPage)
            {
                throw new OfficialWorkerResultLimitExceededException();
            }

            // 只有列數通過 page／total gate 後才配置本頁投影陣列；容量由 shared contract 固定界定。
            var rows = new WorkerValue[page.Entities.Count];
            for (var index = 0; index < page.Entities.Count; index++)
            {
                rows[index] = ProjectRow(page.Entities[index]);
            }

            pages.Add(WorkerValue.FromArray(rows));
            var boundedResult = WorkerValue.FromArray(pages);
            // 每頁投影後立即驗證 cumulative canonical bytes，避免已知超限的結果繼續取得並保留後續頁面。
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
                // 也不得把目前方法區域內已投影的前置 page 當成成功結果回傳。
                throw new OfficialWorkerResultLimitExceededException();
            }

            if (string.IsNullOrWhiteSpace(page.PagingCookie))
            {
                // CE server 宣告仍有資料時必須提供 cookie；只猜下一個 page number 可能重複或漏列，
                // 因此 upstream paging shape 一律 fail closed，不合成替代 paging state。
                throw InvalidSdkResult();
            }

            pagingCookie = page.PagingCookie;
        }

        throw new OfficialWorkerResultLimitExceededException();
    }

    /// <summary>
    /// 建立本次呼叫唯一擁有的固定 new_fee query。欄位、條件、付款狀態、頁大小及 deterministic
    /// order 全由 worker source 擁有；caller 只能提供已由 shared contract 驗證的 contact GUID 與 UTC 日期界線。
    /// </summary>
    /// <param name="contactId">由 typed request 驗證過的非空 contact ID。</param>
    /// <param name="startDateUtc">Kind=Utc 的包含式起始日期界線。</param>
    /// <param name="endDateUtc">Kind=Utc 的包含式結束日期界線。</param>
    /// <returns>方法區域擁有、包含固定 projection、filter、paging 與排序的 QueryExpression。</returns>
    private static QueryExpression CreateQuery(
        Guid contactId,
        DateTime startDateUtc,
        DateTime endDateUtc)
    {
        var query = new QueryExpression(EntityName)
        {
            ColumnSet = new ColumnSet(
                "new_feeid",
                "new_name",
                "createdon",
                "new_pay_date",
                "new_fee_really_paid",
                "new_pay_way",
                "new_category",
                "new_others",
                "new_paid_period"),
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
        query.Criteria.AddCondition("new_category", ConditionOperator.NotNull);
        query.Criteria.AddCondition(
            "new_pay_status",
            ConditionOperator.In,
            100000001,
            100000002,
            100000003,
            100000004,
            100000006);
        query.Criteria.AddCondition(
            "new_pay_date",
            ConditionOperator.OnOrAfter,
            startDateUtc);
        query.Criteria.AddCondition(
            "new_pay_date",
            ConditionOperator.OnOrBefore,
            endDateUtc);
        query.AddOrder("new_name", OrderType.Ascending);
        query.AddOrder("new_feeid", OrderType.Ascending);
        return query;
    }

    /// <summary>
    /// 以固定十欄順序投影一筆 new_fee。每個已知 attribute 都先驗證精確 SDK type；
    /// amount 缺失為 0m，其餘 optional 值為 Null，任何 entity/type 不符都使整個 operation fail closed。
    /// SDK Entity 與其 attribute dictionary 不會離開此方法或被保存至跨 request collection。
    /// </summary>
    /// <param name="entity">RetrieveMultiple 回傳且應為 new_fee 的單一 Entity。</param>
    /// <returns>固定十欄且不含任何 SDK 型別的 row。</returns>
    private static WorkerValue ProjectRow(Entity entity)
    {
        if (entity is null ||
            !string.Equals(entity.LogicalName, EntityName, StringComparison.Ordinal))
        {
            throw InvalidSdkResult();
        }

        ValidateFeeIdAttribute(entity);
        var createdOn = ReadOptionalDateTime(entity, "createdon");
        var payDate = ReadOptionalDateTime(entity, "new_pay_date");
        var amount = ReadOptionalReference<Money>(entity, "new_fee_really_paid")?.Value ?? 0m;
        var payWay = ReadOptionalReference<OptionSetValue>(entity, "new_pay_way");
        _ = ReadOptionalReference<OptionSetValue>(entity, "new_category");

        return WorkerValue.FromArray(new WorkerValue[]
        {
            entity.Id == Guid.Empty ? WorkerValue.Null() : WorkerValue.FromGuid(entity.Id),
            ToWorkerDateTime(createdOn),
            ToWorkerDateTime(payDate),
            WorkerValue.FromDecimal(amount),
            payWay is null ? WorkerValue.Null() : WorkerValue.FromInt64(payWay.Value),
            ToWorkerString(ReadFormattedValue(entity, "new_pay_way")),
            ToWorkerString(ReadFormattedValue(entity, "new_category")),
            ToWorkerString(ReadOptionalString(entity, "new_others")),
            ToWorkerString(ReadOptionalString(entity, "new_paid_period")),
            ToWorkerString(ReadOptionalString(entity, "new_name"))
        });
    }

    /// <summary>
    /// 從已正規化 request 讀取 contactId；shared contract 已先驗證 Guid shape 與非空值，
    /// 此處只做 worker-local scalar 轉換，不保存原 request dictionary。
    /// </summary>
    /// <param name="request">已移除 optional contactName 的 execution request。</param>
    /// <param name="name">固定的 contactId parameter name。</param>
    /// <returns>非空 contact GUID。</returns>
    private static Guid ReadGuid(WorkerRequestV1 request, string name)
    {
        var value = request.Parameters[name];
        return Guid.ParseExact(value.Scalar!, "N");
    }

    /// <summary>
    /// 驗證 SDK 若同時回傳 primary-id attribute，其型別與值必須和 <see cref="Entity.Id"/> 一致；
    /// row 只從 Entity.Id 投影，避免兩個互相矛盾的 ID 來源被靜默接受。
    /// </summary>
    /// <param name="entity">目前投影的 fee Entity。</param>
    private static void ValidateFeeIdAttribute(Entity entity)
    {
        if (!entity.Attributes.TryGetValue("new_feeid", out var value) || value is null)
        {
            return;
        }

        if (value is not Guid feeId || feeId != entity.Id)
        {
            throw InvalidSdkResult();
        }
    }

    /// <summary>
    /// 從已正規化 request 讀取 UTC ticks 並建立 Kind=Utc 的 DateTime，
    /// 避免 worker machine local timezone 在 QueryExpression 建立時隱式改寫日期界線。
    /// </summary>
    /// <param name="request">已通過 shared request contract 的 execution request。</param>
    /// <param name="name">固定的 startDate 或 endDate parameter name。</param>
    /// <returns>Kind=Utc 且 ticks 完全相同的 DateTime。</returns>
    private static DateTime ReadUtcDateTime(WorkerRequestV1 request, string name)
    {
        var value = request.Parameters[name];
        var ticks = long.Parse(value.Scalar!, NumberStyles.Integer, CultureInfo.InvariantCulture);
        return new DateTime(ticks, DateTimeKind.Utc);
    }

    /// <summary>
    /// 讀取 nullable CRM DateTime。不存在時回傳 null；存在但型別錯誤時拒絕完整 operation，
    /// 不把原始 SDK value 或 exception detail 帶入 IPC。
    /// </summary>
    /// <param name="entity">目前投影的 fee Entity。</param>
    /// <param name="attributeName">固定 DateTime attribute logical name。</param>
    /// <returns>SDK DateTime 或 null；UTC 正規化由 <see cref="ToWorkerDateTime"/> 統一完成。</returns>
    private static DateTime? ReadOptionalDateTime(Entity entity, string attributeName)
    {
        if (!entity.Attributes.TryGetValue(attributeName, out var value) || value is null)
        {
            return null;
        }

        return value is DateTime dateTime
            ? dateTime
            : throw InvalidSdkResult();
    }

    /// <summary>
    /// 讀取 nullable SDK reference attribute；不存在時回傳 null，存在時必須精確符合允許的 SDK class。
    /// </summary>
    /// <typeparam name="T">固定為本投影允許的 Money 或 OptionSetValue 型別。</typeparam>
    /// <param name="entity">目前投影的 fee Entity。</param>
    /// <param name="attributeName">固定 reference attribute logical name。</param>
    /// <returns>符合型別的 SDK value 或 null；value 只在目前 row 投影期間存活。</returns>
    private static T? ReadOptionalReference<T>(Entity entity, string attributeName)
        where T : class
    {
        if (!entity.Attributes.TryGetValue(attributeName, out var value) || value is null)
        {
            return null;
        }

        return value as T ?? throw InvalidSdkResult();
    }

    /// <summary>讀取 nullable string；存在但不是 string 時拒絕整個結果。</summary>
    /// <param name="entity">目前投影的 fee Entity。</param>
    /// <param name="attributeName">固定 string attribute logical name。</param>
    /// <returns>原字串（包含空字串）或 null。</returns>
    private static string? ReadOptionalString(Entity entity, string attributeName)
    {
        if (!entity.Attributes.TryGetValue(attributeName, out var value) || value is null)
        {
            return null;
        }

        return value as string ?? throw InvalidSdkResult();
    }

    /// <summary>
    /// 只讀取 SDK FormattedValues 的固定顯示字串；缺失時回傳 null，且不回傳 raw OptionSet metadata。
    /// </summary>
    /// <param name="entity">目前投影的 fee Entity。</param>
    /// <param name="attributeName">new_pay_way 或 new_category logical name。</param>
    /// <returns>顯示文字或 null。</returns>
    private static string? ReadFormattedValue(Entity entity, string attributeName)
    {
        return entity.FormattedValues.TryGetValue(attributeName, out var value)
            ? value
            : null;
    }

    /// <summary>
    /// 將 nullable SDK DateTime 正規化為 canonical UTC WorkerValue。Utc 保持不變、Local 明確轉 UTC；
    /// Unspecified 依目前 Organization response 契約視為 UTC，避免套用 worker 主機時區。
    /// </summary>
    /// <param name="value">SDK DateTime 或 null。</param>
    /// <returns>缺失時為 Null，存在時為 canonical UTC ticks。</returns>
    private static WorkerValue ToWorkerDateTime(DateTime? value)
    {
        if (!value.HasValue)
        {
            return WorkerValue.Null();
        }

        var utc = value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        };
        return WorkerValue.FromUtcDateTime(new DateTimeOffset(utc));
    }

    /// <summary>將 nullable CLR string 投影成 SDK-free WorkerValue，不建立或保存額外 cache。</summary>
    /// <param name="value">SDK attribute 或 formatted value 的字串。</param>
    /// <returns>原字串或 Null。</returns>
    private static WorkerValue ToWorkerString(string? value) =>
        value is null ? WorkerValue.Null() : WorkerValue.FromString(value);

    /// <summary>
    /// 建立固定且已清理的 upstream shape/type failure；訊息不包含 Entity、attribute value、
    /// CRM endpoint、credential、原始 SDK exception 或其他可跨 boundary 洩漏的內容。
    /// </summary>
    /// <returns>使完整 operation fail closed 的 sanitized exception。</returns>
    private static InvalidOperationException InvalidSdkResult() =>
        new InvalidOperationException("The official CRM query response is invalid.");
}

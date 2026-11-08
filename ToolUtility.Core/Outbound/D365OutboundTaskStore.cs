/*
 * 檔案整體說明
 *
 * 目的與定位：P2-07 的雙目標資料庫 CAS 適配器；只借用固定已授權組織的 SDK，
 * 不建立連線、不以程序鎖代替 D365 RowVersion，實機驗證集中於 P2-12。
 * 主要功能與流程：最多五十筆到期／卡住候選，完整驗證唯一正式 ID 才發布唯讀快照；
 * 以交易內 Update IfRowVersionMatches＋Retrieve 原子取得認領新版本；完成亦守版本柵欄。
 * 使用邊界與維護：每個 store 只限同一已授權組織，教會值來自 SDK，不把格式當授權。
 * SDK 由呼叫端擁有 transport timeout，先排空同步 I/O 再 Dispose；本物件 Dispose
 * 只清借用參照並拒絕新操作。所有 Entity／交易／結果為操作局部，不保留帳密或跨批資料。
 * 失敗路徑：僅明確版本衝突回落敗；其他例外原樣交 Host 通知，不自行重試不確定結果。
 * 日期按 D365 UTC 欄位語意讀取，不套用本機時區；回寫只容許固定有界錯誤分類，不存例外本文。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.ServiceModel;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtilityNameSpace.Outbound;

/// <summary>單一已授權組織的有界 CAS 邊界；資料庫版本才是跨程序 owner 的唯一柵欄。</summary>
public sealed class D365OutboundTaskStore : IOutboundTaskStore, IDisposable
{
    /// <summary>不可變 schema 欄名，沒有任何教會、身分或業務資料。</summary>
    private static readonly string[] Columns =
    {
        "new_outboundtaskid", "versionnumber", "new_accountid", "new_contactid", "new_type", "new_payload",
        "new_idempotencykey", "new_taskstatus", "new_attempts", "new_nextattempton", "new_claimedon"
    };
    /// <summary>外借 SDK 的參照；入場時取局部快照，Dispose 不會提前釋放正在使用的外借資源。</summary>
    private IOrganizationService? service;

    /// <summary>借用已驗證組織的 SDK，不接管連線與 transport timeout，也不接受每次請求切換組織。</summary>
    /// <param name="service">呼叫端須先授權並在所有同步操作返回後才 Dispose 的 SDK。</param>
    public D365OutboundTaskStore(IOrganizationService service) =>
        this.service = service ?? throw new ArgumentNullException(nameof(service));

    /// <summary>精確查詢到期 Pending 或超過十分鐘的 Processing，全部驗證後才發布至多五十筆不可變值。</summary>
    /// <param name="utcNow">明確 UTC 時點；等於十分鐘尚不視為超過。</param>
    /// <param name="maximum">一至五十筆的資料庫 TopCount。</param>
    /// <param name="cancellationToken">入場及唯讀結果發布前檢查；不提前釋放未返回的 SDK。</param>
    /// <returns>脫離 SDK、身分唯一的唯讀結果；無效資料拒絕整批，不去重或部分發布。</returns>
    public IReadOnlyList<OutboundTaskRecord> ReadCandidates(DateTime utcNow, int maximum, CancellationToken cancellationToken)
    {
        RequireUtc(utcNow);
        if (maximum < 1 || maximum > 50) throw new ArgumentOutOfRangeException(nameof(maximum));
        var client = Borrow(cancellationToken);
        var pending = new FilterExpression(LogicalOperator.And);
        pending.AddCondition("new_taskstatus", ConditionOperator.Equal, (int)OutboundTaskStatus.Pending);
        var due = new FilterExpression(LogicalOperator.Or);
        due.AddCondition("new_nextattempton", ConditionOperator.Null);
        due.AddCondition("new_nextattempton", ConditionOperator.LessEqual, utcNow);
        pending.AddFilter(due);
        var processing = new FilterExpression(LogicalOperator.And);
        processing.AddCondition("new_taskstatus", ConditionOperator.Equal, (int)OutboundTaskStatus.Processing);
        processing.AddCondition("new_claimedon", ConditionOperator.LessThan, utcNow.AddMinutes(-10));
        var query = new QueryExpression("new_outboundtask")
        {
            TopCount = maximum,
            ColumnSet = new ColumnSet(Columns),
            Criteria = new FilterExpression(LogicalOperator.Or)
        };
        query.Criteria.AddFilter(pending);
        query.Criteria.AddFilter(processing);
        query.AddOrder("new_outboundtaskid", OrderType.Ascending);
        var rows = client.RetrieveMultiple(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (rows is null || rows.Entities.Count > maximum || rows.MoreRecords)
            throw new ArgumentException("Outbound candidate page violates its bounded contract.");
        var candidate = new List<OutboundTaskRecord>(rows.Entities.Count);
        var ids = new HashSet<Guid>();
        foreach (var row in rows.Entities)
        {
            var record = Map(row);
            if (!ids.Add(record.Id) || !IsEligible(record, utcNow))
                throw new ArgumentException("Outbound candidate identity or schedule is invalid.");
            candidate.Add(record);
        }
        return candidate.AsReadOnly();
    }

    /// <summary>
    /// 用候選版本在同一資料庫交易完成認領與新版本讀回，避免第三個 owner 插入兩步之間。
    /// 不合時機不寫入；只有明確 ConcurrencyVersionMismatch 回空，其他故障不轉為成功或重試。
    /// </summary>
    /// <param name="candidate">從固定已授權組織讀出的不可變候選。</param>
    /// <param name="utcNow">認領及卡住判斷的 UTC 時點。</param>
    /// <param name="cancellationToken">入場前檢查，不能中途強殺同步交易。</param>
    /// <returns>本 owner 已取得的新版本；沒有認領權回空值。</returns>
    public OutboundTaskRecord? TryClaim(OutboundTaskRecord candidate, DateTime utcNow, CancellationToken cancellationToken)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(candidate);
#else
        if (candidate is null) throw new ArgumentNullException(nameof(candidate));
#endif
        RequireUtc(utcNow);
        var client = Borrow(cancellationToken);
        if (!IsEligible(candidate, utcNow)) return null;
        var update = new Entity("new_outboundtask", candidate.Id)
        {
            RowVersion = candidate.RowVersion,
            ["new_taskstatus"] = new OptionSetValue((int)OutboundTaskStatus.Processing),
            ["new_claimedon"] = utcNow,
            ["new_nextattempton"] = null
        };
        var transaction = new ExecuteTransactionRequest
        {
            ReturnResponses = true,
            Requests = new OrganizationRequestCollection
            {
                new UpdateRequest { Target = update, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches },
                new RetrieveRequest { Target = new EntityReference("new_outboundtask", candidate.Id), ColumnSet = new ColumnSet(Columns) }
            }
        };
        try
        {
            if (client.Execute(transaction) is not ExecuteTransactionResponse response || response.Responses is null
                || response.Responses.Count != 2 || response.Responses[0] is not UpdateResponse
                || response.Responses[1] is not RetrieveResponse retrieved || retrieved.Entity is null)
                throw new ArgumentException("Outbound claim transaction response is incomplete.");
            var claim = Map(retrieved.Entity);
            if (claim.Id != candidate.Id || claim.ChurchId != candidate.ChurchId || claim.ContactId != candidate.ContactId
                || claim.Type != candidate.Type || !string.Equals(claim.PayloadJson, candidate.PayloadJson, StringComparison.Ordinal)
                || !string.Equals(claim.IdempotencyKey, candidate.IdempotencyKey, StringComparison.Ordinal)
                || claim.Attempts != candidate.Attempts || claim.Status != OutboundTaskStatus.Processing
                || claim.ClaimedOn != utcNow
                || ulong.Parse(claim.RowVersion, NumberStyles.None, CultureInfo.InvariantCulture)
                    <= ulong.Parse(candidate.RowVersion, NumberStyles.None, CultureInfo.InvariantCulture))
                throw new ArgumentException("Outbound claim identity or version is inconsistent.");
            return claim;
        }
        catch (FaultException<OrganizationServiceFault> exception)
            when (exception.Detail?.ErrorCode == unchecked((int)0x80060882))
        {
            return null;
        }
    }

    /// <summary>以本 owner 新版本回寫終止或重試，舊 owner 不能完成已被別人重認領的列。</summary>
    /// <param name="claim">Processing 快照，包含本次認領的新版本。</param>
    /// <param name="status">只可 Pending、Completed 或 Failed。</param>
    /// <param name="attempts">重排增加一、終止保留原數，最多五次。</param>
    /// <param name="utcNow">回寫 UTC 時點，不早於本次認領。</param>
    /// <param name="nextAttemptOn">Pending 必須有未來 UTC 時點，終止必須為空。</param>
    /// <param name="errorCode">僅固定有界 ASCII 分類，Completed 不保留錯誤。</param>
    /// <param name="cancellationToken">只在入場前取消；不隱藏已完成的寫入。</param>
    /// <returns>成功為 true；明確版本不符為 false，其餘例外原樣傳遞。</returns>
    public bool TrySettle(OutboundTaskRecord claim, OutboundTaskStatus status, int attempts,
        DateTime utcNow, DateTime? nextAttemptOn, string? errorCode, CancellationToken cancellationToken)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(claim);
#else
        if (claim is null) throw new ArgumentNullException(nameof(claim));
#endif
        RequireUtc(utcNow);
        if (claim.Status != OutboundTaskStatus.Processing || claim.ClaimedOn > utcNow
            || status is not (OutboundTaskStatus.Pending or OutboundTaskStatus.Completed or OutboundTaskStatus.Failed)
            || attempts < 0 || attempts > 5
            || (status == OutboundTaskStatus.Pending ? attempts != claim.Attempts + 1 : attempts != claim.Attempts))
            throw new ArgumentException("Outbound settlement state is invalid.");
        if (status == OutboundTaskStatus.Pending)
        {
            if (!nextAttemptOn.HasValue || nextAttemptOn.Value.Kind != DateTimeKind.Utc || nextAttemptOn <= utcNow)
                throw new ArgumentException("Outbound retry requires a future UTC time.");
        }
        else if (nextAttemptOn.HasValue) throw new ArgumentException("Outbound terminal state cannot be scheduled.");
        if (errorCode is not null)
        {
            if (errorCode.Length == 0 || errorCode.Length > 64 || status == OutboundTaskStatus.Completed)
                throw new ArgumentException("Outbound error classification is invalid.");
            foreach (var character in errorCode)
                if (!(character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-'))
                    throw new ArgumentException("Outbound error classification is invalid.");
        }
        var client = Borrow(cancellationToken);
        var update = new Entity("new_outboundtask", claim.Id)
        {
            RowVersion = claim.RowVersion,
            ["new_taskstatus"] = new OptionSetValue((int)status),
            ["new_attempts"] = attempts,
            ["new_claimedon"] = null,
            ["new_nextattempton"] = nextAttemptOn,
            ["new_completedon"] = status == OutboundTaskStatus.Pending ? null : utcNow,
            ["new_lasterror"] = errorCode
        };
        try
        {
            if (client.Execute(new UpdateRequest { Target = update, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches }) is not UpdateResponse)
                throw new ArgumentException("Outbound settlement response is incomplete.");
            return true;
        }
        catch (FaultException<OrganizationServiceFault> exception)
            when (exception.Detail?.ErrorCode == unchecked((int)0x80060882))
        {
            return false;
        }
    }

    /// <summary>解除借用參照、不 Dispose 外借 SDK；呼叫端必須先等待已入場操作返回。</summary>
    public void Dispose() => Interlocked.Exchange(ref service, null);

    /// <summary>單次入場取得局部借用；取消不能讓已在 SDK 內的操作提早歸還資源。</summary>
    /// <param name="cancellationToken">明確 caller token。</param>
    /// <returns>僅供本次同步操作的外借 SDK。</returns>
    private IOrganizationService Borrow(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Volatile.Read(ref service) ?? throw new ObjectDisposedException(nameof(D365OutboundTaskStore));
    }

    /// <summary>依自訂狀態判斷候選，等於十分鐘尚不可搶認領，避免時鐘邊界雙重執行。</summary>
    /// <param name="record">不可變候選。</param>
    /// <param name="utcNow">本次 UTC 時點。</param>
    /// <returns>已到期或超過十分鐘。</returns>
    private static bool IsEligible(OutboundTaskRecord record, DateTime utcNow) =>
        record.Status == OutboundTaskStatus.Pending && (!record.NextAttemptOn.HasValue || record.NextAttemptOn <= utcNow)
        || record.Status == OutboundTaskStatus.Processing && record.ClaimedOn < utcNow.AddMinutes(-10);

    /// <summary>只接受 SDK 正式身分與完整必要欄位；不讓缺值默認成有效 Pending／零次。</summary>
    /// <param name="row">操作局部的 SDK 記錄，不保留參照。</param>
    /// <returns>已驗證且脫離 SDK 的純值。</returns>
    private static OutboundTaskRecord Map(Entity row)
    {
        if (row is null || !string.Equals(row.LogicalName, "new_outboundtask", StringComparison.Ordinal)
            || !row.Attributes.TryGetValue("new_taskstatus", out var rawStatus) || rawStatus is not OptionSetValue
            || !row.Attributes.TryGetValue("new_type", out var rawType) || rawType is not OptionSetValue
            || !row.Attributes.TryGetValue("new_attempts", out var rawAttempts) || rawAttempts is not int)
            throw new ArgumentException("Outbound record metadata is incomplete.");
        var church = row.GetAttributeValue<EntityReference>("new_accountid");
        var contact = row.GetAttributeValue<EntityReference>("new_contactid");
        if (church is null || !string.Equals(church.LogicalName, "account", StringComparison.Ordinal)
            || contact is not null && !string.Equals(contact.LogicalName, "contact", StringComparison.Ordinal))
            throw new ArgumentException("Outbound record scope is invalid.");
        return new OutboundTaskRecord(row.Id, row.RowVersion, church.Id, contact?.Id,
            (OutboundTaskType)row.GetAttributeValue<OptionSetValue>("new_type").Value,
            row.GetAttributeValue<string>("new_payload"), row.GetAttributeValue<string>("new_idempotencykey"),
            (OutboundTaskStatus)row.GetAttributeValue<OptionSetValue>("new_taskstatus").Value,
            row.GetAttributeValue<int>("new_attempts"), ReadUtc(row, "new_nextattempton"), ReadUtc(row, "new_claimedon"));
    }

    /// <summary>SDK UTC 欄位可能沒有 Kind；僅在此已認證資料邊界明訂為 UTC，拒絕主機 Local 值。</summary>
    /// <param name="row">本次 SDK 結果。</param>
    /// <param name="attribute">程式擁有的固定日期欄名。</param>
    /// <returns>空值或 UTC 時點，不依賴機器時區轉換。</returns>
    private static DateTime? ReadUtc(Entity row, string attribute)
    {
        var value = row.GetAttributeValue<DateTime?>(attribute);
        if (!value.HasValue) return null;
        if (value.Value.Kind == DateTimeKind.Local) throw new ArgumentException("Outbound SDK timestamp is not UTC.");
        return DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
    }

    /// <summary>在任何 I/O 前拒絕非 UTC 或不足十分鐘回算範圍的時點，不讀取系統時鐘。</summary>
    /// <param name="value">呼叫端提供的明確 UTC 時點。</param>
    private static void RequireUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc || value.Ticks < TimeSpan.FromMinutes(10).Ticks)
            throw new ArgumentException("Outbound operation requires an explicit UTC timestamp.");
    }
}

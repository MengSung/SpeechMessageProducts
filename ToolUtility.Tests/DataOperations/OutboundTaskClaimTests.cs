/*
 * 檔案整體說明
 *
 * 目的與定位：以離線合成 SDK 驗證 P2-07 的資料庫 CAS；不是 D365 實機驗收。
 * 主要功能與流程：兩個以合成 SDK 驅動的實際 store 經 Barrier 同時入場後才進資料庫
 * 裁決，只有一個版本可認領；認領的新版本供後續完成，舊 owner 必須落敗。另注入重複身分、故障、
 * 缺版本、取消及類型 allowlist，檢查查詢條件、adapter enum 映射、欄位、上限、
 * 狀態與 UTC 卡住界線；整秒 SDK 讀回故障仍驗證認領一致性，不把成功 CAS 誤判失敗。
 * 使用邊界與維護：每案獨有 Moq、Barrier 與資料；await 全部競爭者後才 Dispose。
 * 替身不開連線或保存帳密，store 不擁有 SDK；原始 fault 身分及零額外 I/O 為斷言。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.Outbound;
using Xunit;

namespace ToolUtility.Tests.DataOperations;

/// <summary>真正適配器對假的資料庫版本裁決，預期欄位與錯誤碼不引用產品常數。</summary>
public sealed class OutboundTaskClaimTests
{
    /// <summary>固定 UTC 時點，測試不等待牆鐘且不依賴主機時區。</summary>
    private static readonly DateTime Now = new(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>兩個程序同時認領版本 10，僅一個取得版本 11，只有成功者可進入執行路徑。</summary>
    [Fact]
    public async Task Claim_TwoWorkersParallel_OnlyOneExecutes()
    {
        using var barrier = new Barrier(2);
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        var row = Row();
        var gate = new object();
        var version = 10;
        sdk.Setup(s => s.Execute(It.IsAny<OrganizationRequest>())).Returns((OrganizationRequest request) =>
        {
            var transaction = Assert.IsType<ExecuteTransactionRequest>(request);
            Assert.True(transaction.ReturnResponses);
            Assert.Equal(2, transaction.Requests.Count);
            var update = Assert.IsType<UpdateRequest>(transaction.Requests[0]);
            Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, update.ConcurrencyBehavior);
            Assert.Equal("10", update.Target.RowVersion);
            Assert.Equal(100000001, update.Target.GetAttributeValue<OptionSetValue>("new_taskstatus").Value);
            Assert.Equal(Now, update.Target["new_claimedon"]);
            Assert.Equal(row.Id, Assert.IsType<RetrieveRequest>(transaction.Requests[1]).Target.Id);
            Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(10)));
            lock (gate)
            {
                if (version != 10) throw Fault(unchecked((int)0x80060882));
                version++;
                row.RowVersion = "11";
                row["new_taskstatus"] = new OptionSetValue(100000001);
                row["new_claimedon"] = Now;
                return ClaimedResponse(row);
            }
        });
        using var first = new D365OutboundTaskStore(sdk.Object);
        using var second = new D365OutboundTaskStore(sdk.Object);
        var candidate = Snapshot(Row());
        // 同一正式 ID；不是各自使用不同列來假造競爭成功。
        candidate = new OutboundTaskRecord(row.Id, "10", candidate.ChurchId, null, candidate.Type,
            candidate.PayloadJson, candidate.IdempotencyKey, candidate.Status, 0, null, null);
        var claims = await Task.WhenAll(new[] { first, second }
            .Select(s => Task.Run(() => s.TryClaim(candidate, Now, CancellationToken.None))));
        var won = Assert.Single(claims.Where(c => c is not null));
        Assert.Equal("11", won!.RowVersion);
        Assert.Single(claims.Where(c => c is null));
        Assert.Equal(11, version);
    }

    /// <summary>
    /// 注入實機 D365 9.1 只保留整秒的認領讀回；輸入含次秒時仍須取得新版本，
    /// 並精確驗證寫入本身已量化，不以容忍任意時間差或跳過 CAS 掩蓋 Processing 殘留。
    /// SDK 為本案局部替身，store 借用後 Dispose；沒有連線、背景工作或真實資料。
    /// </summary>
    /// <param name="fractionalTicks">同一秒內的時間尾數，涵蓋毫秒與秒尾邊界。</param>
    [Theory]
    [InlineData(1)]
    [InlineData(1234567)]
    [InlineData(9999999)]
    public void Claim_SecondPrecisionReadback_KeepsSuccessfulOwnership(int fractionalTicks)
    {
        var row = Row(); var candidate = Snapshot(row);
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        sdk.Setup(s => s.Execute(It.IsAny<OrganizationRequest>())).Returns((OrganizationRequest request) =>
        {
            var transaction = Assert.IsType<ExecuteTransactionRequest>(request);
            var update = Assert.IsType<UpdateRequest>(transaction.Requests[0]);
            Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, update.ConcurrencyBehavior);
            Assert.Equal("10", update.Target.RowVersion);
            Assert.Equal(Now, update.Target["new_claimedon"]);
            row.RowVersion = "11";
            row["new_taskstatus"] = new OptionSetValue(100000001);
            row["new_claimedon"] = DateTime.SpecifyKind(Now, DateTimeKind.Unspecified);
            return ClaimedResponse(row);
        });
        using var store = new D365OutboundTaskStore(sdk.Object);
        var claim = store.TryClaim(candidate, Now.AddTicks(fractionalTicks), CancellationToken.None);
        Assert.NotNull(claim);
        Assert.Equal("11", claim!.RowVersion);
        Assert.Equal(Now, claim.ClaimedOn);
        Assert.Equal(DateTimeKind.Utc, claim.ClaimedOn!.Value.Kind);
        sdk.Verify(s => s.Execute(It.IsAny<OrganizationRequest>()), Times.Once);
    }

    /// <summary>卡住 Processing 超過十分鐘可重認領；十分鐘內與尚未到期 Pending 不發寫入。</summary>
    [Fact]
    public void StaleProcessing_Reclaimed()
    {
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        var stale = Row();
        stale["new_taskstatus"] = new OptionSetValue(100000001);
        stale["new_claimedon"] = Now.AddMinutes(-11);
        sdk.Setup(s => s.Execute(It.IsAny<OrganizationRequest>())).Returns((OrganizationRequest _) =>
        {
            stale.RowVersion = "11"; stale["new_claimedon"] = Now;
            return ClaimedResponse(stale);
        });
        using var store = new D365OutboundTaskStore(sdk.Object);
        Assert.NotNull(store.TryClaim(Snapshot(stale), Now, CancellationToken.None));
        stale["new_claimedon"] = Now.AddMinutes(-10);
        Assert.Null(store.TryClaim(Snapshot(stale), Now, CancellationToken.None));
        var future = Row(); future["new_nextattempton"] = Now.AddSeconds(1);
        Assert.Null(store.TryClaim(Snapshot(future), Now, CancellationToken.None));
        sdk.Verify(s => s.Execute(It.IsAny<OrganizationRequest>()), Times.Once);
    }

    /// <summary>完成同樣受新認領版本保護；舊 owner 的 CAS 衝突回 false，不發無條件 Update。</summary>
    [Fact]
    public void Settle_StaleOwnerCannotOverwriteNewClaim()
    {
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        var row = Row(); row["new_taskstatus"] = new OptionSetValue(100000001); row["new_claimedon"] = Now;
        sdk.Setup(s => s.Execute(It.IsAny<OrganizationRequest>())).Returns((OrganizationRequest request) =>
        {
            var update = Assert.IsType<UpdateRequest>(request);
            Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, update.ConcurrencyBehavior);
            Assert.Equal("10", update.Target.RowVersion);
            Assert.Equal(100000002, update.Target.GetAttributeValue<OptionSetValue>("new_taskstatus").Value);
            Assert.Equal(Now, update.Target["new_completedon"]);
            Assert.Null(update.Target["new_lasterror"]);
            throw Fault(unchecked((int)0x80060882));
        });
        using var store = new D365OutboundTaskStore(sdk.Object);
        Assert.False(store.TrySettle(Snapshot(row), OutboundTaskStatus.Completed, 0, Now, null, null, CancellationToken.None));
        sdk.Verify(s => s.Update(It.IsAny<Entity>()), Times.Never);
    }

    /// <summary>候選完整驗證後才發布；缺版本或同批重複正式主鍵都拒絕，不能靜默去重。</summary>
    /// <param name="corruption">注入版本缺失或同一主鍵重複。</param>
    [Theory]
    [InlineData("duplicate")]
    [InlineData("version")]
    public void Candidates_CorruptPageRejected(string corruption)
    {
        var row = Row();
        if (corruption == "version") row.RowVersion = null;
        var page = new EntityCollection(new List<Entity> { row });
        if (corruption == "duplicate") page.Entities.Add(row);
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(page);
        using var store = new D365OutboundTaskStore(sdk.Object);
        Assert.ThrowsAny<ArgumentException>(() => store.ReadCandidates(Now, 50, CancellationToken.None));
    }

    /// <summary>候選只查到期 Pending 或逾十分鐘 Processing，固定上限與主鍵排序，避免掃全表。</summary>
    [Fact]
    public void Candidates_QueryIsBoundedAndUsesCustomStatus()
    {
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase raw) =>
        {
            var q = Assert.IsType<QueryExpression>(raw);
            Assert.Equal("new_outboundtask", q.EntityName); Assert.Equal(50, q.TopCount);
            Assert.False(q.ColumnSet.AllColumns);
            Assert.Contains("versionnumber", q.ColumnSet.Columns);
            Assert.Equal("new_outboundtaskid", Assert.Single(q.Orders).AttributeName);
            Assert.Equal(LogicalOperator.Or, q.Criteria.FilterOperator);
            var pending = q.Criteria.Filters[0]; var processing = q.Criteria.Filters[1];
            Assert.Equal(100000000, Assert.Single(pending.Conditions).Values[0]);
            Assert.Equal("new_taskstatus", pending.Conditions[0].AttributeName);
            Assert.Equal(LogicalOperator.Or, Assert.Single(pending.Filters).FilterOperator);
            Assert.Equal(100000001, processing.Conditions[0].Values[0]);
            Assert.Equal(ConditionOperator.LessThan, processing.Conditions[1].Operator);
            Assert.Equal(Now.AddMinutes(-10), processing.Conditions[1].Values[0]);
            return new EntityCollection(new List<Entity> { Row() });
        });
        using var store = new D365OutboundTaskStore(sdk.Object);
        Assert.Single(store.ReadCandidates(Now, 50, CancellationToken.None));
    }

    /// <summary>
    /// 驗證 Core 與 net10 adapter 的 AllotQuery allowlist 都由 D365 查詢條件限制，
    /// 而非讀回後才過濾；另以無 allowlist 的預設建構式確認原本的 OR 狀態條件、
    /// 分組及查詢結構完全保留。所有列與 IOrganizationService 都是本測試建立的
    /// 合成替身，不連線或持有外部資源，store／adapter 在各自區塊結束時解除借用。
    /// </summary>
    [Fact]
    public void Store_AllowedTypes_QueriesOnlyThoseTypes()
    {
        var queries = new List<QueryExpression>();
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase raw) =>
        {
            queries.Add(Assert.IsType<QueryExpression>(raw));
            return new EntityCollection();
        });

        using (var unfilteredStore = new D365OutboundTaskStore(sdk.Object))
            Assert.Empty(unfilteredStore.ReadCandidates(Now, 50, CancellationToken.None));

        using (var filteredStore = new D365OutboundTaskStore(
                   sdk.Object, new[] { OutboundTaskType.AllotQuery }))
            Assert.Empty(filteredStore.ReadCandidates(Now, 50, CancellationToken.None));

        var unfilteredQuery = queries[0];
        Assert.Equal(LogicalOperator.Or, unfilteredQuery.Criteria.FilterOperator);
        Assert.Empty(unfilteredQuery.Criteria.Conditions);
        Assert.Equal(2, unfilteredQuery.Criteria.Filters.Count);
        Assert.Equal("new_outboundtask", unfilteredQuery.EntityName);
        Assert.Equal(50, unfilteredQuery.TopCount);

        var filteredQuery = queries[1];
        Assert.Equal(LogicalOperator.And, filteredQuery.Criteria.FilterOperator);
        var typeCondition = Assert.Single(filteredQuery.Criteria.Conditions);
        Assert.Equal("new_type", typeCondition.AttributeName);
        Assert.Equal(ConditionOperator.In, typeCondition.Operator);
        Assert.Equal(new object[] { (int)OutboundTaskType.AllotQuery }, typeCondition.Values.ToArray());
        var statusFilter = Assert.Single(filteredQuery.Criteria.Filters);
        Assert.Equal(LogicalOperator.Or, statusFilter.FilterOperator);
        Assert.Equal(2, statusFilter.Filters.Count);
        sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Exactly(2));

#if NET10_0_OR_GREATER
        using (var adapter = new D365OutboundWorkStoreAdapter(
                   sdk.Object, new[] { SpeechMessage.Hosting.Outbound.OutboundWorkType.AllotQuery }))
            Assert.Empty(adapter.ReadCandidates(Now, 50, CancellationToken.None));

        var adapterQuery = queries[2];
        Assert.Equal(LogicalOperator.And, adapterQuery.Criteria.FilterOperator);
        var adapterTypeCondition = Assert.Single(adapterQuery.Criteria.Conditions);
        Assert.Equal("new_type", adapterTypeCondition.AttributeName);
        Assert.Equal(ConditionOperator.In, adapterTypeCondition.Operator);
        Assert.Equal(new object[] { (int)OutboundTaskType.AllotQuery }, adapterTypeCondition.Values.ToArray());
        Assert.Single(adapterQuery.Criteria.Filters);

        using (var unfilteredAdapter = new D365OutboundWorkStoreAdapter(sdk.Object))
            Assert.Empty(unfilteredAdapter.ReadCandidates(Now, 50, CancellationToken.None));

        var unfilteredAdapterQuery = queries[3];
        Assert.Equal(LogicalOperator.Or, unfilteredAdapterQuery.Criteria.FilterOperator);
        Assert.Empty(unfilteredAdapterQuery.Criteria.Conditions);
        Assert.Equal(2, unfilteredAdapterQuery.Criteria.Filters.Count);
        sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Exactly(4));
#endif
    }

    /// <summary>非版本故障保持同一例外且不重試；取消與 Dispose 都在入場前拒絕新 I/O。</summary>
    [Fact]
    public void Claim_OtherFaultCancellationAndDisposeRemainDistinct()
    {
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        var failure = Fault(unchecked((int)0x80040216));
        sdk.Setup(s => s.Execute(It.IsAny<OrganizationRequest>())).Throws(failure);
        using var store = new D365OutboundTaskStore(sdk.Object);
        Assert.Same(failure, Assert.Throws<FaultException<OrganizationServiceFault>>(
            () => store.TryClaim(Snapshot(Row()), Now, CancellationToken.None)));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => store.ReadCandidates(Now, 50, cts.Token));
        store.Dispose();
        Assert.Throws<ObjectDisposedException>(() => store.ReadCandidates(Now, 50, CancellationToken.None));
        sdk.Verify(s => s.Execute(It.IsAny<OrganizationRequest>()), Times.Once);
    }

    /// <summary>交易成功也必須驗證讀回的新版本與完整 scope；注入版本倒退、本文與正式主鍵竄改都不能取得執行權。</summary>
    /// <param name="corruption">只改一個回應欄位的故障種類。</param>
    [Theory]
    [InlineData("id")]
    [InlineData("payload")]
    [InlineData("same-version")]
    [InlineData("earlier-version")]
    [InlineData("attempts")]
    [InlineData("status")]
    [InlineData("time")]
    public void Claim_InconsistentReadbackRejected(string corruption)
    {
        var row = Row(); var candidate = Snapshot(row);
        row.RowVersion = "11"; row["new_taskstatus"] = new OptionSetValue(100000001); row["new_claimedon"] = Now;
        switch (corruption)
        {
            case "id": row.Id = Guid.NewGuid(); break;
            case "payload": row["new_payload"] = "{\"quantity\":3}"; break;
            case "same-version": row.RowVersion = "10"; break;
            case "earlier-version": row.RowVersion = "9"; break;
            case "attempts": row["new_attempts"] = 1; break;
            case "status": row["new_taskstatus"] = new OptionSetValue(100000000); break;
            case "time": row["new_claimedon"] = Now.AddSeconds(1); break;
        }
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        sdk.Setup(s => s.Execute(It.IsAny<OrganizationRequest>())).Returns(ClaimedResponse(row));
        using var store = new D365OutboundTaskStore(sdk.Object);
        Assert.Throws<ArgumentException>(() => store.TryClaim(candidate, Now, CancellationToken.None));
    }

    /// <summary>兩個不同正式 ID 的相同 payload 記錄不能被內容去重；返回後 SDK 與 caller 的集合變動不能污染已發布快照。</summary>
    [Fact]
    public void Candidates_SameContentDistinctIdsRemainDetached()
    {
        var first = Row(); var second = Row();
        var page = new EntityCollection(new List<Entity> { first, second });
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(page);
        using var store = new D365OutboundTaskStore(sdk.Object);
        var values = store.ReadCandidates(Now, 50, CancellationToken.None);
        Assert.Equal(2, values.Count); Assert.NotEqual(values[0].Id, values[1].Id);
        first["new_payload"] = "changed"; first.RowVersion = "99"; page.Entities.Clear();
        Assert.Equal("{\"quantity\":2}", values[0].PayloadJson); Assert.Equal("10", values[0].RowVersion);
        Assert.Throws<NotSupportedException>(() => ((IList<OutboundTaskRecord>)values).Clear());
    }

    /// <summary>注入 SDK 明確 null 計數；不能因 GetAttributeValue 的預設零讓不完整資料通過發布。</summary>
    [Fact]
    public void Candidates_NullAttemptCounterRejected()
    {
        var row = Row(); row["new_attempts"] = null;
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(new EntityCollection(new List<Entity> { row }));
        using var store = new D365OutboundTaskStore(sdk.Object);
        Assert.Throws<ArgumentException>(() => store.ReadCandidates(Now, 50, CancellationToken.None));
    }

    /// <summary>成功完成／失敗／重排均以認領版本與明確欄位寫回，終止清到期時間、重排不寫完成時點。</summary>
    /// <param name="status">三種合法目的狀態的 schema 字面值。</param>
    [Theory]
    [InlineData(100000000)]
    [InlineData(100000002)]
    [InlineData(100000003)]
    public void Settle_KnownSuccessUsesClaimVersion(int status)
    {
        var row = Row(); row.RowVersion = "11"; row["new_taskstatus"] = new OptionSetValue(100000001); row["new_claimedon"] = Now;
        var retry = status == 100000000;
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        sdk.Setup(s => s.Execute(It.IsAny<OrganizationRequest>())).Returns((OrganizationRequest request) =>
        {
            var update = Assert.IsType<UpdateRequest>(request);
            Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, update.ConcurrencyBehavior);
            Assert.Equal("11", update.Target.RowVersion); Assert.Equal(row.Id, update.Target.Id);
            Assert.Equal(status, update.Target.GetAttributeValue<OptionSetValue>("new_taskstatus").Value);
            Assert.Null(update.Target["new_claimedon"]);
            Assert.Equal(retry ? 1 : 0, update.Target["new_attempts"]);
            Assert.Equal(retry ? Now.AddMinutes(1) : null, update.Target["new_nextattempton"]);
            Assert.Equal(retry ? null : Now, update.Target["new_completedon"]);
            return new UpdateResponse();
        });
        using var store = new D365OutboundTaskStore(sdk.Object);
        Assert.True(store.TrySettle(Snapshot(row), (OutboundTaskStatus)status, retry ? 1 : 0, Now,
            retry ? Now.AddMinutes(1) : null, null, CancellationToken.None));
        sdk.Verify(s => s.Execute(It.IsAny<OrganizationRequest>()), Times.Once);
    }

    /// <summary>Dispose 只拒絕新借用，已入場 SDK 必須等它返回；案例 finally 釋放屏障並 await，不遺留測試 Task 或事件。</summary>
    [Fact]
    public async Task Dispose_InflightReadDrainsWithoutDisposingBorrowedSdk()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase _) =>
        {
            entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            return new EntityCollection(new List<Entity> { Row() });
        });
        using var store = new D365OutboundTaskStore(sdk.Object);
        var work = Task.Run(() => store.ReadCandidates(Now, 50, CancellationToken.None));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10))); store.Dispose();
            Assert.False(work.IsCompleted);
            Assert.Throws<ObjectDisposedException>(() => store.ReadCandidates(Now, 50, CancellationToken.None));
        }
        finally { release.Set(); await work; }
        Assert.Single(await work);
        sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
    }

    /// <summary>合成同一組織的有界正式列；固定教會與來源鍵供獨立判讀，不含真實資料。</summary>
    private static Entity Row()
    {
        var church = Guid.Parse("11111111-1111-1111-1111-111111111111");
        return new Entity("new_outboundtask", Guid.NewGuid())
        {
            RowVersion = "10",
            ["new_accountid"] = new EntityReference("account", church),
            ["new_type"] = new OptionSetValue(100000001),
            ["new_payload"] = "{\"quantity\":2}",
            ["new_idempotencykey"] = "11111111111111111111111111111111-100000001-22222222222222222222222222222222",
            ["new_taskstatus"] = new OptionSetValue(100000000),
            ["new_attempts"] = 0
        };
    }

    /// <summary>以獨立字面 schema 建快照；不藉呼叫產品 mapper 掩蓋錯誤欄位。</summary>
    private static OutboundTaskRecord Snapshot(Entity e) => new(e.Id, e.RowVersion,
        e.GetAttributeValue<EntityReference>("new_accountid").Id, null, OutboundTaskType.QrImageGenerate,
        e.GetAttributeValue<string>("new_payload"), e.GetAttributeValue<string>("new_idempotencykey"),
        (OutboundTaskStatus)e.GetAttributeValue<OptionSetValue>("new_taskstatus").Value, 0,
        e.GetAttributeValue<DateTime?>("new_nextattempton"), e.GetAttributeValue<DateTime?>("new_claimedon"));

    /// <summary>模擬同交易 Update 與 Retrieve 的回應；讀回版本與寫入間不得容許第三者插入。</summary>
    private static ExecuteTransactionResponse ClaimedResponse(Entity row) => new()
    {
        Results = new ParameterCollection
        {
            ["Responses"] = new OrganizationResponseCollection
            {
                new UpdateResponse(),
                new RetrieveResponse { Results = new ParameterCollection { ["Entity"] = row } }
            }
        }
    };

    /// <summary>注入固定官方 SDK fault 碼；不能把其他 SQL／認證錯誤吞成競爭失敗。</summary>
    private static FaultException<OrganizationServiceFault> Fault(int code) =>
        new(new OrganizationServiceFault { ErrorCode = code }, new FaultReason("synthetic"));
}
